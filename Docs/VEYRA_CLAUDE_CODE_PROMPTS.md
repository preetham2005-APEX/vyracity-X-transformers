# VEYRA CITY — Claude Code Build Playbook (prompts, plain text)

Companion to VEYRA_CLAUDE_CODE_PLAYBOOK.pdf · November 2026. Copy prompts from the `text` blocks.


## How to use this playbook

### Your setup (every prompt is written for this)

| Item | Value |
|---|---|
| Master design and spec repository | `E:\PROJECT BANGERRR` (contains `VEYRA_CITY_WORLD_DESIGN`, zipped or unzipped) |
| Unity project | `E:\VeyraCity`, Unity **6.6 (6000.6.8f1)**, Universal Render Pipeline (URP) |
| Blender | **5.2 LTS**, both headless (`blender.exe -b -P script.py`) and live through the Blender MCP on **port 9876** |
| Claude Code plugins | Unity plugin (skills such as `unity:unity-cli`, `unity:urp-postprocessing`, `unity:physics-3d-collision`, `unity:shader-graph-create-custom-node`, `unity:validate-urp-render-graph-renderer-feature`, `unity:asset-transformer-toolkit`, `unity:initialize-ai-navigation`) and Blender MCP |
| Primary target | **PC, RTX 4070: a steady 60+ FPS at 1440p** with a GTA V daytime realism and Cyberpunk 2077 night-neon *quality bar* |
| Secondary target | Android, as a later port using the Low/Mid tiers in Appendix C |
| Status files | `E:\VeyraCity\Docs\PROJECT_STATUS.md` and `E:\VeyraCity\Docs\BUG_TRACKER.md` |

### Why the world "doesn't build correctly"

Your directive diagnosed the symptoms correctly. There is also a process problem behind them: **one huge prompt asks Claude to do everything at once, with no proof that each step works.** Claude then runs out of context, skips verification, and reports success on code that was never run in a real player build.

| Cause | What you see | Fix in this playbook |
|---|---|---|
| One giant prompt | Thousands of lines of code, half never run; compile errors | **One card = one small, testable step** |
| No visual or runtime proof | Claude says "done", but the build crashes, is pink, or the car falls through | Every card ends with **evidence**: screenshots, logs, `stats.json`, and validator output |
| Only tested in the Editor | `Shader.Find` works in the Editor but returns null in the player | Card **V0** builds and runs a **development player** from the start |
| No single source of truth | Roads, bridges, buildings and terrain don't line up; 4–8 m steps (BUG-001) | All positions and heights come from the **Veyra data files** |
| Monolithic downloaded models | A 1.2M-triangle single mesh, no culling, no collision | **Blender ingestion pipeline**: segment, chunk at 250 m, LODs, collision meshes |
| Long sessions forget decisions | Claude contradicts earlier work | A permanent **CLAUDE.md** and **PROJECT_STATUS.md** |
| No checkpoints | One bad step ruins everything | **A git commit after every passed card** |
| "Make it look like GTA/Cyberpunk" is vague | Flat, generic results | Exact **materials, light values, budgets and camera checks** |

### The build loop (Claude must follow it on every card)

**PLAN → BUILD (small) → COMPILE → RUN → CAPTURE EVIDENCE → CHECK → FIX → COMMIT**

1. Start a **fresh Claude Code session per phase**. Paste the **Session Starter (SS)** first, then **one** card.
2. Let Claude finish. It must compile, run, capture screenshots or logs, and compare them with the card's checks.
3. Read the **Acceptance checks** yourself. If one fails, paste that card's **"If it fails"** prompt, or a matching prompt from the fix-it library (Q3).
4. When a card passes, Claude updates `Docs/PROJECT_STATUS.md` and commits with the card ID.
5. **Run the cards in order.** Each card assumes the earlier ones have passed.

### Recommended order

| Phase | Cards | Goal |
|---|---|---|
| 0. Setup and audit | SS, S1, S2, S3, S4, S5 | Rules, a baseline of what exists, data import, the Blender bridge, review cameras |
| 1. Stability (your directive) | V0, F1, F2, F3 | A player build that runs; shader crash fixed; a PBR, glass and neon material library |
| 2. Ingestion (your directive) | B1, B2, B3 | Downloaded and Blender content → 250 m chunks with LODs and collision |
| 3. Physics (your directive) | C1, C2, C3 | Drivable colliders, surface types, **BUG-001 fixed**, building collisions |
| 4. Atmosphere (your directive) | L1–L5 | URP PC settings, post-processing, fog, day–night cycle, wet roads |
| 5. Verification (your directive) | V1 (repeat after every phase) | Batch build, a 60 s automated drive, 0 exceptions, 60+ FPS |
| 6. World | W1–W8 | Terrain, water, roads, junctions, structures, streaming, map |
| 7. Assets | A1–A10 | Trim sheets, facade kits, buildings, landmarks, props, neon, vegetation, cars |
| 8. Districts | DS1–DS3 | Crown Downtown vertical slice first, then Neon Mile at night, then the other districts |
| 9. Life and polish | G1–G5, P1–P3, Q1–Q3 | Traffic, pedestrians, audio, performance, QA |

> **Copy-paste tip:** copying from a PDF can add line breaks. Claude won't mind, but the companion file **VEYRA_CLAUDE_CODE_PROMPTS.md** has the same prompts as clean text.

### What I changed in your directive, and why

Your directive is preserved in full: every path, file name, bug and target value appears in the cards below. These points were corrected, because as written they would cause new failures:

| # | Directive said | Problem | Playbook uses |
|---|---|---|---|
| 1 | Put shaders in Always Included Shaders | Adding URP/Lit there compiles thousands of variants: very long builds and a much larger build. Runtime keyword changes (such as `_EMISSION` at night) can still be stripped. | **Serialized material assets** referenced from a `MaterialLibrary` asset in the scene, plus a **ShaderVariantCollection**. `Shader.Find` is allowed only in Editor code. Always Included only for small custom shaders (F1). |
| 2 | Roughness in alpha for URP Lit | URP Lit reads **Smoothness** (= 1 − roughness), not roughness. | Invert roughness when packing; Smoothness Source = Metallic Alpha (F2). |
| 3 | Volumetric / exponential height fog | URP has distance fog only. Volumetric and height fog are HDRP features unless your Unity version added one. | Built-in exponential² fog plus a custom **height-fog full-screen pass** (Render Graph). Claude checks first for a built-in option in 6.6 (L3). |
| 4 | `UCX_Road` collision for roads | `UCX_` means *convex* (an Unreal convention) and Unity ignores the prefix. A convex road collider is wrong. | An **AssetPostprocessor** that turns `COL_` into a non-convex MeshCollider, `UCX_` into a convex one and `UBX_` into a BoxCollider, and hides the collision renderers (B3). |
| 5 | Blender "-Z forward, Y up" | Correct for single props. For **world-positioned** content, the FBX/glTF handedness flip can turn the whole city **180°**, swapping north and south. | A mandatory **north/east arrow round-trip test** before any chunk is exported. The confirmed preset is recorded in CLAUDE.md (S4). |
| 6 | 250 m chunks | Good for culling, but they must nest inside the **1 km streaming cells** (A1–H6). | `Chunk_<Cell>_<i><j>`: 16 chunks per 1 km cell (B2, W7). |
| 7 | Process downloaded 3D maps | (a) **Licence risk**: ripped GTA or Cyberpunk content must never enter the project. (b) A downloaded city won't match the Veyra road network. | A **licence gate** (needs a licence sidecar). Downloads become **kitbash libraries** or are aligned to Veyra data; the JSON road network stays the authority (B2). |
| 8 | Smooth bridge steps (BUG-001) | The usual root cause is that the deck height and the approach height come from different sources. | One continuous profile from `road_network_geometry.json`, **grade-limited easing**, a **profile validator** and an **automated drive test** (C2). |
| 9 | "0 exceptions, 60+ FPS" | Not repeatable without a fixed route and defined metrics. | An **autopilot test route**; `stats.json` with average FPS, 1 % low, p99 frame time and the number of hitches > 50 ms (V0, V1). |
| 10 | Roads on layer `Default` | Fine if the wheel raycast mask uses it. | Claude **reads the vehicle's LayerMask first**. A dedicated `Drivable` layer is optional later (C1). |

### Honest expectations

- **Claude Code + Unity + Blender does well:** pipelines, tools, terrain, roads, junctions, modular buildings, landmarks to exact dimensions, props, shaders, lighting, post-processing, streaming, traffic, QA and performance work.
- **AI-driven modelling struggles with:** production hero cars and human characters. Cards A8–A10 still give you good greybox/mid quality. For a hero car or pedestrians at GTA level, **license or commission** them (Appendix F).
- **Scale:** GTA V and Cyberpunk 2077 took hundreds of developers and years. The realistic path is a **stunning vertical slice**: Crown Downtown by day plus Neon Mile at night, then expand district by district.
- **Originality:** match their *quality*, not their content. **Never import ripped assets, map layouts, names, logos, music or UI from GTA or Cyberpunk.** Everything here is original Veyra City.


## Phase 0 — Session starter, rules and audit

### SS — Session Starter (paste at the start of EVERY Claude Code session)

*Tools: Unity + Blender | Time: 1 min | Needs: none*

```text
You are the Lead World Architect, Technical Artist and Engine Developer for VEYRA CITY, an original open-world driving game.
Environment: Unity 6.6 (6000.6.8f1) URP project at E:\VeyraCity. Master design/spec repository at E:\PROJECT BANGERRR (contains VEYRA_CITY_WORLD_DESIGN). Blender 5.2 LTS, usable headless (blender.exe -b -P script.py) and live through the Blender MCP on port 9876. Target: PC with RTX 4070, steady 60+ FPS at 1440p, with a AAA open-world quality bar (GTA V daytime realism, Cyberpunk 2077-style night neon in Neon Mile and Downtown); all content original.
Before doing anything:
1. Read E:\VeyraCity\CLAUDE.md and follow it strictly. If it does not exist, stop and tell me.
2. Read Docs\PROJECT_STATUS.md and Docs\BUG_TRACKER.md to see which playbook cards are done and which bugs are open.
3. Work ONLY on the card I give you next. Do not start other cards or refactor unrelated systems.
4. Use the build loop for every step: short plan → small change → compile (zero errors) → run → capture evidence (Unity Game/Scene screenshots via the review-camera tool or Unity tools; Blender viewport screenshots via the Blender MCP; logs; validator output) → look at the evidence critically → fix → repeat.
5. Runtime-critical changes (shaders, materials, physics, streaming, performance) must also be verified in a Development PLAYER build (card V0 harness), not only in the Editor.
6. Never report a step as done unless it compiles, the scene opens, and you have inspected the evidence. Quote the evidence (numbers, log lines, screenshot critique).
7. When the card's acceptance checks pass: update Docs\PROJECT_STATUS.md (card ID, date, what changed, evidence paths, known issues), update Docs\BUG_TRACKER.md if a bug changed state, then git commit "<CARD-ID> <title> ✅".
8. If blocked (missing package, MCP not responding, ambiguous instruction, licence unclear), stop and ask me one clear question instead of guessing.
Use the Unity plugin skills where they fit (unity:unity-cli for batch mode/builds, unity:unity-package-management, unity:urp-postprocessing, unity:physics-3d-collision, unity:shader-graph-create-custom-node, unity:validate-urp-render-graph-renderer-feature, unity:initialize-ai-navigation, unity:asset-transformer-toolkit).
Reply "Ready", then list completed cards and open bugs.
```

### S1 — Create CLAUDE.md (permanent project rules)

*Tools: Unity | Time: 5 min | Needs: git repository at E:\VeyraCity*

```text
Card S1. Create E:\VeyraCity\CLAUDE.md with exactly the rules below (merge with any existing CLAUDE.md: keep existing rules unless they contradict these, and show me the diff). Commit it.

# VEYRA CITY — rules for Claude Code
## Project
- Original open-world driving game. Fictional Veyra City, 8 x 6 km. Districts: D1 Crown Downtown, D2 Azure Coast, D3 Ironport, D4 Blackridge Mountains, D5 Westhaven, D6 Neon Mile, D7 Falcon Airfield, D8 Redwood Valley.
- Quality bar: AAA open world (GTA V realism by day; Cyberpunk-style neon at night in Neon Mile and Downtown). All content original: no ripped or copied assets, names, logos, music, UI or map layouts from other games.
- Unity 6.6 (6000.6.8f1), URP, Forward+, Linear colour, HDR. Packages: Cinemachine 3, Input System, Splines, Addressables, AI Navigation, glTFast, Terrain Tools, ProBuilder (blockouts only), Mathematics, Burst, Collections, Recorder, Memory Profiler.
- Target: PC RTX 4070, 1440p, 60+ FPS (frame time p99 ≤ 16.6 ms, no hitch > 50 ms). Android is a later port (Low/Mid tiers).
## Paths
- Unity project E:\VeyraCity. Design repo E:\PROJECT BANGERRR (read-only reference, do not edit).
- Keep the existing folder layout. Existing: Assets/_Veyra/World (runtime world code, e.g. WorldEnvironment.cs), Assets/_Veyra/Editor (CityBuilder.cs and tools), Assets/_Veyra/Scenes/VeyraCity.unity.
- New content goes to: Assets/_Veyra/Data (CSV/JSON TextAssets), Assets/_Veyra/World/Generated/Materials (material library), Assets/_Veyra/Art/{Materials,Textures,Models,Prefabs,Shaders,VFX,Terrain}, Assets/_Veyra/Art/World/Chunks (processed map chunks), Assets/_Veyra/Scenes/{Cells,Test}.
- Outside Assets: Tools/Blender (Python), ArtSource/Downloads (raw third-party downloads + licence files), ArtSource/Blender (.blend sources), ArtSource/VeyraData (terrain RAW tiles), Docs, Logs, Builds, Screenshots.
## Source of truth
- Veyra data (location_registry.csv, road_network_geometry.json, terrain manifest + RAW tiles, placement CSVs) is the authority for every position and height. Never invent coordinates. Downloaded models never override it.
- 1 unit = 1 m. Data uses x east, y north, z up. Unity = (x - 4000, z, y - 3000). +X east, +Z north.
- Grid: 1 km streaming cells, columns A–H west→east, rows 1–6 north→south (A1 = x 0–1000, y 5000–6000). Render/collision chunks are 250 m and nest inside cells: Chunk_<Cell>_<i><j>, i = column 0–3 west→east, j = row 0–3 south→north within the cell.
## Materials and shaders
- Runtime code must NEVER call Shader.Find or create materials from shader names. Materials are serialized assets in Assets/_Veyra/World/Generated/Materials, referenced via the VeyraMaterialLibrary ScriptableObject that a scene object holds. Shader.Find is allowed only in Editor code.
- Shader keywords toggled at runtime (e.g. _EMISSION) must be covered by a material in the build or by Assets/_Veyra/Art/Shaders/VC_Variants.shadervariants.
- URP Lit packing: Smoothness = 1 - roughness, stored in Metallic alpha (Smoothness Source = Metallic Alpha). Normal maps marked as Normal Map. Emission in HDR.
## Blender → Unity
- Use Tools/Blender/vc_tools.py. Export presets VC_Unity_Prop (local pivot) and VC_Unity_World (world-positioned chunks) as confirmed by the S4 axis round-trip test (record the final axis settings here after S4). Never export world-positioned content before S4 passes.
- Pivot: buildings and props at base centre; road modules at the start of the piece; chunks at the chunk's south-west corner at height 0.
- LOD naming: <name>_LOD0/_LOD1/_LOD2. Collision naming: COL_<name> = non-convex MeshCollider (roads, terrain-like surfaces), UCX_<name> = convex MeshCollider, UBX_<name> = BoxCollider. Collision meshes never render.
## Physics
- Drivable surfaces: non-convex MeshCollider, on the layer(s) included in the vehicle wheel-raycast LayerMask (currently Default; read the vehicle code before changing). Each drivable collider maps to a SurfaceDefinition (Asphalt, Kerb, Cobblestone, Concrete, Dirt, Gravel, Grass, Metal).
- Road heights come only from the validated road profile; max grade per class (MW 5 %, EX 8.5 %, HW 9 %, AR 8 %, CO 10 %, MT/GR 15 %, SV 5 %), no vertical step > 0.05 m between adjacent collider surfaces.
## Quality rules
- Every mesh has LODs (LOD0, LOD1 ≈ 40 % tris, LOD2 ≈ 10 %), except small props < 2 m (LOD0 + LOD1). Large static geometry is chunked (no single mesh > 250 m or > 300k tris).
- PBR only, no pink or missing materials. Textures power-of-two, BC7 (PC) / ASTC 6x6 (Android).
- Nothing floats, intersects visibly, or blocks a road. Road markings: white lane/edge lines, yellow lines separate opposing traffic.
- Respect the Appendix C budgets.
## Workflow
- One playbook card at a time. Small steps. Zero compile errors and no new warnings.
- Evidence for every claim: screenshots from named review cameras, Development player logs, stats.json, validator reports. Save evidence under Screenshots/<date>_<card>/ and Logs/.
- Editor tools in Assets/_Veyra/Editor, re-runnable (idempotent): running twice must not duplicate objects.
- Keep Docs/PROJECT_STATUS.md and Docs/BUG_TRACKER.md current. Commit after each passed card. Never delete or rewrite large systems without asking.
```

**Acceptance checks**

- `CLAUDE.md` is committed. Existing rules are kept unless they contradicted the new ones, and the diff was shown to you.

### S2 — Audit the existing project (no changes yet)

*Tools: Unity + Blender | Time: 30–45 min | Needs: SS, S1*

```text
Card S2. Audit the current E:\VeyraCity project and record a baseline. DO NOT fix anything in this card.
1. Git: confirm git + Git LFS (track *.fbx *.blend *.glb *.png *.tga *.psd *.exr *.raw *.wav *.ogg). Check .gitignore excludes Library, Temp, Obj, Logs, Builds, UserSettings, Screenshots. Create tag baseline-before-playbook.
2. Inventory (write to Docs/AUDIT.md):
   - Packages/manifest.json with versions; render pipeline assets and renderer data; Quality levels; Graphics settings (Always Included Shaders list).
   - All scripts under Assets/_Veyra with one-line purpose each. Read in detail: WorldEnvironment.cs, CityBuilder.cs (find their actual paths), GameFlow.cs, the vehicle controller (raycast wheels: its LayerMask, how it reads surfaces), SurfaceDefinition (or equivalent), and whatever writes stats.json.
   - Every Shader.Find, new Material(...), Resources.Load call in runtime code (file:line).
   - Scenes, especially Assets/_Veyra/Scenes/VeyraCity.unity: root objects, object count, total triangle count, number of MeshColliders/Renderers, the largest meshes (name, tris, bounds).
   - ArtSource/Downloads: each file, size, triangle count (use Blender headless to read it), and whether a licence file exists next to it.
3. Baseline evidence: open VeyraCity.unity, enter Play mode for 30 s, capture Game-view screenshots from 3 spots (spawn, a bridge, downtown), console error/warning counts, Editor FPS (for reference only).
4. Update Docs/BUG_TRACKER.md (create if missing) with ID, title, severity, repro steps, evidence, status:
   - BUG-001 Vertical steps of 4–8 m at bridge abutments (car hits invisible walls / launches).
   - BUG-002 ArgumentNullException: shader is null in WorldEnvironment.OnEnable and GameFlow.Start in player builds (Shader.Find stripped).
   - BUG-003 Monolithic downloaded map meshes (~1.2M tris single mesh): no culling, missing collisions, cars fall through.
   - BUG-004 Greybox city: CityBuilder.cs generates flat primitives without PBR/trim/emissive materials.
   - Plus anything new found in the audit.
5. Write Docs/PROJECT_STATUS.md with a "Baseline" section summarising the audit and a cards table (Card | Date | Result | Evidence | Notes).
Report a short summary and the top 5 risks. Commit "S2 audit baseline ✅".
```

