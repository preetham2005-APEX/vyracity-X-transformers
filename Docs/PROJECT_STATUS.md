# PROJECT_STATUS — VEYRA CITY (Unity project)

**Last updated:** 2026-11-09 · **Process:** VEYRA Claude Code Playbook (one card at a time, evidence, commit per card) · **Rules:** `CLAUDE.md`

The history before the playbook (Phases 0–2 and the full-game pass) is recorded in the design repo, `E:\PROJECT BANGERRR\Docs\PROJECT_STATUS.md` (read-only from now on). Its final game state is commit `afd9978` in this repo.

## Playbook card log
| Card | Date | State | What changed | Evidence | Commit |
|---|---|---|---|---|---|
| S1 Create CLAUDE.md | 2026-11-09 | Done | `CLAUDE.md` created with the S1 rules verbatim, plus the non-contradicting rules from the old design-repo CLAUDE.md, and a list of the conflicts and how each was resolved | `Logs/S1/CLAUDE_md.diff` | this commit |
| Visual upgrade pass (user request, outside the card order) | 2026-11-09 | Implemented, Tested (player build, review shots) | Post: ACES, bloom 1.2 (threshold 1.25 day → 0.9 night), SSAO 1.2/0.8 blue noise. Neon #00FFFF/#FF007F/#FFAA00. Wet roads to 0.96 smoothness via gloss maps (smoothness in metallic alpha). Weather cycle clear/overcast/rain. PBR road, kerb (granite), sidewalk and facade library. **All 2,708 buildings, 15,269 containers and 8 cranes generated from the data supplement** into `Chunk_<Cell>_<i><j>` with UBX colliders. 9.6 M grass instances. GameFlow initialises material templates in Awake (execution order −1000). Licence-gated `Tools/Blender/process_city_map.py` (B2 pipeline) | `Screenshots/2026-11-09_visual-pass*`, `Logs/visual*/stats.json`, `Logs/B2_test/chunks` | see git log |

## Known non-compliance with CLAUDE.md (for later cards)
- **Materials and shaders:** `World/RuntimeMaterials.cs` still falls back to `Shader.Find` in runtime code, and materials come from `GameDatabase` templates, not `VeyraMaterialLibrary`. Card F1.
- **Chunk naming:** buildings now use `Chunk_<Cell>_<i><j>`; roads and terrain meshes are still `Cell_<x>_<z>` on a 250 m grid that isn't aligned to the 1 km cells; the rule is `Chunk_<Cell>_<i><j>`. Cards B2 / W7.
- **Folder layout:** world data is in `Assets/_Veyra/Resources/World` and `WorldSource/`, not `Assets/_Veyra/Data` and `ArtSource/VeyraData`. Card S3.
- **LODs:** city meshes have no LOD0/1/2. Several packages in the rules are not installed yet (Cinemachine 3, Splines, Addressables, glTFast, Terrain Tools and others).
- **Status docs:** `Docs/BUG_TRACKER.md` now exists; it carries the open bugs from the design repo (full history up to BUG-030 stays there).
