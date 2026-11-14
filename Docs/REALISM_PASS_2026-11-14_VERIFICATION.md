# Realism pass — independent verification, 2026-11-14

**By:** session "Game realism audit and physics improvements" (author of `Docs/OPUS_55_MASTER_PROMPT.md`).
**Method:** read the code and assets at `955cbfd9` plus the working tree. No Unity lock taken, no
Editor run, nothing built. This is a read-only check of what is actually in the repo against what
the realism brief asked for and what `Docs/PROJECT_STATUS.md` records as landed.
**Scope note:** four sessions were writing to this checkout while this was written, so anything
uncommitted may have moved since.

---

## A. Verified in the repo

Each line was confirmed by reading the file, not by trusting a commit message.

| Brief item | State | Evidence |
|---|---|---|
| Wheel contact is a sphere sweep, not a ray | **Landed** | `VehiclePhysics/VehicleController.cs:845` — `Physics.SphereCastNonAlloc`. A ray fallback remains at `:862` |
| Stuck recovery (nudge → respawn) | **Landed** | `Gameplay/StuckRecovery.cs`; wired in `Gameplay/GameFlow.cs:1156-1159` with a HUD toast |
| Missing SurfaceDefinitions | **Landed** | `Data/Surfaces/` now holds 12 assets — added `kerb`, `cobblestone`, `metal`, `paving`, `rock`, `sand` to the original 6 |
| Render scale 1.0 | **Landed (uncommitted)** | `Settings/VC_URP_PC_High.asset:29` — `m_RenderScale: 1` (was 0.9) |
| SSAO at real quality | **Landed (uncommitted)** | `Settings/VC_Renderer_PC_High.asset` — `Samples: 0` (High), `NormalSamples: 2` |
| Decal Renderer Feature | **Landed (uncommitted)** | present in `VC_Renderer_PC_High/Medium/Ultra.asset` |
| Road decal content | **Landed** | `7cd1d6bf` — 16-tile original procedural atlas (oil, skids, grates, cracks, patches, gutter grime) |
| Adaptive Probe Volumes | **Started** | `ProbeVolume` references present in `Scenes/Cells/Cell_A*.unity`. Sky occlusion not confirmed |
| Scatter spawner time-slicing (BUG-170) | **Landed** | `AI/StreetFurniture.cs` and `AI/ParkedCars.cs` both use `IEnumerator` + `FrameBudget.Pump` with `YieldEvery = 4`; matches the pattern already in `StreetTrees` |
| Building kit replaces box shells | **Landed** | `a0b6c9c8` — 2,690 of 2,708 records assembled from an 88-model kit; night glass `b73f605b` |
| Road shoulder work | **Started** | shoulder handling present in `Editor/RoadRenderBuilder.cs` |

Measured result worth recording: D2's BUG-170 trims took R_SMOKE release CPU p95/p99 from
33.6/79.9 ms to 17.6/26.6 ms (`fdd2014c`, `Logs/D2_BUG170/summary.txt`).

---

## B. Not started

- **Visual Effect Graph was missing; it has now been added** (see §F). No `.vfx` asset exists yet,
  so every smoke, dust, spray, crash and nitro effect is still the runtime-built Shuriken system on
  a 64×64 procedural puff. **This remains the largest realism gap: six-way lit smoke, the nitro
  moment and the crash VFX are all still to be authored.**

  Correction to an earlier claim of mine: **Shader Graph was never missing.** It resolves as
  `com.unity.shadergraph` 17.6.0 with `"source": "builtin"` — it ships with the Editor and arrives
  as a dependency of URP, so it is absent from `manifest.json` but present in the project. Same for
  **Burst 1.8.14** and **Collections 2.4.3**, which arrive as dependencies of
  `com.unity.render-pipelines.core`. Only Mathematics, Terrain Tools, Recorder, Memory Profiler and
  Addressables are genuinely absent of the packages CLAUDE.md names.
- **Pedestrians are still boxes and spheres.** `AI/Pedestrians.cs:74, 578-581, 684-686` —
  `sphere` head, `sphere` hair, procedural `torsoMesh`/`limbMesh`.
- **Foliage wind is still not consumed.** `_VC_WindStrength` is published by
  `World/VeyraGlobals.cs` and `World/WorldEnvironment.cs`, and read only by `VC_SkyDome.shader`
  and `VC_WaterSurface.hlsl`. No foliage shader reads it; no leaf translucency, no impostors.
- **Facade depth is faked, not built.** `VC_FacadeSurface.hlsl:267` shades a "fake reveal" —
  there is no parallax-occlusion window interior and no recessed geometry.
