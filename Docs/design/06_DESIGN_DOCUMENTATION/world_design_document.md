# MIDNIGHT DISTRICT — Veyra City World Design Document

**Version 1.0 · concept-to-production handoff · Android (Unity) open-world driving game**

This document is the master reference for the world of Veyra City. The geography, roads and every location ID are canonical. They come from the validated world data that also generated the maps, the 3D assets and the location registry, so all numbers here match the files in this package.

| Companion document | Purpose |
|---|---|
| `district_specifications.md` | Per-district detail, boundary polygons, 3D production specs |
| `road_network.md` | Road hierarchy, every named road, structures, junctions, race routes, validation |
| `location_registry.csv` | 209 locations with world, normalised and Unity coordinates |
| `art_direction_guide.md` | Visual identity, materials, lighting, markings, camera standards |
| `visual_quality_roadmap.md` | BLOCKOUT → GREYBOX → ART PASS → FINAL pipeline, Android budgets |
| `asset_status_registry.csv` | Stage of every asset in the package |
| `image_generation_prompts.md` | Prompts for the concept images that could not be generated here |

---

## 1. Vision

**The city is the main character; racing is one of many ways to experience it.** Veyra is a believable modern coastal metropolis. Its geography (a sheltered bay, a river, a mountain range and a working port) explains why its car culture looks the way it does. Every district feels different in daylight, at golden hour and at night. Every road was placed for a reason before any race used it.

### Design pillars
1. **Believable place first.** Real proportions, real road engineering (grades, curve radii, interchanges, bridges, tunnels), a city that makes economic sense.
2. **Every road is worth driving.** The network is designed for flow, sightlines and variety: the coastal sunset run, the downtown canyon, the touge, the gravel stage and the top-speed runway.
3. **Culture you can see.** Five crews, each owning a district, a look, a meet spot and a discipline.
4. **Freedom of progression.** Explore, deliver, photograph, restore, race or just cruise; all of them pay.
5. **Mobile-first craft.** Visual ambition delivered through art direction, lighting and smart LOD, not raw polygon count. See the roadmap for budgets.

### Refinements made to the original brief
| Brief | Refinement | Reason |
|---|---|---|
| Azure Coast on the eastern **and** southern coast; Ironport to the south-east | Azure Coast is the continuous **south-western** coastline from Sorrel Cape to Lantern Point and the marina. The east coast belongs to Ironport and Falcon Airfield. | Keeps the luxury coast contiguous instead of cut in half by the port. Ports and airfields need flat reclaimed land, which the east coast supplies. The south-west-facing coast gives sunsets over the sea along the C1. |
| Crown Downtown "near the central waterfront" | Downtown sits on the north shore of the enclosed **Crown Bay**, whose mouth is spanned by the 1.27 km **Lantern Strait Bridge** | Creates a postcard skyline across water and a landmark bridge, and closes the coastal expressway loop. |
| Neon Mile "adjacent to Downtown" | Across the **Sable River** from Downtown, linked by three bridges; its spine is **Lumen Avenue, a 1.57 km diagonal "Mile"** | Neon reflecting on the river; a natural boundary; the name means something. |
| Mountain heights unspecified | Blackridge summit **470 m**, Halcyon Pass **330 m**; every road respects its grade limit (motorway 5 %, highway 9 %, touge 15 %) | Real roads cannot climb 800 m in 3 km. Relief is tuned so driving is plausible and the views still work. |
| One ring highway | **Veyra Ring (R1, 8.9 km)** around Downtown and Neon Mile, with the elevated **Harbor Viaduct** and the **Kestrel Tunnel**, plus an **outer loop** (C1 + H2 + H3, 20.4 km) through every outer district | Supports both city circuits and an endurance "Grand Loop" with no dead ends. |

---

## 2. Story and soul

Veyra grew around a river mouth. The **Sable River** drains Mirror Lake in the north-western valley, runs along the foot of the Blackridge range and meets the sea in Crown Bay. The first wharves became **Ironport**, the warehouses beside them became nightclubs (**Neon Mile**), and the money built **Crown Downtown**. The suburbs spread west (**Westhaven**), the wealthy took the sunset coast (**Azure Coast**), farms and timber held the valley (**Redwood Valley**), the old airport closed (**Falcon Airfield**), and the mountain roads above it all became legend (**Blackridge**).

