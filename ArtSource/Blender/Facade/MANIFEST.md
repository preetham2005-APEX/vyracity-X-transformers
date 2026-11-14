# Veyra City - facade articulation + rooftop clutter kit

Closes gap #5 in `Docs/VISUAL_GAP_ANALYSIS.md` ("Facades are flat"). The 88-model building kit in
`ArtSource/Blender/Buildings/` fixed the massing; this kit is the articulation - real window reveals,
floor banding, tower crowns, ground-floor shopfronts and rooftop clutter.

Built 2026-11-14 by `build/build_facade.py` (re-runnable). **28 models, 2,692 triangles at LOD0, 21 texture files.**
All geometry and all textures original to this project; the benchmark captures were read for
construction logic (shape, proportion, depth) only.

## Priority order

The main session is working the shading side of the same gap (night ambient floor, per-window reveal
shading, floor/column coherence), so it asked for window reveals, floor banding and tower crowns
first, then the shopfront kit, then rooftop clutter - its shading and this geometry meeting in the
middle rather than both faking depth. That is the order below.

Why Part 0 matters at night: the tower set carries its floor lines and window grid in the
**curtain-wall texture** - that is what makes a 240 m tower cost the same as a 28 m one - so there is
no geometry for a lit pane to sit behind, and the facade reads as a flat box with lit rectangles
painted on. These modules put the recess back where it is seen, at a cost paid per module placed
rather than per tower.

## Module grid: 6.0 m bay / 3.0 m half-bay

**Derivation, in one line:** 6.0 m is the ground-floor rhythm already in the building kit - tower
podium colonnade piers are spaced `int(w / 6.0)` (`build_towers.podium`) and market-town shopfront
bays are 6.0 m (`build_coast_town.shopfront`) - so the facade kit inherits it rather than inventing
a round number.

The upper-facade pier pitches in the building kit (4.0 / 4.5 / 5.0 m) are deliberately ignored: the
ground-floor kit sits below the first spandrel band, and the window modules are placed per opening
rather than tiled, so neither needs to align to the structural grid above.

## Pivot convention

> **Deliberate deviation from CLAUDE.md.** CLAUDE.md says "buildings and props at base centre".
> Three of the four anchor kinds below (`wall`, `window`, `band`) instead pivot at the **wall face**,
> and `window` and `band` put z = 0 at the sill line or band soffit rather than at the ground.
> **Why:** these pieces mount onto a facade, and most of them repeat UP it. A base-centre pivot would
> force the placer to compute the mounting height for every instance and to know each piece's depth
> to sit it against the wall. Anchoring at the wall face at the piece's own datum means the placer
> needs one point - a position on the building's front face - and the piece carries the rest.
> Free-standing props (`ground`) keep the CLAUDE.md base-centre convention unchanged. The anchor kind
> is listed per prop in the model tables below, and is also in `report_facade.json` as `anchor`.

| Anchor | Pieces | Origin |
|---|---|---|
| `wall` | shopfront kit, awnings, signs | wall face, centred, **z = 0 at ground** - the piece carries its own mounting height, so one anchor places everything |
| `window` | the three window reveal modules | wall face, centred, **z = 0 at the opening sill** (they repeat up a facade) |
| `band` | slab / cornice / sill bands, crowns, parapet cap | wall face, centred, **z = 0 at the band soffit** |
| `ground` | A-frame, display plinth, all rooftop props | base centre, z = 0 (the normal prop convention) |

Outward is Blender -Y, so after the `VC_Unity_Prop` export every piece faces **Unity +Z** - the same
way the kit buildings' fronts face, so a piece dropped on a building front needs no extra rotation.

## Reveal depth

The glazing on all three window modules sits **0.26 m** behind the wall face. That is a single named
constant, `REVEAL_DEPTH` in `build/build_facade.py`, used by every reveal - one line and one re-run
away from being something else:

```
"D:\New folder\blender.exe" --factory-startup -b -noaudio -P ArtSource/Blender/Facade/build/build_facade.py \
    -- window_reveal_single window_reveal_band window_reveal_storey
```

