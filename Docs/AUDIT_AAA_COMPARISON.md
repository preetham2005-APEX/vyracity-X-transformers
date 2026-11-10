# AUDIT_AAA_COMPARISON: Veyra City against GTA V, Cyberpunk 2077 and Asphalt 9/Legends

**Date:** 2026-11-10
**Code state:** HEAD `2f9a20b`, plus the uncommitted working tree. Two pieces of work are in progress in other sessions: the F1 material architecture and the rebadging of real-brand cars.
**Type:** read-only audit. No game code, asset or setting was changed.
**Builds on:**
- `Docs/AUDIT.md` (S2 baseline)
- `Docs/VISUAL_BENCHMARK_REPORT.md` (visual research, 2026-11-09)
- `Docs/BUG_TRACKER.md`
- `Docs/VEYRA_CLAUDE_CODE_PROMPTS.md` (the playbook cards and Appendix B and C budgets)

**How to read this:**
- **[V]** means verified by reading code, assets or logs, or by measuring.
- **[I]** means inference.
- Paths are relative to `E:\VeyraCity`.
- The comparisons with GTA V, Cyberpunk 2077 and Asphalt are about technique and quality bar only. Every name, asset, layout and UI in Veyra stays original (CLAUDE.md).
- New defects found by this audit are numbered BUG-066 to BUG-090 and SEC-003, and are entered in `Docs/BUG_TRACKER.md`.

---

## 0. Corrections to the brief

Several assumptions in the audit request don't match the project. The rest of this document uses the actual state.

| Brief says | Actual state | Evidence |
|---|---|---|
| "Only 1 active visual car (ArdenKite) with procedural placeholders" | **26 drivable entries.** Kite stock and Kite street share the Blender-authored Kite model. 5 catalogue cars (strata, volkhart, hammerhead, sovereign, vento) are still built at runtime by `ProceduralCar`. 19 are downloaded models. Traffic uses 19 models. | `Data/GameDatabase.asset`, `Gameplay/GameFlow.cs:488-495`, `Screenshots/2026-11-10_A8/new_cars_ingame.png` [V] |
| "Batch-import all 32 downloaded vehicles (Bugatti Tourbillon, Ferrari SF90 XX, Porsche GT3 RS, Lamborghini Terzo…)" | **20 of the 32 are fictional CC-BY models, and they are already in the game** (the generic pack is split into 10 traffic cars). **12 are real brands.** 5 are CC-BY and processed in `ArtSource/Vehicles` only (uncommitted). **7 cannot ship in a commercial game:** Bugatti Tourbillon and Bolide (CC-BY-NC-SA), Bugatti EB110 (CC-BY-NC), Porsche 911 GT3 RS (CC-BY-NC-SA), Lamborghini Terzo Millennio (CC-BY-NC), BMW M6 GT3 (CC-BY-NC-ND) and McLaren P1 (Sketchfab Standard). All real-brand cars also conflict with CLAUDE.md: "Real-brand cars … are never used". | `license.txt` inside each zip; `*.licence.json` sidecars [V] |
| "23 city packs can replace the procedural box generators across all 8 districts" | The catalogue lists 23 entries: 13 CC-BY, 7 Sketchfab Standard, 1 CC-BY-SA, 1 with no licence (Isparta) and 1 ripped GTA map (refused). **None of them matches Veyra's road network, and the Veyra data decides every position** (CLAUDE.md). They can only serve as a kitbash parts library, and they cover D1 and D6 well, part of D5, and the rest hardly at all (§3.D). | `Docs/AUDIT.md` §9, `ArtSource/Downloads/FullMapModels/*.zip` [V] |
| `TireFriction.cs`, `Suspension.cs` | These files don't exist. The tyre model is `VehiclePhysics/TyreModel.cs`; the suspension is inline in `VehicleController.Simulate` (`:291-353`). | [V] |
| "BUG-005: mountain race Halcyon Ascent blocked ~200 m in" | That bug is **BUG-026**. BUG-005 is "7 locations outside their district polygon". | `Docs/BUG_TRACKER.md` [V] |
| "BUG-002: Shader.Find null reference in player builds" | It did not reproduce in any of the 5 V0 player runs (`shaderNull=0`). In the uncommitted F1 working tree, runtime code has **0** `Shader.Find` calls and `Art/Shaders/VC_Variants.shadervariants` (51 variants) now exists. The fix is on disk but not committed or proven in a build yet. | `Logs/V0/*/verify.txt`; grep on the working tree [V] |
| "CityBuilder.cs mesh ribbons" | **There is no ribbon generator.** Roads are the design package's blockout GLBs, converted 1:1 by `Tools/World/build_world_data.py:368-411` and bucketed by `CityBuilder.BuildMeshes` (`:634-759`). | [V] |
| `DefaultVolumeProfile.asset` holds the post-processing | The game's post is `World/Generated/PostProfile.asset`. `Assets/Settings/DefaultVolumeProfile.asset` contains leftover test components and a missing-script entry (`:977`). | [V] |
| Target GPU "RTX 4070" | Every measurement so far ran on an **RTX 4070 Laptop GPU** with VSync on (frame times quantised to 4.17 ms steps). The desktop target has more headroom, but it hasn't been measured. | `Logs/stats.json` [V] |

---

## 0b. Status update (2026-11-10, 22:00, HEAD `522a05a`)

The other sessions completed many cards after this audit was written in the morning. The analysis below describes the morning state. This table shows where each Phase 0 item and each new finding stands now. The full statuses are in `Docs/BUG_TRACKER.md`.

**Phase 0 progress:**

| Roadmap item | Now |
|---|---|
| 0.1 SEC-003 token | Fallback removed from `Tools/download_fancy_cars.py`. **User:** revoke the token |
| 0.2 F1 | Done: F1 `df5da7c`, F2a/b/c `258b3b2`/`3eb71f1`/`1c2198f`, F3 `eaa4fb2` (closes BUG-002, 039, 046, 048, 076) |
| 0.3 B1 | Done (`c289be8`): the 7 forbidden cars, the GTA archives, the Sketchfab Standard kits, Isparta and Plaza are in `ArtSource/Downloads/_Quarantine` |
| 0.4 Drivability | C1 (`cabe3d2`), C2 (`cbdaf79`, BUG-001) and C3 (`0a0b4d5`) are done. **V1 (`0562881`) still fails on driving rules:** 7 walls remain (BUG-108) |
| 0.5 Measurement | BUG-090 still open: GPU time and draw calls aren't recorded. BUG-067 has an uncommitted fix (below) |
| 0.6 Decisions | Real-brand cars: the user allowed the 5 CC-BY rebadges, but `CLAUDE.md:86` still forbids them. Arcade grip against the spec: not recorded |

**Phase 1–4 work started since:** B2/B3 kitbash ingestion (80 Full_Gameready pieces imported), L1 (MSAA off, STP), and L2–L5 (partial). The vehicle session's round 2 adds a player-car reflection probe, damage and street trees.

**New findings (SEC-003, BUG-066 to BUG-090):**

| Status | IDs |
|---|---|
| Fixed in `e35e8ff` (vehicle session) | BUG-077, 078, 079, 080, 081, 082, 083, 084, 085, 086, 088, 089. The audit re-checked 077, 078, 081, 082, 084, 085 and 088 in code |
| Fixed in Roadmap A1 (2026-11-10/11) | BUG-067 `bea9179`, BUG-090 `52e20a7`, BUG-056 `e4aa454`, BUG-057 `081d53c`, BUG-075 `214d6ef`, BUG-108 `bd134ae` (drive surface; V1 driving routes pass in Dev Mono). New: BUG-111 (render meshes still the blockout, for B1) |
| Partly fixed | BUG-068 (car probe only; `realtimeReflectionProbes` still 0), BUG-073 (L4 fades), BUG-074 (L1: STP; cameras still request SMAA) |
| Still open | SEC-003 token revocation (user), BUG-066 (CLAUDE.md conflict, user decision), BUG-069, 070, 071, 072, 087, 111 |

---

## 1. Executive summary and visual gap analysis

### 1.1 Verdict

**The foundations are stronger than the looks.** Veyra has:
- an 8 × 6 km world generated from validated data;
- a 100 Hz raycast vehicle with a real tyre model;
- an automated player test harness with 18 review cameras;
- about 146 FPS average at 1440p (VSync on, laptop RTX 4070).

**It still reads as a greybox prototype.** None of the 18 S5 review shots would pass for a shipped game. Three facts dominate:

1. **Detail is missing at the scales the eye reads.** No frame contains window depth, kerb bevels, cracks, light pools, reflections, trees, props or people. GTA V and Cyberpunk spend most of their art budget on exactly these.
2. **The game does not drive end to end.** V0 autotest routes stop at:
   - bridge abutments (BUG-001, steps up to 8.5 m);
   - tunnel exits (BUG-049);
   - concrete colliders in the carriageway (BUG-051);
   - the Lantern Strait Bridge deck, where the car falls into the sea (BUG-050);
   - the start of H2 (BUG-052).

   A racing game with walls in its roads is not shippable, however it looks.
3. **The asset plan in the brief would break the licence rules.**
   - 7 of the 12 real-brand car downloads are non-commercial, no-derivatives or Sketchfab Standard.
   - The 5 CC-BY ones conflict with CLAUDE.md and with car makers' trade dress.
   - An untracked script holds a hard-coded Sketchfab API token (SEC-003).

### 1.2 Why it looks like a prototype (ranked by screen area)

