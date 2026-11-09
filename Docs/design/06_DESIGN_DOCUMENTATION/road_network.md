# Veyra City — Road Network Specification

All geometry comes from the validated network in `08_3D_ASSETS/roads/road_network_geometry.json` (2D centrelines plus 3D profiles sampled every 4 m). Coordinates are metres from the south-west corner (x east, y north). Unity: X = x − 4000, Z = y − 3000, Y = elevation.

## 1. Road hierarchy

| Class | Use | Lanes | Width (m) | Design speed | Max grade | Mesh module |
|---|---|---|---|---|---|---|
| MW | Motorway (Veyra Ring) | 3+3, divided | 32 | 110 km/h | 5 % | road_kit `road_MW_*` |
| EX | Expressway | 2+2, divided | 24 | 100 km/h | 8.5 % | road_kit `road_EX_*` |
| HW | Highway | 2+2 (1+1 in mountains) | 18 | 90 km/h | 9 % | road_kit `road_HW_*` |
| AR | Arterial boulevard | 2+2 + turn lane | 20 | 60 km/h | 8 % | road_kit `road_AR_*` |
| CO | Local / collector street | 1+1 + parking | 11 | 40 km/h | 10 % | road_kit `road_CO_*` |
| MT | Mountain road | 1+1, no shoulder | 7.5 | 60 km/h | 15 % | road_kit `road_MT_*` |
| RU | Rural road | 1+1 | 7 | 70 km/h | 10 % | road_kit `road_RU_*` |
| GR | Gravel road | single track + passing bays | 5.5 | 50 km/h | 15 % | road_kit `road_GR_*` |
| SV | Service / industrial road | 1+1, wide | 12 | 40 km/h | 5 % | road_kit `road_SV_*` |
| RW | Runway / taxiway (closed course) | open surface | 45 | — km/h | 0.8 % | road_kit `road_RW_*` |

Total network: **165.2 km**. By class: CO 86.0 km, AR 13.5 km, MT 12.0 km, EX 12.0 km, HW 11.4 km, SV 10.7 km, MW 8.9 km, RW 4.4 km, RU 3.1 km, GR 3.1 km.

Lane markings standard: white = lane lines and edge lines, yellow = lines separating opposing traffic (double yellow on undivided highways and arterials, dashed yellow on local/rural roads). Motorway and expressway carriageways are separated by a 0.81 m concrete barrier.

## 2. Named roads

