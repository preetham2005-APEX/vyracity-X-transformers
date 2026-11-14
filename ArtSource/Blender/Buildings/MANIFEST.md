# Veyra City - building kit (ArtSource/Blender/Buildings)

Original modular buildings, authored headless in Blender 5.2.3 by the scripts in `build/` (re-runnable:
running them again rebuilds every .blend, .fbx and texture from scratch). Nothing is downloaded, scanned
or traced; reference frames were read for proportion and construction logic only. All signage is blank.

Built 2026-11-13. Metric, 1 unit = 1 m.

## Why these sizes

Every model is sized from `Assets/_Veyra/Data/world_building_placements.csv` (2,708 records), never
invented, and the set for each district was chosen by **simulating `BuildingAssembler`'s own fit rule**
(footprint +-22 % after a uniform 0.85-1.2 scale, height 0.65-1.5x, either orientation) against the real
records. `BuildingAssembler` then stretches each instance to the record's exact height, so the number
that matters is the height stretch at best fit - reported per set below.

## D5 suburban houses

Source id `Veyra_Original_Suburban` - districts D5, styles Suburban.

Coverage against the real records: **1923 of 1923 fit**; height stretch at best fit median 7 %, p90 14 %, worst 30 %. Every record in the district is covered.

| Model | Footprint | Height | LOD0 | LOD1 | LOD2 | Slots |
|---|---|---|---|---|---|---|
| `VC_House_DormerCottage` | 14 x 11 m | 7.27 m | 852 | 588 | 140 | 6 |
| `VC_House_Duplex` | 14 x 11 m | 7.30 m | 1096 | 568 | 100 | 6 |
| `VC_House_FlatParapet` | 14 x 11 m | 7.14 m | 756 | 588 | 192 | 6 |
| `VC_House_GablePorch` | 14 x 11 m | 7.27 m | 1184 | 704 | 140 | 6 |
| `VC_House_GarageCommunity` | 40 x 22 m | 7.08 m | 960 | 528 | 432 | 7 |
| `VC_House_GarageFront` | 14 x 11 m | 7.20 m | 904 | 568 | 184 | 7 |
| `VC_House_GarageSmall` | 9 x 8 m | 6.10 m | 320 | 236 | 140 | 7 |
| `VC_House_HipBay` | 14 x 11 m | 7.27 m | 1112 | 656 | 200 | 6 |
| `VC_House_RanchCarport` | 14 x 11 m | 7.12 m | 616 | 424 | 176 | 6 |
| `VC_House_SplitLevel` | 14 x 11 m | 7.20 m | 1032 | 600 | 132 | 6 |

Set LOD0 total: **8832 triangles** across 10 models (mean 883).

Model usage over the district's records: `Duplex` 913, `RanchCarport` 880, `DormerCottage` 47, `GarageFront` 47, `FlatParapet` 34, `GarageSmall` 1, `GarageCommunity` 1.

## D1 Crown Downtown towers

Source id `Veyra_Original_Downtown` - districts D1, styles ArtDeco, GlassModern.

Coverage against the real records: **231 of 232 fit**; height stretch at best fit median 10 %, p90 17 %, worst 41 %. The one record that does not fit (52 x 168.3 x 117.1 m) is a data outlier far outside the district's norms, not a kit gap, and keeps its procedural shell - see "Records the kit does not fit".

