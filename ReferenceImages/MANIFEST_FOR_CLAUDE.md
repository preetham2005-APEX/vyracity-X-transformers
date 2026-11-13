# VEYRA CITY — Visual Reference Gallery for Claude Code Opus

**STRICT RULE (CLAUDE.md):** All images in this folder are **VISUAL TARGET BENCHMARKS ONLY**.
- Never import images, brand names, logos, HUD artwork, or map layouts into `Assets/`.
- Open and visually inspect these images alongside Veyra screenshots (`Screenshots/2026-11-10_iter32/`, `2026-11-10_V1_player/`) to diagnose lighting ratios, specular roughness, tree silhouettes, road wear, storefront depth, volumetric fog, and camera speed framing.

## How to populate (if folders are currently empty)

Run from an online Windows terminal:
```bat
Tools\download_references.cmd
```
Optional: drop recorded gameplay clips (`.mp4`/`.mkv`/`.webm`) into `ReferenceImages/_RawVideos/` and run:
```bat
python Tools\download_reference_gallery.py --extract-videos
```

## Reference Categories & What Claude Should Compare

### `01_GTAV_Enhanced_City_Day/` (15 images verified on disk)
- **Target for Veyra:** Daytime urban realism, skyscraper materials, paved plazas, palm/broadleaf foliage density, dusk haze, highways, airport/port scale — target for D1 Crown Downtown, D2 Azure Coast, D3 Ironport, D5 Westhaven, D7 Airfield.
  - `GTAV_01_Downtown_Boulevard_Curb_Buildings.jpg`: Wide asphalt boulevard, tire wear grooves, raised concrete median curb with manicured grass & trees, street banner flags on building facade, modern stone clad facade, overhead highway green sign.
  - `GTAV_02_Coastal_Hwy_UtilityPoles_Ocean.jpg`: Wooden utility poles with multiple overhead drooping catenary cables, chainlink fencing, W-beam guard rails, construction barrels, weathered cracked road edge, ocean horizon with subtle atmospheric depth.
  - `GTAV_03_Freeway_Overpass_GantrySigns_Cliff.jpg`: Green highway gantry sign spanning lanes, pedestrian bridge, stepped concrete retaining wall cutting into rocky cliff terrain, crisp painted road lane dashes.
  - `GTAV_04_Freeway_Canyon_Skyscrapers_Slopes.jpg`: Depressed freeway canyon, concrete retaining walls, grassy embankments with trees, overpass bridge, distant skyline with varied architectural heights and setbacks.
  - `GTAV_05_Highway_Interchange_Piers_Flyover.jpg`: Massive concrete bridge piers, elevated highway ramp banking, overhead traffic signals, junction pavement markings, stop lines.
  - `GTAV_06_Skyline_Vista_Overpass_Billboards.jpg`: Elevated highway curving across vista, high-rise cluster with reflective glass and varied heights, roadside commercial billboard, streetlamps.
  - `GTAV_07_Curved_Flyover_Ramp_Parapet.jpg`: Smooth curved flyover with solid concrete parapet barrier, expansion joints, distant cityscape with atmospheric depth perspective.
  - `GTAV_08_Coastal_Avenue_Apartments_Heli.jpg`: Coastal multi-lane avenue with concrete median barrier, luxury apartment balconies, distant mountains fading in aerial haze.
  - `GTAV_09_AutoShop_Industrial_Signs_OilStains.jpg`: Weathered commercial building, corrugated awnings, layered signage, patched asphalt with dark oil stains, red painted curbs, chain link gate.
  - `GTAV_10_Hill_Residential_Dirt_Stucco_Wires.jpg`: Residential dirt/gravel side road transition to asphalt, stucco houses, overhead power lines, cypress and pine trees, overcast ambient light.
  - `GTAV_11_Rain_Residential_Puddles_BrickWalls.jpg`: Rain/wet street in hill neighborhood, clear puddles reflecting sky and trees, red-painted curb, brick and stucco retaining walls, hedges, fallen leaf debris along curb.
  - `GTAV_12_HeavyRain_Boulevard_Puddles_WetSpray.jpg`: Wet wide boulevard under heavy rain, glossy water layer, tire spray particle VFX, atmospheric rain haze, reflective road surface.
  - `GTAV_13_Dealership_Construction_Crane_Netting.jpg`: Active construction site with tower crane, blue safety mesh, exposed concrete floor slabs, yellow inflatable air dancer in parking lot.
  - `GTAV_14_Historic_SpanishMission_Palms_Signals.jpg`: Spanish colonial revival church/mission with white stucco, arched windows, tile roof, lush royal palms, cantilevered traffic light gantry.
  - `GTAV_15_Hill_Street_ConcreteJoints_RedCurbs.jpg`: Sloped concrete residential street with expansion joints, yellow center lines, red curbs, landscaped hedges and palm trees, residential gates.