**Acceptance checks**

- `Docs/AUDIT.md`, `PROJECT_STATUS.md` and `BUG_TRACKER.md` exist, and BUG-001 to BUG-004 are recorded with repro steps.
- The audit lists every runtime `Shader.Find` call with its file and line.
- Baseline screenshots are saved.

### S3 — Import the Veyra data and the coordinate helpers

*Tools: Unity | Time: 30–60 min | Needs: S2*

```text
Card S3. Bring the Veyra design data into the project and give the code one coordinate system.
Source: E:\PROJECT BANGERRR\VEYRA_CITY_WORLD_DESIGN (unzip VEYRA_CITY_WORLD_DESIGN.zip there first if only the zip exists).
1. Copy (do not move) into E:\VeyraCity:
   - Assets/_Veyra/Data/: 06_DESIGN_DOCUMENTATION/location_registry.csv, 08_3D_ASSETS/roads/road_network_geometry.json, 08_3D_ASSETS/placement_data/*.csv, 09_ART_PASS_BENCHMARK/assets/crown_downtown_building_placements.csv, 08_3D_ASSETS/terrain/terrain_tiles_manifest.json (all as TextAssets).
   - ArtSource/VeyraData/terrain/: tiles_raw16/*.raw and the splatmap/heightmap/landcover PNGs (editor-only; not in Assets).
   - Assets/_Veyra/Art/Textures/Maps/: 01_MASTER_MAP/*.png.
   - Docs/design/: the whole 06_DESIGN_DOCUMENTATION folder and 09_ART_PASS_BENCHMARK/BENCHMARK_REPORT.md + renders (visual reference).
2. If CityBuilder or other code already has readers or coordinate helpers, extend them instead of duplicating. Otherwise create in Assets/_Veyra/World/Data:
   - VeyraCoords (static): ToUnity(x, y, z) = new Vector3(x - 4000f, z, y - 3000f); FromUnity; CellOf(x, y) → "A1".."H6" (column A–H from x/1000, row 1–6 = 6 - floor(y/1000)); ChunkOf(x, y) → Chunk_<Cell>_<i><j> (250 m); YawFromHeading(deg) = -deg for prefabs whose arm/forward is local +X, 90 - deg for prefabs facing local +Z.
   - LocationRecord + LocationRegistry (all columns: id, name, category, district_id, district, x_m, y_m, norm_x, norm_y, unity_x, unity_y, unity_z, elevation_m, grid_cell, road, crew, details, description). Robust CSV parsing (quoted fields with commas).
   - BuildingPlacement, VegetationRecord, StreetFurnitureRecord (columns in Appendix A).
   - RoadNetwork data classes for road_network_geometry.json (roads[] with id, name, cls, closed, length_m, centreline_xy, profile_xyz_every_4m, profile_structure, max_grade; structures[]; interchanges[]; river_centreline; river_width_m; lake_polygon; lake_level_m; coastline; districts; race_routes; shortcuts). Use Newtonsoft (com.unity.nuget.newtonsoft-json) for nested arrays; JsonUtility cannot parse arrays of arrays.
3. Editor menu "Veyra/Data/Validate Data": load everything and log counts. Expected: 209 locations, 93 roads (total ≈ 165.2 km), 17 structures, 24 interchanges, 16 race routes, 282 Crown Downtown placements. Fail loudly on parse errors.
4. Debug scene Assets/_Veyra/Scenes/Test/T_DataCheck.unity: small spheres at every location (colour by category), LineRenderers for every road profile (colour by class). Capture a top-down orthographic image of the full 8000 x 6000 m world and compare it with Assets/_Veyra/Art/Textures/Maps/master_world_map.png: ring road around Downtown, coastal expressway along the south/east coast, mountain hairpins in the north.
5. Compare with the existing VeyraCity.unity layout: report whether the current city is aligned to this data (sample 5 landmarks). Record misalignments as bugs.
Commit "S3 data import ✅".
```

**Acceptance checks**

- The validation menu reports exactly the expected counts.
- The top-down view matches the master map: north is up (+Z) and Crown Bay is in the south-centre.
- LM-01 Veyra Spire resolves to Unity (170, ~9.8, −670) and LM-06 Lantern Point Lighthouse to (−130, ~9.4, −2640).

**If it fails**

```text
S3: the debug layout is <mirrored / rotated / offset / scaled> compared with the master map. Unity X must be x - 4000 and Unity Z must be y - 3000 (north = +Z). Print the Unity positions of LM-01 and LM-06 and fix VeyraCoords until they match (170, 9.8, -670) and (-130, 9.4, -2640). Re-capture.
```

### S4 — Blender bridge, export presets and the axis round-trip test

*Tools: Blender + Unity | Time: 30–45 min | Needs: S2*

```text
Card S4. Make the Blender ↔ Unity pipeline trustworthy before any content is exported.
1. Connectivity: through the Blender MCP (port 9876) run Python that prints bpy.app.version_string and the scene unit settings. Find the Blender 5.2 executable for headless use (e.g. C:\Program Files\Blender Foundation\Blender 5.2\blender.exe); if not found, ask me. Run a trivial headless script (blender.exe -b --factory-startup -P Tools/Blender/hello.py) and capture its output.
2. Create Tools/Blender/vc_tools.py (importable both in MCP sessions and headless) with:
   - vc_setup_scene(): Metric, unit scale 1.0, metres; collections EXPORT, LOD0, LOD1, LOD2, COLLISION, REFERENCE.
   - vc_export_fbx(objects, path, mode): mode "prop" (object at origin, local pivot) or "world" (keeps world location). Base arguments: use_selection=True, global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True, object_types={'MESH','EMPTY'}, mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False, path_mode='COPY', embed_textures=False, axis_up='Y', axis_forward = value from the test below (start with '-Z'). If Blender 5.2 rejects an argument, look it up with the Blender API docs tool and adapt.
   - vc_make_lods(obj, ratios=(1.0, 0.4, 0.1)), vc_apply_and_check(obj) (apply transforms, recalc normals, report non-manifold/zero-area faces), vc_set_pivot(obj, 'BASE_CENTER'|'MIN_CORNER'), vc_tri_count(objs).
3. AXIS ROUND-TRIP TEST (mandatory, asymmetric):
   - In Blender build VC_AXIS_TEST: a 10 x 4 x 3 m box at the origin; a 20 m arrow along Blender +Y with a cone and a text mesh "N"; a 10 m arrow along +X labelled "E"; a 2 m marker cube at Blender (100, 200, 5) named VC_AXIS_MARKER.
   - Export twice to Assets/_Veyra/Art/Models/_Test/: VC_AxisTest_Prop.fbx (mode prop, box + arrows only) and VC_AxisTest_World.fbx (mode world, everything).
   - In Unity (scene Assets/_Veyra/Scenes/Test/T_AxisTest.unity) instantiate both at position (0,0,0), rotation (0,0,0). EXPECTED: the N arrow points to Unity +Z, the E arrow to +X, the marker is at Unity (100, 5, 200), the box is exactly 10 x 4 x 3 m next to a 10 m ProBuilder reference cube, root scale (1,1,1), and LOD groups are created for _LOD0/_LOD1/_LOD2 names.
   - If the world export arrives rotated 180° (N → -Z, marker at (-100, 5, -200)), change the world preset (try axis_forward='Z', axis_up='Y'; or apply a 180° Z rotation before export) and repeat until the expectation holds. Keep the prop preset so that a model's front (Blender -Y) faces Unity +Z.
   - glTF check: import 08_3D_ASSETS/district_blockouts/blockout_D1_crown_downtown.glb with glTFast into the same test scene. Its NORTH_ARROW node must point to Unity +Z and the Veyra Spire must sit at Unity (170, ~10, -670). If it is rotated 180°, write the fix (root rotation (0,180,0)) into the importer helper and document it.
   - Unity FBX import defaults (AssetPostprocessor or presets): Scale Factor 1, Convert Units ON, Bake Axis Conversion ON, Read/Write OFF, Generate Lightmap UVs only for static architecture.
4. Write the confirmed axis settings for both presets into CLAUDE.md (section Blender → Unity) and into vc_tools.py docstrings. Screenshots: Blender viewport + Unity Scene view from above with the grid.
Commit "S4 blender pipeline ✅".
```

**Acceptance checks**

- In Unity the N arrow points +Z, the E arrow points +X, and the marker is at (100, 5, 200).
- The box matches the 10 m reference cube, and the root scale is 1.
- The D1 GLB lands with the Spire at (170, ~10, −670).
- CLAUDE.md records the confirmed presets.

**If it fails**

```text
S4: imported test is <rotated 90° / rotated 180° / mirrored / 100x too big / missing LOD group>. Print the Unity world positions of the arrow tips and the marker, the FBX importer settings (globalScale, useFileUnits, bakeAxisConversion) and the exact Blender export arguments. Change ONE setting at a time and re-run the round trip until the expectation holds. Mirroring (negative scale) is never acceptable—fix the axes instead.
```

### S5 — Review cameras and automatic screenshots (your quality radar)

*Tools: Unity | Time: 45 min | Needs: S3*

```text
Card S5. Build a review-camera system so every later card is checked from the same viewpoints.
1. ScriptableObject ReviewCameraSet with entries of two kinds: FIXED {name, district tag, Unity X, height (absolute Y, or "above ground" that raycasts down to terrain/road), Unity Z, lookAt (Unity), fov, timeOfDay (Day/Golden/Dusk/Night)} and CHASE {name, district tag, roadId, distanceAlongRoad_m, direction (+1/-1), back 7.5 m, up 2.1 m, lookAhead 30 m, fov 62, timeOfDay}. CHASE cameras find the point on the road profile (road_network_geometry.json) and place the player car (or a 4.3 m proxy) there, so driving shots stay valid when roads are rebuilt.
2. Create Assets/_Veyra/Settings/ReviewCameras.asset with the 18 cameras of Appendix E.
3. Editor window "Veyra/Review/Capture": open scene(s) → for each (filtered) camera place a temporary camera → set time of day (sun angle only until card L4 exists) → render 2560x1440 PNG to Screenshots/<yyyy-mm-dd>_<card>/<camera>.png → build a labelled 6 x 3 contact sheet. Works in Edit mode; Play-mode option for dynamic content. Optional overlay: district polygons + road centrelines for alignment checks.
4. Add a runtime equivalent (VeyraReviewCapture MonoBehaviour) that the Development player can run with command-line flag -reviewCapture to capture the same cameras in a build.
5. Test on T_DataCheck and on VeyraCity.unity. Show me both contact sheets and write a critique (scale, alignment, lighting, materials).
From now on every world-changing card ends with a capture of its cameras and a written critique.
Commit "S5 review cameras ✅".
```

**Acceptance checks**

- One menu click produces 18 screenshots and a contact sheet.
- CHASE cameras sit on the road surface, behind a car facing along the road.
- The player build can capture with `-reviewCapture`.


## Phase 1 — Stability: player build, shaders and materials

### V0 — Development player build and automated test harness (needed before any fix)

*Tools: Unity (unity:unity-cli) | Time: 60–90 min | Needs: S2, S3*

```text
Card V0. Build the test harness that proves runtime changes in a real player (the Editor hides shader stripping and many physics issues). Reuse existing code where it exists (stats.json writer, GameFlow, Arden Kite spawn); extend, don't duplicate.
1. Build scripts (Assets/_Veyra/Editor/VeyraBuild.cs), callable from the menu and from batch mode:
   - BuildDevMono(): Windows x64, Mono, Development Build ON, Script Debugging OFF, output Builds/DevMono/VeyraCity.exe, scenes from Build Settings (VeyraCity.unity first).
   - BuildReleaseIL2CPP(): Windows x64, IL2CPP, non-development, output Builds/Release/VeyraCity.exe.
   - Both write Logs/build_<timestamp>.log with the BuildReport summary (result, size, duration, errors, shader variant count if available).
   Batch command (put in Tools/build_dev.cmd): "<Unity 6000.6.8f1 path>\Editor\Unity.exe" -batchmode -quit -projectPath E:\VeyraCity -executeMethod VeyraBuild.BuildDevMono -logFile E:\VeyraCity\Logs\unity_build.log
2. Test routes: ScriptableObject VeyraTestRoute = list of segments {roadId, sStart_m, sEnd_m, targetSpeed_kmh}. The autopilot teleports the car to each segment start (raycast down to the drivable surface, aligned with the road tangent, initial speed = target), then follows the road profile from road_network_geometry.json (lookahead steering on the centreline of the right-hand lane, PID throttle/brake to target speed). Create:
   - R_SMOKE (≈ 60 s): A1 Crown Avenue 1350→2075 m at 60 km/h (crosses Crown Bridge), then A2 Meridian Boulevard 1580→1820 m at 60 km/h (Meridian Bridge).
   - R_BRIDGES: every structure with 150 m approach on each side, at 90 km/h on MW/EX/HW and 60 km/h elsewhere: R1 0→2900 (Harbor Viaduct + Sable Mouth Span), R1 5400→6300 (Kestrel Tunnel), R1 7130→7540 (West Sable Bridge), C1 3600→7260 (Lantern Strait Bridge + Gantry Expressway), H2 0→1500 (Corvo Tunnel + Corvo Creek Viaduct), H2 1330→2860 (Blackridge Tunnel), H2 3690→4300 (Highline Viaduct), A1 1476→1889, A2 1483→1919, A4 1337→1786, A10 1304→1744, A22 1562→1880 (to road end), H3 584→957, A15 2020→2386.
   - R_DOWNTOWN_NIGHT: RACE-16 Midnight Crown Circuit roads (A22, A2, A1, A4) one lap at 70 km/h, time of day 23:00.
3. Runtime test runner VeyraAutoTest (Assets/_Veyra/World/Testing), activated only by command line: -autotest <seconds> (0 = until the route is complete) -route <name> -statsPath <file> [-timeOfDay <h>] [-quality <name>] [-reviewCapture]. It: loads VeyraCity, spawns the Arden Kite, runs the route, collects metrics, writes stats.json, quits.
   stats.json fields: build (dev/release, mono/il2cpp, version, git hash), date, gpu, resolution, quality, route, duration_s, frames, avg_fps, fps_1pct_low, frametime_ms {p50, p95, p99, max}, hitches_over_50ms, gc_alloc_kb_per_frame_avg, memory_mb {managed, total_reserved, gfx}, exceptions, errors, warnings, vehicle {spawned_on_road (bool), spawn_height_above_surface_m, falls_below_surface, airborne_events, invisible_wall_events (speed drop > 30 km/h within 0.2 s without a visible collision), max_vertical_accel_g, distance_m, segments_completed/total}, per_segment [...].
   Count exceptions/errors with Application.logMessageReceivedThreaded.
4. Tools/run_autotest.cmd: runs Builds/DevMono/VeyraCity.exe -screen-width 2560 -screen-height 1440 -screen-fullscreen 1 -autotest 60 -route R_SMOKE -statsPath E:\VeyraCity\Logs\stats.json -logFile E:\VeyraCity\Logs\player.log
   Tools/verify_run.ps1: parses player.log + stats.json and prints PASS/FAIL per rule: 0 exceptions, 0 "shader is null", 0 errors, spawned_on_road, falls_below_surface = 0, invisible_wall_events = 0, segments all completed; perf rules (only on release builds): fps_1pct_low ≥ 60, p99 ≤ 16.6 ms, hitches_over_50ms = 0.
5. Run it NOW on the current project (expected to FAIL because of BUG-002). Record the baseline numbers in PROJECT_STATUS.md and attach log excerpts to BUG-002.
Commit "V0 player test harness ✅" (the harness passes when it runs and reports correctly, even if the game fails the rules).
```

**Acceptance checks**

- A single command builds the Dev Mono player. A second one runs the 60 s test and produces `player.log` and `stats.json`.
- `verify_run.ps1` prints a PASS/FAIL line per rule. The baseline FAILs are recorded.

### F1 — Shader and material architecture fix (BUG-002)

*Tools: Unity | Time: 1–2 h | Needs: V0*

```text
Card F1. Fix BUG-002 (ArgumentNullException: shader is null in WorldEnvironment.OnEnable and GameFlow.Start in player builds) properly, so it can never come back.
1. List every runtime Shader.Find / new Material(Shader) / Resources.Load<Shader> use (from Docs/AUDIT.md; re-scan). Explain for each why it returns null in a player (shader not referenced by any built asset → stripped).
2. Create VeyraMaterialLibrary (ScriptableObject, Assets/_Veyra/World/VeyraMaterialLibrary.cs): serialized Material fields grouped by category (Road, Sidewalk, Building, Glass, Neon, Props, Vegetation, Water, Vehicle, Debug) + Get(VeyraMat key) with a clear error (Debug.LogError with context) if a slot is empty. Asset: Assets/_Veyra/World/Generated/Materials/VeyraMaterialLibrary.asset.
3. Editor-only VeyraMaterialFactory (Assets/_Veyra/Editor): creates/updates the .mat assets in Assets/_Veyra/World/Generated/Materials (Shader.Find is fine here), fills the library, idempotent. Start with the materials the current code creates at runtime; F2 adds the full palette.
4. Refactor runtime code: WorldEnvironment, GameFlow, CityBuilder runtime paths and any other offender get a [SerializeField] VeyraMaterialLibrary reference (assigned in VeyraCity.unity) and use library materials (sharedMaterial, or new Material(libraryMat) when a per-instance copy is really needed). No shader-name lookups at runtime. Add null guards that log a precise error instead of throwing.
5. Keyword safety: do not toggle shader keywords at runtime. Keep _EMISSION enabled on emissive materials and drive brightness through properties/global floats (e.g. _VC_NightFactor). Create Assets/_Veyra/Art/Shaders/VC_Variants.shadervariants (capture variants after playing day + night, or add them programmatically), add it to Preloaded Shaders and warm it up during loading.
6. Graphics settings: remove "Universal Render Pipeline/Lit" (and other huge URP shaders) from Always Included Shaders if present—they cause variant explosion. Only tiny custom shaders that are truly created at runtime may stay. Report shader variant count and build time before/after.
7. Guard rail: an EditMode test (Unity Test Framework) NoRuntimeShaderFind that fails if any .cs file outside Editor folders contains "Shader.Find(". Add it to the V1 verification.
8. Verify: Dev Mono build → run_autotest (R_SMOKE) → verify_run: 0 exceptions, 0 "shader is null". Player screenshots (-reviewCapture) show no pink/magenta. Also run a quick Release IL2CPP smoke run (10 s) to confirm.
Update BUG-002 → Fixed (evidence: log excerpts, stats.json, screenshots). Commit "F1 shader architecture fix ✅".
```

