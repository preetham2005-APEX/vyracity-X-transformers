# AUDIT — VEYRA CITY baseline before the playbook (card S2)

**Date:** 2026-11-09 · **Baseline:** tag `baseline-before-playbook` = commit `6911501` · **Unity:** 6000.6.8f1 (URP 17.6.0) · **Blender:** 5.2.3 LTS (headless) · **Unity CLI:** 1.0.0-beta.13 (beta.17 available, update awaits the user's approval)

S2 changes nothing in the game. It records what exists. Evidence lives on disk (Logs/ and Screenshots/ are not in git):

| Evidence | Path |
|---|---|
| Scene statistics | `Logs/S2/scene_stats.txt` |
| Play-mode baseline, bridge probe, console, Editor FPS | `Logs/S2/playmode_baseline.txt` |
| Baseline screenshots (spawn, bridge, downtown) | `Screenshots/2026-11-09_S2/S2_1…S2_3_*.png` |
| Downloads: archive listing, licence text, triangle counts | `Logs/S2/downloads_inventory.json`, `downloads_licences.csv`, `downloads_triangles.json`, `downloads_table.md` |
| Last player-build run (visual pass) | `Logs/visual3/stats.json` |

---

## 1. Git and Git LFS

| Check | Result |
|---|---|
| Git / Git LFS | 2.52.0.windows.1 / git-lfs 3.7.1. No remote is configured (not pushed anywhere). |
| LFS patterns the card requires (`*.fbx *.blend *.glb *.png *.tga *.psd *.exr *.raw *.wav *.ogg`) | All tracked. Also LFS: gltf, obj, jpg, tif, hdr, mp3, mp4, ttf, otf, dll, bank, vmesh, `WorldSource/terrain/*.bin` and **everything under `Assets/_Veyra/World/Generated/**`** (including `.mat` and `.meta` text files, so Unity's YAML merge cannot work there). `*.raw` is listed twice. 8,125 LFS files, 528 MB in `.git/lfs`; pack 6 MB. |
| `.gitignore` | Library, Temp, Obj, Logs, Builds, UserSettings already ignored. **Screenshots was not ignored** (11 files from the visual pass are tracked). S2 added `/[Ss]creenshots/`; the 11 tracked files stay tracked. |
| `VeyraCity.unity` | Plain-text YAML, not LFS; the visual-pass commit changed ~859k lines of it. |
| Tag | `baseline-before-playbook` → `6911501` (visual upgrade pass + SEC-002 fix). |
| Working tree at audit time | `Assets/_Veyra/World/Generated/Materials/Marking_Yellow.mat` modified (not by S2; see F-07), `Docs/OPUS_MASTER_MAP_PROMPT.txt` untracked, two untracked screenshot folders from earlier visual passes. |
| Secret scan | SEC-002: a Sketchfab API token was hard-coded in 7 `Tools/*.py` scripts (commit `e65856d`). Fixed in `6911501` (environment variable); the user must regenerate the token. |

## 2. Packages (`Packages/manifest.json`)

| Package | Manifest | Resolved | CLAUDE.md wants it |
|---|---|---|---|
| com.unity.render-pipelines.universal | 17.6.0 | 17.6.0 | yes |
| com.unity.inputsystem | 1.20.1 | 1.20.1 | yes |
| com.unity.cinemachine | 3.1.7 | **6.6.0** (Editor-bundled) | yes (Cinemachine 3); installed but **unused** (custom `ChaseCamera`) |
| com.unity.probuilder | 6.1.4 | 6.1.4 | yes (blockouts only) |
| com.unity.ai.navigation | 2.0.16 | 2.0.16 | yes; **unused** (no NavMesh code) |
| com.unity.splines | — | 2.9.1 (dependency) | yes; not a direct dependency |
| com.unity.mathematics / burst / collections | — | 1.4.0 / 2.0.0 / 6.6.0 (dependencies) | yes; not direct dependencies |
| com.unity.nuget.newtonsoft-json | — | 3.2.2 (dependency) | needed by S3 |
| com.unity.shadergraph | — | 17.6.0 (via URP) | needed by F2 |
| com.unity.addressables | **missing** | — | yes (W7) |
| com.unity.cloud.gltfast | **missing** | — | yes (S4) |
| com.unity.terrain-tools | **missing** | — | yes (W1/W2) |
| com.unity.recorder | **missing** | — | yes (G1) |
| com.unity.memoryprofiler | **missing** | — | yes (P2) |
| com.unity.pipeline | 0.10.0-exp.1 | 0.10.0-exp.1 | experimental; drives the Editor from the CLI (risk R-17) |
| com.unity.test-framework | 1.4.5 | 1.8.0 | — |
| Others | collab-proxy 2.13.6, ide.rider 3.1.0, ide.visualstudio 2.0.28, ugui 2.6.0, built-in modules | | |

## 3. Render pipeline, quality and graphics settings

- **Graphics settings:** `m_CustomRenderPipeline` is empty; each quality level assigns its own URP asset. Always Included Shaders: the 7 Unity built-in defaults only (fileIDs 7, 15104, 15105, 15106, 10753, 10770, 10783). **URP/Lit is not in Always Included** (good). Preloaded shaders: none. No `ShaderVariantCollection` exists.
- **Quality levels:** `Mobile` (Mobile_RPAsset) and `PC` (PC_RPAsset); current = PC. Both lodBias PC 2 / Mobile 1, maximumLODLevel 0.
- **Player:** Linear colour space, Windows APIs default (D3D12 used at runtime), Mono for iteration (BuildScript switches to IL2CPP per build), bundle version `0.1.0-foundation`, company `DefaultCompany`.

| URP setting | PC_RPAsset | Mobile_RPAsset |
|---|---|---|
| Renderer | PC_Renderer: **Forward+** (mode 2), SSAO (Blue Noise method 0, intensity 1.2, radius 0.8, falloff 100, direct 0.25) | Mobile_Renderer |
| HDR / precision | on / 32-bit | on / 32-bit |
| MSAA / render scale / upscaler | 4x / 1.0 / Auto | 4x / 0.8 / Auto |
| Depth / opaque texture | on / on | off / off |
| Main light shadows | 2048, distance **160 m**, 4 cascades (12.3 / 29.3 / 53.6 %), soft High | 1024, distance **160 m**, 4 cascades — overwritten by CityBuilder (see F-10) |
| Additional lights | per-pixel, 4 per object, shadows on (2048 atlas) | per-pixel, shadows off |
| SRP Batcher / dynamic batching | on / off | on / off |
| GPU Resident Drawer / GPU occlusion | **off / off** (L1 turns on) | off / off |
| Colour grading mode | **LDR** (L2 should use HDR grading) | LDR |
| LOD cross-fade | on | on |

- **Time:** Fixed Timestep 0.01 s (100 Hz), Maximum Allowed Timestep 0.1 s.

## 4. Code inventory

**Assemblies** (all under `Assets/_Veyra`, one asmdef each): Core → []; VehiclePhysics → [Core]; Input → [Core, VehiclePhysics, InputSystem]; Assists → [Core, VehiclePhysics]; Nitro → [Core, VehiclePhysics, Assists]; World → [Core, URP]; Racing → [Core, VehiclePhysics, World, Assists]; AI → [Core, VehiclePhysics, Assists, World, Racing, Nitro]; UI → [Core, VehiclePhysics, Assists, Nitro, Racing, World, uGUI, InputSystem]; Gameplay → everything; Editor → everything; Tests.EditMode, Tests.PlayMode.
**Rules check:** VehiclePhysics references only Core (✔). Input uses the Input System through `IDriverIntentSource` (✔). AI racers produce `DriverIntent` (`AIRacerDriver : IDriverIntentSource`) (✔); traffic cars are kinematic and do not (✗, by design for now). Every MonoBehaviour/ScriptableObject lives in a file named after its class (✔, 26 checked).

| Script | Lines | Purpose |
|---|---|---|
| AI/AIRacerDriver.cs | 223 | AI racer: pure pursuit on the racing line, speed √(μg/κ)·skill, ±5 % catch-up, nitro on straights; an `IDriverIntentSource` |
| AI/ProceduralCar.cs | 254 | Builds placeholder procedural car bodies (runtime materials, `_EMISSION` toggled) |
| AI/TrafficCar.cs | 116 | One traffic car: kinematic lane following on a road polyline until hit, then free physics |
| AI/TrafficSystem.cs | 172 | Spawns and recycles up to 36 traffic cars around the player; near-miss events |
| Assists/AssistPreset.cs | 29 | ScriptableObject: assist settings (Casual / Advanced) |
| Assists/AssistProcessor.cs | 130 | Steering filter, counter-steer, TCS, stability, ABS, drift assist on the intent |
| Assists/VehicleDriver.cs | 56 | Order −50: intent source → assists → `VehicleController.SetControls` |
| Core/BuildInfo.cs | 41 | Build metadata (`StreamingAssets/build_info.json`) |
| Core/DeviceReport.cs | 36 | Device/runtime report logged at startup |
| Core/FrameStats.cs | 52 | Frame-time statistics (avg, p50/p95/p99, hitches) |
| Core/LaunchOptions.cs | 42 | Parses `-veyraAutoQuit/-veyraScreenshot/-veyraScreenshotAt/-veyraStats/-veyraWarmup/-veyraOverlay` |
| Core/VeyraBootstrap.cs | 129 | Auto-created bootstrap: frame stats, overlay, screenshots, auto-quit; **writes stats.json (`:114`)** |
| Core/WorldCoordinates.cs | 30 | Data ↔ Unity conversion (x − 4000, z, y − 3000) |
| Editor/BuildingGenerator.cs | 448 | 2,708 buildings, 6,050 container stacks, 8 cranes from `Data/*.csv` into `Chunk_<Cell>_<i><j>` with `UBX_` colliders |
| Editor/BuildScript.cs | 108 | Batch build `BuildWindows -veyraBackend mono\|il2cpp [-veyraDevelopment]` |
| Editor/CarImportTools.cs | 135 | Imports processed downloaded cars into `Art/Vehicles/Downloaded` (no licence gate; never run) |
| Editor/CityBuilder.cs | 1120 | Rebuilds `VeyraCity.unity` from `WorldSource`: URP config, GameDatabase, materials, terrain, roads, colliders, buildings, grass, water, props |
| Editor/CollisionPrefixPostprocessor.cs | 62 | `UCX_`/`UBX_` → colliders for vehicle art (no `COL_`) |
| Editor/GrassBuilder.cs | 171 | Terrain grass detail |
| Editor/ProcTextures.cs | 264 | Procedural PBR textures (asphalt, granite, facades…), MetalGloss packing (smoothness in A) |
| Editor/SceneTools.cs | 69 | Foundation scene and build-scene registration |
| Editor/TestTrackBuilder.cs | 549 | Builds the physics test track scene |
| Editor/VehicleAssetTools.cs | 199 | Arden Kite import, prefab, LODs, colliders |
| Editor/VehicleDataTools.cs | 220 | Creates vehicle/surface/assist data assets |
| Gameplay/ChaseCamera.cs | 85 | Custom chase camera |
| Gameplay/GameData.cs | 397 | Save (`profile.json`), upgrades, career (hard-coded data) |
| Gameplay/GameDatabase.cs | 24 | ScriptableObject: cars, material templates, presets for runtime |
| Gameplay/GameFlow.cs | 1149 | State machine (Boot / MainMenu / FreeRoam / Garage / Race / Results), wiring, review shots, demo/tour |
| Gameplay/ScriptedDriver.cs | 21 | Scripted intent source (tests) |
| Gameplay/TelemetryRecorder.cs | 83 | CSV telemetry (F7) |
| Gameplay/TestTrackDirector.cs | 147 | Test-track scene director, `-veyraDemoDrive` |
| Gameplay/VehicleAudio.cs | 193 | Procedural engine and tyre audio |
| Gameplay/VehicleFactory.cs | 116 | Spawns cars from definitions/prefabs |
| Gameplay/VehicleFX.cs | 308 | Tyre smoke, nitro flames, skid marks (runtime particle materials) |
| Gameplay/VehicleTestHarness.cs | 81 | Handling test harness |
| Input/DriverInputFilter.cs | 56 | Keyboard rate limits, stick dead zones |
| Input/PlayerDriverInput.cs | 100 | Input System → `DriverIntent` |
| Input/VeyraControlsJson.cs | 554 | Default bindings as a JSON string |
| Nitro/DriftScorer.cs | 136 | Drift chains, scoring, nitro charge |
| Nitro/NitroSystem.cs | 253 | Nitro meter, 3 modes (Standard, Clean Boost 0.60–0.85 s, Overdrive); mode table hard-coded |
| Racing/RaceSession.cs | 453 | Race flow, checkpoints (~180 m), positions, wrong way, respawn, rewards; `Drag`/`Drift` kinds have no logic |
| UI/HudController.cs | 385 | HUD: speed, gear, revs, nitro, race info, drift chains, minimap |
| UI/MenuController.cs | 252 | Menus (runtime-built uGUI) |
| UI/TelemetryOverlay.cs | 79 | IMGUI telemetry overlay (F3) |
| UI/UIKit.cs | 194 | Runtime uGUI helpers (LegacyRuntime font) |
| VehiclePhysics/DriverIntent.cs | 50 | `DriverIntent`, `IDriverIntentSource`, `VehicleControls` |
| VehiclePhysics/Powertrain.cs | 158 | Engine, clutch, gearbox |
| VehiclePhysics/SurfaceDefinition.cs | 12 | ScriptableObject: Id, GripMultiplier, RollingResistanceMultiplier |
| VehiclePhysics/TyreModel.cs | 65 | Combined-slip Magic Formula tyre |
| VehiclePhysics/VehicleController.cs | 586 | Raycast vehicle in FixedUpdate: suspension, anti-roll, diffs, ABS/TCS, aero, nitro thrust |
| VehiclePhysics/VehicleDefinition.cs | 136 | Versioned tuning ScriptableObject (`TuningVersion`, `TuningHash`) |
| VehiclePhysics/VehicleState.cs | 60 | Per-wheel state (telemetry source) |
| VehiclePhysics/VehicleSurface.cs | 10 | Tags a collider with a SurfaceDefinition |
| World/InstancedPropRenderer.cs | 126 | GPU-instanced trees and lamps per cell (no colliders) |
| World/RuntimeMaterials.cs | 84 | Runtime material factory from GameDatabase templates, `Shader.Find` fallback |
| World/WorldData.cs | 333 | Loads `Resources/World/*.json`; road/route polylines; `GroundHeight` |
| World/WorldEnvironment.cs | 299 | `[ExecuteAlways]` time of day, weather, fog, post, emissive/wet material driving, light pool, rain |
| Tests (5 files) | 914 | EditMode: CoreTests, VehicleAssetTests, VehicleMathTests (43 tests). PlayMode: HandlingMatrixTests, TelemetryCaptureTests (21 tests). No tests for world build, roads, nitro, races, AI, traffic, save or runtime materials |

### 4.1 Detailed reads (the card's list)

- **WorldEnvironment.cs** (`World/`): `[ExecuteAlways]` (`:14`). `Update` runs in edit mode too and calls `Apply`, which writes **shared material assets** every frame: emissive colours (`:194-199`), wet-road smoothness and base colour (`:201-206`), skybox exposure/thickness/tint (`:181-183`) and the water `_BumpMap` offset (`:125-126`). Bloom threshold 1.25 by day → 0.9 at night (`:189-190`, the BUG-031 fix). Fog is exponential² (`:172-177`). Rain uses `RuntimeMaterials.NewParticle` (`:308`).
- **CityBuilder.cs** (`Editor/`): see §6. Rebuilds the scene from scratch (`NewScene`, saved over `VeyraCity.unity`), so objects are not duplicated, but ~3,800 generated mesh assets are deleted and recreated with new GUIDs on every run, every URP asset is reconfigured, and vehicle/AI tuning assets are overwritten from hard-coded editor lambdas (`:204-215`, `:236-259`).
- **GameFlow.cs** (`Gameplay/`): 1,149 lines. Material templates initialised in `Awake` (execution order −1000). `new Material` per renderer per spawn for paint (`:387`, never destroyed), event markers (`:537`), garage box at y = 1500 with `_EMISSION` toggled (`:926-931`). Review shots and tour via `-veyra*` flags (`:226-241`).
- **VehicleController.cs** (`VehiclePhysics/`): `FixedUpdate` at 100 Hz; wheel raycast `Physics.RaycastNonAlloc(origin, dir, hitBuffer, maxDistance, ~0, QueryTriggerInteraction.Ignore)` (`:558`) — **the LayerMask is all layers (`~0`), not "Default" as CLAUDE.md says**; it skips its own rigidbody. Surface read: `GetComponentInParent<VehicleSurface>()` on the hit collider, cached per collider (`:570-580`). `ResetTo` zeroes velocities (`:603-611`). Some constants are hard-coded: angular damping 0.05 and max angular velocity 30 (`:111-113`), TCS trim (`:313`), ABS rates 14/6 (`:347-348`), `LowSpeed 3` (`:25`).
- **SurfaceDefinition.cs** (`VehiclePhysics/`): Id, GripMultiplier, RollingResistanceMultiplier. Assets in `Data/Surfaces`: dry_asphalt 1.00, wet_asphalt 0.75, concrete 0.95, gravel 0.60, dirt 0.65, grass 0.50. **Kerb, Cobblestone and Metal (CLAUDE.md's list) do not exist**; kerbs and sidewalks use Concrete; the whole terrain (sand, rock, paving layers too) is tagged Grass (`CityBuilder.cs:585`). Rain clones dry asphalt (`GameFlow.cs:145-149, 561-562`).
- **stats.json writer:** `Core/VeyraBootstrap.cs:114`, enabled by `-veyraStats <path>`. Fields today: build (version, git commit, Unity version, backend, development), gpu, API, resolution, quality, vSync, exceptions, frames {count, seconds, avg, p50, p95, p99, max ms, 1 % low FPS, hitches > 50 ms}. V0 adds the vehicle, memory, GC and per-segment fields.

## 5. Runtime material and shader calls (non-Editor, non-Test code)

| Call | Location |
|---|---|
| `Shader.Find` | `World/RuntimeMaterials.cs:24-25` (URP/Lit, FallbackError), `:42-44` (Particles/Unlit, URP/Unlit, Particles/Standard Unlit), `:64-65` (URP/Unlit, FallbackError) — fallbacks when GameDatabase templates are null |
| `new Material(...)` | `World/RuntimeMaterials.cs:20, 26, 38, 47, 60, 68`; `Gameplay/GameFlow.cs:387` (paint instance, leaks), `:537` (marker) |
| Indirect (`RuntimeMaterials.NewLit/NewUnlitTransparent/NewParticle`) | `AI/ProceduralCar.cs:17`; `Gameplay/GameFlow.cs:518, 927`; `Gameplay/VehicleFX.cs:113, 116, 263`; `World/WorldEnvironment.cs:308` |
| Runtime keyword toggles | `_EMISSION`: `AI/ProceduralCar.cs:24`, `Gameplay/GameFlow.cs:931`. `_SURFACE_TYPE_TRANSPARENT`: `World/RuntimeMaterials.cs:86`, `Gameplay/VehicleFX.cs:128`. No `VC_Variants.shadervariants` covers them |
| Shared-asset writes at runtime and in edit mode | `World/WorldEnvironment.cs:125-126, 181-183, 194-206` |
| `Resources.Load` | `World/WorldData.cs:206` (`Resources/World/*.json` TextAssets) |
| `VeyraMaterialLibrary` | **does not exist** (CLAUDE.md requires it; card F1) |

## 6. World build pipeline (as built)

1. `Tools/World/build_world_data.py` reads the design package and writes `Assets/_Veyra/Resources/World/{roads,routes,locations,districts}.json` (road profiles copied 1:1, **no grade or step validation**), 48 modified terrain RAWs (road corridors lowered 0.25 m; 44 of 48 tiles now differ from the package) plus tunnel-portal hole masks into `WorldSource/terrain`, `WorldSource/meshes/D1–D8.vmesh` (roads/structures/water from the district blockout GLBs; Z negated, winding flipped) and copies of the 16 placement CSVs (`WorldSource/placement`, hashes identical to the package). `WorldSource/build_report.txt`: 16 race routes rebuilt within 0–2.8 % of design length.
2. `CityBuilder.Build()` (menu) creates the scene, 48 `Terrain_<cell>` tiles (513², height −60…+500), road/sidewalk/marking/structure meshes bucketed by triangle centroid into `CityMeshes/Cell_<x>_<z>` on a 250 m grid that is **not aligned to the 1 km cells**, one non-convex MeshCollider per surface per bucket, buildings via BuildingGenerator into `Buildings/Chunk_<Cell>_<i><j>`, grass, a 24 × 20 km sea plane, district water, and instanced props.
3. Road surfaces come from the blockout GLB vertices (+0.012 m); AI, traffic, races and terrain lowering use the profile. **The S2 probe on A1 shows the two agree within 0.06 m**, so the bridge steps are in the source profile (BUG-001), not introduced by the build.
4. Data facts that matter for later cards (from the design package and `validate_world.py`): registry and lamp heights are **terrain** heights, wrong on structures (30 locations > 2 m off the deck, 81 lamps); 7 locations outside their district; no yaw for vegetation/locations; no checkpoints; no interchange ramp geometry; the package has building placements for D1 only (282). `Assets/_Veyra/Data/world_building_placements.csv` (2,708 rows, the "data supplement" used by the visual pass) is **not part of the design package**; its provenance must be recorded in S3.

## 7. Scene `Assets/_Veyra/Scenes/VeyraCity.unity` (opened in the Editor, `Logs/S2/scene_stats.txt`)

| Metric | Value |
|---|---|
| Root objects | 6: World (13,624 children), Sun, PostProcess, Environment, Main Camera, Game |
| GameObjects | 13,631 |
| MeshFilters / MeshRenderers | 3,050 / 3,050 (SkinnedMeshRenderer 0) |
| Triangles in MeshFilters | 1,281,716 (GPU-instanced trees/lamps and terrain grass come on top) |
| Largest single meshes | 6,400 tris (`M_concrete_3_-6`, 255 × 116 m), 6,074, 4,778, 4,203, 3,750 … all road/structure buckets |
| Colliders | MeshCollider non-convex 750, convex 0; BoxCollider 8,790; TerrainCollider 48; Capsule 0 |
| LODGroups | 1,733, **none with more than one LOD** |
| Terrains / Lights / Cameras / Reflection probes | 48 / 1 / 1 / **0** |
| Renderers wider than 250 m | **1,207**: Sea 24,000 × 20,000 m; inland water D1–D8 1.5–6 km; `steel` structure buckets up to 506 m; many road buckets 251–266 m |
| Grouping | `CityMeshes/Cell_#_#` × 441 vs `Buildings/Chunk_<Cell>_<ij>` × 270 (A 15, B 53, C 50, D 39, E 30, F 45, G 34, H 4) |
| Build Settings scenes | VeyraCity.unity, PhysicsTestTrack.unity, Foundation.unity |

## 8. Baseline run (Editor, `Logs/S2/playmode_baseline.txt`)

- Editor loaded with **0 errors, 0 warnings, no compile failure**.
- Play mode 138 s, frames advancing (`job wait` on `Time.frameCount`). Free roam at noon, clear weather.
- **Spawn** `S2_1_spawn_GAR01_1200.png`: car at (−1999.9, 17.1, −851.2) = GAR-01. Westhaven: rows of identical box houses with red roofs, grass, lamps, skyline. Greybox.
- **Bridge** `S2_2_bridge_A1_CrownBridge_abutment_1200.png`: A1 Crown Bridge south abutment. The road rises 6.91 m within 8 m (17.53 → 24.44 m at s = 1624 → 1632) and drops 7.77 m within 16 m at the north end. On screen: a ~7 m wall of asphalt. Neon strips glow cyan/magenta across the river at noon.
- **Downtown** `S2_3_downtown_A1_CrownAvenue_1200.png`: Crown Avenue. Markings correct (double yellow centre, white lane dashes). Towers are extruded boxes with a tiled window texture; the curtain-wall tower renders near-black at noon (no reflection probes); a magenta neon strip is lit by day.
- Console after play: **0 errors, 3 warnings** — `UCX_Body_01/02/03` (Arden Kite) exceed the 256-polygon convex limit, so partial hulls are used.
- Editor FPS (reference only): **197.5 FPS** average over 10.3 s at a 1472 × 860 game view; HUD showed 128–142 FPS at 979 × 526.
- Last **player** evidence (visual pass, Dev Mono, 2560 × 1440, 107 s, `Logs/visual3/stats.json`): avg 117.1 FPS, p99 **18.28 ms**, max 229.7 ms, 1 % low 54.7 FPS, **11 hitches > 50 ms**, 0 exceptions. Fails CLAUDE.md (p99 ≤ 16.6 ms, no hitch > 50 ms); a release IL2CPP run at 1440p does not exist yet (V0).
- Observation, not yet a bug: after each `ResetTo` the HUD showed 20–39 km/h (once in reverse) with no scripted input; some device input reached the car. V0's autotest owns the input and will settle it.

## 9. ArtSource/Downloads (Blender 5.2.3 headless cross-check + glTF accessor counts)

Triangle counts come from each archive's `scene.gltf` (walking the node hierarchy, so instances count). Headless Blender import of two archives gave identical numbers (Modular_Roads_Pack 1,461; Bokaroo_67 28,293). Licences come from the `license.txt` that Sketchfab puts inside each archive. **No licence sidecar file exists next to any download**, `DOWNLOAD_LIST.md` and `MAP_MODELS_CATALOG.md` are empty (0 bytes), and none of this content is in `Assets/` or `WorldSource/` (grep: no hits). Total 1,544.7 MB in 47 files.

Summary: **33 CC-BY-4.0**, **7 Sketchfab Standard** (not Creative Commons), **1 CC-BY-SA-4.0** (ShareAlike), **1 with no licence** (Isparta photogrammetry GLB), **3 ripped GTA Liberty City maps** (two copies of one zip + a second archive; also present in the design repo root and extracted there) — excluded, not opened. Scale check: Modular_Roads_Pack imports at 585 × 588 × 0.31 m (wrong unit scale).

| # | File | Size MB | Triangles | Meshes / mats / tex | Licence (from archive `license.txt`) | Author | Note |
|---|---|---|---|---|---|---|---|
| 1 | `Bokaroo_67_22015d18.zip` | 9.8 | 28,293 | 26 / 8 / 15 | CC-BY-4.0 | Daniel Zhabotinsky |  |
| 2 | `Canyon_75_Taxi_9e8f92a2.zip` | 11.0 | 20,029 | 28 / 8 / 14 | CC-BY-4.0 | Daniel Zhabotinsky |  |
| 3 | `Coupe_33_HotRod_1_1f00f875.zip` | 10.1 | 17,551 | 25 / 8 / 13 | CC-BY-4.0 | Daniel Zhabotinsky |  |
| 4 | `Coupe_33_HotRod_2_c880d700.zip` | 10.1 | 18,268 | 25 / 8 / 13 | CC-BY-4.0 | Daniel Zhabotinsky |  |
| 5 | `Doutrive_78_30700a84.zip` | 11.3 | 24,581 | 29 / 8 / 13 | CC-BY-4.0 | Daniel Zhabotinsky |  |
| 6 | `Fairheaven_Cop_Cruiser_61766c1e.zip` | 14.3 | 19,501 | 36 / 8 / 13 | CC-BY-4.0 | Daniel Zhabotinsky |  |
| 7 | `Fairheaven_LT_80_e2678da9.zip` | 14.1 | 19,705 | 32 / 8 / 12 | CC-BY-4.0 | Daniel Zhabotinsky |  |
| 8 | `Generic_Passenger_Car_Pack_20f9af9b.zip` | 23.4 | 69,312 | 75 / 41 / 39 | CC-BY-4.0 | Comrade1280 |  |
| 9 | `GtaLC_2001_FULL_MAP_6695e98d.zip` | 23.9 | not opened |  | none |  | **Ripped commercial-game map (GTA Liberty City). Excluded: never open or import.** |
| 10 | `Illinois_90_d604871e.zip` | 10.2 | 19,328 | 32 / 8 / 13 | CC-BY-4.0 | Daniel Zhabotinsky |  |
| 11 | `Iron_Howl_V8_47a36113.zip` | 131.1 | 409,100 | 16 / 7 / 21 | CC-BY-4.0 | dark_igorek |  |
| 12 | `Kiri_86_5c17c582.zip` | 12.0 | 20,314 | 30 / 8 / 13 | CC-BY-4.0 | Daniel Zhabotinsky |  |
| 13 | `Lasley_67_1269720e.zip` | 10.8 | 21,034 | 30 / 8 / 13 | CC-BY-4.0 | Daniel Zhabotinsky |  |
| 14 | `LCT_3000_95_663a0953.zip` | 10.2 | 18,939 | 27 / 8 / 13 | CC-BY-4.0 | Daniel Zhabotinsky |  |
| 15 | `Negotiator_80_892b2684.zip` | 10.2 | 19,765 | 28 / 8 / 13 | CC-BY-4.0 | Daniel Zhabotinsky |  |
| 16 | `Orion_Skylark_GT_b1cc3110.zip` | 121.8 | 492,740 | 18 / 8 / 22 | CC-BY-4.0 | dark_igorek | single mesh 104,872 tris |
| 17 | `Phoenix_455_71_955edc73.zip` | 10.3 | 25,943 | 30 / 8 / 13 | CC-BY-4.0 | Daniel Zhabotinsky |  |
| 18 | `Riverside_88_8509a7f8.zip` | 9.8 | 19,331 | 30 / 8 / 13 | CC-BY-4.0 | Daniel Zhabotinsky |  |
| 19 | `Shvan_92_09d718c9.zip` | 11.2 | 23,045 | 24 / 8 / 13 | CC-BY-4.0 | Daniel Zhabotinsky |  |
| 20 | `Tiara_GT_83_Tuned_59a0eaaa.zip` | 10.1 | 18,547 | 35 / 8 / 13 | CC-BY-4.0 | Daniel Zhabotinsky |  |
| 21 | `V12_Goblin_Supercar_0a20e49a.zip` | 7.1 | 86,623 | 16 / 5 / 9 | CC-BY-4.0 | ollitei |  |
| 22 | `FullMapModels/Asian_Buildings_Set_d3d7c14c.zip` | 82.3 | 299,228 | 382 / 100 / 197 | SKETCHFAB Standard | golukumar | Sketchfab Standard (not CC): decision needed in B1 |
| 23 | `FullMapModels/Atlanta_Corporate_Office_d96380fb.zip` | 20.3 | 37,290 | 12 / 12 / 23 | CC-BY-4.0 | 99.Miles |  |
| 24 | `FullMapModels/Brownstone_Building_Set_b15e6344.zip` | 20.8 | 68,356 | 70 / 14 / 30 | CC-BY-4.0 | Daniel Zhabotinsky |  |
| 25 | `FullMapModels/CCity_Building_Set_1_a2d5c7bf.zip` | 11.1 | 31,050 | 1357 / 22 / 12 | CC-BY-4.0 | Neberkenezer |  |
| 26 | `FullMapModels/City_Road_Layout_Mockup_3260e3fe.zip` | 38.6 | 8,036 | 11 / 11 / 22 | CC-BY-4.0 | 99.Miles |  |
| 27 | `FullMapModels/Cyberpunk_City_3f24e5c5.zip` | 86.9 | 1,257,909 | 68 / 16 / 12 | CC-BY-4.0 | mortalityrexotable | monolithic > 1 M tris |
| 28 | `FullMapModels/Downtown_Buildings_Set_7378e7fb.zip` | 25.5 | 45,560 | 43 / 16 / 44 | CC-BY-4.0 | Daniel Zhabotinsky |  |
| 29 | `FullMapModels/Full_Gameready_City_HongKong_31c1ede1.zip` | 61.8 | 368,217 | 488 / 52 / 49 | SKETCHFAB Standard | abhayexe | Sketchfab Standard (not CC): decision needed in B1 |
| 30 | `FullMapModels/Full_Gameready_City_Retexture_5fad2351.zip` | 31.5 | 763,348 | 229 / 100 / 101 | CC-BY-4.0 | mortalityrexotable |  |
| 31 | `FullMapModels/Futuristic_City_688a1866.zip` | 69.1 | 754,516 | 83 / 47 / 49 | SKETCHFAB Standard | golukumar | Sketchfab Standard (not CC): decision needed in B1; single mesh 122,364 tris |
| 32 | `FullMapModels/Futuristic_Dystopian_City_925230e8.zip` | 12.2 | 601,430 | 450 / 49 / 25 | SKETCHFAB Standard | golukumar | Sketchfab Standard (not CC): decision needed in B1 |
| 33 | `FullMapModels/gtalc-2001-full-map-3dmodel.zip` | 83.9 | not opened |  | none |  | **Ripped commercial-game map (GTA Liberty City). Excluded: never open or import.** |
| 34 | `FullMapModels/GtaLC_2001_FULL_MAP_6695e98d.zip` | 23.9 | not opened |  | none |  | **Ripped commercial-game map (GTA Liberty City). Excluded: never open or import.** |
| 35 | `FullMapModels/isparta_city_block_turkey.glb` | 59.0 | 399,999 | 16 / 16 / 16 | none |  | No licence evidence |
| 36 | `FullMapModels/Japanese_Street_At_Night_fb1bdcd7.zip` | 103.2 | 51,687 | 149 / 34 / 107 | CC-BY-4.0 | AFX/CGMotion 3DModel Maker |  |
| 37 | `FullMapModels/Modern_City_Block_c80dba24.zip` | 19.8 | 61,243 | 52 / 7 / 21 | SKETCHFAB Standard | akselmot | Sketchfab Standard (not CC): decision needed in B1 |
| 38 | `FullMapModels/Modular_City_Builder_Starter_Kit_784358d2.zip` | 29.3 | 198,524 | 321 / 2 / 3 | CC-BY-4.0 | Pieter Ferreira |  |
| 39 | `FullMapModels/Modular_Roads_Pack_99023ee0.zip` | 1.5 | 1,461 | 12 / 2 / 2 | CC-BY-4.0 | Mini Loft Games | imports at 585 × 588 m: wrong scale |
| 40 | `FullMapModels/Neighbourhood_City_Modular_6a0e3b97.zip` | 44.6 | 415,293 | 1321 / 25 / 60 | SKETCHFAB Standard | golukumar | Sketchfab Standard (not CC): decision needed in B1 |
| 41 | `FullMapModels/New_York_City_4737b6ee.zip` | 66.3 | 310,151 | 79 / 48 / 67 | CC-BY-4.0 | golukumar |  |
| 42 | `FullMapModels/NY_Highway_Interstate_95_f668f36f.zip` | 112.5 | 112,024 | 47 / 47 / 64 | SKETCHFAB Standard | 99.Miles | Sketchfab Standard (not CC): decision needed in B1 |
| 43 | `FullMapModels/Plaza_Night_Time_282f4973.zip` | 31.1 | 1,476,258 | 47 / 3 / 5 | CC-BY-SA-4.0 | patrix | ShareAlike: decision needed in B1; monolithic > 1 M tris |
| 44 | `FullMapModels/Realistic_Chicago_Buildings_c4a3993e.zip` | 2.6 | 11,643 | 5 / 5 / 8 | CC-BY-4.0 | 99.Miles |  |
| 45 | `FullMapModels/San_Francisco_City_10884175.zip` | 24.1 | 92,578 | 20 / 7 / 26 | CC-BY-4.0 | Abimael Gonzalez |  |

A CC-BY licence on Sketchfab is the uploader's claim. B1 still has to check each car for real-brand likeness (several Zhabotinsky "low poly" cars echo real models) and each city kit for game-ripped content before anything enters the project. `Tools/Blender/process_city_map.py` already refuses files without a `.licence.json` sidecar and names containing GTA/Cyberpunk; `process_downloaded_car.py` has no licence gate.

## 10. CLAUDE.md compliance summary

| Rule | State |
|---|---|
| No runtime `Shader.Find` / materials from shader names; `VeyraMaterialLibrary` | ✗ (§5) → F1 |
| Runtime keywords covered by `VC_Variants.shadervariants` | ✗ file missing → F1 |
| URP Lit packing: smoothness in metallic alpha, normal maps marked | ✔ for procedural textures (`ProcTextures.cs:220-229`); one-pixel seam in asphalt/granite normals (`:93, 233`) |
| Chunk naming `Chunk_<Cell>_<i><j>` | buildings ✔, roads/structures/colliders ✗ (`Cell_x_z`) → B2/W7 |
| Folder layout (`Assets/_Veyra/Data`, `ArtSource/VeyraData`) | ✗ data in `Resources/World` and `WorldSource/` → S3 |
| LOD0/1/2 on every mesh; no mesh > 250 m or > 300k tris | ✗ no LOD1/LOD2 anywhere; 1,207 renderers > 250 m; tris per mesh ✔ (max 6,400) |
| Drivable surfaces: non-convex MeshCollider + SurfaceDefinition; wheel mask | ✔ 750 non-convex colliders with `VehicleSurface`; mask is `~0`, CLAUDE.md says Default; Kerb/Cobblestone/Metal surfaces missing → C1 |
| Road grades per class; no step > 0.05 m | ✗ BUG-001, BUG-034, BUG-035 → C2 |
| Nothing floats or blocks a road | ✗ trees/lamps have no colliders; lamp/registry heights wrong on structures (BUG-041); bridge walls (BUG-001) |
| Tuning in versioned ScriptableObjects | partly: VehicleDefinition ✔; nitro table, upgrades, AI preset, career and several controller constants hard-coded |
| Editor tools idempotent | objects ✔ (scene rebuilt), assets ✗ (GUID churn, settings overwritten) |
| Licence file before third-party content enters the project | nothing third-party has entered ✔; no sidecars exist yet → B1 |
| No secrets in git | ✗ SEC-002 (fixed in HEAD; token still to be regenerated) |
| Performance target (p99 ≤ 16.6 ms, no hitch > 50 ms) | ✗ last measured dev build p99 18.28 ms, 11 hitches; no release 1440p run → V0/V1 |

## 11. Top 5 risks

1. **Road data is not drivable as authored.** Bridge abutment steps up to 8.5 m on 8 of 9 river bridges, grades up to 25 % on MT roads and 21.5 % sustained on A1, a 53 m step on A19. C2 can only reach "0 violations" by easing the profile away from the raw data, which is a design decision (D-5).
2. **Licensing and originality.** 1.5 GB of downloads: 3 ripped GTA maps, 7 Sketchfab-Standard and 1 ShareAlike city kits, 1 unlicensed photogrammetry block, cars that may echo real models; no sidecars; and a pasted brief that assumes "all CC-BY". One wrong import breaks the project's core rule.
3. **Performance and scale.** No LODs, 1,207 renderers wider than 250 m, a 24 km sea plane, GPU Resident Drawer off, 11 hitches > 50 ms and p99 18.3 ms already in a dev build, before the art pass (A cards) adds detail.
4. **Runtime material architecture.** `Shader.Find` fallbacks, runtime `new Material`, keyword toggles without a variant collection, and `WorldEnvironment` writing shared assets in edit mode: a player-only failure class (BUG-002 history) that also dirties assets and git.
5. **Non-idempotent world rebuilds.** Each CityBuilder run recreates ~3,800 assets with new GUIDs (≈ 860k-line scene diffs), overwrites URP and tuning assets, and keeps two height sources (blockout mesh vs profile). Long-term it makes review, merging and regression tracking unreliable.

## 12. Follow-ups noted by the audit (not fixed in S2)

- The world-data package has a nested exact duplicate (`VEYRA_CITY_WORLD_DESIGN/VEYRA_CITY_WORLD_DESIGN`, MD5-identical, 187 files), which makes `validate_world.py` double-count.
- CLAUDE.md inaccuracies to correct when the owning card runs: wheel mask is `~0` (C1); grade list lacks RU 10 % and RW 0.8 % (C2); neon in Downtown conflicts with the art-direction guide ("neon only on the Mile").
- Missing features observed: `Drag`/`Drift` race kinds have no logic; traffic does not use `DriverIntent`; Cinemachine and AI Navigation unused.
