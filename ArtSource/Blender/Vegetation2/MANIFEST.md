# Veyra City vegetation set 2 - MANIFEST

Second vegetation set: D4 Blackridge alpine, D8 Redwood Valley, D3 Ironport industrial neglect,
and the shared ground-cover scatter modules. Set 1 (`../Vegetation/`) is unchanged and is read only
from here - this set reuses its bark atlas and its geometry library rather than copying either.

All content original. Built with Blender 5.2.3 LTS headless, exported through
`Tools/Blender/vc_tools.py` (`VC_Unity_Prop`), 1 Blender unit = 1 m.

## Materials: 2 new-set materials, 3 across both sets

| Material | Textures | Used by |
|---|---|---|
| `M_Veyra_Foliage_B` | `T_Veyra_Foliage_Atlas_B_{BaseColor,Normal,MetallicSmoothness}.png` (2048, alpha clip 0.5, Render Face Both) | every set 2 asset except the snag |
| `M_Veyra_Bark` | **shared with set 1** - `../Vegetation/textures/T_Veyra_Bark_Atlas_*.png` (1024, opaque) | every trunk in both sets |

So the complete Veyra vegetation layer is **3 materials**: foliage A, foliage B, bark. A second
foliage atlas was unavoidable - alpine spruce, redwood spray, ruderal weeds and dry grass have no
usable stand-in among set 1's tiles - but the bark atlas is genuinely shared, and
`VC_Tree_Snag_Weathered` uses only bark, so it ships with a single material slot.

Set 2's verify pass fails any asset that references set 1's foliage atlas, so the two atlases can
never accidentally end up on one mesh (which would make it a 3-submesh asset).

### Atlas B tiles (16)

Clusters: `spruce_spray`, `redwood_spray`, `dead_grass`, `rock_tuft`, `weed_tall`, `weed_broadleaf`, `sapling_leaf`, `mountain_scrub`
Billboards (LOD2): `bb_spruce`, `bb_fir`, `bb_redwood`, `bb_sapling`, `bb_mtn_scrub`, `bb_weed`, `bb_dead_grass`, `bb_ground_patch`

## Assets

| Asset (FBX) | size x/y/z m | LOD0 | LOD1 | LOD2 | LOD1 % | canopy normal bias | collision | atlas tiles |
|---|---|---|---|---|---|---|---|---|
| `VC_Tree_Spruce_Alpine` | 5.4 x 4.7 x 18.0 | 2096 | 876 | 4 | 42% | 43 deg | UCX_VC_Tree_Spruce_Alpine (24 tris) | spruce_spray + bark_conifer (LOD2: bb_spruce) |
| `VC_Tree_Fir_Alpine` | 8.2 x 8.3 x 13.0 | 1808 | 732 | 4 | 40% | 45 deg | UCX_VC_Tree_Fir_Alpine (24 tris) | spruce_spray + bark_conifer (LOD2: bb_fir) |
| `VC_Tree_Snag_Weathered` | 2.2 x 1.9 x 9.2 | 239 | 105 | 20 | 44% | n/a (no foliage) | UCX_VC_Tree_Snag_Weathered (24 tris) | bark_conifer only - no foliage, ONE material |
| `VC_Shrub_MountainScrub` | 1.9 x 1.9 x 1.1 | 146 | 48 | 4 | 33% | 26 deg | none | mountain_scrub + bark_stem (LOD2: bb_mtn_scrub) |
| `VC_Shrub_RockLedgeTuft` | 0.9 x 0.8 x 0.7 | 60 | 24 | 4 | 40% | 36 deg | none | rock_tuft (LOD2: bb_mtn_scrub) |
| `VC_Tree_Redwood_22m` | 8.7 x 8.0 x 22.0 | 1764 | 624 | 6 | 35% | 49 deg | UCX_VC_Tree_Redwood_22m (24 tris) | redwood_spray + bark_conifer (LOD2: bb_redwood) |
| `VC_Tree_Redwood_34m` | 12.2 x 11.8 x 34.0 | 2068 | 732 | 6 | 35% | 49 deg | UCX_VC_Tree_Redwood_34m (24 tris) | redwood_spray + bark_conifer (LOD2: bb_redwood) |
| `VC_Tree_Redwood_46m` | 16.2 x 15.9 x 46.0 | 2372 | 900 | 6 | 38% | 49 deg | UCX_VC_Tree_Redwood_46m (24 tris) | redwood_spray + bark_conifer (LOD2: bb_redwood) |
| `VC_Weed_ConcreteCrack` | 0.7 x 0.8 x 0.5 | 36 | 16 | 4 | 44% | 23 deg | none | weed_broadleaf (LOD2: bb_ground_patch) |
| `VC_Weed_FenceLine` | 1.4 x 1.5 x 1.2 | 92 | 38 | 4 | 41% | 30 deg | none | weed_tall (LOD2: bb_weed) |
| `VC_Tree_VolunteerSapling` | 2.8 x 2.5 x 4.2 | 216 | 88 | 4 | 41% | 42 deg | UCX_VC_Tree_VolunteerSapling (24 tris) | sapling_leaf + bark_stem (LOD2: bb_sapling) |
| `VC_Grass_DeadClump` | 1.1 x 1.1 x 0.8 | 68 | 28 | 4 | 41% | 29 deg | none | dead_grass (LOD2: bb_dead_grass) |
| `VC_Ground_GrassTuft_S` | 0.5 x 0.6 x 0.6 | 12 | 6 | 2 | 50% | 81 deg | none | dead_grass (LOD2: bb_dead_grass) |
| `VC_Ground_GrassPatch_M` | 1.0 x 1.2 x 0.7 | 28 | 12 | 2 | 43% | 78 deg | none | dead_grass, rock_tuft (LOD2: bb_ground_patch) |
| `VC_Ground_WeedPatch_M` | 1.1 x 1.1 x 0.8 | 28 | 12 | 2 | 43% | 78 deg | none | weed_tall, weed_broadleaf (LOD2: bb_ground_patch) |
| `VC_Ground_DryPatch_L` | 1.7 x 1.7 x 0.8 | 44 | 18 | 2 | 41% | 75 deg | none | dead_grass, weed_tall (LOD2: bb_ground_patch) |
| **TOTAL (set 2)** | | **11077** | **4259** | **78** | | | | |

