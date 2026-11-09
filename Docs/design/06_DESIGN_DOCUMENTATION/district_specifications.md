# Veyra City — District Specifications

Eight districts, each with a distinct identity, built on the validated master map. Every section ends with a 3D production specification (models, modular pieces, scale, lighting and materials, Android budget) for the Unity build. Stage labels follow `visual_quality_roadmap.md`: BLOCKOUT → GREYBOX → ART PASS → FINAL.

## 1. Crown Downtown

*The glass-and-stone financial heart of Veyra, ringed by the elevated Harbor Viaduct and split by the Sable River.*

- **Area:** 3.5 km² of land · **Elevation:** 1–110 m · **Map cells:** D3, D4, D5, E3, E4, E5
- **Colour code:** #3E6FB0 · **Status:** ART PASS (prototype benchmark district): typology architecture, street dressing, traffic, day/night lighting. Not FINAL.
- **Identity:** Near-future modern CBD: real proportions and believable planning, with a few signature forms (twisting towers, LED crown lighting, bronze-glass Veyra Spire). Nightshift underground culture lives beneath it in the parking levels.
- **Neighbours & boundary features:** Veyra Ring R1 on the west and north (Westgate, Sable and Foothill interchanges); Sable River on the east (Neon Mile across Meridian, Union Street and the Sable Drawbridge); Crown Bay waterfront on the south (Harbor Front Drive under the Harbor Viaduct).

### Boundary polygon (x, y metres; normalised in brackets)

`(3250,1720)[0.406,0.287] · (3750,1472)[0.469,0.245] · (3900,1540)[0.487,0.257] · (4200,1580)[0.525,0.263] · (4500,1590)[0.562,0.265] · (4800,1580)[0.600,0.263] · (4930,1570)[0.616,0.262] · (4970,1560)[0.621,0.260] · (4940,1900)[0.618,0.317] · (4950,2400)[0.619,0.400] · (4960,2900)[0.620,0.483] · (4935,3150)[0.617,0.525] · (4950,3645)[0.619,0.608] · (4350,3640)[0.544,0.607] · (4100,3630)[0.512,0.605] · (3900,3620)[0.487,0.603] · (3600,3600)[0.450,0.600] · (3250,3600)[0.406,0.600] · (3250,3475)[0.406,0.579] · (3250,3300)[0.406,0.550] · (3250,2450)[0.406,0.408]`

### Architecture, materials and environment

- Grid of ~150 m blocks. Arterial spines: Crown Avenue (N–S) and Meridian Boulevard (E–W) meet at Crown Plaza.
- Seven building typologies on the approved footprints: T1 glass podium tower, T2 art-deco stepped stone, T3 twisting tower (signature), T4 fin-slab office, T5 residential with balconies, T6 brick midrise with storefronts, T7 modern glass lowrise.
- Podiums of 4–5 floors with continuous storefront glazing and awnings; towers set back 4–8 m from podium edges; chamfered corners on the premium towers.
- Materials: blue/silver/green/bronze curtain wall (mullion grid 1.5 m × 3.8 m floors), limestone and light concrete, red/brown/cream brick, dark metal spandrels, granite plaza paving.
- Roofs: mechanical penthouses, HVAC units, water tanks on brick midrises, antennas and aircraft-warning lights on towers above 170 m.
- Streets: 3 m sidewalks with 0.15 m kerbs, gutters and drains every ~45 m, zebra crossings and stop lines at every downtown intersection, signal poles on opposite corners, street trees in pits every 15 m, benches and bus shelters on arterials.

### Atmosphere: day / golden hour / night

- Day: crisp blue sky, glass reflecting sky and neighbours, long shadows down the avenues in the afternoon.
- Golden hour: warm haze over Crown Bay, bronze Veyra Spire catching the sun, Blackridge ridges layered behind the skyline.
- Night: mixed warm/cool office windows (≈30 % lit), white LED crown bands, cool-white LED street lighting, warm storefront spill, red tail-light streams on the Harbor Viaduct, wet-road reflections after rain.

### Landmarks and key locations

- **LM-01 Veyra Spire** (Landmark) — 342 m glass-and-bronze tower, tallest in Veyra; aircraft-warning crown lights at night.
- **LM-02 Crown Plaza** (Landmark) — Granite pedestrian plaza with fountains at Crown Ave & Meridian Blvd; heart of downtown.
- **LM-03 Exchange Tower** (Landmark) — Stepped 1980s stone-and-glass office tower; Level B4 garage beneath.
- **LM-04 Harbor Viaduct** (Landmark) — Double-deck elevated section of the Veyra Ring along the downtown waterfront.
- **LM-05 Sable Drawbridge** (Landmark) — Steel bascule bridge carrying Harbor Front Drive over the river mouth.
- **LM-15 Kestrel Tunnel** (Landmark) — 600 m twin-bore ring tunnel under Kestrel Hill.
- **LM-27 Kestrel Heights** (Landmark) — Hillside luxury apartments above the river, facing the skyline.
- **GAR-03 Crown Motor Gallery** (Garage) — Glass-fronted luxury showroom and exotic dealership; player storage for high-value cars.
- **GAR-04 Level B4** (Garage) — Underground performance garage four levels beneath Exchange Tower. Nightshift HQ.
- **GAR-06 Kestrel Heights Penthouse Garage** (Garage) — High-end personal garage with car lift to a penthouse; city-view display wall.
- **GAR-09 The Redline Vault** (Garage) — Invitation-only garage beneath Veyra Spire; vaulted concrete, museum lighting. Redline Syndicate HQ.
- **DLR-02 Meridian Auto House** (Dealership) — New sports and performance cars; multi-brand glass showroom.
- **MEET-03 Crown Underground P3** (Car meet) — Underground car park meet beneath a downtown office block.
- **MEET-12 Redline Invitationals** (Car meet) — Invite-only showcase inside the Redline Vault.
- **VP-11 Crown Bay Promenade** (Scenic viewpoint) — Waterfront walk; Lantern Strait Bridge framed by the bay.

