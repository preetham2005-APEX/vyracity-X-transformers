#ifndef VC_WATER_SURFACE_INCLUDED
#define VC_WATER_SURFACE_INCLUDED

// Veyra/Water surface (round 2; owner: vehicle/visual session, agreed with the F/L-card session). Reuses the shared
// UnityPerMaterial layout of VC_Input.hlsl (no new fields): _BaseColor = deep water colour, _GlassColor = colour at grazing
// angles (the teal of light scattered under the surface), _Smoothness, _BumpScale = ripple strength, _BaseMap_ST.xy =
// ripples per metre, _MacroScale1 / _MacroScale2 = swell wavelengths (m), _ScrollSpeed = current speed (m/s),
// _EmissionColor.rgb = sun glitter tint (intensity in .a). Lighting, Forward+ lights (neon and lamps mirror in it),
// reflections, shadows and fog come from VC_ForwardPass (UniversalFragmentPBR).

// _BumpMap / sampler_BumpMap come from URP's SurfaceInput.hlsl (included by VC_Input.hlsl).

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
    float2 slope = (d1 * c1 * 0.06 + d2 * c2 * 0.04) * wind;
    half3 rip = VC_WaterRipples(xz, t);
    // ripples in tangent space of a horizontal surface: x = world x, y = world z
    half3 n = normalize(half3(rip.x - slope.x, rip.z, rip.y - slope.y));
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
    s.occlusion = 1.0;
    // sun glitter: tiny HDR sparkles where a ripple facet mirrors the sun (bloom turns them into glints)
    Light sun = GetMainLight();
    half3 R = reflect(-V, n);
    half g = pow(saturate(dot(R, sun.direction)), 900.0) * 40.0 + pow(saturate(dot(R, sun.direction)), 90.0) * 0.6;
    s.emission = sun.color * _EmissionColor.rgb * _EmissionColor.a * g * (1.0 - night * 0.7) * sun.distanceAttenuation;
}

#endif
