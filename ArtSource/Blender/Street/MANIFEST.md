# Veyra City - street furniture set (ArtSource/Blender/Street)

Original geometry, authored headless in Blender 5.2.3 by `build/build_street.py` (re-runnable: running it
again rebuilds every .blend and .fbx from scratch, with no duplicated objects). Reference frames in
`ReferenceImages/08_GTAV_VideoCapture_2026-11-12/` (VC_12, VC_17, VC_20, VC_22, VC_30, VC_31) were read for
**proportion and construction logic only**; no mesh, texture, logo, lettering or layout comes from any other
game. Every sign plate and the bus-shelter poster are **blank** - Unity applies Veyra's own artwork.

Built 2026-11-14. Blender 5.2.3 LTS, metric, 1 unit = 1 m.

## Pipeline facts

- Export preset **VC_Unity_Prop** through `vc_tools.vc_export_fbx(..., mode="prop")`: FBX, axis_forward -Z,
  axis_up Y, global_scale 1, apply_unit_scale, FBX_SCALE_UNITS, bake_space_transform.
- **Pivot: base centre**, on the x/y origin at z = 0 for every prop. For the lamps that is the *pole* base,
  not the bounding-box centre (a cobra arm overhangs the carriageway) - which is what street placement needs.
- Authored with the **front facing Blender -Y**, so the preset's 180 deg turn puts the front at **Unity +Z**.
  Confirmed by re-importing every FBX and measuring it (`build/verify_fbx.py` -> `build/verify.json`).
- One LOD set per FBX. The root is an EMPTY named after the prop; its children are the `<name>_LOD<n>` meshes
  plus the collision meshes, so Unity builds exactly one LODGroup per file.
- Rotation and scale applied (every LOD mesh reports scale 1/1/1, rotation 0/0/0). Caps are triangulated, so
  **0 n-gons**; normals recalculated outward, **0 inverted faces**, 0 degenerate faces, 0 loose vertices;
  UVs on every LOD (Smart UV Project, 0.02 island margin).
- Materials are Principled **metal/roughness PBR**, solid albedo, no texture maps yet - so nothing can break
  the power-of-two rule. Slot order in the table below is the Unity material order.
- Luminaires carry a dedicated emissive slot **`VC_Lamp_Emissive`** (HDR emission 1.00 / 0.92 / 0.74 at
  strength 8) on the lens or lantern glass only, so Unity can drive `_EMISSION` at night.
- LOD ratios are the spec 1.0 / 0.40 / 0.10 **with a floor** (LOD1 >= 72 tris, LOD2 >= 56 tris). These props
  are cheap enough that a literal 10 % would delete the pole; the ratio actually used is listed per prop.
- Preview renders: `preview/sheet_small_props.png`, `preview/sheet_tall_props.png` and their `_34`
  three-quarter versions. The pale 1.80 m rods at each end are a human-scale reference, not part of the set.

## Triangle counts

