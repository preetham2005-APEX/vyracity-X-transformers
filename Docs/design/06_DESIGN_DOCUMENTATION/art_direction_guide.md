# Veyra City — Art Direction Guide

**Target:** a believable, near-future modern coastal city. The quality bar is the premium open-world racing genre (Forza Horizon / Need for Speed / CarX Street level of polish) with its own identity, delivered within Android budgets.

> **Status reminder:** nothing in this package is FINAL art. The maps are final as cartography. The 3D content is BLOCKOUT, with an ART PASS prototype for Crown Downtown and Blackridge. See `visual_quality_roadmap.md`.

---

## 1. Visual pillars

1. **Real before cool.** Real proportions, real engineering, real materials. Futuristic touches (twisting towers, LED crown lighting, parametric facades, clean signage) sit on a believable modern city.
2. **Light is the hero.** Every district is designed for three looks: day, golden hour and night. Composition, silhouettes and reflections are judged at all three.
3. **Distinct districts, one city.** Shared road markings, signage, street furniture and car designs tie the districts together; architecture, palette and lighting tell them apart.
4. **Composition over clutter.** Detail where the camera lives: kerbs, junctions, storefronts, guardrails and vegetation edges. Restraint everywhere else.
5. **Mobile honesty.** Achieve the look through art direction, baked lighting, trim sheets and LOD, not polygon count.

### Avoid
Cartoon shading · cyberpunk excess (no neon on every surface, no flying cars, no holograms) · oversaturated colour · floating roads or objects · impossible junctions · empty streets · identical repeating buildings · overly futuristic cars · inconsistent scale or architecture between views.

---

## 2. Global standards

### Coordinates and scale
1 unit = 1 m. Floor heights: office 3.8–4.2 m, residential 3.1 m, retail ground floor 4.8 m. Lane width 3.25–3.65 m. Kerb 0.15 m. Sidewalk 3 m. Car 4.3–4.8 m long. Person 1.75 m.

### Road markings and signage (Veyra standard)
- **White:** lane lines (3 m dash / 9 m gap on fast roads, 3 / 6 on local roads), edge lines, stop lines, zebra crossings.
- **Yellow:** separates opposing traffic. Double yellow on undivided highways and arterials, dashed yellow on local and rural roads. On divided motorways and expressways, a yellow edge line runs along the median.
- **Motorway signs:** dark green panels with white text on steel gantries, route shields R1 (red-orange), C1 (orange), H2–H4 (yellow), matching the master map.
- **District wayfinding:** white sign with a 10 cm stripe in the district colour.
- **Mountain roads:** white/black delineator posts every 25 m, W-beam guardrail on every valley side, chevron boards on hairpins.

### Street furniture family
Double-arm LED street lamp (10 m, 4000 K) in urban areas · warm coastal lantern (6 m, 2700 K) on the Azure Coast · sodium high-mast (25 m, 2200 K) in Ironport · signal poles with mast arms · bus shelters in glass and steel · timber benches with steel frames · black steel bollards.

### Vehicles
Fictional brands only: **Arden** (Japanese-inspired), **Volkhart** (German), **Marlowe** (British GT), **Castellan** (Italian exotic), **Rourke** (American muscle), plus generic traffic. Real-world proportions, believable panel gaps and lighting signatures. No overly futuristic shapes. Hero cars use clear-coat paint with a flake option, real carbon weave, multi-layer headlights and textured tyres.

---

## 3. District palettes and identities