| # | Cause | Evidence [V] | How the reference games do it |
|---|---|---|---|
| 1 | **Buildings are primitive shells.** <br>• 7 shape types; 73 % of the 2,708 buildings are gabled `house` shells; 1,923 share one Suburban facade. <br>• One 512² texture covers a 12 × 14 m patch (~40 px/m). <br>• No normal or mask maps on any facade or glass material, so wall, glass and mullion share one smoothness. <br>• Window lights come from a 16-state hash that repeats every 4 × 4 windows, in phase on every building. <br>• No trim, cornice, window recess, interior or rooftop clutter beyond 1–3 HVAC boxes. | `Editor/BuildingGenerator.cs:273-339, 355-366, 82-91`; `Editor/ProcTextures.cs:140-196`; `Editor/CityBuilder.cs:354-363`; `CAM_06`, `CAM_07` | **GTA V:** hand-modelled buildings with recessed windows, unique trim and fake-interior window maps; distant LODs baked from the same assets. **Cyberpunk 2077:** modular kits on shared trim sheets, several architectural styles layered per district, dense decals (grime, posters, cables) over every seam. |
| 2 | **No reflections anywhere.** <br>• 0 reflection probes in `VeyraCity.unity`. <br>• Realtime probes are switched off on PC (`QualitySettings.asset:82`). <br>• No SSR. <br>• The environment reflection is the skybox at 128 px and never updated. <br>Result: glass towers are flat blue slabs, wet roads glitter but reflect nothing, and car clear coat reflects only the sky. | `VeyraCity.unity:35-36, 96`; `QualitySettings.asset:82`; `CAM_07`, `CAM_13` | **GTA V:** a low-resolution environment map refreshed around the camera every frame, planar water reflection and screen-space reflections. **Cyberpunk:** screen-space or ray-traced reflections; puddles mirror every sign. |
| 3 | **Flat light.** <br>• 1 directional light plus 28 unshadowed point lights. They are re-picked every 0.3 s with a hard on/off, centred 91 m ahead of the camera, and only in Play mode. <br>• Neon lights nothing. <br>• No light probes, APV or lightmaps, so no bounce light and no darker alleys. | `World/WorldEnvironment.cs:222, 257-312` | **GTA V:** thousands of "LOD lights" (distant light sprites) and time-of-day ambient. **Cyberpunk:** dense local lights inside volumetric fog. |
| 4 | **Flat roads.** <br>• Asphalt is a slab with 2 vertices across the full width: 0 % crossfall and no superelevation, even on the A19 hairpins. <br>• Vertical 0.14 m kerb face with streaked UVs: the GLB's along-road UVs are discarded for planar XZ. <br>• Junction seams; no gutters, decals, wear lanes or puddle masks. <br>• The renderer has no Decal feature. | Cross-sections measured from `WorldSource/meshes/D1.vmesh`, `D4.vmesh`; `CityBuilder.cs:660, 799-815`; `Settings/PC_Renderer.asset:27-28` | Crowned carriageway with gutters, bevelled kerbs, drop kerbs at crossings, tar seams, patch and crack decals, manholes, darker wheel tracks, and puddles that collect at low points in rain. |
| 5 | **Empty frames.** <br>• **No tree or lamp appears in any of the 36 S5 shots** (new: BUG-067), although the data puts 1,206 pines within 300 m of CAM_10. <br>• The only street furniture is lamps (BUG-008). <br>• No pedestrians. 36 traffic cars spawn 140–360 m away. <br>• Downtown towers stand on grass terrain (BUG-058). | `World/InstancedPropRenderer.cs:77-107`; `AI/TrafficSystem.cs:17-19` | Every frame has parked cars, props, people and street trees. GTA's ambient population is tuned per district and time of day. |
| 6 | **No atmosphere.** <br>• Exp² fog with no height falloff: 50 % fog at about 2 km by day and about 1 km at night. <br>• Built-in procedural sky, no clouds, black night sky with no city glow. <br>• Aerial shots wash out (BUG-059). | `WorldEnvironment.cs:205-210` | **GTA V:** cloud layers with cloud shadows, light shafts and height haze. **Cyberpunk:** volumetric fog and clouds are the core of its night look. |
| 7 | **Image quality.** <br>• MSAA 4x **and** SMAA, with no TAA or STP. <br>• High-frequency asphalt normals at smoothness 0.96 when wet cause specular glitter. <br>• Sun shadows end at 160 m, while 72 buildings are over 100 m tall (max 342 m). | `Settings/PC_RPAsset.asset:28, 57`; `CityBuilder.cs:460`; `ProcTextures.cs:93` | Temporal AA or upscaling with specular anti-aliasing; distant shadows from cascades plus baked or cached terms. |

### 1.3 What is already at a good level (keep it)

- **Data-driven world and validated coordinates.** S3 checked 209 locations, 93 roads and 2,708 buildings against the data (`Logs/S3/validate_data.txt`). This is the right base for the generation-plus-kitbash route.
- **Test infrastructure:**
  - One-command builds;
  - `VeyraAutoTest` routes with PASS/FAIL rules;
  - 18 review cameras with contact sheets;
  - telemetry.

  Most indie projects never get this. Every phase below plugs into it.
- **Tyre and suspension core:**
  - Simplified magic formula with a friction ellipse and load sensitivity.
  - Wheel spin sub-stepped 6× (implicit).
  - Spring and damper with separate bump and rebound, anti-roll bars and bump stops.
  - Tuning stored in `VehicleDefinition` ScriptableObjects.
- **Arcade layer:**
  - A brake-tap drift holds 32° and keeps 83 % of its speed.
  - The nitro has three tiers (Standard, Clean Boost, Overdrive).
  - Camera FOV, shake, motion blur and speed streaks react to driving.
- **Rendering base:** HDR, ACES, bloom, SSAO, Forward+ and the SRP Batcher. Car paint already uses URP Complex Lit with clear coat.
- **Frame-time headroom:**
  - Average 146 FPS, p99 12.5 ms in a release IL2CPP build at 2560×1440.
  - Graphics memory 2.4 GB of the 4 GB budget.
  - There is budget to spend on visuals, provided streaming and LOD arrive with the content (Phase 2).

### 1.4 A realistic target

GTA V and Cyberpunk 2077 were hand-built by studios of hundreds over many years. Asphalt's cars are licensed hero models. Veyra's realistic route to "AAA-looking" is:
- generation from the data with strict rules;
- kitbash and trim-sheet modules;
- hand finishing of hero areas: Downtown, Neon Mile and Ironport first.

After Phases 0–4 below, D1 and D6 should hold up in night screenshots and read as a believable city by day, at 60+ FPS. Matching GTA's hand-made density across the whole 48 km² is not a near-term goal, and this audit doesn't pretend it is.

---

## 2. Comparative matrix

**Veyra score:** 0–10, where 10 means parity with the strongest reference for that row. It is a judgement based on the evidence in §1 and §3.
**Gap:** C = critical (blocks a shippable look or feel), L = large, M = medium, S = small.

### 2.1 Graphics

| Area | Veyra City today | GTA V | Cyberpunk 2077 | Asphalt 9/Legends | Score | Gap |
|---|---|---|---|---|---|---|
| Building and facade detail | 7 primitive shapes, one 512² facade per style, no normal or mask maps, phase-locked windows | Hand-modelled, unique trim, fake interiors | Modular kits, trim sheets, heavy decals, style layering | Set dressing built for 2–3 s fly-bys; high-detail hero tracks | **2** | C |
| Road surface | Flat slab, no crown or gutter, streaked kerbs, no decals | Crown, gutters, kerbs, tar seams, patches, manholes, puddles | Same plus layered grime and markings | Glossy, highly reflective racing asphalt with strong lane art | **3** | L |
| PBR completeness | Roads have normal and gloss maps; facades and glass have none; 0 of 70 materials have AO | Full PBR-like sets (2013 era), heavy hand detail | Full PBR with detail maps and decals | Full PBR, clear coat on cars | **3** | L |
| Reflections | None (0 probes, realtime off, no SSR) | Dynamic environment map, SSR, planar water | SSR or RT reflections, mirror puddles | Strong cube-map reflections on cars and wet tracks | **1** | C |
| Lighting and GI | 1 sun, 28 popping point lights, no probes or GI | Time-of-day ambient, distant light sprites | Dense local lights, GI, volumetrics | Baked, art-directed lighting per track | **2** | C |
| Night and neon | Emissive bands that light nothing; neon glows at noon (BUG-048) | Lit windows, light pools, distant light sprites | Signature layered neon, light spill, haze | Over-saturated neon tracks, bloom | **3** | L |
| Sky, fog, weather | Procedural sky, exp² fog, no clouds; wetness is a uniform smoothness change | Cloud layers, weather presets, rain puddles | Volumetric clouds and fog, rain on every surface | Static art-directed skies | **2** | L |
| Post and image quality | ACES, bloom, SSAO; MSAA+SMAA, no TAA; LDR grading | Tuned tonemapping, DoF, bloom | Heavy grading, chromatic aberration, film grain, DLSS/FSR | Heavy bloom, motion blur, lens flares | **5** | M |
| Vegetation and terrain | 256² splats, 3-card grass, cone trees missing from review shots | Dense hand-placed foliage, terrain blending | Sparse (desert and urban), but detailed | Track-side dressing only | **2** | L |

### 2.2 Physics and driving

| Area | Veyra City today | GTA V | Cyberpunk 2077 | Asphalt 9/Legends | Score | Gap |
|---|---|---|---|---|---|---|
| Handling model | Raycast suspension, simplified magic-formula tyres, friction ellipse, 100 Hz | Per-car traction curves, explicit roll centres and anti-roll (handling data per car) | Reworked in 2.0 into a credible arcade-sim model | Pure arcade: no tyre model, automatic acceleration | **6** | M |
| Weight transfer and body roll | Emergent; about 1.8° physics roll at 0.85 g plus 2.0° visual lean; arcade forces at the centre of mass cancel roll | Pronounced roll (about 5–8°), dive and squat; the signature "weight" | Moderate roll, heavy cars feel heavy | None to speak of; cars stay flat | **4** | M |
| Arcade layer and assists | Slide damping, yaw tracking, extra downforce, drift controller, air control, flip recovery, wall glancing | Light assists, no drift assist | Light assists | Full assist ("TouchDrive"), knock-downs | **6** | M |
| Nitro and boost | 3 tiers with timing and double-tap; thrust fades to about 0.06 g near top speed; no shockwave | None | None (a few cars have boost) | Signature: nitro, perfect nitro, shockwave, refill from stunts | **6** | M |
| Drift and stunts | Drift scoring with ×5 multiplier and grace window; 360s penalised, no barrel roll or takedown rewards | Drift possible, unscored | Unscored | 360s, barrel rolls, takedowns, near misses all rewarded | **5** | M |
| Camera feel | Custom chase cam, FOV 60→74° (+8° nitro), shake, motion blur, streaks; **no collision** | Weighty follow cam with collision and look-behind | Collision, several cameras | Low, wide, aggressive; dutch on drifts; impact punch | **4** | L |
| Damage | None | Deformation, detachable parts, tyre burst | Light | Visual only (scratches, sparks) | **0** | L |

### 2.3 World detail

