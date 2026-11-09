# VEYRA CITY: Visual benchmark against GTA V and Cyberpunk 2077

**Date:** 2026-11-09 · **Build compared:** commit `e65856d` (visual upgrade pass), player build at 2560×1440 on an RTX 4070 Laptop GPU · **Status:** research report, no game code changed

**Purpose:** find what separates Veyra from the two quality bars (GTA V daytime realism, Cyberpunk 2077 neon nights) and turn each gap into an engineering task that fits Unity 6.6 URP at 60 FPS. We study techniques and densities only. Every asset, name, logo, UI and map stays original (CLAUDE.md).

---

## 1. Verdict

The rendering base is the right kind: HDR, ACES, bloom, SSAO, wet roads, weather and a day/night cycle are all in. The city still reads as a prototype, for five reasons, in order of visual impact:

1. **Empty streets.** There are no pedestrians, no parked cars and almost no street furniture. Traffic spawns 140–360 m away, so the road around the player is empty. GTA V and Cyberpunk fill every frame with people, parked and moving cars, and props.
2. **Flat buildings.** They are extruded boxes with a tiled window texture: no window depth, no interiors, no ground-floor life, no rooftop clutter, no real signs. Both reference games hand-build every building. Cyberpunk layers four architectural styles per district and covers texture gaps with decals.
3. **No reflections or light spill.** The city has no reflection probes and no screen-space reflections, and neon lights nothing. Only 28 real lights are active. Our wet roads glitter, where GTA V and Cyberpunk mirror long streaks of every lamp, headlight and sign.
4. **Flat sky and air.** Day is a gradient with no clouds, and night is black with no city glow. GTA V has a cloud ring with 20 swappable cloud layers, cloud shadows and light shafts. Cyberpunk's volumetric fog and clouds are its signature night look.
5. **Plain surfaces.** The asphalt noise is uniform, and there are no cracks, patches, manholes, stains or tyre marks.

**The good news:** most of 3, 4 and 5 can be fixed with Unity 6.6 URP features we have but haven't switched on: reflection probes, decals, lens flare, many lights in Forward+, and a hidden SSR feature (section 5). Items 1 and 2 need content and systems: a prop and decoration pass, a facade kit, and crowd and traffic work.

**The 10 upgrades with the biggest visual gain per day of work:**

