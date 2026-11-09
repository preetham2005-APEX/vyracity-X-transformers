# PROJECT_STATUS — VEYRA CITY (Unity project)

**Last updated:** 2026-11-09 · **Process:** VEYRA Claude Code Playbook (one card at a time, evidence, commit per card) · **Rules:** `CLAUDE.md` · **Baseline:** tag `baseline-before-playbook` (`6911501`)

The history before the playbook (Phases 0–2 and the full-game pass) is recorded in the design repo, `E:\PROJECT BANGERRR\Docs\PROJECT_STATUS.md` (read-only from now on). Its final game state is commit `afd9978` in this repo.

## Playbook cards

| Card | Date | Result | Evidence | Notes |
|---|---|---|---|---|
| S1 Create CLAUDE.md | 2026-11-09 | ✅ Passed (`7843ee0`) | `Logs/S1/CLAUDE_md.diff` | S1 rules verbatim, plus the non-contradicting rules of the old design-repo CLAUDE.md and the list of conflicts resolved |
| Visual upgrade pass (user request, outside the card order) | 2026-11-09 | Implemented, tested in a player build; not a card, not accepted (`e65856d`) | `Screenshots/2026-11-09_visual-pass*`, `Logs/visual*/stats.json` | Overlaps F2, L2, A3, B2: post (ACES, bloom 1.2, threshold 1.25 day → 0.9 night, SSAO), neon palette, wet roads, weather cycle, PBR road/kerb/sidewalk/facade library, 2,708 buildings + 6,050 container stacks + 8 cranes from the data supplement into `Chunk_<Cell>_<i><j>`, grass, licence-gated `Tools/Blender/process_city_map.py` |
| SEC-002 token fix (security, outside the card order) | 2026-11-09 | ✅ Fixed in HEAD (`6911501`); user must regenerate the token | `git grep` on HEAD: no token | Sketchfab API token moved from 7 `Tools/*.py` scripts to the `SKETCHFAB_API_TOKEN` environment variable |
| S2 Audit the existing project | 2026-11-09 | ✅ Passed (`50a0b75`) | `Docs/AUDIT.md`; `Logs/S2/` (scene_stats, playmode_baseline, downloads_*); `Screenshots/2026-11-09_S2/` (3 shots) | No game changes. `.gitignore` now ignores `Screenshots/`; tag `baseline-before-playbook`. BUG-001 to BUG-004 recorded with repro steps; design-repo bugs carried over; BUG-037 to BUG-048 new |
| S3 Import the Veyra data and coordinate helpers | 2026-11-09 | ✅ Passed (this commit) | `Logs/S3/validate_data.txt` (PASS: 209 locations, 93 roads / 165.2 km, 17 structures, 24 interchanges, 16 race routes, 282 Crown Downtown placements; LM-01 → (170, 9.8, −670), LM-06 → (−130, 9.4, −2640)); `Logs/S3/editmode_tests.json` (59/59, 16 new); `Logs/S3/import_manifest.csv` (98 files, SHA-256); `Logs/S3/alignment_check.txt`; `Screenshots/2026-11-09_S3/T_DataCheck_topdown.png`, `T_DataCheck_vs_master_map.png` | Data copied (not moved) by the re-runnable `Tools/World/import_veyra_data.py` into `Assets/_Veyra/Data`, `ArtSource/VeyraData/terrain`, `Assets/_Veyra/Art/Textures/Maps`, `Docs/design`. Readers in `Assets/_Veyra/World/Data` (CSV, LocationRegistry, placements, RoadNetwork via Newtonsoft, TerrainManifest). Coordinates: the existing `VeyraCity.Core.WorldCoordinates` is the playbook's "VeyraCoords" (extended with CellOf, ChunkOf, YawFromHeading; `BuildingGenerator.ChunkOf` now uses it). Menus: `Veyra/Data/Validate Data`, `Build Data Check Scene`, `Capture Data Check Top-Down`, `Build And Capture Data Check`. `T_DataCheck.unity` matches the master map (north +Z up, Crown Bay south-centre, ring road, coastal expressway, northern hairpins). `VeyraCity.unity` is aligned: 5 landmarks hit their own building collider at the data position with roofs exactly at base + height; terrain within 0.22 m. Supplement provenance recorded in `Assets/_Veyra/Data/README.md` |

**Next card:** V0 (development player build and automated test harness). Planned order after the user's brief: V0 → S4 → S5 → F1 → F2 → F3 → B1 → B2/B3 → C1–C3 → L1–L5 → … (S4 must pass before any world-positioned export; S5 before V1).