### Activities in this district

- Race start: RACE-01 Crown Sprint, RACE-02 Ring Run, RACE-16 Midnight Crown Circuit
- Speed trap: STP-01 Viaduct Camera, STP-04 Kestrel Tunnel Camera, STP-09 Meridian Camera
- Collectible area: COL-01 Downtown Rooftop Badges
- Stunt jump: JMP-02 Crown Ramp, JMP-03 Kestrel Crest
- Delivery hub: DEL-05 Downtown Courier Hub
- Photo challenge: PHO-01 Spire Reflection, PHO-09 Tunnel Lights

### 3D production specification

**Required 3D models and environment assets**

- Building typology prefabs T1–T7 (procedural rules + trim-sheet facades), 3 hero landmarks: Veyra Spire (342 m), Exchange Tower (165 m), Crown Motor Gallery.
- Harbor Viaduct segments, hammerhead piers, slip ramps, sign gantries; Kestrel Tunnel twin-bore portal; Crown, Meridian, Union and Regent river bridges; Sable Drawbridge (bascule).
- Crown Plaza set: granite paving, two-tier fountain with water jets, planters, benches.
- Street furniture: double-arm LED lamps, traffic signal poles, bus shelters, benches, bollards, bins, drains, street-name signs, parking meters.
- Traffic and parked vehicles (sedan, hatch, SUV, coupe, van, taxi, bus, box truck); underground car-park entrances (Level B4, Crown Underground P3).

**Suggested modular building and road pieces**

- Facade modules on a 3.0 m bay grid: curtain-wall panel 3.0 × 3.8, punched window 3.0 × 3.6, storefront 6.0 × 4.8, spandrel band, corner column, cornice/parapet 3 m, awning 4.2 m.
- Roof modules: parapet, flat slab 6 × 6, mechanical penthouse, HVAC unit, water tank, antenna, crown light band.
- Road kit: AR straight 10/20 m, AR 4-way junction (crossings + stop lines), CO 4-way and T, sidewalk 10 m with kerb, viaduct span 30 m, bridge deck 20 m, pier.

**Scale and proportions**

- Floor heights: office 3.8–4.2 m, residential 3.1 m, retail ground floor 4.8 m. Tower heights 12–260 m (landmarks 165 and 342 m).
- Arterial carriageway 20 m (2+2 + turn lane), local street 11 m (1+1 + parking), sidewalks 3 m; Harbor Viaduct deck 32 m at +10.5 m.
- Typical block 150 × 150 m between street centrelines; buildings set 13 m back from centreline (kerb + sidewalk).
- Blockout content: 282 building footprints (heights 14–342 m).

**Lighting and material specification**

- Unity URP: baked GI for static city geometry, light probes for vehicles, one reflection probe per block plus a box-projected probe per avenue.
- Facades: single trim-sheet atlas (2048²) + procedural window shader (window grid from world position, randomised lit interiors at night). Reference GLSL in 09_ART_PASS_BENCHMARK/assets.
- Glass: smoothness 0.9–0.95, metallic 0.6–0.8, tinted per tower; stone: smoothness 0.25; asphalt: smoothness 0.1 dry / 0.75 wet with puddle mask.
- Night: emissive window atlas, crown-light emissive bands, light-cookie projection for street-light pools (no per-lamp realtime lights on mobile), bloom threshold 0.8.

**Mobile (Android) performance budget for this district**

- Budget per 1 km streaming cell (C4/D4/C5/D5): ≤ 350 draw calls after batching/instancing, ≤ 1.2 M visible triangles on high tier, ≤ 600 k on mid tier.
- LOD chain per tower: LOD0 (facade modules) < 150 m, LOD1 merged shell with baked facade texture < 600 m, LOD2 impostor/billboard beyond; HLOD for whole blocks beyond 1 km.
- Occlusion culling is essential here (street canyons); bake Unity occlusion per cell. GPU-instanced traffic with 3 LODs (900 / 300 / 60 tris).
- Texture memory ≤ 96 MB per cell (ASTC 6×6), shared facade atlases across all typologies.

- Current blockout file `blockout_D1_crown_downtown.glb`: 446,504 triangles, 19.0 MB, 469 vegetation instances and 1020 street-furniture items placed (CSV in `08_3D_ASSETS/placement_data`).

**Reference images for this district:** `07_DISTRICT_3D_REFERENCE/1_crown_downtown/` (aerial, wide, street-level blockout renders, road layout, landmark sheet) and `09_ART_PASS_BENCHMARK/renders/`.


---

## 2. Azure Coast

*A premium seafront of palms, villas, beaches and the sunset coastal expressway.*

- **Area:** 2.0 km² of land · **Elevation:** 1–136 m · **Map cells:** A5, B5, C5, C6, D5, D6
- **Colour code:** #1E9EA6 · **Status:** BLOCKOUT (layout, scale, road geometry final-ish; no art pass yet).
- **Identity:** Mediterranean-meets-Pacific coastal luxury: white stucco villas, timber decks, marina glamour, the Lantern Point lighthouse and the C1 sunset drive.
- **Neighbours & boundary features:** Calder Sea to the south; Westhaven along the inland edge (Coral, Palm and Dune beach-access streets); Downtown at Harbor Interchange/Marina Gate; C1 continues east over the Lantern Strait Bridge to Ironport.

### Boundary polygon (x, y metres; normalised in brackets)

