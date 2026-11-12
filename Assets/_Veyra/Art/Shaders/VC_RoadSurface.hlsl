#ifndef VC_ROAD_SURFACE_INCLUDED
#define VC_ROAD_SURFACE_INCLUDED

// Veyra/Road surface (card F2a): asphalt, concrete, kerbs, paving, cobbles, gravel, dirt, markings.
//  - World-space macro variation (two noise scales, ± _MacroVariation albedo) against tiling.
//  - Optional patch/crack overlay (_DetailMap) masked by world noise, and tyre-wear lanes from UV1.x (0–1 across the
//    carriageway) when the road mesh carries it. Iteration 29: the overlay darkens relative to the asphalt (a = coverage,
//    r = tone: seams and cracks near 0, patch fill ~0.15) so it reads on any base albedo; it was an absolute albedo, which
//    on the city's very dark asphalt was invisible.
//  - Wetness = global _VC_Wetness x _WetnessResponse (+ _WetnessOverride for testing): albedo x 0.55, smoothness -> 0.90,
//    normal strength x 0.3; a low-frequency puddle mask (grows with wetness) -> smoothness 0.98, flat normal; animated rain
//    ripples while _VC_RainIntensity > 0.
//  - Round 2 (visual iteration 24, GTA V streets): UV1 now comes from Gameplay/RoadWear at runtime (x across the
//    carriageway, y = lane count, so the wheel paths follow the real lanes); with it, dirt and darker, rougher asphalt
//    in the gutters along both kerbs (where water also pools first in the rain) and an oil-drip line down each lane.