**Confirmed by the main session**, which owns the night shading. Its reasoning, recorded here so the
number does not read as arbitrary: punched masonry openings sit 0.15-0.25 m back and curtain-wall
glass behind its mullion line more like 0.05-0.10 m, but this reveal has to read at 50-150 m from a
moving car rather than at arm's length, and a physically-perfect 0.12 m would vanish at exactly the
distance where it matters. 0.26 m is deliberately at the generous end.

`REVEAL_DEPTH_BAND` is a second constant, currently equal to `REVEAL_DEPTH`. The 3 m ribbon module
may later go to 0.18-0.20 m, since ribbon glazing really is shallower; that is optional polish, not a
blocker, and it is one line plus one model re-run.

### Division of labour with the facade shader - do not "fix" this

**This geometry owns NEAR depth; `VC_FacadeSurface.hlsl` owns FAR depth.** The shader already fades
its interior raycast out between 40 m and 240 m, and its new per-window reveal shading is weighted
the opposite way: near zero up close, where these modules cast a real shadow, ramping up as they LOD
out. Full-strength fake reveal up close would stack two head shadows on one window and look worse
than either alone.

So this geometry is built **as if the shader does nothing** - confirmed by the session writing the
shader. No sill is softened, no head shadow is pre-compensated, nothing here assumes something else
will darken it. A future reader should not "correct" that.

## Models

### Part 0 - window reveals, floor banding and tower crowns (priority)

| Model | Size W x D x H (m) | LOD0 | LOD1 | LOD2 | Anchor | Collision | What it is |
|---|---|---|---|---|---|---|---|
| `VC_Fac_CorniceBand` | 6.00 x 0.34 x 0.66 | 36 | 36 | 24 | band | - | 6 m profiled cornice |
| `VC_Fac_CrownFin` | 0.86 x 0.58 x 3.58 | 48 | 48 | 24 | band | - | vertical crown fin with a BLANK lit reveal |
| `VC_Fac_CrownSetback` | 6.00 x 0.96 x 2.30 | 72 | 72 | 48 | band | - | 6 m tower crown: parapet, setback step, coping, BLANK light cove |
| `VC_Fac_SillBand` | 6.00 x 0.19 x 0.16 | 24 | 24 | 12 | band | - | 6 m string course / continuous sill |
| `VC_Fac_SlabBand` | 6.00 x 0.27 x 0.42 | 36 | 36 | 12 | band | - | 6 m floor-line slab edge with a drip |
| `VC_Fac_WindowRevealBand` | 3.10 x 0.34 x 1.45 | 132 | 120 | 84 | window | - | 3.0 m ribbon window, four lights, 0.24 m reveal |
| `VC_Fac_WindowRevealSingle` | 1.60 x 0.34 x 1.60 | 108 | 96 | - | window | - | 1.5 x 1.6 m punched window, 0.26 m reveal, recessed glazing |
| `VC_Fac_WindowRevealStorey` | 1.60 x 0.34 x 3.30 | 120 | 108 | 84 | window | - | 1.5 x 3.3 m storey-height bay with a transom, 0.30 m reveal |

Part subtotal at LOD0: **576 triangles** across 8 models.

### Part 1 - ground-floor / shopfront kit