| Model | Footprint | Height | LOD0 | LOD1 | LOD2 | Slots |
|---|---|---|---|---|---|---|
| `VC_Tower_Block117100` | 117 x 117 m | 101.20 m | 2304 | 1260 | 204 | 5 |
| `VC_Tower_Block11762` | 117 x 117 m | 63.20 m | 2304 | 1260 | 204 | 5 |
| `VC_Tower_Deco120` | 54 x 54 m | 124.70 m | 2464 | 1468 | 388 | 5 |
| `VC_Tower_Deco175` | 54 x 54 m | 179.70 m | 2464 | 1468 | 388 | 5 |
| `VC_Tower_Deco240` | 54 x 54 m | 244.70 m | 2464 | 1468 | 388 | 5 |
| `VC_Tower_Glass130` | 55 x 56 m | 137.10 m | 1348 | 832 | 256 | 5 |
| `VC_Tower_Glass190` | 55 x 56 m | 197.10 m | 1348 | 832 | 256 | 5 |
| `VC_Tower_Glass28` | 55 x 56 m | 28.70 m | 1284 | 768 | 192 | 5 |
| `VC_Tower_Glass42` | 55 x 56 m | 42.70 m | 1284 | 768 | 192 | 5 |
| `VC_Tower_Glass62` | 55 x 56 m | 63.10 m | 1308 | 792 | 216 | 5 |
| `VC_Tower_Glass90` | 55 x 56 m | 91.10 m | 1320 | 804 | 228 | 5 |
| `VC_Tower_Slab115110` | 54 x 115 m | 111.20 m | 1752 | 996 | 204 | 5 |
| `VC_Tower_Slab115200` | 54 x 115 m | 201.20 m | 1752 | 996 | 204 | 5 |
| `VC_Tower_Slab11550` | 54 x 115 m | 51.20 m | 1752 | 996 | 204 | 5 |
| `VC_Tower_Slab80170` | 54 x 80 m | 171.20 m | 1440 | 852 | 204 | 5 |
| `VC_Tower_Slab8070` | 54 x 80 m | 71.20 m | 1440 | 852 | 204 | 5 |
| `VC_Tower_Thin31130` | 31 x 60 m | 137.10 m | 1156 | 712 | 256 | 5 |
| `VC_Tower_Thin31150` | 31 x 117 m | 157.10 m | 1684 | 1000 | 256 | 5 |
| `VC_Tower_Thin3134` | 31 x 60 m | 34.70 m | 1092 | 648 | 192 | 5 |
| `VC_Tower_Thin3170` | 31 x 60 m | 71.10 m | 1116 | 672 | 216 | 5 |
| `VC_Tower_Thin3180l` | 31 x 117 m | 81.10 m | 1644 | 960 | 216 | 5 |

Set LOD0 total: **34720 triangles** across 21 models (mean 1653).

Model usage over the district's records: `Glass42` 44, `Glass28` 30, `Glass62` 28, `Glass90` 25, `Deco120` 17, `Slab8070` 16, `Slab11550` 11, `Slab115110` 11, `Glass130` 8, `Deco175` 8, `Slab80170` 8, `Glass190` 5, `Block117100` 4, `Thin3170` 4, `Thin31130` 3, `Thin3134` 3, `Block11762` 2, `Deco240` 2, `Thin3180l` 1, `Thin31150` 1.

## D6 Neon Mile low-rise

Source id `Veyra_Original_NeonMile` - districts D6, styles BrickIndustrial, ModernLowrise.

Coverage against the real records: **114 of 116 fit**; height stretch at best fit median 8 %, p90 17 %, worst 27 %. The 2 records that do not fit (56.9 x 222.4 x 60.9 m; 54.5 x 223.8 x 14.4 m) are each a data outlier far outside the district's norms, not a kit gap, and keep its procedural shell - see "Records the kit does not fit".

| Model | Footprint | Height | LOD0 | LOD1 | LOD2 | Slots |
|---|---|---|---|---|---|---|
| `VC_Neon_Brick30` | 57 x 57 m | 30.00 m | 1224 | 804 | 372 | 6 |
| `VC_Neon_Brick33l` | 56 x 105 m | 33.00 m | 1524 | 948 | 372 | 6 |
| `VC_Neon_Brick42` | 57 x 57 m | 42.00 m | 1260 | 816 | 372 | 6 |
| `VC_Neon_Brick62` | 57 x 57 m | 62.00 m | 1332 | 852 | 372 | 6 |
| `VC_Neon_Brick62l` | 56 x 105 m | 62.00 m | 1620 | 996 | 372 | 6 |
| `VC_Neon_Low14l` | 57 x 112 m | 14.00 m | 1260 | 852 | 408 | 6 |
| `VC_Neon_Low16` | 58 x 58 m | 16.00 m | 1044 | 732 | 408 | 6 |
| `VC_Neon_Low18l` | 57 x 112 m | 18.00 m | 1260 | 852 | 408 | 6 |
| `VC_Neon_Low21` | 58 x 58 m | 21.00 m | 1056 | 732 | 408 | 6 |

Set LOD0 total: **11580 triangles** across 9 models (mean 1286).

Model usage over the district's records: `Brick30` 30, `Brick42` 27, `Low21` 20, `Low16` 12, `Brick33l` 10, `Low18l` 6, `Brick62l` 4, `Low14l` 3, `Brick62` 2.

## D2 Azure Coast stucco

Source id `Veyra_Original_Coast` - districts D2, styles CoastalStucco.

Coverage against the real records: **81 of 81 fit**; height stretch at best fit median 5 %, p90 26 %, worst 26 %. Every record in the district is covered.

