#ifndef VC_FACADE_SURFACE_INCLUDED
#define VC_FACADE_SURFACE_INCLUDED

// Veyra/Facade surface (card F2b). Building UVs (BuildingGenerator) are in texture tiles: u along the wall, v up from
// the building base; one tile holds _BaysPerTile bays and _FloorsPerTile floors.
//  - Punched windows (_WindowRect) or a glass curtain wall (_CurtainWall: two panes per bay, mullions, spandrel band).
//  - Interior mapping: every window shows a fake room (box of 1 bay x 1 floor x _RoomDepth) ray-cast in tangent space;
//    back wall from the 4 x 4 _InteriorAtlas, side walls / floor / ceiling shaded from it.
//  - Night: a seeded share of windows per building (_LitFractionMin–Max) lights up in warm 3000 K or cool 4500 K,
//    staggered as _VC_NightFactor rises; offices (_Office) dim after 23:00. Blinds on some windows.
//  - Grime: darker near street level and in streaks under the sills.
// Horizontal faces (roofs that share the material) get the plain wall, without windows.

void VC_FacadeRoom(float2 room, float3 dirTS, float variant, out half3 color, out half depth01)
{
    float3 o = float3(room, 0.0);
    float3 d = dirTS;
    d.x = abs(d.x) < 1e-4 ? 1e-4 : d.x;
    d.y = abs(d.y) < 1e-4 ? 1e-4 : d.y;
    d.z = max(d.z, 1e-3);
    float depth = max(_RoomDepth, 0.2);
    float tx = (d.x > 0.0 ? 1.0 - o.x : -o.x) / d.x;
    float ty = (d.y > 0.0 ? 1.0 - o.y : -o.y) / d.y;
    float tz = depth / d.z;
    float t = min(tx, min(ty, tz));
    float3 hit = o + d * t;
    float idx = floor(variant * 16.0);
    float2 cell = float2(fmod(idx, 4.0), floor(idx / 4.0));
    float2 atlasBase = cell / 4.0;
    half3 back = SAMPLE_TEXTURE2D(_InteriorAtlas, sampler_InteriorAtlas, atlasBase + saturate(hit.xy) * 0.25 * 0.98 + 0.0025).rgb;
    half3 wallTone = SAMPLE_TEXTURE2D_LOD(_InteriorAtlas, sampler_InteriorAtlas, atlasBase + float2(0.02, 0.17), 3).rgb;
    half3 floorTone = SAMPLE_TEXTURE2D_LOD(_InteriorAtlas, sampler_InteriorAtlas, atlasBase + float2(0.125, 0.005), 3).rgb;
    half z01 = saturate(hit.z / depth);
    if (t == tz)            color = back;
    else if (t == tx)       color = wallTone * 0.85;
    else if (d.y < 0.0)     color = floorTone * 0.8;
    else                    color = lerp(half3(0.82, 0.8, 0.76), wallTone, 0.25) * (0.75 + 0.25 * (1.0 - z01));
    color *= lerp(1.0, 0.55, z01);
    depth01 = z01;
}