| ID | Name | Class | Length (m) | Elevation range (m) | Max grade (p99) | Districts |
|---|---|---|---|---|---|---|
| R1 | Veyra Ring | MW | 8924 | 8 – 33 | 5.0 % | Downtown, Ironport, Neon Mile, Falcon Airfield |
| C1 | Azure Coastal Expressway | EX | 11996 | 3 – 164 | 8.5 % | Azure Coast, Ironport, Blackridge, Falcon Airfield |
| H2 | Ridgeline Highway | HW | 5165 | 114 – 186 | 9.0 % | Blackridge, Redwood Valley |
| H3 | Redwood Parkway | HW | 4198 | 6 – 139 | 9.0 % | Azure Coast, Westhaven, Redwood Valley |
| H4 | Sable Valley Link | HW | 2013 | 22 – 126 | 9.0 % | Downtown, Westhaven, Redwood Valley |
| A1 | Crown Avenue | AR | 2075 | 3 – 30 | 5.9 % | Downtown |
| A2 | Meridian Boulevard | AR | 2819 | 7 – 21 | 4.6 % | Downtown, Neon Mile, Falcon Airfield |
| A3 | Westhaven Avenue | AR | 1925 | 13 – 19 | 1.3 % | Downtown, Westhaven |
| A4 | Harbor Front Drive | AR | 1884 | 2 – 7 | 0.9 % | Downtown, Azure Coast, Ironport |
| A5 | Port Road | SV | 2522 | 3 – 11 | 5.0 % | Ironport |
| A6 | Airfield Road | AR | 511 | 8 – 21 | 6.9 % | Falcon Airfield |
| A7 | Lumen Avenue ("the Mile") | AR | 1752 | 5 – 24 | 3.7 % | Neon Mile, Falcon Airfield |
| A8 | Marina Drive | AR | 1065 | 4 – 10 | 8.0 % | Downtown, Azure Coast |
| A9 | Ironport Avenue | AR | 1438 | 2 – 5 | 0.9 % | Ironport, Neon Mile |
| A10 | Union Street | CO | 2130 | 5 – 9 | 2.5 % | Downtown, Neon Mile |
| A11 | Falcon Perimeter Road | SV | 1452 | 8 – 8 | 0.1 % | Falcon Airfield |
| A12 | Berth Road | SV | 2348 | 3 – 3 | 0.2 % | Ironport |
| A13 | Sorrel Ridge Road | MT | 3061 | 23 – 199 | 15.0 % | Azure Coast, Redwood Valley |
| A14 | Old Farm Road | GR | 1774 | 103 – 146 | 15.0 % | Redwood Valley |
| A16 | Lake Road | RU | 635 | 125 – 142 | 10.0 % | Redwood Valley |
| A17 | Timber Road | GR | 1332 | 118 – 169 | 15.0 % | Redwood Valley |
| A18 | Halcyon Pass Road | MT | 4388 | 30 – 324 | 15.0 % | Downtown, Blackridge |
| A19 | Summit Road | MT | 808 | 287 – 461 | 15.0 % | Blackridge |
| A20 | Corvo Ridge Road | MT | 3790 | 24 – 461 | 15.0 % | Blackridge |
| A21 | Kestrel Drive | CO | 1265 | 19 – 32 | 5.3 % | Downtown |
| A22 | Regent Street | CO | 1880 | 4 – 27 | 1.9 % | Downtown |
| A26 | Millbrook Main Street | CO | 412 | 117 – 129 | 10.0 % | Redwood Valley |
| A27 | Lantern Lane | CO | 190 | 21 – 28 | 10.0 % | Azure Coast |
| A28 | Kestrel Hill Road | CO | 389 | 63 – 100 | 10.0 % | Blackridge |
| A29 | Foundry Lane | SV | 700 | 3 – 4 | 0.3 % | Ironport |
| A30 | Warehouse Row | SV | 561 | 3 – 4 | 0.4 % | Ironport |
| A31 | Refinery Road | SV | 1585 | 3 – 3 | 0.0 % | Ironport |
| A32 | Mill Street | CO | 230 | 120 – 131 | 10.0 % | Redwood Valley |
| A33 | Marquee Street | CO | 1450 | 7 – 17 | 0.9 % | Neon Mile |
| A34 | Hangar Road | SV | 1004 | 8 – 11 | 2.3 % | Falcon Airfield |
| A35 | Terminal Road | SV | 565 | 8 – 9 | 0.6 % | Falcon Airfield |
| RWY | Falcon Runway 18/36 | RW | 1925 | 8 – 10 | 0.8 % | Falcon Airfield |
| TWA | Taxiway Alpha | RW | 1650 | 8 – 13 | 0.8 % | Falcon Airfield |
| TWL1 | Taxiway link A1 | RW | 180 | 8 – 8 | 0.0 % | Falcon Airfield |
| TWL2 | Taxiway link A3 | RW | 180 | 8 – 8 | 0.0 % | Falcon Airfield |
| TWL3 | Taxiway link A5 | RW | 180 | 9 – 13 | 47.0 % | Falcon Airfield |
| TWL4 | Apron link | RW | 170 | 8 – 8 | 0.0 % | Falcon Airfield |
| TWL5 | South taxiway link | RW | 140 | 8 – 8 | 0.0 % | Falcon Airfield |
| A15 | Mirror Lake Loop | RU | 2494 | 142 – 155 | 2.5 % | Redwood Valley |
| A24 | Coral Street (beach access) | CO | 238 | 5 – 8 | 7.0 % | Azure Coast, Westhaven |
| A23 | Palm Street (beach access) | CO | 324 | 3 – 9 | 7.6 % | Azure Coast, Westhaven |
| A25 | Dune Road (beach access) | CO | 556 | 4 – 10 | 9.5 % | Azure Coast, Westhaven |

Plus **46 generated local streets** (76.9 km) in the Downtown, Neon Mile and Westhaven grids. Named grid streets include Afterhours Street, Arcade Street, Atlas Street, Ballroom Street, Bastion Street, Birch Street, Bishop Street, Cedar Street, Chancery Street, Cinema Street, Coral Street, Dock Gate Street, Dune Road, Elm Street, Encore Street, Exchange Street, Foundry Street, Gateway Drive, Guild Street, Harbour Lane, Hillcrest Road, Jukebox Street, Juniper Lane, Kestrel Terrace, Lantern Alley, Larkspur Road, Laurel Street, Ledger Street, Maple Avenue, Marigold Lane, Marina Gate, Marquee Row, Mint Street, Neon Lane, Oakridge Drive, Palm Street, Pilot Street, Quay Street, Reel Street, Riverside Drive, Seaview Road, Skyline Street, Spire Street, Tower Street, Velvet Row, Wharf Street.