**Acceptance checks**

- The player log has 0 exceptions and no "shader is null"; this holds in both the Dev Mono and Release IL2CPP builds.
- The NoRuntimeShaderFind test passes. The variant count and build time did not explode.

**If it fails**

```text
F1: player still logs "<error line>". Find the call stack in player.log, identify the exact material/shader, and check whether (a) a runtime path still creates the material by name, (b) the material asset is not referenced by any scene/prefab in the build, (c) the variant is stripped (URP Global Settings stripping, missing keyword). Fix that one cause and re-run the Dev build test.
```

### F2 — The PBR material palette (roads, buildings, neon)

*Tools: Unity + Blender | Time: 3–4 h (split: roads / buildings / neon) | Needs: F1*

```text
Card F2. Build the production material palette, all registered in VeyraMaterialLibrary and created by VeyraMaterialFactory. Use CC0 textures (ambientCG, Poly Haven; record URL + licence in Docs/ASSET_SOURCES.md) or bake procedural textures in Blender (Cycles, 2048²) when downloading is not possible.
Texture rules: BaseColor sRGB; Normal = Normal Map; Metallic map RGBA with R = metallic, A = smoothness (= 1 - roughness: INVERT roughness textures when packing), Smoothness Source = Metallic Alpha; Occlusion map (G channel). BC7 on PC. 2K default, 4K only for asphalt and hero facades.
A) ROADS — Shader Graph SG_Road (URP Lit target):
   - Materials: M_Asphalt_New, M_Asphalt_Worn, M_Asphalt_Patched, M_Concrete_Deck (viaducts/runway), M_Kerb_Granite, M_Sidewalk_Concrete (1.6 m joints), M_Cobblestone, M_Gravel, M_Dirt, M_Road_Markings (white/yellow atlas, slightly raised smoothness, worn alpha).
   - Features: world-space macro variation (two noise scales, ±8 % albedo), tyre-wear lanes (darker/smoother along wheel paths using road UV.x), crack/patch decal slots, oil stains near junctions.
   - Wetness: global _VC_Wetness (0–1) × material _WetnessResponse. Wet → albedo × 0.55, smoothness → 0.90, normal strength × 0.3; puddle mask (low-frequency noise threshold driven by wetness) → smoothness 0.98 and flat normal; rain ripples normal when _VC_RainIntensity > 0. Expose a per-material "Wetness" slider for testing.
B) BUILDINGS — Shader Graph SG_Facade (URP Lit target), driven by trim-sheet UVs (A1) and per-object seed:
   - Glass curtain wall: dark blue-grey and bronze variants, smoothness 0.92–0.96, metallic 0, mullion grid from UV (3.0 x 3.6 m bays by default, parameters BayWidth/FloorHeight), spandrel panels.
   - Interior parallax ("interior mapping"): per-window fake room using a 2D interior atlas (4 x 4 room variations; generate them in Blender: furnished office/apartment interiors rendered to an atlas, or use CC0 interior atlases) with depth 3–4 m, curtains/blinds variation, per-window random seed from floor/bay index.
   - Night windows: emissive window grid; lit fraction per building 25–70 % (seeded), warm 3000 K / cool 4500 K mix, staggered turn-on between 18:30–21:00 using _VC_NightFactor + per-window threshold; offices dim after 23:00. HDR emission 1.5–3.
   - Opaque facades: M_Facade_Concrete, M_Facade_Limestone_Deco, M_Facade_Brick_Red, M_Facade_Brick_Dark, M_Facade_Stucco_White (Azure Coast), M_Facade_MetalPanel, M_Roof_Gravel, M_Roof_Membrane, M_Storefront_Glass (with interior parallax shops), with edge-wear and grime gradient (dirtier at street level and under ledges).
C) NEON / EMISSIVE — Shader Graph SG_Neon:
   - HDR emission colour × intensity (2.5–8; never below 2.5 so Bloom threshold 0.9 catches it), tube falloff (bright core, softer edge via UV), optional flicker/buzz (noise; only for ≤ 10 % of signs), animated sequences (chasing bulbs for marquees).
   - Palette: neon cyan #00FFFF, hot magenta #FF007F, amber sodium #FFAA00, plus LED white 6500 K and signal red #FF2A2A. Brightness linked to _VC_NightFactor (day: signs read as unlit tubes with 10 % emission; night: full).
   - Variants: M_Neon_Cyan, M_Neon_Magenta, M_Neon_Amber, M_Neon_White, M_LED_Screen (scrolling UV video-like texture), M_Sign_Backlit.
D) OTHER: M_Metal_Painted, M_Metal_Galvanized, M_Plastic_Dark, M_Rubber, M_Wood_Weathered, M_Leaves (alpha clip), M_Bark, M_CarPaint (URP Complex Lit with Clear Coat; metallic flakes normal).
E) Calibration: albedo in plausible ranges (asphalt 0.05–0.12 linear, concrete 0.25–0.40, brick 0.15–0.30, never > 0.9 or < 0.02). Use the URP Rendering Debugger → Material Validation (Albedo/Metallic) and fix violations.
F) Material lab scene Assets/_Veyra/Scenes/Test/T_MaterialLab.unity: spheres + 4 x 4 m wall samples + a 20 m road strip with markings and kerb + a neon sign, lit at Day, Golden and Night (simple sun presets until L4). Capture 2560x1440 shots of each lighting state and critique.
Commit per part: "F2a road materials ✅", "F2b facade materials ✅", "F2c neon materials ✅".
```

**Acceptance checks**

- Every material appears in the library, and nothing is pink in the player build.
- Albedo validation is clean.
- Wet asphalt visibly reflects in the night material-lab shot.
- Windows show interior depth when viewed at an angle, and neon blooms at night but not at noon.

### F3 — Global shader properties and the night-cycle link

*Tools: Unity | Time: 45 min | Needs: F2*

```text
Card F3. Create one place that drives every time-of-day and weather-dependent material.
1. VeyraGlobals (runtime static + MonoBehaviour in WorldEnvironment) sets global shader properties every frame (only when changed): _VC_TimeOfDay (0–24), _VC_NightFactor (0 day → 1 night, smoothstep 18:00→20:00 and 05:30→07:00), _VC_Wetness, _VC_RainIntensity, _VC_WindStrength, _VC_WindDirection, _VC_SunDirection.
2. All SG_ shaders read these instead of per-material toggles. No keyword toggles at runtime (see F1).
3. Debug UI (F1 key / dev menu): sliders for time of day, wetness, rain, wind.
4. Verify in the player: time scrubbing from 12:00 to 23:00 turns on windows gradually, neon brightens, wet roads respond to the wetness slider. Capture 4 shots in the player (-reviewCapture with -timeOfDay 12, 19, 21, 23).
Commit "F3 global shader properties ✅".
```

## Phase 2 — Ingestion pipeline: downloads and Blender to 250 m chunks

### B1 — Licence gate and download triage

*Tools: Blender (headless) | Time: 30–60 min | Needs: S4*

```text
Card B1. Before processing any downloaded 3D map, make sure we are allowed to use it and know what it is.
1. Licence gate: every file in E:\VeyraCity\ArtSource\Downloads must have a sidecar <file>.license.txt with source URL, author, licence (CC0, CC-BY x.x with attribution text, or a purchased store licence that permits use in a commercial game), and download date. Files without it, or with non-commercial/editorial/"no AI" licences, or that look extracted from a commercial game (ripped GTA/Cyberpunk or other game formats/folder names, game logos, recognisable real game maps) → move to ArtSource/Downloads/_Quarantine and list them for me. Never process quarantined files.
2. Write Docs/ASSET_SOURCES.md (table: asset, file, source, author, licence, attribution text, used in).
3. Triage with Blender headless (Tools/Blender/triage_downloads.py → Logs/triage.json): per accepted file—format, object count, total triangles, largest single mesh (tris, bounds), material and texture count, missing textures, estimated unit scale (door/storey heights, a car if present), up axis, whether it is a CITY (many buildings + streets in one model) or a KIT (separate buildings/props).
4. Recommend a mode for each: KITBASH (split into reusable buildings/props; positions come from Veyra data, not from the download), ALIGNED (only if the model was authored for Veyra coordinates), or REJECT (quality/licence/scale). Note: a downloaded city will not match the Veyra road network; do not drop it in as the world.
Commit "B1 licence gate & triage ✅".
```

**Acceptance checks**

- Every accepted download has a licence record, and quarantined files are listed.
- `triage.json` exists and each file has a recommended mode.

### B2 — process_city_map.py: segment, chunk, LOD and collision (directive Phase 2)

*Tools: Blender (headless + MCP for visual checks) | Time: 3–5 h | Needs: B1, S4*

```text
Card B2. Write E:\VeyraCity\Tools\Blender\process_city_map.py (uses vc_tools.py), runnable headless:
blender.exe -b --factory-startup -P Tools/Blender/process_city_map.py -- --input ArtSource/Downloads/<file> --mode kitbash|aligned --out Assets/_Veyra/Art/World/Chunks --report Logs/ingest_<name>.json [--scale <f>] [--chunk 250]
Pipeline:
1. Import by extension (fbx, obj, glb/gltf, blend append). Apply all transforms.
2. Normalise: 1 Blender unit = 1 m (apply --scale or the triage estimate); Blender Z up; remove cameras/lights/empties without children; merge vertices by 1 mm; recalc normals; delete loose/zero-area geometry.
   - ALIGNED mode: Blender X = x - 4000, Blender Y = y - 3000 (Veyra world → Unity frame), Z = elevation. Validate against the Veyra heightmap (ArtSource/VeyraData/terrain/heightmap_world_4097x3073_16bit.png: height = -60 + v/65535*560; pixel column = x/8000*4096, row = (1 - y/6000)*3072) at 50 random road points; report the mean/max offset; stop if > 1.5 m.
3. Segmentation into collections Road_Surface_*, Sidewalk_*, Building_*, Prop_*, Terrain_*, Vegetation_*:
   a) names/material names (regex table in Tools/Blender/segment_rules.json: road|asphalt|street|lane|highway → Road_Surface; sidewalk|curb|kerb|pavement|footpath → Sidewalk; building|wall|facade|roof|window|house|tower → Building; lamp|light|sign|bench|pole|hydrant|bin|fence|barrier → Prop; tree|bush|grass|leaf|foliage → Vegetation; ground|terrain|land → Terrain);
   b) for monolithic unnamed meshes: separate by loose parts and by material, then classify by geometry: flat (normal·up > 0.95), height above local ground < 0.35 m, elongated strips ≥ 5 m wide → Road_Surface; raised 0.10–0.25 m strips beside roads → Sidewalk; parts with vertical extent > 3 m → Building; small parts (bounding diagonal < 8 m) → Prop;
   c) report the unclassified share; if > 10 % of triangles, stop and show me a Blender MCP screenshot of the unclassified parts in red.
4. Chunking (ALIGNED mode): 250 m grid in Veyra coordinates. Bisect continuous surfaces (roads, sidewalks, terrain) exactly on chunk borders (bmesh bisect + split) and keep shared border vertices identical so neighbouring chunks weld seamlessly; assign whole objects (buildings, props, trees) to the chunk containing their base centre (never cut buildings). Name Chunk_<Cell>_<i><j> (see CLAUDE.md). KITBASH mode: no world chunks; export each building/prop separately with base-centre pivot to Assets/_Veyra/Art/Models/Kitbash/<source>/VC_KB_<source>_<type>_<n>.fbx.
5. Per chunk:
   - Render meshes: merge by category + material (target ≤ 12 materials per chunk), LOD0 = cleaned, LOD1 ≈ 40 % (planar-aware decimate; roads keep their edges), LOD2 ≈ 10 % (buildings → simplified boxes with baked facade texture is acceptable). Lightmap UV2 for static buildings if lightmaps are used.
   - Collision meshes (never rendered): COL_Road_<chunk> (road + sidewalk + drivable plazas, non-convex, markings removed, decimated planar, welded, borders identical with neighbours), COL_Ground_<chunk> if the download has ground; buildings → UBX_<building> if box-like (OBB fit error < 0.5 m) else UCX_<building>_<n> convex hulls (≤ 255 triangles each, Unity convex limit); props → UBX_ or none for tiny props.
   - Budgets: LOD0 ≤ 300k tris per chunk (warn), ≤ 500k (fail).
   - Pivot at the chunk's south-west corner, height 0.
6. Export each chunk with the VC_Unity_World preset from S4 to Assets/_Veyra/Art/World/Chunks/<Cell>/Chunk_<Cell>_<i><j>.fbx; textures (if licence allows) to Assets/_Veyra/Art/Textures/Imported/<source>/. Write material mapping hints (source material → VeyraMat key, from Tools/Blender/material_map.json) and Assets/_Veyra/Art/World/Chunks/chunks_manifest.json: chunk, cell, file, bounds, tris per LOD, collision objects, materials, source licence id.
7. Report JSON: input tris vs output per LOD, chunk count, max chunk tris, unclassified %, warnings, time.
Test on the smallest accepted download first; show me a Blender MCP screenshot of one chunk with categories colour-coded and its collision meshes in wireframe. Then process the rest.
Commit "B2 city map ingestion ✅".
```

**Acceptance checks**

- No exported mesh exceeds the budgets, and every chunk has COL_/UBX_/UCX_ collision.
- Chunk borders weld with no cracks; check a border in Blender.
- The manifest lists every chunk, and unclassified geometry is under 10 %.

**If it fails**

```text
B2: <segmentation wrong / chunk seams crack / decimation destroys roads / export rotated>. Fix only that stage, re-run on ONE chunk, show a Blender viewport screenshot before/after, then re-run the batch. For rotation problems re-run the S4 axis test with the same preset.
```

### B3 — Unity chunk importer, colliders and placement

*Tools: Unity | Time: 1–2 h | Needs: B2, F1*

```text
Card B3. Make Unity import B2's chunks correctly and place them automatically.
1. AssetPostprocessor VeyraModelPostprocessor for Assets/_Veyra/Art/World/Chunks/** and Assets/_Veyra/Art/Models/**:
   - Preprocess: scale factor 1, Convert Units ON, Bake Axis Conversion ON, Read/Write OFF, normals Import, tangents Calculate Mikk, material import via material description → remap to VeyraMaterialLibrary materials using material_map.json (fallback M_Debug_Missing = obvious orange, and log it).
   - Postprocess: COL_* → MeshCollider (convex = false); UCX_* → MeshCollider (convex = true); UBX_* → BoxCollider fitted to the mesh bounds; then remove their renderers. Assign PhysicsMaterial/SurfaceDefinition by prefix (Road_Surface → Asphalt, Sidewalk → Concrete/Kerb, Terrain → Grass/Dirt per material). Put drivable colliders on the layer(s) used by the vehicle wheel-raycast mask (read the vehicle code; currently Default). Static flags: Contribute GI, Occluder Static (buildings only), Occludee Static, Reflection Probe Static; Batching Static OFF when GPU Resident Drawer is on. LODGroup with screen-relative heights LOD0 0.35, LOD1 0.12, LOD2 0.03, cull 0.01 (buildings) / 0.005 (landmarks).
2. Editor tool "Veyra/World/Place Chunks": reads chunks_manifest.json, instantiates each chunk under World/Chunks/<Cell>/ in VeyraCity.unity (or the cell scene once W7 exists). Idempotent. Flags chunks whose COL_Road surfaces deviate more than 1.5 m (horizontal) from the Veyra road network or whose roads are not connected to it.
3. Occlusion: bake occlusion culling for one test cell (smallest occluder 5 m, smallest hole 0.25 m, backface threshold 100) and report culled-renderer counts from two street-level cameras. Compare with GPU occlusion culling if your URP version supports it; keep whichever is faster.
4. Verify: Physics Debugger screenshot over a chunk (colliders visible), Frame Debugger draw-call count for that cell before/after merging, and a V0 player run with a custom route across the chunked area: spawned_on_road = true, falls_below_surface = 0.
Update BUG-003 with evidence. Commit "B3 chunk import & colliders ✅".
```

**Acceptance checks**

- Collision meshes are invisible, and all drivable surfaces have non-convex colliders with a SurfaceDefinition.
- The car never falls through, and draw calls and triangles in the test cell are within the Appendix C budgets.

## Phase 3 — Physics: colliders, surfaces and BUG-001

### C1 — CityBuilder drivable colliders and SurfaceDefinition (directive Phase 3)

*Tools: Unity (unity:physics-3d-collision) | Time: 2 h | Needs: B3, V0*

```text
Card C1. Make everything CityBuilder generates physically correct for raycast-wheel driving.
1. Read Assets/_Veyra/Editor/CityBuilder.cs (and any runtime builder), the vehicle controller and SurfaceDefinition first. Summarise how wheels raycast (LayerMask, max distance, how surface type is read). Keep that API.
2. SurfaceDefinition (create/extend ScriptableObject): id, linked PhysicsMaterial, grip multiplier (Asphalt 1.00, Concrete 0.98, Kerb 0.92, Cobblestone 0.88, Metal 0.80, Gravel 0.65, Dirt 0.60, Grass 0.50), wet grip multiplier (× 0.80 at _VC_Wetness = 1), rolling resistance, tyre audio set, skid-mark material, particle (smoke/dust/spray), bump noise amplitude. Fast lookup: Dictionary<PhysicsMaterial, SurfaceDefinition> (no GetComponent per wheel per frame).
3. CityBuilder changes:
   - Every drivable surface → MeshCollider convex = false built from a dedicated collision mesh (no markings, welded, no skirts), on the wheel-raycast layer (Default unless the vehicle mask says otherwise), PhysicsMaterial = its SurfaceDefinition's.
   - Kerbs: separate collider with Kerb surface; 0.15 m height with a 0.10 m chamfer on the road side so cars mount kerbs smoothly instead of snagging.
   - Adjacent collider segments share identical border vertices (or overlap 0.1 m); no gaps > 1 cm; no T-junction cracks.
   - Bridge decks, ramps, plazas, car parks, garage floors also drivable with the right surfaces.
4. Validator "Veyra/Physics/Validate Drivable": for every road sample every 2 m along the profile and every 1 m across the carriageway width, raycast down from 5 m with the vehicle mask: must hit a drivable collider, with the expected SurfaceDefinition, within 5 cm of the render surface. Output Logs/drivable_report.csv + a top-down heat-map PNG of misses. Target: 0 misses on the Veyra network.
5. Player test: R_SMOKE + R_DOWNTOWN_NIGHT; log surface changes; spawned_on_road = true, falls_below_surface = 0, invisible_wall_events = 0.
Commit "C1 drivable colliders & surfaces ✅".
```

