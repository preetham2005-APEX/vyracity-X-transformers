# 09_ART_PASS_BENCHMARK — Crown Downtown (benchmark) and Blackridge

**Stage: ART PASS (prototype). Not FINAL.** These images come from the real 3D scene (same footprints, roads, terrain and heights as the approved map). They were rendered with a look-development renderer (WebGL2: shadows, aerial perspective, procedural facades, night lighting, bloom), **not** inside Unity, and **not** profiled on Android.

They are kept separate from concept art (folders 02–05, not generated) and from blockout renders (folder 07).

## Renders

| File | Camera | Time |
|---|---|---|
| renders/br_halcyon_drive_day.jpg | Driving chase cam, Halcyon Pass Road | Day |
| renders/br_highline_drive_gold.jpg | Driving chase cam, Ridgeline Highway / Highline Viaduct | Golden hour |
| renders/br_overlook_golden.jpg | Cinematic: Skyline Overlook VP-01 | Golden hour |
| renders/br_switchbacks_aerial.jpg | Aerial: Corvo Switchbacks | Day |
| renders/dt_aerial_day.jpg | Aerial (same camera as the blockout aerial) | Day |
| renders/dt_aerial_night.jpg | Aerial | Night |
| renders/dt_drive_crown_day.jpg | Driving chase cam, Crown Avenue | Day |
| renders/dt_drive_meridian_night.jpg | Driving chase cam, Meridian Blvd (wet) | Night |
| renders/dt_plaza_closeup_day.jpg | Close-up, pedestrian height, Crown Plaza | Day |
| renders/dt_skyline_golden.jpg | Cinematic: skyline from Crown Bay | Golden hour |
| renders/dt_skyline_night.jpg | Cinematic: skyline from Crown Bay | Night |
| renders/dt_viaduct_night.jpg | Driving chase cam, Harbor Viaduct (R1) | Night |
| renders/compare_downtown_aerial_blockout_vs_artpass.jpg | Before/after, same camera | Day |
| renders/compare_halcyon_driving_blockout_vs_artpass.jpg | Before/after, driving camera | Day |

## Assets

| File | Contents |
|---|---|
| assets/crown_downtown_typology_kit_artpass.glb/.obj | T1–T7 building typologies + Veyra Spire + Exchange Tower (art-pass shapes) |
| assets/crown_downtown_hero_block_artpass.glb | Crown Plaza and surrounding blocks: terrain, roads with markings, gutters, crossings, signals, trees, plaza and fountain, buildings, parked cars (world-aligned) |
| assets/crown_downtown_building_placements.csv | All 282 Downtown footprints → typology, height, seed, Unity coordinates (drive a Houdini/Blender procedural rebuild) |
| assets/vegetation_rock_kit_artpass.glb/.obj | 4 conifers, 4 broadleaf, autumn, dead snag, bush, planted street tree, 3 rocks |
| assets/vehicles_traffic_and_hero_greybox.glb/.obj | Arden Kite stock and street build (before/after proportions), sedan, hatch, SUV, coupe, van, taxi, truck, bus |
| assets/lookdev_facade_shader_reference.glsl | Facade, asphalt, water, foliage and night-lighting shader logic used for these renders, to port to Unity URP |

## What still separates this from FINAL
See `06_DESIGN_DOCUMENTATION/visual_quality_roadmap.md` §3 (gap table), §5 (Android budgets) and §8 (milestones). Main gaps: textured trim sheets, hand-modelled landmarks, production car models, alpha-card vegetation with wind, decals, pedestrians, VFX, audio, in-engine baked lighting and device profiling.