| District | Palette | Materials | Day | Golden hour | Night |
|---|---|---|---|---|---|
| Crown Downtown | Steel blue, silver, bronze, limestone | Curtain wall, limestone, granite, dark metal | Crisp, reflective canyons | Bronze Spire glow, warm haze over the bay | Mixed warm/cool windows, white crown bands, LED pools |
| Azure Coast | Turquoise, sand, white stucco, palm green | Stucco, teak, glass balustrades | Bright, high-key | **Signature:** sun over the sea along C1 | Warm lanterns, pier lights |
| Ironport | Rust orange, gunmetal, container colours | Painted steel, concrete, corrugated metal | Hazy, dusty | Silhouetted cranes | Sodium orange, flare glow, crane beacons |
| Blackridge | Pine green, granite grey, dry gold grass | Conifers, rock, asphalt, timber | Dappled forest light | Low sun through trees; skyline at the overlook | No street lighting; reflective delineators only |
| Westhaven | Warm ochre, brick red, lawn green | Brick, clapboard, asphalt shingles | Lived-in, friendly | Long shadows across lawns | Porch lights, TV glow |
| Neon Mile | Brick, magenta/cyan/amber neon (on the Mile only) | Brick, painted signage, wet asphalt | Faded signage | Transition: signs flicker on | **Signature:** neon on wet roads |
| Falcon Airfield | Slate grey, runway white, hazard yellow | Concrete, corrugated hangars | Heat haze | Long runway shadows | Event floodlights, staging lights |
| Redwood Valley | Olive, wheat gold, barn red, lake blue | Timber, brick, gravel | Pastoral | Golden fields, dust | Dark countryside, farmhouse lights |

---

## 4. Lighting

- **Sun:** azimuth 245° (WSW) for the reference golden hour; elevation 30° for the "hero day" look and 15° for golden hour. Shadows fall toward the east-north-east, so the downtown avenues get alternating light and shade.
- **Atmosphere:** height fog (denser below 150 m), sun-tinted in-scattering at golden hour, layered backdrop ridges beyond the world edge.
- **Night:** office windows about 30 % lit (residential about 45 %, storefronts 85 %), warm/cool mix; street-light pools as projected cookies; emissive signage plus bloom (threshold 0.8); wet roads with puddle masks.
- **Exposure and grading:** ACES-style filmic curve, gentle S-curve, cool shadows and warm highlights, light vignette. Never crush the blacks on mobile screens.

## 5. Materials (PBR, Unity URP)

| Surface | Albedo | Smoothness | Metallic | Notes |
|---|---|---|---|---|
| Asphalt dry / wet | 0.30 sRGB grey with grain and patch noise | 0.10 / 0.75 | 0 | Tyre-wear lanes, patch repairs, puddle mask |
| Concrete / sidewalk | 0.55–0.62 | 0.20 | 0 | Slab joints every 1.6 m |
| Curtain-wall glass | Tinted blue/silver/green/bronze | 0.92 | 0.7 | Mullion grid 1.5 × 3.8 m; interior parallax at LOD0 |
| Limestone | 0.78 warm | 0.25 | 0 | Punched windows 60 % of bay |
| Brick | 0.48–0.56 red/brown | 0.15 | 0 | Course height 75 mm |
| Painted steel (port) | Container colours | 0.35 | 0.3 | Edge wear, rust streaks |
| Car paint | Per livery | 0.85 clear-coat | 0.5 | Flake normal optional |
| Foliage | Dark/mid/light greens | 0.10 | 0 | Translucency 0.3, wind sway vertex animation |
| Water (sea/bay) | Deep teal | 0.95 | 0 | Two scrolling normal maps, reflection probe |

**Trim sheets:** one 2048² facade atlas per district family (Downtown glass/stone, Coast stucco, Industrial metal, Suburban brick/siding) plus a shared road and kerb atlas. This is the single biggest lever for mobile quality.

## 6. Camera standards (used for every review image)

| Camera | Height / distance | FOV | Use |
|---|---|---|---|
| Driving chase | 7–7.5 m behind, 2.0–2.2 m up, look 40 m ahead | 60–64° | Default gameplay review |
| Bumper / close | 0.6 m up at the front bumper | 75° | Speed sensation check |
| Pedestrian close-up | 1.75 m | 60–65° | Material and detail check |
| Cinematic establishing | 15–60 m up, 0.5–2 km from subject | 45–55° | Skyline and composition |
| Aerial | 600–1200 m up, 40–55° pitch | 40–48° | Layout and massing |

## 7. Consistency checklist (run on every screenshot)
☐ Scale: cars, doors, lanes and people read correctly · ☐ No floating or sunk objects · ☐ Road markings follow the Veyra standard · ☐ District palette correct · ☐ Landmarks in the right map positions relative to each other (check against the master map) · ☐ Lighting matches the time of day · ☐ Nothing missing or untextured · ☐ Horizon has depth (no empty edge of world).
