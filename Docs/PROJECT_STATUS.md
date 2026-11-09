# PROJECT_STATUS — VEYRA CITY (Unity project)

**Last updated:** 2026-11-09 · **Process:** VEYRA Claude Code Playbook (one card at a time, evidence, commit per card) · **Rules:** `CLAUDE.md`

The history before the playbook (Phases 0–2 and the full-game pass) is recorded in the design repo, `E:\PROJECT BANGERRR\Docs\PROJECT_STATUS.md` (read-only from now on). Its final game state is commit `afd9978` in this repo.

## Playbook card log
| Card | Date | State | What changed | Evidence | Commit |
|---|---|---|---|---|---|
| S1 Create CLAUDE.md | 2026-11-09 | Done | `CLAUDE.md` created with the S1 rules verbatim, plus the non-contradicting rules from the old design-repo CLAUDE.md, and a list of the conflicts and how each was resolved | `Logs/S1/CLAUDE_md.diff` | this commit |

## Known non-compliance with CLAUDE.md (for later cards)
- **Materials and shaders:** `World/RuntimeMaterials.cs` still falls back to `Shader.Find` in runtime code, and materials come from `GameDatabase` templates, not `VeyraMaterialLibrary`. Card F1.
- **Chunk naming:** city chunks are named `Cell_<x>_<z>` on a 250 m grid that isn't aligned to the 1 km cells; the rule is `Chunk_<Cell>_<i><j>`. Cards B2 / W7.
- **Folder layout:** world data is in `Assets/_Veyra/Resources/World` and `WorldSource/`, not `Assets/_Veyra/Data` and `ArtSource/VeyraData`. Card S3.
- **LODs:** city meshes have no LOD0/1/2. Several packages in the rules are not installed yet (Cinemachine 3, Splines, Addressables, glTFast, Terrain Tools and others).
- **Status docs:** `Docs/BUG_TRACKER.md` does not exist in this repo yet; bugs up to BUG-030 are in the design repo. Card S2 baseline.