| # | Upgrade | Fixes | Effort |
|---|---|---|---|
| 1 | Real-time reflection probe that follows the player car, plus box probes along main roads | Car paint and glass reflecting the real street | S |
| 2 | Proxy lights for every neon sign and band, street-light pool 28 → about 120, headlight cookies | Neon lighting the street; light pools on wet roads | S |
| 3 | SSR spike (hidden URP feature) plus puddle masks that grow with rain | Mirror-like wet roads at night | S–M |
| 4 | Distant light sprites (GTA's trick): every lamp and window beyond about 300 m as an instanced dot; headlight pairs for distant traffic | Night skyline that sparkles to the horizon | S |
| 5 | Sky v2: cloud layers, sun, moon, stars, city glow at night, cloud shadows | Daytime realism, night mood | M |
| 6 | Traffic v2: spawn 40–80 m away out of view, 60–100 cars, density by district and hour, parked cars on kerbs | Living streets | M |
| 7 | Decals from road data: cracks, patches, manholes, stains, tyre marks, crossing wear | Road realism up close | M |
| 8 | Street decoration pass along road splines (Cyberpunk's method): traffic lights, signs, poles, cables, bins, benches, bus stops, street trees | Scale and life at street level | M–L |
| 9 | Facade kit v2 with an interior-mapping window shader | Buildings with depth; rooms visible at night | L |
| 10 | Pedestrian crowd with simple scenarios (waiting at crossings, bus stops, benches) | A city instead of a set | L |

---

## 2. How this was researched

- **Our side:** the 10 review screenshots from the player build, `Logs/visual3/stats.json`, and a check of the code, the renderer and pipeline assets, and the installed URP 17.6.0 package source.
- **GTA V:**
  - Adrian Courrèges' frame-by-frame "GTA V Graphics Study" (all 3 parts) and NVIDIA's PC graphics guide.
  - The open FiveM native documentation, for weather, rain, dirt, damage and scenario systems.
  - Rockstar's Enhanced-edition text (via TechPowerUp).
  - PC Gamer's RTX 4070 benchmarks, Rockstar's GDC 2014 audio talk, and Digital Foundry video chapter lists.
- **Cyberpunk 2077:**
  - CD Projekt Red's Update 2.0 patch notes.
  - The 80.lv interview with CDPR's art and lighting leads.
  - NVIDIA's RT Overdrive and Ray Reconstruction articles and interview.
  - CDPR's GDC 2022 and SIGGRAPH 2021 talks, TechPowerUp benchmarks, and Digital Foundry chapter lists.
- **Limits, stated plainly:**
  - The browser pane was not allowed to open adriancourreges.com, nvidia.com or images.nvidia.com, so **I did not look at the reference images myself**. Every image link in section 8 was checked to load (HTTP 200), and the descriptions come from the source captions.
  - **I did not watch the videos.** The timestamps come from the creators' chapter lists.
  - Points marked *(secondary)* come from guides or search summaries. Points marked *(unverified)* could not be confirmed.
  - Neither studio publishes prop or decal densities, so density targets below are our own proposals, not their numbers.

---

## 3. Where Veyra is today (evidence)

Source: `Screenshots/2026-11-09_visual-pass3/` (the chase camera on Crown Avenue, Meridian Boulevard and Lumen Avenue / Neon Mile at 12:00 and 23:00, rain at night, and an autopilot race), `Logs/visual3/stats.json`, and a code and settings check on 2026-11-09.

| Area | What's in the game now |
|---|---|
| Post | ACES; bloom 1.2 (threshold 1.25 by day, 0.9 at night); SSAO 1.2 / 0.8; no auto exposure, lens flare, motion blur or depth of field |
| Shadows | Main light 2048 px, 160 m, 4 cascades |
| Reflections | No reflection probes in the city (one in the garage only); no SSR; the car and glass reflect the skybox only |
| Lights at night | Pool of 28 real street lights (range 26 m) around the player; neon, windows and signs are emissive only and light nothing |
| Sky | Gradient sky material; no clouds, stars, moon disc or city glow; overcast only greys the colour |
| Weather | Clear, overcast or rain every 3–6 min in free roam; rain streaks; wetness raises road smoothness to 0.96; dries in 90 s |
| Water | Flat sea plane and river with a scrolling normal map; no waves, foam, refraction or shoreline |
| Roads | Procedural asphalt with normal and gloss maps, granite kerbs, paving with joints, painted markings; no decals |
| Buildings | 2,708 generated from the data: walls, roofs, podiums, crowns, a neon band and blade signs in two districts; windows are a flat texture (no depth, no interiors) |
| Props | Street lamps, 15,269 containers, 8 cranes; nothing else (no traffic lights, signs, benches, bins, poles or parked cars) |
| Vegetation | 11.4 M grass blades on terrain; few trees along streets |
| Life | Traffic: up to 36 cars spawned 140–360 m away; no pedestrians, police, animals or aircraft |
| Vehicles | Procedural placeholder car; no damage, dirt or headlight projection; premium models waiting on your downloads |
| Audio | Engine and effects only; no ambience layers, music or radio |
| HUD | Speed, nitro, money and level; the minimap draws a grid, not the road map |
| Performance | 117 FPS average, p99 18.3 ms (target 16.6 ms), 11 hitches over 50 ms, 0 exceptions (Mono dev build) |

**Bugs seen in the review shots (new, to be logged in `Docs/BUG_TRACKER.md`):**
- In the race shots (16 s and 26 s into RACE-16 on autopilot), the player car shows 1–2 km/h and **R** (reverse): it's stuck.
- Neon bands stay lit in daylight and read as flat magenta and yellow stripes.
- Window tiles on Neon Mile low-rise blocks are about twice too big.
- Storefront panels at night are flat beige boxes.
- The minimap shows a grid, not the roads.
- Grass tufts on road verges read as clumps on bare concrete.

---

## 4. Gap analysis and upgrades by area

Effort: **S** = days, **M** = 1–2 weeks, **L** = several weeks. Frame-time costs are estimates that must be measured on the RTX 4070. Sources: [AC] Courrèges' GTA V study, [NV] NVIDIA guides and articles, [FX] FiveM native docs, [CP] CDPR patch notes, [80] 80.lv CDPR interview.

### 4.1 Lights at night and light spill

**Reference:**
- **Cyberpunk** with ray tracing off budgets about 10 shadow-casting lights at a time, but many more unshadowed lights; signs, screens and billboards light their surroundings. Path tracing makes every one of them cast shadows [80, NV].
- **GTA V:**
  - Every distant light is a 32×32 instanced sprite, tens of thousands of them, and distant traffic is moving headlight-sprite pairs that become real cars as you approach [AC].
  - Headlights cast shadows at High shadow quality [NV].
  - Headlights get anamorphic streaks at night [AC].

**Veyra now:** 28 street lights within range; neon lights nothing; no distant light sprites; no headlight projection on the road.

**Upgrade:**
1. **Light budget in Forward+:**
   - Up to about 8 shadowed lights: the player's 2 headlights and the nearest lamps.
   - 150–250 unshadowed lights, culled by distance.
   - The building generator emits a coloured proxy light (6–15 m range) for every neon band, blade sign and big billboard. The street-light pool goes from 28 to about 120.
2. **Distant light sprites:**
   - An instanced dot for every street lamp, traffic light and lit window cluster beyond about 300 m.
   - Headlight and tail-light sprite pairs moving along the road splines, standing in for distant traffic.
3. **Headlights:** spot lights with a cookie; Screen Space Lens Flare; an anamorphic streak (a horizontal bloom pass).

**Effort:** S–M · **Cost:** about 1–2 ms, to be measured.

### 4.2 Reflections and wet roads

**Reference:**
- **GTA V** (no ray tracing) [AC]:
  - Renders a 128 px cubemap every frame around the player car and favours the upper hemisphere, because roofs and bonnets face up.
  - Uses planar reflections only for water and mirrors.
  - The long streaks of lamps on wet roads come from lights hitting a glossy surface.
  - A rain level grows puddles over time, and streets stay wet after rain stops [FX].
  - The 2025 Enhanced edition adds ray-traced reflections.
- **Cyberpunk** with ray tracing off: SSR (its weakness is grain), plus reflection probes and mirror quality settings. Turning SSR off gains about 12% FPS *(secondary)*.

**Veyra now:** no city probes and no SSR. Wet roads only raise smoothness, so they sparkle instead of mirroring. Roads dry in 90 s.

**Upgrade:**
1. **Player-car probe:** one real-time reflection probe that follows the car (128 px, time-sliced one face per frame). This is GTA V's own trick.
2. **Road probes:** box-projected probes along main roads every 80–120 m, with day and night captures blended by time of day.
3. **SSR spike:** enable URP's hidden SSR feature (section 5) and measure quality and cost in the player build. If it isn't production-ready, write our own half-resolution SSR as a Full Screen Pass.
4. **Puddles:** a mask per road from noise and road camber, grown by a rain level. Ripple normals while it rains. Drying over several minutes, not 90 s.

**Effort:** S–M · **Cost:** probes about 0.3 ms, SSR about 1–2 ms, to be measured.

### 4.3 Sky, clouds, weather and atmosphere

**Reference:**
- **GTA V:**
  - Sky dome with noise textures and a horizon cloud ring (2048×512 maps) [AC].
  - 20 swappable cloud layers with timed transitions; 17 weather types, two of which can be blended by percentage [FX].
  - Cloud shadows on the ground, and a half-resolution ray-marched light-shaft map [AC].
  - Fog is tuned to hide low-detail distant buildings [AC].
- **Cyberpunk:**
  - Volumetric fog and volumetric clouds, its heaviest raster cost, especially at night and in rain *(secondary)*.
  - Weathers: sunny, light clouds, cloudy, fog, rain, toxic rain, pollution and sandstorm *(secondary)*.
  - Lighting and weather are fully dynamic, with no baking, because of the vertical world (GDC 2022).

**Veyra now:**
- Gradient sky; no clouds, moon or stars.
- 3 weather types.
- Simple distance fog; no light shafts.
- Night sky is pure black.

**Upgrade:**
1. **Procedural skybox (Shader Graph):**
   - 3–4 cloud layers we generate ourselves, blended per weather.
   - Sun disc, moon and stars.
   - Horizon city glow at night, tinted per district.
   - A scrolling cloud-shadow cookie on the sun.
2. **Weather set:** Clear, Cloudy, Overcast, Rain, Storm (lightning flashes and thunder), Fog, and Smog (Neon Mile and Ironport at night), with 20–60 s blends.
3. **Volumetric fog and light shafts:** custom half-resolution ray-marched fog as a Full Screen Pass. URP has no volumetric fog. It carries a quality setting, because it is the most expensive item in Cyberpunk too.

**Effort:** M (sky and weather), L (volumetrics) · **Cost:** sky under 0.2 ms; volumetrics 1–2 ms at half resolution, to be measured.

### 4.4 Buildings and facades

**Reference:**
- **GTA V:**
  - Hand-modelled facades, with parallax occlusion mapping at Very High [NV].
  - Distant districts collapse into hand-made proxies: a whole district in 1 draw call; several km² of hills in 1 draw call and 2,500 triangles [AC].
- **Cyberpunk** [80]:
  - Every building is hand-placed.
  - Four layered architectural "era" styles, mixed per district with district colour coding.
  - Texel density twice that of The Witcher 3, with decals used extensively.
  - Phantom Liberty added "negative space" rules so dense areas stay readable.
  - Dust and overgrowth are scaled to how much traffic an area gets.

**Veyra now:**
- Extruded boxes with a flat window texture; no rooftop clutter.
- Beige storefront panels at night.
- Window tiles too big on some low-rise blocks.

**Upgrade:**
1. **Facade kit v2 in `BuildingGenerator.cs`, per style kit:**
   - Recessed windows (0.15–0.3 m inset) with mullions.
   - A ledge or cornice at each floor.
   - Balconies on residential blocks; AC units; fire escapes in old districts.
   - Grime streaks and base dirt as decals.
2. **Interior-mapping window shader:** fake rooms behind the glass, with varied lit and unlit rooms, warm and cool light, and blinds. This is the biggest single night-time win for buildings.
3. **Storefronts:** glass with interior cards, awnings, sign boxes, and light spilling onto the sidewalk.
4. **Rooftops:** HVAC units, water tanks, antennas, helipads on towers, billboard frames.
5. **District identity:** our own style layers per district (from the design repo's style kits), with colour coding and negative-space rules.
6. **Distance:** LOD0/1/2 and a proxy mesh per 1 km cell.

**Effort:** L · **Cost:** offset by LODs and the GPU Resident Drawer.

### 4.5 Signs, neon and billboards

**Reference:**
- **Cyberpunk:** dense advertising (GamesRadar counted 20 ads in one screenshot), animated LED boards and holograms, grouped with deliberate negative space. Signs light their surroundings [80, NV].
- **GTA V:** store signage and billboards everywhere; no published numbers.

**Veyra now:** neon lines and blade signs in two districts; no text or logos; neon stays bright in daylight.

**Upgrade:**
1. **Sign generator:** an original brand list (names and logos generated as textures).
2. **Sign types:**
   - Blade signs and box signs.
   - Rooftop billboards.
   - Animated LED boards (flipbook emission).
   - Holograms for Neon Mile (additive scan-line shader).
   - Flicker on a few.
3. **Day/night:** emission drops to about 15% by day, so signs read as painted panels.
4. **Light:** every large sign gets a proxy light (4.1).
5. **Density by district:** high on Neon Mile and Downtown, low in the suburbs, with resting space between clusters.

**Effort:** M.

### 4.6 Street furniture, props and decals

**Reference:**
- **GTA V:**
  - Props are interaction points: ATMs, parking meters, bus-stop benches, bins.
  - Birds sit on telegraph poles; cables and trees are tessellated.
  - Parked cars have paint fade [FX, NV].
- **Cyberpunk:**
  - Everything is hand-placed. The one procedural tool is a road-spline tool that also places decorations along the spline, which artists then adjust [80].
  - Decals are used extensively.

**Veyra now:** street lamps only; no decals.

**Upgrade:**
1. **Decoration pass from `road_network_geometry.json`** (the same idea as Cyberpunk's spline tool). It places:
   - Traffic lights and stop lines at junctions.
   - Signs, hydrants, bins, benches, bus stops, parking meters, bollards and planters.
   - Street trees every 10–15 m on avenues.
   - Utility poles with sagging cables in older districts.
   - Parked cars along kerbs.

   Everything is instanced. Proposed target: one prop every 8–15 m of sidewalk downtown.
2. **URP Decal Renderer Feature, decals placed from road data:**
   - Cracks, patches, manholes, drains, oil stains, tyre marks at junctions, worn crossings.
   - Graffiti (original art).
   - Sand and dust near Ironport and the outskirts, scaled by traffic.
3. **Asphalt fix:** lower the noise contrast and add large-scale variation (patch repairs, darker wheel tracks per lane).

**Effort:** M–L · **Cost:** decals about 0.3–0.5 ms.

### 4.7 Traffic, pedestrians, police and wildlife

**Reference:**
- **GTA V:**
  - About 140 pedestrian scenario types: smoking, phoning, jogging, waiting at crossings, sitting at bus stops, using ATMs, and more.
  - Population schedules split each zone into 12 two-hour slots, with different people and vehicles per slot.
  - Traffic waits at red lights.
  - Police find you by line of sight, using vision cones on the radar.
  - Animals: gulls, pigeons, dogs, deer and more [FX; community docs].
  - NVIDIA suggests 75% population density and warns that density cost rises sharply in real gameplay [NV].
- **Cyberpunk 2.0** [CP]:
  - Traffic changes lanes around obstacles, and pedestrians avoid moving cars.
  - Traffic density varies by time of day.
  - Police chase you on foot or in cars, with roadblocks, and units scale over 5 wanted levels.
  - Gang and faction chases happen in the streets.
  - Crowd density is a graphics setting because it is CPU-bound *(secondary)*.

**Veyra now:** up to 36 cars, spawned 140–360 m away; no pedestrians, police or animals.

**Upgrade:**
1. **Traffic v2:**
   - Spawn 40–80 m away, out of camera view (behind buildings).
   - 60–100 cars near the player, plus distant headlight sprites (4.1).
   - Density by district and two-hour time slot.
   - Obey traffic lights; lane changes with indicators; horns.
2. **Pedestrians:**
   - An instanced crowd using vertex-animation textures (walk, idle, phone, wait).
   - 100–300 visible.
   - Simple scenarios at props: crossing waits, bus stops, benches, ATMs.
   - People step back from cars and flee from crashes.
3. **Heat / police for street racing:** needs a design decision first. A good fit is GTA-style line-of-sight escape plus Cyberpunk-style escalation with roadblocks.
4. **Sky and sea life:** gulls at Ironport, pigeons on poles, helicopters with searchlights at night, planes on approach.

**Effort:** L · **Cost:** mostly CPU; must be measured.

### 4.8 Vehicles

**Reference:**
- **GTA V:**
  - Dents are applied as an impact sphere with a radius and depth.
  - Windows smash individually.
  - Dirt level runs from 0 to 15 and builds up while driving.
  - Parked cars get paint fade [FX].
  - The chase camera has high-speed zoom and shake, plus handbrake swing.
  - Engine sound is granular synthesis from recordings made while driving.
- **Cyberpunk 2.0:**
  - Torque curves, speed-sensitive steering, measured braking distances, better behaviour over kerbs, a heavier feel [CP].
  - Several camera distances.
  - Windows break with glass shards.

**Veyra now:** procedural placeholder car; no damage or dirt.

**Upgrade:**
1. Import your downloaded cars.
2. **Car paint shader:** clear coat and flakes, plus a dirt mask, a wet-droplet layer and dent deformation (GTA-style impact spheres).
3. **Breakable parts:** breakable glass and detachable bumpers.
4. **Chase camera:** FOV widens and the camera shakes with speed; handbrake swing; look-back.
5. **Player headlights:** spot lights with a cookie and shadows.

**Effort:** M–L.

### 4.9 Water and nature

**Reference:**
- **GTA V:**
  - A vertex-animated ocean mesh.
  - Half-resolution refraction with depth-based colour, plus caustics.
  - Fresnel blend of reflection and refraction.
  - Grass and flowers with shadows at Ultra [AC, NV].
- **Cyberpunk:** desert badlands outside the city; dust and overgrowth scaled by traffic [80].

**Veyra now:**
- A flat sea and river with a scrolling normal map.
- 11.4 M grass blades.
- Few trees.

**Upgrade:**
1. **Ocean shader:** Gerstner waves, depth colour, shoreline foam, and the 4.2 reflections.
2. **Trees:** proper tree models with LODs and wind; palms on the coast; forest density on the hills.

**Effort:** M.

### 4.10 Audio

**Reference:**
- **GTA V** (Rockstar's GDC 2014 audio talk):
  - 949 ambience zones, 1,210 rules and 518 static emitters.
  - Occlusion and reverb.
  - Granular engines.
  - 15 music and 2 talk radio stations.
- **Cyberpunk:**
  - About 11 radio stations.
  - Police radio chatter over the car radio [CP].

**Veyra now:** engine and effects only.

**Upgrade:**
1. **Ambience zones from district data:** traffic hum, crowds, port, sea, birds, distant sirens, with rules by time of day and weather.
2. **Reverb zones** in tunnels and underpasses.
3. **Rain sounds** on the car.
4. **Music:** 2–3 stations of original or properly licensed music.

**Effort:** M.

### 4.11 HUD, minimap and navigation

**Reference:**
- **GTA V:** the minimap is square tiles plus vector road meshes that stay sharp at any zoom [AC].
- **Cyberpunk 2.0:**
  - The minimap zooms out with speed [CP].
  - The route shows only on the minimap, and players complain they miss turns at speed *(secondary)*. We can beat this.

**Veyra now:** a grid placeholder.

**Upgrade:**
1. **Road map:** draw the road network as a vector mesh with district colours.
2. **Zoom:** speed-based.
3. **GPS:** a route line on the minimap, plus an optional in-world route line and arrows on the road.

**Effort:** S–M.

### 4.12 Camera and post-processing

**Reference:**
- **GTA V** [AC]:
  - Filmic tonemapper.
  - Asymmetric eye adaptation: dark to bright adapts faster.
  - Sun flare with 12 streaks and 70 sprites.
  - Anamorphic headlight streaks.
  - Depth of field.
  - Motion blur with the player masked out.
- **Cyberpunk:** toggles for film grain, chromatic aberration, depth of field, lens flare and motion blur.

**Veyra now:** ACES, bloom and SSAO only.

**Upgrade:**
1. **Auto exposure:** a custom script, because URP has none. It uses asymmetric adaptation for tunnels and night.
2. **Lens flare:** Screen Space Lens Flare.
3. **Motion blur:** light camera motion blur at speed.
4. **Depth of field:** for the photo mode and the garage.
5. **Optional effects:** subtle grain, chromatic aberration and vignette, each with an off switch.

**Effort:** S.

### 4.13 Distance, LOD and performance (prerequisite)

**Reference:**
- **GTA V:**
  - Culling and LOD on the GPU.
  - Hand-made distant proxies.
  - Dithered LOD cross-fades.
  - About 4,155 draw calls per frame [AC].
  - On an RTX 4070 at 1440p, the Enhanced edition with maximum ray tracing stays above 60 FPS *(PC Gamer)*.
- **Cyberpunk:**
  - Raster Ultra at 1440p needs about an RTX 4070 for 60 FPS *(TechPowerUp)*.
  - An SSD is required for streaming.

**Veyra now:** no LODs on city meshes, and p99 is already 18.3 ms before any of the above is added.

**Upgrade:**
1. **Per-cell LOD:** LOD0/1/2 plus a proxy per 1 km cell.
2. **Draw cost:** GPU Resident Drawer with GPU occlusion culling.
3. **Upscaling:** STP as a quality option.
4. **Streaming:** Addressables.
5. **Measurement:** frame budget measured after every phase.

**Effort:** M–L.

---

## 5. Unity 6.6 URP: what we can switch on

Checked against the installed URP package (17.6.0) in `Library/PackageCache`, not the documentation.

| Feature | In URP 17.6? | In our project now | Use |
|---|---|---|---|
| Decal Renderer Feature | Yes | Off | Road cracks, patches, manholes, stains, tyre marks, graffiti |
| Screen Space Reflections renderer feature | Yes, but compiled out unless the scripting define `URP_SCREEN_SPACE_REFLECTION` is set (hidden preview) | Off | Wet roads mirroring neon, glass. Needs a spike for stability and cost in the player build |
| Reflection probes (blending, box projection, atlas) | Yes | Enabled in the pipeline, but no probes in the city | Car paint, glass and wet roads reflecting the real street |
| Adaptive Probe Volumes | Yes | Off (legacy light probes) | Bounce light in streets. Cyberpunk's raster GI uses probes every 1.5–4 m. Baking the whole map is heavy, so start downtown |
| Surface Cache GI renderer feature | Present, but compiled out unless `SURFACE_CACHE` is set (experimental) | Off | Real-time GI; evaluate later, not production-ready |
| GPU Resident Drawer | Yes (Forward+) | Off | Cuts CPU draw cost for dense props and buildings |
| STP upscaler | Yes | Off | Frees GPU time for new effects at 1440p |
| Screen Space Lens Flare | Yes | Off | Headlights, sun, street lamps |
| Full Screen Pass renderer feature | Yes | Not used | Hook for custom effects (volumetric fog, our own SSR, rain on the lens) |
| Volumetric fog / light shafts | **No** (URP has none) | n/a | Custom ray-marched fog pass, or a licensed package |
| Auto exposure (eye adaptation) | **No** | n/a | Small custom script: measure scene luminance, drive post exposure |
| Hardware ray tracing | **No** (HDRP only) | n/a | Not planned: switching to HDRP means rewriting every material and shader, and CLAUDE.md fixes URP |

---

## 6. Upgrade roadmap

Each phase is a set of playbook-style cards. Each card is checked the same way: the same review shots before and after (Crown Ave, Meridian Blvd and Neon Mile at 12:00 and 23:00, plus rain), 0 exceptions, and **p99 ≤ 16.6 ms in the release build**.

| Phase | Contents | Time estimate | Visual result |
|---|---|---|---|
| **V1 Light and reflections** | 4.1 light budget and distant sprites; 4.2 probes, SSR spike, puddles; 4.12 auto exposure and lens flare; neon dimmed by day; minimap road map; fixes for the stuck autopilot and oversized windows; GPU Resident Drawer and STP (to win back frame time first) | 1–2 weeks | Night streets glow and mirror; car paint reflects the city |
| **V2 Sky and weather** | 4.3 sky v2, cloud layers and shadows, 7 weather types with blends, storm | 1–2 weeks | Daytime realism; mood per weather |
| **V3 Street level** | 4.6 decals and decoration pass, parked cars, street trees; 4.5 sign generator; 4.7 traffic v2 | 3–4 weeks | Streets look lived-in and full |
| **V4 Buildings** | 4.4 facade kit v2, interior mapping, storefronts, rooftops, district styles; 4.13 LODs and proxies | 3–5 weeks | Skyline and facades with depth |
| **V5 Life and sound** | 4.7 pedestrians, wildlife and aircraft, heat / police (after the design decision); 4.10 ambience and radio | 4–6 weeks | A living city |
| **V6 Atmosphere and water** | 4.3 volumetric fog and light shafts; 4.9 ocean and trees; 4.8 car damage, dirt and wet paint | 3–4 weeks | The Cyberpunk-style night look; coast and nature |

**Realistic expectation:** after V1–V3, Veyra should hold up well in screenshots at night and read as a believable city by day. Matching GTA V's hand-made detail everywhere is years of work for a large team. Our route is to generate content from the world data with strong rules, and to hand-finish hero areas (Neon Mile, Downtown, Ironport).

---

## 7. Assets we need, and legal sources

Every asset needs a licence sidecar (`<model>.licence.json`) and must pass the name filter (CLAUDE.md).

| Need | Legal sources |
|---|---|
| PBR textures (asphalt, concrete, brick, plaster, metal) and HDRI skies | Poly Haven (CC0), ambientCG (CC0); Blender MCP can search and import Poly Haven assets |
| Street props and trees | Own Blender work; Poly Haven models (CC0); Asset Store packs (purchase needs your OK) |
| Pedestrians and animations | Own work, or a royalty-free character pack you download (Mixamo needs your Adobe sign-in) |
| Hero cars | Your downloads (in progress) |
| Signs, logos, billboards | Generated in-house: original brand names and art only |
| City ambience and music | CC0 recordings with licence files, or commissioned or generated original music |

---

## 8. References

### Text sources
- Adrian Courrèges, "GTA V – Graphics Study", parts 1–3: https://www.adriancourreges.com/blog/2015/11/02/gta-v-graphics-study/
- NVIDIA, GTA V PC graphics and performance guide: https://www.nvidia.com/en-us/geforce/news/grand-theft-auto-v-pc-graphics-and-performance-guide/
- FiveM / CitizenFX native documentation: https://github.com/citizenfx/natives
- GTA V Enhanced: TechPowerUp (Rockstar's feature text) https://www.techpowerup.com/333659/ · PC Gamer performance analysis https://www.pcgamer.com/hardware/gta-5-enhanced-performance-analysis-forget-max-settings-my-advice-is-to-enable-very-high-rt-and-enjoy-the-show · TweakTown TU 1.71 https://www.tweaktown.com/news/105876/
- Rockstar GDC 2014 audio talk (slides): https://gta.com.ua/img/news/sound-of-gta5-gdc2014.pdf
- CDPR, Update 2.0 patch notes: https://www.cyberpunk.net/en/news/49060/update-2-0
- 80.lv, how Night City was built: https://80.lv/articles/interview-how-cyberpunk-2077-s-night-city-was-built-almost-entirely-by-hand/
- NVIDIA, RT Overdrive launch https://nvidia.com/en-us/geforce/news/cyberpunk-2077-ray-tracing-overdrive-update-launches-april-11/ · Overdrive interview https://nvidia.com/en-my/geforce/news/cyberpunk-2077-ray-tracing-overdrive-mode-interview/ · DLSS 3.5 Ray Reconstruction https://nvidia.com/en-us/geforce/news/nvidia-dlss-3-5-ray-reconstruction/
- CDPR GDC 2022, "Bringing Light to Night City": https://gdcvault.com/play/1027959/ · SIGGRAPH 2021 shadow optimisations: https://history.siggraph.org/learning/shadows-optimizations-in-cyberpunk-2077-by-dybisz-and-witanowski
- TechPowerUp, Phantom Liberty performance: https://techpowerup.com/review/cyberpunk-2077-phantom-liberty-benchmark-test-performance-analysis/7.html

### Reference images (each link checked to load; descriptions are from the source captions)
**GTA V**: Courrèges study, prefix `https://www.adriancourreges.com/img/blog/2015/gtav/`
- `a/00_final_frame.jpg`: finished daytime frame (the target composition)
- `a/06_shadow.jpg`: the 4 shadow cascades
- `a/20_light_shaft.jpg`: the half-resolution light-shaft map
- `a/25_atm_fog.jpg`: fog hiding distant low-detail buildings
- `a/27_atm_cloud.jpg`: sky dome and cloud ring
- `b/01_bulb_after.jpg`: night city made of light sprites
- `b/40_light_refl.jpg`: headlight and lamp streaks on a wet road
- `b/05_little_seoul.png`: low-detail proxy of a whole district
- `c/03_anamorphic_2.jpg`: anamorphic headlight streaks
- `a/39_map.png`: minimap tiles and vector roads

**GTA V**: NVIDIA guide, prefix `https://international.download.nvidia.com/geforce-com/international/images/grand-theft-auto-v/`
- `grand-theft-auto-v-pc-reflection-quality-003-ultra.png`: rain-night reflections
- `grand-theft-auto-v-pc-population-density-001-100-percent.png`: a street at full population density
- `grand-theft-auto-v-pc-post-fx-001-ultra.png`: dusk lights with bloom and flare

**Cyberpunk 2077**: NVIDIA Overdrive article, prefix `https://images.nvidia.com/aem-dam/Solutions/geforce/news/cyberpunk-2077-ray-tracing-overdrive-update-launches-april-11/` (in each pair, "off" is raster and "on" is path tracing)
- `cp2077-alleyway2-rtx-off-badged.jpg` / `cp2077-alleyway2-rtx-on-badged.jpg`: wet alley lighting and reflections
- `cp2077-shadows2-rt-off-badged.jpg` / `cp2077-shadows2-rt-on-badged.jpg`: what a small shadow-light budget loses
- `cp2077-porschedof-rtx-off-badged.jpg` / `cp2077-porschedof-rtx-on-badged.jpg`: a car "grounded" by contact lighting (our car-on-road benchmark)
- `cyberpunk-2077-phantom-liberty-ray-tracing-overdrive-dlss-3-5-ray-reconstruction-comparison-rtxoff-001.jpg` / `...-rtxon-001.jpg`: Dogtown, soft shadows from every light
- Ray Reconstruction on neon and billboard reflections: https://images.nvidia.com/aem-dam/Solutions/geforce/news/gamescom-ct0037/dlss-3-5-ray-reconstruction-improves-cyberpunk-2077-reflections.jpg

### Reference videos (timestamps from the creators' chapter lists)
**GTA V**
- Digital Foundry, Enhanced edition ray tracing: https://www.youtube.com/watch?v=N6gMvs52-Hs (what RTGI changes, 0:53; performance, 12:58)
- Digital Foundry, maxed Enhanced vs the GTA 6 trailer: https://www.youtube.com/watch?v=Dge00vH-NWE (reflections 4:13, shadows 7:13, GI 8:08, environment assets 9:10)
- Every Enhanced setting compared: https://www.youtube.com/watch?v=89MylWqN-Yo (RT reflections 3:35, RTGI 4:37, Post FX 15:09, population 16:39, LOD 17:14)
- Digital Foundry, PS5 vs PS4 vs PC (foliage, density, LOD): https://www.youtube.com/watch?v=m5QHInJShQw
- 4K rain walk where the weather clears and streets stay wet: https://www.youtube.com/watch?v=xKmOSaQnWBE
- RT on/off, 70 s: https://www.youtube.com/watch?v=PkxiCuzAuCQ

**Cyberpunk 2077**
- Digital Foundry, what ray tracing delivers (each effect against its raster fallback, the best one for URP planning): https://www.youtube.com/watch?v=6bqA8F6B6NQ
- Digital Foundry, best raster settings (cost against value): https://www.youtube.com/watch?v=pC25ambD8vs
- Digital Foundry, Overdrive preview: https://www.youtube.com/watch?v=I-ORt8313Og
- Digital Foundry, 2.0 Ray Reconstruction (reflections 05:37, lighting while driving 13:06): https://www.youtube.com/watch?v=hhAtN_rRuQo
- Digital Foundry, Phantom Liberty tech review (Dogtown 01:08, 2.0 changes 07:06): https://www.youtube.com/watch?v=8UTU03TooUI
- Two-hour night rain drive, no commentary (puddles, traffic, camera feel): https://www.youtube.com/watch?v=mnEaAv0ONxQ
- 2.0 police chase: https://www.youtube.com/watch?v=B6ehwRqfd2Q