Set 1 total for comparison: 9732 / 3698 / 110. Both sets together: **20809** LOD0 triangles across 33 assets.

## Triangle budget

Every asset is inside the brief's budgets (4000 for large trees, 1500 for small). The heaviest is
`VC_Tree_Redwood_46m` at 2372, 59% of its allowance. Ground cover is held far tighter than the
brief required - 12 to 44 triangles at LOD0, and **2 triangles at LOD2** (a single card, not a
crossed pair) - because ground cover is the only asset class genuinely instanced in the thousands,
so a triangle here is paid for roughly a thousand times more often than a triangle on a redwood.

Where silhouette and triangles conflicted, triangles won:
- **Spruce and fir share one `spruce_spray` tile.** A real fir's needles are flatter and blunter
  than a spruce's. Distinguishing them would have cost a second tile and gained nothing at 6 m
  spacing; the two silhouettes are separated by branch geometry instead (spruce: narrow, steep,
  strongly downswept; fir: 1.6x broader, shallower taper).
- **Redwood foliage is `redwood_spray` at three scales**, not three authored crowns.
- **Ground-cover LOD2 is a single card.** It will turn edge-on to the camera. At the distance
  ground cover reaches LOD2 it is 1-3 pixels tall, and the 2 triangles saved are multiplied by
  thousands of instances.

## Placement spacing and scatter density - set 2

Authoring intent: what each asset was designed to be seen at. Densities are instances per square
metre of the area being scattered, not of the whole district.

