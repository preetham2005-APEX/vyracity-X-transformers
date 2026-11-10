#ifndef VC_NEON_SURFACE_INCLUDED
#define VC_NEON_SURFACE_INCLUDED

// Veyra/Neon surface (card F2c): HDR emission _NeonColor x _NeonIntensity (2.5–8 so the night bloom threshold 0.9 catches
// it), x _DayFactor by day (unlit tubes read at ~10 %), full at night via _VC_NightFactor.
//  - Tube falloff: bright core where the tube faces the camera, softer at the edges.
//  - Flicker on ~_FlickerChance of the signs (chosen per ~6 m world cell, since signs are merged per chunk).
//  - Marquee chase (_ChaseCount bulbs per UV unit along u) and LED screens (_EmissionMap scrolled by _ScrollSpeed).

void VC_Surface(VC_FragInput f, inout SurfaceData s, inout half3 normalWS)
{
    float2 uv = f.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
    half night = lerp(_DayFactor, 1.0, saturate(_VC_NightFactor));

    half facing = saturate(dot(f.normalWS, f.viewDirWS));
    half core = lerp(0.55, 1.0, pow(facing, max(_TubeFalloff, 0.01)));

    float signSeed = VC_Hash12(floor(f.positionWS.xz / 6.0) + floor(f.positionWS.y / 6.0) * 7.7);
    half flicker = 1.0;
    if (signSeed < _FlickerChance)
    {
        float n = VC_ValueNoise(float2(_Time.y * 11.0, signSeed * 173.0));
        float buzz = VC_ValueNoise(float2(_Time.y * 53.0, signSeed * 91.0));
        flicker = (n > 0.28 ? 1.0 : 0.12) * lerp(0.85, 1.0, buzz);
    }

    half chase = 1.0;
    if (_ChaseCount > 0.0)
    {
        float x = frac(uv.x * _ChaseCount - _Time.y * _ChaseSpeed);
        half bulb = smoothstep(0.42, 0.18, abs(frac(uv.x * _ChaseCount * 4.0) - 0.5));
        chase = lerp(0.25, 1.0, step(0.5, x)) * lerp(0.35, 1.0, bulb);
    }

    float2 screenUV = uv + float2(_Time.y * _ScrollSpeed, 0.0);
    half3 pattern = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, screenUV).rgb;

    half3 emission = _NeonColor.rgb * _NeonIntensity * night * core * flicker * chase * pattern;
    half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
    s.albedo = saturate(lerp(baseTex.rgb, _NeonColor.rgb * 0.5, 0.35));
    s.metallic = 0.0;
    s.smoothness = _Smoothness;
    s.occlusion = 1.0;
    s.normalTS = half3(0, 0, 1);
    s.emission = emission;
    s.alpha = 1.0;
    normalWS = f.normalWS;
}

#endif
