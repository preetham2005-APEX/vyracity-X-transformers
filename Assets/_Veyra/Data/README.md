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