| Model | Size W x D x H (m) | LOD0 | LOD1 | LOD2 | Anchor | Collision | What it is |
|---|---|---|---|---|---|---|---|
| `VC_Fac_AFrame` | 0.74 x 0.56 x 1.09 | 72 | 72 | - | ground | UBX_VC_Fac_AFrame | pavement A-frame board, BLANK both faces |
| `VC_Fac_AwningBox` | 3.08 x 1.17 x 0.80 | 60 | 60 | 24 | wall | - | rigid box awning |
| `VC_Fac_AwningFabric` | 3.00 x 1.47 x 0.96 | 72 | 68 | 40 | wall | - | fabric awning with valance and folding arms |
| `VC_Fac_DisplayPlinth` | 1.72 x 0.92 x 0.85 | 48 | 48 | - | ground | UBX_VC_Fac_DisplayPlinth | shop display plinth |
| `VC_Fac_EntryRecess` | 3.00 x 1.15 x 3.46 | 132 | 132 | 96 | wall | UBX_VC_Fac_EntryRecess | recessed entry, 1.05 m deep, glazed door pair and transom |
| `VC_Fac_GrilleClosed` | 3.00 x 0.30 x 3.39 | 168 | 108 | 48 | wall | UBX_VC_Fac_GrilleClosed | roll-up security grille, shut, in its guide rails |
| `VC_Fac_GrilleOpen` | 3.00 x 0.40 x 3.31 | 84 | 76 | 64 | wall | UBX_VC_Fac_GrilleOpen | the same grille rolled up on its drum |
| `VC_Fac_Shopfront3m` | 3.00 x 0.22 x 3.46 | 108 | 96 | 84 | wall | UBX_VC_Fac_Shopfront3m | 3 m shopfront: stall riser, mullion, transom, head |
| `VC_Fac_Shopfront6m` | 6.00 x 0.22 x 3.46 | 132 | 120 | 84 | wall | UBX_VC_Fac_Shopfront6m | 6 m shopfront, three mullions |
| `VC_Fac_SignFascia` | 3.00 x 0.20 x 0.74 | 48 | 48 | 24 | wall | - | flush fascia sign panel, BLANK |
| `VC_Fac_SignProjecting` | 0.20 x 1.41 x 0.95 | 84 | 84 | 72 | wall | - | wall-mounted projecting sign box, BLANK both faces |

Part subtotal at LOD0: **1008 triangles** across 11 models.

### Part 2 - rooftop clutter

| Model | Size W x D x H (m) | LOD0 | LOD1 | LOD2 | Anchor | Collision | What it is |
|---|---|---|---|---|---|---|---|
| `VC_Roof_AntennaCluster` | 1.10 x 0.63 x 3.60 | 132 | 108 | 56 | ground | - | mast, cross arms, whips and a drum |
| `VC_Roof_DuctRun` | 0.72 x 4.00 x 1.38 | 84 | 84 | 72 | ground | UBX_VC_Roof_DuctRun | 4 m insulated duct run with flanges and sleepers |
| `VC_Roof_FanCowl` | 1.00 x 1.00 x 1.04 | 188 | 156 | - | ground | UBX_VC_Roof_FanCowl | extract fan cowl on an upstand kerb |
| `VC_Roof_HvacLarge` | 2.76 x 1.76 x 2.06 | 132 | 116 | 80 | ground | UBX_VC_Roof_HvacLarge | large packaged AC unit, twin fans, louvre bank |
| `VC_Roof_HvacSmall` | 1.35 x 1.10 x 0.94 | 84 | 76 | - | ground | UBX_VC_Roof_HvacSmall | small packaged AC unit on sleeper rails |
| `VC_Roof_ParapetCap` | 3.00 x 0.60 x 0.44 | 40 | 40 | 28 | band | - | 3 m parapet coping module |
| `VC_Roof_RoofHatch` | 1.16 x 1.04 x 1.05 | 48 | 48 | - | ground | - |  |
| `VC_Roof_StairOverrun` | 3.36 x 2.76 x 2.69 | 48 | 48 | 24 | ground | UBX_VC_Roof_StairOverrun | stair / lift overrun box with coping |
| `VC_Roof_WaterTank` | 2.48 x 2.20 x 3.76 | 352 | 236 | 116 | ground | UCX_VC_Roof_WaterTank | tank on a four-leg stand with a ladder |

Part subtotal at LOD0: **1108 triangles** across 9 models.

Whole kit at LOD0: **2692 triangles** across 28 models (mean 96, largest 352).

LOD policy follows CLAUDE.md: props under 2 m ship **LOD0 + LOD1**, larger meshes get three levels.
Every LOD is authored - the same description re-run at a lower detail level - never decimated; a
collapse decimate shreds this kind of box geometry (proved on the building kit, where the decimated
LOD2s rendered as see-through shells).

## Materials and textures

**One shared set for all 28 props** - 7 materials, 21 files - because every prop here instances and
BUG-170 is open. No prop has a texture of its own.