**Acceptance checks**

- The validator reports 0 misses and 0 wrong surfaces.
- The player tests pass the vehicle rules, and the car mounts kerbs without stopping dead.

### C2 — Fix BUG-001: bridge abutment steps and road-profile continuity

*Tools: Unity | Time: 2–3 h | Needs: C1*

```text
Card C2. Fix BUG-001 (vertical steps of 4–8 m at bridge abutments) at the root and prove it with data.
1. Reproduce: for each of the 17 structures in road_network_geometry.json (structures[]: road, s0, s1, kind, name), sample the drivable collider height along the road every 1 m from s0 - 200 m to s1 + 200 m (centre and both outer lanes). Write Logs/bug001_before.csv and list every step > 0.05 m and every grade above the class limit.
2. Root cause: find where CityBuilder computes deck heights vs approach heights (e.g. fixed deck height, terrain-sampled approaches, spline interpolation overshoot). Explain it.
3. Fix with a single source of truth: decks, approaches, ramps and tunnels all take their height from profile_xyz_every_4m (already grade-validated: MW 5 %, EX 8.5 %, HW 9 %, AR 8 %, CO 10 %, MT/GR 15 %, SV 5 %). Interpolate between samples with a monotone cubic (no overshoot). If a Unity Spline is used, set knot tangents to AutoSmooth/continuous and sample height from the profile, not from spline overshoot.
4. Where existing geometry must still meet a different height (e.g. legacy pieces), ease the transition: length L = max(40 m, |Δz| / (g_max - 0.005)); z(s) = z_a(s) + Δz · (3t² - 2t³), t = (s - s0)/L. Additionally limit the grade change: |g_i - g_{i-1}| ≤ 1.2 % per 4 m (smooth crests and sags).
5. Terrain: build embankments/cuttings around approaches—terrain under the road corridor (road width + 4 m) set to road height - 0.3 m, blended back with 1:2 side slopes; keep a backup of the TerrainData before editing.
6. Abutments: continuous collider across the deck/approach joint (expansion joints are visual only), 6 m approach slab, parapets aligned.
7. Validator "Veyra/Physics/Validate Road Profiles" (run on all roads): step between 1 m samples ≤ 0.05 m + grade allowance; grade ≤ class limit + 0.5 %; grade change ≤ 1.2 % per 4 m; collider-vs-render deviation ≤ 0.05 m. Output Logs/road_profile_report.csv and per-structure profile plots (PNG, before vs after).
8. Player test route R_BRIDGES at target speeds AND at 140 km/h: airborne_events = 0, invisible_wall_events = 0, max_vertical_accel_g < 2.0, all segments completed.
Update BUG-001 → Fixed with before/after CSVs, plots and stats.json. Commit "C2 BUG-001 bridge steps fixed ✅".
```

**Acceptance checks**

- `road_profile_report.csv` shows 0 violations across all 93 roads.
- R_BRIDGES passes at both speeds, and the before/after plots show smooth profiles.

**If it fails**

```text
C2: violation remains at <structure, s = …>. Plot the collider height, render height and profile height for that 400 m window, identify which one diverges, fix that generator only, and re-run the validator for that road plus the R_BRIDGES segment.
```

### C3 — Building and prop colliders (crash into storefronts, not through them)

*Tools: Unity | Time: 1 h | Needs: C1*

```text
Card C3. Give every building and prop a cheap, correct collider.
1. CityBuilder buildings: BoxCollider per building mass (or per podium + tower), fitted to the facade plane (not to overhangs/balconies), on a "WorldStatic" layer that collides with the vehicle body; UCX_ convex hulls (≤ 255 tris) for non-box shapes; no MeshColliders on building LOD0 meshes.
2. Props: lamps/poles/signs → capsule colliders + a "Breakable" component later (G-cards); bins/benches → boxes; trees → capsule on trunk only. Small decorative props (< 0.5 m) → no collider.
3. Storefront glass: collider at the glass plane so cars stop at the window line (breakable glass later).
4. Check the vehicle body collider: rounded/beveled underside so it does not snag on collider seams.
5. Validator: drive the autopilot with a 3° steering offset along Crown Avenue to hit kerbs and storefronts; count tunnelling (car inside a building collider) → must be 0; set the vehicle Rigidbody to Continuous Speculative or Continuous Dynamic if tunnelling appears at > 150 km/h.
6. Physics Debugger screenshots of one downtown block.
Commit "C3 building colliders ✅".
```

**Acceptance checks**

- No tunnelling into buildings at up to 200 km/h.
- The physics cost per frame (from the Profiler) stays under 2 ms in downtown.


## Phase 4 — Atmosphere: GTA days and Cyberpunk nights

### L1 — URP renderer and quality tiers for PC (RTX 4070)

*Tools: Unity | Time: 1 h | Needs: F1*

```text
Card L1. Configure URP for a high-end PC look at 60+ FPS (1440p, RTX 4070), keeping lower tiers for later ports.
1. URP assets in Assets/_Veyra/Settings (create or update, don't duplicate existing ones): VC_URP_PC_Ultra, VC_URP_PC_High (default), VC_URP_PC_Medium, VC_URP_Mobile_Mid, VC_URP_Mobile_Low, each with its own Universal Renderer Data. Map to Quality levels with the same names.
2. PC_High: Rendering Path Forward+; HDR ON (precision 64-bit if the bloom bands, else 32-bit); Depth Texture + Opaque Texture ON; main light shadows 4096, 4 cascades, max distance 160 m (directive), splits about 6 % / 18 % / 45 %, last-cascade border 10 %, soft shadows High; additional light shadows atlas 4096, max 8 shadowed additional lights; Reflection Probe Blending + Box Projection ON; SRP Batcher ON; LOD cross-fade ON (dither).
   PC_Ultra: shadow distance 250 m, soft shadows High, render scale 1.0. PC_Medium: 2 cascades 120 m, shadows 2048.
3. Upscaling/AA: Spatial-Temporal Post-Processing (STP) if available (render scale 0.8–1.0 on High; quality setting chooses), else TAA (High quality) or SMAA High. Measure each option's cost in V1.
4. GPU Resident Drawer (Instanced Drawing) + GPU Occlusion Culling if available in this Unity version (requires Forward+; set BatchRendererGroup Variants to Keep All). Verify with the Frame Debugger that instanced draws appear. If they cause artifacts, document and fall back to SRP Batcher + baked occlusion.
5. Renderer features on PC_High: Screen Space Ambient Occlusion (method Blue Noise, intensity 1.2, radius 0.8 m—"high radius" for alley contact shadows, falloff 100 m, direct lighting strength 0.25, After Opaque OFF), Decals (DBuffer, for road wear, cracks, puddles, graffiti), and later the custom height-fog pass (L3). Validate any custom feature with the unity:validate-urp-render-graph-renderer-feature skill (Render Graph API is mandatory).
6. Mobile tiers: copy Android settings from Appendix C (they are not tuned now).
7. Capture the review cameras on PC_High and PC_Medium; record GPU frame time per tier (V0 harness, release build, -quality flag).
Commit "L1 URP PC tiers ✅".
```

**Acceptance checks**

- The quality tiers switch correctly in the player.
- Shadows have no visible cascade seams or shimmering while driving, and acne and peter-panning are fixed with depth and normal bias.
- GPU frame time is recorded for each tier.

### L2 — Post-processing volumes (directive values, tuned per time and place)

*Tools: Unity (unity:urp-postprocessing) | Time: 1–2 h | Needs: L1*

```text
Card L2. Build the post-processing stack with Volumes. Use the unity:urp-postprocessing skill.
1. Global volumes (weights blended by the time-of-day controller in L4): VP_Global_Day, VP_Global_Golden, VP_Global_Night.
   Shared baseline (directive): Tonemapping ACES; Bloom threshold 0.9, intensity 1.2, scatter 0.7, High Quality Filtering ON (PC), clamp 65472, optional subtle lens dirt (intensity 0.4) only at night.
   Day: White Balance temperature +3, Color Adjustments contrast +8, saturation +4, post exposure 0; Shadows/Midtones/Highlights slightly cool shadows; Vignette 0.15.
   Golden: temperature +18, tint +4, post exposure +0.1, saturation +8, bloom intensity 1.4.
   Night: temperature -8, split toning shadows teal (#1F4F5F) / highlights warm magenta (#FF5FA0) balance -20, post exposure +0.35, contrast +12, saturation +10, Chromatic Aberration 0.06, Film Grain (Thin1, 0.12), Vignette 0.25.
   Motion Blur: camera mode, intensity 0.25, quality High (PC_High+ only), scaled with car speed by script.
   Depth of Field: OFF in gameplay (enable in photo mode/cutscenes only).
2. URP has no built-in auto-exposure: implement a simple eye adaptation (luminance from a small downsampled buffer or a fixed per-volume post exposure) only if the fixed values look wrong when entering tunnels; local volumes handle most cases.
3. Local volumes (box, blend distance 20–60 m): tunnels (+0.6 exposure, sodium tint), Neon Mile (bloom intensity 1.6, saturation +14, CA 0.08 at night), Ironport (haze tint, contrast -5), Blackridge forest (cooler, +5 contrast), garages/interiors.
4. Calibrate: no clipped whites on the noon sky, bloom only on emissives/specular highlights (not on white facades at noon), neon colours stay saturated (check #00FFFF, #FF007F, #FFAA00 tubes are not washing to white—reduce intensity or use the tonemapper's highlight handling if they do).
5. Capture all review cameras at Day, Golden and Night; side-by-side contact sheets; critique against Appendix B.
Commit "L2 post-processing ✅".
```

**Acceptance checks**

- Noon looks natural, golden hour looks warm and cinematic, and night shows saturated neon without washed-out highlights.
- Bloom never fires on daylight concrete.
- The post-processing cost on PC_High is under 1.5 ms of GPU time.

### L3 — Sky, fog and atmospheric perspective

*Tools: Unity + Blender | Time: 2 h | Needs: L2*

```text
Card L3. Add depth: sky, fog and haze.
1. Sky: if this Unity 6.6 URP provides a physically based sky, use it; otherwise use a Shader Graph sky (gradient sky driven by sun elevation + sun disc + cloud layer from 2 scrolling CC0 cloud textures) or blended CC0 HDRIs from Poly Haven (day, golden, night; record licences). Ambient lighting from the sky; environment reflections updated when time of day changes (time-sliced).
2. Fog: first check whether this Unity version ships a URP volumetric/height fog override; if yes use it. Otherwise: built-in fog Exponential Squared (density from the time-of-day controller: day 0.00035, golden 0.0005, night 0.0007; colour from the sky horizon) + a custom height-fog Full Screen Pass Renderer Feature (Render Graph, injection Before Post-Processing, reading depth): analytic exponential height fog with base height 0 m, falloff 120 m, density per area (sea haze, Blackridge morning valley fog, Ironport smog, Neon Mile humid haze), in-scattering colour boosted toward the sun direction. Validate the feature with unity:validate-urp-render-graph-renderer-feature.
3. Fake light shafts: soft additive cone meshes under street lamps and neon signs at night (fade with distance and when looking straight up), and an optional sun-shaft radial pass on High.
4. Distant world: a low-poly horizon backdrop (Blender) of ridges beyond the map edge and the open sea to the south/east, so no world edge is visible from the Skyline Overlook (VP-01).
5. Capture CAM_09_BR_Overlook_Golden, CAM_03_DT_Skyline_Golden, CAM_13_NM_Lumen_Night. Critique layered depth and haze readability.
Commit "L3 sky & fog ✅".
```

**Acceptance checks**

- Distant ridges fade into haze in clear layers.
- There is no fog banding and no fog over the sky dome, and the world edge is not visible.
- The cost is under 0.6 ms of GPU time.

### L4 — Day–night cycle and night lighting (directive Night Cycle)

*Tools: Unity | Time: 2–3 h | Needs: F3, L2, L3*

```text
Card L4. Implement the full day–night cycle in WorldEnvironment (extend the existing component).
1. Time controller: timeOfDay 0–24 h, default 1 game day = 48 real minutes, pause/scrub for debug, fixed-time option for missions. Sun path for latitude ~34° N; sun intensity and colour-temperature curves (noon 5800 K, golden 3400 K, civil twilight blue hour); moon as a dim cool directional light (8000 K, 0.05–0.08) at night; star layer.
2. Drive: sun/moon lights, sky, ambient, fog density/colour, volume weights (Day/Golden/Night), and globals from F3 (_VC_NightFactor etc.).
3. Night lights (performance-aware):
   - Street lamps: emissive heads always; real Spot lights (with a cookie, no shadows) only for the nearest N lamps (Light Pool Manager: PC_High N = 48, Ultra 96; reassigned every 0.25 s by distance to the camera, fade in/out over 0.5 s); all other lamps use a projected light-pool decal (additive) on the road.
   - Building windows (F2 facade emission) turn on gradually; landmark crown lighting (Veyra Spire, Exchange Tower, Grand Lumen Hotel) with real accent lights.
   - Neon signs: emission + a few real point lights (no shadows) for big signs to spill colour onto the street and wet asphalt.
   - Vehicles: headlights (spot + cookie; shadows for the player car only), tail lights, brake lights emissive.
   - Tunnels: continuous emissive strips + pooled real lights.
4. Reflection probes: baked probe per city block for Day and Night sets (swap/blend by _VC_NightFactor) + one realtime probe around the player car (time-sliced, PC_High+) for car paint and wet roads.
5. APV: if Adaptive Probe Volumes with Lighting Scenarios (Day/Night) blending is supported in this URP version, bake Day and Night scenarios for the downtown vertical slice and blend; otherwise rely on realtime sun + SSAO + reflection probes.
6. Verify in the player: -timeOfDay 12, 18.5, 20, 23 captures of all review cameras; light count per frame and GPU time at 23:00 in Neon Mile.
Commit "L4 day-night cycle ✅".
```

**Acceptance checks**

- The 18:00–21:00 transition is smooth, with windows and neon turning on gradually.
- Night in Neon Mile on PC_High stays at 60+ FPS (release build).
- No lights pop visibly while driving.

### L5 — Wet asphalt, reflections and rain

*Tools: Unity (+ VFX Graph) | Time: 2–3 h | Needs: L4, F2*

```text
Card L5. Make wet nights look like a AAA night city.
1. Wetness states: Dry, Damp, Wet, Rain (sets _VC_Wetness, _VC_RainIntensity). Neon Mile and Downtown have a "wet night" preset (streets wet from cleaning) even without rain.
2. Reflections: check whether this URP version has screen-space reflections; if yes enable on PC_High+; if not, implement a lightweight SSR Renderer Feature (Render Graph, half resolution, hierarchical-Z or linear march with 32 steps, roughness-aware fade, fallback to reflection probes) for PC_High+, and validate it with unity:validate-urp-render-graph-renderer-feature. Puddles (smoothness 0.98) must show neon and lamp reflections.
3. Rain VFX (VFX Graph on PC; particle system fallback): camera-attached rain volume (density by intensity), splashes on roads, ripples (normal map in SG_Road), dripping from awnings, tyre spray from cars (SurfaceDefinition wet particles), wet darkening of facades in the lower 3 m.
4. Audio hooks for rain (G4 will fill them).
5. Capture CAM_07_DT_Drive_Meridian_Night and CAM_13_NM_Lumen_Night Dry vs Wet vs Rain; critique reflections (stretch, sharpness, no screen-edge artefacts dominating).
6. Performance: Wet + Rain on PC_High in Neon Mile at night, release build: 1 % low ≥ 60 FPS.
Commit "L5 wet roads & rain ✅".
```

**Acceptance checks**

- Wet roads at night reflect neon and lamps convincingly, and edge artefacts are hidden by fade.
- Rain stays within budget and shows no sorting issues.

## Phase 5 — Verification: zero-defect build (repeat after every phase)

### V1 — Batch build, 60 s player test and evidence (directive Phase 5)

*Tools: Unity (unity:unity-cli) | Time: 30–60 min per run | Needs: V0 (+ whatever phase you just finished)*

```text
Card V1. Run the full verification and record evidence. Do not change game code in this card except to fix a failure (then re-run everything).
1. Run CityBuilder in batch mode to rebuild Assets/_Veyra/Scenes/VeyraCity.unity: "<Unity 6000.6.8f1>\Editor\Unity.exe" -batchmode -quit -projectPath E:\VeyraCity -executeMethod <CityBuilder batch entry, create one if missing, e.g. CityBuilder.BuildCityBatch> -logFile E:\VeyraCity\Logs\citybuilder.log. It must finish with 0 errors; report generated object/collider counts.
2. Run the EditMode tests (NoRuntimeShaderFind, data validation, drivable validator, road profile validator) in batch mode (-runTests -testPlatform EditMode -testResults Logs/editmode.xml).
3. Build the Development Mono player (VeyraBuild.BuildDevMono) and run: R_SMOKE for 60 s (-autotest 60), R_BRIDGES (-autotest 0), R_DOWNTOWN_NIGHT (-timeOfDay 23). Collect player.log + stats.json per run.
4. Build the Release player (IL2CPP) and run the same routes for performance numbers at 2560x1440, PC_High, VSync OFF (headroom) and once with VSync ON (smoothness).
5. Verify (verify_run.ps1) and write the evidence table into Docs/PROJECT_STATUS.md:
   - player.log: 0 exceptions, 0 errors, no "shader is null" (BUG-002 confirmed fixed).
   - Vehicle: Arden Kite spawned on the road surface, falls_below_surface 0, airborne_events 0 (outside stunt jumps), invisible_wall_events 0, all segments completed (intersections, bridges, city blocks).
   - Performance (release): fps_1pct_low ≥ 60, frametime p99 ≤ 16.6 ms, hitches_over_50ms = 0, GC alloc ≈ 0 KB/frame in steady driving.
   - Review-camera captures from the player build attached.
6. Update Docs/BUG_TRACKER.md: close bugs whose evidence passes, open new ones for failures (with repro + logs). Tag the commit v<phase>-verified.
Commit "V1 verification <date> ✅" only if every rule passes; otherwise commit "V1 verification <date> ❌" with the failure list and stop.
```

**Acceptance checks**

- Every rule passes on the same commit, and `PROJECT_STATUS.md` contains the evidence table with links to the log and stats files.

**If it fails**

```text
V1 failed: <rule(s)>. For each failure: show the evidence line, the most likely cause, and the smallest fix. Fix ONE failure at a time and re-run only the relevant route, then the full V1 at the end. For performance failures, attach a Profiler capture (release build with -autotest and Profiler connected, or a development build for relative comparisons) and identify the top 5 CPU and GPU costs first.
```


