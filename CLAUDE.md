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
- Use Tools/Blender/vc_tools.py. Export presets VC_Unity_Prop (local pivot) and VC_Unity_World (world-positioned chunks) as confirmed by the S4 axis round-trip test. Never export world-positioned content before S4 passes.
- Confirmed settings (S4 passed 2026-11-10, Logs/S4/axis_test.txt):
  - Both presets: FBX, axis_forward -Z, axis_up Y, global_scale 1, apply_unit_scale, apply_scale_options FBX_SCALE_UNITS, bake_space_transform (Apply Transform). Unity imports with Scale Factor 1, Convert Units on, Bake Axis Conversion on, Read/Write off (VeyraModelImportDefaults, first import of Assets/_Veyra/Art/Models and Art/World/Chunks). Net mapping: Blender (x, y, z) → Unity (x, z, y). With Bake Axis Conversion on, axis_forward makes no difference (-Z and Z measured identical).
  - VC_Unity_World: no extra turn; world positions kept (marker at Blender (100, 200, 5) → Unity (100, 5, 200); north +Y → +Z, east +X → +X).
  - VC_Unity_Prop: the pivot object is moved to the origin and turned 180° about Blender Z for the export, so a model's front (Blender -Y) faces Unity +Z; the file's top objects show rotation (0, 180, 0), scale 1.
  - LODs: Unity puts ONE LODGroup on the FBX root and puts every *_LOD<n> mesh of the file in level n; export one LOD set (one prop or one chunk) per file.
  - Veyra package glTF/GLB (glTF +X east, -Z north) through glTFast arrives turned 180°: instantiate with root rotation (0, 180, 0) via VeyraGltf.InstantiatePackageGlb (D1 blockout: Veyra Spire at (170, 9.5, -670)).
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

---

## Kept from the previous CLAUDE.md (E:\PROJECT BANGERRR\CLAUDE.md)
These earlier rules stay in force because nothing above contradicts them. Where the old file conflicted with the rules above, the rules above win; those conflicts are listed at the end.

### Platform and engine
- Windows PC (x64), keyboard and gamepad from day one.
- IL2CPP for release builds, Mono for iteration.
- Upgrade to Unity 6.7 LTS only at a milestone boundary, on a Git branch. Never upgrade mid-milestone.
- Everything for this project stays on E: (files, caches, builds, temp output; set TEMP/TMP to E:\Temp for Unity and Blender jobs). The Unity project must never live inside OneDrive.

### Reference documents (design repo, read-only)
GAME_DESIGN_DOCUMENT, TECHNICAL_ARCHITECTURE, VEHICLE_HANDLING_SPEC, NITRO_BALANCE_SPEC, INPUT_AND_ACCESSIBILITY_SPEC, RACE_RULES_SPEC and VEHICLE_ASSET_PIPELINE are in E:\PROJECT BANGERRR\Docs. The world data is in E:\PROJECT BANGERRR\VEYRA_CITY_WORLD_DESIGN.

### Toolchain
- Unity CLI and MCP (`unity`), Blender MCP on TCP 9876, headless Blender, git and gh.
- MCP servers that duplicate native CLI or filesystem actions stay off, for token security.

### Code architecture
- All runtime code lives under Assets/_Veyra/ in assembly definitions. Planned assemblies: Core, VehiclePhysics, Input, Assists, Nitro, Racing, AI, UI. Also in use: World, Gameplay, Editor, Tests.
- VehiclePhysics never references UI or Input.
- Input uses the Unity Input System only, through the DriverIntent provider.
- AI produces the same DriverIntent as the player.
- Nitro is a bounded, meter-driven boost with three modes: Standard, Clean Boost and Overdrive. No heat or temperature meters.
- ScriptableObject and MonoBehaviour classes each live in a file with the same name as the class. Otherwise their assets and components break in player builds.

### Vehicle physics
- Physics runs in FixedUpdate. The project uses 100 Hz; 50 and 120 Hz must stay supported.
- Every tuning value (mass, torque curve, springs, dampers, friction) lives in a versioned ScriptableObject, never hard-coded.
- Telemetry (speed, slip angle, suspension compression, G-forces) is recorded during tests.

### Vehicles in Blender
- 1 Blender unit = 1 m.
- Apply rotation and scale only. Never apply location on wheels, calipers, steering wheel, doors or part sockets; wheel origins stay on the hubs.
- The car front faces Unity +Z after import, with no −90° X fix.
- Names: Body_LOD0/1/2, Wheel_FL/FR/RL/RR (spin), Caliper_FL/FR/RL/RR (steer only), UCX_/UBX_ collision.
- Packed mask textures store smoothness (1 − roughness) in alpha.

### Principles
- **Honest quality:** never mark anything finished without objective evidence. Targets are measured, not assumed.
- **Non-destructive:** never overwrite existing design docs, models or art without a backup and a Git checkpoint.
- **Secrets:** never commit or hard-code API keys, tokens or credentials.
- **Licensing:** third-party content needs a licence file in ArtSource/Downloads before it enters the project. Real-brand cars and ripped game assets are never used.

### Conflicts resolved in favour of the rules above
- **Framerate:** "stable 60 FPS on recommended hardware" → 1440p on an RTX 4070, p99 ≤ 16.6 ms, no hitch > 50 ms.
- **Design repo:** "design, assets and documentation repository" → read-only reference. Status docs now live in E:\VeyraCity\Docs.
- **Blender export axes:** "−Z forward, Y up, apply unit" as the fixed export → VC_Unity_Prop and VC_Unity_World, as confirmed by the S4 round-trip test. The old setting was only validated for one local-pivot vehicle (Arden Kite, Phase 2).
- **Collision prefixes:** only UCX_ and UBX_ before → COL_ (non-convex) added; UCX_ is convex.
