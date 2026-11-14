# Veyra City - road surface wear (Layer 2)

Decal atlases and flush road-surface meshes. Everything is original: the textures are generated
from seeded numpy noise by `build/vc_tex.py` - no photographs, no traced or sampled imagery.
The reference captures (`ReferenceImages/08_GTAV_VideoCapture_2026-11-12/` VC_04, VC_15, VC_21,
VC_24) were read only for WHERE wear accumulates and WHAT SHAPE it takes; no colour, black level,
contrast or saturation value is taken from them, because they are compressed video captures.

* Rebuild textures: `"D:\New folder\blender.exe" -b --factory-startup -P build/build_textures.py`
  (plain Python 3 + numpy also works - the texture path never touches bpy)
* Rebuild meshes:   `... -P build/build_meshes.py`
* Self-check:       `... -P build/qa_check.py` - **15/15 pass with 0 issues**
  (`build/qa_report.json`)
* Previews: `review/*_preview.png` (each decal composited over flat asphalt and lit by its own
  normal map), `review/_contact_sheet.png`, `review/VC_*.png` (mesh 3/4 orthos).

## 1. Decal atlases

Each atlas is a 2 x 2 grid of decals. Three maps per atlas:

| File | Format | Channels |
|---|---|---|
| `<atlas>_BC.png` | RGBA, **sRGB** | RGB = albedo the decal paints, A = decal coverage |
| `<atlas>_N.png` | RGB, **raw / Normal Map** | tangent-space normal, OpenGL (+Y up) |
| `<atlas>_MS.png` | RGBA, **raw** | R = metallic (0 everywhere), G = AO, B = height, A = **smoothness** |

* **URP Lit packing as the project requires it:** smoothness (= 1 - roughness) lives in the
  alpha of the metallic map, so the material must use Smoothness Source = **Metallic Alpha** and
  `_MS` goes in the Metallic slot. `_N` must be imported with Texture Type = Normal Map; `_MS`
  with sRGB **off**.
* **Height channel (MS.b)**: 0.5 = the road plane, full range +/- 20 mm. It is there for
  parallax/blend-height use; the normal map already carries the same relief.
* **Border alpha**: every cell fades to alpha 0 over the outer 7 % of the cell, so a decal
  blends into asphalt instead of showing a rectangle. The QA script fails if any cell border
  is not exactly 0.
* **Smoothness** is the decal's own target value (dry Veyra asphalt reference = 0.25): rubber
  and polished wheel paths go up, crack interiors and grime go down, fresh fluid goes to ~0.8.
* All sizes are power-of-two. 8-bit PNG sources; compress to BC7 (PC) / ASTC 6x6 (Android) on
  import, and BC5 for the normal maps.

### Atlas table