void VC_Surface(VC_FragInput f, inout SurfaceData s, inout half3 normalWS)
{
    float2 uv = f.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
    half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
    half4 mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, uv);
    half3 nTS = SampleNormal(uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);

    float2 w = f.positionWS.xz;
    float m1 = VC_Fbm(w / max(_MacroScale1, 0.1), 3);
    float m2 = VC_ValueNoise(w / max(_MacroScale2, 0.1));
    half macro = 1.0 + _MacroVariation * ((m1 - 0.5) * 1.2 + (m2 - 0.5) * 0.8);
    albedo.rgb *= macro;

    half smooth = mask.a * _Smoothness;
    half metal = mask.r * _Metallic;
    half occlusion = lerp(1.0, mask.g, _OcclusionStrength);

    if (_PatchAmount > 0.001)
    {
        float pn = VC_Fbm(w / max(_PatchScale, 0.5), 3);
        half pm = saturate((pn - (1.0 - _PatchAmount * 0.6)) * 10.0);
        half4 det = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, f.uv * _DetailMap_ST.xy + _DetailMap_ST.zw);
        half k = pm * det.a;
        albedo.rgb = lerp(albedo.rgb, albedo.rgb * lerp(0.22, 0.62, saturate(det.r * 4.0)), k);
        smooth = lerp(smooth, smooth * 0.8, k);
    }
    half gutter = 0.0;
    if (_TyreWear > 0.001)
    {
        // Two wheel paths per lane, at 0.3 and 0.7 of a lane; UV1.x spans the carriageway 0–1, UV1.y = lanes (iteration 24).
        float lanes = max(1.0, round(f.uv1.y));
        float x = frac(f.uv1.x * lanes);
        half hasUv1 = step(0.0001, f.uv1.x + f.uv1.y);
        // iteration 25: stronger and broken up along the road (12 % read as nothing on dark asphalt)
        half paths = exp(-pow((x - 0.3) / 0.085, 2.0)) + exp(-pow((x - 0.7) / 0.085, 2.0));
        half wear = saturate(paths) * _TyreWear * hasUv1 * (0.7 + 0.3 * VC_ValueNoise(w * 0.35));
        albedo.rgb *= 1.0 - 0.38 * wear;   // rubbered wheel paths
        smooth = lerp(smooth, saturate(smooth + 0.18), wear);
        // oil drips down the lane centre, broken up along the road
        half oil = exp(-pow((x - 0.5) / 0.045, 2.0)) * smoothstep(0.35, 0.7, VC_ValueNoise(w * float2(0.9, 0.9) + 17.0)) * _TyreWear * hasUv1;
        albedo.rgb *= 1.0 - 0.3 * oil;
        smooth = lerp(smooth, saturate(smooth + 0.1), oil);
        // gutters: grit and old water stains along both kerbs
        float edge = min(f.uv1.x, 1.0 - f.uv1.x);
        gutter = (1.0 - smoothstep(0.0, 0.07, edge)) * hasUv1 * (0.7 + 0.3 * VC_ValueNoise(w * 0.6));
        albedo.rgb = lerp(albedo.rgb, albedo.rgb * half3(1.7, 1.6, 1.4) + half3(0.03, 0.027, 0.02), gutter * 0.85);   // pale dust and grit at the kerb
        smooth *= 1.0 - 0.35 * gutter;
    }

    half wet = saturate(_VC_Wetness * _WetnessResponse + _WetnessOverride);
    if (wet > 0.001)
    {
        // iteration 30: beyond ~10 m the rain ripples and the bump detail alias into specular glitter (the moon's mirror
        // highlight on a 0.98-smooth surface scattered by ripple normals turned wet night streets snow-white), so they fade
        // with distance and the wet gloss is capped below a perfect mirror
        float dCam = distance(_WorldSpaceCameraPos.xyz, f.positionWS);
        // D1 rain: small, high-frequency ripples (rings ~15 cm) that are gone by ~14 m (were 0.5 m rings out to 30 m)
        half nearRipple = saturate(1.0 - (dCam - 4.0) / 10.0);
        half detailKeep = lerp(1.0, 0.15, saturate((dCam - 6.0) / 40.0));
        float pnoise = VC_Fbm(w / 7.0, 3);
        half puddle = saturate((pnoise - (1.02 - wet * _PuddleAmount * 0.75)) * 9.0);
        puddle = max(puddle, gutter * wet * _PuddleAmount * 0.9);   // water runs to the kerbs first
        albedo.rgb *= lerp(1.0, 0.55, wet);
        // iteration 31: 0.72 (the lamp pool's 48 spot lights reflected off 0.86-0.98 gloss at grazing angles merged into
        // one white sheet past ~15 m on wet nights; real wet asphalt and paving are glossy, not mirrors; puddles stay sharp)
        // D1: porosity: open asphalt soaks up the first rain and only turns glossy when saturated (wet^1.5)
        smooth = lerp(smooth, 0.72, wet * sqrt(wet));
        nTS.xy *= lerp(1.0, 0.3 * detailKeep, wet);
        albedo.rgb *= lerp(1.0, 0.85, puddle);
        smooth = lerp(smooth, 0.9, puddle);
        nTS = lerp(nTS, half3(0, 0, 1), puddle);
        if (_VC_RainIntensity > 0.001)
            nTS.xy += (VC_Ripples(w * 5.5, _Time.y * 1.3) + VC_Ripples(w * 7.3 + 3.1, _Time.y * 1.3 + 0.5)) * 0.3
                      * _VC_RainIntensity * (0.3 + 0.7 * puddle) * wet * nearRipple;
        nTS = normalize(nTS);
    }

    normalWS = TransformTangentToWorld(nTS, half3x3(f.tangentWS, f.bitangentWS, f.normalWS));
    s.albedo = saturate(albedo.rgb);
    s.metallic = metal;
    // D1 specular anti-aliasing (Kaplanyan-Tokuyoshi): the normal's variance across the pixel widens the highlight, so the
    // moon and the lamps reflect as soft streaks instead of white glitter on wet bumpy asphalt at a distance
    float3 dndx = ddx(normalWS), dndy = ddy(normalWS);
    float kernel = min(0.5 * (dot(dndx, dndx) + dot(dndy, dndy)), 0.12);
    float pr = 1.0 - smooth;
    float a2 = saturate(pr * pr * pr * pr + kernel);
    s.smoothness = 1.0 - sqrt(sqrt(a2));
    // iteration 32: environment reflection at night: the scene's default reflection is the editor-baked daytime sky, which URP's
    // Forward+ path keeps using outside the probe boxes (a runtime sky probe and a custom default reflection both had no
    // effect in iterations 25-31); occlusion scales only indirect light, so lamps and the moon still reflect
    s.occlusion = occlusion * lerp(1.0, 0.2, saturate(_VC_NightFactor));
    s.normalTS = nTS;
    s.emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uv).rgb * _EmissionColor.rgb;
    s.alpha = 1.0;
}

#endif