## Phase 6 — World foundation

> **Note on CityBuilder:** if `CityBuilder.cs` already generates part of what a W card describes, run the card in **upgrade mode**: check the existing output against the card's acceptance checks and upgrade it to the specification. **Never create a second generator for the same thing.** Every W card must keep C1 (colliders), C2 (profile continuity) and F1 (no runtime Shader.Find) passing.

### W1 — Terrain from the 48 Veyra heightmap tiles

*Tools: Unity | Time: 45–60 min | Needs: S3, S5*

```text
Card W1. Build (or upgrade) the terrain from the Veyra tiles. Upgrade mode if terrain already exists.
Data: Assets/_Veyra/Data/terrain_tiles_manifest.json + ArtSource/VeyraData/terrain/tiles_raw16/terrain_<cell>.raw (48 tiles A1–H6). Each tile: 513 x 513 samples, 16-bit unsigned little-endian, rows ordered SOUTH→NORTH (row 0 = southern edge = Unity local z 0), columns west→east. Height (m) = -60 + value/65535 * 560. TerrainData size (1000, 560, 1000); position = manifest unity_position (y = -60).
1. Editor tool "Veyra/World/Import Terrain" (idempotent): TerrainData per tile (heightmapResolution 513), heights[row, col] = value / 65535f (no flipping), SetHeights(0, 0, heights), save to Assets/_Veyra/Art/Terrain/TD_<cell>.asset, GameObject Terrain_<cell> at the manifest position under World/Terrain, groupingID 1, allowAutoConnect, SetNeighbors.
2. Settings (PC_High): pixel error 3, basemap distance 800, Draw Instanced ON, terrain holes OFF (tunnel portals are handled by geometry). TerrainCollider on the wheel-raycast layer; surface type from the dominant splat layer at the hit point (cached lookup) → SurfaceDefinition (Grass, Dirt, Gravel, Rock, Sand).
3. Sea plane at y = 0 (temporary until W3), sun.
4. Verify: no seams at tile corners (C4/D4/C5/D5 close-up); Mirror Lake basin around Unity (-2950, ~150, 2390); Blackridge Summit ≈ 467 m at (1040, 467, 2290); Veyra Spire ground ≈ 9.8 m at (170, 9.8, -670); coastline matches master_world_map.png from above; road corridors visible as smooth benches cut into the terrain.
Capture the aerial review cameras and critique. Commit "W1 terrain ✅".
```

**Acceptance checks**

- 48 terrain tiles with no seams; mountains in the north and Crown Bay in the south.
- Spot heights are within ±5 m.

**If it fails**

```text
W1: terrain is <mirrored north-south / seams / wrong heights>. Sample tile E2 at row 256, col 256, print value and height, compare with manifest min/max for E2. If mountains appear in the south, the row order is flipped—row 0 must be the south edge. Re-run the importer (idempotent) and re-capture.
```

### W2 — Terrain materials and ground detail

*Tools: Unity (+ Blender) | Time: 1 h | Needs: W1, F2*

```text
Card W2. Texture the terrain at AAA quality.
1. Eight Terrain Layers (PBR, CC0 or Blender-baked, licences in ASSET_SOURCES.md): Grass, ForestFloor, Rock, Sand, UrbanPaving, DirtGravel, Farmland, Lawn. Mask maps packed per URP terrain rules (R metallic, G AO, B height, A smoothness).
2. Splats: ArtSource/VeyraData/terrain/splatmap_0_4096x3072.png (RGBA = Grass, ForestFloor, Rock, Sand) and splatmap_1_4096x3072.png (RGBA = UrbanPaving, DirtGravel, Farmland, Lawn). North-up images of the whole world: tile column c (0–7 from west) uses pixel columns c*512..+512; tile with south index rr (0 = southernmost) uses image rows (5 - rr)*512..+512 counted from the TOP; flip vertically when writing alphamaps (alphamap row 0 = south). Normalise weights. Tool "Veyra/World/Apply Splats", idempotent.
3. Anti-tiling: height-based blending between layers, macro colour variation (large-scale noise), distance-based tiling change (custom terrain Shader Graph if needed), slope-based rock on > 35°.
4. Ground detail within 80 m (PC_High): grass clumps (Blender, ≤ 60 tris, wind via _VC_WindStrength), small rocks, fallen leaves in forests, litter decals in urban areas. Density by layer.
5. Capture aerial + Blackridge + Redwood + Westhaven cameras. Critique tiling, palette (Appendix B) and transitions.
Commit "W2 terrain materials ✅".
```

**Acceptance checks**

- No visible tiling grid from 300 m up.
- Beaches are sandy, steep slopes rocky and the city ground paved, with splats aligned to the roads and coast.

### W3 — Water: sea, Crown Bay, Sable River and Mirror Lake

*Tools: Unity (+ Blender) | Time: 1–2 h | Needs: W1*

```text
Card W3. Build believable water.
1. Shader Graph SG_Water: depth-based colour (shallow turquoise → deep navy via scene depth), two scrolling normal maps, fresnel reflection (reflection probes; SSR from L5 on High+), sun glint, shoreline foam (depth fade < 1.5 m), refraction (Opaque Texture) on PC, wave vertex displacement for the sea (Gerstner, 3 waves, small amplitude in the bay).
2. Sea: tiled 2 km quads following the camera at y = 0, extending 30 km beyond the world edge (open horizon to the south and east).
3. Sable River: mesh from river_centreline (width from river_width_m[0] at the lake to river_width_m[1] at the mouth); surface = terrain height on the centreline + 1.8 m, smoothed, never below 0; UVs flow downstream.
4. Mirror Lake: mesh from lake_polygon at lake_level_m (150 m). Sable Falls (LM-25) waterfall: VFX + mesh.
5. Wet-sand band along Azure Coast beaches (decal, 10 m).
6. Underwater: simple fog/tint when the camera is below y = 0 (car falls into the bay).
7. Capture Crown Bay skyline, Mirror Lake, a downtown river bridge; critique.
Commit "W3 water ✅".
```

**Acceptance checks**

- The horizon is open sea, the river sits in its banks with no gaps, and the bay looks deep while the beaches look shallow.

### W4 — Roads part 1: splines, surfaces and markings

*Tools: Unity (+ Blender for textures) | Time: 2–3 h (2 sessions) | Needs: W1, C1, C2, F2*

```text
Card W4. Generate (or upgrade in CityBuilder) the road network from data. Roads must look engineered, not like ribbons.
Data: road_network_geometry.json → roads[].profile_xyz_every_4m (heights already include grades, bridge decks, tunnel floors, viaducts), roads[].cls. Use these heights exactly (C2 validator must stay green).
Classes (carriageway incl. shoulders): MW 32 m (3+3 lanes of 3.65 m, concrete median barrier), EX 24 m (2+2, median barrier), HW 18 m (2+2; 1+1 in mountains), AR 20 m (2+2 + 3.2 m centre turn lane, kerbs), CO 11 m (1+1 + parking lanes, kerbs), MT 7.5 m (1+1), RU 7 m, GR 5.5 m gravel, SV 12 m, RW 45 m runway.
1. SplineContainer per road id under World/Roads/<class>/ with knots at the data points (simplify ≤ 0.05 m error), tangents AutoSmooth, heights from the profile (C2).
2. RoadMeshBuilder: sweep a class cross-section (parallel-transport frame with world up, so hairpins don't twist): asphalt surface (UV u across width, v = metres/10), 0.3 m skirts, separate marking submesh; segment meshes per 250 m chunk (VC_ROAD_<roadId>_<chunk>); collision meshes per C1 (no markings).
3. Markings: white 0.15 m lane lines (dash 3 m / gap 9 m on MW/EX/HW/AR; 3/6 on CO/RU/MT), white solid edge lines, double yellow centre on HW/AR, dashed yellow on CO/RU, dashed white on MT, yellow median edges on MW/EX, arrows/"ONLY" text near junctions. Rendered as decals or offset quads with depth offset (no z-fighting).
4. Materials from F2 (SG_Road: wear lanes, patches, cracks, oil, wetness). Concrete deck on viaducts and the runway.
5. Terrain conformance: heightmap already has road cuts; fix small mismatches by lowering skirts, never by raising terrain over the road.
6. Capture CHASE cameras (Crown Avenue, Meridian, Halcyon, C1 coast) and critique lane widths vs the car, crisp markings, joins.
Commit "W4 road surfaces ✅".
```

**Acceptance checks**

- All 93 roads are built with a total length of about 165 km.
- No z-fighting, floating edges or gaps.
- The C1 and C2 validators are still at 0 violations.

### W5 — Roads part 2: intersections, kerbs, sidewalks and roadside

*Tools: Unity + Blender | Time: 2–3 h | Needs: W4*

```text
Card W5. Make junctions and streets look like a real city.
1. Intersections: detect where centrelines meet/cross within 2 m (plus interchanges[] with grade_separated = false). Cut road meshes back by (other width/2 + 6 m) and fill with junction meshes: flat asphalt with rounded kerb returns (CO 6 m, AR 9 m radius), stop lines 0.45 m, zebra crossings (0.55 m stripes, 1.1 m pitch, 3.2 m long) on every urban arm (D1, D5, D6), box hatching at major AR×AR junctions, traffic-signal pole positions (used by G2). Colliders per C1.
2. Urban kerbs and sidewalks (AR, CO in towns): 0.15 m granite kerb (chamfered collider per C1), 3.0 m sidewalk (F2 concrete, 1.6 m joints), 0.5 m gutter, drains every 45 m, kerb ramps at crossings, driveways in Westhaven. Modular Blender pieces placed along splines or swept geometry—whichever stays within the draw-call budget (combine per chunk).
3. Rural/mountain edges (MT, RU, HW outside towns): 1 m gravel shoulder, grass verge, W-beam guardrail where the outside drop > 2.2 m within 9 m, delineator posts every 25 m elsewhere, chevron signs on hairpins (radius < 30 m), rockfall netting on cuttings > 6 m.
4. Signage: speed limits per class, street-name blades at urban junctions (names from the data), motorway gantries on R1/C1 (exits named after interchanges IC-01..IC-09), all with original Veyra typography.
5. Capture CAM_05 plaza, CAM_07 Meridian, a Westhaven street, CAM_10 Halcyon. Critique as a senior level artist.
Commit "W5 junctions & streets ✅".
```

**Acceptance checks**

- Junctions are clean, with crossings and stop lines on every downtown junction.
- Guardrails run on the valley side of mountain roads.
- The validators are still green.

### W6 — Roads part 3: bridges, tunnels, viaducts and interchanges

*Tools: Unity + Blender | Time: 3–5 h (one family per session) | Needs: W4, W5, C2*

```text
Card W6. Build every road structure from structures[] and profile_structure flags (heights from the profile only—C2 must stay green).
Families, one per session: (a) river bridges (Crown, Meridian, Union Street, Regent Street, Sable Drawbridge, West Sable, Millbrook, Outflow, Sable Mouth Span); (b) Harbor Viaduct (R1, 2.65 km elevated) + Gantry Expressway (C1 elevated, 2.08 km); (c) tunnels: Kestrel (R1, 600 m twin bore), Blackridge (H2, 1.22 km), Corvo (H2, 0.83 km) with portals; (d) Highline Viaduct (Hollow Creek gorge) + Corvo Creek Viaduct; (e) Lantern Strait Bridge (C1, 1.27 km, hero cable-stayed); (f) interchange ramps IC-01..IC-09 (trumpet/diamond per descriptions, ≤ 6 % grade, connecting real road ends).
For each family:
1. Blender modules with LODs at exact class widths: deck segments with parapets and expansion joints, hammerhead piers (viaducts), twin columns (bridges), abutments, tunnel ring sections (20 m: lights, jet fans, emergency niches, cable trays), portal façades, railings, sign gantries.
2. Unity placement along the spline where profile_structure matches; piers every 30–40 m reaching terrain or sea floor; continuous drivable colliders (C1); occluders for tunnel interiors.
3. Tunnel lighting: emissive strips every 4 m + pooled real lights (L4); entrance zone brighter (exposure local volume from L2).
4. Lantern Strait Bridge: two A-frame concrete pylons ~140 m at 30 % and 70 % of the span, fan cables (12 pairs per pylon side), aircraft warning lights, 2+2 lanes + maintenance walkways, night architectural lighting (cool white uplights on pylons).
5. Player test R_BRIDGES must still pass; capture structure close-ups day and night.
Commit per family ("W6a river bridges ✅", ...).
```

**Acceptance checks**

- No gaps, steps or invisible walls on any structure (R_BRIDGES passes).
- Piers reach the ground, and tunnels are lit and readable.

### W7 — World streaming: 48 cells × 16 chunks

*Tools: Unity | Time: 2–3 h | Needs: W1–W6, B3*

```text
Card W7. Stream the world so it scales and never hitches.
1. One additive scene per 1 km cell (Assets/_Veyra/Scenes/Cells/Cell_<A1..H6>.unity) containing that cell's terrain, its 16 chunks (roads, kerbs, buildings, props, vegetation, local lights/probes). Bootstrap scene: player, cameras, WorldEnvironment, managers, global water, sky, distant backdrop and HLOD proxies.
2. HLOD per cell (Blender or Unity tool): merged, simplified buildings + roads with a baked atlas (≤ 30k tris per cell), shown beyond 1.2 km; landmarks keep their own far LOD so the skyline reads from everywhere.
3. CellStreamer: Addressables group per cell; PC_High loads a 5 x 5 cell window around the player (radius ≈ 2 km), unloads beyond 2.5 km; prefetch in the travel direction at speed > 80 km/h (look-ahead 600 m); async loads spread across frames (activation time-sliced) to keep hitches < 50 ms.
4. Tool "Veyra/World/Distribute To Cells" (idempotent) moves objects into cell scenes by position.
5. Debug overlay (F3): current cell/chunk, loaded cells, memory, last load times.
6. Player test: drive R_BRIDGES + the RACE-12 Grand Loop roads (C1, H2, H3) at 200 km/h; stats.json hitches_over_50ms = 0, memory stable over 10 minutes (no leak).
Commit "W7 streaming ✅".
```

**Acceptance checks**

- No missing ground or pop-in within 600 m, even at 200 km/h.
- No hitches above 50 ms, and memory stays flat.

### W8 — Pause map and minimap (GTA-style)

*Tools: Unity (unity:ui) | Time: 2 h | Needs: S3, W7*

```text
Card W8. Build the map UI (use the unity:ui skill to choose UI Toolkit or uGUI).
1. Pause map from Assets/_Veyra/Art/Textures/Maps/master_world_map.png (map area only, excluding the legend panel: left 2400/3160 of the width). Map pixel ↔ world: px = x/8000 * mapWidth, py = (1 - y/6000) * mapHeight. Zoom 1x–8x, pan, legend toggle, district filter, waypoint → GPS route (A* over the road graph from road_network_geometry.json junctions).
2. Minimap: circular, rotates with the car, roads rendered from splines at startup (or satellite_map.png styled), player arrow, GPS line, icons within 600 m.
3. Icons from location_registry.csv categories with the legend colours (Appendix B).
4. Optional fog of war revealing districts as visited.
5. Capture at 2560x1440 and 1920x1080.
Commit "W8 map UI ✅".
```

**Acceptance checks**

- A GPS route to any location can be driven to completion.
- The player arrow on the map is within 10 m of the real position.


## Phase 7 — Assets in Blender (AAA kit-based production)

> **Rules for every A card:**
>
> - Build in Blender through the MCP for visual iteration (viewport screenshots after each step) and headless for batch export.
> - Use `vc_tools.py` presets from S4; LODs and collision follow CLAUDE.md naming.
> - **Texel density:** 512 px/m for props close to the player, 256 px/m for building ground floors, 128 px/m for upper floors and terrain-scale objects.
> - Import into Unity through B3's postprocessor, so materials remap to the library and colliders are created.
> - Each card ends with a **turntable or close-up screenshot in Unity** under PC_High lighting (Day and Night).

### A1 — Trim sheets and texture atlases

*Tools: Blender | Time: 2–3 h | Needs: S4, F2*

```text
Card A1. Create the trim sheets that every building and prop will share (this is how AAA cities stay detailed and cheap).
1. Facade trim sheet T_Trim_Facade_A (4096 x 4096): horizontal strips for concrete bands, window frames (aluminium, painted steel), cornices, art-deco mouldings, metal panel seams, storefront frames, signage frames, roof edges, drainpipes, vents. Each strip 128/256/512 px tall with a matching height-normal and smoothness. Bake in Blender (Cycles, high-poly strip models → low-poly plane) or composite CC0 textures; record sources.
2. Material sets per trim: BaseColor, Normal, Metallic(R)+Smoothness(A) (smoothness = 1 - roughness), AO. Variants: T_Trim_Facade_Brick, T_Trim_Facade_Coastal (white stucco, terracotta), T_Trim_Industrial (corrugated steel, rust, safety yellow).
3. Tileables (2048²): concrete (3), glass reflection breakup (1), brick (2), limestone (1), stucco (1), corrugated metal (1), roof gravel/membrane (2), with a shared grime/edge-wear mask set (world-space or vertex-colour driven in SG_Facade).
4. Interior atlas for SG_Facade (4 x 4 rooms, 2048²): offices, apartments, shops, parking, rendered in Blender from simple furnished room scenes with soft lighting; also a night version (lights on).
5. Decal atlas (2048²): stains, cracks, graffiti tags (original), posters (original fictional brands), manhole covers, road patches, tyre marks.
6. Document UV rules in Docs/ART_TRIMS.md (strip positions in UV space) so A2 can map modules exactly.
Show Blender renders of each sheet and a Unity T_MaterialLab capture. Commit "A1 trim sheets ✅".
```

**Acceptance checks**

- The sheets tile seamlessly and look crisp at 1.5 m viewing distance.
- The UV rules are documented, and the Albedo validator is clean.

### A2 — Modular facade kits (one per architectural style)

*Tools: Blender | Time: 3–4 h per style (one style per session) | Needs: A1*