| Area | Veyra City today | GTA V | Cyberpunk 2077 | Asphalt 9/Legends | Score | Gap |
|---|---|---|---|---|---|---|
| Street furniture and props | Lamps only (2,777, no colliders); no signals, signs, bins, benches | Dense, collidable, breakable | Dense, layered | Track-side dressing | **1** | C |
| Traffic | 36 kinematic cars, spawning 140–360 m away | Dense, reactive, varies by district and hour | Moderate | Rival racers plus traffic on some tracks | **3** | L |
| Pedestrians | None | Dense, with scenarios | Dense crowds | None | **0** | L (C for the "city" brief) |
| Landmarks and district identity | Placement data only; hero landmarks (card A4) not built | Iconic, hand-made | Iconic megabuildings | Iconic per track | **2** | L |
| Interiors | None; no fake interiors | Fake-interior windows, some real interiors | Many interiors | None | **0** | M |

### 2.4 Audio

| Area | Veyra City today | GTA V | Cyberpunk 2077 | Asphalt 9/Legends | Score | Gap |
|---|---|---|---|---|---|---|
| Engine | Fully synthesised harmonics; 0 audio files in the project | Recorded, layered by load and rpm per car class | Recorded, layered | Recorded, exaggerated | **2** | L |
| Tyres and surfaces | One fixed-pitch squeal (about 1.4 kHz); no surface roll noise, scrape, shift or bottoming sounds | Per-surface tyre and roll, scrapes, debris | Similar | Strong drift squeal and impact sounds | **2** | L |
| Ambience and music | None | Per-district ambience, radio stations | Dense city ambience, radio | Licensed soundtrack | **0** | L |

### 2.5 Performance

| Area | Veyra City today | GTA V / Cyberpunk practice | Score | Gap |
|---|---|---|---|---|
| Frame rate | About 146 FPS average, p99 12.5 ms (release, 1440p, laptop 4070, VSync on); simple content | Locked 60 with full content | **7** | S today, L once content arrives |
| Hitches | 0 while driving; up to 165.8 ms after teleports; 54 > 50 ms in the car-swap showcase | Streamed and pre-warmed | **5** | M |
| Streaming | **None.** One 28.9 MB scene; no Addressables; far plane 3,500 m | Cell streaming with HD/LOD swaps | **1** | C (blocks Phase 2) |
| LOD and HLOD | All 1,733 LODGroups have one level; 1,207 renderers are wider than 250 m | Several LODs plus HLOD proxies for the skyline | **1** | C |
| Batching and culling | SRP Batcher on; GPU Resident Drawer off; GPU occlusion off; no baked occlusion; static batching off | GPU-driven or occlusion-culled | **3** | L |
| Measurement | Harness records FPS, p99, hitches and GC, but **not** draw or SetPass calls, triangles or GPU time; VSync always on | Full budget telemetry | **5** | M |

---

## 3. Detailed findings

### 3.A Graphics and world architecture

#### A1. Buildings (`BuildingGenerator.cs`, `ProcTextures.cs`)

**How they are built [V]:**
- Flat-shaded world-space quads, merged per 250 m chunk and material (`:33-40`, `:425-450`).
- Shapes in the data: house 1,986, box 594, cylinder 92, stepped 26, hangar 6, tower 2, mast 2.
- The only per-building variation is footprint, height, yaw, facade choice and a seed. The seed moves only the roof HVAC boxes, the crown and the neon colours. There is no per-building tint, no UV offset and no window-size variation.
- Each facade has albedo and emission only. Textures are RGBA32, max 2048, compressed HQ (`ProcTextures.cs:54-77`).
- One `UBX_` box per building on the Concrete surface. Cylinders and masts also get a box, which leaves invisible corners about 7 m wide on the 92 tanks (BUG-071).
- A single-level LODGroup per chunk mesh. It only culls at tens of kilometres, so it costs CPU for nothing.

**Why it reads as greybox:**
- Silhouettes have no breakup and roofs have knife edges.
- Windows have no depth and no different specular response from the wall.
- One texture phase per material, so 71 % of buildings look the same up close.
- No ground-floor life.

**What AAA cities do instead (technique, not content):**
- **Trim sheets.** One 4K texture holds horizontal strips (cornices, frames, panel seams) that many meshes map to, so detail is high and texture memory is low. This is playbook card **A1**.
- **Modular kits** that snap to a metric grid: storey 3.0–3.6 m, bays of 1.5, 3 and 6 m. Corners, storefronts, parapets, balconies, fire escapes, rooftop props. Card **A2**.
- **Fake-interior (interior mapping) windows.** A shader shows a room behind the glass using a cube map or a room atlas, with parallax, a random room per window and lights on at night. GTA V uses window interior maps widely; Cyberpunk mixes them with real interiors.
- **Decal layering** over seams: grime, leaks, posters, signs, cables. Cyberpunk's districts depend on it.
- **HLOD:** each block's far LOD is a single merged mesh with baked facades, so the skyline costs a few draw calls.

**How the downloaded packs replace the boxes:** see §3.D for licence and coverage, and Phase 1 and 2 in §5 for the pipeline. In short:
1. B1 licence gate.
2. B2 `process_city_map.py --mode kitbash` cuts each pack into per-building and per-prop FBXs with LODs and collision.
3. B3 importer.
4. A `KitbashCatalog` ScriptableObject that the new `BuildingAssembler` fits to the 2,708 placement records.

**The downloads never decide a position.**

#### A2. Roads (`Tools/World/build_world_data.py`, `CityBuilder.cs`)

**[V] What is there:**
- The blockout GLBs, converted 1:1. Cross-sections every 3.98 m along the 4 m profile.
- `build_world_data.py:49` copies `profile_xyz_every_4m` unchanged: no smoothing, no grade check. This is why BUG-001, BUG-034 and BUG-035 reach the colliders unchanged.
- Widths are fixed per class (`:28`).
- The terrain corridor is only lowered (z − 0.25 m, `:284, 330-333`). There are no cut or fill slopes, and bridges are skipped (`:290`).

**Measured cross-sections:**

| Element | Veyra today (A1 Crown Avenue, AR class, 20 m) | AAA-style target (Phase 3) |
|---|---|---|
| Carriageway | Flat slab: 2 vertices across, 0 % crossfall, skirt to −0.25 m | 2 % crown on urban roads (2.5 % on MW/EX/HW); superelevation on curves up to the class limit; at least 1 vertex per lane edge plus the crown line |
| Gutter | None | 0.3–0.5 m concrete gutter, 2–4 % falls to drains |
| Kerb | Vertical 0.14 m face; planar UVs streak it | 0.15 m kerb with a 20 mm bevel; drop kerbs at crossings and driveways; its own `Kerb` SurfaceDefinition (BUG-042) |
| Sidewalk | 3 m block, 0.20 m high, 1.5 m slabs | 2 % crossfall towards the gutter; joints, tree pits, utility lids |
| Markings | Separate quads 3 cm above the asphalt; constant 0.1 emission (`CityBuilder.cs:326-327`) | Decals or a markings mask in the road shader; worn edges; no emission |
| Wear | None | Darker wheel tracks (vertex colour R from lane count), edge grime (G), patches and age (A) |
| Decals | None (no Decal renderer feature) | URP Decal feature with an original decal atlas (A1): cracks, patches, tar seams, manholes, tyre marks, stains; seeded density per road class |
| Wet | Uniform smoothness up to 0.96 and albedo × 0.55 on 7 materials | Puddle mask (vertex colour B at profile low points plus world-space noise), porosity-based darkening, rain ripples |
| UVs | GLB along-road UVs discarded for planar XZ (`CityBuilder.cs:660, 799-815`) | u across, v = s / tile length; box projection only on vertical faces |
| Junctions | Overlapping slabs; `LosesJunction` deletes triangles (seams; BUG-027) | Generated junction patches (card W5) |

#### A3. Materials and runtime shaders (card F1, in progress) [V]

The uncommitted working tree has:
- 0 `Shader.Find` and 0 `EnableKeyword` calls in runtime code.
- `VeyraMaterialLibrary`, `VeyraMaterialFactory` and `MaterialAuditor`.
- `VC_Variants.shadervariants` (51 variants), linked in Preloaded Shaders.

**Still open:**
- **The library holds only Vehicle, Effects and Debug slots** (`World/VeyraMaterialLibrary.cs:89-112`). The world palette is still serialized scene references made by the editor-only `CityBuilder.Mat()` (`:261-305`).
- **BUG-039 is not fixed.** `WorldEnvironment` still writes shared materials every frame, edit mode included (`:140, 234-255`). The new save guard only resets them before saves. The right fix is card F3: global shader properties (`_VC_Wetness`, `_VC_NightFactor`, `_VC_Rain`) read by the shaders, never per-frame material writes.
- **BUG-046 has changed form.** `GameFlow.cs:506, 517` now caches paint copies in a static `paintCache` that is never cleared.

#### A4. Lighting and post-processing against a Cyberpunk-style night [V]

| Setting | Now | Recommended (cards L1–L5) |
|---|---|---|
| Renderer | Forward+, SRP Batcher | Keep. Turn on **GPU Resident Drawer** and **GPU occlusion culling** (both off: `PC_RPAsset.asset:85, 88`) |
| Anti-aliasing | MSAA 4x **and** SMAA (`PC_RPAsset.asset:28`, `CityBuilder.cs:460`) | Pick one temporal solution: STP (render scale about 0.77–1.0 at 1440p) or TAA. Drop MSAA. Add specular anti-aliasing on the high-frequency normals |
| Shadows | 2048 map, 160 m, 4 cascades | 300–400 m with tuned splits; a 4096 map on PC High; check the cost against the Appendix C budget (GPU ≤ 13 ms) |
| Colour grading | LDR (`m_ColorGradingMode: 0`) | HDR grading, with one LUT or volume per district and time of day (Appendix B colour script) |
| Tonemapping | ACES | Keep |
| Bloom | Threshold 0.9 at night (1.25 by day), intensity 1.2, scatter 0.7, HQ filtering off | HQ filtering on; an original lens-dirt texture at night only; Neon Mile intensity set by volume |
| Exposure | Post exposure 0.3 at night, 0.05 by day | URP has no eye adaptation. Script an exposure curve against sun elevation and local light density |
| SSAO | Full resolution, intensity 1.2, radius 0.8 | Half resolution plus blue noise; spend the time saved on reflections |
| Reflections | None | Box-projected baked probes per block; one realtime probe following the player car (time-sliced, 128–256 px); turn on realtime probes in PC quality; an SSR spike (`VISUAL_BENCHMARK_REPORT.md` §5), kept only if ≤ 1.5 ms |
| GI | None | Adaptive Probe Volumes baked per cell, using sky occlusion and day/night lighting scenarios (Unity 6 URP) |
| Fog | Exp², no height falloff | Height fog plus distance fog with density reduced at camera altitude (BUG-059). Neon Mile and Ironport also get a half- or quarter-resolution volumetric fog pass with local-light scattering (fog budget ≤ 0.6 ms) |
| Night lights | 28 popping point lights, Play mode only | About 100 faded point and spot lights centred on the car (Forward+ handles it; PC High allows 48, 8 of them shadowed, per view); distant light sprites for every lamp and lit window beyond about 300 m; headlights on all near AI cars |
| Neon | HDR emission, also at noon (BUG-048) | Emission × NightFactor (about 10 % by day); proxy lights per sign group; flicker through MaterialPropertyBlocks |
| `DefaultVolumeProfile.asset` | Test components and a missing script (`:977`) | Clean it (BUG-075) |