**Working tree note:** the Unity Editor rewrote some files by itself during S2/S3 and they are left uncommitted: `QualitySettings.asset` (antiAliasing 0 → 4, synced from the PC URP asset's MSAA), `URPProjectSettings.asset` (folder path), 12 Arden Kite materials (legacy `_Color` synced to `_BaseColor`) and `Marking_Yellow.mat` (BUG-039).

## Baseline (card S2, 2026-11-09)

**What exists.** A playable open-world prototype: raycast vehicle physics at 100 Hz with assists, input, nitro (3 modes), races (16 routes), AI racers, kinematic traffic, garage, career and menus, on a city generated from the Veyra data: 48 terrain tiles, 93 roads as blockout meshes with 750 non-convex drivable colliders, 2,708 buildings, containers, cranes, water, 67,499 instanced trees and 2,777 lamps.

**Measured.**
- Editor loads with 0 errors and 0 warnings. A 138 s Play session: 0 errors, 3 warnings (Kite convex hulls over 256 polygons), ~198 FPS in the Editor (reference only).
- Scene: 13,631 GameObjects, 3,050 meshes, 1.28 M triangles in meshes (plus instanced props), 8,790 box colliders, 48 terrains, 1 light, 0 reflection probes.
- Last player run (visual pass, Dev Mono, 1440p): avg 117 FPS, **p99 18.28 ms, 11 hitches > 50 ms**, 0 exceptions. No release IL2CPP 1440p run exists yet.

**Main gaps against CLAUDE.md and the playbook** (details in `Docs/AUDIT.md`, IDs in `Docs/BUG_TRACKER.md`):
- **Road data:** bridge abutment steps up to 8.5 m (BUG-001), grades up to 25 % (BUG-034), a 53 m step on A19 (BUG-035). The engine reproduces the data exactly, so the fix is C2's eased profile (decision D-5).
- **Materials:** runtime `Shader.Find`, `new Material` and keyword toggles; no `VeyraMaterialLibrary`, no `VC_Variants.shadervariants`; `WorldEnvironment` writes shared assets in edit mode (BUG-002, BUG-039, BUG-046) → F1.
- **Geometry:** no LOD1/LOD2 anywhere (BUG-038); 1,207 renderers wider than 250 m (BUG-037); roads grouped as `Cell_x_z`, not `Chunk_<Cell>_<i><j>` (BUG-040).
- **Physics:** Kerb/Cobblestone/Metal surfaces missing, terrain all Grass (BUG-042); trees and lamps have no colliders (BUG-043); the wheel raycast uses all layers (`~0`), not "Default" as CLAUDE.md says.
- **Data layout:** since S3 the design data is in `Assets/_Veyra/Data` and `ArtSource/VeyraData` with readers in `World/Data`. The current city pipeline (CityBuilder, `WorldData`) still reads the derived copies in `Assets/_Veyra/Resources/World` and `WorldSource/`; they stay until the W cards move the builders over (not deleted). Supplement provenance: `Assets/_Veyra/Data/README.md` (D1 identical to the package; D2–D8 still to verify against the blockout GLBs in A3).
- **Packages missing:** Addressables, glTFast, Terrain Tools, Recorder, Memory Profiler (Newtonsoft is now a direct dependency, S3). Cinemachine and AI Navigation are installed but unused.
- **Licensing:** 1.5 GB of downloads with no licence sidecars: 33 CC-BY-4.0, 7 Sketchfab Standard, 1 CC-BY-SA-4.0, 1 unlicensed photogrammetry block, 3 ripped GTA Liberty City maps (excluded, never opened). Nothing third-party is in `Assets/` yet (B1).
- **Tools:** CityBuilder recreates ~3,800 assets with new GUIDs per run and overwrites URP and tuning assets (BUG-045).

**Top 5 risks** (from the audit): road data not drivable as authored; licensing/originality of the downloads; performance and scale (no LODs, oversized meshes, hitches); runtime material architecture; non-idempotent world rebuilds.

## Open user actions
- Regenerate the Sketchfab API token (SEC-002) and revoke the old GitHub token (SEC-001).
- Approve or decline the Unity CLI update 1.0.0-beta.13 → beta.17.
- Decide in B1: Sketchfab Standard and CC-BY-SA kits; delete the GTA Liberty City downloads (two in `ArtSource/Downloads`, plus the zip and extracted folder in the design repo root).
