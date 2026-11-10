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
// Round 2 (visual iteration 20, GTA V / Cyberpunk street level): ground-floor shopfronts on most punched-window buildings
// (wide display glass, a backlit fascia sign in a seeded colour, some shops behind roller shutters, lit from dusk), a
// slab line at every floor and a cornice over the shop floor, and TV flicker in a few lit flats at night. No new
// properties or keywords (same CBUFFER, same variants).
// Distance (iteration 20): from far away every window drew a high-contrast fake room, so towers read as noise. The
// rooms now fade with distance and grazing angle (Fresnel) into plain sky-reflecting glass, and when a window cell is
// smaller than ~2 pixels the grid fades to its average coverage and average lit share (per 4 x 4-window block), so
// distant facades and night skylines stop shimmering.
// Ground (iteration 20): the city's building UVs are absolute world height (CityBuilder: v = y / 14) and downtown stands
// 4-43 m above zero, so street level comes from the baked terrain height (GroundHeightField: _VC_GroundHeight over
// _VC_GroundHeightRect). Shopfronts are laid out in metres above that ground: glass 0.25-3.0 m, sign 3.15-3.8 m,
// cornice to 4.3 m; street-level grime counts floors from it too. Without the field the ground is y = 0 (as before).

TEXTURE2D(_VC_GroundHeight); SAMPLER(sampler_VC_GroundHeight);
float4 _VC_GroundHeightRect;   // x/z min, 1/width, 1/depth (global; z = 0 while unset)