| Asset | Pattern | Spacing / density | Notes |
|---|---|---|---|
| `VC_Tree_Spruce_Alpine` | forest stand | 6-11 m on centre, 0.012-0.025 /m^2 | D4 slopes. Mix ~60/40 with Fir. Thin to 0.008 /m^2 above the treeline band. |
| `VC_Tree_Fir_Alpine` | forest stand | 7-12 m on centre, 0.010-0.020 /m^2 | Broader crown (6.5 m); use on the lower slopes and in clearing edges. |
| `VC_Tree_Snag_Weathered` | rare accent | 1 per 20-40 live conifers | Never in groups of more than 2. Highest value on ridgelines and burn scars. |
| `VC_Shrub_MountainScrub` | scatter | 0.05-0.15 /m^2, min 1.1 m apart | Between and under the conifers; raise to 0.2 /m^2 in clearings where trees are absent. |
| `VC_Shrub_RockLedgeTuft` | scatter, masked to rock | 0.10-0.40 /m^2, min 0.5 m apart | Rock ledges and scree only. 60 tris, so it tolerates the high end of that range. |
| `VC_Tree_Redwood_22m` | grove | 10-16 m on centre, 0.006-0.012 /m^2 | The default redwood: ~50 % of the D8 grove population. |
| `VC_Tree_Redwood_34m` | grove | 12-18 m on centre, 0.004-0.008 /m^2 | ~30 % of the population. Keep 8 m minimum centre distance - the trunk is 1.9 m across at the base. |
| `VC_Tree_Redwood_46m` | landmark | 1 in 8-12 redwoods, 18-30 m on centre | Read from kilometres away, so place these on ridges and valley floors with clear sky behind, not buried in a stand. |
| `VC_Weed_ConcreteCrack` | linear, along cracks and joints | 0.3-1.2 m spacing in a 0.15 m band | Follow the actual crack and expansion-joint lines; a scatter over open concrete reads wrong. |
| `VC_Weed_FenceLine` | linear row | 0.9-1.3 m pitch | Fence lines, wall bases, kerb-to-building strips. Overlap by ~15 % for a continuous run. |
| `VC_Tree_VolunteerSapling` | rare single | 1 per 15-40 m of neglected frontage | Never in a row, never evenly - a volunteer by definition landed where nobody planted it. |
| `VC_Grass_DeadClump` | scatter | 0.08-0.25 /m^2, min 0.8 m apart | D3 yards and D8 dry season verges. |
| `VC_Ground_GrassTuft_S` | dense scatter | 0.5-2.0 /m^2, cull at 25 m | 12 tris: the ONLY module in either set safe above 1 /m^2. This is the near-field filler. |
| `VC_Ground_GrassPatch_M` | dense scatter | 0.15-0.50 /m^2, cull at 45 m | The general-purpose ground module. Scatter this, then sprinkle GrassTuft_S inside 25 m on top. |
| `VC_Ground_WeedPatch_M` | dense scatter | 0.10-0.35 /m^2, cull at 45 m | D3 and neglected ground. Mix 1:2 with GrassPatch_M rather than using it alone. |
| `VC_Ground_DryPatch_L` | sparse scatter | 0.05-0.15 /m^2, cull at 70 m | Covers the most ground per instance, so it carries the mid distance. Largest footprint (1.8 m). |

### The density rule that actually matters

Spacing above is art direction. This is the performance contract:

    instances_in_view  ~=  density * pi * cull_distance^2

Keep that under **6000 per module type**, and under **12000 for all vegetation combined**. At the
triangle counts in the tables above that is roughly 200-350k triangles of vegetation on screen,
which is comfortable on an RTX 4070 and - with GPU instancing on both foliage materials and the
bark material - costs a handful of draw calls rather than thousands.

Worked example: `VC_Ground_GrassPatch_M` at 0.3 /m^2 culled at 45 m gives 0.3 * pi * 45^2 = 1900
instances, 53k triangles. The same density culled at 150 m instead gives 21000 instances. The
density is not what breaks the budget - the cull distance is, because it is squared. If a density
has to rise, lower the cull distance first.

Given BUG-170 (main-thread p95 18-93 ms against a 10 ms budget), the thing to watch on the CPU side
is not triangles at all but the number of distinct renderers submitted per frame. Scatter through
instanced rendering or a batched scatter system, not one GameObject per plant.

## Placement spacing and scatter density - set 1

Repeated here so both sets can be read from one page; the same table is in `../Vegetation/MANIFEST.md`.