```text
Card A2. Build modular building kits in Blender, one style per session, all snapping to a metric grid.
Grid: office floor 3.6 m, residential floor 3.0 m, ground floor 4.8 m (storefront) or 6.0 m (lobby); bays 1.5 / 3.0 / 6.0 m.
Pieces per kit (each with LOD0/LOD1/LOD2, trim-sheet UVs, base pivot, UBX_ colliders only on ground-floor pieces): wall bay (solid, window types ×3), corner (inner/outer), ground storefront (with interior card), entrance/lobby, loading door, cornice/parapet, roof edge, roof slab, balcony, fire escape segment, setback ledge, crown/top piece, rooftop props (HVAC units, water tank, antennas, satellite dishes, helipad, maintenance crane), wall clutter (AC units, pipes, vents, signage brackets).
Styles (session order): 1) GlassModern (D1 towers: curtain wall + spandrels + fins); 2) ArtDeco (D1: limestone, stepped setbacks, vertical piers, bronze spandrels); 3) ResidentialMidrise (D1/D5: balconies, punched windows); 4) BrickIndustrial (D3/D6: red/dark brick, steel lintels, fire escapes, painted signage); 5) ModernLowrise; 6) CoastalStucco (D2 villas, beach clubs, marina); 7) Suburban (D5 houses: gables, garages, porches, fences, driveways); 8) Industrial (warehouses, rail sheds, tanks, refinery columns); 9) Alpine/Rural/MarketTown (D4 chalets, D8 barns, silos, brick market town); 10) Aviation (hangars, terminal, grandstand).
Existing package pieces (08_3D_ASSETS/modular_kits/building_kit.glb) are blockout references for scale only—replace them.
Per style: assemble a 3-building test block in Blender (MCP screenshot), export, and capture in Unity at Day and Night (windows lit). Triangle budget per bay piece: LOD0 ≤ 1.5k, LOD1 ≤ 500, LOD2 ≤ 100.
Commit per style "A2.<n> <style> kit ✅".
```

**Acceptance checks**

- Pieces snap with no gaps or z-fighting.
- The test block reads as a real building style from 5 m and from 500 m.
- The night windows work.

### A3 — Building generator from placement data (all 2,708 buildings)

*Tools: Blender + Unity | Time: 4–6 h (D1 first, then other districts) | Needs: A2, W1, W5*

```text
Card A3. Generate the buildings from data using the kits. Placement data: Assets/_Veyra/Data/crown_downtown_building_placements.csv (D1, typologies T1–T7) and world_building_placements.csv from the playbook data supplement (all districts: footprint, height, unity_yaw_deg, style_kit, ground_min, cell, chunk, seed).
1. Typology recipes (ScriptableObjects or Blender JSON recipes), seeded variation:
   T1 glass podium tower (2–4 floor podium with storefronts, 4 m setback, glass tower, lit crown); T2 art-deco stepped (setbacks at ~55 % and ~80 % height, limestone piers, bronze spandrels); T3 twisting tower (1–2° rotation per floor, glass); T4 fin slab (vertical metal fins); T5 residential with balconies; T6 brick midrise with storefronts; T7 modern lowrise; plus recipes for every style_kit in the supplement (Suburban house variants with roof types, CoastalStucco villas, Industrial sheds/tanks, Rural, Alpine, Aviation, MarketTownBrick).
2. Assembly approach—measure both on 20 D1 buildings and keep the faster one at equal quality:
   A) Unity-side assembly from module prefabs (instanced by GPU Resident Drawer/SRP Batcher), per-building LODGroup with LOD1/LOD2 proxies baked in Blender (merged shell + baked facade texture).
   B) Blender-side merged mesh per building (LOD0/1/2) exported per 250 m chunk.
3. Placement: base at unity_y_base with a plinth extending 2 m below ground (no floating on slopes), yaw = unity_yaw_deg, footprint w along local X, l along local Z. Buildings must not overlap roads/sidewalks (check against the road collision meshes; shrink or skip and log).
4. Landmarks use hero assets from A4 (never generic recipes): Veyra Spire, Exchange Tower, Grand Lumen Hotel, Starlight Theater, etc.
5. Colliders per C3, rooftop props, ground-floor storefronts with interior parallax and original fictional shop signs (A6).
6. Order: D1 (vertical slice) → D6 → D2 → D5 → D3 → D7 → D8 → D4. After D1: capture CAM_02, CAM_03, CAM_05, CAM_06 Day/Golden/Night; compare against Docs/design/../09_ART_PASS_BENCHMARK renders (must be at least as good, ideally much better). Profile D1 in a release build (60+ FPS).
Commit per district "A3.<district> buildings ✅".
```

**Acceptance checks**

- Building counts per district match the data (D1 282, D5 1,973, and so on).
- No building floats or sits on a road.
- The skyline matches the master-map silhouette, and D1 holds 60+ FPS.

### A4 — Hero landmarks

*Tools: Blender | Time: 2–4 h each | Needs: A1, A2*

```text
Card A4. Model the hero landmarks at exact dimensions (Docs/design/district_specifications.md + landmark_blockouts.glb for scale/positions). One landmark per session, highest quality, LOD0 up to 150k tris (Veyra Spire up to 250k), far LOD for the skyline, night lighting design included.
Order: LM-01 Veyra Spire (342 m, octagonal tiers, bronze glass, crown fins, aircraft lights, crown light show at night) → LM-03 Exchange Tower (165 m stepped) → LM-07 Lantern Strait Bridge pylons (from W6e) → LM-20 Starlight Theater (art-deco, 30 m vertical neon marquee) → LM-21 Grand Lumen Hotel (95 m, rooftop sign) → LM-06 Lantern Point Lighthouse (rotating beam) → LM-11 Gantry STS cranes (78 m, 8 units) → LM-13 Falcon Control Tower (34 m) → LM-14 Blackridge Summit Masts (120 m) → LM-26 Millbrook Clock Tower (24 m) → Azure Pier with Ferris wheel (animated) → Vantage car park (9 levels, drivable ramps with C1 colliders) → garages (Juniper Lane, Foundry 9, Apex Shed, etc.).
Each: reference board (shape language, materials, original design—no real-world or game copies), blockout → detail → UVs/trims → LODs → collision → export → Unity placement at its registry position → Day/Golden/Night captures from 3 distances (50 m, 500 m, 2 km).
Commit per landmark "A4 <LM-id> ✅".
```

**Acceptance checks**

- Each landmark is recognisable from 2 km as a silhouette, and its night lighting is distinctive.
- Positions match the registry within 1 m.

### A5 — Street furniture and roadside props

*Tools: Blender + Unity | Time: 3–4 h | Needs: A1, W5*

```text
Card A5. Build the street-furniture library and place it from data.
1. Props (LOD0 + LOD1, ≤ 3k tris LOD0 unless noted, 512 px/m trims): streetlamp_urban_led_10m, streetlamp_sodium_highmast_25m, streetlamp_coastal_6m, tunnel light strip, traffic signal pole + heads (animated by G2), pedestrian signals, sign gantry (34 m), speed/warning/street-name signs, bollards, benches, bus shelter with ad panel, bins, recycling, newspaper box, fire hydrant, parking meter, bike rack, planter, tree grate, manhole/drains (decals), electrical boxes, phone/charging kiosk, fencing (chain-link, ornamental), water barriers, traffic cones, construction barrier, guardrail (W-beam) + terminals, delineators, power poles + cables (D5, D8), ISO containers 20/40 ft (Ironport colours), drag staging tree, timing board, grandstand section.
2. Placement from data: Assets/_Veyra/Data/street_furniture_<district>.csv (prefab names streetlamp_urban_led_10m, streetlamp_sodium_highmast_25m, streetlamp_coastal_6m; yaw = -heading_deg because these lamp arms point along local +X; use 90 - heading for +Z-facing prefabs). Containers from container_placements.csv (12.19 x 2.44 m, long axis = Unity X, tiers stacked 2.59 m), cranes from crane_placements.csv. Procedural rules for the rest (bins every 60 m on urban sidewalks, benches at plazas/bus stops, hydrants every 90 m, signals at junctions from W5).
3. Instancing: GPU Resident Drawer / instanced indirect for high counts; LOD cross-fade; colliders per C3; lamps feed the L4 Light Pool Manager.
4. Capture CAM_05, CAM_06, CAM_07 Day and Night.
Commit "A5 street furniture ✅".
```

**Acceptance checks**

- Lamp counts match the CSVs (2,552 urban LED, 119 sodium high-mast, 106 coastal).
- Lamp arms point over the road, and nothing blocks lanes or crossings.

### A6 — Neon, LED and signage kit (Cyberpunk flavour, used with restraint)

*Tools: Blender + Unity | Time: 3 h | Needs: F2, A2*

```text
Card A6. Create the original signage that gives Veyra its night identity (strongest in Neon Mile, selective in Downtown, almost none elsewhere).
1. Blender: neon tube letters/shapes generated from curves (bevel 12–18 mm tubes, backing panels, mounting brackets, cables), vertical blade signs (6–30 m), horizontal fascia signs, rooftop signs, marquee with chasing bulbs (Starlight Theater), LED video boards (aspect 16:9 and tall), holographic-style projected decals (subtle, not sci-fi flying holograms), awnings with backlit text.
2. Content: ~60 ORIGINAL fictional brands and words (bars, diners, garages, karaoke, hotels, tyre shops, noodle bars, crew tags: Iron Wolves, etc.) in an original style—no real brands, no copied Cyberpunk logos or typography.
3. Materials: SG_Neon palette (#00FFFF, #FF007F, #FFAA00, white 6500 K, red), intensity 2.5–8, flicker on ≤ 10 %, turn-on stagger at dusk.
4. Placement rules: Neon Mile (Lumen Avenue "the Mile", Arcade Street, Lantern Alley, Reel Street) dense—every storefront at least one sign, 1 big vertical sign per 40 m; Downtown—hotels, bars, theatre district streets only; elsewhere—gas stations, diners, motels sparingly.
5. Light spill: big signs get pooled real point lights (L4), others light-pool decals.
6. Capture CAM_13_NM_Lumen_Night dry and wet, plus a close-up of the Starlight Theater marquee.
Commit "A6 neon & signage ✅".
```

**Acceptance checks**

- Neon Mile at night reads as its own place: saturated, layered and reflective.
- Every brand is original, and performance holds at 60+ FPS.

### A7 — Vegetation and rocks

*Tools: Blender + Unity | Time: 3–4 h | Needs: W2*

```text
Card A7. Build and place vegetation (counts from data: ~53,500 pine_tree_14m, ~12,200 deciduous_tree_10m, ~1,400 bush_2m, ~420 palm_tree_12m).
1. Blender trees (or licensed SpeedTree/CC0 sources—record licences): pine (3 variants), deciduous (3), palm (2), bush (3), street tree for urban grates (2), dead tree, plus rocks/boulders (6) and cliff pieces (4) for Blackridge. LOD0 6–15k tris (trees), LOD1 2k, LOD2 300, billboard/impostor beyond 250 m (octahedral impostor baking if possible). Wind via vertex colours + SG_Foliage (_VC_WindStrength).
2. Placement: vegetation_<district>.csv (type, unity_x/y/z, scale) → rotate randomly by seed; snap to terrain; skip anything within road width + 2 m. Instanced rendering (GPU Resident Drawer or indirect instancing), per-cell grouping for streaming.
3. Forest density/lighting: ambient occlusion on trunks, ground cover (W2 details), forest-edge treatment.
4. Capture CAM_09, CAM_10, CAM_12, CAM_18 (Redwood). Measure GPU cost of vegetation in Blackridge (target ≤ 3 ms on PC_High).
Commit "A7 vegetation ✅".
```

**Acceptance checks**

- The forests look dense and natural from the overlook, with no trees on roads.
- Vegetation costs no more than 3 ms of GPU time in Blackridge.

### A8 — Hero car: Arden Kite (stock and street build)

*Tools: Blender + Unity | Time: 1–3 days (iterative) | Needs: F2*

```text
Card A8. Upgrade the player car (fictional 2004 Arden Kite coupe; scale reference 08_3D_ASSETS/vehicles/vehicle_scale_proxies.glb: ≈ 4.3 x 1.8 x 1.3 m). Original design—no real brand shapes, badges or logos.
1. Blender: clean subdivision-ready body (panels separated: doors, bonnet, boot, bumpers, wings), wheel arches, glass, lights with internal reflectors, interior (dashboard, seats, wheel) at mid detail, 4 wheel/tyre assets (brake discs + calipers). LOD0 ≤ 120k tris, LOD1 40k, LOD2 10k, LOD3 2k.
2. Street-build kit (modular, swappable): front splitter, side skirts, rear diffuser, wing, widebody flares, 3 wheel designs (18"), lowered stance (-50 mm), exhaust tips.
3. Materials: M_CarPaint (Complex Lit clear coat, flakes), chrome, rubber, glass with tint, light lenses with emissive (headlight, tail, brake, indicators driven by G1).
4. Rig for Unity: wheel pivots at hub centres, steering pivot, doors/bonnet pivots, named sockets (camera targets, exhaust, lights). Keep the existing vehicle controller's expected hierarchy (read it first).
5. Capture turntable in Unity Day/Night + chase camera shots (CAM_06, CAM_07).
If after two iterations it is below GTA-level quality, tell me honestly and list licensed/commission options (Appendix F) instead of iterating further.
Commit "A8 Arden Kite ✅".
```

**Acceptance checks**

- The car is clean and believable at chase-camera distance, with correct reflections and wheel placement.
- The controller still works.

### A9 — Traffic vehicles

*Tools: Blender + Unity | Time: 1–2 days | Needs: A8*

```text
Card A9. Create 10 original traffic vehicles at lower detail (LOD0 ≤ 40k tris): compact hatchback, sedan, wagon, SUV, pickup, minivan, taxi (Veyra livery), city bus, box truck, semi-tractor + container trailer (Ironport). Shared material atlas, colour variations by seed, emissive lights driven by G2 (headlights at night, brake lights), simple interiors (silhouette only), LOD3 ≤ 1k for distant traffic. Original designs and fictional brands only.
Capture a parking-lot line-up Day and Night. Commit "A9 traffic cars ✅".
```

**Acceptance checks**

- All 10 vehicles have consistent scale (checked against the 1.75 m human and the Kite), lights and LODs.

### A10 — Pedestrians (optional for the slice)

*Tools: Blender + Unity | Time: 1–2 days | Needs: none*

```text
Card A10. Pedestrians for street life. Prefer licensed/CC0 characters and animations (e.g. Mixamo characters/animations under their licence terms, or purchased packs; record licences). Create 12 visual variants (clothing/colour swaps, district styles: business downtown, beach Azure Coast, workers Ironport), LOD0 ≤ 20k tris, LOD2 ≤ 1.5k, impostors at distance; animations: idle, walk ×2, run, phone, wait at crossing, sit. Unity Humanoid rig, shared Animator Controller, GPU-friendly (no per-character unique materials).
Capture a busy sidewalk at Crown Plaza. Commit "A10 pedestrians ✅".
```

**Acceptance checks**

- 12 variants with correct scale and animation; 100 pedestrians visible cost under 2 ms of CPU time.


## Phase 8 — Districts (one at a time, vertical slice first)

> **Status ladder** (use these exact words in `Docs/PROJECT_STATUS.md` and the asset status registry):
>
> - **BLOCKOUT:** layout, scale and roads only.
> - **GREYBOX:** playable, collisions correct, neutral materials.
> - **ART PASS:** final materials, lighting and set dressing in place, but still has known issues.
> - **FINAL:** passes the Q2 visual review (average ≥ 4.5/5 from every camera, Day, Golden and Night), V1 performance and Q1 QA with zero open A/B bugs.
>
> **Only you, the human, may mark a district FINAL** after looking at the captures. Claude may propose it.

### DS1 — Crown Downtown: the vertical slice (do this first, and do it best)

*Tools: Unity + Blender | Time: 1–2 weeks of sessions | Needs: Phases 1–6, A1–A6*

```text
Card DS1. Take Crown Downtown from its current state to ART PASS, aiming for FINAL. This district is the benchmark every other district is measured against.
Reference: Docs/design/07_DISTRICT_3D_REFERENCE/1_crown_downtown/district_3d_spec.md, 09_ART_PASS_BENCHMARK renders (minimum bar), Appendix B art direction. Style dial: 80 % GTA-realism by day; at night 30 % neon (hotels, theatre streets, crowns), never Neon-Mile density.
Checklist (work through in order; capture after each item; commit per item "DS1.<n> … ✅"):
1. Layout audit: all 282 placements present (A3), landmarks LM-01..LM-05, LM-15, LM-27 placed (A4), garages GAR-03/04/06/09, dealership DLR-02, meets MEET-03/12 present with entrances that work.
2. Streets: Crown Avenue (A1), Meridian Boulevard (A2), Harbor Front Drive (A4), Union Street (A10), Regent Street (A22), Tower/Spire/Bishop streets: junctions, crossings, signals, signage, parking bays, bus stops, loading zones (W5).
3. Ground plane detail: sidewalks with joints, kerb wear, drains, manholes, tree grates + street trees, planters, decals (stains, gum, cracks, tyre marks at junctions), puddle zones for wet nights.
4. Buildings: storefronts at street level with interior parallax and original shop signs, lobbies with lit interiors at night, rooftop clutter (HVAC, tanks, antennas), building-to-building variation (no two neighbours with same recipe + colour).
5. Crown Plaza (LM-02): paving pattern, fountain (animated water), seating, trees, sculpture, event lighting.
6. Harbor Viaduct + waterfront: underside detail (bearings, drainage, graffiti, parking beneath), Crown Bay Promenade VP-11, marina edge.
7. Life: traffic (G2) at downtown density, parked cars, pedestrians (G3), birds, steam vents, flags, window cleaners' gondola on one tower.
8. Lighting: Day/Golden/Night tuned locally (reflection probes per block, local volumes for plaza/alleys), crown lighting on Veyra Spire and Exchange Tower, light pools on wet streets.
9. Audio ambience zones (G4).
10. Performance (release build, PC_High, 1440p, 23:00, wet): R_DOWNTOWN_NIGHT 1 % low ≥ 60 FPS; R_SMOKE noon 1 % low ≥ 60 FPS.
11. Review (Q2): CAM_02–CAM_08 × Day/Golden/Night in the PLAYER build; self-score per rubric and list the 10 weakest details with fixes; fix the top 5; re-capture.
Update the status registry: D1 = ART PASS (Claude may propose FINAL with evidence).
```

**Acceptance checks**

- Every checklist item has a capture and a commit, and V1 passes.
- The Q2 average is at least 4.0; at least 4.5 is needed before you mark it FINAL.

### DS2 — Neon Mile at night: the Cyberpunk-flavoured showcase

*Tools: Unity + Blender | Time: 1 week of sessions | Needs: DS1, A6, L5*

```text
Card DS2. Make Neon Mile (district D6) the night-life showcase. Reference: Docs/design/07_DISTRICT_3D_REFERENCE/6_neon_mile/district_3d_spec.md. Style dial: by day an ordinary gritty brick entertainment district (GTA realism); from dusk, 70 % neon intensity—layered signage, wet streets, haze, crowds—original, not a copy of Night City.
1. Streets: Lumen Avenue "the Mile" (A7, diagonal 4-lane), Arcade Street, Lantern Alley, Reel Street; alleys with fire escapes, dumpsters, steam, cables overhead, string lights.
2. Buildings: brick warehouse conversions (BrickIndustrial kit), LM-20 Starlight Theater (30 m vertical marquee, chasing bulbs), LM-21 Grand Lumen Hotel (95 m, rooftop sign), Vantage car park (9 levels, drivable to the rooftop meet MEET-04), Lumen Imports DLR-03, Lumen Cross MEET-10.
3. Signage density per A6 rules; layered depth (near tubes, mid blade signs, far rooftop signs and LED boards); colour script: cyan/magenta dominant with amber sodium accents; avoid uniform rainbow.
4. Wet-night preset as default at night (L5), haze (L3), local post volume (L2), reflections (SSR/probes).
5. Crowds and queues outside venues (G3), parked modified cars at meets, music bleed audio zones.
6. Lumen Circuit race (RACE-05) drivable with barriers at night.
7. Performance: 23:00 wet, 1 % low ≥ 60 FPS (release, PC_High).
8. Review: CAM_13 + 6 extra Neon Mile shots (street, alley, rooftop meet, marquee close-up, hotel from Lumen Cross, aerial) Day and Night.
Status → ART PASS. Commit per item.
```