### `02_Cyberpunk2077_Neon_WetNight/` (0 images)
- **Target for Veyra:** Layered 3D neon signs, gritty wet alleys, overhead cables, steam/fog light scatter, puddles mirroring emissives, storefront depth — target for D6 Neon Mile & Downtown night.
  - *(Empty — run `Tools\download_references.cmd` online or paste gameplay screenshots here)*

### `03_Asphalt_Legends_Nitro_HUD/` (0 images)
- **Target for Veyra:** Aggressive chase camera framing, motion blur + speed lines, saturated clear-coat car reflections, multi-tier nitro shockwave flames, colourful drift/race tracks — target for VehicleFX, ChaseCamera, CarDetailFX.
  - *(Empty — run `Tools\download_references.cmd` online or paste gameplay screenshots here)*

### `04_NFS_StreetRacing_Rain_Night/` (0 images)
- **Target for Veyra:** Rain street racing, realistic raindrop streaks, tail-light reflection trails, wet road clear-coat sheen without icy frost glare — target for Rain/WetNight shaders & Lens/VFX fix.
  - *(Empty — run `Tools\download_references.cmd` online or paste gameplay screenshots here)*

### `05_Forza_Crew_Biomes_Roads_Nature/` (0 images)
- **Target for Veyra:** Rural highways, dense pine forests, mountain switchbacks with guardrails/rocks, sandy beaches, lakes, farmland fields, overcast/golden skies — target for D4 Blackridge, D8 Redwood Valley, D2 beaches, road crowns.
  - *(Empty — run `Tools\download_references.cmd` online or paste gameplay screenshots here)*

### `06_Rendering_Studies_NVIDIA_Courreges/` (0 images)
- **Target for Veyra:** Frame-by-frame shadow cascade, distant light sprite, anamorphic headlight, SSR puddle, and car-grounding technical references.
  - *(Empty — run `Tools\download_references.cmd` online or paste gameplay screenshots here)*

### `07_Video_Keyframes/` (0 images)
- **Target for Veyra:** Auto-extracted frames from any gameplay videos you place in ReferenceImages/_RawVideos/ (use --extract-videos).
  - *(Empty — run `Tools\download_references.cmd` online or paste gameplay screenshots here)*

## Curated Direct URL List (Included in Downloader)
- Steam App `3240220` — GTA V Enhanced (`01_GTAV_Enhanced_City_Day/`)
- Steam App `271590` — GTA V Legacy (`01_GTAV_Enhanced_City_Day/`)
- Steam App `1091500` — Cyberpunk 2077 (`02_Cyberpunk2077_Neon_WetNight/`)
- Steam App `1815780` — Asphalt Legends Unite (`03_Asphalt_Legends_Nitro_HUD/`)
- Steam App `1222680` — Need for Speed Heat (`04_NFS_StreetRacing_Rain_Night/`)
- Steam App `1846380` — Need for Speed Unbound (`04_NFS_StreetRacing_Rain_Night/`)
- Steam App `1551360` — Forza Horizon 5 (`05_Forza_Crew_Biomes_Roads_Nature/`)
- Steam App `2698940` — The Crew Motorfest (`05_Forza_Crew_Biomes_Roads_Nature/`)
- Verified Adrian Courreges + NVIDIA Graphics Study Stills: 22 images (`06_Rendering_Studies_NVIDIA_Courreges/`)

