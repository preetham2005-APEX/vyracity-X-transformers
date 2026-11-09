# Veyra City — Visual Quality Roadmap (Blockout → Final)

**The world is not visually complete.** This package holds a validated world layout, Unity-ready terrain and road data, district blockouts, and an **ART PASS prototype** of two benchmark areas (Crown Downtown, Blackridge). None of it is FINAL. This document defines the stages, records where every part of the world stands, and lays out the path to a polished, premium open-world racing game on Android.

---

## 1. Stage definitions (used in `asset_status_registry.csv` and every document)

| Stage | Definition | Exit criteria |
|---|---|---|
| **BLOCKOUT** | Correct position, scale, connectivity and massing. Primitive shapes, flat colours. | Layout approved; validation passes (no gaps, floating objects or broken roads); scale checked from the driving camera. |
| **GREYBOX** | Real silhouettes and modular construction, untextured or placeholder materials. Gameplay-testable. | All modules snap; collision correct; LOD0/1 exist; performance within 1.5× of budget. |
| **ART PASS** | Final-direction materials, lighting, set dressing and atmosphere applied. Shapes can still be procedural. | Matches the art direction guide at all three times of day; reviewed from the four standard cameras; within budget on the mid-tier device. |
| **FINAL** | Production-quality authored assets (hand-finished landmarks, textured trim sheets, real cars), all LODs and impostors, baked lighting, VFX and audio hooks. Profiled on target devices. | Art sign-off at 1× zoom on device; passes performance gates; no visual bugs in the QA checklist. |

---

## 2. Where the world stands now

| Area | Stage | Evidence |
|---|---|---|
| World layout, road network, district boundaries, location registry | **Approved / final as design data** | Master map, validation report (0 errors) |
| Terrain heightmap (48 tiles, roads cut in) and splat masks | **GREYBOX** (production-usable base; needs sculpt and erosion pass) | `08_3D_ASSETS/terrain` |
| Road centrelines and 3D profiles | **GREYBOX** (geometry final-ish; spline tool import pending) | `road_network_geometry.json` |
| Crown Downtown | **ART PASS (prototype)** | 12 benchmark renders, typology kit, hero-block GLB |
| Blackridge Mountains | **ART PASS (prototype)**: vegetation, rocks, guardrails, atmosphere | 4 benchmark renders, vegetation and rock kit |
| Azure Coast, Ironport, Westhaven, Neon Mile, Falcon Airfield, Redwood Valley | **BLOCKOUT** | District blockout GLBs and blockout renders |
| Vehicles | **GREYBOX** (lofted bodies, wheel arches, lights; no interiors, no LODs, no textures) | `vehicles_traffic_and_hero_greybox.glb` |
| Concept art (22 images) | **Not generated**; prompts provided | `image_generation_prompts.md` |

---

## 3. What the art-pass benchmark changed (Crown Downtown)

Same approved footprints, heights and roads; replaced:

- **Architecture:** 282 buildings reassigned to 7 typologies: T1 glass podium tower (60), T2 art-deco stepped (26), T3 twisting tower (10), T4 fin-slab office (34), T5 residential with balconies (85), T6 brick midrise with storefronts and awnings (54), T7 modern lowrise (10). There are 3 rebuilt landmarks: Veyra Spire (octagonal tiers, crown fins, spire), Exchange Tower and Crown Motor Gallery. Podiums, setbacks, chamfers, cornices, roof plant, water tanks, antennas and aircraft lights.
- **Facades:** procedural window grids (floor height and bay width per typology), mullions and spandrels, storefront ground floors, interiors lit at night at about 30 % (warm/cool mix), and distance anti-aliasing. The reference shader is in `09_ART_PASS_BENCHMARK/assets/lookdev_facade_shader_reference.glsl`.
- **Streets:** gutters, drains, zebra crossings and stop lines at every downtown intersection, signal poles, street trees every 15 m, benches and bus shelters, Crown Plaza (granite paving, two-tier fountain), Harbor Viaduct sign gantries, and Crown Interchange slip ramps (flyovers).
- **Life:** about 3,100 traffic and parked vehicles (8 body types, 11 paints), tail-lights and headlights at night.
- **Lighting:** sun plus sky ambient, 4k shadow map, aerial perspective with sun in-scattering, procedural clouds, backdrop ridges, night street-light pools (projected light map), emissive crowns and bloom, wet-road night look.

**Blackridge:** layered conifers (4 variants), broadleaf (4) and autumn trees, dead snags, rock scatter on slopes steeper than 28°, W-beam guardrail on valley sides, delineators every 25 m, tunnel portals, dry/green grass variation, golden-hour atmosphere.

### Honest gap to FINAL (what the benchmark still lacks)
| Gap | What FINAL needs |
|---|---|
| Facades are procedural patterns on flat walls | Trim-sheet textures (albedo/normal/mask), window recesses with interior parallax, real depth, signage, balcony detail, edge wear |
| Landmarks are procedural | Hand-modelled hero landmarks (Veyra Spire, Lantern Strait Bridge, Starlight Theater, gantry crane) with authored materials |
| Cars are lofted greybox shapes | Production car models: about 40–60k triangles for the hero LOD0, interiors, glass layers, brake/suspension detail, damage-free paint shader, 4 LODs |
| Trees are low-poly geometric | Alpha-card foliage (SpeedTree-style or equivalent) with wind animation, LODs and impostors |
| No decals | Cracks, patches, manholes, oil stains, tyre marks, puddles, graffiti, posters |
| No pedestrians, audio or VFX | Crowds at meets and plazas, ambient audio zones, rain, spray, exhaust heat, sparks |
| Lighting is a lookdev approximation | Unity URP baked GI, light probes, reflection probes, time-of-day blending, tuned post-processing |
| No device profiling | Profiling on low, mid and high Android tiers (section 5) |

