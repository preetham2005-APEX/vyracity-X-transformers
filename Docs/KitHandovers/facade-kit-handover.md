# Facade articulation + rooftop clutter kit — hand-over (Blender session)

**Status: done.** 28 models, `.blend` + single-LOD-set `.fbx`, in `E:\VeyraCity\ArtSource\Blender\Facade\`
and `…\Facade\export\`. 21 texture files, QA 28/28 clean. Nothing in `Assets/`, Unity never run,
nothing committed, no branch switched. Closes gap #5 in `Docs/VISUAL_GAP_ANALYSIS.md`.

Full detail: `ArtSource/Blender/Facade/MANIFEST.md`. Review renders: `…\Facade\review\*.png`.

## What it is

| Part | Models | LOD0 tris | What |
|---|---|---|---|
| 0 (priority) | 8 | 576 | 3 window reveal modules, 3 floor/cornice/sill bands, 2 tower crowns |
| 1 | 11 | 1,012 | recessed entry, 3 m and 6 m shopfronts, grille open + closed, 2 awnings, projecting + fascia signs, A-frame, display plinth |
| 2 | 9 | 1,108 | 2 HVAC units, fan cowl, duct run, water tank, stair overrun, parapet cap, antenna cluster, roof hatch |

**2,696 triangles at LOD0 for the whole kit**, largest single model 352 (budget 400). One shared
material set — 7 materials, 21 textures — no prop has a texture of its own.

## The numbers the Unity session was waiting for

It is matching its night-shader reveal fade to wherever this geometry drops. Measured sizes against the
project's own `ChunkImportTests` rule (LOD0 `screenRelativeTransitionHeight` pinned at **0.35** for any
model with 3+ LODs), at FOV 60:

| Module | size | LODs | LOD0 holds to |
|---|---|---|---|
| `VC_Fac_WindowRevealSingle` | 1.60 m | 2 | **exempt** — threshold free to tune |
| `VC_Fac_WindowRevealBand` | 3.10 m | 3 | 7.7 m |
| `VC_Fac_WindowRevealStorey` | 3.30 m | 3 | 8.2 m |
| `VC_Fac_CrownFin` | 3.58 m | 3 | 8.9 m |
| slab / sill / cornice bands, `CrownSetback` | 6.00 m | 3 | 14.8 m |

**LOD1 is not where the reveal dies.** LOD1 keeps head, jambs, sill and recessed glass and only drops
sills, mullions and transom bars — the recess survives. So the handover number for the shader ramp is the
**cull** distance, not the first LOD step.

## Decisions baked in (all agreed before the build)

- **6.0 m bay / 3.0 m half-bay**, derived from the building kit's own ground-floor rhythm
  (`int(w/6.0)` tower podium piers, 6.0 m market-town shopfront bays) rather than picked.
- **Four anchor kinds**, a deliberate CLAUDE.md deviation ("props at base centre") with the reason
  written into the MANIFEST: `wall` (wall face, z=0 at ground — the piece carries its own mounting
  height), `window` (z=0 at the opening sill), `band` (z=0 at the band soffit), `ground` (base centre).
  Outward is Blender −Y → Unity +Z, same as the kit buildings' fronts.
- **Reveal depth 0.26 m**, one named constant `REVEAL_DEPTH`; `REVEAL_DEPTH_BAND` is a second constant
  (currently equal) for the optional 0.18–0.20 m ribbon polish. One line and one model re-run.
- **Built as if the shader does nothing.** No softened sills, no pre-compensated head shadows. The
  MANIFEST says so with a do-not-fix note, because the shader is deliberately weighted near-zero up
  close and someone will otherwise "correct" a sill that looks under-shadowed.
- LOD0+LOD1 for props under 2 m per CLAUDE.md, enforced per-prop by size in the QA script.
- All signage is **blank** geometry on its own slot. `VC_Fac_Emissive` (crown light coves) is blank too
  and is runtime-toggled, so it needs `VC_Variants.shadervariants` coverage.

## Merge contract (agreed with the Unity session)

Reveals go into their **own chunked mesh with their own LODGroup**, not merged into the tower mesh — a
245 m tower holds LOD0 to 606 m under the 0.35 rule, so anything merged into it pays full reveal
triangles for half a kilometre. Envelope: reveals on street-facing faces, lower 6 floors; bands full
height; nothing above floor 6 (at 200–800 m a 0.26 m recess is sub-pixel — that range is the shader's).

On merge, remap `VC_Fac_Metal` / `_Glass` / `_Concrete` to the host kit's equivalents so a merged tower
gains **one** extra submesh (`VC_Fac_Panel`) rather than seven. Anything merged into a render mesh drops
its collision meshes; shopfront pieces that need collision get placed, not merged.

## QA

`build/qa_check.py` re-imports every FBX: **28 files, 0 problems** — one EMPTY root, `_LOD<n>` contiguous
from 0, LOD count against the 2 m rule, applied transforms, 0 n-gons, 0 inverted faces at LOD0, 0
degenerate, 0 loose verts, UVs everywhere, slots only from the shared 7, LOD0 tri budget, anchor at its
own datum, collision naming, and that a wall-anchored piece projects toward the street.

LOD normals are gated by a **render**, not a metric: `review/lod<n>_backface_check.png` renders every LOD
backface-culled against red, so a flipped or missing triangle shows as a hole. LOD0/1/2 all solid.

The first QA run found 11 problems: 2 real model bugs (a sill nosing 5 mm below the sill line; a repeated
profile point making 3 zero-area faces in the fabric awning) and 9 instances of two of **my own QA rules
being wrong** — "bbox min z must be 0" and "must be centred on x" applied to every anchor kind, when a
wall-anchored awning legitimately starts at 3.3 m and a water tank's ladder legitimately sits off-centre.
Both model bugs fixed, both rules scoped per anchor kind.

## Street set re-export, same session

`ArtSource/Blender/Street/` re-exported with the merged material names:
`VC_Metal_Galv→VC_Steel_Galv`, `VC_Metal_Dark→VC_Steel_Dark`, `VC_Metal_CastIron→**VC_Iron_Painted**`
(painted castings — *not* `VC_Iron_Cast`, which is bare iron at metallic 1.0 and would hang a near-black
mirror on an ornate lamp standard in wet night reflections). QA 16/16 clean, renamed slots confirmed
present in the FBX. `VC_Metal_Garage` in the building kit stays unchanged — settled as a standing rule
that per-set naming schemes may coexist.

## Rebuild / re-verify (headless, no Unity involved)

```
"D:\New folder\blender.exe" --factory-startup -b -noaudio -P ArtSource/Blender/Facade/build/make_textures.py
"D:\New folder\blender.exe" --factory-startup -b -noaudio -P ArtSource/Blender/Facade/build/build_facade.py -- all
"D:\New folder\blender.exe" --factory-startup -b -noaudio -P ArtSource/Blender/Facade/build/qa_check.py
"D:\New folder\blender.exe" --factory-startup -b -noaudio -P ArtSource/Blender/Facade/build/review.py
python ArtSource/Blender/Facade/build/make_manifest.py
```

`build_facade.py` imports `vc_bld_lib` from `ArtSource/Blender/Buildings/build/` for shared bmesh
helpers — the only dependency outside this folder.

## Still to do, Unity side

- `Assets/_Veyra/AI/RooftopDetails.cs` already places roof props at runtime; the Part 2 models are
  authored to be consumed by it (base-centre pivots, z=0, one shared material set) but nothing in
  `Assets/` was touched, so the wiring is open.
- The 7 `VC_Fac_*` names need VeyraMaterialLibrary slots, on top of the building kit's 68.
- Veyra's own artwork still has to be applied to every blank sign face.