#### A5. Streaming and performance architecture [V]

- **One scene for the whole world.** `VeyraCity.unity` is 28.9 MB with 45,813 YAML objects. There are no Addressables, no `Scenes/Cells`, no baked occlusion, and the camera far plane is 3,500 m. Card W7 (48 cells × 16 chunks) has to land **before** Phase 2 adds real geometry.
- **Terrain:**
  - 48 tiles at 513², alphamap at 3.9 m per texel.
  - 8 layers, each a 256² diffuse with no normal or mask map.
  - Only layers 0, 1, 2, 3, 5 and 7 are ever painted (`CityBuilder.cs:544-549`), so Paving and Farmland never appear.
- **Grass:** 3 crossed cards, 120 m view distance (`GrassBuilder.cs`).
- **Trees and lamps:**
  - Drawn with `RenderMeshInstanced` from `LateUpdate`.
  - Frustum-culled against `Camera.main` or the Scene view, and `RenderParams.camera` is never set (`InstancedPropRenderer.cs:77-107`, BUG-067).
  - Only pine and deciduous trees have a second LOD (hard switch at 90 m).
  - Prop meshes have no UVs, and foliage is flat-colour cones.

### 3.B Vehicle visuals and roster

#### B1. Fleet today [V]

| Group | Count | Model source | State |
|---|---|---|---|
| Arden Kite stock and street | 2 entries, 1 model | Authored in-project in Blender | Interim art (BUG-020); convex hull warnings (BUG-047) |
| Catalogue cars | 5 | `ProceduralCar` boxes at runtime | Need models |
| Downloaded fictional cars (CC-BY) | 19 drivable; 9 of them also traffic | Sketchfab: Daniel Zhabotinsky, dark_igorek, ollitei | In game; badges stripped; credits in-game |
| Generic traffic pack (CC-BY) | 10 | Comrade1280 | Traffic only |
| Real-brand CC-BY rebadges | 5 | Ferrari SF90 → `ardente_furia`, Rolls-Royce Ghost → `halcyon_regent`, Koenigsegg Gemera → `nordvik_hex`, Aston Martin DB11 → `ashcombe_tempest`, Chevrolet C8 → `stanton_sidewinder` | **Uncommitted**, in `ArtSource/Vehicles` only. Stanton has no `process_report.json` yet. **Blocked by CLAUDE.md** until the user changes the rule. A legal check of the body designs is needed before any release. |
| Real-brand, licence forbids use | 7 | Bugatti Tourbillon, Bolide and EB110; Porsche 911 GT3 RS; Lamborghini Terzo Millennio; BMW M6 GT3; McLaren P1 | **Cannot ship:** non-commercial (NC), ShareAlike (SA), no-derivatives (ND), or Sketchfab Standard. Not processed, because the pipeline gate requires a CC0 or CC-BY sidecar (`process_downloaded_car.py:64-77`). Quarantine them (Phase 0). |

