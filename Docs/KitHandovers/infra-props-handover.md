# Road-infrastructure prop set — hand-over (Blender session)

**Status: done.** 18 props, each a `.blend` + a single-LOD-set `.fbx`, in
`E:\VeyraCity\ArtSource\Blender\Infra\` and `…\Infra\export\`. Nothing written to `Assets/`,
nothing committed, no branch switched. Only `ArtSource/Blender/Infra/` was touched.

Full detail (tri table, pivots, collision types, per-prop notes, integration notes):
`ArtSource/Blender/Infra/MANIFEST.md`. Review renders: `…\Infra\review\*.png`.

## For whoever integrates these

- Export preset `VC_Unity_Prop` via `Tools/Blender/vc_tools.py`. Root empty + `*_LOD<n>` children
  per file → one LODGroup. Collision meshes ride in the same FBX (`COL_` / `UCX_` / `UBX_`).
- Facing: prop front = Blender −Y → Unity +Z. Linear modules (kerbs, guardrail, barrier) run from
  their pivot toward **Unity +Z**; the kerb rises toward **Unity +X**; gantry spans and mast arms
  reach toward **Unity +X**.
- Materials are Principled stand-ins (`VC_Concrete_Kerb`, `VC_Steel_Galv`, `VC_Timber_Pole`, …),
  **no image textures** — remap onto the VeyraMaterialLibrary. Every mesh has a 1 m-per-UV-unit
  box projection, so tiling materials land at sane texel density.
- Sign panels are **blank** geometry (no text/symbols/shields) by design.
- Utility poles carry **no cables** — catenary stays procedural in Unity.
- Two deliberate pivot deviations (both hang rather than stand): `VC_SignPanel_Blank_*` and
  `VC_Signal_Head_3` pivot at their TOP centre / mount point, not base centre. Easy to change.

## Rebuild / re-verify (headless, no Unity lock involved)

```
"D:\New folder\blender.exe" -b --factory-startup -P ArtSource/Blender/Infra/build/build_all.py
"D:\New folder\blender.exe" -b --factory-startup -P ArtSource/Blender/Infra/build/qa_check.py
```

`qa_check.py` opens each `.blend` and re-imports each `.fbx`: transforms applied, LOD/collision
naming, 0 n-gons, 0 inverted/open shells, 0 degenerate faces, 0 loose verts, UVs + materials
present, FBX tri counts matching. Last run: **18/18 clean** (`build/qa_report.json`).

## Budgets (BUG-170 relevant)

Kerb module 32 tris (limit 200), guardrail module 184 (limit 800), gantry truss 1760 / 2760 tris
for 12 m / 20 m (limit 4000). Everything is low-material and instanceable.