`(0,1700)[0.000,0.283] · (600,1480)[0.075,0.247] · (1300,1400)[0.163,0.233] · (2000,1330)[0.250,0.222] · (2700,1360)[0.338,0.227] · (3250,1720)[0.406,0.287] · (3750,1472)[0.469,0.245] · (3700,1450)[0.463,0.242] · (3600,1300)[0.450,0.217] · (3580,1100)[0.448,0.183] · (3640,900)[0.455,0.150] · (3760,700)[0.470,0.117] · (3900,520)[0.487,0.087] · (3960,380)[0.495,0.063] · (3900,300)[0.487,0.050] · (3780,330)[0.472,0.055] · (3550,450)[0.444,0.075] · (3300,600)[0.412,0.100] · (3000,740)[0.375,0.123] · (2700,840)[0.338,0.140] · (2350,900)[0.294,0.150] · (2000,920)[0.250,0.153] · (1650,960)[0.206,0.160] · (1300,1040)[0.163,0.173] · (1000,1140)[0.125,0.190] · (760,1210)[0.095,0.202] · (560,1180)[0.070,0.197] · (420,1080)[0.052,0.180] · (300,1050)[0.037,0.175] · (200,1180)[0.025,0.197] · (120,1450)[0.015,0.242] · (0,1620)[0.000,0.270]`

### Architecture, materials and environment

- White and sand stucco villas (2–3 storeys, flat roofs, glass balustrades, pools); timber beach clubs; marina clubhouse in glass and teak.
- Palm Promenade: royal palms every 22 m, timber boardwalk, warm-white coastal lamps.
- Lantern Point: white stone lighthouse on a 30 m bluff; Sorrel Cape cliffs at the west end.

### Atmosphere: day / golden hour / night

- Day: turquoise shallows, bright sand, sharp palm shadows.
- Golden hour (signature): sun sets over the sea to the west-south-west along the C1; warm rim light on cars.
- Night: warm coastal lamps, pier Ferris wheel lights, glamorous marina meets.

### Landmarks and key locations

- **LM-06 Lantern Point Lighthouse** (Landmark) — White stone lighthouse on the headland guarding the Lantern Strait.
- **LM-07 Lantern Strait Bridge** (Landmark) — 1.27 km cable-stayed bridge, two 140 m A-frame pylons; carries C1 across the bay mouth.
- **LM-08 Azure Pier** (Landmark) — Timber pleasure pier with a small Ferris wheel and food stalls.
- **LM-09 Palm Promenade** (Landmark) — 1.5 km seafront promenade of royal palms, cafes and beach clubs.
- **LM-10 Azure Marina** (Landmark) — Yacht marina on Crown Bay's west shore; clubhouse, berths, seafood restaurants.
- **LM-22 Sorrel Cape** (Landmark) — Wind-swept headland at the west end of the coast; 60 m cliffs.
- **GAR-08 Marina Concourse Club** (Garage) — Glass clubhouse garage by the marina. Coastline Society HQ.
- **DLR-05 Coastline Classics** (Dealership) — Restored classic GTs and convertibles under a palm-shaded canopy.
- **MEET-05 Marina Concourse** (Car meet) — Sunday-evening luxury concours by the yachts.
- **MEET-08 Sunset Beach Lot** (Car meet) — Beachfront lot for casual multiplayer meets at golden hour.
- **VP-03 Lighthouse Lookout** (Scenic viewpoint) — Bridge and skyline across the strait from the lighthouse car park.
- **VP-04 Sorrel Cape Overlook** (Scenic viewpoint) — Western cliffs; sun sets over the open sea.
- **RST-05 Lantern Boathouse** (Hidden garage / barn find) — Boathouse with a seized roadster on blocks.

### Activities in this district

- Race start: RACE-03 Azure Coast Sprint, RACE-04 Strait Dash, RACE-12 Grand Loop, RACE-14 Sorrel Ridge Run
- Speed trap: STP-02 Strait Camera, STP-03 Palm Promenade Camera
- Speed zone: SPZ-02 Azure Mile Zone
- Collectible area: COL-05 Azure Beach Shells
- Stunt jump: JMP-07 Dune Kicker
- Photo challenge: PHO-02 Bridge at Sunset, PHO-07 Golden Promenade

### 3D production specification

**Required 3D models and environment assets**

- Villa prefabs (3 variants), beach club pavilion, Azure Pier with Ferris wheel, marina berths and yachts, lighthouse, lifeguard towers, parasols/loungers, palm trees (3 variants), dune grass clumps.
- Lantern Strait Bridge (cable-stayed, 2 A-frame pylons 140 m) shared with Ironport.

**Suggested modular building and road pieces**

- Stucco wall 3 m, glass balustrade 3 m, flat roof edge, timber deck 4 × 4, pool module, boardwalk 5 m, seawall 10 m, beach-access stairs.
- Road kit: EX straight/curves with palm-planted verges; signalised T-junction.

**Scale and proportions**

- C1 carriageway 24 m (2+2, divided) with 3 m verges; villas 18–24 m wide; pier 190 m long, Ferris wheel Ø 36 m.
- Blockout content: 82 building footprints (heights 5–28 m).

**Lighting and material specification**

- Sand: smoothness 0.2, wet sand band smoothness 0.6; sea: planar reflection (low res) or SSR substitute + normal-map waves; stucco smoothness 0.15.

**Mobile (Android) performance budget for this district**

- Open sightlines: rely on LOD/HLOD and fog distance (900 m) rather than occlusion; impostor palms beyond 150 m; ≤ 250 draw calls per cell.

- Current blockout file `blockout_D2_azure_coast.glb`: 169,324 triangles, 6.2 MB, 1,564 vegetation instances and 141 street-furniture items placed (CSV in `08_3D_ASSETS/placement_data`).

**Reference images for this district:** `07_DISTRICT_3D_REFERENCE/2_azure_coast/` (aerial, wide, street-level blockout renders, road layout, landmark sheet).


---

## 3. Ironport Industrial Zone

*A working harbour of gantry cranes, container canyons, refinery flare stacks and the birthplace of the underground scene.*

