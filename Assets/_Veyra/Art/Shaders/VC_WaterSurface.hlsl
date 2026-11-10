#ifndef VC_WATER_SURFACE_INCLUDED
#define VC_WATER_SURFACE_INCLUDED

// Veyra/Water surface (round 2; owner: vehicle/visual session, agreed with the F/L-card session). Reuses the shared
// UnityPerMaterial layout of VC_Input.hlsl (no new fields): _BaseColor = deep water colour, _GlassColor = colour at grazing
// angles (the teal of light scattered under the surface), _Smoothness, _BumpScale = ripple strength, _BaseMap_ST.xy =
// ripples per metre, _MacroScale1 / _MacroScale2 = swell wavelengths (m), _ScrollSpeed = current speed (m/s),
// _EmissionColor.rgb = sun glitter tint (intensity in .a). Lighting, Forward+ lights (neon and lamps mirror in it),
// reflections, shadows and fog come from VC_ForwardPass (UniversalFragmentPBR).
// Iteration 20: _MacroVariation (a road field, unused here) scales the swell per renderer: 0 (the material) = the sea's
// full swell, WaterSurface.cs sets 0.25 on the river and reservoir through a property block (calm inland water).

// _BumpMap / sampler_BumpMap come from URP's SurfaceInput.hlsl (included by VC_Input.hlsl).
// Iteration 22 (GTA V / Forza shorelines): water depth from the baked terrain height (GroundHeightField, global
// _VC_GroundHeight over _VC_GroundHeightRect): shallow water turns a lighter, greener teal over the bed, and a band of
// broken, drifting foam laps the banks and the beach where the depth goes under ~1.2 m. Off while the field is unset.

TEXTURE2D(_VC_GroundHeight); SAMPLER(sampler_VC_GroundHeight);
float4 _VC_GroundHeightRect;

half3 VC_WaterRipples(float2 xz, float t)
{
    float2 s = _BaseMap_ST.xy;
    float v = max(_ScrollSpeed, 0.01);
    // two ripple layers crossing at an angle, a coarser third for chop
    half3 a = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, xz * s + float2(0.031, 0.017) * t * v), 1.0);
    half3 b = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, float2(-xz.y, xz.x) * s * 0.77 + float2(-0.023, 0.029) * t * v), 1.0);
    half3 c = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, xz * s * 0.21 + float2(0.009, -0.006) * t * v), 1.0);
    half3 n = half3(a.xy + b.xy + c.xy * 1.6, a.z * b.z * c.z);
    n.xy *= _BumpScale;
    return normalize(n);
}

