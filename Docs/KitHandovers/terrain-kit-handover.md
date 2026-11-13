# Terrain kit + far-tree impostors — handover (session: veg)

Status: **done and run**. 19 assets in `ArtSource/Blender/Terrain/`, verify **19/19, 0 issues**.
Nothing in `Assets/`, no Unity, no commits, no branch switch. `Vegetation/` and `Vegetation2/` were
read but not modified.

## Headline numbers
- 13 rock assets + 6 far-tree impostors. LOD0 total **7,306 tris**; impostors are **6 tris each**
  (3 cards: two crossed vertical + one horizontal).
- **2 new materials** (`M_Veyra_Rock`, `M_Veyra_Impostor`); the fallen logs reuse `M_Veyra_Bark`.
  Whole natural world across three kits = **5 materials**.
- Rock is a **tiling** texture set, not an atlas: 410 px/m at a 2.5 m repeat, one material.

## The number the Unity session asked for — impostor switch distances in METRES

| Species | role | switch at | texel floor | tile |
|---|---|---|---|---|
| Redwood_46m | forest | **161 m** | 57 m | 1024 px |
| Spruce_Alpine | forest | **63 m** | 44 m | 512 px |
| Pine_Tall | forest | **45 m** | 31 m | 512 px |
| Palm_Royal_16m | street | **141 m** | 44 m | 512 px |
| Broadleaf_Spreading | street | **76 m** | 24 m | 512 px |
| Palm_Fan_Tall | street | **45 m** | 28 m | 256 px |

Forest species swap at screen-relative height 0.25, street species at 0.11 (a lower h swaps
further out, keeping the real mesh for longer where the player drives past). Texel floor is
enforced in code, not prose; nothing is currently floored, nearest margin 1.42x. Distances are at
1440p / 60 deg vertical FOV — regenerate from the metres if either changes.

Suggested chain: **LOD0 ends 0.45, LOD1 ends the species' h, impostor ends 0.012 (cull)**.

## What went wrong and what it cost
Five real bugs, all caught by the verify pass or the review sheets rather than by reading:
1. The 90-degree bake view rendered **empty** — camera position and rotation disagreed, so it
   looked away from the tree. Both cards would have carried the same view.
2. Impostor cards were sized to the tree's bbox rather than the **rendered frame**, which carries
   a 1.02 pad and a quantised aspect; every impostor drew its tree slightly wrong-sized.
3. The cut-face module was **not periodic** (noise fed `u` directly), so modules would have shown
   a seam and a collision step. Now circular in `u` and verified on the re-imported mesh.
4. Collider checks read **local** coordinates; `bake_space_transform` puts the height axis on the
   root, so a 2.25 m radius was being read as 2.34 m of height. World space now.
5. The review renderer did not hide `COL_` meshes — only `UCX_`/`UBX_`. An untextured collider was
   z-fighting the cut face and reading as white. Fixed locally in `make_review_terrain.py`, NOT in
   set 1's copy. **Anyone else adding COL_ colliders will hit this.**

## Flagged, not worked around
`VeyraModelPostprocessor` surface lookup — already adopted as the `COL_<surface>_<name>`
convention. This kit ships `COL_gravel_VC_Rock_ScreePatch` and `COL_concrete_VC_Rock_CutFace_8m`.
Concrete is **nearest-available, not true**: there is no rock surface asset, and this kit does not
invent tokens with nothing behind them. A `rock` surface is the right answer whenever one exists.

## Known soft spots
- Impostors: no parallax, ~25-30 % narrowing at 45 deg azimuth, and the crown collapses when seen
  from above — D4 and D8 both have clifftop roads, so that will be seen. A third horizontal card
  (2 more tris) fixes the last one if it matters.
- The bedding strata on the cut face read subtly; a harder sawtooth made it look like painted
  stripes, so this is the deliberate end of that trade.
- Octahedral impostors are recorded as rejected **with re-open conditions** in the manifest, so
  nobody relitigates it from scratch.

Review sheets: `Terrain/review/REVIEW_{Boulder,Outcrop,CutFace,Scree,Shore,Log,Impostor}.png`.
Full detail, including the 0.35 LOD-convention conflict, in `Terrain/MANIFEST.md`.