**Acceptance checks**

- From a still frame at night, a stranger can tell this is Neon Mile.
- The neon stays saturated with no washed-out whites, and performance meets target.

### DS3 — The remaining districts (one card per district, same pattern)

*Tools: Unity + Blender | Time: 3–6 days each | Needs: DS1 (benchmark), relevant A cards*

Paste the template with the district block filled in. Keep the order D2 Azure Coast → D5 Westhaven → D3 Ironport → D7 Falcon Airfield → D8 Redwood Valley → D4 Blackridge (Blackridge's driving roads were already polished in the W cards).

```text
Card DS3-<district id>. Take <district name> from BLOCKOUT to ART PASS at the same quality bar as Crown Downtown (DS1 captures are the benchmark).
Reference: Docs/design/07_DISTRICT_3D_REFERENCE/<folder>/district_3d_spec.md and Appendix B for palette/style dial.
Do, in order, capturing and committing per item: (1) layout audit against location_registry (landmarks, garages, meets, races, viewpoints present and reachable); (2) roads and roadside dressing; (3) buildings from world_building_placements.csv with the right style kits; (4) landmarks (A4); (5) vegetation and ground (A7, W2); (6) district-specific set dressing (list below); (7) life (traffic/pedestrians density for this district); (8) lighting per time of day + local volumes; (9) audio zones; (10) performance check on the district's routes; (11) Q2 review with the district cameras.
District block:
<paste one of the blocks below>
Status → ART PASS when done.
```

**District blocks (paste into the template):**

- **D2 Azure Coast** (`2_azure_coast`). Mediterranean–Californian coastal luxury, 90 % realism, warm sunsets. C1 Azure Coastal Expressway with palms (424 palms) and median planting; white stucco villas with glass balustrades and pools; Palm Promenade (LM-09), Azure Pier with an animated Ferris wheel (LM-08), Azure Marina (LM-10) with boats, Lantern Point Lighthouse (LM-06) with a rotating beam, Sorrel Cape cliffs (LM-22), Lantern Strait Bridge (LM-07); beaches with wet-sand bands, lifeguard towers, beach clubs. Races RACE-03/04/15. Cameras: CAM_14 + 4 extras (beach, villa street, pier at night, C1 sunset chase).
- **D5 Westhaven** (`5_westhaven`). Believable suburbs, 95 % realism. 1,973 buildings (mostly Suburban houses) with roof, colour and garden variation, driveways, fences, mailboxes, lawns, sprinklers, parked cars, power lines; Westhaven Plaza (LM-24), Westgate Retail Park (MEET-09), Second Gear Motors (DLR-01), **Juniper Lane starter garage (GAR-01, the player's first garage: hero quality)**, community garage GAR-07. Cameras: CAM_16 + 3 extras.
- **D3 Ironport** (`3_ironport`). Heavy industry at dusk, sodium orange, 85 % realism. Gantry Container Terminal (LM-11): 8 STS cranes (crane_placements.csv) and 6,050 containers (container_placements.csv), straddle carriers, reefer plugs, quay edge, ships at berth; refinery (LM-12) with tank farm (80 tanks), flare stacks (animated flame + heat haze), pipe racks; freight yard (LM-23) with rail sheds; Warehouse Row, Foundry 9 (GAR-02, hero interior), Undercroft meet beneath the Dockside Interchange (MEET-01); Gantry Expressway elevated (W6b). Races RACE-06, drag DRG-02. Cameras: CAM_15 + 4 extras.
- **D7 Falcon Airfield** (`7_falcon_airfield`). Decommissioned airfield turned motorsport venue: 1.9 km concrete runway with markings, taxiways, six barrel-roof hangars, a 1960s concrete control tower (LM-13), terminal, grandstands (LM-28), Falcon Dragway (DRG-01) with a staging tree and timing board, apron drift pad (DFT-04), Falcon Hangar 3 garage (GAR-10), heat haze by day, floodlit events at night. Cameras: CAM_17 + 3 extras.
- **D8 Redwood Valley** (`8_redwood_valley`). Pastoral farmland and a brick market town, golden mornings. Farmland patchwork (splat Farmland), red barns, silos, farmhouses, hay bales, fences; Millbrook town with the clock tower (LM-26), Millbrook Diner meet (MEET-11), the stone-arch Millbrook Bridge; Mirror Lake (LM-18) with a jetty (VP-08), Sable Falls (LM-25); Old Farm Road gravel (dust VFX), barn finds (hidden garages). RACE-10 rally stage. Cameras: CAM_18 + 3 extras.
- **D4 Blackridge Mountains** (`4_blackridge`). Forested granite ridges, touge culture, golden light and morning valley fog. Halcyon Pass Road (10 hairpins), Corvo Ridge Road (8 switchbacks), Summit Road, H2 tunnels and viaducts (W6), summit masts (LM-14), Hollow Pines village (LM-19) with its logging-yard drift zone (DFT-05), the Apex Shed garage (GAR-05), Skyline Overlook (VP-01) and other viewpoints, rock cuttings with netting, waterfalls, wildlife ambience. Races RACE-07/08/09/14. Cameras: CAM_09–CAM_12 + 3 extras.


## Phase 9 — Life, performance and QA

### G1 — Vehicle feel and cameras (Cinemachine 3)

*Tools: Unity | Time: 3–4 h | Needs: C1, A8*

```text
Card G1. Make driving feel premium. Read the existing vehicle controller first; tune and extend it, don't replace it unless I approve.
1. Handling targets for the Arden Kite (stock): 0–100 km/h ≈ 7.5 s, top speed ≈ 215 km/h, stable at 200 km/h on R1, oversteer controllable with handbrake, surface grip from SurfaceDefinition (C1), wet grip from _VC_Wetness. Expose tuning in a ScriptableObject; record before/after telemetry (speed, slip angles) on R_SMOKE.
2. Cameras (Cinemachine 3): chase cam (7.5 m back, 2.1 m up, FOV 62 → 75 with speed, slight lag, look-ahead into corners), hood cam, bumper cam, cinematic replay cams near landmarks; camera collision with Deoccluder (never inside buildings/terrain); impulse shake from kerbs, bumps, collisions, scaled by SurfaceDefinition bump amplitude.
3. Visual feedback: tyre smoke and skid marks (decals, pooled), sparks on scrapes, rain spray, exhaust heat haze, motion blur scaled by speed (L2), speed lines optional.
4. Input: Input System bindings for keyboard/mouse and gamepad (vibration on surfaces/impacts).
5. Capture a 20 s Recorder video of R_SMOKE chase cam (MP4) and screenshots.
Commit "G1 vehicle feel ✅".
```

**Acceptance checks**

- The car is fun and stable, the camera never clips, and feedback effects appear on every surface.

### G2 — Traffic AI and signals

*Tools: Unity | Time: 1–2 days | Needs: W5, A9*

```text
Card G2. Living traffic.
1. Lane graph from road profiles: lane centrelines per class (offsets from the cross-section), directions, speed limits; junction connectors (turn paths) with priorities; signal phases at signalised junctions (W5), stop signs on minor CO arms.
2. Simulation: Intelligent Driver Model for car-following, gap-acceptance for turns and lane changes, yield to the player, emergency brake + horn, simple indicators/brake lights (A9 emissives). Burst + Jobs for >200 agents; physics only for cars within 60 m of the player, kinematic beyond.
3. Spawning: ring 150–450 m around the player, never in view; density per district and time (Downtown rush hours, Neon Mile night, Westhaven low, Ironport trucks); max active on PC_High = 120.
4. Debug view: lane graph gizmos, agent states.
5. Validate: 10 min soak test in the player—no deadlocks at junctions, no cars driving into buildings, CPU cost ≤ 2 ms.
Commit "G2 traffic ✅".
```

**Acceptance checks**

- Junctions flow without gridlock, signals are obeyed, and the CPU cost stays within budget.

### G3 — Pedestrians and crowds

*Tools: Unity (unity:initialize-ai-navigation) | Time: 1 day | Needs: A10, W5*

```text
Card G3. Use the unity:initialize-ai-navigation skill. NavMesh on sidewalks, plazas and crossings (areas: Sidewalk, Crossing, Plaza; roads excluded), NavMesh Links at crosswalks gated by pedestrian signals. Behaviours: wander between points of interest, wait at crossings, sit on benches, gather at meets/venues (Neon Mile queues), react to the player car (step back, run away; no gore). LOD: full animation within 40 m, reduced update rate to 120 m, impostors beyond. PC_High budget: 150 visible, ≤ 2 ms CPU. Density by district/time.
Capture Crown Plaza noon and Neon Mile 23:00. Commit "G3 pedestrians ✅".
```

### G4 — Audio: engine, surfaces and city ambience

*Tools: Unity | Time: 1 day | Needs: G1*

```text
Card G4. Sound makes the world real. Audio Mixer with groups (Master, Music/Radio, SFX, Vehicles, Ambience, UI) and snapshots (Driving, Paused, Tunnel, Interior). Engine: layered RPM loops (on-load/off-load), turbo/exhaust pops; tyres per SurfaceDefinition (asphalt roll, gravel crunch, skid), wind by speed; ambience zones per district (Downtown traffic hum + distant sirens, Neon Mile music bleed + crowd, Ironport cranes/horns/machinery, Blackridge wind/birds/creeks, Azure waves/gulls, Westhaven dogs/sprinklers, Falcon wind/PA, Redwood insects/farm), reverb zones (tunnels, under viaducts, car parks, alleys), rain on surfaces/roof (L5). Use CC0/licensed sounds only (freesound CC0, Sonniss GDC bundles—record licences). Optimise per unity:optimize-audio (compression, load types).
Commit "G4 audio ✅".
```

### G5 — Activity hooks (races, zones, collectibles, garages)

*Tools: Unity | Time: 1–2 days | Needs: W8, G1*

```text
Card G5. Wire the world's activities from data (no full game modes yet, just working hooks):
- 16 race routes (race_routes[]): start/finish triggers, checkpoints along the listed roads, ghost-free timing, barriers/cones placement for circuits.
- Speed traps (10), speed zones (5), drift zones (5, scoring by angle × speed × duration), drag strips (3, staging-tree sequence), time trials (6), stunt jumps (9), photo challenges (10), collectibles (10 areas), hidden shortcuts (8), delivery hubs (5), car meets (12, parked-car spawners), garages (11) and hidden barn finds (6) with enter/exit interiors, dealerships (5).
- Map icons (W8) and a simple HUD toast for each activity; save system (unlocks, best times, garage contents).
Validate: every location in location_registry.csv is reachable by GPS from the starting garage (automated test) and its trigger fires in a player run.
Commit "G5 activity hooks ✅".
```

### P1 — Rendering performance pass (LOD, HLOD, occlusion, instancing)

*Tools: Unity | Time: 1 day | Needs: DS1*

```text
Card P1. Meet the PC budgets in Appendix C with headroom.
1. Tool "Veyra/Perf/Budget Report": for each cell and each review camera—draw calls, SetPass calls, visible triangles, shadow casters, real-time lights, materials, texture memory; flag violations; HTML report in Logs/.
2. Fix the top offenders in order of cost: shadow casting off for small props and distant buildings, LOD transition heights, impostors for distant trees/buildings, HLOD for far cells, GPU Resident Drawer/instancing coverage, material count per chunk (atlases), overdraw (foliage, neon glow cards, particles), light pool sizes.
3. Measure with the release build + Profiler (GPU and CPU timelines) on R_SMOKE, R_DOWNTOWN_NIGHT, R_BRIDGES; before/after table.
Target (PC_High, 1440p, RTX 4070): GPU ≤ 13 ms, CPU main thread ≤ 10 ms, render thread ≤ 8 ms.
Commit "P1 rendering perf ✅".
```

### P2 — Memory, streaming and loading

*Tools: Unity | Time: 4–6 h | Needs: W7*

```text
Card P2. Memory Profiler snapshots at start, after 10 min of driving and after a full loop (RACE-12 Grand Loop): find leaks (Addressables handles not released, duplicated textures/materials, runtime-created meshes never destroyed). Texture streaming budget (PC_High 4 GB), BC7/BC5 compression, mipmaps for all world textures, audio compression, mesh Read/Write off where possible. Loading: async scene activation time-sliced; shader warm-up (F1 variants) during the loading screen. Target: memory flat ±5 % after warm-up; no hitch > 50 ms during streaming.
Commit "P2 memory & streaming ✅".
```

### P3 — Platform profiling and the Android port plan

*Tools: Unity | Time: 4 h (PC) + later port | Needs: P1, P2*

```text
Card P3. (a) PC: final release-build profiling at 1440p and 1080p on PC_Ultra/High/Medium; write Docs/PERFORMANCE.md with tables per route and time of day. (b) Android plan (do not execute yet unless I ask): switch to the Mobile tiers, Vulkan, ASTC 6x6, reduced cell window (3 x 3), HLOD everywhere beyond 400 m, light pools 12, no SSR, SSAO off on Low, target 30 FPS Low/Mid; list the expected work and risks.
Commit "P3 profiling ✅".
```

### Q1 — Automated QA suite (run nightly or before every V1)

*Tools: Unity | Time: 3–4 h | Needs: S5, C1*

```text
Card Q1. Build automated QA (Unity Test Framework EditMode + PlayMode, batch-runnable via Tools/qa.cmd, HTML report in Logs/qa_<date>.html):
- Floating objects (renderer bottom > 0.15 m above the first surface below), sunken objects (> 1 m below), objects intersecting road surfaces or blocking lanes.
- Pink/missing materials, missing textures, materials not in VeyraMaterialLibrary, runtime Shader.Find (F1 test).
- Missing LODGroups, LOD triangle ratios violated, meshes over budget, single meshes > 250 m.
- Drivable validator (C1) and road profile validator (C2) results.
- Missing colliders on buildings/props, collision renderers visible.
- Duplicate objects from non-idempotent tools (same name + position).
- Lights without range/shadow limits, real-time lights outside the light pool.
- Texture size/compression violations, naming convention violations (CLAUDE.md).
- Reachability: every location reachable by GPS from GAR-01.
- Asset licences: every imported third-party asset listed in ASSET_SOURCES.md.
Commit "Q1 QA suite ✅".
```

### Q2 — Visual review rubric (your eyes, structured)

*Tools: Unity | Time: 1 h per review | Needs: S5*

```text
Card Q2. Run a visual review for <district / phase>. Capture the relevant review cameras (Appendix E) at Day, Golden and Night in the PLAYER build. For each shot score 1–5 on: (1) silhouette & composition, (2) materials & texture detail, (3) lighting & atmosphere, (4) density & life, (5) scale & proportions, (6) technical polish (no popping, seams, z-fighting, floating). Write Docs/reviews/<date>_<scope>.md with the contact sheet, scores, the 10 weakest details (with camera + screen position) and concrete fixes. Compare with the previous review side by side. A district reaches ART PASS at average ≥ 4.0 and may be proposed FINAL at ≥ 4.5 with no score below 4. Be harsh and specific—compare with AAA open-world references in terms of quality, not content.
```

## Fix-it library (Q3): paste when something breaks

**Pink or magenta materials in the player**

```text
Pink materials in the player build at <camera/location>. Identify the renderers and materials (log their names via a debug overlay or Frame Debugger), check whether the shader is URP-compatible, whether the material is referenced by a built scene/prefab/Addressable, and whether the needed variant is stripped. Fix via VeyraMaterialLibrary/F1 rules—never via Shader.Find. Re-run V1 R_SMOKE.
```

**Model rotated, mirrored or at the wrong scale**

```text
<asset> imports <rotated 90°/180°, mirrored, 100x too big/small>. Re-run the S4 axis round-trip with the same preset and compare. Check FBX importer settings (scale factor, convert units, bake axis conversion) and the Blender export arguments; fix the preset, not the instance transform. Re-export and confirm in Unity with the 10 m reference cube.
```

**The car falls through the road or terrain**

```text
Car falls through at <location/road id, s>. Run the drivable validator around it, check collider presence, convex flag, layer vs wheel-raycast mask, seams between chunk colliders, Rigidbody collision detection mode, spawn height. Fix and run a player autotest segment over that spot at 60/140/200 km/h.
```

**Invisible walls, steps or launches (BUG-001 class)**

```text
Invisible wall/launch at <location>. Plot collider height vs render height vs road profile for ±200 m (C2 tool). Find the discontinuity (step > 0.05 m, grade jump > 1.2 %/4 m, overlapping colliders, a hidden collider from an imported chunk). Fix the generator, re-run the road profile validator and the R_BRIDGES segment.
```

**Low FPS**

```text
FPS below target at <route/camera/time>: fps_1pct_low = <x>. In a release build with the Profiler attached, capture 300 frames there. Report the top 5 CPU markers and top 5 GPU passes, then fix the biggest one only (with before/after numbers). Repeat until the target is met. Don't lower visual quality without telling me which setting and what it costs visually.
```

**Hitches or stutter (more than 50 ms)**

```text
Hitches at <location/time>. Use Profiler markers around streaming (Addressables load/activation), GC.Alloc per frame, shader compilation (missing warm-up), physics mesh baking, lighting probe updates. Fix the cause (time-slice activation, pre-bake colliders, warm up variants, pool objects) and re-run W7's 200 km/h test.
```

**Terrain seams, holes or floating roads**

```text
<Seam/hole/floating> at <cell or position>. Check terrain neighbours/SetNeighbors, height sampling at borders, road skirts, and whether a road mesh height deviates from the profile. Fix without raising terrain over roads. Re-capture that spot.
```

**Z-fighting, flickering markings or decals**

```text
Flicker on <markings/decals/coplanar faces> at <camera>. Use depth offset in the marking shader or URP decals, remove duplicate coplanar geometry, check near clip plane (≥ 0.3 m for gameplay cameras). Re-capture at 2560x1440.
```

**Neon washed out or bloom too strong**

```text
Neon looks <washed out/white/overblown>. Check emission colour vs intensity (keep saturation, intensity 2.5–8), tonemapper (ACES desaturates highlights—test Neutral for comparison), bloom threshold/intensity/scatter per local volume, exposure at night. Propose two calibrated options with captures; I'll pick.
```

**Lights popping or shadows shimmering**

```text
<Lights pop / shadows shimmer>. Check Light Pool Manager fade times and hysteresis, shadow cascade splits, shadow resolution, stable-fit, depth/normal bias, and LOD cross-fade settings. Fix and capture a 10 s Recorder clip before/after.
```

**Blender MCP not responding (port 9876)**

```text
Blender MCP is not responding. Check: Blender 5.2 is running with the MCP add-on enabled and its server started on port 9876; no other process is using the port; firewall prompt not blocking. Until it is back, continue with headless Blender (blender.exe -b -P) for batch steps and tell me which visual checks are pending.
```

**Compile errors loop or Claude going in circles**

```text
Stop. List the current compile errors exactly (file:line), the last 3 changes you made, and your hypothesis for each error. Revert to the last green commit if the errors come from your last change (git stash/checkout the touched files), then re-apply the intended change in smaller steps, compiling after each.
```

**Scope creep or context overflow**

```text
We are drifting. Summarise in 10 lines: the card we're on, what's done, what's left, open problems. Write it to Docs/PROJECT_STATUS.md under "Session handover", commit, and stop. I'll start a fresh session with the Session Starter and this card.
```


## Appendices

### Appendix A — Data schemas and conversions

**Coordinates.** World x = east, y = north, z = elevation, in metres, with the origin at the SW corner of the 8 × 6 km map.

- Unity = (x − 4000, z, y − 3000): +X is east, +Z is north, +Y is up.
- Normalised map position = (x/8000, y/6000).
- Grid cell: column = "ABCDEFGH"[x // 1000]; row = 6 − (y // 1000), so row 1 is the north edge.
- Chunk (250 m) = `Chunk_<Cell>_<i><j>` with i = (x mod 1000) // 250 (west→east) and j = (y mod 1000) // 250 (south→north).

**Prefab yaw.** Unity yaw = −heading_deg for prefabs whose arm or forward axis is local +X (the package's lamp prefabs). Use 90 − heading_deg for +Z-facing prefabs. For buildings, `unity_yaw_deg = −rot_deg`, with the footprint width along local X.

| File | Columns / keys |
|---|---|
| location_registry.csv (209 rows) | id, name, category, district_id, district, x_m, y_m, norm_x, norm_y, unity_x, unity_y, unity_z, elevation_m, grid_cell, road, crew, details, description |
| crown_downtown_building_placements.csv (282) | name, typology (T1–T7, landmark), x_m, y_m, footprint_w_m, footprint_l_m, rot_deg, height_m, seed, unity_x, unity_y_ground, unity_z |
| world_building_placements.csv (2,708, supplement) | id, name, landmark_id, district_id, kind, shape, style_kit, typology, x_m, y_m, footprint_w_m, footprint_l_m, rot_deg, unity_yaw_deg, height_m, ground_center_m, ground_min_m, unity_x, unity_y_base, unity_z, cell, chunk, seed |
| container_placements.csv (6,050) / crane_placements.csv (8) | id, x_m, y_m, (length_axis, length_m, width_m, tiers, color_hex) / height_m, ground_m, unity_x, unity_y_base, unity_z, cell, chunk |
| `vegetation_<district>.csv` | type (pine_tree_14m, deciduous_tree_10m, bush_2m, palm_tree_12m), x_m, y_m, z_m, unity_x, unity_y, unity_z, scale |
| `street_furniture_<district>.csv` | prefab (streetlamp_urban_led_10m, streetlamp_sodium_highmast_25m, streetlamp_coastal_6m), x_m, y_m, z_m, unity_x, unity_y, unity_z, heading_deg, road_id |
| road_network_geometry.json | units, coordinates, roads[] {id, name, cls, closed, length_m, centreline_xy, profile_xyz_every_4m, profile_structure ("", elevated, tunnel, bridge_sea, bridge_river, viaduct), max_grade}, structures[] {road, s0, s1, kind, name, length, x, y}, interchanges[] {id, name, x, y, roads, desc, grade_separated}, river_centreline, river_width_m, lake_polygon, lake_level_m (150), coastline, districts {D1..D8: name, polygon, color}, race_routes[] {id, name, kind, roads, length_m, start, finish, district, valid}, shortcuts |
| terrain_tiles_manifest.json | format, height_mapping, unity_terrain_height 560, tile_world_size_m 1000, tiles[] {tile, file, resolution 513, world_x_m, world_y_m, unity_position [x, −60, z], unity_size, min/max_height_m, land_fraction} |

**Terrain RAW tiles.** 513 × 513, 16-bit unsigned little-endian, rows south→north. Height = −60 + v/65535 × 560 m. TerrainData size is (1000, 560, 1000), positioned at Y = −60. Use `heights[row, col] = v/65535`.

**Splat maps.** 4096 × 3072, north-up: splat0 RGBA = Grass, ForestFloor, Rock, Sand; splat1 RGBA = UrbanPaving, DirtGravel, Farmland, Lawn. A tile at column c and south-index r uses pixel columns c·512…+512 and rows (5 − r)·512…+512 counted from the top; flip it vertically when writing the alphamap.

**Whole-world heightmap PNG.** 4097 × 3073, 16-bit, north-up, same height mapping. Pixel column = x/8000·4096; row = (1 − y/6000)·3072.

**Road classes:**

| Class | Width | Lanes | Speed | Max grade |
|---|---|---|---|---|
| MW | 32 m | 3+3 | 110 km/h | 5 % |
| EX | 24 m | 2+2 | 100 km/h | 8.5 % |
| HW | 18 m | 2+2 / 1+1 | 90 km/h | 9 % |
| AR | 20 m | 2+2 + turn lane | 60 km/h | 8 % |
| CO | 11 m | 1+1 + parking | 40 km/h | 10 % |
| MT | 7.5 m | 1+1 | 60 km/h | 15 % |
| RU | 7 m | 1+1 | 70 km/h | — |
| GR | 5.5 m | single track | 50 km/h | 15 % |
| SV | 12 m | 1+1 | 40 km/h | 5 % |
| RW | 45 m | open surface | — | — |

**Network totals.** 93 roads (165.2 km), 17 structures, 24 interchanges/junctions, 16 race routes.

### Appendix B — Art direction: style dial and palettes

| District | Map colour | Day look (GTA-realism dial) | Night look (neon dial) | Key light and atmosphere |
|---|---|---|---|---|
| D1 Crown Downtown | #3E6FB0 | 80 % realism: glass, limestone, clean | 30 %: hotels, theatre streets, crowns | Crisp noon; golden skyline from the bay; cool/warm office windows |
| D2 Azure Coast | #1E9EA6 | 90 %: white stucco, palms, turquoise water | 10 %: pier lights, beach clubs | Warm sunsets, sea haze |
| D3 Ironport | #C8702E | 85 %: rust, containers, cranes | 15 %: sodium orange, flare stacks | Dusk smog, high-mast sodium |
| D4 Blackridge | #2F5E4E | 95 %: granite, conifers | 0 %: darkness, headlights only | Golden hour; morning valley fog |
| D5 Westhaven | #C49A2C | 95 %: suburbs, lawns | 5 %: porch lights, a few shop signs | Warm afternoon |
| D6 Neon Mile | #B8458C | 60 %: gritty brick by day | **70 %**: layered neon, wet streets, haze | Night is the hero; wet asphalt reflections |
| D7 Falcon Airfield | #66707E | 90 %: concrete, hangars | 20 %: floodlit event lighting | Heat haze, open sky |
| D8 Redwood Valley | #7C8B2E | 95 %: farmland, brick town | 5 %: town square lamps | Golden mornings, lake mist |

- **Neon palette:** cyan #00FFFF, hot magenta #FF007F, amber sodium #FFAA00, LED white 6500 K, signal red #FF2A2A. Use HDR intensity 2.5–8 with bloom threshold 0.9.
- **Colour script:** Neon Mile is cyan/magenta dominant with amber accents. Downtown nights mix warm and cool windows with a few accent signs. Avoid a uniform rainbow.
- **Map icon colours:**

  | Icon | Colour | Icon | Colour |
  |---|---|---|---|
  | Garage | #1F5FBF | Time trial | #0F8B8D |
  | Dealership | #2E8B57 | Car meet | #0096C7 |
  | Race start | #D7263D | Scenic viewpoint | #EF7D00 |
  | Drift | #7B3FBF | Collectible | #C9A227 |
  | Drag | #B5172B | Stunt jump | #4C9A2A |
  | Speed trap / zone | #E0A100 | Delivery | #8B5A2B |
  | Photo | #C2185B | Hidden garage | #5F6B4E |
  | Landmark | #222222 | | |

- **Road markings:** white lane and edge lines; yellow lines separate opposing traffic.
- **Material calibration (linear albedo):** asphalt 0.05–0.12, concrete 0.25–0.40, brick 0.15–0.30, vegetation 0.05–0.20. Never above 0.9 or below 0.02.

### Appendix C — Performance budgets

| Tier | Resolution / FPS | GPU frame time | CPU main thread | SetPass / draw calls | Visible tris | Real-time lights (shadowed) | Texture memory |
|---|---|---|---|---|---|---|---|
| PC Ultra | 1440p–4K / 60 | ≤ 14 ms | ≤ 10 ms | ≤ 600 / ≤ 4,000* | ≤ 12 M | 96 (12) | ≤ 6 GB |
| **PC High (target, RTX 4070)** | **1440p / 60+** | **≤ 13 ms** | **≤ 10 ms** | **≤ 450 / ≤ 3,000\*** | **≤ 8 M** | **48 (8)** | **≤ 4 GB** |
| PC Medium | 1080p / 60 | ≤ 12 ms | ≤ 10 ms | ≤ 350 / ≤ 2,200* | ≤ 5 M | 32 (4) | ≤ 3 GB |
| Android Mid | 1080p / 30 | — | ≤ 25 ms | 250 draw calls | ≤ 700 k | 12 (0) | ≤ 600 MB |
| Android Low | 720p / 30 | — | ≤ 30 ms | 150 draw calls | ≤ 300 k | 6 (0) | ≤ 300 MB |

\* Before GPU Resident Drawer batching. With it, raw draw calls fall sharply, so track SetPass calls and GPU time instead.

Other rules for all PC tiers:

- Per 250 m chunk: LOD0 ≤ 300 k tris (warn) / 500 k (fail) and ≤ 12 materials.
- Hitches: none above 50 ms.
- GC: ≈ 0 KB/frame while driving.
- Physics: ≤ 2 ms.
- Traffic plus pedestrians: ≤ 4 ms CPU.
- Post-processing: ≤ 1.5 ms. Fog: ≤ 0.6 ms. Vegetation: ≤ 3 ms GPU in Blackridge.

### Appendix D — Naming and folders

- **Meshes and prefabs:** `VC_<District>_<Category>_<Name>[_LOD#]`, for example `VC_D1_BLD_GlassTower_A_LOD0`.
- **Chunk objects:** `Road_Surface_*`, `Sidewalk_*`, `Building_*`, `Prop_*`, `Terrain_*`, `Vegetation_*`.
- **Collision:** `COL_` = non-convex MeshCollider, `UCX_` = convex (≤ 255 triangles), `UBX_` = box.
- **Materials and textures:** materials `M_<Name>`; Shader Graphs `SG_<Name>`; textures `T_<Name>_<BaseColor|Normal|MetallicSmooth|AO|Emission|Mask>`.
- **Scenes:** `VeyraCity.unity` (main), `Cells/Cell_<A1..H6>.unity`, `Test/T_*.unity`.
- **Folders:**
  - Runtime code: `Assets/_Veyra/World/…`
  - Editor tools: `Assets/_Veyra/Editor/…`
  - Material library: `Assets/_Veyra/World/Generated/Materials`
  - Chunks: `Assets/_Veyra/Art/World/Chunks/<Cell>/`
  - Data: `Assets/_Veyra/Data`
  - Blender scripts: `Tools/Blender`
  - Raw sources: `ArtSource/{Downloads,Blender,VeyraData}`
  - Status and logs: `Docs`, `Logs`, `Builds`, `Screenshots`
- **Git:** commit messages are `<CARD-ID> <title> ✅/❌`; tag `v<phase>-verified` after V1 passes.

### Appendix E — The 18 review cameras

Unity coordinates. "+h" means height above the ground or road surface (raycast). CHASE cameras use the road profile, the right-hand lane, and sit 7.5 m back and 2.1 m up.

| # | Name | Type | Position (X, Y, Z) / road | Look at | FOV | Time |
|---|---|---|---|---|---|---|
| 1 | CAM_01_World_Overview | Fixed | (−200, 3800, −6200) | (0, 0, 300) | 55 | Day |
| 2 | CAM_02_DT_Aerial_Day | Fixed | (−1050, 1150, −2350) | (250, 60, −500) | 40 | Day |
| 3 | CAM_03_DT_Skyline_Golden | Fixed | (350, 22, −2180) over Crown Bay | (200, 130, −600) | 50 | Golden |
| 4 | CAM_04_DT_Skyline_Night | Fixed | as #3 | as #3 | 50 | Night |
| 5 | CAM_05_DT_Plaza_Closeup | Fixed | (38, +1.75, −508) | (60, 33, −440) | 64 | Day |
| 6 | CAM_06_DT_Drive_Crown | Chase | A1 Crown Avenue, 300 m, northbound | ahead 30 m | 62 | Day |
| 7 | CAM_07_DT_Drive_Meridian_Night | Chase | A2 Meridian Boulevard, 760 m, eastbound | ahead 30 m | 62 | Night, wet |
| 8 | CAM_08_R1_Viaduct_Night | Chase | R1 Veyra Ring, 650 m (Harbor Viaduct) | ahead 60 m | 64 | Night |
| 9 | CAM_09_BR_Overlook_Golden | Fixed | (560, +6, 1490) at VP-01 | (250, 60, −700) | 55 | Golden |
| 10 | CAM_10_BR_Halcyon_Drive | Chase | A18 Halcyon Pass Road, 2380 m | ahead 35 m | 62 | Day |
| 11 | CAM_11_BR_Switchbacks_Aerial | Fixed | (1800, 560, 750) | (2390, 200, 1400) | 42 | Day |
| 12 | CAM_12_H2_Highline_Golden | Chase | H2 Ridgeline Highway, 3950 m, westbound | ahead 60 m | 62 | Golden |
| 13 | CAM_13_NM_Lumen_Night | Chase | A7 Lumen Avenue, 700 m | ahead 40 m | 62 | Night, wet |
| 14 | CAM_14_AC_Lighthouse_Golden | Fixed | (−190, +2, −2570) at VP-03 | (535, 40, −2605) bridge | 55 | Golden |
| 15 | CAM_15_IP_Berth_Dusk | Fixed | (1600, +2, −2700) on Berth Road | (2300, 40, −2580) cranes | 60 | Dusk |
| 16 | CAM_16_WH_Street_Day | Fixed | (−2260, +1.7, −610) | (−2200, 8, −550) Westhaven Plaza | 64 | Day |
| 17 | CAM_17_FA_Dragway_Aerial | Fixed | (2500, 120, −1100) | (2900, 8, −600) | 45 | Day |
| 18 | CAM_18_RV_MirrorLake_Morning | Fixed | (−2950, +3, 1800) at VP-08 | (−2950, 160, 2600) | 55 | Morning (07:30) |

### Appendix F — Sources, licences and outsourcing

- **Safe sources:**
  - CC0: ambientCG (materials), Poly Haven (HDRIs, materials, models), Kenney and Quaternius (props), freesound filtered to CC0.
  - Licensed: Unity Asset Store (Standard EULA permits use in games), Fab/Epic and Sketchfab, checked per asset. CC-BY needs attribution; avoid NC, editorial and "NoAI" licences.
  - Mixamo characters and animations under Adobe's terms.
- **Never:** ripped or extracted assets from GTA, Cyberpunk or any commercial game; real car brands, logos or badges; real company logos; copyrighted music. Keep every source in `Docs/ASSET_SOURCES.md`, enforced by card B1.
- **Hero car and characters:** if A8/A10 quality isn't enough, commission an original vehicle (ArtStation or a vehicle-specialist studio) with a game licence, or buy an unbranded fictional sports-coupe model with a game-use licence and adapt it to the Arden Kite design.
- **Texture tools:** Blender baking (free), Material Maker (free), Substance (commercial).

### Appendix G — Command-line cheat sheet (Windows)

Adjust the Unity Hub install path if needed.

**Unity batch: CityBuilder, tests and builds**

```bat
set UNITY="C:\Program Files\Unity\Hub\Editor\6000.6.8f1\Editor\Unity.exe"
%UNITY% -batchmode -quit -projectPath E:\VeyraCity -executeMethod CityBuilder.BuildCityBatch -logFile E:\VeyraCity\Logs\citybuilder.log
%UNITY% -batchmode -projectPath E:\VeyraCity -runTests -testPlatform EditMode -testResults E:\VeyraCity\Logs\editmode.xml -logFile E:\VeyraCity\Logs\tests.log
%UNITY% -batchmode -quit -projectPath E:\VeyraCity -executeMethod VeyraBuild.BuildDevMono -logFile E:\VeyraCity\Logs\unity_build.log
%UNITY% -batchmode -quit -projectPath E:\VeyraCity -executeMethod VeyraBuild.BuildReleaseIL2CPP -logFile E:\VeyraCity\Logs\unity_build_release.log
```

**Player test run and verification**

```bat
E:\VeyraCity\Builds\DevMono\VeyraCity.exe -screen-width 2560 -screen-height 1440 -screen-fullscreen 1 -autotest 60 -route R_SMOKE -statsPath E:\VeyraCity\Logs\stats.json -logFile E:\VeyraCity\Logs\player.log
powershell -ExecutionPolicy Bypass -File E:\VeyraCity\Tools\verify_run.ps1 -Log E:\VeyraCity\Logs\player.log -Stats E:\VeyraCity\Logs\stats.json
```

**Blender headless**

```bat
"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b --factory-startup -P E:\VeyraCity\Tools\Blender\process_city_map.py -- --input E:\VeyraCity\ArtSource\Downloads\<file> --mode kitbash --out E:\VeyraCity\Assets\_Veyra\Art\World\Chunks --report E:\VeyraCity\Logs\ingest.json
```

`CityBuilder.BuildCityBatch` and `VeyraBuild.*` are the method names this playbook asks Claude to create or confirm. If your class has a namespace, include it, for example `Veyra.Editor.CityBuilder.BuildCityBatch`.

### Appendix H — Definition of done (per card)

1. It compiles with zero errors and no new warnings.
2. The card's acceptance checks pass, and Claude has quoted the evidence: screenshots, logs, stats or validator output.
3. Runtime-critical work has been verified in a player build.
4. No regressions: F1, C1 and C2 validators are green, and V1 passes at the end of each phase.
5. `PROJECT_STATUS.md` and `BUG_TRACKER.md` are updated, and the commit is tagged with the card ID.
6. Third-party assets are licensed and listed.
7. Nothing is labelled FINAL without your review.