## 3. Structures (bridges, tunnels, viaducts, elevated roads)

| Name | Type | Road | Length (m) | Centre (x, y) |
|---|---|---|---|---|
| Harbor Viaduct | elevated | R1 Veyra Ring | 2655 | (4566, 1700) |
| Kestrel Tunnel | tunnel | R1 Veyra Ring | 600 | (4650, 3642) |
| Lantern Strait Bridge | bridge sea | C1 Azure Coastal Expressway | 1272 | (4535, 395) |
| Gantry Expressway (elevated) | elevated | C1 Azure Coastal Expressway | 2076 | (5900, 1033) |
| Blackridge Tunnel | tunnel | H2 Ridgeline Highway | 1220 | (5090, 5700) |
| Corvo Tunnel | tunnel | H2 Ridgeline Highway | 832 | (6590, 5625) |
| Corvo Creek Viaduct | viaduct | H2 Ridgeline Highway | 310 | (5995, 5700) |
| Highline Viaduct | viaduct | H2 Ridgeline Highway | 306 | (3200, 5650) |
| West Sable Bridge | bridge river | R1 Veyra Ring | 105 | (3250, 3476) |
| Sable Mouth Span (Harbor Viaduct) | bridge river | R1 Veyra Ring | 146 | (4957, 1708) |
| Millbrook Bridge | bridge river | H3 Redwood Parkway | 73 | (1657, 4563) |
| Crown Bridge | bridge river | A1 Crown Avenue | 113 | (4099, 3312) |
| Meridian Bridge | bridge river | A2 Meridian Boulevard | 136 | (4951, 2451) |
| Sable Drawbridge | bridge river | A4 Harbor Front Drive | 149 | (4964, 1624) |
| Union Street Bridge | bridge river | A10 Union Street | 140 | (4944, 2100) |
| Regent Street Bridge | bridge river | A22 Regent Street | 109 | (3700, 3356) |
| Outflow Bridge | bridge river | A15 Mirror Lake Loop | 65 | (1363, 4856) |

## 4. Interchanges and named junctions