| Asset | Pattern | Spacing / density | Notes |
|---|---|---|---|
| `VC_Palm_Royal_8m` | row along boulevard edge | 9-12 m on centre | Mix the three heights ~2:3:2 (8/12/16) along a run; never two 16 m adjacent. Single file, not clumped. |
| `VC_Palm_Royal_12m` | row along boulevard edge | 9-12 m on centre | As above - this is the one to default to. |
| `VC_Palm_Royal_16m` | row along boulevard edge | 9-12 m on centre | Landmark height. 1 in 4 at most, or the rhythm reads artificial. |
| `VC_Palm_Fan_Short` | cluster | 2.5-4 m on centre, clusters of 3-7 | Plaza corners, medians, front gardens. Also fine as singles. |
| `VC_Palm_Fan_Tall` | cluster | 2.5-4 m on centre, clusters of 3-7 | As above; 1 tall per 2-3 short. |
| `VC_Tree_Broadleaf_Spreading` | street row / park group | 11-14 m on centre | Crown is 10 m across: below 11 m the crowns interpenetrate visibly. In parks 8-10 m is deliberate (closed canopy). |
| `VC_Tree_Broadleaf_Upright` | street row | 10-13 m on centre | Narrower crown (9 m), so it takes the tighter streets. Mix ~1:1 with Spreading along a long avenue. |
| `VC_Tree_Cypress_6m` | formal row / accent | 2.5-3.5 m on centre | Meant to touch and form a screen. As a single accent, keep 4 m clear. |
| `VC_Tree_Cypress_9m` | formal row / accent | 3.0-4.0 m on centre | Driveway and villa entrances; pairs read well. |
| `VC_Tree_Pine_Tall` | forest clump | 8-14 m on centre, 0.008-0.018 /m^2 | Clumps of 5-20 with gaps between clumps - an even field reads as an orchard, not a forest. |
| `VC_Tree_Pine_Umbrella` | forest edge / ridge | 10-16 m on centre, 0.005-0.012 /m^2 | Wide crown (8 m). Use on ridges and forest edges where the umbrella silhouette is against sky. |
| `VC_Hedge_Box_2m` | tiled module | exactly 2.00 m pitch | Modules butt flush; ends are capped at the module boundary. Vary the yaw by 0 or 180 deg only. |
| `VC_Hedge_Box_Corner` | tiled module | centre 1.85 m from the adjoining straight module's centre | Nominal footprint 1.70 x 1.70 m: 0.85 (half corner) + 1.00 (half straight) = 1.85 m along each arm axis. |
| `VC_Hedge_Box_2m_Tall` | tiled module | exactly 2.00 m pitch | Do not mix with the 1.2 m module in the same run - the height step is obvious. |
| `VC_Shrub_DryScrub` | scatter | 0.04-0.12 /m^2, min 1.2 m apart | Roadside verges and dry slopes. Drive-over, no collider. |
| `VC_Shrub_GreenBush` | scatter / planted group | 0.03-0.10 /m^2, min 1.5 m apart | Residential planting beds and medians; groups of 3-5 read better than an even field. |
| `VC_Shrub_VergeTuft` | scatter | 0.10-0.30 /m^2, min 0.7 m apart | Kerb lines and gutter edges. The cheapest of set 1's shrubs - use this one where density is wanted. |

## Technique

Identical to set 1 (see `../Vegetation/MANIFEST.md`): leaf cards against a packed atlas, hard alpha
cut with 14 px colour dilation, canopy-biased custom split normals, authored LODs, bark tiling
inside an atlas rect by restarting the tile in V every ~2-3 m of trunk. New here:

- **Crossed foliage tufts.** Each tuft on a conifer or sapling is 2 cards sharing a centre, rotated
  about the branch axis, so no branch ever goes edge-on to the camera. Same triangle cost as 2 flat
  cards, far better volume.
- **Ground-cover normals are biased 0.88 toward the ground-plane-up direction** rather than toward a
  canopy sphere. A scattered field of upright cards shaded off their own flat normals flickers dark
  as the camera turns; this is what stops it.
- **The snag carries no foliage at all** and is the only single-material asset in either set.

## Collision

`UCX_<name>` convex trunk colliders on all five tree species and the snag (24 tris each). Nothing
else has a collider: mountain scrub, rock tufts, all weeds, dead grass and every ground-cover module
are meant to be driven over.

## Unity import notes

Same as set 1 (`../Vegetation/MANIFEST.md`, "Unity import notes"), with one addition: create
`M_Veyra_Foliage_B` as a second serialised material alongside `M_Veyra_Foliage`, same settings
(URP/Lit, Alpha Clipping on at 0.5, Render Face Both, Smoothness Source Metallic Alpha, GPU
instancing on), and register it in `VeyraMaterialLibrary`. The bark material is shared, not
duplicated.

Suggested LOD transition heights: trees 0.45 / 0.12 / 0.012; redwoods 0.50 / 0.15 / 0.02 (they are
large and tall, so they stay on LOD0 longer in screen-height terms); weeds and shrubs
0.40 / 0.10 / 0.015; ground cover 0.30 / 0.08 / 0.02 with an aggressive cull distance per the
density rule above.

## Rebuilding

```
blender -b --python scripts/make_atlas_b.py    # atlas B + atlas_uvs.json
blender -b --python scripts/build_all2.py      # .blend files + FBX + build_stats.json
blender -b --python scripts/verify2.py         # re-imports every FBX and checks it
blender -b --python scripts/make_review2.py    # review sheets
blender -b --python scripts/make_manifest2.py  # this file, and set 1's density section
```

Verify result at the time of writing: **16 assets, 0 issues** (`scripts/verify_report.json`).
