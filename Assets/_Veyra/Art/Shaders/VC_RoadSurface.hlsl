#ifndef VC_ROAD_SURFACE_INCLUDED
#define VC_ROAD_SURFACE_INCLUDED

// Veyra/Road surface (card F2a): asphalt, concrete, kerbs, paving, cobbles, gravel, dirt, markings.
//  - World-space macro variation (two noise scales, ± _MacroVariation albedo) against tiling.
//  - Optional patch/crack overlay (_DetailMap) masked by world noise, and tyre-wear lanes from UV1.x (0–1 across the
//    carriageway) when the road mesh carries it.
//  - Wetness = global _VC_Wetness x _WetnessResponse (+ _WetnessOverride for testing): albedo x 0.55, smoothness -> 0.90,
//    normal strength x 0.3; a low-frequency puddle mask (grows with wetness) -> smoothness 0.98, flat normal; animated rain
//    ripples while _VC_RainIntensity > 0.

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
        albedo.rgb = lerp(albedo.rgb, det.rgb * macro, k);
        smooth = lerp(smooth, smooth * 0.8, k);
    }
    if (_TyreWear > 0.001)
    {
        // Two wheel paths per lane, at 0.3 and 0.7 of a lane; UV1.x spans the carriageway 0–1.
        float lanes = 2.0;
        float x = frac(f.uv1.x * lanes);
        half paths = exp(-pow((x - 0.3) / 0.07, 2.0)) + exp(-pow((x - 0.7) / 0.07, 2.0));
        half wear = saturate(paths) * _TyreWear * step(0.0001, f.uv1.x + f.uv1.y);
        albedo.rgb *= 1.0 - 0.12 * wear;
        smooth = lerp(smooth, saturate(smooth + 0.12), wear);
    }

    half wet = saturate(_VC_Wetness * _WetnessResponse + _WetnessOverride);
    if (wet > 0.001)
    {
        float pnoise = VC_Fbm(w / 7.0, 3);
        half puddle = saturate((pnoise - (1.02 - wet * _PuddleAmount * 0.75)) * 9.0);
        albedo.rgb *= lerp(1.0, 0.55, wet);
        smooth = lerp(smooth, 0.90, wet);
        nTS.xy *= lerp(1.0, 0.3, wet);
        albedo.rgb *= lerp(1.0, 0.85, puddle);
        smooth = lerp(smooth, 0.98, puddle);
        nTS = lerp(nTS, half3(0, 0, 1), puddle);
        if (_VC_RainIntensity > 0.001)
            nTS.xy += VC_Ripples(w * 1.7, _Time.y) * _VC_RainIntensity * (0.25 + 0.75 * puddle) * wet;
        nTS = normalize(nTS);
    }

    normalWS = TransformTangentToWorld(nTS, half3x3(f.tangentWS, f.bitangentWS, f.normalWS));
    s.albedo = saturate(albedo.rgb);
    s.metallic = metal;
    s.smoothness = smooth;
    s.occlusion = occlusion;
    s.normalTS = nTS;
    s.emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uv).rgb * _EmissionColor.rgb;
    s.alpha = 1.0;
}

#endif