By day it is a working, beautiful city. After sunset the parking levels under Exchange Tower fill with tuned cars, the Undercroft beneath the Dockside Interchange glows sodium-orange, and headlights snake up Halcyon Pass.

**The player** arrives with a tired silver **Arden Kite** (a 2004, 140 hp rear-drive coupe, fictional) and rents a single brick lock-up at **GAR-01 Juniper Lane Garage** in Westhaven. Nobody knows them. There is no obligation to become a racer. The city opens through any combination of exploring, working, building and competing.

---

## 3. Geography at a glance

- **World size:** 8.0 × 6.0 km playable (48 km²), 1 km streaming grid (columns A–H west→east, rows 1–6 north→south). The world is bounded by sea on the south and east, the Sorrel Hills on the west and a northern ridge.
- **Coordinates:** metres from the south-west corner (x east, y north). Normalised = (x/8000, y/6000). Unity = (x − 4000, elevation, y − 3000).
- **Water:** Calder Sea, Crown Bay (enclosed harbour, 1.16 km mouth at the Lantern Strait), Sable River (34 m wide at the lake, 120 m at the mouth), Mirror Lake (150 m above sea level).
- **Relief:** sea level to 470 m at Blackridge Summit. Kestrel Hill (125 m) stands inside the city above the ring tunnel. The Sorrel Hills rise to about 275 m.

| # | District | Position | Land area | Character |
|---|---|---|---|---|
| 1 | Crown Downtown | Centre, north shore of Crown Bay | 3.5 km² | Financial district, viaduct, river bridges |
| 2 | Azure Coast | South-west coast | 2.0 km² | Beaches, villas, marina, sunset C1 |
| 3 | Ironport Industrial Zone | South-east | 3.8 km² | Container port, refinery, Iron Wolves |
| 4 | Blackridge Mountains | North / north-east | 12.1 km² | Touge passes, tunnels, viewpoints |
| 5 | Westhaven Suburbs | West | 6.0 km² | Starter area, houses, plaza |
| 6 | Neon Mile | East of Downtown across the river | 2.0 km² | Nightlife, the Mile, rooftop meets |
| 7 | Falcon Airfield | East | 3.6 km² | Legal drags, top speed, big meets |
| 8 | Redwood Valley | North-west | 7.1 km² | Farms, Millbrook, Mirror Lake, gravel |

---

## 4. Road network summary

165 km of road in a single connected network (validated: 0 disconnected roads, 0 unplanned crossings, every water crossing is a bridge). Key routes:

- **R1 Veyra Ring** (motorway, 8.9 km): Harbor Viaduct (elevated along the waterfront), Sable Mouth span, Kestrel Tunnel, interchanges IC-01 to IC-08.
- **C1 Azure Coastal Expressway** (12.0 km): Sorrel Cape → beaches → Lantern Point → **Lantern Strait Bridge** → Gantry Expressway (elevated) → Dockside Interchange → east coast → Cliffside climb → Corvo Junction.
- **H2 Ridgeline Highway** (5.2 km): Corvo Tunnel → Corvo Creek Viaduct → Blackridge Tunnel → Highline Viaduct → Hollow Pines → Redwood Junction.
- **H3 Redwood Parkway** and **H4 Sable Valley Link** connect the valley, Millbrook and the suburbs.
- **Mountain roads:** Halcyon Pass Road (4.4 km, 10 hairpins), Summit Road, Corvo Ridge Road (8 switchbacks), Sorrel Ridge Road.
- **Urban spines:** Crown Avenue, Meridian Boulevard, Lumen Avenue ("the Mile"), Harbor Front Drive, Westhaven Avenue, Marina Drive, Ironport Avenue, Port Road, Berth Road.

Full detail is in `road_network.md`.

---

## 5. Car culture: the five crews