| Prop | Size W x D x H (m) | LOD0 | LOD1 | LOD2 | ratios | budget | Collision (tris) | Material slots |
|---|---|---|---|---|---|---|---|---|
| `VC_Hydrant` | 0.35 x 0.35 x 0.78 | 260 | 102 | - | 1.00 / 0.40 | small < 400 | UBX_VC_Hydrant (12) | 0: VC_Hydrant_Red |
| `VC_ParkingMeter` | 0.19 x 0.19 x 1.33 | 104 | 68 | - | 1.00 / 0.69 | small < 400 | UBX_VC_ParkingMeter (12) | 0: VC_Paint_Slate, 1: VC_Glass_Clear |
| `VC_TrashCan_Civic` | 0.66 x 0.66 x 1.03 | 240 | 96 | - | 1.00 / 0.40 | small < 400 | UCX_VC_TrashCan_Civic (28) | 0: VC_Paint_Civic_Green |
| `VC_Dumpster` | 1.86 x 1.33 x 1.19 | 264 | 90 | - | 1.00 / 0.40 | small < 400 | UBX_VC_Dumpster (12) | 0: VC_Paint_Civic_Blue |
| `VC_PostBox` | 0.56 x 0.49 x 1.25 | 92 | 70 | - | 1.00 / 0.78 | small < 400 | UBX_VC_PostBox (12) | 0: VC_Paint_Civic_Green |
| `VC_NewsBox` | 0.44 x 0.44 x 1.10 | 148 | 70 | - | 1.00 / 0.49 | small < 400 | UBX_VC_NewsBox (12) | 0: VC_Paint_Orange, 1: VC_Glass_Clear |
| `VC_BusShelter` | 4.16 x 1.76 x 2.50 | 268 | 84 | 36 | 1.00 / 0.40 / 0.21 | shelter < 1500 | UBX_VC_BusShelter_Post1 (12), UBX_VC_BusShelter_Post2 (12), UBX_VC_BusShelter_Post3 (12), UBX_VC_BusShelter_Post4 (12), UBX_VC_BusShelter_Rear (12), UBX_VC_BusShelter_Left (12), UBX_VC_BusShelter_Poster (12), UBX_VC_BusShelter_Bench (12), UBX_VC_BusShelter_Roof (12) | 0: VC_Paint_Alu_Grey, 1: VC_Glass_Clear, 2: VC_Wood_Slat, 3: VC_Poster_Blank |
| `VC_Lamp_Cobra_Single` | 0.43 x 3.08 x 9.37 | 220 | 84 | 52 | 1.00 / 0.40 / 0.26 | lamp < 1200 | UCX_VC_Lamp_Cobra_Single (28), UBX_VC_Lamp_Cobra_Single_Shaft (12) | 0: VC_Paint_Alu_Grey, 1: VC_Lamp_Emissive |
| `VC_Lamp_Cobra_Twin` | 0.43 x 5.72 x 9.37 | 320 | 122 | 48 | 1.00 / 0.40 / 0.17 | lamp < 1200 | UCX_VC_Lamp_Cobra_Twin (28), UBX_VC_Lamp_Cobra_Twin_Shaft (12) | 0: VC_Paint_Alu_Grey, 1: VC_Lamp_Emissive |
| `VC_Lamp_Boulevard_Twin` | 0.36 x 4.30 x 6.70 | 228 | 80 | 48 | 1.00 / 0.40 / 0.25 | lamp < 1200 | UCX_VC_Lamp_Boulevard_Twin (28), UBX_VC_Lamp_Boulevard_Twin_Shaft (12) | 0: VC_Paint_Alu_Grey, 1: VC_Lamp_Emissive, 2: VC_Concrete_Plinth |
| `VC_Lamp_Ornate_Single` | 0.60 x 0.60 x 5.00 | 368 | 146 | 54 | 1.00 / 0.40 / 0.15 | lamp < 1200 | UCX_VC_Lamp_Ornate_Single (28), UBX_VC_Lamp_Ornate_Single_Shaft (12) | 0: VC_Iron_Painted, 1: VC_Lamp_Emissive |
| `VC_Lamp_Ornate_Twin` | 2.03 x 0.60 x 5.08 | 616 | 246 | 60 | 1.00 / 0.40 / 0.10 | lamp < 1200 | UCX_VC_Lamp_Ornate_Twin (28), UBX_VC_Lamp_Ornate_Twin_Shaft (12) | 0: VC_Iron_Painted, 1: VC_Lamp_Emissive |
| `VC_SignBlade` | 1.10 x 1.10 x 2.94 | 120 | 68 | 52 | 1.00 / 0.60 / 0.47 | small < 400 | UBX_VC_SignBlade (12) | 0: VC_Steel_Dark, 1: VC_Sign_BlankFace |
| `VC_SignSpeedLimit` | 0.66 x 0.17 x 2.36 | 112 | 72 | 56 | 1.00 / 0.64 / 0.50 | small < 400 | UBX_VC_SignSpeedLimit (12) | 0: VC_Steel_Dark, 1: VC_Sign_BlankFace |
| `VC_Bollard` | 0.26 x 0.27 x 1.02 | 180 | 72 | - | 1.00 / 0.40 | small < 400 | UCX_VC_Bollard (28) | 0: VC_Paint_Slate, 1: VC_Retro_White |
| `VC_Barrel_Construction` | 0.56 x 0.56 x 0.90 | 156 | 70 | - | 1.00 / 0.46 | small < 400 | UCX_VC_Barrel_Construction (28) | 0: VC_Paint_Orange, 1: VC_Retro_White |

