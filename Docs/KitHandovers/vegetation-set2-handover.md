# Vegetation set 2 — handover (session: veg)

Status: **done**. 16 assets in `ArtSource/Blender/Vegetation2/`, verified, nothing in `Assets/`,
no Unity, no commits, no branch switch.

## Headline numbers
- **3 materials across BOTH sets**: `M_Veyra_Foliage` (set 1), `M_Veyra_Foliage_B` (set 2),
  `M_Veyra_Bark` — the bark atlas is genuinely shared, not duplicated.
  `VC_Tree_Snag_Weathered` uses bark only, so it is a single-material asset.
- Set 2 LOD0 total **11,077 tris** over 16 assets; heaviest is `VC_Tree_Redwood_46m` at 2,372
  (59 % of its 4,000 budget). Ground cover is 12–44 tris at LOD0 and **2 tris at LOD2**.
- Both sets together: 20,809 LOD0 tris across 33 assets.
- `scripts/verify2.py`: **16 assets, 0 issues**. It also fails any set-2 asset that references
  set 1's foliage atlas, so the two atlases can never land on one mesh.

## Why a second foliage atlas was unavoidable
Spruce needles, redwood sprays, ruderal weeds and dry straw have no usable stand-in in atlas A,
and atlas A was full. I kept the cost to one extra material by putting *everything* set 2 needs
into atlas B — including the ground cover — so no set-2 mesh ever mixes atlases.

## Density notes — in BOTH manifests, as asked
`Vegetation2/MANIFEST.md` carries the full table for both sets plus the performance rule; the
set-1 table is also appended to `Vegetation/MANIFEST.md` under "Placement spacing and scatter
density". The rule that matters for BUG-170:

    instances_in_view  ~=  density * pi * cull_distance^2

Keep under 6,000 per module type, 12,000 for all vegetation. **The cull distance is the lever,
not the density** — it is squared. If a density has to rise, cut the cull distance first. And the
CPU cost is renderer count, not triangles: scatter through instancing, never one GameObject per
plant.

## Two things I changed in set 1 (both requested or necessary, no assets touched)
1. `Vegetation/MANIFEST.md` — added the density section you asked for.
2. `Vegetation/scripts/make_review.py` — added a `if __name__ == "__main__"` guard. It called
   `main()` at import time, so set 2 importing its render helpers re-ran set 1's review renders.
   Output is unchanged; set 1's meshes, atlases and FBX are untouched.

## Known soft spots
- Spruce and fir share one needle tile; they are separated by branch geometry, not texture.
- You can see the trunk through the spruce crown. True of real spruce, but worth knowing.
- Ground-cover LOD2 is a single card and will turn edge-on. Deliberate — see the manifest.

Review sheets: `Vegetation2/review/REVIEW_{Alpine,Redwood,Industrial,Ground}.png`.