| Material | Use |
|---|---|
| `VC_Fac_Metal` | mullions, frames, grilles, brackets, ducting, antennas |
| `VC_Fac_Glass` | shopfront and window glazing |
| `VC_Fac_Panel` | painted panel: stall risers, sign bodies, awning boxes, plant casings |
| `VC_Fac_Fabric` | awning canvas |
| `VC_Fac_Blank` | **BLANK** sign faces - no text, logo or symbol anywhere in this kit |
| `VC_Fac_Concrete` | sills, copings, cornices, plinths, upstands |
| `VC_Fac_Emissive` | **BLANK** crown light coves and lit reveals, HDR emission |

Packing follows CLAUDE.md's URP Lit rule (**not** the glTF packing the building kit uses, because
those go through `KitbashMaterials` while these go through the VeyraMaterialLibrary):

```
<mat>_BC.png   sRGB albedo
<mat>_N.png    OpenGL tangent-space normal, +Y up, Non-Color
<mat>_MS.png   R = metallic, A = smoothness (1 - roughness), Non-Color
```

Unity: Smoothness Source = **Metallic Alpha**, `_N` as Normal Map, `_MS` with sRGB off. 512 x 512,
power of two, seamless; one tile covers 1.0 m, matching the 1 m box projection the meshes carry (the
same convention as the Infra set).

`VC_Fac_Emissive` is toggled at runtime, so CLAUDE.md's keyword rule applies: it must be covered by a
material in the build or by `Assets/_Veyra/Art/Shaders/VC_Variants.shadervariants`.

## LOD crossover distances (what the shader fade has to match)

This kit authors **no LOD distances**. The FBX carries only the `<name>_LOD<n>` naming; Unity's
importer builds the LODGroup and owns the thresholds, so the crossover is set on the Unity side, not
here. It is therefore **screen-size driven, not metres** - which is why the table below converts.

Unity's screen-relative transition height for a vertical FOV is
`h = size / (2 * d * tan(fov / 2))`, where `size` is the LODGroup size (the largest axis of the
renderer bounds), so `d = size / (2 * h * tan(fov / 2))`. Veyra's scene camera is **60 deg vertical**
(`VeyraCity.unity`); `ChaseCamera` moves between about 55 and 70 with speed, which shifts these
distances by roughly -10 % / +21 %.

Distance in metres at which a module reaches a given threshold, at 1440p and FOV 60:

| Module (LODGroup size) | h = 0.5 | 0.25 | 0.10 | 0.05 | 0.035 | 0.02 | 0.01 |
|---|---|---|---|---|---|---|---|
| `VC_Fac_CorniceBand` (6.00 m) | 10.4 | 20.8 | 52.0 | 103.9 | 148.5 | 259.8 | 519.6 |
| `VC_Fac_CrownFin` (3.58 m) | 6.2 | 12.4 | 31.0 | 62.0 | 88.6 | 155.0 | 310.0 |
| `VC_Fac_WindowRevealBand` (3.10 m) | 5.4 | 10.7 | 26.8 | 53.7 | 76.7 | 134.2 | 268.5 |
| `VC_Fac_WindowRevealSingle` (1.60 m) | 2.8 | 5.5 | 13.9 | 27.7 | 39.6 | 69.3 | 138.6 |
| `VC_Fac_WindowRevealStorey` (3.30 m) | 5.7 | 11.4 | 28.6 | 57.2 | 81.7 | 142.9 | 285.8 |

**The project already fixes this threshold.** `Assets/_Veyra/Tests/EditMode/ChunkImportTests.cs`
fails any imported model with **3 or more** LOD levels unless LOD0's `screenRelativeTransitionHeight`
is exactly **0.35** - so the column to read is not 0.5 but 0.35, which at FOV 60 is `size / 0.404`
metres. Two things follow, both verified by reading the test rather than assumed:

