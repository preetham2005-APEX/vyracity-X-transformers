# Vegetation set — handover (session: veg)

Status: **done**. 17 assets in `ArtSource/Blender/Vegetation/`, verified, nothing written to `Assets/`,
nothing committed, no Unity touched.

## Headline numbers
- **2 distinct materials for the whole vegetation set** (`M_Veyra_Foliage` alpha-clip + `M_Veyra_Bark`),
  one 2048 foliage atlas shared by every species, one 1024 bark atlas.
- 17 LOD0 meshes total **9,732 tris**. Heaviest single asset 1,552 (Pine_Tall) against a 4,000 budget.
  LOD1 = 32–41 % of LOD0 (authored, not decimated). LOD2 = 4–20 tris.
- `scripts/verify.py` re-imports every FBX and checks naming / transforms / pivot / UVs / budgets /
  normal bias / material count: **17 assets, 0 issues**.

## What the main session needs to do on the Unity side
Full detail in `ArtSource/Blender/Vegetation/MANIFEST.md` ("Unity import notes"). The two that will
silently ruin the look if missed:
1. **Normals: Import** on the FBX importer. Recalculate throws away the canopy-biased custom split
   normals, which are the whole reason the foliage shades as a volume.
2. `M_Veyra_Foliage` must be **Alpha Clipping on (0.5), Render Face Both, Smoothness Source =
   Metallic Alpha**. The cards are single-sided quads and rely on two-sided rendering.
Also: GPU instancing on both materials; register them in `VeyraMaterialLibrary` (no `Shader.Find`).

## Deliberate deviations from the brief — flag if unwanted
- Hedges carry `UBX_` BoxColliders (a hedge has no trunk; a drive-through 2.5 m hedge wall seemed
  worse than the rule). Shrubs carry no collider at all.
- LOD2 billboard cards are sized to the LOD0 crown width so the silhouette doesn't pop, which
  stretches the painted tile ~1.3× horizontally on the fan palm.

## Known soft spots (not blockers)
- Pine whorls are still somewhat regular; reads fine at driving speed, less so in a hero shot.
- Faint seams are visible between hedge shell cells at close range on the 2.5 m module.
- Palm frond V-fold is obvious from directly overhead (helicopter / drone cameras).

Review sheets per species: `ArtSource/Blender/Vegetation/review/REVIEW_<Species>.png`
(4 rows: LOD0 front, LOD0 three-quarter, LOD1 front, LOD2 front, same framing).
