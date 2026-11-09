# Veyra Playbook — Data Supplement

These files complete the data in `VEYRA_CITY_WORLD_DESIGN` for the playbook cards (A3, D1–D8, W7). They are generated from the same validated world model, terrain and coordinate system.

| File | Rows | What it is |
|---|---|---|
| `world_building_placements.csv` | 2,708 | Every building in all 8 districts: footprint, height, rotation, kind, suggested style kit and typology (D1 typologies T1–T7 match `crown_downtown_building_placements.csv`), ground heights, Unity position, 1 km cell and 250 m chunk |
| `container_placements.csv` | 6,050 | Ironport 40 ft containers (12.19 × 2.44 m, long axis east–west), stack tiers 1–4, colour |
| `crane_placements.csv` | 8 | Ship-to-shore gantry cranes (78 m) on the Gantry Container Terminal quay |

## Conventions
- World: x east, y north, metres, origin at the SW corner of the 8 × 6 km map. Unity = (x − 4000, elevation, y − 3000); +Z is north.
- `rot_deg` is counter-clockwise from east (data convention). In Unity, `unity_yaw_deg = −rot_deg`, with `footprint_w_m` along the building's local X and `footprint_l_m` along local Z.
- `ground_min_m` is the lowest terrain height under the footprint corners. Place the building base at `unity_y_base` (= ground_min) and extend a plinth/foundation down so no gap shows on slopes.
- `cell` is the 1 km streaming cell (A–H west→east, 1–6 north→south). `chunk` = `Chunk_<Cell>_<i><j>` (250 m; i west→east 0–3, j south→north 0–3).
- `seed` is a deterministic 0–1 value for variation (colours, window patterns, rooftop props).
- Style kits: GlassModern/ArtDeco, ResidentialMidrise, ModernLowrise, BrickIndustrial, CoastalStucco, Suburban, SuburbanRetail, Industrial, IndustrialTank, IndustrialRefinery, Alpine, Rural, MarketTownBrick, Aviation, Roadside, Landmark (see the playbook, card A2).

Put these in `E:\VeyraCity\Assets\_Veyra\Data\` next to the other CSVs (card S3).

## Provenance check (card S3, 2026-11-09)
- The three supplement files are **not part of the design package**. They entered git in commit `afd9978` (pre-playbook full-game checkpoint, 2026-11-09 21:15), written by the earlier Claude Code session; this README is the only description of how they were made.
- **D1 (282 rows) is identical to the package**: every row is within 0.1 m of a `crown_downtown_building_placements.csv` placement with the same height, footprint and typology, and every package placement has its counterpart.
- 12 of 18 `landmark_id` rows sit at their `location_registry.csv` point (< 0.5 m). The other 6 are buildings set back 20–35 m from a road-side registry point: LM-28 Falcon Grandstands 20.6 m, GAR-05 30 m, GAR-08 25 m, RST-03 20 m, RST-02 20 m, GAR-01 35 m.
- D2–D8 (2,426 rows) have no CSV in the package; the README says they come from the same world model. They are **not yet verified against the district blockout GLBs** (follow-up for card A3, before buildings are rebuilt).
- `Veyra/Data/Validate Data` checks every row's Unity formula and `cell`/`chunk` columns (all 2,708 + 6,050 + 8 rows pass).

## Design-package files (card S3)
Copied unchanged (SHA-256 verified) from `E:\PROJECT BANGERRR\VEYRA_CITY_WORLD_DESIGN` by `Tools/World/import_veyra_data.py`: `location_registry.csv`, `road_network_geometry.json`, the 16 `vegetation_*`/`street_furniture_*` CSVs, `crown_downtown_building_placements.csv` and `terrain_tiles_manifest.json`. Readers: `Assets/_Veyra/World/Data` (namespace `VeyraCity.World.Data`); coordinates: `VeyraCity.Core.WorldCoordinates` (the playbook's "VeyraCoords").