- the gate applies at 3 or more LOD levels, and CLAUDE.md gives props under 2 m only LOD0 + LOD1, so
  **exactly one module is exempt**: `VC_Fac_WindowRevealSingle` at 1.60 m, whose threshold is
  whatever the importer sets and is free to tune. Every other Part 0 module carries 3 LODs and is
  bound, which puts LOD0 much closer than the shader fade starts:

  - `VC_Fac_CorniceBand` (6.00 m, 3 LODs): LOD0 to **14.8 m**
  - `VC_Fac_CrownFin` (3.58 m, 3 LODs): LOD0 to **8.9 m**
  - `VC_Fac_CrownSetback` (6.00 m, 3 LODs): LOD0 to **14.8 m**
  - `VC_Fac_SillBand` (6.00 m, 3 LODs): LOD0 to **14.8 m**
  - `VC_Fac_SlabBand` (6.00 m, 3 LODs): LOD0 to **14.8 m**
  - `VC_Fac_WindowRevealBand` (3.10 m, 3 LODs): LOD0 to **7.7 m**
  - `VC_Fac_WindowRevealSingle` (1.60 m, 2 LODs): exempt
  - `VC_Fac_WindowRevealStorey` (3.30 m, 3 LODs): LOD0 to **8.2 m**

- holding a 1.6 m module at LOD0 out to 40 m still needs about **0.035**, which no default produces.
  It has to be set deliberately.

**But LOD1 is not where the reveal dies.** On these modules LOD1 drops sills, mullions and transom
bars and keeps the head, jambs, sill and recessed glass - the recess survives. Modules under 2 m
have no LOD2 at all (CLAUDE.md). So the distance at which real depth actually disappears is the
**cull** distance, not the first LOD step, and that is the number a shader ramp should key to.

## If these are merged into a building at build time

The Unity session is merging the Part 0 modules into each building's LOD0 mesh in the editor rather
than instancing them at runtime - static, chunked, no runtime spawn, which is what CLAUDE.md wants
for large static geometry and which avoids the per-window instance churn entirely. Nothing in this
kit changes for that; the pieces, the 0.26 m reveal and the anchors are the same either way. Two
things do change on the Unity side, and both are worth having written down.

### 1. The triangle arithmetic moves from spawn cost to mesh cost

A punched reveal module is **108 triangles** at LOD0 (counted from the box list in
`build_facade._reveal`: head, sill, two jambs, glazing, two frame bars, a mullion and a sill nosing;
the ribbon is 132 and the storey bay 120). That is cheap per module and expensive per tower:

| Merge envelope | Modules | Tris per tower | 100 towers |
|---|---|---|---|
| 20 floors x 30 bays x 4 faces | 2,400 | **259,200** | 25,920,000 |
| street face only, all 20 floors | 600 | 64,800 | 6,480,000 |
| street face, lower 6 floors | 180 | 19,440 | 1,944,000 |
| street face, lower 3 floors | 90 | 9,720 | 972,000 |

For scale: the tallest tower in the building kit is **1,348 triangles for the whole building**, and
CLAUDE.md caps a single mesh at 300,000. Merging reveals onto every bay of every face puts one tower
at 259,200 - inside the cap, but about 190x the building it is merged into, and 26 M triangles across
a hundred towers in view. That is no longer a spawn problem, it is a vertex and memory one, against a
GPU budget that measured 9.1-9.9 ms p95 with the plain boxes.

The hierarchy that keeps this affordable is the one the skyline already has: **reveals where the
player is, bands where they are not.** A 6 m band module is 36 triangles, so a floor line costs about
330 triangles per 55 m face - an order of magnitude less per metre of facade than per-window reveals.
Street-facing faces and the lower few floors get real reveals; everything above gets banding and the
curtain-wall texture it already has. The merge envelope is the Unity session's call; this table is
here so the number is chosen rather than discovered.

### 2. Material equivalence - a merged building should gain one submesh, not seven

A merged mesh carries the union of both material sets, so merging naively would give a tower both
`VC_Twr_Glass` and `VC_Fac_Glass` - two draw calls for the same visual material. Three of the seven
facade slots have an exact equivalent in every host kit and should be remapped at merge time:

| Facade slot | Remap to, per host kit | |
|---|---|---|
| `VC_Fac_Metal` | `VC_Twr_Metal` / `VC_Nm_Metal` / `VC_Mr_Metal` / `VC_In_Metal` / `VC_Mt_Trim` | exact |
| `VC_Fac_Glass` | `VC_Twr_Glass` / `VC_Nm_Glass` / `VC_Mr_Glass` / `VC_Cs_Glass` / `VC_Mt_Glass` | exact |
| `VC_Fac_Concrete` | `VC_Twr_Concrete` / `VC_Nm_Concrete` / `VC_Mr_Concrete` / `VC_In_Concrete` | exact |
| `VC_Fac_Panel` | no host equivalent - **keep** | the one new slot a merged tower gains |
| `VC_Fac_Fabric` | shopfront kit only, never merged into a tower | - |
| `VC_Fac_Blank` | shopfront/sign kit only | - |
| `VC_Fac_Emissive` | crowns only; keep, it is runtime-toggled | - |

So a tower merged with reveals and bands gains exactly **one** extra submesh (`VC_Fac_Panel`), and
crowns add `VC_Fac_Emissive`. Without the remap it gains four to seven.

### 3. Collision on merge

The Part 0 modules carry **no colliders** by design, so merging them changes nothing about physics -
every building keeps its own data-footprint collider. The Part 1 shopfront pieces do carry `UBX_`
colliders (they are at ground level where a car can reach them); if any of those are merged rather
than placed, drop the `UBX_` meshes in the merge rather than baking them into the render mesh.

### 4. What the shader ramp keys to, under the merge

Merging moves the answer from a per-module cull to the **building's** LOD0 -> LOD1 transition, since
the building's LOD1 will not carry the merged reveals. That is one number per building size, driven
by the building's bounds rather than the module's, and the Unity session controls it directly - so
the distance table above describes the modules as standalone props and is superseded for merged use.
It still holds for anything placed as a prop (the shopfront and rooftop kits).

## Self-check

`build/qa_check.py` re-imports every exported FBX and measures it: **28 files, 0 problems**.

It checks: one EMPTY root; `<name>_LOD<n>` children contiguous from 0; LOD count against the
CLAUDE.md 2 m rule; applied transforms; 0 n-gons, 0 inverted faces at LOD0, 0 degenerate faces,
0 loose vertices; UVs on every LOD; material slots drawn only from the shared 7; the LOD0
triangle budget; the pivot (anchor at z = 0 and centred on x); collision naming; and that a
wall-anchored piece projects toward the street rather than behind the wall face.

LOD normals are gated by a render, not a metric: `build/review.py` renders every LOD
**backface-culled against a red world** (`review/lod<n>_backface_check.png`), so a flipped or missing
triangle shows as a hole. Both numeric inverted-face tests are unreliable on this geometry - that was
established on the building kit, where a decimated LOD2 every metric called clean rendered hollow.

## Rebuild / re-verify (headless, no Unity involved)

```
"D:\New folder\blender.exe" --factory-startup -b -noaudio -P ArtSource/Blender/Facade/build/make_textures.py
"D:\New folder\blender.exe" --factory-startup -b -noaudio -P ArtSource/Blender/Facade/build/build_facade.py -- all
"D:\New folder\blender.exe" --factory-startup -b -noaudio -P ArtSource/Blender/Facade/build/qa_check.py
"D:\New folder\blender.exe" --factory-startup -b -noaudio -P ArtSource/Blender/Facade/build/review.py
python ArtSource/Blender/Facade/build/make_manifest.py
```

`build/build_facade.py` imports `vc_bld_lib` from `ArtSource/Blender/Buildings/build/` (same author,
same session) for the shared bmesh helpers. That is the only dependency outside this folder.

## For whoever wires this up

- Nothing was written to `Assets/`, Unity was never run, nothing committed, no branch switched.
- `Assets/_Veyra/AI/RooftopDetails.cs` already exists and places roof props at runtime. The Part 2
  models are authored to be consumed by it (or by the main session) - base-centre pivots, z = 0, one
  shared material set - but nothing in `Assets/` was touched, so the wiring is still to do.
- Every sign face in this kit is blank geometry on its own material slot. Veyra's own artwork is
  applied in Unity.