| ID | Name | Position | Roads | Type |
|---|---|---|---|---|
| IC-01 | Harbor Interchange | (3299, 1766) | R1 Veyra Ring, A8 Marina Drive | Trumpet interchange; ring to Marina Drive & Azure Coast |
| IC-02 | Westgate Interchange | (3250, 2450) | R1 Veyra Ring, A2 Meridian Boulevard, A3 Westhaven Avenue | Diamond interchange; Westhaven Ave passes under the ring and becomes Meridian Blvd |
| IC-03 | Sable Interchange | (3250, 3300) | R1 Veyra Ring, H4 Sable Valley Link | Half-cloverleaf; ring to the Sable Valley Link |
| IC-04 | Foothill Interchange | (3900, 3620) | R1 Veyra Ring, A1 Crown Avenue, A18 Halcyon Pass Road | Diamond; Crown Ave north becomes Halcyon Pass Road |
| IC-05 | Northgate Interchange | (6015, 3376) | R1 Veyra Ring, A7 Lumen Avenue ("the Mile"), A20 Corvo Ridge Road | Three-level junction at the ring's NE corner; Lumen Ave and Corvo Ridge Road |
| IC-06 | Falcon Interchange | (6040, 2700) | R1 Veyra Ring, A2 Meridian Boulevard, A6 Airfield Road | Diamond; Meridian Blvd to Airfield Road |
| IC-07 | Dockside Interchange | (5946, 1803) | R1 Veyra Ring, C1 Azure Coastal Expressway | Four-level stack; ring meets the coastal expressway. The Undercroft meet sits beneath it |
| IC-08 | Crown Interchange | (4100, 1700) | R1 Veyra Ring, A1 Crown Avenue, A4 Harbor Front Drive | Viaduct slip ramps down to Crown Ave & Harbor Front Drive |
| IC-09 | Gantry Interchange | (5798, 802) | C1 Azure Coastal Expressway, A9 Ironport Avenue, A5 Port Road, A12 Berth Road | Cloverleaf serving the port gates |
| JN-01 | Lantern Junction | (3450, 719) | C1 Azure Coastal Expressway, A8 Marina Drive | Signalised T-junction at the base of Lantern Point |
| JN-02 | Sunset Junction | (1250, 1175) | C1 Azure Coastal Expressway, H3 Redwood Parkway | Large roundabout with palm-planted island |
| JN-03 | Corvo Junction | (7150, 5450) | C1 Azure Coastal Expressway, H2 Ridgeline Highway | Mountain T-junction above the sea cliffs |
| JN-04 | Redwood Junction | (2150, 5150) | H2 Ridgeline Highway, H3 Redwood Parkway, A17 Timber Road | Rural roundabout with timber-frame gas station |
| JN-05 | Millbrook Cross | (1601, 4450) | H3 Redwood Parkway, H4 Sable Valley Link, A16 Lake Road, A26 Millbrook Main Street | Town-centre junction with clock tower |
| JN-06 | Lumen Cross | (5374, 2508) | A7 Lumen Avenue ("the Mile"), A2 Meridian Boulevard | Signalised plaza where the Mile crosses Meridian Blvd |
| JN-07 | Halcyon North Junction | (4300, 5700) | A18 Halcyon Pass Road, H2 Ridgeline Highway | T-junction, west portal side of the Blackridge Tunnel |
| JN-08 | Halcyon Pass Top | (4350, 5050) | A18 Halcyon Pass Road, A19 Summit Road | Saddle junction with gravel summit lot |
| JN-09 | Palm Street Junction | (2121, 1053) | C1 Azure Coastal Expressway, A23 Palm Street (beach access) | Signalised junction, beach access |
| JN-10 | Coral Street Junction | (1070, 1229) | C1 Azure Coastal Expressway, A24 Coral Street (beach access) | Signalised junction, beach access |
| JN-11 | Dune Road Junction | (2842, 948) | C1 Azure Coastal Expressway, A25 Dune Road (beach access) | Signalised junction, beach access |
| JN-12 | Sorrel Junction | (600, 1271) | C1 Azure Coastal Expressway, A13 Sorrel Ridge Road | T-junction at the foot of the Sorrel Hills |
| JN-13 | Perimeter Junction | (7517, 2151) | C1 Azure Coastal Expressway, A11 Falcon Perimeter Road | Airfield south gate junction |
| JN-14 | Port Road North Junction | (7100, 1851) | C1 Azure Coastal Expressway, A5 Port Road | Signalised truck junction by the freight yard |
| JN-15 | Lighthouse Turn | (3700, 560) | C1 Azure Coastal Expressway, A27 Lantern Lane | Right-in/right-out turn to Lantern Lane and the lighthouse |

In addition there are **329 at-grade intersections** (signalised or priority) and **12 grade-separated crossings** where local streets pass under the Harbor Viaduct or the Gantry Expressway.

## 5. District connections (how you drive from one district to another)

- **Crown Downtown ↔ Azure Coast:** A4 Harbor Front Drive; A8 Marina Drive
- **Crown Downtown ↔ Ironport Industrial Zone:** A4 Harbor Front Drive
- **Crown Downtown ↔ Blackridge Mountains:** A18 Halcyon Pass Road; R1 Veyra Ring
- **Crown Downtown ↔ Westhaven Suburbs:** A3 Westhaven Avenue; H4 Sable Valley Link
- **Crown Downtown ↔ Neon Mile:** A10 Union Street; A2 Meridian Boulevard; R1 Veyra Ring
- **Azure Coast ↔ Ironport Industrial Zone:** C1 Azure Coastal Expressway
- **Azure Coast ↔ Westhaven Suburbs:** A23 Palm Street (beach access); A24 Coral Street (beach access); A25 Dune Road (beach access); H3 Redwood Parkway
- **Azure Coast ↔ Redwood Valley:** A13 Sorrel Ridge Road
- **Ironport Industrial Zone ↔ Neon Mile:** A9 Ironport Avenue; R1 Veyra Ring
- **Ironport Industrial Zone ↔ Falcon Airfield:** C1 Azure Coastal Expressway
- **Blackridge Mountains ↔ Neon Mile:** R1 Veyra Ring
- **Blackridge Mountains ↔ Falcon Airfield:** A20 Corvo Ridge Road; C1 Azure Coastal Expressway
- **Blackridge Mountains ↔ Redwood Valley:** H2 Ridgeline Highway
- **Westhaven Suburbs ↔ Redwood Valley:** H3 Redwood Parkway; H4 Sable Valley Link
- **Neon Mile ↔ Falcon Airfield:** A2 Meridian Boulevard; A7 Lumen Avenue ("the Mile"); R1 Veyra Ring

## 6. District boundaries