void VC_Surface(VC_FragInput f, inout SurfaceData s, inout half3 normalWS)
{
    float t = _Time.y;
    float2 xz = f.positionWS.xz;
    // swell: two long wave trains (analytic slopes), stronger with the wind
    float wind = 0.6 + 0.8 * _VC_WindStrength;
    float2 d1 = normalize(float2(0.8, 0.6)), d2 = normalize(float2(-0.45, 0.9));
    float k1 = 6.2831853 / max(_MacroScale1, 1.0), k2 = 6.2831853 / max(_MacroScale2, 1.0);
    float c1 = cos(dot(d1, xz) * k1 - t * sqrt(9.81 * k1)), c2 = cos(dot(d2, xz) * k2 - t * sqrt(9.81 * k2));
    float swellK = _MacroVariation > 0.001 ? _MacroVariation : 1.0;
    float2 slope = (d1 * c1 * 0.06 + d2 * c2 * 0.04) * wind * swellK;
    half3 rip = VC_WaterRipples(xz, t);
    // ripples in tangent space of a horizontal surface: x = world x, y = world z
    half3 n = normalize(half3(rip.x - slope.x, rip.z, rip.y - slope.y));
    // iteration 30: ripple normals alias into specular glitter with distance (at night the moon's highlight lit the whole
    // river blue-white); they flatten from ~25 m out to ~25 % at 180 m, so far water mirrors the sky and the city glow
    float dCam = distance(_WorldSpaceCameraPos.xyz, f.positionWS);
    half farFlat = saturate((dCam - 25.0) / 155.0);
    n = normalize(lerp(n, normalize(half3(n.x * 0.25, n.y, n.z * 0.25)), farFlat));
    normalWS = n;

    half3 V = f.viewDirWS;
    half grazing = pow(1.0 - saturate(dot(n, V)), 3.0);
    half night = saturate(_VC_NightFactor);
    // deep colour, lifted toward the scattering tint at grazing angles and on wave crests facing the viewer
    half3 deep = _BaseColor.rgb * lerp(1.0, 0.35, night);
    half3 scatter = _GlassColor.rgb * lerp(1.0, 0.25, night);
    half crest = saturate((c1 * 0.6 + c2 * 0.4) * 0.5 + 0.5);
    s.albedo = lerp(deep, scatter, saturate(grazing * 0.6 + crest * 0.15));
    s.metallic = 0.0;
    s.smoothness = _Smoothness;
    if (_VC_GroundHeightRect.z > 0.0)
    {
        float bed = SAMPLE_TEXTURE2D_LOD(_VC_GroundHeight, sampler_VC_GroundHeight, (xz - _VC_GroundHeightRect.xy) * _VC_GroundHeightRect.zw, 0).r;
        float depth = f.positionWS.y - bed;
        half shallow = saturate(1.0 - depth / 5.0);
        half3 shallowCol = half3(0.16, 0.36, 0.34) * lerp(1.0, 0.3, night);
        s.albedo = lerp(s.albedo, shallowCol, shallow * shallow * 0.75);
        // foam: two drifting noise layers, pulsing up the shore
        float fn = VC_ValueNoise(xz * 0.32 + float2(t * 0.11, t * 0.07)) * 0.6 + VC_ValueNoise(xz * 1.4 - float2(t * 0.23, -t * 0.17)) * 0.4;
        half band = saturate(1.0 - depth / 1.2) * (0.75 + 0.25 * sin(t * 1.3 - depth * 5.0));
        half foam = smoothstep(0.45, 0.7, fn * band + band * 0.35) * step(-0.5, depth);
        s.albedo = lerp(s.albedo, half3(0.82, 0.86, 0.86) * lerp(1.0, 0.25, night), foam * 0.85);
        s.smoothness = lerp(s.smoothness, 0.55, foam);
        normalWS = normalize(lerp(normalWS, half3(0, 1, 0), foam * 0.6));
    }
    // iteration 32: environment reflection at night: the scene's default reflection is the editor-baked daytime sky, which URP's
    // Forward+ path keeps using outside the probe boxes (a runtime sky probe and a custom default reflection both had no
    // effect in iterations 25-31); occlusion scales only indirect light, so lamps and the moon still reflect
    s.occlusion = lerp(1.0, 0.12, night);
    // sun glitter: tiny HDR sparkles where a ripple facet mirrors the sun (bloom turns them into glints)
    Light sun = GetMainLight();
    half3 R = reflect(-V, n);
    half g = pow(saturate(dot(R, sun.direction)), 900.0) * 40.0 + pow(saturate(dot(R, sun.direction)), 90.0) * 0.6;
    s.emission = sun.color * _EmissionColor.rgb * _EmissionColor.a * g * (1.0 - night * 0.7) * sun.distanceAttenuation;
    // iteration 29 (GTA V night harbour): the reflection probe holds only the night sky, so at night the water also mirrors
    // the city's glow low on the horizon (warm sodium haze) and broken, rippling streaks of light from the waterfront
    half horizon = exp(-max(R.y, 0.0) * 7.0) * saturate(R.y * 20.0 + 1.0);
    half fresnelN = 0.04 + 0.96 * pow(1.0 - saturate(dot(n, V)), 5.0);
    float streak = VC_ValueNoise(float2(dot(xz, float2(0.7, 0.7)) * 0.08, t * 0.05) * 6.0) * VC_ValueNoise(xz * 0.9 + t * 0.4);
    half3 cityGlow = half3(1.0, 0.55, 0.28) * (0.05 + 0.5 * smoothstep(0.55, 0.9, streak));
    s.emission += cityGlow * horizon * fresnelN * night * 1.6;
}

#endif