- **Area:** 3.8 km² of land · **Elevation:** 1–14 m · **Map cells:** F5, F6, G5, G6, H5, H6
- **Colour code:** #C8702E · **Status:** BLOCKOUT.
- **Identity:** Gritty, mechanical, sodium-lit. Iron Wolves turf: drag runs on Berth Road, meets under the Dockside Interchange flyovers.
- **Neighbours & boundary features:** Crown Bay (west), Calder Sea (south and east), the R1 Harbor Viaduct (north-west, Neon Mile beyond) and C1 east of Dockside Interchange (Falcon Airfield beyond).

### Boundary polygon (x, y metres; normalised in brackets)

`(4952,1700)[0.619,0.283] · (4970,1560)[0.621,0.260] · (5010,1560)[0.626,0.260] · (5150,1500)[0.644,0.250] · (5300,1380)[0.662,0.230] · (5420,1200)[0.677,0.200] · (5480,980)[0.685,0.163] · (5440,760)[0.680,0.127] · (5330,560)[0.666,0.093] · (5180,400)[0.647,0.067] · (5120,300)[0.640,0.050] · (5200,230)[0.650,0.038] · (5400,200)[0.675,0.033] · (7050,200)[0.881,0.033] · (7250,260)[0.906,0.043] · (7450,420)[0.931,0.070] · (7620,700)[0.953,0.117] · (7730,1050)[0.966,0.175] · (7780,1500)[0.973,0.250] · (7790,2020)[0.974,0.337] · (7480,1980)[0.935,0.330] · (7100,1850)[0.887,0.308] · (6500,1800)[0.812,0.300] · (6150,1760)[0.769,0.293] · (6000,1750)[0.750,0.292] · (5500,1720)[0.688,0.287]`

### Architecture, materials and environment

- Container terminal: stacks 1–4 high of ISO 40 ft containers, 8 ship-to-shore gantry cranes (78 m) on the quay.
- Warehouses (70 × 40 m, 11–16 m tall, corrugated steel, roller doors), Foundry 9 (brick with sawtooth roof).
- Refinery: tank farm (Ø 28–44 m), process columns 45–70 m, pipe racks, flare stack; freight rail yard.

### Atmosphere: day / golden hour / night

- Day: hazy industrial light, rust and paint-chip colours.
- Night: orange high-mast sodium lighting, refinery lit like a city, crane warning lights, steam and flare glow.

### Landmarks and key locations

- **LM-11 Gantry Container Terminal** (Landmark) — Six-berth container terminal with eight ship-to-shore gantry cranes.
- **LM-12 Ironport Refinery** (Landmark) — Flare stacks, cracking towers and pipe racks; lit like a city at night.
- **LM-23 Ironport Freight Yard** (Landmark) — Rail marshalling yard with gantry container loaders.
- **GAR-02 Foundry 9** (Garage) — Industrial tuning workshop in a former steel foundry; dyno cell, fabrication bay. Iron Wolves HQ.
- **DLR-04 Ironport Salvage & Classics** (Dealership) — Project cars and parts cars in a fenced yard; cheap restoration bases.
- **MEET-01 The Undercroft** (Car meet) — Meet beneath the Dockside Interchange flyovers; sodium light, concrete pillars. Iron Wolves + Nightshift.
- **MEET-02 Warehouse Row Meet** (Car meet) — Open roller doors, work lights, engine bays on show.
- **RST-06 Container 7731** (Hidden garage / barn find) — Unclaimed container with a crated import.

### Activities in this district

- Race start: RACE-06 Ironport Industrial Circuit
- Drift zone: DFT-03 Warehouse Row Drift Yard
- Drag strip: DRG-02 Berth Road Straight
- Collectible area: COL-02 Ironport Container Tags
- Stunt jump: JMP-01 Container Ramp
- Delivery hub: DEL-02 Ironport Freight Office
- Photo challenge: PHO-04 Gantry Silhouettes

### 3D production specification

**Required 3D models and environment assets**

- STS gantry crane, straddle carrier, reach stacker, container (20/40 ft, 8 liveries), tank, process column, pipe rack segments, flare stack, warehouse kit, rail tracks and wagons, chain-link fence, jersey barriers, high-mast lamps.

**Suggested modular building and road pieces**

- Warehouse bay 8 × 10 m, roller door 6 × 6, sawtooth roof 15 m, pipe rack 10 m, quay edge 10 m, rail 25 m straight/curve, SV road kit 12 m.

**Scale and proportions**

- Quay apron 60 m deep; Berth Road straight 1.4 km (drag strip DRG-02 = 800 m); container rows 2.44 m wide with 1 m gaps.
- Blockout content: 122 building footprints (heights 8–66 m).

**Lighting and material specification**

- Painted steel smoothness 0.35 with edge-wear mask; concrete yard smoothness 0.2; sodium light cookies 2200 K.

**Mobile (Android) performance budget for this district**

- Containers are heavily instanced (GPU instancing, 12-tri LOD1); merge stacks into combined meshes per block for distant LOD; ≤ 300 draw calls per cell.

- Current blockout file `blockout_D3_ironport.glb`: 328,368 triangles, 14.2 MB, 0 vegetation instances and 138 street-furniture items placed (CSV in `08_3D_ASSETS/placement_data`).

**Reference images for this district:** `07_DISTRICT_3D_REFERENCE/3_ironport/` (aerial, wide, street-level blockout renders, road layout, landmark sheet).


---

## 4. Blackridge Mountains

*Forested ridges above the city: touge passes, switchbacks, tunnels, viaducts and the best views in Veyra.*