| Atlas | Size | Cell | Cell world size | Cells (UV rect x, y, w, h) | Source bytes |
|---|---|---|---|---|---|
| `T_RW_TyreTrack` | 2048 x 2048 | 1024 px | 4.00 m across x 4.00 m along | `twin_track_straight` (0.0, 0.5, 0.5, 0.5)<br>`braking_scuff` (0.5, 0.5, 0.5, 0.5)<br>`turn_scuff` (0.0, 0.0, 0.5, 0.5)<br>`junction_polish` (0.5, 0.0, 0.5, 0.5) | 1.23 MB |
| `T_RW_Crack` | 2048 x 2048 | 1024 px | 3.00 m across x 3.00 m along | `alligator_dense` (0.0, 0.5, 0.5, 0.5)<br>`alligator_sparse` (0.5, 0.5, 0.5, 0.5)<br>`longitudinal_branching` (0.0, 0.0, 0.5, 0.5)<br>`sealed_crack` (0.5, 0.0, 0.5, 0.5) | 2.54 MB |
| `T_RW_LaneWear` | 2048 x 2048 | 1024 px | 1.00 m across x 4.00 m along | `white_edge_line_worn` (0.0, 0.5, 0.5, 0.5)<br>`white_lane_dash_scuffed` (0.5, 0.5, 0.5, 0.5)<br>`yellow_double_centre_worn` (0.0, 0.0, 0.5, 0.5)<br>`white_heavy_worn` (0.5, 0.0, 0.5, 0.5) | 1.06 MB |
| `T_RW_Oil` | 1024 x 1024 | 512 px | 2.00 m across x 2.00 m along | `pooled_stain` (0.0, 0.5, 0.5, 0.5)<br>`drip_cluster` (0.5, 0.5, 0.5, 0.5)<br>`smeared_trail` (0.0, 0.0, 0.5, 0.5)<br>`fresh_spill` (0.5, 0.0, 0.5, 0.5) | 0.45 MB |
| `T_RW_Patch` | 1024 x 1024 | 512 px | 3.00 m across x 3.00 m along | `rectangular_patch` (0.0, 0.5, 0.5, 0.5)<br>`trench_patch` (0.5, 0.5, 0.5, 0.5)<br>`pothole_fill` (0.0, 0.0, 0.5, 0.5)<br>`skin_seal` (0.5, 0.0, 0.5, 0.5) | 0.84 MB |
| `T_RW_Grime` | 1024 x 1024 | 512 px | 2.00 m across x 2.00 m along | `kerb_gradient` (0.0, 0.5, 0.5, 0.5)<br>`gutter_silt` (0.5, 0.5, 0.5, 0.5)<br>`shade_blotch` (0.0, 0.0, 0.5, 0.5)<br>`debris_line` (0.5, 0.0, 0.5, 0.5) | 0.68 MB |
| `T_RW_Markings` | 2048 x 2048 | 1024 px | 4.00 m across x 4.00 m along | `crosswalk_block` (0.0, 0.5, 0.5, 0.5)<br>`arrow_straight` (0.5, 0.5, 0.5, 0.5)<br>`arrow_turn_right` (0.0, 0.0, 0.5, 0.5)<br>`stop_bar` (0.5, 0.0, 0.5, 0.5) | 0.96 MB |
| `T_RW_Puddle` | 2048 x 2048 | 1024 px | 4.00 m across x 4.00 m along | `sheet_puddle` (0.0, 0.5, 0.5, 0.5)<br>`gutter_puddle` (0.5, 0.5, 0.5, 0.5)<br>`drying_ring` (0.0, 0.0, 0.5, 0.5)<br>`damp_patch` (0.5, 0.0, 0.5, 0.5) | 1.55 MB |
| `T_RW_Contact` | 1024 x 1024 | 512 px | 2.00 m across x 2.00 m along | `prop_base_ring` (0.0, 0.5, 0.5, 0.5)<br>`footway_kerb_line` (0.5, 0.5, 0.5, 0.5)<br>`wall_base_splash` (0.0, 0.0, 0.5, 0.5)<br>`drip_run` (0.5, 0.0, 0.5, 0.5) | 0.41 MB |

### Per-decal ranges

