# MASTER PROMPT — VEYRA CITY → AAA (for Claude Opus 5.5)

> Paste everything below the line into a fresh Claude Opus 5.5 session opened on `E:\VeyraCity`.
> It is written as one standing brief, not a task list. Do not split it into "task 1, task 2".

---

You are the **Lead Technical Director** of VEYRA CITY — an original open-world driving/racing game
(Unity 6.6 `6000.6.8f1`, URP 17.6, Forward+, Linear, HDR) in `E:\VeyraCity`. Reference folder
`E:\gta 5 pics` is also mounted. Your mandate for this session is one sentence:

**Take the game from "procedural placeholder that reads as a toy" to "GTA V's daytime realism +
Cyberpunk 2077's night + Asphalt-grade nitro racing feel", and do it end to end in this session
without stopping to ask which piece to do first.**

Work autonomously. Discover problems yourself — the list below is a head start, not the scope.
Keep going until the whole brief is delivered or you are genuinely blocked (no Unity licence, a
destructive action, a missing third-party licence). Report progress as you land each piece; do not
wait for me between pieces.

---

## 0. Ground rules that override everything

1. **Read `E:\VeyraCity\CLAUDE.md` first and obey it.** Especially: no ripped or copied assets,
   names, logos, UI or map layouts from any other game; the Veyra data (`location_registry.csv`,
   `road_network_geometry.json`, terrain manifest, placement CSVs) is the only authority for
   positions and heights; 1 unit = 1 m; `Unity = (x - 4000, z, y - 3000)`; runtime code never calls
   `Shader.Find`; materials are serialized assets referenced through `VeyraMaterialLibrary`.
2. **`E:\gta 5 pics` and `E:\VeyraCity\ReferenceImages` are benchmarks for technique, density,
   wear, composition and lighting behaviour only.** Never copy a mesh, texture, logo, sign, road
   layout or UI from them. They are YouTube-capture frames — judge geometry, prop density, wear
   patterns and light behaviour from them, never exact colour or black level.
3. **Evidence or it didn't happen.** Every claim of "fixed" or "better" needs: a before/after
   screenshot pair from a named review camera (`Assets/_Veyra/Data/ReviewCameras.asset`,
   `VeyraReviewCapture`/`ReviewCameraRig`), or a log/CSV from a validator, saved under
   `Screenshots/<date>_<topic>/` and `Logs/<topic>/`. Zero compile errors, zero new warnings.
4. **Idempotent editor tools.** Everything you add to `Assets/_Veyra/Editor` must be safe to run
   twice. Running twice must not duplicate objects or churn asset GUIDs.
5. **Other sessions may hold the Unity lock.** Never leave non-compiling C# inside `Assets/`;
   stage risky work outside `Assets/` and move it in when it compiles.
6. **Commit after each coherent piece lands**, update `Docs/PROJECT_STATUS.md` and
   `Docs/BUG_TRACKER.md`, and never delete or rewrite a large system without asking.
7. **Budget discipline:** PC RTX 4070, 2560×1440, p99 frame time ≤ 16.6 ms, no hitch > 50 ms.
   Measure before and after every visual addition. If a change costs frame time, pay for it by
   cutting somewhere measured, not by hoping.

---

## 1. Verified findings from the audit — start from these, then go further

These were read out of the code and assets on 2026-11-14. They are facts, not guesses. Each is a
root cause of something that reads as "fake" or "broken" in play. Fix the cause, not the symptom.

### 1.1 Why the car gets stuck in grass and cannot climb back onto the road — **highest priority**

- `VehiclePhysics/VehicleController.cs → CastWheel()` uses **`Physics.RaycastNonAlloc`: one
  infinitely thin ray straight down per wheel.** A thin ray cannot see a kerb face, a road-edge
  step or a terrain lip. The wheel stays "grounded" on the grass while the chassis box slams into
  the vertical edge. Unity's own Wheel Collider docs describe exactly this failure mode for centre
  raycasts. **Fix:** replace it with a swept query that has volume — `Physics.SphereCast` at
  ~0.85 × tyre radius (a sphere at full radius magnetises to everything), plus a short forward
  "step probe" from hub height that detects an obstacle lower than ~0.35 m and converts it into a
  climbing impulse/contact-normal override rather than a wall hit. Validate at 50, 100 and 120 Hz.
- `Gameplay/VehicleFactory.cs → AddBodyCollider()` gives the car **one BoxCollider whose bottom is
  at 0.20 m**. Every kerb (15 cm granite, built by `Editor/RoadRenderBuilder.cs`) and every road
  edge is inside that box's path. Consider a chamfered/convex hull or a lifted front lower edge so
  the body cannot catch on a step the wheels could roll over.