| Model | Footprint | Height | LOD0 | LOD1 | LOD2 | Slots |
|---|---|---|---|---|---|---|
| `VC_Coast_ClubA` | 24 x 12 m | 6.80 m | 744 | 552 | 144 | 6 |
| `VC_Coast_ClubB` | 24 x 12 m | 10.29 m | 1296 | 876 | 216 | 6 |
| `VC_Coast_Marina` | 33 x 20 m | 7.60 m | 1016 | 716 | 152 | 6 |
| `VC_Coast_VillaA` | 20 x 15 m | 9.60 m | 1364 | 824 | 140 | 6 |
| `VC_Coast_VillaB` | 20 x 15 m | 9.69 m | 828 | 696 | 276 | 6 |
| `VC_Coast_VillaC` | 20 x 15 m | 9.62 m | 1196 | 740 | 200 | 6 |
| `VC_Coast_VillaD` | 20 x 15 m | 6.40 m | 788 | 488 | 128 | 6 |
| `VC_Coast_VillaE` | 20 x 15 m | 7.88 m | 908 | 608 | 200 | 6 |

Set LOD0 total: **8140 triangles** across 8 models (mean 1017).

Model usage over the district's records: `VillaA` 24, `VillaE` 21, `VillaB` 20, `ClubA` 12, `Marina` 3, `VillaD` 1.

## D8 Redwood Valley market town

Source id `Veyra_Original_MarketTown` - districts D8, styles MarketTownBrick.

Coverage against the real records: **65 of 65 fit**; height stretch at best fit median 3 %, p90 6 %, worst 10 %. Every record in the district is covered.

| Model | Footprint | Height | LOD0 | LOD1 | LOD2 | Slots |
|---|---|---|---|---|---|---|
| `VC_Town_TerraceA` | 12 x 12 m | 7.90 m | 684 | 468 | 168 | 6 |
| `VC_Town_TerraceB` | 12 x 12 m | 8.88 m | 924 | 588 | 168 | 6 |
| `VC_Town_TerraceC` | 12 x 12 m | 9.62 m | 548 | 356 | 104 | 6 |
| `VC_Town_TerraceD` | 12 x 12 m | 6.82 m | 824 | 488 | 128 | 6 |
| `VC_Town_TerraceE` | 12 x 12 m | 8.11 m | 708 | 480 | 180 | 6 |

Set LOD0 total: **3688 triangles** across 5 models (mean 737).

Model usage over the district's records: `TerraceD` 29, `TerraceA` 11, `TerraceB` 10, `TerraceE` 9, `TerraceC` 6.

## ResidentialMidrise (D1 + D5)

Source id `Veyra_Original_Midrise` - districts D1, D5, styles ResidentialMidrise.

Coverage against the real records: **87 of 87 fit**; height stretch at best fit median 7 %, p90 18 %, worst 23 %. Every record in the district is covered.

| Model | Footprint | Height | LOD0 | LOD1 | LOD2 | Slots |
|---|---|---|---|---|---|---|
| `VC_Mid_Mid34A` | 34 x 18 m | 18.68 m | 624 | 408 | 144 | 5 |
| `VC_Mid_Mid34B` | 34 x 18 m | 18.68 m | 588 | 408 | 144 | 5 |
| `VC_Mid_Mid34C` | 34 x 18 m | 15.68 m | 588 | 396 | 144 | 5 |
| `VC_Mid_Mid3830` | 38 x 24 m | 31.68 m | 816 | 480 | 144 | 5 |
| `VC_Mid_Mid3845` | 38 x 24 m | 46.68 m | 852 | 540 | 144 | 5 |
| `VC_Mid_Mid3860` | 38 x 24 m | 61.68 m | 1140 | 588 | 144 | 5 |
| `VC_Mid_Mid3875` | 38 x 24 m | 76.68 m | 1068 | 648 | 144 | 5 |

Set LOD0 total: **5676 triangles** across 7 models (mean 810).

Model usage over the district's records: `Mid34C` 40, `Mid3845` 17, `Mid3860` 10, `Mid3830` 8, `Mid34A` 7, `Mid3875` 5.

## D3 Ironport sheds

Source id `Veyra_Original_Ironport` - districts D3, D7, styles Industrial.

Coverage against the real records: **41 of 41 fit**; height stretch at best fit median 6 %, p90 18 %, worst 26 %. Every record in the district is covered.