- **Area:** 12.1 km² of land · **Elevation:** 13–470 m · **Map cells:** C1, C2, C3, D1, D2, D3, E1, E2, E3, F1, F2, F3, G1, G2, G3, H1, H2
- **Colour code:** #2F5E4E · **Status:** ART PASS (prototype): vegetation variety, rock scatter, guardrails/delineators, terrain shading, atmosphere. Not FINAL.
- **Identity:** Pacific-northwest-style conifer forest with granite outcrops; Apex Collective's midnight touge culture at Halcyon Pass.
- **Neighbours & boundary features:** R1 north side and Kestrel Hill (south), the Sable River (south-west), Redwood Valley (west, along the Hollow Creek watershed), Falcon Airfield (south-east) and the sea cliffs (east).

### Boundary polygon (x, y metres; normalised in brackets)

`(3250,3600)[0.406,0.600] · (3600,3600)[0.450,0.600] · (3900,3620)[0.487,0.603] · (4100,3630)[0.512,0.605] · (4350,3640)[0.544,0.607] · (4950,3645)[0.619,0.608] · (5300,3620)[0.662,0.603] · (5700,3550)[0.713,0.592] · (6050,3400)[0.756,0.567] · (6600,3500)[0.825,0.583] · (6600,4200)[0.825,0.700] · (7720,4200)[0.965,0.700] · (7760,4800)[0.970,0.800] · (7880,5300)[0.985,0.883] · (8000,5550)[1.000,0.925] · (8000,6000)[1.000,1.000] · (2650,6000)[0.331,1.000] · (2550,5200)[0.319,0.867] · (2450,4600)[0.306,0.767] · (2350,4000)[0.294,0.667] · (2800,3700)[0.350,0.617] · (3250,3475)[0.406,0.579]`

### Architecture, materials and environment

- Sparse: Apex Shed (timber and concrete workshop), Pinecrest abandoned service station, Hollow Pines timber chalets, broadcast masts on the summit.
- Civil structures: Blackridge Tunnel (1.22 km), Corvo Tunnel (0.82 km), Corvo Creek and Highline viaducts, cut slopes with rock faces.

### Atmosphere: day / golden hour / night

- Day: dappled forest light, distant layered ridges in aerial perspective.
- Golden hour: low sun through conifers, skyline glowing at the Skyline Overlook.
- Night: no street lighting on mountain roads; reflective delineators and guardrails catch headlights; fog in Hollow Creek gorge.

### Landmarks and key locations

- **LM-14 Blackridge Summit Masts** (Landmark) — Red-and-white broadcast masts on the 812 m summit, visible citywide.
- **LM-16 Blackridge Tunnel** (Landmark) — 1.22 km Ridgeline Highway tunnel through the summit's north spur; the 820 m Corvo Tunnel lies further east beyond the Corvo Creek Viaduct.
- **LM-17 Highline Viaduct** (Landmark) — Curved concrete viaduct 70 m above Hollow Creek gorge.
- **LM-19 Hollow Pines Village** (Landmark) — Mountain village of timber chalets along the Ridgeline Highway.
- **GAR-05 The Apex Shed** (Garage) — Timber-and-concrete drift workshop at the foot of Halcyon Pass; alignment rack, tyre wall. Apex Collective HQ.
- **GAR-11 Hollow Pines Service** (Garage) — Mountain mechanic and fuel stop; tyre and brake service between runs.
- **MEET-06 Halcyon Summit Lot** (Car meet) — Gravel lot at the pass top; midnight Apex Collective gathering.
- **VP-01 Skyline Overlook** (Scenic viewpoint) — Signature view: downtown skyline, Crown Bay, Lantern Strait Bridge and the Calder Sea.
- **VP-02 Blackridge Summit Overlook** (Scenic viewpoint) — 360-degree view from 812 m; whole map visible on clear days.
- **VP-05 Corvo Saddle Lookout** (Scenic viewpoint) — View east over the airfield and the sea cliffs.
- **VP-06 Kestrel Hill Park** (Scenic viewpoint) — Small park on Kestrel Hill above the tunnel; downtown rooftops.
- **VP-09 Highline Vista** (Scenic viewpoint) — Pull-off at the viaduct's west end over Hollow Creek gorge.
- **VP-10 Cliffside Bend** (Scenic viewpoint) — C1 pull-off 200 m above the sea.
- **RST-03 Pinecrest Service Station** (Hidden garage / barn find) — Abandoned 1960s gas station; a classic in the service bay.

### Activities in this district

- Race start: RACE-07 Halcyon Ascent, RACE-08 Corvo Descent, RACE-09 Blackridge Traverse
- Drift zone: DFT-01 Corvo Switchbacks, DFT-02 Halcyon Hairpins, DFT-05 Hollow Pines Logging Yard
- Drag strip: DRG-03 Blackridge Tunnel Roll
- Speed trap: STP-06 Blackridge Tunnel Camera
- Speed zone: SPZ-01 Ring North Zone, SPZ-04 Highline Zone
- Time trial / test: TT-01 Halcyon Time Trial
- Collectible area: COL-03 Blackridge Trail Markers, COL-09 Corvo Ridge Relics
- Stunt jump: JMP-04 Halcyon Cutback
- Photo challenge: PHO-10 Cliffside Coast

### 3D production specification

**Required 3D models and environment assets**

- Conifers (4 variants + dead snags), broadleaf at low elevation, bushes, rocks/boulders (3 sizes), cliff faces, guardrails (W-beam), delineator posts, tunnel portals, viaduct spans/piers, warning signs, chevrons, summit lot props.

**Suggested modular building and road pieces**

- MT road kit (7.5 m): straights, R30/R60 curves, R14 hairpin; guardrail 4 m + terminal; retaining wall 10 m; rock-cut wall 10 m; culvert; HW tunnel section 20 m + portal.

**Scale and proportions**

- Halcyon Pass Road 4.39 km climbing 30 → 324 m (max 15 %); Corvo Ridge Road 3.79 km; summit 470 m; conifers 14–20 m tall.
- Blockout content: 28 building footprints (heights 6–120 m).

