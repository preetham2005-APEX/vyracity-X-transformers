#ifndef VC_KITGLASS_SURFACE_INCLUDED
#define VC_KITGLASS_SURFACE_INCLUDED

// Veyra/KitGlass: night-lit glazing for the authored building kit (ArtSource/Blender/Buildings).
//
// Why this is not Veyra/Facade. The kit's buildings carry their piers, mullions and per-floor bands as REAL
// GEOMETRY - that is the whole point of the kit, and it is why the city stopped looking like boxes. Drawing
// Veyra/Facade's UV window grid on top of that gives windows inside windows. So this shader draws no grid at all:
// each glazed panel is lit per FLOOR and per BAY derived from WORLD POSITION, which is independent of whatever UVs
// a kit model happens to have, and of how the model was scaled to fit its record.
//
// The lit-window logic is deliberately the same as VC_FacadeSurface's (floor and core coherence, three light
// temperatures, wide per-panel brightness spread, view-angle falloff), so the kit buildings and the handful of
// remaining shell buildings read as the same city at night rather than two different ones.

TEXTURE2D(_VC_GroundHeight); SAMPLER(sampler_VC_GroundHeight);
float4 _VC_GroundHeightRect;   // x/z min, 1/width, 1/depth (global; z = 0 while unset)

void VC_Surface(VC_FragInput f, inout SurfaceData s, inout half3 normalWS)
{
    half4 b = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, f.uv) * _BaseColor;
    half3 albedo = b.rgb;

    // street level, so a floor index means the same thing on a hill as on the flat
    float groundY = 0.0;
    if (_VC_GroundHeightRect.z > 0.0)
        groundY = SAMPLE_TEXTURE2D_LOD(_VC_GroundHeight, sampler_VC_GroundHeight,
                                       (f.positionWS.xz - _VC_GroundHeightRect.xy) * _VC_GroundHeightRect.zw, 0).r;
    float hAbove = f.positionWS.y - groundY;

    float fh = max(_KitFloorHeight, 1.0);
    float bw = max(_KitBayWidth, 1.0);
    float floorId = floor(hAbove / fh);
    // which horizontal axis runs ALONG this wall: the one the normal is not facing
    float along = abs(f.normalWS.x) > abs(f.normalWS.z) ? f.positionWS.z : f.positionWS.x;
    float bayId = floor(along / bw);

    // one building per ~24 m world cell, one occupancy mood per ~60 m block (as VC_FacadeSurface)
    float bseed = VC_Hash12(floor(f.positionWS.xz / 24.0) + _Seed * 13.1);
    float occ = VC_Hash12(floor(f.positionWS.xz / 60.0) + _Seed * 7.7);
    float wseed = VC_Hash12(float2(bayId * 1.37, floorId * 2.11) + bseed * 97.0);
    float floorSeed = VC_Hash12(float2(floorId, bseed * 17.3));
    float colSeed = VC_Hash12(float2(bayId, bseed * 29.7));

    float litFrac = lerp(_LitFractionMin, _LitFractionMax, bseed) * lerp(0.15, 1.3, occ * occ);
    float hour = _VC_TimeOfDay;
    half officeDim = (_Office > 0.5 && (hour >= 23.0 || hour < 6.0)) ? 0.35 : 1.0;
    // busy floors and nearly dark ones; ~4 % of bays are stair / lift cores lit all night
    half floorK = 0.25 + 1.5 * floorSeed * floorSeed;
    half core = step(0.96, colSeed) * 0.65;
    half pLit = saturate(litFrac * officeDim * floorK);
    half isLit = saturate(step(wseed, pLit) + core);
    float threshold = lerp(0.12, 0.92, frac(wseed * 7.31));
    half on = isLit * smoothstep(threshold - 0.06, threshold + 0.06, _VC_NightFactor);

    half3 warm = half3(1.0, 0.71, 0.42), cool = half3(1.0, 0.88, 0.76);
    float lk = frac(wseed * 3.77);
    half3 lightColor = lk < 0.55 ? warm : lk < 0.88 ? cool : half3(0.78, 0.88, 1.0);
    lightColor *= 0.35 + 1.15 * frac(wseed * 23.9);

    // a lit room is only visible through the opening's solid angle: square-on shows the room, edge-on shows
    // reflection. Without this both faces of a tower glow identically and it reads flat.
    half NdotV = saturate(dot(f.normalWS, normalize(f.viewDirWS)));
    half faceK = lerp(0.3, 1.0, NdotV);

    half3 emission = lightColor * _WindowEmission * on * faceK;

    // unlit glazing goes darker as night falls so the lit panels read against it
    half night = saturate(_VC_NightFactor);
    albedo = lerp(albedo, _GlassColor.rgb * 0.4, night * 0.75);

    s.albedo = saturate(albedo);
    s.metallic = _Metallic;
    s.smoothness = _GlassSmoothness;
    // the scene's default reflection is the editor-baked daytime sky, which Forward+ keeps using outside the probe
    // boxes; occlusion scales only indirect light, so lamps and the moon still reflect (as VC_FacadeSurface)
    s.occlusion = lerp(1.0, 0.25, night);
    s.normalTS = half3(0, 0, 1);
    s.emission = emission;
    s.alpha = 1.0;
}

#endif