| Model | Footprint | Height | LOD0 | LOD1 | LOD2 | Slots |
|---|---|---|---|---|---|---|
| `VC_Ind_ShedBarrel` | 64 x 40 m | 16.82 m | 408 | 184 | 44 | 5 |
| `VC_Ind_ShedFlat` | 64 x 40 m | 10.92 m | 680 | 324 | 132 | 5 |
| `VC_Ind_ShedMonitor` | 90 x 50 m | 15.90 m | 664 | 280 | 52 | 5 |
| `VC_Ind_ShedRail` | 120 x 24 m | 16.30 m | 376 | 376 | 64 | 5 |
| `VC_Ind_ShedRailLow` | 120 x 24 m | 10.80 m | 376 | 376 | 64 | 5 |
| `VC_Ind_ShedSawtooth` | 90 x 50 m | 17.50 m | 660 | 312 | 60 | 5 |

Set LOD0 total: **3164 triangles** across 6 models (mean 527).

Model usage over the district's records: `ShedFlat` 21, `ShedMonitor` 13, `ShedRailLow` 5, `ShedSawtooth` 2.

## D3 Ironport tanks and process columns

Source id `Veyra_Original_IronportTanks` - districts D3, styles IndustrialRefinery, IndustrialTank.

Coverage against the real records: **83 of 83 fit**; height stretch at best fit median 10 %, p90 19 %, worst 29 %. Every record in the district is covered.

| Model | Footprint | Height | LOD0 | LOD1 | LOD2 | Slots |
|---|---|---|---|---|---|---|
| `VC_Ind_ProcessColumn` | 8 x 8 m | 52.10 m | 578 | 218 | 70 | 3 |
| `VC_Ind_TankCone` | 34 x 34 m | 19.10 m | 1020 | 600 | 108 | 3 |
| `VC_Ind_TankDome` | 34 x 34 m | 14.55 m | 1058 | 626 | 126 | 3 |

Set LOD0 total: **2656 triangles** across 3 models (mean 885).

Model usage over the district's records: `TankDome` 60, `TankCone` 20, `ProcessColumn` 3.

## D4 Blackridge alpine

Source id `Veyra_Original_Alpine` - districts D4, styles Alpine.

Coverage against the real records: **25 of 25 fit**; height stretch at best fit median 6 %, p90 11 %, worst 11 %. Every record in the district is covered.

| Model | Footprint | Height | LOD0 | LOD1 | LOD2 | Slots |
|---|---|---|---|---|---|---|
| `VC_Alp_ChaletA` | 12 x 10 m | 8.02 m | 956 | 668 | 176 | 6 |
| `VC_Alp_ChaletB` | 12 x 10 m | 8.00 m | 836 | 572 | 152 | 6 |
| `VC_Alp_ChaletC` | 12 x 10 m | 8.00 m | 520 | 376 | 116 | 6 |
| `VC_Alp_Workshop` | 42 x 24 m | 8.50 m | 680 | 416 | 152 | 6 |

Set LOD0 total: **2992 triangles** across 4 models (mean 748).

Model usage over the district's records: `ChaletB` 13, `ChaletA` 11, `Workshop` 1.

## D8 Redwood Valley rural

Source id `Veyra_Original_Rural` - districts D8, styles Rural.

Coverage against the real records: **25 of 25 fit**; height stretch at best fit median 3 %, p90 4 %, worst 4 %. Every record in the district is covered.

| Model | Footprint | Height | LOD0 | LOD1 | LOD2 | Slots |
|---|---|---|---|---|---|---|
| `VC_Rur_BarnBig` | 64 x 30 m | 10.85 m | 380 | 260 | 104 | 6 |
| `VC_Rur_BarnGambrel` | 26 x 14 m | 9.70 m | 384 | 264 | 72 | 6 |
| `VC_Rur_BarnLeanto` | 26 x 14 m | 8.40 m | 368 | 272 | 56 | 6 |
| `VC_Rur_FarmhouseA` | 12 x 10 m | 7.02 m | 644 | 452 | 128 | 6 |
| `VC_Rur_FarmhouseB` | 12 x 10 m | 7.02 m | 572 | 404 | 140 | 6 |
| `VC_Rur_Mill` | 32 x 18 m | 11.00 m | 952 | 520 | 76 | 6 |
| `VC_Rur_Silo` | 7 x 7 m | 16.55 m | 556 | 316 | 112 | 6 |

Set LOD0 total: **3856 triangles** across 7 models (mean 550).

Model usage over the district's records: `Mill` 8, `Silo` 8, `FarmhouseA` 7, `BarnBig` 1, `BarnLeanto` 1.