**Lighting and material specification**

- Foliage: alpha-tested cards on a mesh trunk at LOD0, translucency 0.3; rock: triplanar granite; terrain: 4-layer splat (grass, forest floor, rock, dirt).

**Mobile (Android) performance budget for this district**

- Vegetation is the cost driver: GPU instancing, LOD0 < 60 m, LOD1 < 250 m, impostors beyond, density culling by distance; terrain pixel error 8; ≤ 220 draw calls per cell.

- Current blockout file `blockout_D4_blackridge.glb`: 306,616 triangles, 11.1 MB, 46,357 vegetation instances and 32 street-furniture items placed (CSV in `08_3D_ASSETS/placement_data`).

**Reference images for this district:** `07_DISTRICT_3D_REFERENCE/4_blackridge/` (aerial, wide, street-level blockout renders, road layout, landmark sheet) and `09_ART_PASS_BENCHMARK/renders/`.


---

## 5. Westhaven Suburbs

*Everyday suburbs where the journey starts: Juniper Lane, the first car and the first race.*

- **Area:** 6.0 km² of land · **Elevation:** 7–103 m · **Map cells:** A3, A4, A5, B3, B4, B5, C3, C4, C5, D3, D4, D5
- **Colour code:** #C49A2C · **Status:** BLOCKOUT.
- **Identity:** Believable mid-century and modern suburbia: gabled houses, front lawns, driveways, a shopping plaza, a community garage and the used-car lot.
- **Neighbours & boundary features:** Azure Coast (south), Veyra Ring (east), the Sable River and Hillcrest Road (north), Sorrel Hills (west).

### Boundary polygon (x, y metres; normalised in brackets)

`(600,1480)[0.075,0.247] · (1300,1400)[0.163,0.233] · (2000,1330)[0.250,0.222] · (2700,1360)[0.338,0.227] · (3250,1720)[0.406,0.287] · (3250,2450)[0.406,0.408] · (3250,3300)[0.406,0.550] · (3250,3475)[0.406,0.579] · (2800,3700)[0.350,0.617] · (600,3720)[0.075,0.620]`

### Architecture, materials and environment

- ~1,900 detached houses (12–14 × 10–11 m, 6–8.5 m tall) on gently curving streets; 4-storey apartments on Westhaven Avenue; Westhaven Plaza retail; Westgate Retail Park big-box.

### Atmosphere: day / golden hour / night

- Day: warm, lived-in, sprinklers and kids' bikes.
- Night: quiet amber porch lights, sparse LED street lights, TV glow in windows.

### Landmarks and key locations

- **LM-24 Westhaven Plaza** (Landmark) — Open-air neighbourhood shopping centre with a supermarket and cafes.
- **GAR-01 Juniper Lane Garage** (Garage) — Player's starter garage: single-bay brick lock-up behind a rented duplex. Spawn point.
- **GAR-07 Westhaven Community Garage** (Garage) — Shared DIY bays; restoration missions and parts swaps.
- **DLR-01 Second Gear Motors** (Dealership) — Used-car lot with bunting; the player's first car purchase.
- **MEET-09 Westgate Retail Park** (Car meet) — Big-box retail car park by the ring; default 10-20 player online meet.

### Activities in this district

- Race start: RACE-11 Westhaven Sprint, RACE-13 Riverside Sprint
- Speed trap: STP-07 Parkway Camera
- Time trial / test: TT-05 Ring Time Attack
- Collectible area: COL-08 Westhaven Yard Gnomes
- Delivery hub: DEL-01 Westhaven Depot

### 3D production specification

**Required 3D models and environment assets**

- House prefabs (6 variants: 1/2 storey, gable/hip roofs), garages, fences, mailboxes, driveways, trees, school, plaza stores, gas station, used-car lot props, the Juniper Lane starter garage interior.

**Suggested modular building and road pieces**

- House wall 3 m, gable/hip roof sets, garage door, porch, fence 3 m; CO road kit with driveway cut-outs; sidewalk with grass verge.

**Scale and proportions**

- Local streets 11 m; lots ~27 m wide; houses set 24 m from centreline.
- Blockout content: 1973 building footprints (heights 4–18 m).

**Lighting and material specification**

- Baked GI, small reflection probes; window emissive atlas at night.

**Mobile (Android) performance budget for this district**

- Houses are the main cost: 3-LOD prefabs (800/200/40 tris), aggressive instancing, HLOD per street block; ≤ 250 draw calls per cell.

- Current blockout file `blockout_D5_westhaven.glb`: 366,984 triangles, 16.2 MB, 4,613 vegetation instances and 728 street-furniture items placed (CSV in `08_3D_ASSETS/placement_data`).

**Reference images for this district:** `07_DISTRICT_3D_REFERENCE/5_westhaven/` (aerial, wide, street-level blockout renders, road layout, landmark sheet).


---

## 6. Neon Mile

*One mile of neon, cinemas, rooftop car parks and Friday-night meets.*

- **Area:** 2.0 km² of land · **Elevation:** 1–31 m · **Map cells:** F3, F4, F5, G3, G4
- **Colour code:** #B8458C · **Status:** BLOCKOUT (night identity defined; art pass pending).
- **Identity:** Converted riverside warehouse and hotel quarter: brick and painted signage by day, controlled neon by night (signage concentrated on the Mile, not everywhere).
- **Neighbours & boundary features:** Sable River (west, three bridges to Downtown), Veyra Ring on the north, east and south; Ironport beyond the Harbor Viaduct.

### Boundary polygon (x, y metres; normalised in brackets)