Whole set at LOD0: **3696 triangles** across 16 props (476 collision triangles).

## Files

| Prop | .blend | .fbx | Pivot | Notes |
|---|---|---|---|---|
| `VC_Hydrant` | `ArtSource/Blender/Street/VC_Hydrant.blend` | `ArtSource/Blender/Street/export/VC_Hydrant.fbx` | base centre (0, 0, 0) | 0.78 m. Second colour variant: material VC_Hydrant_Yellow (same mesh). |
| `VC_ParkingMeter` | `ArtSource/Blender/Street/VC_ParkingMeter.blend` | `ArtSource/Blender/Street/export/VC_ParkingMeter.fbx` | base centre (0, 0, 0) | 1.34 m, single head on post. Display glass = slot 1. |
| `VC_TrashCan_Civic` | `ArtSource/Blender/Street/VC_TrashCan_Civic.blend` | `ArtSource/Blender/Street/export/VC_TrashCan_Civic.fbx` | base centre (0, 0, 0) | 1.03 m civic/park litter bin with rain hood. |
| `VC_Dumpster` | `ArtSource/Blender/Street/VC_Dumpster.blend` | `ArtSource/Blender/Street/export/VC_Dumpster.fbx` | base centre (0, 0, 0) | 1.84 x 1.28 x 1.15 m commercial wheeled dumpster, sloped twin-leaf lid. |
| `VC_PostBox` | `ArtSource/Blender/Street/VC_PostBox.blend` | `ArtSource/Blender/Street/export/VC_PostBox.fbx` | base centre (0, 0, 0) | 1.25 m pillar post box, slot and collection door on the front (-Y). |
| `VC_NewsBox` | `ArtSource/Blender/Street/VC_NewsBox.blend` | `ArtSource/Blender/Street/export/VC_NewsBox.fbx` | base centre (0, 0, 0) | 1.10 m newspaper vending box. Window glass = slot 1. |
| `VC_BusShelter` | `ArtSource/Blender/Street/VC_BusShelter.blend` | `ArtSource/Blender/Street/export/VC_BusShelter.fbx` | base centre (0, 0, 0) | 4.16 x 1.76 x 2.50 m. Open side faces -Y (Unity +Z). Poster frame is BLANK (slot 3), both faces. |
| `VC_Lamp_Cobra_Single` | `ArtSource/Blender/Street/VC_Lamp_Cobra_Single.blend` | `ArtSource/Blender/Street/export/VC_Lamp_Cobra_Single.fbx` | base centre (0, 0, 0) | 8.52 m pole, arm reaches 2.46 m toward -Y. Lens = slot 1 (VC_Lamp_Emissive). |
| `VC_Lamp_Cobra_Twin` | `ArtSource/Blender/Street/VC_Lamp_Cobra_Twin.blend` | `ArtSource/Blender/Street/export/VC_Lamp_Cobra_Twin.fbx` | base centre (0, 0, 0) | 8.52 m pole, two opposed arms (±Y). Lenses = slot 1 (VC_Lamp_Emissive). |
| `VC_Lamp_Boulevard_Twin` | `ArtSource/Blender/Street/VC_Lamp_Boulevard_Twin.blend` | `ArtSource/Blender/Street/export/VC_Lamp_Boulevard_Twin.fbx` | base centre (0, 0, 0) | 6.28 m modern twin-arm boulevard lamp. Lenses = slot 1 (VC_Lamp_Emissive). |
| `VC_Lamp_Ornate_Single` | `ArtSource/Blender/Street/VC_Lamp_Ornate_Single.blend` | `ArtSource/Blender/Street/export/VC_Lamp_Ornate_Single.fbx` | base centre (0, 0, 0) | 5.01 m post-top cast-iron lantern standard. Glass cage = slot 1 (VC_Lamp_Emissive). |
| `VC_Lamp_Ornate_Twin` | `ArtSource/Blender/Street/VC_Lamp_Ornate_Twin.blend` | `ArtSource/Blender/Street/export/VC_Lamp_Ornate_Twin.fbx` | base centre (0, 0, 0) | 5.09 m twin-lantern cast-iron standard, lanterns at x = ±0.80 m. Glass cages = slot 1 (VC_Lamp_Emissive). |
| `VC_SignBlade` | `ArtSource/Blender/Street/VC_SignBlade.blend` | `ArtSource/Blender/Street/export/VC_SignBlade.fbx` | base centre (0, 0, 0) | 2.95 m street-name blade sign, two crossed BLANK plates (slot 1). |
| `VC_SignSpeedLimit` | `ArtSource/Blender/Street/VC_SignSpeedLimit.blend` | `ArtSource/Blender/Street/export/VC_SignSpeedLimit.fbx` | base centre (0, 0, 0) | 2.36 m slim post, 0.66 m BLANK circular plate (slot 1) facing -Y. |
| `VC_Bollard` | `ArtSource/Blender/Street/VC_Bollard.blend` | `ArtSource/Blender/Street/export/VC_Bollard.fbx` | base centre (0, 0, 0) | 1.02 m traffic bollard, reflective band = slot 1. |
| `VC_Barrel_Construction` | `ArtSource/Blender/Street/VC_Barrel_Construction.blend` | `ArtSource/Blender/Street/export/VC_Barrel_Construction.fbx` | base centre (0, 0, 0) | 0.90 m construction barrel, two reflective bands = slot 1. |