---

## 4. Production pipeline to FINAL

| Step | Tool | Input from this package | Output |
|---|---|---|---|
| Terrain | Unity Terrain (or Gaea/World Machine erosion first) | 48 RAW tiles + splat masks + manifest | Streaming terrain tiles with 8 layers |
| Roads | Unity Splines (or EasyRoads3D / custom spline tool) | `road_network_geometry.json` (3D profiles), road kit | Road meshes per cell, intersections, markings |
| Buildings | Houdini or Blender Geometry Nodes procedural generator | Building placements CSV + typology rules + trim sheets | Building prefabs with LOD0–2 and HLOD |
| Trim sheets and materials | Substance Designer/Painter | Art direction material table | 2048² atlases per district family |
| Landmarks | Blender (hand modelled) | Landmark blockouts (exact dimensions) | Hero meshes + LODs |
| Vegetation | SpeedTree or alpha-card workflow in Blender | Vegetation and rock kit (proportions), placement CSVs | Trees with wind, LODs and impostors |
| Vehicles | Blender + Substance | Greybox proportions and the before/after spec | Hero and traffic cars, modular part slots |
| Lighting | Unity URP: baked GI, adaptive probe volumes / light probes, reflection probes | Lighting spec per district | Lightmaps per cell, time-of-day presets |
| Streaming | Unity Addressables | 1 km grid A1–H6 | Async-loaded cells |

**Naming:** `VC_<District>_<Category>_<Name>_<LOD>` (for example, `VC_D1_BLD_T1Tower_LOD0`). **Units:** metres, Y-up in Unity. glTF exports here are Y-up with −Z = north. After import, check the `NORTH_ARROW` node; if your importer flips handedness, rotate the root 180° about Y.

---

## 5. Android performance budgets

| Tier (examples) | Target | Draw calls | Visible triangles | Texture memory (total) | Shadows | Post-processing |
|---|---|---|---|---|---|---|
| Low (Adreno 610 / Mali-G57 class) | 30 fps, 720p | ≤ 150 | ≤ 300 k | ≤ 300 MB | 1 cascade, 40 m | Tonemap + colour grade |
| Mid (Adreno 650 / Mali-G78 class) | 30 fps, 900p–1080p | ≤ 250 | ≤ 700 k | ≤ 600 MB | 2 cascades, 120 m | + bloom (half-res) |
| High (Adreno 740+ / Immortalis class) | 60 fps, 1080p+ | ≤ 400 | ≤ 1.5 M | ≤ 1 GB | 2 cascades, 200 m | + soft particles, higher-res reflections |

**Techniques (mandatory):** SRP Batcher + GPU instancing (traffic, trees, lamps, containers) · LOD groups on everything taller than 2 m · HLOD per city block beyond 1 km · baked occlusion culling per cell (critical in Downtown canyons) · impostors for trees and distant towers · mipmap streaming · ASTC 6×6 (4×4 for hero cars and UI) · baked lighting with light probes for dynamic objects · no realtime point lights except headlights (lamp pools are projected cookies) · fog distance tuned per district to cap draw distance.

**Benchmark scene size (measured):** the full Downtown lookdev scene is about 4.3 M triangles plus about 3 M for vegetation, before any LOD. That is far above mobile budgets **by design**: it is a lookdev scene, not a game build. **Implication (estimate, not yet measured on device):** from street level most of that geometry is occluded by the canyon walls or far enough away for HLOD and impostors, so the visible LOD0 set must be brought under the mid-tier budget through the techniques above. Measure this in the M1 vertical slice.

---

## 6. World streaming
- 48 cells of 1 km (A1–H6, matching the map grid and terrain tiles). Load the current cell and its 8 neighbours at LOD0/1. HLOD for rings 2–3. Backdrop meshes for the horizon.
- The fastest roads (R1, C1) need prefetch along the direction of travel (about 300 m look-ahead at 200 km/h).
- Each cell's Addressables group: terrain tile, road meshes, buildings (HLOD + LOD), props (instanced), lighting data.

---

## 7. Camera test protocol (every district, every milestone)
For each district render **aerial, cinematic, driving and close-up** cameras at **day, golden hour and night** (12 images) **from the actual game build**. Run the consistency checklist in `art_direction_guide.md`, then capture GPU frame time and draw calls on one device per tier along the district's main race route. Store results with the build number.

---

## 8. Milestones (beauty before size)
| Milestone | Scope | Stage target |
|---|---|---|
| M0 (this package) | Whole-world layout, terrain, roads, blockouts, Downtown and Blackridge art-pass prototypes | Design approved |
| M1 Vertical slice | Crown Downtown + Harbor Viaduct + Crown Bay waterfront in Unity, 1 hero car, day + night | ART PASS in engine, profiled |
| M2 Benchmark final | M1 brought to FINAL; becomes the visual benchmark for all districts | FINAL |
| M3 First playable world | + Blackridge (Halcyon Pass) + Azure Coast (C1 sunset) + Westhaven starter area | ART PASS → FINAL |
| M4 Full world | Ironport, Neon Mile, Falcon Airfield, Redwood Valley | FINAL |
| M5 Polish | Weather, pedestrians, VFX, audio, optimisation sweep | FINAL |

**Rule:** a smaller, beautiful, fully playable area beats a large unfinished one. Districts that are not at least ART PASS should stay out of the playable build.