`(4950,3645)[0.619,0.608] · (5300,3620)[0.662,0.603] · (5700,3550)[0.713,0.592] · (6050,3400)[0.756,0.567] · (6060,3100)[0.757,0.517] · (6040,2700)[0.755,0.450] · (6020,2200)[0.752,0.367] · (6000,1750)[0.750,0.292] · (5500,1720)[0.688,0.287] · (4952,1700)[0.619,0.283] · (4940,1900)[0.618,0.317] · (4950,2400)[0.619,0.400] · (4960,2900)[0.620,0.483] · (4935,3150)[0.617,0.525]`

### Architecture, materials and environment

- Lumen Avenue (the Mile, 1.57 km diagonal) with the Starlight Theater vertical marquee, Grand Lumen Hotel (95 m), Vantage 9-level rooftop car park; brick midrises 12–48 m.

### Atmosphere: day / golden hour / night

- Night (primary): magenta/cyan/amber neon on wet asphalt, steam vents, crowds near the Lumen Cross meet.
- Day: faded painted signs, loading bays.

### Landmarks and key locations

- **LM-20 Starlight Theater** (Landmark) — Art-deco movie palace with a 30 m vertical marquee sign.
- **LM-21 Grand Lumen Hotel** (Landmark) — 28-storey hotel; rooftop pool bar overlooks the Mile.
- **DLR-03 Lumen Imports** (Dealership) — Tuner-friendly imports and modern turbo hatchbacks.
- **MEET-04 Vantage Rooftop** (Car meet) — Rooftop of a 9-level parking structure over the Mile; skyline backdrop.
- **MEET-10 Lumen Cross Meet** (Car meet) — Street meet in the closed-off plaza at Lumen Cross on Friday nights.

### Activities in this district

- Race start: RACE-05 Lumen Circuit
- Speed trap: STP-10 Mile Camera
- Collectible area: COL-06 Neon Mile Signs
- Stunt jump: JMP-09 Rooftop Gap
- Photo challenge: PHO-03 Neon Rain

### 3D production specification

**Required 3D models and environment assets**

- Neon sign kit (vertical, horizontal, marquee), theatre facade, hotel tower, parking structure with ramps, club entrances, food trucks, string lights, steam vents.

**Suggested modular building and road pieces**

- Brick warehouse wall 3 m, loading dock, fire escape, rooftop billboard, neon sign set; CO/AR road kit.

**Scale and proportions**

- The Mile 20 m carriageway; parking structure 72 × 46 m × 9 levels (3.0 m floor-to-floor).
- Blockout content: 119 building footprints (heights 13–95 m).

**Lighting and material specification**

- Emissive signage with bloom; wet-road puddle mask + planar reflection approximation; colored light cookies.

**Mobile (Android) performance budget for this district**

- Limit dynamic lights to 4 per view; neon is emissive + bloom, not real lights; ≤ 300 draw calls per cell.

- Current blockout file `blockout_D6_neon_mile.glb`: 281,740 triangles, 11.6 MB, 0 vegetation instances and 653 street-furniture items placed (CSV in `08_3D_ASSETS/placement_data`).

**Reference images for this district:** `07_DISTRICT_3D_REFERENCE/6_neon_mile/` (aerial, wide, street-level blockout renders, road layout, landmark sheet).


---

## 7. Falcon Airfield

*A decommissioned airfield turned motorsport venue: legal drags, top-speed runs and the biggest meets.*

- **Area:** 3.6 km² of land · **Elevation:** 2–76 m · **Map cells:** G2, G3, G4, G5, H2, H3, H4, H5
- **Colour code:** #66707E · **Status:** BLOCKOUT.
- **Identity:** Wide-open, wind-swept, motorsport-official: grandstands, timing boards, painted runway numbers, old hangars and a 1960s control tower.
- **Neighbours & boundary features:** C1 and Ironport to the south; Veyra Ring/Neon Mile to the west; Blackridge foothills to the north; the sea to the east.

### Boundary polygon (x, y metres; normalised in brackets)

`(6000,1750)[0.750,0.292] · (6150,1760)[0.769,0.293] · (6500,1800)[0.812,0.300] · (7100,1850)[0.887,0.308] · (7480,1980)[0.935,0.330] · (7790,2020)[0.974,0.337] · (7790,2100)[0.974,0.350] · (7760,2800)[0.970,0.467] · (7780,3500)[0.973,0.583] · (7720,4200)[0.965,0.700] · (6600,4200)[0.825,0.700] · (6600,3500)[0.825,0.583] · (6050,3400)[0.756,0.567] · (6060,3100)[0.757,0.517] · (6040,2700)[0.755,0.450] · (6020,2200)[0.752,0.367]`

### Architecture, materials and environment

- Runway 18/36 (1.93 km × 45 m), Taxiway Alpha, apron, six barrel-roof hangars (62 × 74 m), control tower (34 m), old terminal, logistics sheds.

### Atmosphere: day / golden hour / night

- Day: heat haze over the runway, wind socks.
- Night: event floodlights, staging-tree lights, grandstand lighting.

### Landmarks and key locations

- **LM-13 Falcon Control Tower** (Landmark) — 1960s concrete control tower, now race control for airfield events.
- **LM-28 Falcon Grandstands** (Landmark) — Steel-frame grandstands (4,000 seats) beside the dragway start line.
- **GAR-10 Falcon Hangar 3** (Garage) — Event garage and pit lane for airfield competitions.
- **MEET-07 Falcon Field Meet** (Car meet) — Old terminal car park; monthly community meet, multiplayer hub (20 players).
- **RST-04 Falcon Hangar 6** (Hidden garage / barn find) — Sealed hangar holding a forgotten endurance racer.

### Activities in this district

- Race start: RACE-15 Cliffside Run
- Drift zone: DFT-04 Falcon Apron Drift Pad
- Drag strip: DRG-01 Falcon Dragway
- Speed trap: STP-05 Falcon Coast Camera
- Speed zone: SPZ-03 Airfield Coast Zone
- Time trial / test: TT-02 Falcon Handling Course, TT-03 Falcon Top Speed Run, TT-04 Falcon Acceleration Test
- Collectible area: COL-07 Falcon Hangar Plates
- Stunt jump: JMP-06 Hangar Roof
- Delivery hub: DEL-03 Falcon Logistics Park
- Photo challenge: PHO-06 Runway Horizon