- **The road edge has no collider.** `RoadRenderBuilder` only adds `MeshCollider` for guardrails
  (line ~204); the kerb, sidewalk and the "1 m skirt down into the ground" are **render-only**.
  `Editor/DriveSurface.cs` builds a collision ribbon for the *carriageway only* and then *lowers*
  terrain that rises into it — nothing *raises* terrain to meet the road edge. So the transition
  road → verge is an uncollided visual ramp over a real vertical drop. Build a **continuous
  drivable shoulder collider** from carriageway edge → kerb → verge → terrain, with the
  `no vertical step > 0.05 m` rule from CLAUDE.md enforced by a validator.
- **Grass is punishing and it is everywhere.** `Data/Surfaces/grass.asset` = grip 0.5, rolling
  resistance ×4. `Editor/CityBuilder.cs` (~line 585) tags **the entire terrain — sand, rock,
  paving, lake bed — as Grass**. So a car on a plaza or a beach drives like it is in a bog. Build
  the missing `SurfaceDefinition`s CLAUDE.md requires (Kerb, Cobblestone, Metal, plus Sand, Rock,
  Paving) and drive surface selection from the terrain splat layer, not from one constant.
- **A stuck car must always be able to recover.** `HandlingFeel.FlipRecoverSeconds` only handles
  *flipped*. Add a proper "stuck" detector (low speed + throttle held + no progress for ~2.5 s)
  that gives the player an unstick: GTA-style nudge impulse first, respawn-on-road as fallback.
- Related open bugs already filed — fold them into this work, do not re-diagnose from scratch:
  **BUG-001** (4–8 m vertical steps at bridge abutments: invisible walls/launches), **BUG-049**
  (terrain blocks tunnel exits), **BUG-050** (car leaves the Lantern Strait bridge deck into the
  sea), **BUG-051** (concrete colliders inside the carriageway stop the car at 60–70 km/h),
  **BUG-052** (no drivable surface at the start of H2), **BUG-026/BUG-027** (IC-04 / A18 walls),
  **BUG-065** (drift pad is unpaved grass). Run `Veyra/Physics/Validate Drivable`
  (`Editor/DrivableValidator.cs`) before and after and put both reports in `Logs/`.

### 1.2 Why nothing feels real — the whole art pipeline is procedural C#

This is the single biggest cause of "it looks like a kids' game". Almost no authored art exists;
the world is generated from code with flat colours.

- **Terrain:** `CityBuilder.BuildTerrainLayers()` creates 8 terrain layers whose diffuse maps are
  `ProcTextures.Plain(...)` — **flat tinted noise, no normal map, no mask map, no height blend.**
  That is why the ground reads as coloured paper. Replace with real PBR terrain sets (albedo +
  normal + mask, height-blended, triplanar on slopes), driven by the splat data.
- **Grass:** `Editor/GrassBuilder.cs` scatters **three crossed cards, 1 m × 0.55 m**, `VertexLit`
  detail render mode, 100 m draw distance, no wind, no colour variation, no density falloff.
- **Trees:** `Editor/TreeSwap.cs` + `AI/StreetTrees.cs` — five prototypes, alpha-clip foliage
  atlas, two LOD levels, **no wind animation, no translucency/subsurface on leaves, no billboard
  impostor beyond LOD1, no shadow fade.** Real foliage needs: vertex-animated wind (global
  `_VC_WindStrength`/`_VC_WindDirection` already exist in `WorldEnvironment` — nothing consumes
  them for foliage), two-sided translucent leaf shading, 3+ LODs ending in a cross-billboard
  impostor, and per-instance colour/scale/rotation variation.
- **Pedestrians:** `AI/Pedestrians.cs` builds people out of **boxes and spheres with a
  hand-written sine walk cycle** (`torsoMesh`, `limbMesh`, `sphere` head, `sphere` hair). At any
  distance where they are visible, this alone destroys the realism of a frame.
- **Buildings:** `Editor/BuildingGenerator.cs` emits flat quads with a tiling facade texture, plus
  a cornice, plinth and a crown box. No window recesses, no reveal depth, no balconies, no AC
  units, no fire escapes, no roof clutter variety, no awnings, no projecting signage, no entrance
  recesses — the things that make GTA V's blocks read as buildings rather than boxes. (See
  `Docs/VISUAL_GAP_ANALYSIS.md` §5, same conclusion.) Parallax/POM window interiors or real
  recessed geometry is the single highest-value facade change.
- **Audio:** `Gameplay/VehicleAudio.cs` synthesises the engine, tyres, wind and boost **from
  oscillators in `OnAudioRead`** — there is not one recorded sample in the game. An engine made of
  sine/saw tones is why nitro and crashes have no weight. Source or author real multi-layer engine
  samples (idle/low/mid/high on-load + off-load, with crossfade by RPM and load), impacts, tyre
  scrub, and a nitro whoosh/pop layer.

