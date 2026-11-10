# VEYRA CITY: visual and driving iteration log (GTA V / Asphalt / Cyberpunk benchmark)

**Started:** 2026-11-10 · **Method:** each iteration captures the same 8 in-game scenes from the Dev player
(`VeyraCity.exe -veyraIterShots <dir>`, scripted road-following drive, see `GameFlow.IterationShots`), scores them
against the rubric below, picks the biggest gaps, fixes them, rebuilds and captures again. Evidence:
`Screenshots/<date>_iter<NN>/` (contact sheet `sheet.png`) and the `[Iter]` telemetry line in the player log.

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