| Crew | Home turf & HQ | Discipline | Look | Cars (fictional) | Meets | Reputation ladder |
|---|---|---|---|---|---|---|
| **The Nightshift** | Downtown & Neon Mile · **GAR-04 Level B4** under Exchange Tower | Street sprints through traffic, point-to-point at night | Charcoal and sodium amber, discreet single pinstripe; workwear jackets, reflective tape | Sleeper sedans and turbo hatches (Volkhart 4S, Arden Strata) | MEET-03 Crown Underground P3, MEET-04 Vantage Rooftop, MEET-10 Lumen Cross | Day Shift → Swing Shift → Graveyard → Foreman |
| **Iron Wolves** | Ironport · **GAR-02 Foundry 9** | Drag racing, big single-turbo builds | Oxidised orange, gunmetal, hazard chevrons; overalls, steel-toe boots | Muscle coupes and boxy turbo imports (Rourke Hammerhead) | MEET-01 The Undercroft, MEET-02 Warehouse Row | Rivet → Bolt → Forge → Anvil |
| **Apex Collective** | Blackridge · **GAR-05 The Apex Shed** | Touge, drifting, precision | Forest green and white, minimal numbers; technical outdoor wear | Light rear-drive coupes (Arden Kite, Sato-style roadsters) | MEET-06 Halcyon Summit Lot | Clean Line → Fast Line → Perfect Line → Apex |
| **Coastline Society** | Azure Coast · **GAR-08 Marina Concourse Club** | Concours, coastal cruises, GT runs | Pearl, sand and navy, gold script; linen and loafers | GTs, convertibles, restored classics (Marlowe Sovereign, Castellan Riva Spider) | MEET-05 Marina Concourse, MEET-08 Sunset Beach Lot | Guest → Member → Patron → Commodore |
| **Redline Syndicate** | Citywide elite · **GAR-09 The Redline Vault** under Veyra Spire | Everything, at the highest level (Redline Series) | Deep crimson and bare carbon, no logos; tailored black | Top-tier builds (Castellan Vento R, Volkhart RS-Evo) | MEET-12 Redline Invitationals (invite only) | Contender → Seeded → Ranked → Syndicate |

**Progression link:** reaching the top rank in any two crews earns an invitation to the Redline Syndicate. Crew challenges start at each HQ. Crew reputation unlocks crew-specific parts, liveries and garage décor.

---

## 6. Activities (all 17 brief activities, mapped to real locations)

| # | Activity | Where (location IDs) | Rewards |
|---|---|---|---|
| 1 | Offline street races vs AI | RACE-01…16 (sprints, circuits, touge, rally, endurance) | Cash, rep, parts |
| 2 | Drag races | DRG-01 Falcon Dragway (legal), DRG-02 Berth Road Straight, DRG-03 Blackridge Tunnel Roll | Cash, Iron Wolves rep |
| 3 | Drift challenges | DFT-01 Corvo Switchbacks, DFT-02 Halcyon Hairpins, DFT-03 Warehouse Row Yard, DFT-04 Falcon Apron Pad, DFT-05 Hollow Pines Logging Yard | Style points → cash, Apex rep |
| 4 | Time trials | TT-01…06 (Halcyon, Falcon handling course, top-speed run, acceleration test, Ring time attack, gravel) | Cash, leaderboard |
| 5 | Speed traps | STP-01…10 (cameras on viaduct, bridge, tunnels, highways) | Stars, cash |
| 6 | Speed zones | SPZ-01…05 (average-speed sections) | Stars, cash |
| 7 | Delivery missions | DEL-01…05 (Westhaven Depot is the starter) | Cash, XP |
| 8 | Vehicle photography | PHO-01…10 (Spire Reflection, Bridge at Sunset, Neon Rain…) | XP, liveries |
| 9 | Scenic viewpoints | VP-01…11 (VP-01 Skyline Overlook is the signature) | XP, map reveal |
| 10 | Hidden collectibles | COL-01…10 collection areas (badges, tags, signs, gnomes, cairns) | Parts, décor |
| 11 | Stunt jumps | JMP-01…09 | Stars, cash |
| 12 | Car restoration | RST-01…06 barn finds, GAR-07 Community Garage | Classic cars |
| 13 | Daily driving challenges | Rotating objectives anywhere (e.g., "300 m of drift on Corvo Ridge Road") | Daily cash |
| 14 | Garage discovery | RST-01…06 + GAR-10, GAR-11 | Storage, cars |
| 15 | Online multiplayer racing | Any RACE route; lobbies at MEET-07, MEET-08, MEET-09 | Cash, rep |
| 16 | Player car meets | MEET-07 Falcon Field Meet, MEET-09 Westgate Retail Park (10–20 players) | Social, photo |
| 17 | Crew challenges | At each crew HQ (GAR-02, -04, -05, -08, -09) | Crew rep |

