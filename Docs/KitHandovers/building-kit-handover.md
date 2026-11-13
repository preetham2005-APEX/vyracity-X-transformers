# Building kit COMPLETE - 88 models, 2,687 of 2,690 targeted records (99.9 %) (session: street props / buildings)

88 original building models in `E:\VeyraCity\ArtSource\Blender\Buildings\` (.blend + export/*.fbx),
210 original tiling PBR textures, 13 kitbash manifests. Full detail: `ArtSource/Blender/Buildings/MANIFEST.md`
and `UNITY_HANDOFF.md`. Street furniture (16 props) is separate, in `ArtSource/Blender/Street/`.

## What it plugs into

Not a new system. Veyra already has `BuildingAssembler` + `KitbashCatalogBuilder` + `KitbashMaterials`,
which swap the procedural box shells for kit models wherever a kit fits. It had no licence-clean kit for
the districts below, so those records kept their boxes (BUG-004).

| Set | Source id | District / styles | Models | Records it fits |
|---|---|---|---|---|
| 1 | `Veyra_Original_Suburban` | D5 `Suburban` | 8 houses (14 x 11) + 2 garages | **1,923 of 1,923** |
| 2 | `Veyra_Original_Downtown` | D1 `GlassModern`/`ArtDeco` | 21 towers | **231 of 232** |
| 3 | `Veyra_Original_NeonMile` | D6 `BrickIndustrial`/`ModernLowrise` | 9 blocks | **114 of 116** |
| 4 | `Veyra_Original_Coast` | D2 `CoastalStucco` | 5 villas (20 x 15), 2 beach clubs (24 x 12), 1 marina (33 x 20) | **81 of 81** |
| 5 | `Veyra_Original_MarketTown` | D8 `MarketTownBrick` | 5 terraces, all 12 x 12 | **65 of 65** |
| 6 | `Veyra_Original_Midrise` | D1+D5 `ResidentialMidrise` | 3 at 34 x 18, 4 at 38 x 24 | **87 of 87** |
| 7 | `Veyra_Original_Ironport` | D3+D7 `Industrial` | 6 sheds (sawtooth, monitor, barrel, flat, 2 rail) | **41 of 41** |
| 8 | `Veyra_Original_IronportTanks` | D3 `IndustrialTank` / `IndustrialRefinery` | 2 tanks + 1 process column | **83 of 83** |
| 9 | `Veyra_Original_Alpine` | D4 `Alpine` | 3 chalets (12 x 10) + workshop | **25 of 25** |
| 10 | `Veyra_Original_Rural` | D8 `Rural` | 2 barns, 2 farmhouses, silo, big barn, mill | **25 of 25** |
| 11 | `Veyra_Original_Aviation` | D7 `Aviation` | 2 hangars, terminal, grandstand | **8 of 8** (see caveat) |
| 12 | `Veyra_Original_Retail` | D5 `SuburbanRetail` | 3 retail boxes | **3 of 3** |
| 13 | `Veyra_Original_Roadside` | D4 `Roadside` | gas station | **1 of 1** |

Model sizes were NOT invented: each set was chosen by simulating the assembler's own fit rule against the
real records in `world_building_placements.csv`. Height stretch at best fit: D1 median 10 % / p90 17 %,
D6 8 % / 18 %, D2 5 % / 26 %, D8 3 % / 6 %. Together the five sets fit **2,414 of the 2,417 records in
those style groups** (89 % of all 2,708 building records).

## The three records that still do not fit - check the DATA, not the kit

`build/analyse_misfits.py` tests every record and says which test failed and why. The first pass left 19
misfits; all but three were height-band gaps (the footprint already fitted a model), so those models were
re-run at the missing heights. What is left is not a kit gap:

| Record | Kind | Footprint | Height | Why |
|---|---|---|---|---|
| `BLD-00181` | office (D1) | 52.0 x 168.3 m | 117.1 | 168 m long; district p95 is 118 m |
| `BLD-00388` | midrise (D6) | 56.9 x 222.4 m | 60.9 | 222 m long; district p95 is 123 m |
| `BLD-00403` | lowrise (D6) | 54.5 x 223.8 m | 14.4 | 224 m long single-storey shed |

Worth a look before placement - a 224 m long building at 14 m tall reads as a data error rather than a
design intent. If they are intentional, say so and I will author them as one-offs.

**A separate blocker, found by auditing the models against the catalogue's own gates:**
`VC_Avi_Terminal` (130 x 36) and `VC_Avi_Grandstand` (18 x 150) trip `max(Footprint) > 120 -> oversize`
in KitbashCatalogBuilder, and their records are that size too, so NO model can serve them until that
limit is raised (160 m covers both). The models are built and correct; they just will not enter the
catalogue. Three more sit exactly on 120 m and pass only because the test is a strict `>`.

**Logged as BUG-171 in `Docs/BUG_TRACKER.md`** (appended row only, nothing else in that file touched,
not committed) so it survives the hand-over.

## Needs from the Unity side (I could not touch Assets/)

1. Copy FBX + textures + manifests (exact list in `UNITY_HANDOFF.md`).
2. **Eight lines in `KitbashCatalogBuilder.Packs`** - without them the catalog gives the entries no styles
   or districts, `StyleMatch` fails and nothing is placed. The lines are written out in the hand-off.
3. A `VC-ORIGINAL` row in `Docs/THIRD_PARTY_LICENSE_REGISTER.csv` (original content, no third party),
   or the entries read as `UNREGISTERED`.
4. `Veyra/Buildings/Build Kitbash Catalog` then `Veyra/Buildings/Assemble Buildings`.

## Two findings worth keeping

- **A collapse decimate destroys box-assembled architecture.** It eats the massing and leaves mullion fins
  and slivers; the LOD2s were see-through shells with no silhouette, for towers *and* houses. Every LOD in
  this kit is now AUTHORED - the same building description re-run at a lower detail level
  (`vc_bld_lib.set_detail`). If other sessions decimate kit geometry, check it the same way.
- **Both numeric "inverted face" tests are unreliable on decimated shells** (a normal recalc is ambiguous
  on an open shell; a nearest-point test against LOD0 mis-reads where the surface drifted). The gate that
  actually works is a render: backface-culled against a red world, so a flipped or missing triangle shows
  as a hole. `preview/lod*_backface_check_*.png`.

## Cost (BUG-170 is open P1 on the CPU)

Whole kit 88,160 tris at LOD0 across 88 models; towers 1,116-2,464 each, houses/villas/terraces 548-1,364. Tri count is independent of
building height - facade relief comes from full-height piers/mullions and per-floor bands, not per-window
geometry. Few material slots per model so instances batch.

## D6 signage

Blank. Frames, fascia bands, blade signs and rooftop boards with no lettering or logo, all on one slot
`VC_Neon_Emissive` (HDR emission) for Unity to drive `_EMISSION` and colour at night - same pattern as the
street set's `VC_Lamp_Emissive`. CLAUDE.md's keyword rule applies: that material must be in the build or in
`VC_Variants.shadervariants`.

## Not built

Only the 11 `Landmark` records, which `BuildingAssembler` skips itself ("landmark (A4 hero pending)").

Texture prefixes repeat where sources share a material family (`VC_In_` for both Ironport sources,
`VC_Rt_` for retail and roadside), so those files are copied into both folders.

`UNITY_HANDOFF.md` is now the single consolidated document: the placement contract as the C# needs it,
the copy list, the 13 Packs lines, expected coverage per source, the blockers above, the full 88-model
table, and a section listing exactly which assembler behaviours I read in the source versus the one I
could not verify (whether LOD1/LOD2 renderers are enabled at assemble time, which is what `FitHeight`
and `HideUnsupported` measure over).

Nothing written to `Assets/`, Unity never run, nothing committed, branch untouched.
