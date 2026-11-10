# VEYRA CITY: visual and driving iteration log (GTA V / Asphalt / Cyberpunk benchmark)

**Started:** 2026-11-10 · **Method:** each iteration captures the same 8 in-game scenes from the Dev player
(`VeyraCity.exe -veyraIterShots <dir>`, scripted road-following drive, see `GameFlow.IterationShots`), scores them
against the rubric below, picks the biggest gaps, fixes them, rebuilds and captures again. Evidence:
`Screenshots/<date>_iter<NN>/` (contact sheet `sheet.png`) and the `[Iter]` telemetry line in the player log.

## Result after 15 iterations (2026-11-10)
| | Iteration 1 | Iteration 15 |
|---|---|---|
| Mean rubric score (0–5) | 1.94 | 3.60 |
| Sky | flat gradient (1) | drifting cloud decks, haze, night city glow, stars, moon (3) |
| Street life | empty kerbs (1) | traffic that stops at reds and turns, parked cars, walkers, bus-stop crowds, zebra crossings (4.5) |
| Street detail | trees and lamps only (1) | hydrants, bins, benches, meters, mailboxes, shelters, signs, crossings, stop lines, steam vents (4) |
| Night lighting | faint lamps (2) | headlight pools from player, racers and nearby traffic, tail-light spill, streaks (4) |
| Handling spread (skidpad) | every car 1.05–1.18 g | truck 0.83 g … hypercars 1.22 g |
| Launch from spawn | 0–100 km/h not reached in 7 s (wheelspin) | 5.7 s, no wheelspin |
| Frame time (visual scenes) | p99 37.5 ms with exceptions | p95 10–15 ms per scene, 0 exceptions |
| EditMode tests | 80 | 90 (all green) |

Still open: the cruise shows render-side hitches outside this work's code (most likely first-use shader/PSO compilation on
D3D12; proposed to the shader session as a GraphicsStateCollection warm-up); wet roads that read as frost, dark noon street
canyons and the street-lamp light pool belong to the lighting cards L1–L5; the five rebadged real-brand cars need a design
legal check before release; the Sketchfab token in the untracked `Tools/download_fancy_cars.py` must be revoked (SEC-003).

## Reference material
Official store screenshots viewed for comparison (not copied into the project):
- **GTA V Enhanced** (Steam app 3240220, 11 shots): dusk skyline with haze and lit freeway traffic; rain-wet night
  street mirroring neon and police lights; red sedan at street level with deep contact shadow, worn asphalt, kerbside
  detail; golden-hour haze over the city; dense flowering grass.
- **Cyberpunk 2077** (Steam app 1091500, 26 shots): night market street with orange/pink lit volumetric haze, neon sign
  shapes, string lights, overhead walkways, wet ground; car chase with smoke and sparks; neon-wheeled bike on wet road.
- **Asphalt Legends** (Steam app 1815780, 6 shots): low chase camera, strong ground motion blur, glossy cars with sharp
  sky reflections, bright saturated daylight and clouds, nitro flames, rival cars close, colourful barriers and banners.
- Written research: `Docs/VISUAL_BENCHMARK_REPORT.md` (rendering, density and systems of GTA V and Cyberpunk) and the
  handling research behind `VehiclePhysics/HandlingFeel.cs` (GTA V handling.meta, Asphalt nitro/drift, arcade techniques).

## Rubric (0 = missing, 1 = placeholder, 2 = basic, 3 = decent, 4 = good, 5 = reference level)
| # | Criterion | Reference look |
|---|---|---|
| R1 | Car look | Glossy clear-coat paint reflecting the street, dark contact shadow, believable wheels, glowing lamps |
| R2 | Speed sensation | Low wide camera, FOV and blur at speed, streaks, nitro flames and trails (Asphalt) |
| R3 | Wet night | Road mirrors lamps, neon and tail lights; spray; rain streaks (GTA, Cyberpunk) |
| R4 | Night lighting richness | Varied lit windows, neon shapes, light pools on the road, coloured spill |
| R5 | Atmosphere | Day haze and aerial perspective; lit coloured fog at night; dusk sky colour |
| R6 | Sky | Clouds, sun glow, moon and stars, dusk gradient |
| R7 | Street life | Traffic near the player, parked cars, variety of models |
| R8 | Street detail | Props, signs, decals, road wear |
| R9 | Drift and effects | Smoke, skid marks, sparks, angle readability |
| R10 | Frame time | p99 ≤ 16.6 ms at 1440p (Dev player as a proxy) |

## Iterations

### Iteration 1 (2026-11-10): reflections, contact shadow, camera, traffic density
**Changes:** live reflection probe on the player car (`CarReflectionProbe`, one cube face per frame, car on layer 30 so it
does not reflect itself); soft contact shadow under every car (`CarContactShadow`); chase camera lower and closer
(5.5 m / 1.6 m, look-ahead 3 m); traffic up to 64 cars spawning from 55 m out of view; audit fixes BUG-077 (dampers ∝ mass),
078 (clear-coat toggle), 080 (smoke colour), 081 (per-renderer flame colour), 082 (rate-independent filters), 083
(frame-rate-independent kerb shake), 084 (nitro torque cap), 085 (thread-safe audio noise), 086 (tests use the shipped feel),
088 (wheels in every LOD level), 089 (tuning version only bumps on change). Also: the 5 rebadged CC-BY cars imported.
**Capture:** `Screenshots/2026-11-10_iter01/sheet.png`. Telemetry: noon 73 km/h, nitro run 172 km/h, drift held at −33°.
**Scores:** R1 car 3.5 · R2 speed 3 · R3 wet night 1.5 · R4 night lights 2 · R5 atmosphere 1.5 · R6 sky 1 · R7 street life 1 ·
R8 street detail 1 · R9 drift/effects 3 · R10 frame time n/a (run polluted by an exception, see below).
**Found:** `ParkedCars` threw every frame (MaterialPropertyBlock created in a static initialiser, BUG-091), so no parked cars and a
red error console; nitro speed streaks far too dense (they filled the sky); rain wetness carried into the day shots; no
traffic visible on the street being driven (spawns spread over side streets); noon frames dark and the sky flat (lighting card,
reported to the F1/L session).