| Atlas | Cell | Peak alpha | Relief (mm) | Smoothness range |
|---|---|---|---|---|
| `T_RW_TyreTrack` | `twin_track_straight` | 0.78 | -0.8 .. -0.0 | 0.25 .. 0.40 |
| `T_RW_TyreTrack` | `braking_scuff` | 0.71 | +0.0 .. +0.3 | 0.25 .. 0.32 |
| `T_RW_TyreTrack` | `turn_scuff` | 0.63 | +0.0 .. +0.0 | 0.25 .. 0.37 |
| `T_RW_TyreTrack` | `junction_polish` | 0.46 | +0.0 .. +0.0 | 0.26 .. 0.41 |
| `T_RW_Crack` | `alligator_dense` | 0.96 | -9.6 .. -0.0 | 0.12 .. 0.25 |
| `T_RW_Crack` | `alligator_sparse` | 0.90 | -7.2 .. -0.0 | 0.14 .. 0.25 |
| `T_RW_Crack` | `longitudinal_branching` | 0.95 | -12.3 .. -0.0 | 0.11 .. 0.25 |
| `T_RW_Crack` | `sealed_crack` | 0.96 | +0.0 .. +4.0 | 0.25 .. 0.54 |
| `T_RW_LaneWear` | `white_edge_line_worn` | 1.00 | +0.0 .. +1.5 | 0.25 .. 0.37 |
| `T_RW_LaneWear` | `white_lane_dash_scuffed` | 1.00 | +0.0 .. +1.5 | 0.25 .. 0.35 |
| `T_RW_LaneWear` | `yellow_double_centre_worn` | 1.00 | +0.0 .. +1.5 | 0.25 .. 0.35 |
| `T_RW_LaneWear` | `white_heavy_worn` | 0.96 | +0.0 .. +1.0 | 0.25 .. 0.31 |
| `T_RW_Oil` | `pooled_stain` | 0.99 | -0.4 .. -0.0 | 0.17 .. 0.66 |
| `T_RW_Oil` | `drip_cluster` | 1.00 | +0.0 .. +0.0 | 0.25 .. 0.63 |
| `T_RW_Oil` | `smeared_trail` | 0.85 | +0.0 .. +0.0 | 0.25 .. 0.58 |
| `T_RW_Oil` | `fresh_spill` | 1.00 | -0.6 .. -0.0 | 0.25 .. 0.83 |
| `T_RW_Patch` | `rectangular_patch` | 1.00 | +0.0 .. +5.7 | 0.25 .. 0.51 |
| `T_RW_Patch` | `trench_patch` | 1.00 | -3.0 .. +2.5 | 0.25 .. 0.43 |
| `T_RW_Patch` | `pothole_fill` | 1.00 | +0.0 .. +5.6 | 0.19 .. 0.41 |
| `T_RW_Patch` | `skin_seal` | 1.00 | +0.0 .. +4.7 | 0.25 .. 0.58 |
| `T_RW_Grime` | `kerb_gradient` | 0.76 | +0.0 .. +0.0 | 0.15 .. 0.25 |
| `T_RW_Grime` | `gutter_silt` | 1.00 | +0.0 .. +1.5 | 0.11 .. 0.25 |
| `T_RW_Grime` | `shade_blotch` | 1.00 | +0.0 .. +0.0 | 0.12 .. 0.25 |
| `T_RW_Grime` | `debris_line` | 1.00 | +0.0 .. +2.2 | 0.15 .. 0.25 |
| `T_RW_Markings` | `crosswalk_block` | 1.00 | +0.0 .. +1.5 | 0.25 .. 0.37 |
| `T_RW_Markings` | `arrow_straight` | 1.00 | +0.0 .. +1.5 | 0.25 .. 0.37 |
| `T_RW_Markings` | `arrow_turn_right` | 1.00 | +0.0 .. +1.5 | 0.25 .. 0.37 |
| `T_RW_Markings` | `stop_bar` | 1.00 | +0.0 .. +1.5 | 0.25 .. 0.37 |
| `T_RW_Puddle` | `sheet_puddle` | 1.00 | -0.3 .. +0.2 | 0.25 .. 0.96 |
| `T_RW_Puddle` | `gutter_puddle` | 0.95 | -0.2 .. +0.3 | 0.25 .. 0.85 |
| `T_RW_Puddle` | `drying_ring` | 0.99 | -0.1 .. +0.2 | 0.25 .. 0.81 |
| `T_RW_Puddle` | `damp_patch` | 0.72 | +0.0 .. +0.0 | 0.25 .. 0.54 |
| `T_RW_Contact` | `prop_base_ring` | 1.00 | +0.0 .. +1.4 | 0.14 .. 0.25 |
| `T_RW_Contact` | `footway_kerb_line` | 0.96 | -1.2 .. +0.3 | 0.12 .. 0.20 |
| `T_RW_Contact` | `wall_base_splash` | 1.00 | +0.0 .. +0.0 | 0.13 .. 0.20 |
| `T_RW_Contact` | `drip_run` | 0.84 | +0.0 .. +0.0 | 0.20 .. 0.29 |

## 1b. Tiling footway material