## Collision

`UBX_` = BoxCollider, `UCX_` = convex MeshCollider (CLAUDE.md). Collision meshes carry no material and never
render. Lamp heads 6-9 m up get no collider; every pole and upright does. The bus shelter ships nine box
colliders (4 posts, rear and left glazing, poster bay, bench, roof).

- `VC_Hydrant`: `UBX_VC_Hydrant` (12 tris)
- `VC_ParkingMeter`: `UBX_VC_ParkingMeter` (12 tris)
- `VC_TrashCan_Civic`: `UCX_VC_TrashCan_Civic` (28 tris)
- `VC_Dumpster`: `UBX_VC_Dumpster` (12 tris)
- `VC_PostBox`: `UBX_VC_PostBox` (12 tris)
- `VC_NewsBox`: `UBX_VC_NewsBox` (12 tris)
- `VC_BusShelter`: `UBX_VC_BusShelter_Post1` (12 tris), `UBX_VC_BusShelter_Post2` (12 tris), `UBX_VC_BusShelter_Post3` (12 tris), `UBX_VC_BusShelter_Post4` (12 tris), `UBX_VC_BusShelter_Rear` (12 tris), `UBX_VC_BusShelter_Left` (12 tris), `UBX_VC_BusShelter_Poster` (12 tris), `UBX_VC_BusShelter_Bench` (12 tris), `UBX_VC_BusShelter_Roof` (12 tris)
- `VC_Lamp_Cobra_Single`: `UCX_VC_Lamp_Cobra_Single` (28 tris), `UBX_VC_Lamp_Cobra_Single_Shaft` (12 tris)
- `VC_Lamp_Cobra_Twin`: `UCX_VC_Lamp_Cobra_Twin` (28 tris), `UBX_VC_Lamp_Cobra_Twin_Shaft` (12 tris)
- `VC_Lamp_Boulevard_Twin`: `UCX_VC_Lamp_Boulevard_Twin` (28 tris), `UBX_VC_Lamp_Boulevard_Twin_Shaft` (12 tris)
- `VC_Lamp_Ornate_Single`: `UCX_VC_Lamp_Ornate_Single` (28 tris), `UBX_VC_Lamp_Ornate_Single_Shaft` (12 tris)
- `VC_Lamp_Ornate_Twin`: `UCX_VC_Lamp_Ornate_Twin` (28 tris), `UBX_VC_Lamp_Ornate_Twin_Shaft` (12 tris)
- `VC_SignBlade`: `UBX_VC_SignBlade` (12 tris)
- `VC_SignSpeedLimit`: `UBX_VC_SignSpeedLimit` (12 tris)
- `VC_Bollard`: `UCX_VC_Bollard` (28 tris)
- `VC_Barrel_Construction`: `UCX_VC_Barrel_Construction` (28 tris)