### Iteration 2 (2026-11-10): street life, lamps, subtler speed effects
**Changes:** BUG-091 fixed (`ParkedCars` creates its property block lazily); kerbside parked cars on collector, arterial and
service roads (`ParkedCars`: 7,401 deterministic slots, 42 % occupied, pooled within 140 m, knocked loose when hit);
traffic spawns half its cars on the player's own road 90–260 m ahead/behind and may appear in view beyond 140 m (was 220 m);
lamp anchors from the Blender pipeline (31 of 34 models measured, 3 estimated) and `CarLampFX` (head/tail glows, brake flare,
night tail-light streaks, headlight beam cones) on player, AI and traffic cars; junction traffic signals (`TrafficLights`: 38 sites,
mast arm + pole head, cycling red/amber/green); speed streaks reduced (rate 160→70, thinner, outer ring only, from 160 km/h);
motion blur switched to camera + objects so the car stays sharp; camera roll into turns; day scenes reset to dry roads.
**Capture:** `Screenshots/2026-11-10_iter02/sheet.png`. 0 exceptions; avg 98.8 FPS, p99 25.0 ms (was 37.5), 23 hitches > 50 ms
(spawns and teleports between scenes).
**Scores:** R1 car 3.5 · R2 speed 3.5 · R3 wet night 1.5 · R4 night lights 2 · R5 atmosphere 1.5 · R6 sky 1 · R7 street life 2.5 ·
R8 street detail 1.5 · R9 drift/effects 3 · R10 frame time 2.
**Found:** traffic and parked cars now read in every street shot (GTA cue); signals exist but none fell in these camera angles;
lamp glows too faint at chase distance; the lighting gaps (dark noon, flat sky, no light pools, glittery wet road) remain with the
L cards.