- **`Validate Drivable` has never been run in this pass.** There is no `Logs/drivable_report.csv`,
  `Logs/drivable_summary.txt` or `Logs/drivable_misses.png` anywhere. The brief's acceptance bar
  (≥ 99.5 % ok, zero `blocked`) is therefore **unmeasured**, and the sphere-cast wheel fix is
  unproven at the world sites that originally failed.
- **PBR terrain is unchanged.** `CityBuilder.BuildTerrainLayers()` still builds layer diffuse maps
  from `ProcTextures.Plain(...)` with no normal or mask maps.

---

## C. Stale records to correct

- **BUG-042 is still listed Open** ("only 6 SurfaceDefinitions; Kerb, Cobblestone and Metal
  missing"). Twelve now exist, including all three named. The surface-tagging half of the bug
  (CityBuilder tagging the whole terrain as Grass) needs re-checking before it is closed, but the
  row as written no longer describes reality.
- **BUG-001, BUG-037, BUG-038, BUG-050 and BUG-052 remain Open and untouched** in the tracker and,
  as far as this check can tell, in the code. They are world-owner items and are correctly listed
  as remaining in `PROJECT_STATUS.md`.
- Several "landed" items in §A are **in the working tree, not committed** (render scale, SSAO,
  decal feature). They are one `git checkout` away from being lost.

---

## D. Remaining work

The coordinator's table in `Docs/PROJECT_STATUS.md` ("Realism pass — state and remaining work",
2026-11-14 evening) is accurate as far as this check goes and is the live list — this section only
adds what the independent check turned up.

Additions and emphasis:

1. **VFX Graph is installed (§F); the Editor still has to resolve it.** Shader Graph was already
   present. The VFX authoring work itself is untouched.
2. **Run `Veyra/Physics/Validate Drivable` and commit the report.** The physics claim of the whole
   pass rests on it and it has not been measured once. Before/after would be ideal; after alone is
   the minimum.
3. **Commit the Lighting working-tree changes.** Render scale, SSAO and the decal feature are
   uncommitted in a checkout that several sessions write to.
4. **Re-audit BUG-042's second half** (terrain tagged Grass everywhere) now that the splat-driven
   tagging has landed, and close or re-word the row.

Still genuinely blocked on the user, not on a session:

- **BUG-066** — 5 CC-BY real-brand car rebadges.
- **BUG-130** (Sable Falls: registry says a 12 m waterfall, terrain gives 4 m) and **BUG-171**
  (three building footprints far outside their district's distribution). Both are *data authority*
  calls. CLAUDE.md makes the Veyra data the authority and forbids inventing coordinates, so no
  session should silently "fix" either — the data owner decides whether the data or the registry
  point changes.
- **SEC-003** — revoke the old Sketchfab token.
- Power plan off Silent before the final performance sign-off.

---

## E. What this document is not

No performance number here was measured by this session. No screenshot was captured. The frame
timings quoted in §A are the owning sessions' own measurements, repeated with their source so they
can be checked, not independently reproduced.

---

## F. VFX Graph install (2026-11-14, this session)

`com.unity.visualeffectgraph` `17.6.0` added to `Packages/manifest.json`, after `com.unity.ugui`.
JSON validated. `manifest.json` was clean in git at the time of the edit, so nothing was clobbered.

**Why 17.6.0:** the other SRP packages in `packages-lock.json` resolve as
`"source": "builtin"` at `17.6.0` (`com.unity.render-pipelines.core`, `com.unity.shadergraph`).
Builtin packages are versioned with the Editor, so 17.6.0 is the version matching
Unity 6000.6.8f1. It is not a registry pin and should not be bumped independently of the Editor.

**Not yet verified — the next person to open the Editor must confirm:**

1. The package resolves and `packages-lock.json` gains a `com.unity.visualeffectgraph` entry.
   At the time of writing, the MCP bridge reported no Editor instance and there was no
   `Temp/UnityLockfile`, so the project was not open and no resolve could be triggered.
2. The console is clean after the import (VFX Graph pulls in its own shader set; expect a
   one-off import and a domain reload).
3. `VisualEffect` components and `.vfx` assets are creatable.

**Owner note:** the coordinator's table assigns the VFX/Shader Graph install slot to Lighting D1.
This was done out of that order at the user's explicit request. Lighting D1 has been told. If the
resolve conflicts with anything in Lighting's working tree, revert the single manifest line —
nothing else in the project depends on it yet.