void VC_Surface(VC_FragInput f, inout SurfaceData s, inout half3 normalWS)
{
    float2 wuv = f.uv * _WallTiling.xy;
    half4 wall = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, wuv) * _BaseColor;
    half4 mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, wuv);
    half3 nTS = SampleNormal(wuv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
    half smooth = mask.a * _Smoothness;
    half metal = mask.r * _Metallic;
    half occlusion = lerp(1.0, mask.g, _OcclusionStrength);

    float2 cellCoord = f.uv * float2(_BaysPerTile, _FloorsPerTile);
    float2 cellId = floor(cellCoord);
    float2 c = frac(cellCoord);
    // Buildings are merged per chunk, so the building seed comes from a ~24 m world cell.
    float bseed = VC_Hash12(floor(f.positionWS.xz / 24.0) + _Seed * 13.1);
    float wseed = VC_Hash12(cellId * float2(1.37, 2.11) + bseed * 97.0 + _Seed);
    bool vertical = abs(f.normalWS.y) < 0.5;

    half glass = 0.0, frame = 0.0, sill = 0.0;
    float2 room = c;
    if (vertical)
    {
        if (_CurtainWall > 0.5)
        {
            float px = frac(c.x * 2.0);
            float mw = max(_WindowRect.w, 0.005);
            half mull = saturate(step(px, mw) + step(1.0 - mw, px));
            half spandrel = step(c.y, 0.14);
            half transom = step(abs(c.y - 0.14), 0.012);
            glass = (1.0 - mull) * (1.0 - spandrel) * (1.0 - transom);
            frame = saturate(mull + transom);
            room = float2(c.x, saturate((c.y - 0.14) / 0.86));
        }
        else
        {
            float hw = _WindowRect.x * 0.5;
            float x0 = 0.5 - hw, x1 = 0.5 + hw, y0 = _WindowRect.y, y1 = _WindowRect.y + _WindowRect.z;
            half inside = step(x0, c.x) * step(c.x, x1) * step(y0, c.y) * step(c.y, y1);
            float edge = min(min(c.x - x0, x1 - c.x), min(c.y - y0, y1 - c.y));
            frame = inside * step(edge, _WindowRect.w);
            glass = inside * (1.0 - frame);
            sill = step(x0 - 0.03, c.x) * step(c.x, x1 + 0.03) * step(y0 - 0.035, c.y) * step(c.y, y0);
            room = c;
        }
    }

    // Grime: street level and streaks under the sills.
    float floors = f.uv.y * _FloorsPerTile;
    half grime = saturate(1.0 - floors / 1.4) * 0.6;
    if (!(_CurtainWall > 0.5) && vertical)
    {
        float hw = _WindowRect.x * 0.5;
        half under = step(abs(c.x - 0.5), hw * 0.8) * step(c.y, _WindowRect.y) * saturate(1.0 - (_WindowRect.y - c.y) / 0.35);
        grime += under * 0.5 * VC_ValueNoise(float2(cellCoord.x * 9.0, 0.0) + cellId * 3.1);
    }
    grime *= _Grime;
    wall.rgb *= 1.0 - 0.35 * saturate(grime);
    smooth *= 1.0 - 0.3 * saturate(grime);
    wall.rgb *= 1.0 - 0.25 * sill;

    half3 albedo = wall.rgb;
    half3 emission = 0;
    half3 wnTS = nTS;
    if (glass > 0.0)
    {
        half3 V = f.viewDirWS;
        float3 dirTS = float3(dot(-V, f.tangentWS), dot(-V, f.bitangentWS), dot(V, f.normalWS));
        half3 roomColor; half depth01;
        VC_FacadeRoom(room, dirTS, wseed, roomColor, depth01);

        // Blinds on some windows (upper part).
        half blindCover = frac(wseed * 13.7) > 0.62 ? frac(wseed * 29.1) * 0.75 : 0.0;
        half blind = step(1.0 - blindCover, room.y);
        half slat = 0.82 + 0.18 * step(0.5, frac(room.y * 38.0));
        roomColor = lerp(roomColor, half3(0.52, 0.48, 0.4) * slat, blind);

        float litFrac = lerp(_LitFractionMin, _LitFractionMax, bseed);
        float hour = _VC_TimeOfDay;
        half officeDim = (_Office > 0.5 && (hour >= 23.0 || hour < 6.0)) ? 0.35 : 1.0;
        half isLit = step(wseed, litFrac * officeDim);
        float threshold = lerp(0.12, 0.92, frac(wseed * 7.31));
        half on = isLit * smoothstep(threshold - 0.06, threshold + 0.06, _VC_NightFactor);
        half3 warm = half3(1.0, 0.71, 0.42), cool = half3(1.0, 0.88, 0.76);
        half3 lightColor = frac(wseed * 3.77) < 0.62 ? warm : cool;
        half3 day = roomColor * _InteriorDim * (1.0 - 0.85 * saturate(_VC_NightFactor));
        // Lit rooms take the colour of their light: interior colours are desaturated towards their luminance.
        half lumRoom = dot(roomColor, half3(0.2126, 0.7152, 0.0722));
        half3 litRoom = lerp(lumRoom.xxx, roomColor, 0.35) * 1.6;
        half3 night = litRoom * lightColor * _WindowEmission * on * (blind > 0.5 ? 0.55 : 1.0);
        emission = (day + night) * glass;

        albedo = lerp(albedo, _GlassColor.rgb * 0.12, glass);
        smooth = lerp(smooth, _GlassSmoothness, glass);
        metal = lerp(metal, 0.0, glass);
        occlusion = lerp(occlusion, 1.0, glass);
        half2 tilt = (VC_Hash22(cellId + 3.3) - 0.5) * 0.02;
        wnTS = lerp(nTS, normalize(half3(tilt, 1.0)), glass);
    }
    if (frame > 0.0)
    {
        albedo = lerp(albedo, half3(0.06, 0.065, 0.07), frame);
        smooth = lerp(smooth, 0.5, frame);
        metal = lerp(metal, 0.6, frame);
    }

    normalWS = TransformTangentToWorld(wnTS, half3x3(f.tangentWS, f.bitangentWS, f.normalWS));
    s.albedo = saturate(albedo);
    s.metallic = metal;
    s.smoothness = smooth;
    s.occlusion = occlusion;
    s.normalTS = wnTS;
    s.emission = emission;
    s.alpha = 1.0;
}

#endif
