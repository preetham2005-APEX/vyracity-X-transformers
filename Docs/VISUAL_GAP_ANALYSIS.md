# Veyra City — visual gap analysis vs AAA benchmark

**Date:** 2026-11-13
**Baseline:** `Screenshots/2026-11-12_iter41/` (Dev Mono, RTX 4070 Laptop, 2560x1440, PC_High)
**Benchmark:** `ReferenceImages/08_GTAV_VideoCapture_2026-11-12/` — read for composition, geometry,
prop density, wear patterns and infrastructure only. **Colour, black level, contrast, saturation and
noise in those frames are not trustworthy** (compressed video captures); no grading decision is taken
from them.

Baseline frames used: `it1_noon_city.png`, `it2_golden_hour.png`.
Benchmark frame used: `VC_17_Day_RetailStreet_Awnings_Shopfronts.png`.

---

## What is already at benchmark

Recording this so it does not get "fixed" by accident:

- **Golden hour** (`it2`) is genuinely strong — warm low sun down the street canyon, correct long
  shadows, believable sun disc occlusion behind the tower. 96 FPS / 10.4 ms.
- **Road marking language** is correct and matches the rule in CLAUDE.md: white lane and edge lines,
  double yellow separating opposing traffic.
- **Tree-lined avenue structure** reads correctly at the right spacing and height.
- **Traffic and parked cars** are present and varied in colour down the full length of the block.
- **Wet/clearcoat car shading** holds up at this distance.

---

## Gaps, in priority order

Priority = (visual impact) / (frame cost). The first two cost nothing per frame.

### 1. Shadows crush to black — no ambient bounce floor  *(Layer 7, zero frame cost)*

**Observed:** In `it1_noon_city`, the left colonnade, the tree shade on both footways and the base of
every building go to near-black. Detail that exists in the geometry is not readable.

**Benchmark:** In `VC_17`, shaded shopfronts, awning undersides and the recessed doorways all stay
legible. Shadow is a *darker version of the material*, not an absence of it.

**Cause:** `RenderSettings.ambientMode = Trilight` with a day ground term of `(0.30, 0.29, 0.27)`
against a sun at intensity 1.9. In a street canyon almost every surface is lit by the ground and
equator terms alone, so the ground term is doing all the work and it is too dark.

**Fix:** raise the ground and equator ambient terms for the day/clear case in `WorldEnvironment.cs`.
This is a uniform-value change — no extra draw calls, no extra lights. **Single highest-value change
available.**

**Risk:** these values are heavily iterated (cards L2/L3/L5, BUG-059/060/128). Must be A/B captured
against this baseline, not changed blind.

### 2. Road surface is uniform  *(Layer 2 — assets in flight)*

**Observed:** VC asphalt is one flat dark grey, with faint cracks only near the car.

**Benchmark:** the reference carriageway shows polished lighter wheel-track lanes, a darker greasy
centre strip, patch repairs with visible seam edges, and grime accumulating against the kerb line.

**Status:** decal atlases and flush surface meshes assigned to the road-infrastructure session
(`ArtSource/Blender/RoadWear/`). Decals cost fill rate, not CPU, and GPU has headroom
(p95 9.1–9.9 ms against budget).

### 3. Sky washes out at noon  *(Layer 7, zero frame cost)*

**Observed:** the `it1` sky is flat pale grey-blue top to bottom.

**Benchmark:** deep saturated zenith grading down to pale haze only near the horizon.

**Cause:** the horizon fog colour is being carried too far up the sky. Candidate: the
`FogAltitudeFactor` thinning curve and the day fog colour `(0.66, 0.72, 0.80)`.

### 4. Ground-plane contact is too clean  *(Layer 2)*

Sidewalks are untextured flat concrete; no panel joints, no tonal variation, no dirt line where the
footway meets the kerb. The reference has all three. Covered by the same decal work as #2.

### 5. Facades are flat  *(Layer 3 — assets in flight)*

VC towers are flat curtain-wall boxes with no recess. The reference has projecting signage, awnings,
recessed entrances and varied parapet heights. Assigned: D1 tower set and D6 low-rise set, with real
window openings, to the building-kit session.

### 6. Street furniture density  *(Layer 5 — assets in flight)*

VC has trees and bins. The reference carries continuous kerbside furniture — poles, meters, hydrants,
boxes, signage — down the whole block. 16 props delivered in `ArtSource/Blender/Street/`.
**Blocked on the spawn fix below.**

---

## Blocker on everything in #2, #5, #6: scatter spawn is not amortised

From `Logs/A8/iter41_player.log`:

| Subsystem | max ms (spawn frame) | avg ms |
|---|---|---|
| StreetTrees | 225.1 | 0.07 |
| StreetFurniture | 205.3 | 0.09 |
| ParkedCars | 166.5 | 0.06 |
| Pedestrians | 93.3 | 0.17 |
| `Streamer.CellLoaded` callbacks | 26.1 | **8.04 (every frame)** |

Worst frame **2,037 ms** = StreetTrees 225.1 + GC. Cruise on road A2: **p99 63.2 ms, 21 frames > 50 ms**
against a 16.6 ms p99 budget.

The average cost of each scatter system is negligible; the entire cost lands in the single frame where
a cell loads. **The fix is time-slicing the spawn across frames, not using fewer props.** Until that
lands, every prop added by the three authoring sessions makes the hitch worse, so placement must wait
for it. Recorded against BUG-170.

---

## Order of work

1. Amortise cell-load scatter spawn (unblocks all prop placement, fixes BUG-170 hitches).
2. Ambient bounce floor + sky gradient — A/B captured against `2026-11-12_iter41`.
3. Road wear decals + flush surface meshes.
4. Place infra, furniture, vegetation and the building kit behind the amortised spawner.

**All four need the Unity Editor licence restored — see PROJECT_STATUS.**