### 3D production specification

**Required 3D models and environment assets**

- Grandstand modules, drag staging tree, timing board, cones, barriers, tyre walls, windsock, runway lights, hangars, control tower, event marquees.

**Suggested modular building and road pieces**

- Runway 50 m section with markings, taxiway 25 m, apron slab 20 × 20, grandstand 10 m, fence 3 m.

**Scale and proportions**

- Falcon Dragway 402 m + 600 m shutdown; top-speed gate at 1.5 km.
- Blockout content: 11 building footprints (heights 10–34 m).

**Lighting and material specification**

- Concrete runway smoothness 0.25 with rubber skid marks decals; floodlight cookies at night.

**Mobile (Android) performance budget for this district**

- Low geometry, long views: invest budget in terrain/backdrop LOD and crowd impostors; ≤ 180 draw calls per cell.

- Current blockout file `blockout_D7_falcon_airfield.glb`: 229,640 triangles, 8.0 MB, 7 vegetation instances and 65 street-furniture items placed (CSV in `08_3D_ASSETS/placement_data`).

**Reference images for this district:** `07_DISTRICT_3D_REFERENCE/7_falcon_airfield/` (aerial, wide, street-level blockout renders, road layout, landmark sheet).


---

## 8. Redwood Valley

*Farms, forests, Mirror Lake and the gravel roads of Millbrook.*

- **Area:** 7.1 km² of land · **Elevation:** 61–284 m · **Map cells:** A1, A2, A3, A4, A5, B1, B2, B3, C1, C2, C3
- **Colour code:** #7C8B2E · **Status:** BLOCKOUT.
- **Identity:** Pastoral contrast to the city: patchwork fields, red barns, a brick market town with a clock tower, a glacial lake and rally gravel.
- **Neighbours & boundary features:** Sorrel Hills (west), Westhaven (south), Sable River and the Hollow Creek watershed (east), northern hills (map edge).

### Boundary polygon (x, y metres; normalised in brackets)

`(0,1700)[0.000,0.283] · (600,1480)[0.075,0.247] · (600,3720)[0.075,0.620] · (2800,3700)[0.350,0.617] · (2350,4000)[0.294,0.667] · (2450,4600)[0.306,0.767] · (2550,5200)[0.319,0.867] · (2650,6000)[0.331,1.000] · (0,6000)[0.000,1.000]`

### Architecture, materials and environment

- Millbrook: 2-storey brick and clapboard main street, clock tower, diner; farmsteads (barn + house + silo); Old Timber Mill; Hartley Barn.

### Atmosphere: day / golden hour / night

- Morning mist on Mirror Lake, golden fields at sunset, dust plumes on Old Farm Road.

### Landmarks and key locations

- **LM-18 Mirror Lake** (Landmark) — Glacial lake in Redwood Valley; mirror-still at dawn.
- **LM-25 Sable Falls** (Landmark) — 12 m waterfall where the Sable River leaves Mirror Lake's basin.
- **LM-26 Millbrook Clock Tower** (Landmark) — Brick clock tower at the centre of Millbrook town.
- **MEET-11 Millbrook Diner** (Car meet) — Chrome-trimmed roadside diner; countryside cruise stop.
- **VP-07 Sorrel Hills Lookout** (Scenic viewpoint) — Hilltop over Westhaven and the coast.
- **VP-08 Mirror Lake Jetty** (Scenic viewpoint) — Wooden jetty; lake, forest and Blackridge reflections.
- **RST-01 Hartley Barn** (Hidden garage / barn find) — Hidden barn; a dust-covered 1970s coupe under a tarp.
- **RST-02 Old Timber Mill** (Hidden garage / barn find) — Abandoned sawmill; a rally car left in the drying shed.

### Activities in this district

- Race start: RACE-10 Redwood Rally Stage
- Speed trap: STP-08 Riverside Camera
- Speed zone: SPZ-05 Parkway Zone
- Time trial / test: TT-06 Redwood Gravel Trial
- Collectible area: COL-04 Redwood Barn Signs, COL-10 Sorrel Hills Cairns
- Stunt jump: JMP-05 Sable River Leap, JMP-08 Hayloft Hop
- Delivery hub: DEL-04 Millbrook Farm Co-op
- Photo challenge: PHO-05 Mirror Lake Dawn, PHO-08 Millbrook Bridge

### 3D production specification

**Required 3D models and environment assets**

- Barn, farmhouse, silo, hay bales, fences, tractors, covered/stone bridge, mill, lake jetty, crops (impostor rows), deciduous and conifer trees.

**Suggested modular building and road pieces**

- Gravel road kit 5.5 m, rural road 7 m, fence 3 m, field edge hedgerow, stone wall.

**Scale and proportions**

- Old Farm Road 1.8 km gravel; Mirror Lake ~720 × 500 m at 150 m elevation.
- Blockout content: 91 building footprints (heights 6–24 m).

**Lighting and material specification**

- Terrain splat with farmland layer; dust particles; lake planar reflection substitute.

**Mobile (Android) performance budget for this district**

- Large open terrain: terrain LOD + impostor forests; ≤ 150 draw calls per cell.

- Current blockout file `blockout_D8_redwood_valley.glb`: 175,884 triangles, 6.1 MB, 14,489 vegetation instances and 0 street-furniture items placed (CSV in `08_3D_ASSETS/placement_data`).

**Reference images for this district:** `07_DISTRICT_3D_REFERENCE/8_redwood_valley/` (aerial, wide, street-level blockout renders, road layout, landmark sheet).