## D7 Falcon Airfield

Source id `Veyra_Original_Aviation` - districts D7, styles Aviation.

Coverage against the real records: **8 of 8 fit**; height stretch at best fit median 4 %, p90 15 %, worst 15 %. Every record in the district is covered.

| Model | Footprint | Height | LOD0 | LOD1 | LOD2 | Slots |
|---|---|---|---|---|---|---|
| `VC_Avi_Grandstand` | 18 x 150 m | 14.00 m | 256 | 208 | 52 | 5 |
| `VC_Avi_HangarArch` | 62 x 74 m | 18.50 m | 304 | 236 | 80 | 5 |
| `VC_Avi_HangarMonitor` | 62 x 74 m | 17.70 m | 532 | 376 | 88 | 5 |
| `VC_Avi_Terminal` | 130 x 36 m | 14.15 m | 456 | 384 | 156 | 5 |

Set LOD0 total: **1548 triangles** across 4 models (mean 387).

Model usage over the district's records: `HangarMonitor` 6, `Terminal` 1, `Grandstand` 1.

## D5 suburban retail

Source id `Veyra_Original_Retail` - districts D5, styles SuburbanRetail.

Coverage against the real records: **3 of 3 fit**; height stretch at best fit median 13 %, p90 19 %, worst 19 %. Every record in the district is covered.

| Model | Footprint | Height | LOD0 | LOD1 | LOD2 | Slots |
|---|---|---|---|---|---|---|
| `VC_Ret_RetailBigbox` | 120 x 60 m | 11.46 m | 548 | 324 | 120 | 5 |
| `VC_Ret_RetailSmall` | 24 x 14 m | 6.20 m | 192 | 168 | 120 | 5 |
| `VC_Ret_RetailStrip` | 40 x 30 m | 7.72 m | 360 | 312 | 120 | 5 |

Set LOD0 total: **1100 triangles** across 3 models (mean 366).

Model usage over the district's records: `RetailStrip` 1, `RetailBigbox` 1, `RetailSmall` 1.

## D4 roadside

Source id `Veyra_Original_Roadside` - districts D4, styles Roadside.

Coverage against the real records: **1 of 1 fit**; height stretch at best fit median 10 %, p90 10 %, worst 10 %. Every record in the district is covered.

| Model | Footprint | Height | LOD0 | LOD1 | LOD2 | Slots |
|---|---|---|---|---|---|---|
| `VC_Ret_GasStation` | 26 x 16 m | 6.64 m | 208 | 168 | 96 | 5 |

Set LOD0 total: **208 triangles** across 1 models (mean 208).

Model usage over the district's records: `GasStation` 1.

## Records the kit does not fit

Every record in the five style groups was tested against the exported models with the assembler's own
rule; `build/analyse_misfits.py` reports which test failed and whether the cause is a gap in the kit or
an anomaly in the data. The first pass left 19 misfits. All but three were **height-band gaps** - the
footprint already fitted an existing model, the height did not - so those models were re-run at the
missing heights (and two D5 garage typologies were added). What remains:

| Record | Kind | Footprint | Height | Failed | Verdict |
|---|---|---|---|---|---|
| `BLD-00181` | office | 52.0 x 168.3 m | 117.1 m | width | data outlier: 168 m long, far beyond the district's p95 of 118 m |
| `BLD-00388` | midrise | 56.9 x 222.4 m | 60.9 m | depth | data outlier: 222 m long, far beyond the district's p95 of 123 m |
| `BLD-00403` | lowrise | 54.5 x 223.8 m | 14.4 m | depth, width | data outlier: 224 m long, far beyond the district's p95 of 123 m |

All three are **data anomalies, not kit gaps**: single buildings 168-224 m long in districts whose
95th percentile is 118-123 m. No model can cover them without being wrong for everything else, so
they keep their procedural shell. They are worth a look before placement - `BLD-00403` in particular
is 223.8 x 54.5 m at 14.4 m tall, which is a 224 m long single-storey shed.

**Coverage: 2687 of the 2690 records these 13 sources target (99.9 %).** Of the 2,708 building records,
the only group no set covers is the 11 `Landmark` records, which `BuildingAssembler` skips itself
("landmark (A4 hero pending)"). The 3 that do not fit are the BUG-171 data anomalies above.

**Whole kit at LOD0: 88160 triangles across 88 models.** Towers and low-rise blocks cost the same
triangles whatever their height, because facade relief is carried by full-height piers/mullions and
per-floor bands rather than per-window geometry - which is also what keeps BUG-170 (open P1, CPU) in mind.