---

### `08_GTAV_VideoCapture_2026-11-12/` (33 images on disk)

**Provenance:** browser captures (2560x1600) of a GTA V 4K walkthrough video, supplied by the user 2026-11-12. Originals kept untouched at `E:\gta 5 pics`.

**Read these with the caveat that they are video captures, not clean game frames.** Each carries YouTube letterboxing, player chrome, a HUD (minimap, health/armour bar, phone overlay), subtitles and a channel watermark, and has been through YouTube's compression. Consequences for how they may be used:
- **Trustworthy:** composition, geometry, prop density, material/wear *patterns*, road marking layout, infrastructure types, sun angle, fog depth, silhouette.
- **NOT trustworthy as absolute values:** exact colour, black level, contrast, saturation, noise. Compression crushes shadows and shifts hue. Never pick a tonemap curve, exposure value or grade from these — use them to judge *relative* ratios (sky vs road, lit vs shade) only.
- **Never sample pixels from the HUD, minimap, phone, subtitles or watermark areas.**
- **Originality (CLAUDE.md):** benchmark only. The named streets, business signage, vehicle brands and the map layout in these frames are Rockstar's and must never be reproduced. What Veyra takes is the *technique* — curb profiles, wear distribution, lighting ratios, prop vocabulary.

| File | Veyra target | What to read off it |
|---|---|---|
| `VC_01_Day_Downtown_ConstructionSite_ParkingBays` | D1 | Active construction blockout: tower crane, blue debris netting, exposed floor slabs, air dancer. Painted parking bays, hedge median |
| `VC_02_Day_CivicBoulevard_GrassMedian_Colonnade` | D1 | Grass median strip, stone colonnade civic facade, vertical banners, distant haze ramp |
| `VC_03_Day_PalmBoulevard_SignalGantry_MissionChurch` | D1/D5 | Cantilevered signal mast arms over multi-lane junction, Spanish mission towers, palm rhythm |
| `VC_04_Day_HillResidential_TyreWear_RedCurbs` | D5 | **Best tyre-wear reference.** Two dark wheel tracks per lane, yellow double centre, red curb, manhole, driveway aprons |
| `VC_05_Day_CoastalHwy_UtilityPoles_Barrels` | D2 | Wooden poles + sagging catenary, orange barrels, chainlink, weathered shoulder, ocean haze |
| `VC_06_Day_Freeway_GantrySign_CutSlope` | D2/D4 | Green gantry sign, pedestrian overbridge, cut slope stratification, W-beam guardrail |
| `VC_07_Day_FreewayCanyon_RetainingWall_Skyline` | D1/D4 | Depressed freeway, stepped concrete retaining wall, grass embankment, skyline setbacks |
| `VC_08_Day_FlyoverPiers_RampEntrance` | D1/D3 | Flyover pier scale from below, ramp entrance geometry, concrete median island nose |
| `VC_09_Day_Vista_StackInterchange_Billboards` | D1 | Stack interchange silhouette, billboard placement, embankment landscaping |
| `VC_10_Day_Vista_CurvedFlyover_Haze` | D1 | **Best aerial-perspective reference.** Fog ramp across 3 depth layers, curved flyover parapet |
| `VC_11_Day_CoastalAvenue_MedianBarrier_Apartments` | D2 | Concrete median barrier, apartment balcony rhythm, coastal palms |
| `VC_12_Day_Industrial_AutoShop_LayeredSignage` | D3 | **Best layered-signage reference.** Projecting 3D signs, corrugated awnings, weed growth at kerb, oil-stained concrete |
| `VC_13_Overcast_HillResidential_DirtRoad_Wires` | D5/D8 | Overcast flat ambient, dirt-to-asphalt transition, stucco houses, cypress |
| `VC_14_Rain_Residential_Puddles_Hedges` | D5 rain | Wet asphalt sheen without blow-out, puddles in gutter line, hedge wall, leaf debris |
| `VC_15_HeavyRain_Boulevard_SheetPuddles_Spray` | D1/D2 rain | **Best wet-road reference.** Large sheet puddles mirroring sky, rain haze, spray |
| `VC_16_Day_CommercialStrip_Billboards_Verge` | D6 | Rooftop billboard stack, shopfront glazing, grass verge, patched asphalt |
| `VC_17_Day_RetailStreet_Awnings_Shopfronts` | D6 | **Best ground-floor articulation reference.** Awnings, recessed entries, projecting signs, varied storefront colour |
| `VC_18_Day_PalmBoulevard_Bus_HedgeMedian` | D1/D2 | Wide boulevard lane count, clipped hedge median, bus scale |
| `VC_19_Storm_Freeway_WetConcrete_WarehouseWall` | D3 storm | Storm sky gradient, wet concrete freeway, painted warehouse wall |
| `VC_20_Day_BridgeApproach_OrnateLamps_Balustrade` | D1 | Ornate twin-lantern lamp standards, concrete balustrade parapet, worn deck |
| `VC_21_Day_HistoricCore_BrickFacades_ConcreteJoints` | D1 | **Best concrete-joint reference.** Tar-filled expansion joints, brick/stone mid-rise mix, kerb extensions |
| `VC_22_Day_BackStreet_RollupGrilles_Dumpsters` | D3/D6 | Roll-up security grilles, dumpsters, faded paint, overhead service wires |
| `VC_23_Day_DowntownIntersection_Heritage_Palms` | D1 | Heritage cornice detail, crosswalk layout, overhead wire sag across junction |
| `VC_24_Day_UnderFreeway_Shade_LightShafts` | D1/D3 | **Best shade/AO reference.** Deep shade under deck, light shafts between piers, strip lighting, ambient bounce |
| `VC_25_Day_MountainDirtRoad_Scrub_Dust` | D4 | Dirt road surface, scrub scatter density, rock outcrops, pine line |
| `VC_26_Day_LakesideDirtRoad_Pines_RockCut` | D8 | Lakeside dirt road, conifer silhouettes, rock cut face, distant haze |
| `VC_27_Day_RuralJunction_DryHills_Palms` | D8 | Rural junction, dry grass, shack construction, dust-road edge blending |
| `VC_28_Rain_Dusk_RuralEdge_WaterTower_Fog` | D8 rain | Dusk rain fog depth, water tower silhouette, wet road at low light |
| `VC_29_Rain_IndustrialDescent_GraffitiWall_Fog` | D3 rain | Wet descent grade, graffitied retaining wall, fog occluding mid-ground |
| `VC_30_Day_WideIntersection_Pylons_Hills` | D3/D7 | Transmission pylon scale, wide junction markings, parked-car density |
| `VC_31_Day_ChannelBridge_Lamps_IndustrialYard` | D3 | Channel bridge, lamp standard spacing, industrial yard clutter |
| `VC_32_Day_PortFreeway_ArchBridge_Tanks` | D3/D7 | **Best port reference.** Arch bridge, storage tanks, gantry, pylon forest |
| `VC_33_Dusk_Freeway_GantryTruss_Mountains` | D4 dusk | Gantry truss structure, dusk sky gradient, concrete freeway at low sun |

**Coverage gaps these do NOT fill:** no night scenes, no neon (D6 Neon Mile night still has zero reference — `02_Cyberpunk2077_Neon_WetNight/` remains empty), no airfield (D7), no interiors.