Boundaries follow physical features so they read naturally in game: the Veyra Ring (R1) encloses Downtown and Neon Mile; the Sable River divides Downtown from Neon Mile and Westhaven from Blackridge; the coastal expressway (C1) separates Ironport from Falcon Airfield. Full polygon coordinates are in `district_specifications.md`.

## 7. Race routes (all on real roads; validated for connectivity)

| ID | Name | Type | Roads in order | Length | Start (x, y) |
|---|---|---|---|---|---|
| RACE-01 | Crown Sprint | Sprint | A2 → A1 → A4 | 2.48 km | (3420, 2450) |
| RACE-02 | Ring Run | Circuit (1 lap) | R1 | 8.92 km | (4500, 1700) |
| RACE-03 | Azure Coast Sprint | Sprint | C1 | 3.12 km | (422, 1212) |
| RACE-04 | Strait Dash | Sprint | A8 → C1 | 3.19 km | (3464, 1316) |
| RACE-05 | Lumen Circuit | Circuit (3 laps) | A7 → A2 → A33 → A10 | 1.59 km | (5197, 2242) |
| RACE-06 | Ironport Industrial Circuit | Circuit (3 laps) | A5 → A12 | 3.66 km | (5901, 775) |
| RACE-07 | Halcyon Ascent | Touge uphill | A18 | 3.24 km | (3938, 3721) |
| RACE-08 | Corvo Descent | Touge downhill | A20 | 3.75 km | (5040, 5249) |
| RACE-09 | Blackridge Traverse | Sprint | H2 | 5.09 km | (7079, 5476) |
| RACE-10 | Redwood Rally Stage | Rally stage | A14 | 1.74 km | (1520, 4295) |
| RACE-11 | Westhaven Sprint | Sprint | A3 → H3 | 2.93 km | (3100, 2450) |
| RACE-12 | Grand Loop | Endurance loop (1 lap) | C1 → H2 → H3 | 20.39 km | (1299, 1161) |
| RACE-13 | Riverside Sprint | Sprint | H4 | 1.90 km | (3149, 3358) |
| RACE-14 | Sorrel Ridge Run | Hill sprint | A13 | 2.96 km | (580, 1364) |
| RACE-15 | Cliffside Run | Sprint | C1 | 3.52 km | (7539, 2255) |
| RACE-16 | Midnight Crown Circuit | Circuit (4 laps) | A22 → A2 → A1 → A4 | 2.41 km | (3700, 2100) |

## 8. Hidden shortcuts

- **SC-01 Rail Spur Cut** (Ironport, 628 m): Drive the freight-rail service track through the yard, skipping Port Road's bends.
- **SC-02 Spire Construction Ramp** (Downtown, 212 m): Ramps and cleared lot of the unfinished Atlas Tower; diagonal cut between Bishop St and Mint St.
- **SC-03 Kestrel Service Tunnel** (Downtown, 223 m): Maintenance adit beside the Kestrel Tunnel, linking Kestrel Drive to the ring's service lane.
- **SC-04 Orchard Track** (Redwood Valley, 336 m): Dirt track between apple rows, linking Redwood Parkway to Old Farm Road.
- **SC-05 Halcyon Firebreak** (Blackridge, 337 m): Steep firebreak that skips hairpin 2 on Halcyon Pass Road. Rough surface.
- **SC-06 Marina Boardwalk** (Azure Coast, 315 m): Timber boardwalk along the marina berths; narrow, cafe furniture to dodge.
- **SC-07 Hangar Run** (Falcon Airfield, 472 m): Through open hangar doors H4-H5 from the apron to Taxiway Alpha.
- **SC-08 Lumen Arcade Alley** (Neon Mile, 212 m): Covered service alley behind the Starlight Theater.

## 9. Validation results

- Roads: 93 polylines (46 generated local streets)
- Total road length: 165.2 km
- At-grade intersections: 329; grade-separated crossings: 12; named interchanges/junctions: 24
- River bridges: 9; structures total: 17
- Locations checked: 160; race routes checked: 16; shortcuts: 8

## Errors (0)
- none
## Warnings (0)
- none
## Notes
- District coverage checked on 16032 land samples (50 m grid): every land point belongs to exactly one district.

Additional 3D checks: every road profile is grade-limited per class (table above). Tunnels keep 42–210 m of rock cover in their central 80 %. Bridge decks clear the water by at least 4.5 m, and the Lantern Strait Bridge has 52 m navigation clearance. The terrain is cut and filled to every at-grade road (max cut 60 m at tunnel portals, max fill 33 m).