## Pipeline facts

- Export preset **VC_Unity_Prop** (`vc_tools.vc_export_fbx(mode="prop")`); **pivot at base centre on
  the x/y origin at z = 0**, which is what `BuildingAssembler` places at the record's ground level.
- Authored with the street front facing Blender -Y, so the front is **Unity +Z** and the record's
  `unity_yaw_deg` turns it to the street.
- One LOD set per FBX: root EMPTY + `<name>_LOD0/1/2` + `UBX_<name>`. The assembler disables kit
  colliders (each record keeps its data-footprint collider), so the UBX is there for other uses.
- Rotation and scale applied; 0 n-gons, 0 inverted faces at LOD0, 0 degenerate faces, UVs on every LOD
  (box projection: a 2.0 m tile for houses and low-rise, 4.0 m - one storey - for towers).
- Material slots are few and shared within a set, so instances batch.
- Windows and doors in the house and low-rise sets are **real openings** with a reveal, recessed
  glazing and a sill. Towers carry their floor lines in the curtain-wall texture instead, which is why
  a 240 m tower costs the same as a 28 m one.
- D6 signage is **blank**: frames, fascia bands, blade signs and a rooftop board with no lettering or
  logo, all on their own slot `VC_Neon_Emissive` (HDR emission) so Unity drives `_EMISSION` and the
  colour at night - the same pattern as the street set's `VC_Lamp_Emissive`.

## LODs are authored, not decimated

A collapse decimate destroys this kind of box-assembled geometry: it eats the massing and leaves the
fins and slivers behind. Rendered backface-culled against a red world, the decimated LOD2s were
see-through shells with no silhouette - for the towers *and* the houses. Every LOD is therefore built
by re-running the same building description at a lower detail level (`vc_bld_lib.set_detail`): LOD1
drops sills, panes, awnings and half the piers; LOD2 is massing only - solid walls, no openings, roof
and crown kept, because that silhouette is what carries most frames at city distance.

The check is the render, not a metric: both numeric "inverted face" tests are unreliable on this
geometry (a normal recalculation is ambiguous on an open shell; a nearest-point test against LOD0
mis-reads wherever the surface drifted). `preview/lod1_backface_check_*.png` and
`preview/lod2_backface_check_*.png` render every LOD backface-culled against red, so a flipped or
missing triangle shows as a hole. They are solid.

## Self-check

`build/verify_fbx.py` re-imports every exported FBX and measures it: **0 problems** over all 88 files
- no n-gons, no inverted faces at LOD0, UVs on every LOD, `_LOD0/1/2` plus a collision mesh in every
file, every base at z = 0 and every pivot on the origin. Result: `build/verify.json`.

## Textures (210 files, all original, power-of-two 512)

`textures/<material>_baseColor.png` (sRGB), `_metallicRoughness.png` (glTF packing: G = roughness,
B = metallic - what `KitbashMaterials` reads) and `_normal.png` (OpenGL tangent space), generated by
`build/make_textures.py` from noise and arithmetic. Every pattern is seamless and sized in metres, so
brick courses, siding boards, roof tiles, curtain-wall storeys and shutter slats come out at real size.

| Set | Material prefixes |
|---|---|
| `Veyra_Original_Suburban` | `VC_Wall_`, `VC_Roof_`, `VC_Trim_`, `VC_Glass_Window`, `VC_Door_`, `VC_Metal_Garage` |
| `Veyra_Original_Downtown` | `VC_Twr_` |
| `Veyra_Original_NeonMile` | `VC_Nm_`, `VC_Neon_Emissive` |
| `Veyra_Original_Coast` | `VC_Cs_` |
| `Veyra_Original_MarketTown` | `VC_Mt_` |
| `Veyra_Original_Midrise` | `VC_Mr_` |
| `Veyra_Original_Ironport` | `VC_In_` |
| `Veyra_Original_IronportTanks` | `VC_In_` |
| `Veyra_Original_Alpine` | `VC_Al_` |
| `Veyra_Original_Rural` | `VC_Ru_` |
| `Veyra_Original_Aviation` | `VC_Av_` |
| `Veyra_Original_Retail` | `VC_Rt_` |
| `Veyra_Original_Roadside` | `VC_Rt_` |

## Hand-off to Unity

This session cannot touch `Assets/` (the main session holds the editor lock) and did not run Unity.
`UNITY_HANDOFF.md` has the copy list and the three lines of C# needed.