## Material library used by this set

| Material | Use | Metallic | Roughness | Emission |
|---|---|---|---|---|
| `VC_Hydrant_Red` | hydrant body (colour variant A) | 0.00 | 0.42 | - |
| `VC_Hydrant_Yellow` | hydrant body (colour variant B, same mesh) | 0.00 | 0.42 | - |
| `VC_Steel_Galv` | spare galvanised steel | 1.00 | 0.44 | - |
| `VC_Steel_Dark` | sign posts and brackets | 0.00 | 0.45 | - |
| `VC_Iron_Painted` | ornate lamp standards | 0.00 | 0.45 | - |
| `VC_Paint_Civic_Green` | post box, civic litter bin | 0.00 | 0.45 | - |
| `VC_Paint_Civic_Blue` | commercial dumpster | 0.00 | 0.45 | - |
| `VC_Paint_Slate` | parking meter, bollard | 0.00 | 0.50 | - |
| `VC_Paint_Orange` | news box, construction barrel | 0.00 | 0.52 | - |
| `VC_Paint_White` | spare white paint | 0.00 | 0.45 | - |
| `VC_Paint_Alu_Grey` | cobra and boulevard lamps, shelter frame | 0.35 | 0.40 | - |
| `VC_Retro_White` | retroreflective bands | 0.00 | 0.22 | - |
| `VC_Glass_Clear` | shelter glazing, meter and news-box windows | 0.00 | 0.07 | - |
| `VC_Sign_BlankFace` | BLANK sign plates | 0.00 | 0.33 | - |
| `VC_Poster_Blank` | BLANK shelter poster | 0.00 | 0.38 | - |
| `VC_Concrete_Plinth` | boulevard lamp plinth | 0.00 | 0.72 | - |
| `VC_Wood_Slat` | shelter bench | 0.00 | 0.58 | - |
| `VC_Lamp_Emissive` | ALL luminaire lenses - Unity drives _EMISSION | 0.00 | 0.18 | 1.00 / 0.92 / 0.74 @ 8.0 |

## Self-check

`build/verify.json` is the machine-readable result of re-importing all 16 exported FBX files: **0 problems**
- no n-gons, no inverted faces, no missing UVs, every file has `_LOD<n>` meshes and at least one collision
mesh, every base sits at z = 0, every root pivot at the origin. Known and accepted: decimated LOD1/LOD2
meshes contain a few non-manifold edges where the collapse opened a shell. That is normal for a far LOD, and
they keep LOD0's winding because the LOD cleanup deliberately does not re-run a normal recalculation on them
(a recalc on a partly open shell is ambiguous and can flip faces).

## Not done here

- No texture maps and no packed UV atlas beyond Smart UV Project; these are PBR solid-colour materials.
- Nothing was written into `Assets/` and Unity was never run - another session holds the editor lock.
- Not yet placed, not yet measured in-engine: no Unity import, LODGroup, collider or frame-cost evidence.