**Licence and originality findings (BUG-066):**
- `ArtSource/Downloads/DOWNLOAD_LIST.md:3` says "All are fictional designs under CC Attribution 4.0". Rows 21–32 are 12 real brands, 7 of them not CC-BY.
- The sidecar notes record "rebadged lookalike allowed, user decision 2026-11-10". That conflicts with CLAUDE.md ("Real-brand cars … are never used").
- `CarImportTools` has no real-brand gate.
- Credit strings carry the real model titles (`CarImportTools.cs:162`). They appear on the dealership card (`GameFlow.cs:1192`) and in the credits (`:623`), next to the line "All car names are fictional".
- **Removing badges does not remove trade dress.** The SF90, Ghost, Gemera, DB11 and C8 bodies remain recognisable designs.
- **[I]** Some "fictional" low-poly classics (Phoenix 455 '71, Kiri '86, Lasley '67) echo real cars closely. B1 should review them too.

**The Asphalt-style hypercar tier has a gap.** The cars that would fill it (Bugatti, McLaren, Porsche, Lamborghini) are exactly the ones whose licences forbid use. Options:
- commission original hypercars (licence register L-010 already plans this for the Kite);
- buy commercially licensed **fictional** cars;
- author 2–3 original designs in Blender through the A8 pipeline.

#### B2. Car pipeline (`Tools/Blender/process_downloaded_car.py` → `Editor/CarImportTools.cs`) [V]

**Blender step:**
1. Licence gate: the sidecar must say CC0 or CC-BY-4.0 (`:64-77`).
2. Strip parts by name or position (`:796-814`).
3. Orient and scale the model.
4. Find 4 wheel hubs and place `Wheel_*` and `Caliper_*` empties at the hub centres (`:820-851`).
5. Join **everything else into one `Body_LOD0`**, so glass, lights and interior are only material slots.
6. LOD0 is capped at 120k tris, LOD1 = min(45 %, 40k), LOD2 = min(15 %, 10k) (`:856-863`). On large models that gives 33 % / 8 % instead of the 40 % / 10 % rule.
7. Collision: one `UBX_Chassis` box plus one `UCX_Body` hull of ≤ 250 faces. The asset pipeline spec asks for 3–5 hulls.
8. Brand marks are painted out of the textures (`:655`).
9. FBX export.

**Unity step:**
- Textures capped at 2048, BC7.
- Materials built from `materials.json`.
- The definition is cloned from `baseDef` and scaled to the model (`:377-439`).
- LODGroup at 0.30 / 0.10 / 0.012, with **the wheels outside the LODGroup** (`:371`, BUG-088).

**Batch command (already working):**
- Blender step: `python -I Tools/Blender/process_all_cars.py [--only id1,id2] [--jobs 3]`. It logs to `Logs/A8/blender_<id>.log` and `process_summary.json`.
- Unity step: Unity menu **Veyra/Vehicles/Import Downloaded Cars**.

The pipeline doesn't need replacing. It needs the Phase 1 fixes:

| Fix | What changes |
|---|---|
| **Split the body by function** | Separate objects or submeshes for paint, glass (sorted transparent), each lamp function (head, tail, brake, indicator L/R, reverse), interior, chrome, rubber and carbon. This fixes BUG-064 (brake glow lighting the headlights) and allows a distinct glass LOD |
| **LOD ratios** | Enforce 40 % / 10 % of LOD0; put the wheel LODs inside the LODGroup |
| **Collision** | 3–5 `UCX_` hulls (body, cabin, nose, tail) under Unity's 256-polygon limit |
| **Physics scaling** | Scale dampers by mass ratio, not √mass (BUG-077); stop using `dragScale` as a speed limiter (BUG-079) |
| **Material validation** | Set `_ClearCoat = 1` wherever `_CLEARCOAT` is on (BUG-078); run `MaterialAuditor` on import |
| **Gates** | Add a real-brand gate to `CarImportTools` (refuse sidecars with `real_brand` unless CLAUDE.md allows it); drop real model titles from in-game credit strings unless the licence requires the exact title, and keep the exact attribution in the credits screen only |

#### B3. Car materials against Asphalt and GTA V [V]

| Part | Veyra now | AAA practice | Phase 3 target |
|---|---|---|---|
| Paint | URP Complex Lit with `_CLEARCOAT`; clear-coat smoothness 0.92–0.96; **`_ClearCoat` float left at 0** on all 29 imported paints | Base plus clear coat, metallic flakes, sometimes pearlescent colour shift | Fix the float; add a flake micro-normal (tiled, about 0.2 mm, sparkle under the sun); two-tone and livery masks; paint wear and dirt mask |
| Reflections on paint | Sky only (no probes in the world) | Dynamic environment map of the real street | Realtime probe following the car (Phase 4) |
| Glass | Lit, transparent premultiplied, smoothness 0.97–1 | Tinted, edge darkening, dirt, rain streaks | Separate glass objects; Fresnel tint; dirt and rain masks driven by `_VC_Wetness` |
| Rims | Flat colour (Kite); imported textures elsewhere | Polished or brushed metal, anisotropic highlights | Metallic 1, smoothness 0.8–0.9, brushed normal |
| Brake calipers | Flat colour | Painted metal | Painted metal with original fictional logos only |
| Carbon fibre | None | Weave normal under clear coat | Weave normal plus clear coat (body kits and Clean Boost cars) |
| Lamps | One shared lamp material per car (BUG-064); real headlights only on the player and the first 2 AI cars (`GameFlow.cs:975`) | Per-function lamps, projector cookies | Per-function emissive (headlight 10–20, brake 6–8, indicator amber, reverse white); cookie spots for near cars, light sprites for far cars |
| Interior | One interior material on low-poly cars; none on V12 Goblin | Full cockpit for the interior camera | Dashboard emissives at night; an interior camera only for cars whose interior holds up |

### 3.C Vehicle physics and driving feel

#### C1. The model in numbers (`VehiclePhysics/VehicleController.cs`, stock Kite) [V]

| Element | Implementation | Kite values |
|---|---|---|
| Suspension | One raycast per wheel. Force = k·x + c·ẋ (separate bump and rebound damping) + bump stop. Anti-roll = arb·(cL − cR). Load clamped to 3·m·g (`:291-353`) | Rest length 0.24 m, travel 0.15 m. k = 28.9 / 30.8 kN/m (front / rear). Damping 1,781 / 3,562 (bump / rebound) at the front. Anti-roll 16,000 / 9,000 |
| Tyres | Simplified magic formula sin(C·atan(B·s)). Friction ellipse. Load sensitivity μ0·(1 − 0.12·(Fz/3000 − 1)). No E term, no relaxation length (`TyreModel.cs:24-62`) | μ 1.05, peak slip ratio 0.10, peak slip angle 8° |
| Mass | Centre of mass from CoMHeight and weight split; box inertia × 0.85 | CoM height 0.48 m, 53 % on the front axle; yaw inertia ≈ 1,764 kg·m² |
| Aero | Drag ½ρ·CdA·v² at the CoM; downforce ½ρ·ClA·v² split between the axles (`:537-547`) | CdA 0.62, ClA 0.05 (only about 94 N at 200 km/h) |
| Powertrain | Interpolated torque curve, clutch launch, 0.25 s shifts, engine braking, limiter cut | 5 speeds, final drive 4.10 |
| Assists | ABS and TCS (feed-forward caps plus slip trim). "ESC" is only a throttle cut above 50° of body slip; no per-wheel yaw braking | Brakes 4,400 N·m, 68 % front |
| Arcade layer (`HandlingFeel_Arcade.asset`, `:594-712`) | Slide damping (≤ 0.55 g at the CoM); yaw tracking (gain 5, ≤ 6 rad/s², `ForceMode.Acceleration`); extra downforce (+0.45 g at 220 km/h); drift mode (tyre lateral × 0.25, commanded centripetal force, speed P-controller, yaw tracked to 16–36°); air auto-level; flip recovery; wall glance | A brake-tap drift holds 32° at 83 % of entry speed; arcade slalom 3.6° of slip (31° in Simulation) |

#### C2. Planted or floaty?

**Planted, close to "on rails"** for the Kite in Arcade [V]:
- Ride frequency 1.55 Hz front, 1.70 Hz rear (a road car).
- Damping ratio 0.30 bump, 0.60 rebound.
- Physics roll is only about 1.8° at 0.85 g, plus 2.0° of visual lean (`VehicleBodyLean`).
- The arcade slide damping and yaw tracking act **at the centre of mass** and ignore inertia (`ForceMode.Acceleration`). They kill the roll and pitch moments that make a car feel heavy, so turn-in feels instant and weightless.

**The heavy downloaded cars are floaty [V].** `CarImportTools.cs:407-408` scales the dampers by √(mass ratio) while springs scale by the mass ratio, so heavier cars are underdamped:

| Car | Damping ratio (bump / rebound) |
|---|---|
| LCT 3000 | 0.14 / 0.29 |
| Iron Howl | 0.22 / 0.45 |
| Kite (reference) | 0.30 / 0.60 |

They bounce after every kerb (BUG-077).

**Bottoming risk [V].** Static sag is 0.103 m of 0.15 m travel. At top speed the arcade downforce leaves about 7 mm before the bump stop, so kerbs give damper spikes of about 17.8 kN on the Kite, and the spike depends on the timestep.

#### C3. Weight transfer and body roll against GTA V

GTA V gets its "heavy" feel from:
- softer springs and more travel;
- explicit roll-centre heights per axle (per-car handling data);
- visible roll of about 5–8°, nose dive under braking and squat under throttle;
- a camera that lags the body.

**Veyra has the right primitives** (anti-roll bars, roll-centre heights at 0.06 / 0.10 m, `VehicleBodyLean`) but tunes them stiff. The arcade layer then overrides yaw directly.

**To move towards GTA's weight without losing the arcade grip:**
1. Apply arcade lateral forces at the axle contact patches, not at the CoM, so they create a roll moment.
2. Use `ForceMode.Force` with the real inertia for yaw assistance.
3. Add a per-class "weight" preset: softer, more travel for muscle and luxury cars; stiff for supercars.
4. Give the camera a body-relative lag.

#### C4. Nitro and drift against Asphalt

| Item | Veyra (`Nitro/NitroSystem.cs`, `DriftScorer.cs`) | Asphalt 9/Legends | Gap |
|---|---|---|---|
| Tiers | Standard ×1.35 torque, +5 % cap; Clean Boost ×1.55, +7 % (second press 0.60–0.85 s after activation); Overdrive ×1.90, +10 % (double-tap on a full meter, empties it in 2 s) | Nitro, Perfect Nitro (timed), Shockwave (double-tap on a full bar) | Mechanically equivalent. Only the names and visuals differ |
| Punch at speed | Thrust = a·m·(1 − v/capV), so Kite Overdrive at 200 km/h gives about 0.06 g. The torque multiplier is not capped, so the top-speed gain is +10.5 / 15.7 / 23.8 % against the spec's +5 / 7 / 10 % (BUG-084) | Strong at any speed; a shockwave knocks rivals aside | No shockwave burst; weak feel at high speed; the speed cap doesn't hold |
| Refill | Drift 6/s × angle × speed (≤ 40 per chain); near miss +5; air +4 per 0.5 s; clean driving +3 per 10 s; passive 1/s in races; `Pickup` never wired | Drifts, jumps, near misses, takedowns, 360s, barrel rolls | No stunt or takedown refills; no pickups |
| Drift score | clamp(angle, 12, 60) × speed × 1.6 × multiplier (×1 → ×5 every 2 s); grace 1.3 s; more than 300° of turn within 25 m scores 0 | Rewards style, including spins | 360s are penalised, the opposite of Asphalt |
| Tuning location | **Hard-coded tables** in `NitroSystem.cs:24-29` and `DriftScorer.cs` (breaks the CLAUDE.md rule; BUG-087) | — | Move them to a versioned `NitroTuning` ScriptableObject |

#### C5. What is missing (ranked by what the player notices)

1. **Camera collision and occlusion.** The camera passes through walls and tunnel roofs (`ChaseCamera.cs:155` only enforces a minimum height). Cinemachine 3 is installed but unused (card G1).
2. **Recorded engine and tyre audio.** Everything is synthesised today:
   - The squeal pitch is fixed at about 1.4 kHz and doesn't follow slip.
   - There are no surface, scrape, shift or bottoming sounds.
   - One `System.Random` is shared across audio-thread callbacks (BUG-085).
3. **World reflections on the paint** (Phase 4).
4. **Asphalt-style camera juice:**
   - dutch angle in drifts (±3–5°);
   - landing and impact punch;
   - nitro radial blur and a chromatic aberration pulse, with an accessibility toggle.

   Speed FOV already exists (60→74°, +8° on nitro).
5. **Weight** (C3).
6. **Real aero.** Downforce is token: ClA 0.05, and the "extra downforce" is non-physical. Drag is misused as a speed limiter: Iron Howl CdA is 2.18 m², and lifting off at 250 km/h decelerates it at 0.42 g, like a parachute (BUG-079).
7. **Stunt rewards and a shockwave** (C4).
8. **Damage:** sparks only. No deformation, detachable parts or dirt.
9. **Rate independence.**
   - The G smoothing (`VehicleController.cs:558-559`, a per-step lerp of 0.3) feeds TCS, ABS and body lean.
   - The wall-glance yaw factor (`:163`, × 0.4 per contact step) also depends on the rate.
   - Behaviour at the required 50 and 120 Hz differs from 100 Hz (BUG-082). Only straight-line Simulation is tested at 50, 100 and 120 Hz.
10. **Self-aligning torque** for force feedback, and a real ESC with per-wheel braking.

**Design conflict to resolve:** the arcade layer adds grip and commanded forces. `VEHICLE_HANDLING_SPEC` §3 says "assists never add grip". Either the spec gets an explicit "Arcade style" exception, or the layer gets reworked. This is the user's decision, recorded in Phase 0.

### 3.D City packs: licence, quality and district coverage [V]

Licences come from each archive's `license.txt`. Triangle counts come from `Docs/AUDIT.md` §9. Verdicts:
- **KITBASH:** cut into per-building and per-prop prefabs.
- **PARTS:** extract props or modules only.
- **REF:** reference only.
- **HOLD:** user decision needed (licence type).
- **REJECT:** cannot be used.

| Pack | Licence | Tris | Verdict | Best use |
|---|---|---|---|---|
| Downtown Buildings Set (Zhabotinsky) | CC-BY | 45,560 | **KITBASH** | D1 midrise and towers |
| Brownstone Building Set (Zhabotinsky) | CC-BY | 68,356 | **KITBASH** | D6 brick base; D5 town centre; D8 brick town |
| CCity Building Set 1 (Neberkenezer) | CC-BY | 31,050 (1,357 meshes) | **KITBASH** | D1 and D5 fill buildings |
| Modular City Builder Starter Kit (Pieter Ferreira) | CC-BY | 198,524 (321 meshes, 2 materials) | **KITBASH** | Street furniture and props (helps BUG-008); simple buildings |
| Atlanta Corporate Office (99.Miles) | CC-BY | 37,290 | **KITBASH** | One D1 glass tower type (not a landmark) |
| Realistic Chicago Buildings (99.Miles) | CC-BY | 11,643 | **KITBASH** | D1 art-deco fill |
| Japanese Street At Night (AFX/CGMotion) | CC-BY | 51,687 (107 textures, emissive maps) | **KITBASH + PARTS** | D6 storefront, awning and sign-frame modules. Repaint every sign with original fictional brands (A6); check the textures for real logos |
| New York City (golukumar) | CC-BY | 310,151 | **PARTS after check** | Facades and props. Its textures carry `TCom_` (Textures.com) and `CityGen` names, so their provenance must be confirmed |
| Cyberpunk City (mortalityrexotable) | CC-BY | 1,257,909, monolithic | **PARTS after check** | Advertising-screen and rooftop props for D6. The name hits the existing `process_city_map.py` refusal rule; open it only after a provenance check that nothing is ripped (BUG-003 risk) |
| Full Gameready City Retexture (mortalityrexotable) | CC-BY | 763,348 | **REF until provenance is clear** | "[retextured]" in the title and `u2SEBuild7` texture names suggest a derivative of another author's pack |
| San Francisco City (Abimael Gonzalez) | CC-BY | 92,578 | **REF** | Recognisable real landmarks (the bridge) don't fit a fictional city; spec/gloss workflow |
| City Road Layout Mockup (99.Miles) | CC-BY | 8,036 | **REF** | Marking and layout reference |
| Modular Roads Pack (Mini Loft Games) | CC-BY | 1,461 | **REF** | Imports at 585 × 588 m (wrong scale); too simple |
| Asian Buildings Set; Full Gameready City HongKong; Futuristic City; Futuristic Dystopian City; Modern City Block; Neighbourhood City Modular; NY Highway I-95 | Sketchfab Standard | 37k–754k | **HOLD** | Not Creative Commons. The user decides in B1 after reading the licence text. If accepted: HongKong and Asian sets for D6 rooftop clutter; Neighbourhood for D5 houses; Modern City Block for D1 street level |
| Plaza Night Time (patrix) | CC-BY-SA | 1,476,258 | **REJECT** (recommended) | ShareAlike would put the derived assets under CC-BY-SA |
| Isparta city block | none | 399,999 | **REJECT** | No licence evidence |
| GTA Liberty City 2001 (two archives) | none (ripped) | — | **REJECT** | Ripped game map; already refused (L-050). Delete it (open user action) |

**Coverage reality.** 1,973 of the 2,708 buildings are in **D5 Westhaven** (mostly Suburban houses). **No accepted CC-BY pack contains a suburban house kit.** So the downloads mainly upgrade D1 (282 buildings) and D6. The single largest visual lever by building count is an **original Suburban kit** (card A2, style 7) with:
- roof types;
- colour, porch, garage, fence and driveway variants.

| District | Packs that help | What has to be authored (card A2 or A4) |
|---|---|---|
| D1 Crown Downtown | Downtown Set, CCity, Atlanta, Chicago (+ Modern City Block if accepted) | GlassModern and ArtDeco kits; hero landmarks (A4) |
| D6 Neon Mile | Japanese Street, Brownstone, Cyberpunk City parts after the check (+ HongKong and Asian sets if accepted) | Neon, LED and sign kit with original brands (A6) |
| D5 Westhaven | CCity, Brownstone for the town centre (+ Neighbourhood if accepted) | **Suburban kit** (most of the map's buildings) |
| D2 Azure Coast | — | CoastalStucco kit: villas, beach clubs, marina |
| D3 Ironport | Existing container and crane generators | Industrial kit: sheds, tanks, rail |
| D7 Falcon Airfield | — | Hangars, terminal, control tower |
| D8 Redwood Valley | Brownstone (brick town) | Farm buildings |
| D4 Blackridge | — | Few buildings; rocks and vegetation (A7, CC0 sources) |

### 3.E Performance

**Measured** (V0 baseline and later runs; RTX 4070 **Laptop**, 2560×1440, VSync on) [V]:

| Metric | Value | Appendix C (PC High) | Verdict |
|---|---|---|---|
| Average FPS / p99 frame time | 146 / 12.5 ms (release IL2CPP) | 60+ / ≤ 16.6 ms | PASS today, on greybox content |
| GPU frame time | **not measured** | ≤ 13 ms | Unknown (BUG-090) |
| CPU main thread | not measured separately | ≤ 10 ms | Unknown |
| SetPass / draw calls | **not measured** | ≤ 450 / ≤ 3,000 | Unknown (BUG-090) |
| Visible triangles | not measured (scene total 1.28 M in meshes plus instanced props) | ≤ 8 M | Probably fine today |
| Real-time lights | 1 + 28 unshadowed | 48 (8 shadowed) | Under-used |
| Graphics memory | 2.43 GB | ≤ 4 GB textures | OK |
| GC per frame (dev) | 3.3–3.6 KB | ≈ 0 | **FAIL** |
| Hitches > 50 ms | 0 while driving; 5 after teleports (max 165.8 ms); 54 in the car-swap showcase (max 233 ms) | none | **FAIL** (BUG-029, BUG-053) |

**Performance traps that Phase 2 will trigger if they aren't fixed first:**
1. **No streaming (one scene).** Every kitbash building added stays resident and in the culling set.
2. **No real LODs or HLOD.** Kitbash buildings are about 10–100× the triangles of today's boxes. Without LOD1, LOD2 and HLOD, the skyline alone exceeds the triangle budget.
3. **GPU Resident Drawer off.** Modular buildings mean many small meshes. Without GRD or BatchRendererGroup, draw calls explode.
4. **No occlusion culling.** Downtown is the ideal case for it.
5. **Runtime collision cooking** (BUG-053, 759 meshes) and car-swap instantiation cause the hitches.
6. **Single-level LODGroups on 250 m chunks** cost CPU and never cull.
7. **The instanced-prop path** culls on the main thread for one camera and ignores shadow casters outside the frustum.

---

## 4. Bug and flaw register

### 4.1 Open bugs already in `Docs/BUG_TRACKER.md` (summary)

| Severity | IDs | Theme | Phase |
|---|---|---|---|
| Critical | BUG-001, BUG-050 | Bridge abutment steps; falling off the Lantern Strait deck | 0 |
| Critical (mitigated) | BUG-002 | Shader.Find stripping. Fixed in the uncommitted F1 tree; needs commit and a build | 0 |
| High | BUG-003, BUG-004, BUG-026, BUG-027, BUG-034, BUG-035, BUG-037, BUG-038, BUG-049, BUG-051, BUG-052 | Monolithic downloads; greybox city; Halcyon Ascent stuck; IC-04 wall; grades; A19 step; oversized renderers; no LODs; tunnel exits; carriageway colliders; H2 start | 0, 2 |
| Medium | BUG-005, 007, 008, 018 (Simulation), 020, 028, 029, 036, 039, 040, 041, 042, 043, 045, 053, 056–059, 065 | Data placement, Kite art, hitches, materials written in edit mode, chunk naming, surfaces, prop colliders, non-idempotent rebuilds, collision cooking, review-camera issues | 0–4 |
| Low | BUG-006, 033, 046 (now a static cache), 047, 048, 054, 055, 060, 061, 063, 064 | Road kit gaps, UBX splits, material cache, hull limits, neon by day, stripped post shaders, golden hour, lane offset, "SKYLARK" lettering, shared lamp material | 1–4 |
| Security | SEC-001, SEC-002 | Token rotation pending (user) | 0 |

### 4.2 New findings from this audit

These are now in `Docs/BUG_TRACKER.md`.

| ID | Sev. | Finding | Evidence [V] | Fix direction | Phase / card |
|---|---|---|---|---|---|
| **SEC-003** | **High** | A Sketchfab API token was hard-coded as the fallback value of `SKETCHFAB_API_TOKEN` in the untracked `Tools/download_fancy_cars.py`. The file isn't git-ignored, so a `git add -A` would have committed it | `Tools/download_fancy_cars.py:7` (value not reproduced here) | **Fixed 2026-11-10 after this audit:** the fallback is removed and the script exits when the variable is missing (same pattern as the SEC-002 fix). **User:** revoke and regenerate the token | 0 |
| BUG-066 | High | 7 of the 12 real-brand car downloads are NC, NC-SA, NC-ND or Sketchfab Standard. `DOWNLOAD_LIST.md:3` claims "all fictional, CC-BY". The 5 CC-BY rebadges conflict with CLAUDE.md and trade dress. Credit strings show real model titles next to "All car names are fictional" | `license.txt` in each zip; `*.licence.json`; `CarImportTools.cs:162`; `GameFlow.cs:623, 1192` | B1 quarantine; user decision (amend CLAUDE.md or drop the 5); legal review before release; real-brand gate in `CarImportTools` | 0 (B1) |
| BUG-067 | High | Trees and lamps are culled against `Camera.main` or the Scene view, so review and other cameras render none (0 in the 36 S5 shots). Shadow casters outside the main frustum disappear | `World/InstancedPropRenderer.cs:77-107` | Cull per camera in `RenderPipelineManager.beginCameraRendering` and set `RenderParams.camera`, or move to BatchRendererGroup or GRD | 0 |
| BUG-068 | Medium | PC quality has `realtimeReflectionProbes: 0`, so the garage probe never renders. The scene has 0 probes, and the environment reflection is a 128 px skybox that never updates | `QualitySettings.asset:82`; `VeyraCity.unity:35-36, 96`; `GameFlow.cs:1129-1151` | Turn realtime probes on for PC; add box probes and a player-car probe; blend day and night reflections | 4 (L1, L5) |
| BUG-069 | Medium | The GLB along-road UVs are discarded for planar XZ, which streaks kerb and skirt faces | `CityBuilder.cs:660, 799-815` | Keep UV0 on top faces; box-project vertical faces | 3 (W4) |
| BUG-070 | Medium | The terrain corridor is only lowered: hard cut edges, no embankments, and fills leave the slab exposed | `Tools/World/build_world_data.py:284, 330-333` | Cut and fill slopes (about 1:1.5 cut, 1:2 fill) with feathering | 3 (W1, W4) |
| BUG-071 | Medium | Cylinders (92 tanks) and masts get BoxColliders, leaving about 7 m invisible-wall corners | `BuildingGenerator.cs:340` | Capsule or convex colliders by shape | 0 (C3) |
| BUG-072 | Low | Lit-window pattern is phase-locked: 16 states, repeating every 4 × 4 windows on every building | `ProcTextures.cs:186-191`; `BuildingGenerator.cs:82-91` | Per-building seed (UV2 or vertex colour) and a per-window hash in the shader | 2 (A1) |
| BUG-073 | Medium | Street-light pool: off in edit mode, hard pops every 0.3 s, centred 91 m ahead (drops the lamps beside the car) | `WorldEnvironment.cs:222, 257-312` | Fade intensities, centre on the car, about 100 lights | 4 (L4) |
| BUG-074 | Low | MSAA 4x plus SMAA (double cost) and no temporal AA (specular shimmer) | `Settings/PC_RPAsset.asset:28`; `CityBuilder.cs:460` | STP or TAA only | 4 (L1) |
| BUG-075 | Low | `DefaultVolumeProfile.asset` holds test components and a missing-script entry | `Assets/Settings/DefaultVolumeProfile.asset:977` | Clean the profile | 4 (L2) |
| BUG-076 | Low | Road markings have a constant 0.1 emission that is never dimmed (they glow at night) | `CityBuilder.cs:326-327, 99` | No emission; brightness from albedo and wetness | 3 (F2) |
| BUG-077 | Medium | Imported-car dampers scale by √(mass ratio), so heavy cars are underdamped (LCT ζ 0.14 / 0.29) | `Editor/CarImportTools.cs:407-408` | Scale dampers by the mass ratio; re-import | 1 |
| BUG-078 | Medium | The `_ClearCoat` float is 0 on all 29 imported paint materials while `_CLEARCOAT` is on. Material re-validation would drop the clear coat | `CarImportTools.cs:258-260`; e.g. `Art/Vehicles/bokaroo_67/Materials/M_Paint_bokaroo_67_0.mat` | Set `_ClearCoat = 1`; have `MaterialAuditor` check keyword and property consistency | 1 |
| BUG-079 | Medium | `dragScale` is used as a top-speed limiter: CdA 2.18–3.84 m² gives 0.42 g lift-off deceleration at 250 km/h | `ArtSource/Vehicles/car_map.json`; `CarImportTools.cs:377-439` | Realistic CdA; limit speed with power and gearing | 1 |
| BUG-080 | Low | Tyre smoke stays brown after a soft surface | `Gameplay/VehicleFX.cs:264-268` | Set the colour every frame | 1 |
| BUG-081 | Low | Flame colour is written into one shared static material for every car every frame (the last car wins); throws a null reference if the library is missing | `VehicleFX.cs:324` | MaterialPropertyBlock | 1 |
| BUG-082 | Medium | Rate-dependent filters: G smoothing is a per-step lerp of 0.3; wall yaw × 0.4 per contact step. Behaviour at 50 and 120 Hz differs from 100 Hz (CLAUDE.md requires both) | `VehicleController.cs:163, 558-559` | Use 1 − e^(−dt/τ); test the arcade layer at 50 and 120 Hz | 4 (G1) |
| BUG-083 | Low | Kerb camera shake comes from the per-render-frame change in compression, so there is less shake at high FPS | `Gameplay/ChaseCamera.cs:166-171` | Use the fixed-step compression velocity | 4 (G1) |
| BUG-084 | Medium | Nitro caps the thrust but not the torque multiplier: top-speed gain +10.5 / 15.7 / 23.8 % against the spec's +5 / 7 / 10 % | `Nitro/NitroSystem.cs:256-263` | Fade the multiplier at capV; add test T14 | 4 (G1) |
| BUG-085 | Low | One `System.Random` is shared across 4 audio-thread callbacks (not thread-safe); squeal pitch is fixed | `Gameplay/VehicleAudio.cs:29, 40, 107, 143` | One generator per callback; pitch follows slip | 4 (G4) |
| BUG-086 | Low | `HandlingFeelTests` test `CreateInstance` defaults, not the shipped asset. The slalom test name ("TurnsHarderThanSim") contradicts the measured yaw (0.66 vs 1.53 rad/s) | `Tests/EditMode/HandlingFeelTests.cs:27` | Load `HandlingFeel_Arcade.asset`; assert the real intent | 1 |
| BUG-087 | Medium | Hard-coded tuning breaks "every tuning value in a versioned ScriptableObject": controller damping and limits, drift thresholds, P-gains, wall yaw, assist gains, Nitro and DriftScorer tables, body-lean gains | `VehicleController.cs:163-167, 195-196, 418, 620-623, 675, 682`; `Assists/AssistProcessor.cs:123, 135`; `Nitro/NitroSystem.cs:24-29`; `Nitro/DriftScorer.cs`; `Gameplay/VehicleBodyLean.cs` | Move them into `HandlingFeel`, `NitroTuning` and `DriftTuning` assets | 1, 4 |
| BUG-088 | Low | Car wheels sit outside the LODGroup: always drawn (up to 36k tris on Iron Howl) and still visible after the body culls | `CarImportTools.cs:371` | Wheel LODs inside the group | 1 |
| BUG-089 | Low | `TuningVersion` bumps on every import (v12_goblin is already at v4); 5 catalogue definitions are named "(Clone)" | `CarImportTools.cs:432`; `Data/Vehicles/Catalog/*.asset` | Bump only when the values change; fix the names | 1 |
| BUG-090 | Medium | The performance harness records neither SetPass and draw calls, triangles, nor GPU and CPU frame time. VSync is always on (quantised frame times), and the runs used a laptop GPU, not the desktop target | `World/Testing/VeyraAutoTest.cs`; `Logs/stats.json` | ProfilerRecorder (Batches, SetPass Calls, Triangles) plus `FrameTimingManager` GPU and CPU times in `stats.json`; a `-vsync 0` switch; a run on a desktop RTX 4070 | 0 (V0, V1) |

### 4.3 Code smells and design conflicts (not tracked as bugs)

- **The world palette lives outside `VeyraMaterialLibrary`.** Its materials are scene references made by `CityBuilder.Mat()` (`:261-305`). F1 should finish the move.
- **Single-level LODGroups on 250 m chunks** (`BuildingGenerator.cs:446-447`, `CityBuilder.cs:734-738`). They are pure overhead; replace them with per-building LODs and HLOD (part of BUG-038).
- **Sun shadow distance is 160 m, but 72 buildings are over 100 m tall** (max 342 m). Skyline shadows pop.
- **The arcade layer adds grip against the handling spec ("assists never add grip").** Needs a decision (Phase 0).
- **`MAP_MODELS_CATALOG.md` has wrong data.** It lists Cyberpunk City as golukumar (the archive says mortalityrexotable) and the Isparta block as "Local / Photogrammetry" (no licence). It also lists the ripped GTA map as a usable model.
- **Hidden dependency:** `ProceduralCar` still builds 5 catalogue cars at runtime, so their visuals bypass the material library rules.

---

## 5. AAA transformation roadmap

**Ground rules:**
- One playbook card at a time.
- Every world-changing card ends with Veyra/Review/Capture All Cameras Now and a written critique.
- A V1 release run must show p99 ≤ 16.6 ms and no hitch > 50 ms.

Estimates assume one developer working with Claude Code. They are ranges, not promises.

### Phase 0: Gate (prerequisite, about 1–2 weeks; partly already in progress)

The brief starts at ingestion. Ingesting content into a world that can't be driven end to end, measured or legally shipped would multiply rework, so these steps come first.

| # | Step | Card | Exit criterion |
|---|---|---|---|
| 0.1 | Remove the token fallback in `Tools/download_fancy_cars.py:7`; the user revokes and regenerates the token | SEC-003 | `git grep` and a file grep find no token; the script exits without the variable |
| 0.2 | Let the in-progress F1 work finish and commit it (material library, variant collection, 0 `Shader.Find`) | F1 | Dev and release players: `shaderNull=0`; `MaterialAuditor` clean; tests green |
| 0.3 | **Licence gate.** Quarantine the 7 forbidden car zips, the GTA map archives, Isparta and Plaza (CC-BY-SA). User decisions on the 7 Sketchfab Standard kits and the 5 real-brand rebadges (amend CLAUDE.md or drop them). Write `Docs/ASSET_SOURCES.md`. Correct `DOWNLOAD_LIST.md` and `MAP_MODELS_CATALOG.md` | B1 | Every accepted file has a sidecar; `triage.json` gives a mode for each file |
| 0.4 | **Drivability.** Decision D-5, then the eased road profile (C2). Fix BUG-049, 050, 051, 052, 071; add Kerb, Cobblestone and Metal surfaces (BUG-042) | C1, C2 | R_SMOKE 2/2, R_BRIDGES 14/14, R_DOWNTOWN_NIGHT 5/5; 0 invisible walls; 0 falls |
| 0.5 | **Measurement.** Add GPU and CPU frame time, SetPass and draw calls and triangles to `stats.json`; add `-vsync 0`; fix BUG-067 so review shots show the real world | V0 follow-up | `stats.json` has the new fields; S5 shots show trees and lamps |
| 0.6 | Record the two design decisions: the arcade layer against the spec, and real-brand cars against CLAUDE.md | — | Decisions written in `PROJECT_STATUS.md` |

### Phase 1: Ingest the cars and city kits as Unity prefabs (about 2–3 weeks)

**1A. Cars (32 downloads → the licensed subset):**
1. **Fix the import before importing more:**
   - BUG-077 (damper scaling), BUG-078 (clear coat), BUG-079 (drag), BUG-088 (wheel LODs), BUG-089.
   - LOD ratios 40 / 10 %.
   - 3–5 `UCX_` hulls.
   - Split the body into paint, glass, per-function lamps, interior, chrome, rubber and carbon (also fixes BUG-064).
2. **Re-run the batch:**
   - `python -I Tools/Blender/process_all_cars.py --jobs 3`
   - Unity menu **Veyra/Vehicles/Import Downloaded Cars**.
   - Then `Tools\build_dev.cmd` and the A8 showcase run.
3. **Replace the 5 `ProceduralCar` catalogue cars** with models: the 5 rebadges only if 0.6 allows them, otherwise original or commissioned cars.
4. **Fill the hypercar tier** (Asphalt's top class) with 2–3 original or commissioned fictional hypercars. Don't use the NC Bugatti, Porsche, Lamborghini or McLaren files.
5. **Acceptance:**
   - Every car has LOD0/1/2 at 100/40/10 % (± 5 %), collision hulls, separate lamp functions, a clean `MaterialAuditor` run and a licence register row.
   - Catalogue performance and handling tests re-run for every car (`catalog_performance.txt`, `HandlingFeelTests` against the shipped asset).

**1B. City kits (23 → about 7–9 accepted CC-BY kits, plus any Standard kits the user accepts):**
1. **B2:** `blender -b --factory-startup -P Tools/Blender/process_city_map.py -- --input <pack> --mode kitbash`.
   - Output: one FBX per building or prop in `Assets/_Veyra/Art/Models/Kitbash/<source>/VC_KB_<source>_<type>_<n>.fbx`.
   - Each has a base-centre pivot, LOD0/1/2 and `UBX_`/`UCX_` collision.
   - Textures are power-of-two and BC7, with a material map to `VeyraMaterialLibrary`.
   - Start with the smallest pack (Realistic Chicago, 11.6k tris), then Downtown Set, Brownstone, CCity, Atlanta, Modular City Builder, Japanese Street.
   - Monolithic packs (Cyberpunk City) only after segmentation and the provenance check.
2. **B3:** `VeyraModelPostprocessor` (colliders by prefix, static flags, surface definitions, remapping to the library materials, orange debug material for misses).
3. **New:** a **`KitbashCatalog` ScriptableObject** built by an editor tool.
   - One entry per prefab: footprint (w × l), height, floor height, style tags (GlassModern, ArtDeco, Brick, Storefront…), district tags, licence ID, tris per LOD, collider type.
4. **Acceptance:**
   - A contact sheet of every catalogue entry.
   - No pink or missing materials.
   - Each prefab LOD0 ≤ the A2 module budgets.
   - The licence ID resolves to a register row.

### Phase 2: Replace the procedural box buildings (about 4–8 weeks, district by district)

**Prerequisites:** card W7 streaming cells (one scene per 1 km cell, Addressables) and card P1 basics (GPU Resident Drawer on, LOD crossfade, HLOD tool). Without them, Phase 2 breaks the performance budget (§3.E).

**Architecture:** replace the shell builder in `BuildingGenerator.cs` with a **`BuildingAssembler`** that keeps the same inputs (the placement CSV records) and the same idempotence and chunking rules (`Chunk_<Cell>_<i><j>`, stable prefab GUIDs instead of regenerated meshes). For each of the 2,708 records, in this order:

1. **Landmark:** a hero asset from card A4 (never generic).
2. **Kitbash fit:**
   - Pick a `KitbashCatalog` entry whose style tag matches the record's `style_kit` and whose footprint fits within ±15 % after a uniform scale of 0.9–1.1.
   - Take the yaw from the data and add a 2 m plinth below ground.
   - Seeded choice, with no identical neighbours.
3. **Modular recipe:** card A2 kit modules on card A1 trim sheets, assembled by typology recipe (T1–T7 plus the style kits). This covers every footprint the catalogue can't fit, and **all D5 suburban houses**.
4. **Facade shader:** an interior-mapping window shader (`SG_Facade`: room atlas, random room and lit state per window from the building seed and `_VC_NightFactor`), with frame depth from a normal map. This also fixes BUG-072.
5. **LOD and HLOD:**
   - Per-building LOD0/1/2.
   - Per-cell HLOD: merged far mesh with baked facade, one material.
   - Remove the single-level chunk LODGroups.
6. **Validation:**
   - No building overlaps a road or sidewalk collider (skip or shrink, and log it).
   - No floating.
   - Building count per district matches the data.

**District order:** D1 → D6 → D2 → D5 → D3 → D7 → D8 → D4 (card A3 order). D1 and D6 are where the downloads help most. D5 is the biggest job and depends on the original Suburban kit.

**Acceptance per district:**
- A Q2 review score of at least 4.0 (Day, Golden, Night) with the S5 cameras.
- A release run with p99 ≤ 16.6 ms and SetPass ≤ 450.
- LOD0 ≤ 300k tris and ≤ 12 materials per chunk.
- BUG-004 closed for that district.

### Phase 3: PBR materials for roads, wetness, neon and car paint (about 3–4 weeks; cards F2, F3, W4, W5, L5)

1. **Road generator (W4 and W5).** Generate road meshes from `road_network_geometry.json` and the C2 eased profile, replacing the blockout slabs:
   - crown, superelevation, gutters, bevelled and drop kerbs, sidewalk crossfall;
   - along-road UVs;
   - vertex colour channels: R wheel-track wear, G edge grime, B puddle mask, A patch or age;
   - colliders from the same profile (no step > 0.05 m);
   - generated junction patches (BUG-027, BUG-069).
2. **Decals:** turn on the URP Decal renderer feature and add the A1 decal atlas: cracks, patches, tar seams, manholes, tyre marks, stains. Placement is seeded from the road data (density by class; more wear at junctions and bus stops).
3. **Global weather properties (F3):**
   - `_VC_Wetness`, `_VC_Rain`, `_VC_NightFactor` are set once per frame as global shader properties, which closes BUG-039.
   - Wetness darkens albedo by porosity, raises smoothness, and fills puddles from the vertex-colour B channel plus world-space noise.
   - A ripple normal flipbook in rain.
   - Specular anti-aliasing on wet surfaces.
4. **Neon (F2, A6):**
   - The Appendix B palette at HDR 2.5–8, scaled by `_VC_NightFactor` (about 10 % by day, closes BUG-048).
   - Sign groups get proxy lights within the Forward+ budget.
   - Original fictional brands only.
5. **Facade materials:** mask and normal maps generated from the window masks, so glass, frame and wall each get their own smoothness and metallic values. Per-building tint through vertex colour.
6. **Car paint and parts (§3.B B3):**
   - flake micro-normal, two-tone and livery masks, dirt and wear;
   - glass with Fresnel tint and rain;
   - brushed rims, painted calipers, carbon weave under clear coat;
   - per-function lamp emission.
7. **Acceptance:**
   - `MaterialAuditor` checks albedo against the Appendix B calibration (asphalt 0.05–0.12 linear, concrete 0.25–0.40…).
   - A `T_MaterialLab` capture.
   - 0 runtime material instances created per frame.

### Phase 4: Lighting, post-processing and camera feel (about 3–4 weeks; cards L1–L5, G1, G4)

1. **Renderer (L1):**
   - GPU Resident Drawer and GPU occlusion on.
   - STP or TAA instead of MSAA + SMAA (BUG-074).
   - Decals, half-resolution SSAO, screen-space lens flare.
   - Shadows at 300–400 m.
   - Realtime reflection probes allowed on PC (BUG-068).
2. **Reflections (L5):**
   - Baked box probes per block (D1 and D6 first).
   - A time-sliced realtime probe following the player car.
   - An SSR spike, kept only within 1.5 ms.
   - Day and night reflection blending.
3. **GI:** APV per cell with sky occlusion and day/night lighting scenarios.
4. **Post (L2):**
   - HDR grading; per-district and per-time volumes following Appendix B (Neon Mile cyan/magenta with amber accents; Downtown mixed warm and cool windows).
   - Bloom with HQ filtering and an original lens dirt.
   - A scripted exposure curve.
   - Clean `DefaultVolumeProfile` (BUG-075).
5. **Sky and atmosphere (L3):**
   - Cloud layers and city glow.
   - Height fog that accounts for camera altitude (BUG-059).
   - Volumetric fog with local-light scattering in D6 and D3 at night (≤ 0.6 ms).
   - Golden-hour timing (BUG-060).
6. **Night (L4):**
   - About 100 faded lights around the car (BUG-073).
   - Distant light sprites for all 2,777 lamps and lit windows.
   - Headlights on near AI cars (cookie spots) and sprites far away.
7. **Camera (G1):**
   - A Cinemachine 3 rig with deoccluder and collision.
   - Body-relative lag for weight; dutch angle in drifts; landing and impact impulses.
   - Nitro radial blur and a chromatic aberration pulse (accessibility toggle).
   - Fix BUG-082, 083 and 084; give nitro its "shockwave" burst, and reward 360s and barrel rolls if the design wants Asphalt-style stunts.
8. **Audio (G4):**
   - Recorded engine sets under licence register L-011 (no recordings from other games); load and rpm crossfades.
   - Tyre squeal pitched by slip; surface roll noise per `SurfaceDefinition`; scrapes, shifts, bottoming.
   - District ambience.
9. **Acceptance:**
   - A Q2 review of all 18 cameras with an average ≥ 4.0 in D1 and D6 at night.
   - A release V1 run on the desktop target: GPU ≤ 13 ms, CPU ≤ 10 ms, post ≤ 1.5 ms, fog ≤ 0.6 ms, no hitch > 50 ms.

---

## 6. Immediate first action

**Do these in this order. The first is a two-minute security fix.**

**1. File to change first: `Tools/download_fancy_cars.py`, line 7 (SEC-003). Done 2026-11-10.**
- The hard-coded token fallback is replaced with the same pattern as the SEC-002 fix: the script reads `SKETCHFAB_API_TOKEN` and exits if it is missing.
- Still open (user action): revoke and regenerate the token at sketchfab.com → Settings → Password & API.

**2. First command: the B1 quarantine (Phase 0.3).** This keeps the forbidden downloads out of every later batch. Run it from `E:\VeyraCity` after the user confirms the list:

```powershell
New-Item -ItemType Directory -Force ArtSource\Downloads\_Quarantine | Out-Null
Move-Item ArtSource\Downloads\Bugatti_*.zip, ArtSource\Downloads\Porsche_911_GT3_RS_2023_f17a982d.zip, ArtSource\Downloads\Lamborghini_Terzo_Millennio_Concept_7ad3dffa.zip, ArtSource\Downloads\BMW_M6_GT3_RaceCar_27956e4e.zip, ArtSource\Downloads\McLaren_P1_Hypercar_747cedad.zip, ArtSource\Downloads\GtaLC_2001_FULL_MAP_6695e98d.zip -Destination ArtSource\Downloads\_Quarantine
```

**3. First engineering change: `Assets/_Veyra/World/InstancedPropRenderer.cs:75-110` (BUG-067).**
- Move the culling from `LateUpdate` against `Camera.main` to `RenderPipelineManager.beginCameraRendering`, per camera, and set `RenderParams.camera`.
- It is about 30 lines and makes the review cameras show the world the player actually sees: 67,499 trees and 2,777 lamps. Every later phase is judged from those shots.
- Verify it with Veyra/Review/Capture All Cameras Now.
- This doesn't overlap the F1 files in progress. Only start it after F1 is committed, because both touch world runtime code and must be checked in a player build.

After these, follow Phase 0 in order: F1 commit, decisions D-5 and 0.6, then C1/C2. Phase 1 starts once the V0 routes pass.

---

## Appendix: evidence index

| Topic | Files |
|---|---|
| Review shots | `Screenshots/2026-11-10_S5/`, `Screenshots/2026-11-10_S5_player/contact_sheet.png`, `Screenshots/2026-11-10_A8/new_cars_ingame.png` |
| Performance | `Logs/stats.json`, `Logs/V0/baseline_summary.md`, `Logs/A8/showcase_player.log`, `Logs/A8/catalog_performance.txt` |
| Licences | `ArtSource/Downloads/*/license.txt` (inside the zips), `ArtSource/Downloads/*.licence.json`, `Docs/THIRD_PARTY_LICENSE_REGISTER.csv`, `Docs/AUDIT.md` §9 |
| Data validation | `Logs/S3/validate_data.txt` |
| Budgets and cards | `Docs/VEYRA_CLAUDE_CODE_PROMPTS.md` (cards; Appendix B, C, E) |
| Earlier visual research | `Docs/VISUAL_BENCHMARK_REPORT.md` |