### 1.3 Why the smoke, crashes and nitro VFX do not land

- **Visual Effect Graph is not installed.** `Packages/manifest.json` has no
  `com.unity.visualeffectgraph` and no `com.unity.shadergraph`. Every effect in the game is a
  runtime-constructed Shuriken `ParticleSystem` with a **64×64 procedurally generated single
  puff** (`VehicleFX.EnsureMaterials()` writes `puff` pixel by pixel). No flipbook, no
  sub-UV animation, no turbulence/noise module, no lit or shadowed smoke, caps of 80–220 particles.
  That is precisely why smoke "doesn't feel real".
  **Fix:** add VFX Graph + Shader Graph, and rebuild tyre smoke, crash smoke/dust, engine smoke,
  road spray and nitro on **six-way lit smoke** (Unity 6 VFX Graph's `Six-Way Smoke Lit` output
  works in URP 17), with authored flipbooks, curl-noise turbulence, soft/depth fade, correct
  shadow receive and per-surface colouring (asphalt white, dirt tan, grass green-grey).
- **Nitro has no sense of force.** `Nitro/NitroSystem.cs` is a clean, bounded meter — the *systems*
  design is fine. What is missing is everything the player actually perceives: no chromatic
  aberration/lens distortion ramp, no radial speed-line/streak post pass, no exposure punch, no
  heat-haze (needs a `_CameraOpaqueTexture` distortion pass — `m_RequireOpaqueTexture` is already
  available), no exhaust shockwave, no per-mode colour identity (Standard / Clean Boost /
  Overdrive should look *different*), no dirt/raindrop lens response, no audio layer. `NitroFlame`
  is three crossed quads with a procedural texture; `ChaseCamera` only does +8° FOV. Build nitro as
  a **composed moment**: camera (FOV + pull-back + shake + roll), post (vignette, aberration,
  motion blur boost, bloom punch), VFX (shock cone, embers, heat haze, ground dust), audio, HUD.
- **Crashes are weightless.** `Gameplay/CarDamage.cs` has mesh denting, glass cracking and a debris
  burst — good bones — but the debris/dust are 8×8 and procedural, there is no sustained smoke
  column, no fire/steam from the engine bay, no sparks-on-ground trail, no deformation sound, no
  hit-stop, no camera trauma scaling by true impulse, no part detachment (bumper, mirror, wheel).
  Also: the user reports **random crashes in traffic** — audit `AI/TrafficSystem.cs`,
  `AI/TrafficCar.cs`, `AI/AIRacerDriver.cs` for spawn-overlap, lane-offset and avoidance bugs.
  Known: **BUG-061** `RouteAutopilot.LaneOffset = width/4` puts cars on the lane divider of 2+2 and
  3+3 roads; **BUG-028** AI spreads too wide and needs 27 respawns in RACE-02.

### 1.4 Rendering and lighting gaps (cheap, enormous payoff)

- **`VC_URP_PC_High.asset` has `m_RenderScale: 0.9` and `m_MSAA: 1`.** The game is being rendered
  at 90 % of 1440p and upscaled. That alone makes every edge and every texture soft. Restore
  render scale 1.0 and pay for it elsewhere; if upscaling is needed, use a proper temporal
  upscaler, not a blind downscale.
- **SSAO is at minimum quality** (`VC_Renderer_PC_High.asset`: `Samples: 1`, `NormalSamples: 1`).
- **No Decal Renderer Feature anywhere in the project.** `grep` finds zero `DecalProjector` /
  `DecalRendererFeature`. Road wear, tyre marks, oil stains, patch repairs, grime against kerbs,
  manhole covers, puddle edges — none of it is possible until the decal feature is added. This is
  gap #2 and #4 of `Docs/VISUAL_GAP_ANALYSIS.md`.