---

## 7. Progression and economy

- **Currencies:** Cash (earned everywhere) and Reputation (per crew plus global "Street Cred"). XP gives player level, which gates dealership tiers.
- **Early game (Westhaven):** tutorial deliveries (DEL-01), first race RACE-11 Westhaven Sprint, first upgrade at GAR-07, first used car at DLR-01 Second Gear Motors.
- **Mid game:** the city opens through crew introductions. Ring Run, Strait Dash, Halcyon Ascent and Falcon Dragway are the "gate" events of their crews.
- **Late game:** Redline Series, the Grand Loop endurance race, the exotics at GAR-03 Crown Motor Gallery, high-end personal garage GAR-06.
- **No pay-to-win requirement:** every car and part is earnable offline.

---

## 8. Garages

| Environment (brief) | Location | Description |
|---|---|---|
| Small residential starter garage | GAR-01 Juniper Lane Garage | Single brick lock-up 4 × 7 m, roller door, pegboard, one work light |
| Industrial tuning workshop | GAR-02 Foundry 9 | Ex-steel foundry, sawtooth roof, dyno cell, fabrication bay |
| Luxury downtown showroom | GAR-03 Crown Motor Gallery | Glass showroom, turntable, polished concrete |
| Underground performance garage | GAR-04 Level B4 | Four levels down, sodium tubes, tool chests, alignment rack |
| Specialised drift workshop | GAR-05 The Apex Shed | Timber and concrete, tyre wall, alignment rack, mountain view |
| High-end personal garage | GAR-06 Kestrel Heights Penthouse Garage | Car lift to the penthouse, skyline display wall |
| Plus | GAR-07 Community Garage, GAR-08 Marina Club, GAR-09 Redline Vault, GAR-10 Falcon Hangar 3, GAR-11 Hollow Pines Service | Crew and event garages |

---

## 9. Customisation and the before/after story

The hero car is the **Arden Kite**: a stock silver coupe (4.30 × 1.78 × 1.30 m, 2.55 m wheelbase) that becomes the **Kite street build** (1.90 m wide, 50 mm lower, 18-inch wheels). Both exist as greybox models in `09_ART_PASS_BENCHMARK/assets/vehicles_traffic_and_hero_greybox.glb` with matching proportions.

Visible upgrade stages: (1) wheels and tyres plus lowering → (2) front splitter, side skirts, rear diffuser → (3) widebody fender flares → (4) rear wing, vented hood, carbon parts → (5) livery, custom headlights and tail-light bar → (6) brakes and exhaust visible through wheels and diffuser. Semi-racing builds, aggressive but street-legal in spirit. Every part is a separate mesh slot on a shared skeleton so the Android build only loads what is fitted.

---

## 10. Time of day, weather and life

- **Day/night cycle:** 24 game minutes per day. Key looks: morning haze, crisp midday, golden hour (signature), blue hour, night.
- **Weather:** clear, overcast, light rain (wet roads at night are a Neon Mile signature), coastal fog in Crown Bay and Hollow Creek gorge.
- **Ambient life:** traffic on arterials and motorways, parked cars in the urban grids, buses on Meridian, trucks in Ironport, flights of gulls on the coast, cranes moving containers, event crowds at the airfield.

---

## 11. Multiplayer (optional, 10–20 players)

Shared-world lobbies of up to 20 players spawn at MEET-07, MEET-08 or MEET-09. Players can join any race route, host meets with photo mode, and run crew challenges together. The world state (time, weather) is host-driven. The traffic density in multiplayer is reduced 40 % for performance.

---

## 12. Map production and validation (stages 2–4 of the brief)

1. **Plan:** district placement, coastline, river and lake, road hierarchy and the location registry, all written as data (one source of truth).
2. **Generate:** master map (vector cartography rendered at 6320 × 3600 px), satellite render (4800 × 3600) and legend, all drawn from that data.
3. **Validate (automated):** road graph connectivity, every crossing classified as junction or grade separation, roads never in water except on bridges, every location inside its district and reachable by road, race routes drivable in order, full district coverage, 3D grades, tunnel cover and bridge clearance. Result: **0 errors, 0 warnings.**
4. **Consistency:** the satellite map, 3D blockouts, art-pass benchmark, heightmap and registry all derive from the same data, so geographic relationships cannot drift between deliverables.
