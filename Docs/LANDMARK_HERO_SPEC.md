# Landmark hero assets — the 11 outstanding records

`BuildingAssembler` places a kit model on 2,690 of 2,708 building records. Eighteen keep their
generated box shell, and **11 of those are landmarks held back deliberately**:

> `BuildingAssembler.cs:87` — `if (b.StyleKit == "Landmark") { Count(noFit, "landmark (A4 hero pending)"); continue; }`

A landmark is not something the kit should fit. Stretching a generic tower to 342 m would give
Veyra its most prominent building as a scaled-up office block. These need bespoke models.

## What they are

Matched by position between `world_building_placements.csv` (style_kit `Landmark`) and
`location_registry.csv` (category `Landmark`). Nine matched at **0 m** — the same point in both
files, so the identification is certain, not inferred.

| Footprint (m) | Height | Registry | Name | District |
|---|---|---|---|---|
| 52 × 52 | **342 m** | LM-01 | Veyra Spire | D1 |
| 58 × 46 | 165 m | LM-03 | Exchange Tower | D1 |
| 48 × 34 | 95 m | LM-21 | Grand Lumen Hotel | D6 |
| 62 × 40 | 24 m | LM-20 | Starlight Theater | D6 |
| 9 × 9 | 28 m | LM-06 | Lantern Point Lighthouse | D2 |
| 10 × 10 | 34 m | LM-13 | Falcon Control Tower | D7 |
| 7 × 7 | 24 m | LM-26 | Millbrook Clock Tower | D8 |
| 6 × 6 | **120 m** | LM-14 | Blackridge Summit Masts (tall) | D4 |
| 6 × 6 | 85 m | LM-14 | Blackridge Summit Masts (short) | D4 |

### Two NOT identified — do not guess

| Footprint | Height | District | Position (x, y) | Nearest registry entry |
|---|---|---|---|---|
| 72 × 46 | 27 m | D6 | 5760, 2400 | LM-20 Starlight Theater, **439 m away** |
| 60 × 28 | 14 m | D1 | 4250, 2010 | LM-03 Exchange Tower, **272 m away** |

Neither is within a plausible distance of any registry landmark, so either they are landmarks
with no registry entry, or they carry `style_kit = Landmark` by mistake. **Resolve this against
the design data before authoring either one** — building the wrong thing at a named location is
worse than leaving the shell. A 72 × 46 m, 27 m D6 building reads like a large retail or
entertainment box; a 60 × 28 m, 14 m D1 building reads like a civic or transport hall.

## What the registry says each one is

From `location_registry.csv` descriptions — these are the design intent and should drive the
model, not the box dimensions:

- **LM-01 Veyra Spire** — 342 m glass-and-bronze tower, tallest in Veyra, aircraft warning lights.
- **LM-03 Exchange Tower** — stepped 1980s stone-and-glass office tower, garage at level B4.
- **LM-21 Grand Lumen Hotel** — 28-storey hotel, rooftop pool bar overlooking the Mile.
- **LM-20 Starlight Theater** — art-deco movie palace with a **30 m vertical marquee sign**.
- **LM-06 Lantern Point Lighthouse** — white stone lighthouse on the headland.
- **LM-13 Falcon Control Tower** — 1960s concrete control tower, now race control.
- **LM-26 Millbrook Clock Tower** — brick clock tower at the centre of Millbrook.
- **LM-14 Blackridge Summit Masts** — red-and-white broadcast masts on the 812 m summit,
  visible city-wide. Two records, 120 m and 85 m.

## Constraints for whoever authors these

- **Originality.** CLAUDE.md: no ripped or copied assets, names, logos or layouts. These are the
  most recognisable buildings in the game and the most tempting to base on a real tower. Don't.
- **The footprint and height in the CSV are the authority.** A hero model is fitted to its record
  like any other, so it must be built to those dimensions, not to a nicer proportion.
- **Export per CLAUDE.md's S4 presets** (`VC_Unity_Prop`), LOD0/1/2, collision prefixes.
  `COL_<surface>_<name>` for anything drivable — see `Docs/SURFACE_CONVENTION.md`.
- **Materials** resolve through `Tools/Blender/material_map.json` onto library slots, so reuse
  existing names where possible — see `Docs/MATERIAL_REGISTER.md`. New names need rules, and
  rules carrying a `category` field are inert for kit models.
- **The masts are not buildings.** 6 × 6 m at 120 m is a lattice structure; it needs an
  openwork silhouette rather than a box, and it is visible from across the map, so its LOD2 and
  any impostor matter more than its LOD0.
- **The Spire is the skyline.** At 342 m against a city whose next tallest is 165 m, it is the
  one asset that defines Veyra's silhouette from every district.

## Status

Not started. No hero asset exists for any of the 11; all 11 currently stand as generated box
shells, which is the correct interim state rather than a defect.