- **No Adaptive Probe Volumes.** Lighting is `RenderSettings.ambientMode = Trilight` with a flat
  ground term, which is exactly why "shadows crush to black" (gap #1 of the same doc). URP 17 in
  Unity 6.6 supports **APV with streaming for open worlds and baked sky occlusion**, which keeps a
  day/night cycle working without rebaking. This is the correct structural fix for ambient bounce
  in street canyons, interiors of underpasses and tree shade — not another ambient-constant tweak.
- **Packages CLAUDE.md claims are missing**: no Addressables, no Mathematics/Burst/Collections, no
  Terrain Tools, no Recorder, no Memory Profiler. Either install them or correct CLAUDE.md —
  do not let the doc and the manifest disagree.
- **No LOD1/LOD2 anywhere** (BUG-038: all 1,733 LODGroups have a single level) and **1,207
  renderers wider than 250 m** (BUG-037) — both break CLAUDE.md's own quality rules and are a
  direct cause of both the hitching and the flat look at distance.
- **Cell-load scatter is not amortised** (BUG-170): StreetTrees 225 ms, StreetFurniture 205 ms,
  ParkedCars 166 ms in the spawn frame; worst frame 2,037 ms. **Every prop you add makes this
  worse until the spawner is time-sliced.** Fix the spawner before placing new content.

### 1.5 Physics settings worth revisiting

`ProjectSettings/DynamicsManager.asset`: solver iterations 6 / velocity 1, contact offset 0.01,
`m_ImprovedPatchFriction: 0`, bounce threshold 2, default max angular speed 50. Fixed timestep is
0.01 (100 Hz) as required. Re-tune these against the car and against world collision, and keep
50/100/120 Hz all stable — `Assets/_Veyra/Tests/PlayMode/HandlingMatrixTests.cs` already exists for
this; extend it rather than replacing it.

---

## 2. What "done" looks like

Deliver all of the following in this session. Order them yourself by dependency (physics →
spawner → lighting/decals → art → VFX → audio → polish), but deliver all of them.

**A. Drive anywhere without fighting the world.**
Leaving the road onto grass, dirt, sand or a plaza and returning to it is smooth at any speed and
any angle. No invisible walls, no catching on kerbs, no sticking at bridge abutments, tunnel mouths
or interchanges. `Veyra/Physics/Validate Drivable` reports ≥ 99.5 % ok with zero `blocked` samples,
and a scripted drive-off/drive-on test at 10 / 40 / 80 km/h and 15° / 45° / 85° approach angles
passes on asphalt, kerb, grass, dirt and gravel. A stuck car always recovers.

**B. Handling that reads as a GTA V car with racing bite.**
Weight transfer visible in the body, grip that degrades believably across surfaces, brake dive,
throttle squat, an arcade drift that holds a line, and AI that drives in its lane without random
contact. Telemetry captured and committed.

**C. Nitro you feel.**
Three visually distinct modes, a composed camera + post + VFX + audio moment, heat haze, shock
cone, ground dust, and a HUD that sells the charge. Side-by-side video/stills against
`ReferenceImages/03_Asphalt_Legends_Nitro_HUD`.

**D. Smoke, dust, spray and crashes that have mass.**
Six-way lit, flipbook-driven, turbulent, surface-coloured, shadow-receiving. A crash produces
debris, sustained smoke, steam, sparks, deformation, part detachment, hit-stop and camera trauma
scaled to real impulse.

**E. A city that reads as a city.**
Facades with depth (recessed windows or parallax interiors, balconies, awnings, projecting
signage, roof clutter), real PBR terrain with normal + mask maps, foliage with wind and
translucency and impostors, kerbside furniture density matching the benchmark frames, road wear
and grime via decals, and pedestrians that are not boxes.

**F. Lighting that holds detail.**
APV (with sky occlusion) replacing the trilight ambient hack, render scale 1.0, SSAO at real
quality, a sky gradient that does not wash out at noon, and the Cyberpunk-grade neon night in
D1/D6 with wet-road response that already exists in `WorldEnvironment` wired to real materials.

**G. It still runs.**
p99 ≤ 16.6 ms at 1440p on the 4070, no hitch > 50 ms, measured in a **player build** (not the
Editor), with `stats.json` committed. LOD chains exist on every mesh. The scatter spawner is
time-sliced.

**H. The record is honest.**
`Docs/PROJECT_STATUS.md` and `Docs/BUG_TRACKER.md` updated; every bug you touched moved with its
evidence; every bug you could not close left open with what you learned. A short
`Docs/REALISM_PASS_<date>.md` with before/after frames and the measured numbers.

---

## 3. How to work

- **Research before you build.** Read `Docs/VISUAL_GAP_ANALYSIS.md`, `Docs/AUDIT_AAA_COMPARISON.md`,
  `Docs/BUG_TRACKER.md`, `Docs/VEYRA_CLAUDE_CODE_PROMPTS.md` (Appendix B/C budgets) and
  `ReferenceImages/MANIFEST_FOR_CLAUDE.md` first — a large amount of analysis already exists and
  re-deriving it wastes the session. Search the web for current URP 17 / Unity 6.6 technique where
  it matters (APV sky occlusion, six-way lit smoke, decal renderer feature, foliage wind,
  terrain height-blend, temporal upscaling) and verify against Unity's own docs, not memory.
- **Open the reference frames and your own capture side by side** before and after each visual
  change, and say in writing what differs. "Looks better" is not a result.
- **Small, idempotent, compiling steps**, each committed with its evidence.
- **Use the Unity MCP / Unity CLI** for scene inspection, screenshots, play-mode tests and
  profiling rather than guessing from source.
- **Do not ask me which item to start with, and do not stop after one item.** If something in this
  brief turns out to be wrong or impossible, say so in one line, do the rest in full, and tell me
  at the end exactly what you left out and why.