Not decals: seamless tiling MATERIAL textures for the footway surface, which the gap analysis
calls out as "untextured flat concrete; no panel joints, no tonal variation" (gap #4). Same
BC/N/MS packing as the atlases, except the albedo is **opaque RGB** (no alpha channel) because
the whole surface is covered. `qa_check.py` measures the wrap discontinuity against the
texture's own local contrast and fails anything that is not seamless.

| Texture | Size | Tile world size | Relief (mm) | Smoothness range | Source bytes |
|---|---|---|---|---|---|
| `T_SW_Concrete_Panel` | 2048 x 2048 | 4.80 m (4 x 4 panels of 1.20 m) | -14.1 .. +0.6 | 0.04 .. 0.23 | 4.22 MB |

### DO NOT "FIX" LATER: the footway tile carries no distinctive features

`T_SW_Concrete_Panel` deliberately has **no cracks, no stains and no recognisable damage** - only
mottling, per-panel tone and chipped joints. This is not an omission to be filled in.

A distinctive feature in a tiling base becomes a *repeating* feature the moment the surface is
longer than the tile, and a pavement is always longer than 4.80 m. One memorable crack becomes
the same crack every 4.8 m down the whole street, which reads worse than no crack at all.
Anything recognisable belongs in the non-repeating decal atlases, which is what `T_RW_Crack`,
`T_RW_Patch`, `T_RW_Oil`, `T_RW_Grime` and `T_RW_Contact` are for. Agreed with the Unity session
on 2026-11-14 after an earlier version of this texture shipped hairline cracks and the 2x2 preview
showed them repeating.

The same rule applies to any future tiling surface in this set: incident goes in decals, not in
the tile.

## 2. Road-surface meshes

Same conventions as the Infra prop set: `VC_Unity_Prop` export preset via
`Tools/Blender/vc_tools.py`, root empty + `<name>_LOD<n>` children (one LOD set per FBX),
`COL_`/`UCX_`/`UBX_` collision in the same file, applied transforms, box UVs at 1 m per UV unit,
PBR metal/roughness stand-in materials for the VeyraMaterialLibrary remap.

**z = 0 is the road surface for every prop here.** All of them are driven over, so each one
carries a non-convex `COL_` collider that is a single flush plate - the collider never follows
the bars or the pattern relief, so wheels roll across instead of catching. The CLAUDE.md rule
(no vertical step over 0.05 m between adjacent collider surfaces) is checked by `qa_check.py`,
which fails the build if any collider rises more than 0.05 m above the road plane.

| FBX / .blend | LOD0 | LOD1 (% of LOD0) | Collision | Type | Collider top above road | Size (m) | Pivot |
|---|---|---|---|---|---|---|---|
| `VC_Manhole_Round_A` | 648 | 272 (42%) | COL_concrete_VC_Manhole_Round_A (72 tris) | COL | **+10 mm** | 0.80 x 0.80 x 0.08 | centre of the cover at the ROAD SURFACE (z = 0) |
| `VC_Manhole_Round_B` | 732 | 344 (47%) | COL_concrete_VC_Manhole_Round_B (72 tris) | COL | **+10 mm** | 0.80 x 0.80 x 0.09 | centre of the cover at the ROAD SURFACE (z = 0) |
| `VC_UtilityCover_Square` | 276 | 108 (39%) | COL_concrete_VC_UtilityCover_Square (12 tris) | COL | **+10 mm** | 0.76 x 0.76 x 0.07 | centre of the cover at the ROAD SURFACE (z = 0) |
| `VC_StormDrain_Grate_Road` | 168 | 96 (57%) | COL_concrete_VC_StormDrain_Grate_Road (12 tris) | COL | **+6 mm** | 0.72 x 0.72 x 0.10 | centre of the grate at the ROAD SURFACE (z = 0) |
| `VC_ExpansionJoint_Strip_4m` | 40 | 12 (30%) | COL_concrete_VC_ExpansionJoint_Strip_4m (12 tris) | COL | **+0 mm** | 0.30 x 4.00 x 0.05 | start of the piece, joint centreline, z = 0 = road surface |

### Per-mesh notes

- **`VC_Manhole_Round_A`** - 0.62 m round cast cover in a 0.80 m frame, concentric-ring pattern with a centre boss and two lift slots. Cover top +10 mm, frame top +6 mm, rim tapers to the road plane. Materials: VC_Iron_Cast.
- **`VC_Manhole_Round_B`** - 0.62 m round cast cover in a 0.80 m frame, 16 radial ribs inside a perimeter ring with a centre boss and one lift slot. Same flush profile as pattern A, so the two read as different castings in the same frame. Materials: VC_Iron_Cast.
- **`VC_UtilityCover_Square`** - 0.60 m square utility access cover in a 0.72 m frame, chequer studs and two lift slots. Cover top +10 mm, frame top +6 mm. Materials: VC_Iron_Cast.
- **`VC_StormDrain_Grate_Road`** - 0.58 m in-carriageway storm grate in a 0.70 m frame: 7 bars recessed 10 mm under the frame top, two cross ribs, a sump floor 90 mm down. The collider is a single flush plate so wheels roll over the bars instead of dropping in. Materials: VC_Concrete_Struct, VC_Iron_Cast.
- **`VC_ExpansionJoint_Strip_4m`** - 4.00 m transverse/longitudinal joint strip, 0.30 m wide: a 52 mm groove with a sealant bead whose top sits 2 mm below the road. The collider is dead flat (zero step), so the joint is visual only for the vehicle. Materials: VC_Concrete_Struct, VC_Joint_Sealant.

Every prop here is under 2 m, so it ships LOD0 + LOD1 per the CLAUDE.md small-prop rule.

## 3. What the self-check verifies

Textures are read back **from the written PNG files**, not from the generator:
power-of-two and square, channel count per map, alpha exactly 0 on every cell border, each cell
reaching a usable peak alpha, unit-length normals with positive Z, metallic exactly 0,
smoothness non-empty.

Meshes: root empty at the origin with identity transform, every mesh parented to it with
rotation and scale applied, contiguous `_LOD` names, collision prefix and name match, 0 n-gons,
0 inverted shells, 0 open shells, 0 degenerate faces, 0 loose verts, UV and material present,
a collider on every prop, **collider top <= 0.05 m above the road plane**, and the FBX
re-imported with one root and matching triangle counts.

## 4. Notes for the Unity-side integration

1. **Decal projection scale.** Each atlas cell has a real-world size (table above); drive the
   decal projector from it instead of guessing. `T_RW_LaneWear` cells are **1.0 m across x 4.0 m
   along** so a 0.15 m line is a real 0.15 m - the other atlases are square.
2. **Lane-wear decals carry the paint itself**, not just wear: the white edge line, the dashed
   lane line and the double yellow centre line are drawn worn, so they can either replace a
   painted line or be laid over one at low alpha.
3. **Normal maps are OpenGL (+Y up)** - Unity's convention. No channel flip needed.
4. **Alpha is coverage, not opacity of a full material.** In a URP decal shader, feed BC.a to
   both base-colour and normal/MS blend so a decal does not flatten the road's own normal where
   it is faint.
5. **Mesh pivots** are at the road surface, so a cover drops straight onto the road mesh at the
   road's own height. The castings continue below z = 0 (down to -0.09 m for the grate sump), so
   they need a hole or simply sink into the road mesh.
6. `VC_ExpansionJoint_Strip_4m` runs toward **Unity +Z** like the other linear modules, pivot at
   the start of the piece. Rotate 90 deg for a transverse joint.
7. The joint sealant material `VC_Joint_Sealant` is new in this set (registered at runtime in
   `props_roadwear.py` so `ArtSource/Blender/Infra/` is left untouched); it needs an entry in the
   VeyraMaterialLibrary alongside the Infra names.
8. **Markings are drawn at true size** inside their 4 m cells (0.50 m zebra stripes on 1.32 m
   centres, 0.30 m arrow shaft, 0.60 m stop bar), lightly worn rather than pristine, with a
   tyre-crossing scuff where wheels actually cross them. `arrow_turn_right` mirrors on U for the
   left-turn arrow. They complement the worn lane lines in `T_RW_LaneWear`: use these where a
   marking should read as maintained, those where it should read as neglected.
9. **Puddles are a wet-surface override, not geometry**: the albedo darkens, smoothness goes to
   ~0.85 in standing water, and the only relief is a sub-millimetre ripple. Drive them from the
   same wetness parameter as the rain system so they fade in and out with the weather rather
   than sitting on a dry road.
10. **The contact atlas is the cheap half of gap #4** - a grime ring at the base of a pole,
    a dirt line with joint weeds where the footway meets the kerb, wall-base splash and drip
    runs. These are what stop props looking stuck on. `wall_base_splash` and `drip_run` are
    authored for a vertical surface (V runs up the wall).
11. **BUG-170 (CPU budget):** the meshes are 40-732 tris with one or two materials each and are
   meant to be GPU-instanced; the decals are 6 atlases, so a whole street's wear is a handful of
   draw calls if the decals share a material per atlas.