// lod: explicit mip of the back-wall sample (iteration 22: the room is evaluated inside a dynamic branch, where implicit
// derivatives are not allowed; the mip comes from the distance instead)
void VC_FacadeRoom(float2 room, float3 dirTS, float variant, float lod, out half3 color, out half depth01)
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
    half3 back = SAMPLE_TEXTURE2D_LOD(_InteriorAtlas, sampler_InteriorAtlas, atlasBase + saturate(hit.xy) * 0.25 * 0.98 + 0.0025, lod).rgb;
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
    float groundY = 0.0;
    if (_VC_GroundHeightRect.z > 0.0)
        groundY = SAMPLE_TEXTURE2D_LOD(_VC_GroundHeight, sampler_VC_GroundHeight, (f.positionWS.xz - _VC_GroundHeightRect.xy) * _VC_GroundHeightRect.zw, 0).r;
    float hAbove = f.positionWS.y - groundY;
    float bayW = 12.0 / max(_BaysPerTile, 0.5);   // metres per bay (CityBuilder: one UV tile = 12 m x 14 m)
    // shop floor: the street level of ~60 % of the urban punched-window buildings (the two house facades, wood and
    // render, are the only materials with a 1.0-bay room depth, so suburban houses keep plain ground floors)
    bool shopFloor = vertical && _CurtainWall < 0.5 && _RoomDepth > 1.05 && hAbove < 4.3 && hAbove > -2.0 && bseed < 0.6;
    // iteration 25: glass towers get a double-height lobby at street level (0.2-4.6 m, no spandrels), lit like the shops
    bool lobby = vertical && _CurtainWall > 0.5 && hAbove < 5.0 && hAbove > -2.0;
    bool street = shopFloor || lobby;
    float sseed = VC_Hash12(float2(floor(cellId.x / 2.0), bseed * 53.0));   // one shop per two bays

    half glass = 0.0, frame = 0.0, sill = 0.0, fascia = 0.0, shutter = 0.0;
    float2 room = c;
    if (vertical)
    {
        if (shopFloor)
        {
            // in metres: display glass 0.25-3.0 m above the pavement, a sign band 3.15-3.8 m
            const float gy0 = 0.25, gy1 = 3.0;
            half inside = step(0.035, c.x) * step(c.x, 0.965) * step(gy0, hAbove) * step(hAbove, gy1);
            float dx = abs(c.x - 0.5) * bayW;
            float edgeM = min((0.5 - 0.035) * bayW - dx, min(hAbove - gy0, gy1 - hAbove));
            half mullion = step(dx, 0.035) * step(0.5, frac(sseed * 5.3));
            half door = step(0.72, frac(sseed * 9.1)) * step(frac(cellId.x * 0.5), 0.25) * step(abs(dx - 0.5), 0.035) * step(hAbove, 2.4);
            frame = inside * saturate(step(edgeM, 0.07) + mullion + door);
            shutter = frac(sseed * 11.3) < 0.14 ? inside : 0.0;
            glass = inside * (1.0 - frame) * (1.0 - shutter);
            fascia = step(3.15, hAbove) * step(hAbove, 3.8) * step(0.02, c.x) * step(c.x, 0.98);
            room = float2(c.x, saturate((hAbove - gy0) / (gy1 - gy0)));
        }
        else if (_CurtainWall > 0.5)
        {
            float px = frac(c.x * 2.0);
            float mw = max(_WindowRect.w, 0.005);
            half mull = saturate(step(px, mw) + step(1.0 - mw, px));
            half spandrel = step(c.y, 0.14);
            half transom = step(abs(c.y - 0.14), 0.012);
            glass = (1.0 - mull) * (1.0 - spandrel) * (1.0 - transom);
            frame = saturate(mull + transom);
            room = float2(c.x, saturate((c.y - 0.14) / 0.86));
            if (lobby)
            {
                half inLobby = step(0.2, hAbove) * step(hAbove, 4.6);
                half head = step(abs(hAbove - 4.75), 0.15);   // a dark head beam over the lobby glass
                glass = (1.0 - mull) * inLobby;
                frame = saturate(mull * inLobby + head);
                room = float2(c.x, saturate((hAbove - 0.2) / 4.4));
            }
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

    float dist = distance(_WorldSpaceCameraPos.xyz, f.positionWS);
    float2 fwc = fwidth(cellCoord);
    half aa = vertical ? smoothstep(0.4, 1.0, max(fwc.x, fwc.y)) : 0.0;   // iteration 21: grid kept to ~1.5 px cells
    if (aa > 0.0)
    {
        half avgGlass = _CurtainWall > 0.5 ? 0.7 : shopFloor ? 0.6 : _WindowRect.x * _WindowRect.z * 0.9;
        glass = lerp(glass, avgGlass, aa);
        frame = lerp(frame, 0.08, aa);
        fascia *= 1.0 - aa;
        shutter *= 1.0 - aa;
        sill *= 1.0 - aa;
    }

    // Grime: street level and streaks under the sills.
    float floors = hAbove / (14.0 / max(_FloorsPerTile, 0.5));   // floors above the pavement
    half grime = saturate(1.0 - floors / 1.4) * 0.6;
    if (!(_CurtainWall > 0.5) && vertical && !shopFloor)
    {
        float hw = _WindowRect.x * 0.5;
        half under = step(abs(c.x - 0.5), hw * 0.8) * step(c.y, _WindowRect.y) * saturate(1.0 - (_WindowRect.y - c.y) / 0.35);
        grime += under * 0.5 * VC_ValueNoise(float2(cellCoord.x * 9.0, 0.0) + cellId * 3.1);
    }
    grime *= _Grime;
    wall.rgb *= 1.0 - 0.35 * saturate(grime);
    smooth *= 1.0 - 0.3 * saturate(grime);
    wall.rgb *= 1.0 - 0.25 * sill;
    if (vertical && !(_CurtainWall > 0.5))
    {
        // slab line at each floor, a deeper cornice band over the shop floor
        wall.rgb *= 1.0 - 0.16 * step(c.y, 0.025) * (shopFloor ? 0.0 : 1.0);
        wall.rgb *= 1.0 - 0.3 * (shopFloor ? step(3.95, hAbove) : 0.0);
    }

    half3 albedo = wall.rgb;
    half3 emission = 0;
    if (shutter > 0.0)
    {
        half rib = 0.82 + 0.18 * step(0.5, frac(room.y * 34.0));
        albedo = lerp(albedo, half3(0.36, 0.37, 0.38) * rib * (1.0 - 0.3 * saturate(grime)), shutter);
        smooth = lerp(smooth, 0.38, shutter);
        metal = lerp(metal, 0.55, shutter);
    }
    if (fascia > 0.0)
    {
        // backlit shop sign: seeded colour, lit from dusk on most shops
        float k = frac(sseed * 7.7);
        half3 fc = k < 0.2 ? half3(0.95, 0.93, 0.88) : k < 0.38 ? half3(0.9, 0.16, 0.14) : k < 0.54 ? half3(0.1, 0.62, 0.66)
                 : k < 0.7 ? half3(0.98, 0.62, 0.12) : k < 0.84 ? half3(0.12, 0.2, 0.42) : half3(0.75, 0.15, 0.55);
        half letters = 0.78 + 0.22 * step(0.5, frac(c.x * 22.0 + sseed)) * step(abs(hAbove - 3.475), 0.13) * step(abs(c.x - 0.5), 0.3);
        albedo = lerp(albedo, fc * 0.62 * letters, fascia);
        smooth = lerp(smooth, 0.6, fascia);
        half signOn = step(frac(sseed * 3.3), 0.75) * smoothstep(0.05, 0.35, _VC_NightFactor);
        emission += fc * letters * _WindowEmission * 0.45 * signOn * fascia;
    }
    half3 wnTS = nTS;
    if (glass > 0.0)
    {
        half3 V = f.viewDirWS;
        float3 dirTS = float3(dot(-V, f.tangentWS), dot(-V, f.bitangentWS), dot(V, f.normalWS));
        // rooms read up close only: fade with distance, and with the grazing angle (glass reflects instead); beyond
        // 240 m (or on anti-aliased cells) the room ray-cast and its three atlas samples are skipped (iteration 22)
        half NdotV = saturate(dot(f.normalWS, normalize(f.viewDirWS)));
        half fres = pow(1.0 - NdotV, 4.0);
        half nearK = saturate(1.0 - (dist - 40.0) / 200.0) * (1.0 - aa);
        half3 roomColor = half3(0.36, 0.34, 0.31);
        half depth01 = 0.5;
        UNITY_BRANCH
        if (nearK > 0.001)
        {
            half3 rc;
            VC_FacadeRoom(room, dirTS, wseed, clamp(log2(max(dist, 1.0) / 12.0), 0.0, 6.0), rc, depth01);
            roomColor = lerp(roomColor, rc, nearK);
        }

        // Blinds on some windows (upper part).
        half blindCover = frac(wseed * 13.7) > 0.62 ? frac(wseed * 29.1) * 0.75 : 0.0;
        half blind = step(1.0 - blindCover, room.y);
        half slat = 0.82 + 0.18 * step(0.5, frac(room.y * 38.0));
        roomColor = lerp(roomColor, half3(0.52, 0.48, 0.4) * slat, blind);

        // iteration 24: occupancy per ~60 m block varies from nearly dark to busy (iteration 21's flat two-thirds still
        // read as an even wall of lit windows; real and GTA V skylines mix dark towers with bright ones), ~25 % overall
        float occ = VC_Hash12(floor(f.positionWS.xz / 60.0) + _Seed * 7.7);
        float litFrac = street ? 0.86 : lerp(_LitFractionMin, _LitFractionMax, bseed) * lerp(0.15, 1.3, occ * occ);
        float hour = _VC_TimeOfDay;
        half officeDim = (_Office > 0.5 && (hour >= 23.0 || hour < 6.0)) ? 0.35 : 1.0;
        half isLit = step(street ? sseed : wseed, street ? litFrac : litFrac * officeDim);
        float threshold = street ? lerp(0.05, 0.3, frac(sseed * 7.31)) : lerp(0.12, 0.92, frac(wseed * 7.31));
        half on = isLit * smoothstep(threshold - 0.06, threshold + 0.06, _VC_NightFactor);
        // far away: the average lit share, varied per block of 4 x 4 windows so a skyline keeps some structure
        half blockVar = 0.5 + VC_Hash12(floor(cellCoord / 4.0) + bseed * 31.0);
        on = lerp(on, saturate(litFrac * officeDim * blockVar) * smoothstep(0.1, 0.9, _VC_NightFactor), aa);
        half3 warm = half3(1.0, 0.71, 0.42), cool = half3(1.0, 0.88, 0.76);
        // iteration 23: three light temperatures (warm lamps, cool white, LED blue-white) and per-window brightness
        float lk = frac(wseed * 3.77);
        half3 lightColor = lk < 0.55 ? warm : lk < 0.88 ? cool : half3(0.78, 0.88, 1.0);
        lightColor *= 0.6 + 0.6 * frac(wseed * 23.9);
        half3 day = roomColor * _InteriorDim * (1.0 - 0.85 * saturate(_VC_NightFactor)) * (0.35 + 0.65 * nearK) * (1.0 - fres);
        // Lit rooms take the colour of their light: interior colours are desaturated towards their luminance.
        half lumRoom = dot(roomColor, half3(0.2126, 0.7152, 0.0722));
        half3 litRoom = lerp(lumRoom.xxx, roomColor, 0.35) * 1.6;
        half3 night = litRoom * lightColor * _WindowEmission * on * (blind > 0.5 ? 0.55 : 1.0);
        if (street) night *= 1.35;   // display windows and lobbies are brighter than flats and offices
        // a few flats watch TV: a cool, flickering light
        if (!street && _Office < 0.5 && frac(wseed * 41.7) < 0.09)
            night *= half3(0.55, 0.72, 1.25) * (0.7 + 0.3 * sin(_Time.y * (6.0 + 11.0 * frac(wseed * 3.1))) * sin(_Time.y * 2.3 + wseed * 50.0));
        emission += (day + night) * glass;

        half paneTint = lerp(0.8 + 0.4 * frac(wseed * 17.3), 1.0, aa);   // panes differ slightly (blinds, film, dirt)
        albedo = lerp(albedo, _GlassColor.rgb * 0.12 * paneTint, glass);
        smooth = lerp(smooth, _GlassSmoothness, glass);
        metal = lerp(metal, 0.0, glass);
        // iteration 32: environment reflection at night: the scene's default reflection is the editor-baked daytime sky, which URP's
    // Forward+ path keeps using outside the probe boxes (a runtime sky probe and a custom default reflection both had no
    // effect in iterations 25-31); occlusion scales only indirect light, so lamps and the moon still reflect
        occlusion = lerp(occlusion, lerp(1.0, 0.25, saturate(_VC_NightFactor)), glass);
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