### Iteration 3 (2026-11-10): crash damage, spray, nitro bursts, camera collision, street trees
**Changes:** dents on hard hits (`CarDamage`: per-car mesh copy, impulse > 2.5 m/s, capped); wet-road spray plumes from the rear
wheels (`CarSprayFX`, scales with speed × wetness); Asphalt-style nitro bursts (`NitroBurstFX`: blue ring + camera kick on a Clean
Boost, magenta shockwave on Overdrive); chase camera sphere-cast so it no longer clips into walls; tree-lined avenues
(`StreetTrees`: 10,068 broadleaf trees every 14 m on collector/arterial kerbs, GPU-instanced, no colliders); stronger lamp glows
(head ×1.4, tail ×1.0 + brake flare); a scripted crash scene (it9) with a damage readout.
**Capture:** `Screenshots/2026-11-10_iter03/sheet.png` (+ it9 crash pair). 0 exceptions; damage after the crash 0.25; avg 81.7 FPS,
p99 50.0 ms, 39 hitches (regression from iteration 2).
**Scores:** R1 car 3.5 · R2 speed 3.5 · R3 wet night 2 · R4 night lights 2.5 · R5 atmosphere 1.5 · R6 sky 1 · R7 street life 3 ·
R8 street detail 2.5 · R9 drift/effects 3.5 · R10 frame time 1.5.
**Found:** the street trees are the biggest single change: the avenues now read like GTA's Vinewood/Rockford streets; tail lamps
glow red at night in the chase view. Problems: frame time regressed (suspect ~1,500 full-detail trees in every shadow cascade);
the "DRIFT LOST" toast sits on top of the car; the crash close-up camera ended up inside the shop front; the night road ahead
of the car stays black (no headlight light on the road: GTA's single strongest night-driving cue); the wet road reads as frost
(peer L card); the kerb faces show UV streaks (BUG-069, W4 card).

### Iteration 4 (2026-11-10): real headlights, frame-time recovery, measurement tooling
**Changes:** the player car and AI racers carry real lights (`CarLampFX.AddRealLights`: a spot above the headlamps that
throws the headlight pool 8–30 m ahead, the GTA night-driving cue, plus a red light behind for tail/brake spill);
`StreetTrees` draws the full mesh with shadows only within 85 m, the low LOD without shadows beyond; toasts moved above the car
(below the drift chain); crash close-up from the road side; per-scene frame timing that skips the first second after each
teleport (`[IterPerf]` line) and A/B switches (`-veyraIterOff trees,probe,parked,lamps,signals`); the capture is now one command
(`Tools/visual_iteration.sh <NN> [--import] [--method …] [--build] [--ab …] [--tests]`).
**Capture:** `Screenshots/2026-11-10_iter04/sheet.png`. 0 exceptions; avg 102.6 FPS, p99 19.7 ms (iteration 3: 50 ms), 20 hitches;
per scene avg 7.6–10.7 ms, p95 11.8–16.8 ms. The A/B runs (`ab_trees`, `ab_probe+parked`) are invalid: an EditMode test run
started on the same machine during them (both came out slower than the baseline); not used.
**Scores:** R1 car 3.5 · R2 speed 3.5 · R3 wet night 2 · R4 night lights 2.5 · R5 atmosphere 1.5 · R6 sky 1 · R7 street life 3 ·
R8 street detail 2.5 · R9 drift/effects 3.5 · R10 frame time 3.
**Found:** the golden-hour scene drove into traffic left over from the noon scene on the same spot (0 km/h, crash smoke);
the tail light (a point light 0.55 m behind the car) washed the whole rear bumper red; the headlight pool is hard to read in the
night chase view; the nitro run slides at 12° (the arcade layer is not holding the car straight under Overdrive);
handling benchmark (new, `Logs/A8/handling_benchmark.txt`): every car corners at 1.05–1.18 g with a 0.09–0.15 s turn-in, so
the 3.6 t truck handles like the hypercars: one global yaw-rate cap (`YawRateLimitG` 1.15) flattens all the cars.

### Iteration 5 (2026-11-10): sky, class handling, crash debris, traffic headlights
**Changes:** a sky layer over the procedural skybox (`Veyra/SkyDome` shader + `SkyDome.cs`, library slot 1050, agreed with
the F/L-card session): drifting fBm cloud decks lit by the sun (golden-hour tint, silver lining, grey overcast and rain decks),
horizon haze in the fog colour, orange city light pollution at night, stars and a moon at the moonlight direction;
handling by class: the arcade yaw-rate cap now scales with tyre grip (`HandlingFeel.ReferenceMu`) and the turn-in with mass,
and every downloaded car got a class tyre grip (truck 0.80 … hypercar 1.20); top speed by gearing instead of drag (BUG-079);
crash debris (paint chips in the car's colour, trim, glass glints, dust puff) on hard hits; real headlight/tail lights on the 6
nearest traffic cars at night; the tail light is now a backwards spot (no more red bumper wash); captures clear the traffic
between scenes and the scripted driver keeps a gap to cars ahead.
**Capture:** `Screenshots/2026-11-10_iter05/sheet.png`. 0 exceptions; avg 103.8 FPS, p99 19.0 ms, 18 hitches. EditMode 87/87.
**Handling benchmark** (`Logs/A8/handling_benchmark.txt`, skidpad g at 80 km/h): truck 0.83 (turn-in 0.18 s), van 0.91,
old sedans/taxis 0.97–1.00, hot rods 0.96, muscle 1.02, GT/sports 1.09–1.17, hypercars 1.21–1.22, Vento 1.25; lift-off at
180 km/h 0.09–0.19 g (hot rods were ~0.4 g). Nitro run slip 12° → 4°.
**Scores:** R1 car 3.5 · R2 speed 3.5 · R3 wet night 2.5 · R4 night lights 2.5 · R5 atmosphere 2.5 · R6 sky 3 · R7 street life 3 ·
R8 street detail 2.5 · R9 drift/effects 3.5 · R10 frame time 3.
**Found:** the clouds are the biggest single jump of the loop (bridge and drift shots now read like GTA's skies); the night sky
has dark cloud, faint stars and a brownish city glow; red tail-light spill now lands on the wet road. Still weak: the headlight
pool does not read on the road ahead in the chase view; the scripted line on the C1 bridge hugs the barrier; pavements are empty
(no pedestrians: GTA's strongest street-level cue).

### Iteration 6 (2026-11-10): pedestrians, headlight pool, cleaner capture lines
**Changes:** sidewalk pedestrians (`Pedestrians`: procedural low-poly walkers, no third-party models; 17,325 pavement
anchors on collector/arterial/service roads, up to 70 around the player, walk cycle, varied height/build/clothes/skin, they
dive clear of a fast car, Need for Speed style, and are never run over); player headlight spot 90 → 230, range 55 → 70 m;
anamorphic streaks through headlights that face the camera at night (Cyberpunk's oncoming-traffic look); the C1 bridge scenes
drive the centre lane; per-scene max slip in the `[Iter]` line.
**Capture:** `Screenshots/2026-11-10_iter06/sheet.png`. 0 exceptions; avg 86.8 FPS, p99 29.2 ms, 25 hitches. Nitro run 172 km/h,
max slip 5° (in lane, no barrier contact). EditMode 86/87: the one failure is the F/L session's new `DebugMissing` library key
(card B3 in progress), not this work.
**Scores:** R1 car 3.5 · R2 speed 3.5 · R3 wet night 2.5 · R4 night lights 3 · R5 atmosphere 2.5 · R6 sky 3 · R7 street life 3.5 ·
R8 street detail 2.5 · R9 drift/effects 3.5 · R10 frame time 2.5.
**Found:** walkers read as people at street distance (silhouette, stride, skin/hair); the player's headlights now light the car
ahead and the road just in front at night. The sequential A/B runs (`ab_peds`, `ab_trees`) came out *slower* than the baseline
again with nothing else running: back-to-back player runs heat the laptop GPU, so later runs are 15–30 % slower and sequential
A/B deltas are meaningless. Iteration 7 switches to toggling the feature inside one run (`-veyraIterAB`). The drift scene peaked
at 62° slip (Asphalt holds ~30–45°); pavements still have nothing on them but trees and lamps.

### Iteration 7 (2026-11-10): street furniture, junction markings, traffic that obeys signals and turns
**Changes:** `StreetFurniture` (procedural, merged per 100 m cell, streamed near the player; 9,147 props): fire hydrants, bins,
parking meters and mailboxes on the kerb line, benches and newspaper boxes at the back of the pavement, bus shelters with a lit
abstract ad panel on arterials, street-name sign posts at block ends, zebra crossings and stop lines at every at-grade junction
mouth; traffic stops at red (and at amber when it can) at the painted stop line, shown by brake lamps that now light whenever a
car slows, and drives on through junctions onto a connected road along a Bézier instead of vanishing at the road end; rain
spray behind the 8 nearest traffic cars (`TrafficSpray`); spray darkened with the light level and stretched along its velocity;
speed streaks from 130 km/h and brighter under nitro; pedestrians carry umbrellas in the rain; crash close-up searches for a
clear viewpoint; in-run A/B switch `-veyraIterAB` (thermal-neutral). Code was compile-checked offline against Unity's own
compiler response files while the shared editor lock was held by the F/L session.
**Capture:** `Screenshots/2026-11-10_iter07/sheet.png` (PC_High tier now uses STP at 0.9 render scale: L1). 0 exceptions;
avg 94.9 FPS, p99 25.0 ms with a Blender batch of the other session loading the CPU. In-run A/B: pedestrians +0.5 ms avg;
furniture within noise (on 14.8 / off 15.7 ms). EditMode 89/89.
**Drift trace** (slip every 0.1 s after the brake tap): 0 → −12 → −26 → −32 → −33° held, then −58° at the barrier contact; the
controller holds the Asphalt-range angle, the 62° peak was the wall.
**Scores:** R1 car 3.5 · R2 speed 3.5 · R3 wet night 1.5 (NaN discs, below) · R4 night lights 3 · R5 atmosphere 2.5 · R6 sky 3 ·
R7 street life 4 · R8 street detail 3.5 · R9 drift/effects 3.5 · R10 frame time 3.
**Found:** the pavements finally read like a lived-in city (hydrant, bins, bench, mailbox, walkers, parked cars in one view).
Regression: the rain scene showed two screen-filling white discs: the umbrella canopy mesh was double-sided on shared vertices,
its recalculated normals cancelled to zero, the lighting produced NaN pixels and bloom spread them. Fixed for iteration 8
(separate vertices per side). The crash close-up still caught the car sliding toward the camera.

### Iteration 8 (2026-11-10): atmosphere and detail batch, hitch instrumentation
**Changes:** steam venting from manhole covers in the lanes (pooled emitters on the nearest vents, dimmed with the light level);
people waiting at bus shelters; umbrella NaN fix (separate vertices per side); minimap restyled toward GTA's radar (slate land,
district tints, sea/river/reservoir in blue, roads near true width with a casing, graded by class); engine smoke from badly
damaged cars; chase camera lags under acceleration and closes in under braking; Asphalt "nitro tunnel" (vignette, chromatic
aberration, slight lens squeeze) on a separate volume whose weight is the boost; car reflection probe 128 → 256 px; headlight
beams brighten in rain; parked cars kept clear of the lane (0.95 m kerb offset), away from structures and steep grades (the
other session's autopilot stuck on them on R_BRIDGES); `-veyraNoParked` / `-veyraNoTraffic` for route validation; parked cars,
furniture cells and new walkers now spawn in small budgeted batches; per-system frame-cost recorder (`Core/FrameCost`,
`[IterCost]`), a 28 s cruise with traffic on the longest arterial (`[IterCruise]`) and a standing-start launch scene.
**Capture:** `Screenshots/2026-11-10_iter08/sheet.png`. 0 exceptions; EditMode 89/89. Whole run (now incl. cruise and launch,
another session's Blender job on the CPU) avg 79.3 FPS, p99 41.8 ms.
**Instrumentation:** a ~5 s freeze on entering free roam came from the one-time slot builders (parked cars 3.5 s, furniture
0.66 s, trees 0.47 s, walkers 0.44 s): `Project(window 0)` scanned every road segment for every sample. In the cruise, the two
worst frames (405 ms, 82 ms) had at most 2.6 ms from any of these systems: the remaining hitches are outside this code
(most likely first-use shader/PSO compilation on D3D12; handed to the shader session). The launch scene reached only 82 km/h in 7 s.
**Scores:** R1 car 3.5 · R2 speed 4 · R3 wet night 3 · R4 night lights 3.5 · R5 atmosphere 3 · R6 sky 3 · R7 street life 4 ·
R8 street detail 4 · R9 drift/effects 3.5 · R10 frame time 2.

### Iteration 9 (2026-11-10): startup freeze, launch diagnosis
**Changes:** binary-search segment lookups in every slot builder (`SegmentAt` instead of `Project` over the whole road); a launch
trace (speed, gear, rpm, applied throttle, rear slip ratio every 0.5 s).
**Capture:** `Screenshots/2026-11-10_iter09/sheet.png`. 0 exceptions; avg 80.2 FPS, p99 41.3 ms (Blender load again).
Startup builders: parked cars 3,488 → 203 ms, furniture 658 → 226 ms, trees 467 → 256 ms, walkers 438 → 120 ms.
**Launch trace:** 0.5 s: 4 km/h, 2nd gear, 5,563 rpm, throttle 0; 1.0–3.0 s: rear slip ratio 6.9 → 2.3 with traction control
holding throttle at 0.13–0.14; recovery only after 3.5 s. Cause: the car is spawned 0.45 m above the road at full throttle, the
wheels spin up in the drop, the automatic upshifts on the free-spinning wheels, and traction control's 25 % floor cannot pull
a spinning wheel back. GTA and Asphalt cars never bog down like this; this is part of the "physics feels wrong" report.
**Scores:** as iteration 8, R10 2.5.

### Iteration 10 (2026-11-10): launch fix, part 1
**Changes:** the automatic gearbox no longer upshifts while the driven wheels spin or are off the ground; traction control may
cut fully on gross wheelspin; new EditMode regression test `DroppedLaunch_DoesNotBogDownInWheelspin` (five cars launched at full
throttle from the game's 0.45 m spawn height must reach 100 km/h within 0.8 s of a settled launch).
**Capture:** `Screenshots/2026-11-10_iter10/sheet.png`. In-game launch 0–100 km/h 6.5 s (iteration 9: not reached in 7 s);
wheelspin cleared by 1.5 s (was 3.5 s). EditMode 89/90: the new test fails as expected (dropped launch 1.1–1.4 s slower):
the slip ratio reads 0 near standstill, so the guard did not see the spin. Avg 76.4 FPS, p99 43.7 ms (Blender load).
**Scores:** as iteration 9.

### Iteration 11 (2026-11-10): launch fix, part 2; drift scene; early slot builds
**Changes:** wheelspin detected by rim speed against road speed (works at standstill) for the upshift guard and traction control;
slot tables built right after the scene loads on staggered frames (behind the menu); drift scene in the middle of the carriageway.
**Capture:** `Screenshots/2026-11-10_iter11/sheet.png`. Launch: the car now stays in 1st, but slip 7.3 → 1.9 still took 1.4 s
at zero throttle: with the clutch locked, the engine's inertia (× gear ratio²) holds the wheels in the spin; 0–100 7.0 s.
The drift is held at 31–44° in open road. The cruise showed traffic fixed-step spikes up to 96 ms: catch-up steps after long
frames multiply the per-step traffic cost.
**Follow-up (verified in EditMode before the next build):** airborne driven wheels get no drive (they land at road speed, as
in GTA/Asphalt), and the automatic slips its clutch during low-speed wheelspin with traction control on; traffic cars cache
their own positions for the 64 × 64 car-following loop.
**Scores:** R1 3.5 · R2 4 · R3 3 · R4 3.5 · R5 3 · R6 3 · R7 4 · R8 4 · R9 4 · R10 2.5.

### Iteration 12 (2026-11-10): launch fix complete, traffic step cost
**Changes:** airborne driven wheels get no drive (they land at road speed); the automatic slips its clutch during low-speed
wheelspin with traction control on; faster traction recovery after a full cut; traffic cars cache their positions for the
car-following loop.
**Capture:** `Screenshots/2026-11-10_iter12/sheet.png`. EditMode 90/90 (the launch regression test is green: a dropped launch is
within 0.3–0.5 s of a settled one). In-game launch: no wheelspin, 1st gear held, 104 km/h at 6.0 s from the spawn drop.
Whole run avg 101.9 FPS, p99 29.9 ms; cruise p99 97 → 42 ms, hitches > 50 ms 70 → 12; traffic fixed-step cost max 96 → 10 ms,
avg 2.24 → 0.64 ms.
**Scores:** R1 3.5 · R2 4 · R3 3 · R4 3.5 · R5 3 · R6 3 · R7 4 · R8 4 · R9 4 · R10 3.

### Iteration 13 (2026-11-10): launch guards limited to low speed
**Changes:** the wheelspin guards (no upshift, full traction cut) apply below 15 m/s only.
**Capture:** EditMode 90/90; launch 0–100 5.7 s; avg 99.6 FPS, p99 33.3 ms. The nitro scene still swerved: the cause was the
capture script, not physics. Once nitro took the car 12 km/h past the script's 160 km/h target, its speed-holder tapped the
brake; a brake tap while steering is the Asphalt drift trigger and braking cancels nitro (BUG-044), so the car drifted into the
barrier on standard nitro.

### Iteration 14 (2026-11-10): nitro scene, zebra crossings
**Changes:** the nitro scene never brakes; walkers cross at the zebra while the cars on that road are held at red, then walk on
along the far pavement (crossing count in the `[Iter]` line).
**Capture:** `Screenshots/2026-11-10_iter14/sheet.png`. EditMode 90/90; Overdrive run 204 km/h with 6° max slip (was 167 km/h
into the barrier); avg 103.8 FPS, p99 27.3 ms; cruise p99 37.8 ms. Crossings 0: walkers start mid-block and short scenes end
before they reach a corner.
**Scores:** R1 3.5 · R2 4 · R3 3 · R4 3.5 · R5 3 · R6 3 · R7 4 · R8 4 · R9 4 · R10 3.

### Iteration 15 (2026-11-10): final pass
**Changes:** a quarter of new walkers start a few metres short of a signalled crossing, heading to it (corners are where GTA's
crowds gather); crash damage read from the crash car; drift captured at full angle before the barrier.
**Capture:** `Screenshots/2026-11-10_iter15/sheet.png`. EditMode 90/90; 0 exceptions. Drift held at −32° at 102 km/h;
Overdrive 204 km/h, 6° max slip; launch 0–100 5.7 s; crash damage 0.24; one walker crossed at a red. Whole run avg 97.0 FPS,
p99 33.9 ms (incl. the cruise and launch); every visual scene p95 10–15 ms; cruise p99 46 ms (render-side hitches, see 8).
**Scores:** R1 3.5 · R2 4 · R3 3 · R4 4 · R5 3 · R6 3 · R7 4.5 · R8 4 · R9 4 · R10 3.

## Round 2 (2026-11-10): buildings, roads, atmosphere, water, map, car detail
New brief from the user: another 20+ passes, now on buildings, roads, building shaders, atmosphere, water, a full-screen
map on M "better than GTA V's", and much more detail in the car's nitro, brake and speed effects. Work split agreed with the
F/L-card session: map, minimap, car FX and HUD, SkyDome and water are this session's; road/facade shaders, fog/exposure and
post profiles move over after that session's V1 commit (add-ons only until then).

### Iteration 16 (2026-11-10): world map v1
**Changes:** `UI/WorldMap.cs` (M / gamepad View, game paused): an aerial render of the real city made once by an
orthographic camera (fog off for that camera only, the player's car excluded) as the base; a road overlay graded by class;
district names zoomed out, street names and landmark names zoomed in; 7 filterable point-of-interest categories from the
world data with colour/letter icons; heading arrow, metric scale bar, north mark; right-click waypoint with a GPS route solved
by `UI/RoadGraph.cs` (426 junction nodes from road ends and centre-line crossings, bridges excluded, A*) and drawn on the map
and on the HUD minimap with distance and ETA; iteration builds now go to `Builds/IterMono` (`Editor/IterationBuild.cs`) so they
never collide with the other session's `Builds/DevMono` players.
**Capture:** `Screenshots/2026-11-10_iter16/sheet_map.png`: aerial base, districts, icons, street names, a 2.15 km magenta
route. Frame times invalid (the other session's player ran on the GPU at the same time).
**Found:** river and sea read near-black from above; mountains flat; the minimap showed faintly through the legend.

### Iteration 17 (2026-11-10): map v2, instrument cluster, brake discs, water, rooftops, sky (build only)
**Changes:** map water layer (sea south of the coastline, river, reservoir) and hillshaded terrain relief from the real
terrain heights, opaque background; new HUD instrument cluster (270° tachometer arc with rpm numerals and red zone, gear in
the arc's gap, 7 shift lights flashing at the cut, G-meter, BRAKE / ABS / TCS / NOS / DRIFT lamps, three-cell nitro gauge in
the mode colour); `CarDetailFX` (brake discs that heat up and glow red→orange→white behind the spokes; tail-lamp light streaks
in the boost colour under nitro and red under hard braking); `Veyra/Water` shader (library slot 800, applied at runtime by
`WaterSurface`): two cross-scrolling ripple layers plus a third chop layer, two swell wave trains, teal scattering at grazing
angles, HDR sun glitter, on the shared Veyra forward pass (sky/probe reflection, Forward+ light reflections, fog);
`RooftopDetails` (stair housings, AC units with fans, New York water tanks on legs, antenna masts with blinking red beacons,
from the UBX building boxes, merged per 250 m cell); sky dome sunset band, high cirrus layer and pink-gold cloud rims;
painted lane arrows before every stop line; capture scenes for the skyline (golden hour, night), the river front and a hard
stop from 200 km/h.
**Fix during the pass:** the new cluster put two `Image` components on one GameObject (gear box, G-meter); the editor tolerated
it, the player got a null and `GameFlow.Start` aborted in the other session's build. Rings moved to child objects; no other
double graphic in the UI files.
**Build:** `Builds/IterMono` built with no shader or script errors (`Logs/A8/iter17_build.log`); material tests 13/13.
**Not captured:** from ~14:27 the GPU driver is wedged (D3D12 "device removed" at the first frame of any player, both
sessions); captures resume after a driver reset (Win+Ctrl+Shift+B) or a restart.

### Iteration 18 (2026-11-10): first round-2 capture after the PC restart
**Capture:** `Screenshots/2026-11-10_iter18/` (sheet, sheet_world, sheet_car, sheet_map). EditMode 93/93, 0 exceptions;
brake disc heat 1.00 after the 200 km/h stop; launch 0–100 5.7 s; nitro run 204 km/h; drift −32° held. Whole run 88.1 FPS,
p99 37.5 ms; cruise p99 50 ms.
**Found:** water and rooftops good; the cluster's dial sat 135 px too low (a `UIKit.Box` pivot is its anchor, so the tach was
placed by its corner) and the nitro hint ran across the speed digits; red brake streaks 30 m long in daylight; the brake-disc
close-up looked at a wall; the riverfront camera sat inside the bank's grass (2 m fallback height); the map route came out
0 m because the skyline scenes had left the car 400 m in the air; long district names overlapped the coordinates; the map
overlay read 16–17 FPS.

### Iteration 19 (2026-11-10): cluster layout, map buildings, capture fixes
**Changes:** cluster rebuilt on centre-placed boxes (600 × 300 panel): dial, throttle/brake pedal bars (GT/Forza telemetry),
shift lights, G-meter, per-wheel brake temperature on a car outline (GT7-style tyre icons: lock-up flashes red, wheelspin
blue), one row of lamps, nitro label + three cells + a hint line (clean-boost window, overdrive cooldown). Brake streaks
0.14 s / 6 cm, at night only (nitro streaks always). Map: building layer from the 2,708 UBX boxes (south-east drop shadows
from height, same light as the hillshade, crisp outlines; faint when zoomed out), hover names for icons, legend counts,
distance tag on the waypoint, a 0.22 s open animation, two-line district title, and the world camera draws nothing while the
opaque map is open. Captures: the car is returned to the road after the skyline scenes; the waypoint snaps to the nearest
road point; river camera over the water; map frame time logged.
**Results:** route 2,170 m, map frame 8.7 ms (115 FPS, was ~60 ms), 0 exceptions, brake heat 0.99, 0–100 5.6 s, run avg
87.5 FPS, cruise p99 41.7 ms.
**Found:** distant towers read as noise: every window drew a high-contrast interior room at every distance and angle;
building UVs are absolute world height (v = y / 14) while downtown stands 4–43 m up, so "ground floor" logic never met the
pavement; the "golden hour" shots ran at 18.3–18.6 h with the sun still 19–23° up (golden light starts below ~22°, strongest
under 10°); the river had the sea's chop; the brake close-up still faced a wall (the car stopped against one).

### Iteration 20 (2026-11-10): facades at street level and at distance, calm inland water
**Changes:** `Veyra/Facade` (`VC_FacadeSurface.hlsl`, no new properties or keywords): ground-floor shopfronts on ~60 % of
the urban punched-window buildings (display glass 0.25–3.0 m above the pavement, a backlit fascia sign in a seeded colour,
14 % behind roller shutters, lit from dusk, a cornice band; the two house facades keep plain ground floors); a slab line at
every floor; TV flicker in ~9 % of lit flats; interiors fade with distance (40–240 m) and grazing angle into sky-reflecting
glass; the window grid anti-aliases to its average coverage and average lit share (per 4 × 4-window block) once a cell is
under ~2 px; panes vary slightly in tint. New `GroundHeightField` (Gameplay): the terrain height over the city baked once into
a 768 × 576 float texture (global `_VC_GroundHeight` / `_VC_GroundHeightRect`), because building UVs are absolute world
height and the shader needs the pavement level; street-level grime now counts floors from it too. Water: `_MacroVariation`
scales the swell per renderer; `WaterSurface` gives the river and reservoir a quarter of the sea's swell and 60 % ripple
strength through a property block (6 inland surfaces). Brake close-up re-parks the car in the nearest road's right-hand lane;
skyline/river beauty shots hide the HUD.
**Results:** build clean (no shader errors), 0 exceptions, route 2,171 m, map frame 8.3 ms, brake heat 0.99, 0–100 5.7 s.
Visual scenes unchanged in cost (8.4–8.9 ms avg); cruise p99 62.5 ms vs 41.7 ms in iteration 19 (second half of the run,
laptop GPU heat-soaked; re-checked next pass).
**Found:** distant towers now read as clean glass instead of noise, but too dark and plain from the skyline; the night skyline
still reads as a wall of lit windows; the river is calm; the tach sits in the red at city speeds because the automatic shifts
at the same rpm whatever the throttle (1st gear at 73 km/h, 6,700 rpm).

### Iteration 21 (2026-11-10): automatic gearbox, speed lines, radar blocks, parapets, true golden hour
**Changes:** the automatic's shift points follow a smoothed pedal load (`Powertrain`: half the upshift rpm and 55 % of the
downshift rpm at a light throttle, the full spec from ~90 % pedal, kickdown when floored; launches, nitro and racing are
unchanged); Asphalt-style radial speed lines over the screen edges (from 170 km/h, strong under nitro, boost-mode colour);
building footprints on the HUD minimap from the map's building layer (GTA-style radar blocks); roof parapets with a dark
coping on the 566 dressed roofs; facade grid anti-aliasing later (cells under ~1.5 px) and two thirds of the lit-window
share at night; golden-hour captures moved to 19.15–19.3 h; capture log now has gear and rpm per shot.
**Results:** 73 km/h cruise now in 3rd at 3,640 rpm (was 1st at 6,700); 0–100 5.7 s with an identical launch trace; nitro
204 km/h; drift −32° held; EditMode **93/93**; 0 exceptions.
**Found:** at 19:18 windows and sky were already 72 % "night" while the sun was 10° up: the shader global `_VC_NightFactor`
is a clock ramp (18:00–20:00) while the lighting follows the sun (sets 20:00); speed lines barely visible at 204 km/h
without nitro (the it5 shot is taken after the meter runs out); the brake close-up car floated 0.4 m; cruise slower
(avg 16.6 ms; whole-run hitches over 50 ms 91).

### Iteration 22 (2026-11-10): shaders follow the sun, GPS turn-by-turn, aerial perspective, shorelines, map declutter
**Changes:** `VeyraGlobals.Apply` takes the lighting's own night factor (WorldEnvironment, sun elevation); the clock ramp
and its tests stay for callers without one. New `AtmosphereScatter`: per frame, after WorldEnvironment writes the fog,
the haze takes the sun's colour when looking towards it (strongest at golden hour, damped by cloud), turns cooler away
from it, and holds a sodium-orange light-pollution glow at night; runs before the sky dome so its horizon matches. GPS:
the route follows the car, re-plans when it is more than 45 m off the line (at most every 2 s, one reused 12 MB buffer),
and a satnav panel over the minimap shows the next turn (arrow, distance, "onto <street>", distance left; metric or
imperial). Water: shallows turn lighter teal over the bed and broken drifting foam laps the banks where the depth is
under ~1.2 m (depth from the ground-height field). Map: icons are decluttered in category priority (no overlaps), names
likewise; faint dashed district borders. Facade: the room ray-cast and its atlas samples are skipped beyond 240 m (explicit
mip from distance so it can sit in a dynamic branch). Rooftop cells are built 8 roofs a frame.
**Results:** GPS "LEFT 1,162 m onto Velvet Row" after closing the map, route 2,171 m, 0 exceptions; launch, drift and
nitro unchanged. The 19.3 h skyline now reads golden: sun-tinted haze, sunlit glass, windows not yet lit.
**Found:** whole-run hitches over 50 ms 127 (55 in iteration 19), cruise p99 66.7 ms with Rooftops spiking to 43.5 ms
(GC suspected: per-roof arrays); the GPS shot's car was still under the launch scene's full-throttle script (burnout
smoke); district names drew under icons.

### Iteration 23 (2026-11-10): capture fixes, mid-boost nitro shot, window light variety, rooftop A/B
**Changes:** a nitro scene captured while boosting (`it19_nitro_fx`, 18.9 h); the brake close-up keeps the car's ride height
when re-parking; speed lines from 150 km/h; the GPS shot binds an idle intent (the launch script's full throttle was still
driving); district names above the icons; lit windows in three temperatures (warm, cool white, LED blue-white) with per-window
brightness; rooftop props without per-roof array garbage; new A/B switches `rooftops` and `atmo`.
**Results:** 0 exceptions; the boost shot shows the whole nitro package at once (orange exhaust flame trails, blue nitro light
streaks, speed lines, the cluster tinted in the boost colour, NOS lamp, cells at 80 %); in-run A/B rooftops on 15.0 ms vs off
15.9 ms (no measurable cost; Rooftops' worst frame 43.5 → 13.5 ms). The A/B run right after the main run averaged 59 FPS vs 82
with identical content: late-run frame times on this laptop are dominated by heat, the short city scenes stay at 8.4–8.7 ms.
**Found:** rain night: wet road and pavement away from the car read as snow-white (they reflect the skybox reflection baked in
the editor, a daytime sky); the cluster's boost tint filled the whole panel; the brake disc glow sits inside the rim barrel;
the night skyline is still an even wall of lit windows.

### Iteration 24 (2026-11-10): road wear, glass-tower lobbies, night occupancy
**Changes:** new `RoadWear` (Gameplay): fills UV1 of the asphalt render meshes around the player (x across the carriageway from
the nearest road centre line, y = lane count) within a frame budget, generates a repair-patch/crack texture, and turns tyre
wear and patches on for the two asphalt materials at runtime (assets untouched, restored in the editor). `Veyra/Road`: wheel
paths follow the real lane count, oil drips down each lane, grit in the gutters where rain pools first. `Veyra/Facade`: a
double-height lit lobby at the foot of every glass tower; lit-window occupancy varies per ~60 m block from nearly dark to busy
(~25 % overall). Brake scene at dusk (19.7 h); boost tint 0.22; disc glow at the wheel's centre plane.
**Results:** EditMode **93/93**, 0 exceptions; RoadWear processed 216 meshes (125,675 vertices), avg 0.24–0.32 ms/frame;
night skyline now mixes dark and busy blocks.
**Found:** the road wear is too subtle to see on the dark asphalt at 1440p (12 % darkening); RoadWear's worst frame 12.6 ms
(the UV upload of a big mesh).

### Iteration 25 (2026-11-10): sky reflection (attempt 1), atlas map view, speed traps, stronger road wear
**Changes:** `SkyReflection` (a 128 px sky-only realtime probe set as the custom default reflection); world map **atlas view**
(V / gamepad right bumper): the radar's district-tinted map with light building blocks under the same roads, labels, icons
and route, as an alternative to the aerial; `SpeedTraps` + HUD banner (camera flash, name, speed, ★ rating at 150/200/250
km/h, session best); road wear and patches made stronger; new capture of the road surface beside the car.
**Results:** 0 exceptions; atlas view works; road wear still not visible; the rain-night road is still white exactly beyond
the car probe's box (the probe-as-custom-reflection had no effect in URP Forward+: it renders one time only and the
generated default texture stays in use); the speed-trap scene found no camera (the data's `road` field holds street names,
not ids).

### Iteration 26 (2026-11-10): speed traps working, roof finishes, road instrumentation
**Changes:** speed-trap scene takes the road nearest the camera; sky probe renders in one frame (25 renders, still no effect on
the reflection); roof finishes on the dressed roofs (pale gravel 32 %, white membrane 23 %, green roof 11 %, the dark membrane
otherwise) and rows of tilted solar panels on 30 % of the big roofs; the road shot logs the renderers, materials, wear values
and UV1 state under the car.
**Results:** speed trap "Strait Camera 219 km/h ★★☆ NEW BEST" with the flash and banner; 0 exceptions; under the car:
M_Asphalt_New (wear 0.6, patches 0.5, UV1 381/381) and M_Asphalt_Worn (wear 1.0, patches 0.85, UV1 882/882), so values and UVs
arrive. The skyline roofs now vary in colour with solar rows and parapets.
**Found:** the asphalt's albedo detail is hidden at grazing angles by the environment reflection (URP's grazing reflectance
at smoothness 0.38 reaches ~0.4, of a stale daytime sky); the brake-disc glow still does not read through this car's rims.

### Iteration 27 (2026-11-10): lens droplets, damage icon, reflection attempt 2, top-down road check
**Changes:** rain beads on the camera lens (procedural droplets with bright rims and dark cores, some already run; fade in
with the rain, thin out at speed as the wind clears them, run down faster the faster you go); the cluster's car icon is now a
status icon: the body tints white → red with damage and the label reads "DAMAGE nn %"; `SkyReflection` regenerates the default
reflection with `DynamicGI.UpdateEnvironment`; a straight-down road capture.
**Results:** 0 exceptions; droplets read well in the rain-night shot; the top-down shot shows plain asphalt with no wear,
gutters or patches at all, and the rain-night pavement is still white beyond the car's probe (the default environment is
not what Forward+ samples there).
**Found:** the street under the capture car is drawn by overlapping chunks including `M_Concrete_Deck`, the city's most used
road surface (358 meshes), which RoadWear skipped (asphalt only).

### Iteration 28 (2026-11-10): concrete road deck in RoadWear, city-wide sky probe, nitro gain pop-ups
**Changes:** RoadWear also takes the concrete road deck (861 meshes, 3 materials); `SkyReflection` becomes one realtime
sky-only probe whose box covers the whole city (low importance); Asphalt-style nitro gain pop-ups by the gauge ("+12 %
DRIFT", "+5 % NEAR MISS", rising and fading; passive regeneration excluded); the road shot logs the collider under the car.
**Results:** EditMode **93/93**, 0 exceptions; the surface under the capture car is `Collider_asphalt` with wear values and
UV1 present, yet the top-down shot is still plain; the rain-night pavement is still white beyond the car probe.
**Found:** the sky probe had a culling mask of 0, which URP evidently never renders (the car probe, same API, works); the
road effects were written as small absolute colour shifts (patch fill 0.02 linear, wear −24 %) on an asphalt that is almost
black and, in the capture street, in building shade, so they cannot read; RoadWear's worst frame 23 ms (fresh vertex and UV
arrays per mesh).

### Iteration 29 (2026-11-10): HUD performance, night water glow, probe fixes, relative road detail
**Changes:** HUD texts are rebuilt only when what they show changes (speed, gear, G, nitro label and hint, GPS), and the
cluster, minimap, GPS panel and the full-screen effects sit on nested canvases so each re-batches on its own; the water
mirrors a warm city glow low on the horizon at night with broken light streaks; a night river capture; the sky probe renders
an unused layer (a culling mask of 0 never rendered); the car probe's box grows to 360 m; road patches and cracks darken
relative to the asphalt, wheel paths −38 %, paler gutter dust; RoadWear reuses one vertex and one UV list.
**Results:** 0 exceptions; GPS, speed trap and road wear (273 meshes) fine. Frame times **not comparable**: the other session
announced it was taking the machine for its V1 builds at the end of this run, and every system slowed together (TrafficPhysics
avg 1.40 ms vs ~1.0, cruise p99 142.6 ms); no build or player of mine runs until it releases.
**Found:** the wet-night white band did not move with a 360 m car probe, so it is not a reflection-probe problem: it is the
moon's specular on near-mirror wet surfaces (0.98 smooth puddles) scattered by rain-ripple normals, which alias into a white
glitter carpet beyond ~10 m; the night river shows the same blue-white glitter; the top-down road is still plain.
**Prepared, compile-checked, not yet run:** ripple and bump detail on wet roads fade beyond ~10 m and wet gloss is capped
(0.86, puddles 0.93); water normals flatten with distance; a 10 m control patch with hand-made UVs and the worn asphalt in the
top-down shot (shader vs mesh); in-run A/B switches `roadwear`, `skyrefl`, `carprobe`, `hud`.
