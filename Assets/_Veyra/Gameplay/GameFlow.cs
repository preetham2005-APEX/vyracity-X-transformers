using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VeyraCity.AI;
using VeyraCity.Assists;
using VeyraCity.Input;
using VeyraCity.Nitro;
using VeyraCity.Racing;
using VeyraCity.UI;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Top-level game flow: main menu → free roam (event markers, traffic) ↔ races (AI field, results,
    /// rewards, story) ↔ garage (cars, upgrades, paint) with settings, pause and saving throughout.
    /// </summary>
    [DefaultExecutionOrder(-1000)] // the material library must be active before any OnEnable/Start in the scene
    public sealed class GameFlow : MonoBehaviour
    {
        public enum State { Boot, MainMenu, FreeRoam, Garage, Race, Results }

        [SerializeField] GameDatabase db;
        [Tooltip("Assets/_Veyra/World/Generated/Materials/VeyraMaterialLibrary.asset: every material runtime code creates or uses (card F1)")]
        [SerializeField] VeyraMaterialLibrary materials;
        [SerializeField] ChaseCamera chaseCamera;
        [SerializeField] WorldEnvironment environment;
        [SerializeField] Transform menuOrbitCentre;

        public static GameFlow Instance { get; private set; }
        public State Current { get; private set; } = State.Boot;
        public SaveData Save => save;
        public VehicleController PlayerCar => player;
        public VehicleDriver PlayerDriver => playerDriver;
        public GameDatabase Database => db;
        public VeyraMaterialLibrary Materials => materials;
        public WorldEnvironment WorldEnv => environment;

        /// <summary>
        /// Automated tests (VeyraAutoTest, card V0): free roam from home with no traffic, clear weather and a fixed time of
        /// day, so runs are repeatable. Returns the player's driver so the test can bind its own intent source.
        /// </summary>
        public VehicleDriver BeginAutomatedFreeRoam(float timeOfDay)
        {
            automated = true;
            Application.runInBackground = true;
            EnterFreeRoam(false);
            traffic.Active = false;
            traffic.Clear();
            if (environment != null)
            {
                environment.AutoWeather = false;
                environment.AnimateTime = false;
                environment.SetWeather(WorldEnvironment.WeatherKind.Clear);
                environment.SetTime(timeOfDay);
            }
            return playerDriver;
        }

        SaveData save;
        bool automated, keepDisplay;
        PlayerDriverInput input;
        HudController hud;
        RoadWear roadWear;
        SpeedTraps speedTraps;
        ActivityHooks activities;
        WorldMap worldMap;
        MenuController menu;
        TrafficSystem traffic;
        /// <summary>BUG-170: the most physics catch-up per frame (s); see Start.</summary>
        public const float MaxCatchUpSeconds = 0.04f;
        RaceSession race;
        Camera cam;
        AudioSource uiAudio;
        AudioClip beepLow, beepHigh, click;

        VehicleController player;
        VehicleDriver playerDriver;
        NitroSystem playerNitro;
        DriftScorer playerDrift;
        GameObject playerRoot;
        readonly List<GameObject> opponents = new List<GameObject>();
        readonly List<Marker> markers = new List<Marker>();
        RaceDefinition currentRace;
        CareerEvent currentCareer;
        Chapter currentChapter;
        bool paused;
        float saveTimer;
        Transform garageRoot;
        GameObject garageDisplay;
        float orbitAngle;
        SurfaceDefinition roadSurfaceRuntime;
        bool demo;
        string demoRaceId;
        Marker nearMarker;

        sealed class Marker
        {
            public RaceDefinition Race;
            public CareerEvent Career;
            public Chapter Chapter;
            public Vector3 Position;
            public GameObject Visual;
        }

        static readonly Color[] RivalColors = { new Color(1f, 0.3f, 0.3f), new Color(1f, 0.8f, 0.2f), new Color(0.6f, 0.4f, 1f), new Color(0.3f, 1f, 0.5f), new Color(1f, 0.5f, 0.1f), new Color(0.3f, 0.7f, 1f) };
        static readonly string[] RivalNames = { "Kade Morrow", "Lina Sato", "Rook Delgado", "Ines Fairchild", "Tobias Wren", "Yara Quell", "Dmitri Hale", "Noor Castell" };

        // ------------------------------------------------------------------ boot
        void Awake()
        {
            Instance = this;
            VeyraMaterialLibrary.Activate(materials, this);
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-veyraSaveDir") SaveSystem.OverrideFolder = a[i + 1];
            foreach (var arg in a)
            {
                // Automated verification runs must keep ticking without focus and keep the window size they were given.
                if (arg.StartsWith("-veyraTour") || arg.StartsWith("-veyraDemo") || arg.StartsWith("-veyraAutoQuit") || arg.StartsWith("-veyraShots") || arg.StartsWith("-veyraCarShots") || arg.StartsWith("-veyraIterShots") || arg.StartsWith("-veyraReviewShots") || arg == "-autotest") { automated = true; Application.runInBackground = true; }
                if (arg.StartsWith("-screen-")) keepDisplay = true;
            }
            cam = chaseCamera != null ? chaseCamera.GetComponent<Camera>() : Camera.main;
        }

        /// <summary>
        /// Card F1: compiles the variants in VC_Variants.shadervariants while loading instead of on first use (fewer first-use
        /// hitches, BUG-029). Graphics Settings also lists the collection under Preloaded Shaders, in which case Unity has
        /// already warmed it at startup and this only logs that.
        /// </summary>
        void WarmUpShaders()
        {
            var variants = materials != null ? materials.Variants : null;
            if (variants == null) { Debug.LogWarning("[Veyra] no shader variant collection in the material library; skipping warm-up", this); return; }
            if (variants.isWarmedUp) { Debug.Log($"[Veyra] shader warm-up: {variants.variantCount} variants of {variants.shaderCount} shaders already warm (Preloaded Shaders)"); return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            variants.WarmUp();
            Debug.Log($"[Veyra] shader warm-up: {variants.variantCount} variants of {variants.shaderCount} shaders in {sw.Elapsed.TotalMilliseconds:0} ms");
        }

        void Start()
        {
            WarmUpShaders();
            // BUG-170 (D2): physics runs at 100 Hz and the project allowed 0.1 s of catch-up, so one 100 ms hitch was followed by
            // up to 10 physics steps in the next frame (the 30-40 ms PhysicsFixedUpdate frames in the profile); 0.04 s = at most
            // 4 steps, the game slows briefly during a hitch instead of spiralling (-veyraTimeScale scales it as before)
            Time.maximumDeltaTime = MaxCatchUpSeconds * Mathf.Max(1f, Time.timeScale);
            save = SaveSystem.Load() ?? NewSave();
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(transform, false);
            }
            input = GetComponent<PlayerDriverInput>();
            if (input == null) input = gameObject.AddComponent<PlayerDriverInput>();
            input.PausePressed += OnPausePressed;
            input.CameraCyclePressed += () => { if (chaseCamera != null && chaseCamera.enabled) chaseCamera.Cycle(); };
            input.ResetPressed += OnResetPressed;
            hud = gameObject.AddComponent<HudController>();
            worldMap = gameObject.AddComponent<WorldMap>();   // full-screen map on M / gamepad View
            gameObject.AddComponent<WaterSurface>();          // round-2 water on the sea, river and reservoir
            gameObject.AddComponent<GroundHeightField>();     // iteration 20: terrain height for street-level facade detail
            gameObject.AddComponent<AtmosphereScatter>();     // iteration 22: view-dependent haze (sun scattering, city glow)
            roadWear = gameObject.AddComponent<RoadWear>();   // iteration 24: wheel paths, oil, gutters, patches on the asphalt
            gameObject.AddComponent<SkyReflection>();         // iteration 25: reflections follow the sky (wet night roads read as snow)
            speedTraps = gameObject.AddComponent<SpeedTraps>(); // iteration 26: Forza-style speed cameras from the world data
            activities = gameObject.AddComponent<ActivityHooks>(); // G5 (round 3): speed/drift zones, jumps, drag strips, discoveries
            hud.Build();
            menu = gameObject.AddComponent<MenuController>();
            menu.Build();
            traffic = new GameObject("Traffic").AddComponent<TrafficSystem>();
            traffic.transform.SetParent(transform, false);
            traffic.CarPrefabs = db != null ? db.TrafficModels : Array.Empty<GameObject>();
            var parkedCars = new GameObject("ParkedCars").AddComponent<ParkedCars>();   // kerbside parked cars (street life)
            parkedCars.transform.SetParent(transform, false);
            parkedCars.Prefabs = traffic.CarPrefabs;
            new GameObject("TrafficLights").AddComponent<TrafficLights>().transform.SetParent(transform, false);   // junction signals
            new GameObject("StreetTrees").AddComponent<StreetTrees>().transform.SetParent(transform, false);       // tree-lined avenues
            new GameObject("SkyDome").AddComponent<SkyDome>().transform.SetParent(transform, false);              // clouds, haze, stars, moon
            new GameObject("Pedestrians").AddComponent<Pedestrians>().transform.SetParent(transform, false);      // sidewalk walkers
            new GameObject("StreetFurniture").AddComponent<StreetFurniture>().transform.SetParent(transform, false); // hydrants, bins, benches, shelters
            new GameObject("RooftopDetails").AddComponent<RooftopDetails>().transform.SetParent(transform, false); // roof clutter: AC, tanks, masts
            race = gameObject.AddComponent<RaceSession>();
            race.CountdownTick += OnCountdownTick;
            race.RaceStarted += () => { hud.BigMessage("GO!", UIKit.Good, 1.2f); Play(beepHigh, 1f); };
            race.LapCompleted += (p, lap, time) => { if (p.IsPlayer && lap < race.TotalLaps) hud.ShowToast($"LAP {lap}  {UIKit.FormatTime(time)}", UIKit.Accent); };
            race.ParticipantFinished += OnParticipantFinished;
            race.RaceFinished += OnRaceFinished;
            race.Respawned += p => { if (p.IsPlayer) hud.ShowToast("RESPAWNED", UIKit.TextDim); };
            uiAudio = gameObject.AddComponent<AudioSource>();
            uiAudio.spatialBlend = 0f;
            beepLow = VehicleAudio.Beep(660f, 0.18f);
            beepHigh = VehicleAudio.Beep(1320f, 0.45f);
            click = VehicleAudio.Beep(1800f, 0.03f);
            if (db != null)
            {
                roadSurfaceRuntime = db.DryAsphalt != null ? Instantiate(db.DryAsphalt) : null;
                if (roadSurfaceRuntime != null) roadSurfaceRuntime.name = "dry_asphalt_runtime";
                // Road colliders share one runtime surface so rain can change grip without editing assets.
                foreach (var s in FindObjectsByType<VehicleSurface>(FindObjectsInactive.Include))
                    if (s.Surface == db.DryAsphalt && roadSurfaceRuntime != null) s.Surface = roadSurfaceRuntime;
                // Roadmap A2: a streamed cell that loads again carries the asset surface; swap it too.
                VeyraCity.World.Streaming.CellStreamer.CellLoaded += OnCellLoaded;
            }
            if (environment != null) environment.WeatherChanged += _ => { };
            ApplySettings();
            BuildMarkers();
            BuildGarage();
            ParseArgs();
            if (tour) return;
            if (demo) StartDemo();
            else ShowMainMenu();
        }

        SaveData NewSave()
        {
            var s = new SaveData();
            if (db != null)
                foreach (var c in db.Cars)
                    if (c.Starter) { s.Cars.Add(new OwnedCar { CarId = c.Id, Paint = c.DefaultPaint }); s.CurrentCar = c.Id; }
            return s;
        }

        string shotDir, reviewDir;

        /// <summary>
        /// Review cameras (playbook S5 style): the chase camera on named roads at noon and at 23:00, then a 30 s autopilot race.
        /// </summary>
        System.Collections.IEnumerator ReviewShots()
        {
            System.IO.Directory.CreateDirectory(reviewDir);
            yield return null;
            EnterFreeRoam(false);
            menu.Hide();
            SetPaused(false);
            traffic.Active = true;
            environment.AutoWeather = false;
            environment.SetWeather(WorldEnvironment.WeatherKind.Clear);
            environment.AnimateTime = false;
            var spots = new (string road, string label, float frac)[] { ("A1", "CrownAvenue", 0.42f), ("A2", "MeridianBoulevard", 0.55f), ("A7", "LumenAvenue_NeonMile", 0.5f) };
            foreach (float hour in new[] { 12f, 23f })
                foreach (var spot in spots)
                {
                    RoadPath road = null;
                    foreach (var r in WorldData.Roads) if (r.Data.id == spot.road) road = r;
                    if (road == null || player == null) continue;
                    float s = road.Path.Length * spot.frac;
                    var t = road.Path.TangentAt(s);
                    var right = new Vector3(t.z, 0f, -t.x).normalized;
                    var pos = road.Path.PositionAt(s) + right * 2.6f;
                    pos.y = WorldData.GroundHeight(pos + Vector3.up * 3f, pos.y) + 0.45f;
                    player.ResetTo(pos, Quaternion.LookRotation(new Vector3(t.x, 0f, t.z)));
                    environment.SetTime(hour);
                    if (chaseCamera != null) chaseCamera.SetTarget(player);
                    yield return new WaitForSecondsRealtime(3.5f);
                    string file = System.IO.Path.Combine(reviewDir, $"review_{spot.label}_{hour:00}00.png");
                    ScreenCapture.CaptureScreenshot(file);
                    Debug.Log("[Review] captured " + file);
                    yield return new WaitForSecondsRealtime(0.6f);
                }
            // rain at night on the Mile
            environment.SetWeather(WorldEnvironment.WeatherKind.Rain);
            yield return new WaitForSecondsRealtime(22f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(reviewDir, "review_LumenAvenue_NeonMile_2300_rain.png"));
            yield return new WaitForSecondsRealtime(0.6f);
            environment.SetWeather(WorldEnvironment.WeatherKind.Clear);
            var def = Career.QuickRaces().Find(r => r.RouteId == "RACE-16");
            StartRace(def, null, null, autopilot: true);
            Debug.Log("[Review] 30 s autopilot race started");
            for (int k = 0; k < 3; k++)
            {
                yield return new WaitForSecondsRealtime(10f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(reviewDir, $"review_race_{k}.png"));
            }
            Debug.Log("[Review] done");
        }

        string carShotDir, iterShotDir, iterCar = "stanton_sidewinder";

        /// <summary>
        /// Visual iteration capture (-veyraIterShots &lt;dir&gt; [-veyraIterCar id]): the same eight scenes every time, each
        /// driven on real roads by a road-following script so the camera, motion blur and effects are live:
        /// it1 noon city (A2), it2 golden hour (A2), it3 neon night (A7), it4 rain night (A7), it5 nitro at speed (C1),
        /// it6 brake-tap drift (C1), it7 evening traffic (A1), it8 close three-quarter of the car at dusk.
        /// </summary>
        System.Collections.IEnumerator IterationShots()
        {
            System.IO.Directory.CreateDirectory(iterShotDir);
            yield return null;
            EnterFreeRoam(false);
            menu.Hide();
            SetPaused(false);
            environment.AutoWeather = false;
            environment.AnimateTime = false;
            if (Array.Exists(db.Cars, c => c.Id == iterCar)) save.CurrentCar = iterCar;
            var log = new System.Text.StringBuilder("[Iter]");
            var cam = chaseCamera != null ? chaseCamera.GetComponent<Camera>() : Camera.main;
            var perf = new System.Text.StringBuilder("[IterPerf]");
            var frameMs = new List<float>(1024);
            // A/B switches for frame-time hunting: -veyraIterOff trees,probe,parked,lamps,signals
            var off = new HashSet<string>();
            var argv = Environment.GetCommandLineArgs();
            for (int a = 0; a + 1 < argv.Length; a++)
                if (argv[a] == "-veyraIterOff") foreach (var k in argv[a + 1].Split(',')) off.Add(k.Trim().ToLowerInvariant());
            if (off.Count > 0) perf.Append(" off=" + string.Join("+", off));
            var treesSys = GetComponentInChildren<StreetTrees>();
            if (treesSys != null && off.Contains("trees")) treesSys.Active = false;
            var parkedSys = GetComponentInChildren<ParkedCars>();
            if (parkedSys != null && off.Contains("parked")) parkedSys.Active = false;
            var signalsSys = GetComponentInChildren<TrafficLights>();
            if (signalsSys != null && off.Contains("signals")) signalsSys.gameObject.SetActive(false);
            var pedsSys = GetComponentInChildren<Pedestrians>();
            if (pedsSys != null && off.Contains("peds")) pedsSys.Active = false;
            var furnSys = GetComponentInChildren<StreetFurniture>();
            if (furnSys != null && off.Contains("furniture")) furnSys.gameObject.SetActive(false);
            if (off.Contains("rooftops")) { var rt = GetComponentInChildren<RooftopDetails>(true); if (rt != null) rt.gameObject.SetActive(false); }
            // In-run A/B (thermal-neutral, iteration 7): -veyraIterAB <peds|trees|parked|furniture|sky|probe> switches the
            // feature on and off every 0.8 s inside every scene and splits the frame times.
            string abFeature = null;
            for (int a = 0; a + 1 < argv.Length; a++) if (argv[a] == "-veyraIterAB") abFeature = argv[a + 1].Trim().ToLowerInvariant();
            var abOn = new List<float>(4096);
            var abOff = new List<float>(4096);
            // P1 (round 3): managed allocations per frame (the audit measured 14.7 KB/frame after round 2, 3.3 in V0)
            var gcRec = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            long gcSum = 0, gcOnSum = 0, gcOffSum = 0; int gcN = 0, gcOnN = 0, gcOffN = 0;
            void SetFeature(bool on)
            {
                switch (abFeature)
                {
                    case "peds": if (pedsSys != null) pedsSys.gameObject.SetActive(on); break;
                    case "trees": if (treesSys != null) treesSys.enabled = on; break;
                    case "parked": if (parkedSys != null) parkedSys.gameObject.SetActive(on); break;
                    case "furniture": if (furnSys != null) furnSys.gameObject.SetActive(on); break;
                    case "sky": { var sky = GetComponentInChildren<SkyDome>(true); if (sky != null) sky.gameObject.SetActive(on); break; }
                    case "rooftops": { var rt = GetComponentInChildren<RooftopDetails>(true); if (rt != null) rt.gameObject.SetActive(on); break; }
                    case "atmo": AtmosphereScatter.Enabled = on; break;
                    // iteration 30: round-2 systems
                    case "roadwear": if (roadWear != null) roadWear.enabled = on; break;
                    case "skyrefl": { var sr = GetComponent<SkyReflection>(); if (sr != null) sr.enabled = on; var pr = sr != null ? sr.GetComponentInChildren<ReflectionProbe>(true) : null; if (pr != null) pr.enabled = on; break; }
                    case "carprobe": { var pr = player != null ? player.GetComponentInChildren<ReflectionProbe>(true) : null; if (pr != null) pr.enabled = on; break; }
                    case "hud": hud.Visible = on; break;
                    // Phase E follow-up (D2): pause the traffic root like peds/parked (TrafficSystem.Active=false would clear and respawn every car)
                    case "traffic": if (traffic != null) traffic.gameObject.SetActive(on); break;
                    case "probe": { var pr = player != null ? player.GetComponentInChildren<ReflectionProbe>() : null; if (pr != null) pr.enabled = on; break; }
                }
            }

            RoadPath FindRoad(string id)
            {
                foreach (var r in WorldData.Roads) if (r.Data.id == id) return r;
                return null;
            }
            // Spawns on the road and drives it: pure pursuit on the lane centre, speed held by throttle.
            System.Collections.IEnumerator Drive(string roadId, float frac, float kmh, float hour, WorldEnvironment.WeatherKind weather,
                                                 bool traffic, float seconds, string shot, Func<float, DriverIntent, DriverIntent> extra = null, float lane = 2.6f, bool standing = false)
            {
                var road = FindRoad(roadId);
                if (road == null) { log.Append($" {shot}:no-road"); yield break; }
                environment.SetWeather(weather);
                environment.SetTime(hour);
                this.traffic.Clear();            // iteration 5: cars left from the previous scene sat on the spawn point (it2 crash)
                this.traffic.Active = traffic;
                float s0 = road.Path.Length * frac;
                var t0 = road.Path.TangentAt(s0);
                var right0 = new Vector3(t0.z, 0f, -t0.x).normalized;
                var p0 = road.Path.PositionAt(s0) + right0 * lane;
                p0.y = WorldData.GroundHeight(p0 + Vector3.up * 3f, p0.y) + 0.45f;
                SpawnPlayer(new Pose(p0, Quaternion.LookRotation(new Vector3(t0.x, 0f, t0.z))));
                if (off.Contains("probe")) { var pr = player.GetComponentInChildren<ReflectionProbe>(); if (pr != null) Destroy(pr.gameObject); Destroy(player.GetComponent<CarReflectionProbe>()); }
                if (off.Contains("lamps")) Destroy(player.GetComponent<CarLampFX>());
                playerNitro.ResetState(NitroSystem.MeterMax);
                if (!standing) player.ForceForwardSpeed(kmh / 3.6f * 0.8f);
                var script = new ScriptedDriver { Vehicle = player };
                playerDriver.Bind(player, script, db.Casual);
                float start = Time.time;
                float sAlong = s0;
                int hint = 0;
                road.Path.Project(p0, ref hint, 0);
                script.Policy = v =>
                {
                    var pos = v.transform.position;
                    sAlong = Mathf.Max(sAlong, road.Path.Project(pos, ref hint, 20));
                    float look = 12f + v.State.Speed * 0.7f;
                    var tgtT = road.Path.TangentAt(sAlong + look);
                    var target = road.Path.PositionAt(sAlong + look) + new Vector3(tgtT.z, 0f, -tgtT.x).normalized * lane;
                    var to = Vector3.ProjectOnPlane(target - pos, Vector3.up);
                    float ang = Vector3.SignedAngle(Vector3.ProjectOnPlane(v.transform.forward, Vector3.up), to, Vector3.up);
                    var i = new DriverIntent { Steer = Mathf.Clamp(ang / 25f, -1f, 1f) };
                    // keep a gap to anything ahead in the lane (traffic), like a driver would
                    float want = kmh;
                    if (Physics.SphereCast(pos + Vector3.up * 0.7f, 0.9f, v.transform.forward, out var ahead, 12f + v.State.Speed * 1.4f, ~(1 << CarReflectionProbe.CarLayer), QueryTriggerInteraction.Ignore)
                        && ahead.rigidbody != null && ahead.rigidbody != v.Body)
                        want = Mathf.Min(kmh, Mathf.Max(0f, (ahead.distance - 8f) * 2.2f));
                    float err = want - v.State.SpeedKmh;
                    i.Throttle = Mathf.Clamp01(err / 15f + 0.25f);
                    i.Brake = err < -12f ? Mathf.Clamp01(-err / 30f) : 0f;
                    return extra != null ? extra(Time.time - start, i) : i;
                };
                // frame times after a 1 s settle (spawn and streaming hitches excluded)
                frameMs.Clear();
                float maxSlip = 0f, t100 = -1f;
                var trace = shot.Contains("drift") ? new System.Text.StringBuilder(" trace:") : null;
                var launch = standing ? new System.Text.StringBuilder(" launch:") : null;
                float nextLaunch = 0.5f;
                float nextTrace = 2.4f;
                while (Time.time - start < seconds)
                {
                    if (Time.time - start > 1f) { frameMs.Add(Time.unscaledDeltaTime * 1000f); maxSlip = Mathf.Max(maxSlip, Mathf.Abs(player.State.SlipAngleDeg)); if (gcRec.Valid) { gcSum += gcRec.LastValue; gcN++; } }
                    if (standing && t100 < 0f && player.State.SpeedKmh >= 100f) t100 = Time.time - start;
                    if (trace != null && Time.time - start >= nextTrace)
                    {
                        nextTrace += 0.1f;
                        trace.Append($"{player.State.SlipAngleDeg:0}{(player.State.ArcadeDrift ? "d" : "")},");
                    }
                    if (standing && Time.time - start >= nextLaunch)
                    {
                        // launch trace (iteration 9): speed, gear, rpm, applied throttle, rear wheel slip ratio
                        nextLaunch += 0.5f;
                        var ls = player.State;
                        launch.Append($" {Time.time - start:0.0}s:{ls.SpeedKmh:0}/g{ls.Gear}/{ls.EngineRpm:0}rpm/th{ls.AppliedThrottle:0.00}/sr{player.GetWheel(2).SlipRatio:0.00}");
                    }
                    if (abFeature != null && Time.time - start > 1f)
                    {
                        float tt = Time.time - start - 1f;
                        bool on = ((int)(tt / 0.8f) & 1) == 0;
                        SetFeature(on);
                        if (tt % 0.8f > 0.2f)
                        {
                            (on ? abOn : abOff).Add(Time.unscaledDeltaTime * 1000f);
                            if (gcRec.Valid) { if (on) { gcOnSum += gcRec.LastValue; gcOnN++; } else { gcOffSum += gcRec.LastValue; gcOffN++; } }
                        }
                    }
                    yield return null;
                }
                if (frameMs.Count > 0)
                {
                    frameMs.Sort();
                    float sum = 0f;
                    foreach (var f in frameMs) sum += f;
                    perf.Append($" {shot}:avg{sum / frameMs.Count:0.0}/p95 {frameMs[(int)(frameMs.Count * 0.95f)]:0.0}/max{frameMs[frameMs.Count - 1]:0}ms");
                }
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(iterShotDir, shot + ".png"));
                var st = player.State;
                log.Append($" {shot}:{st.SpeedKmh:0}km/h,g{st.Gear}/{st.EngineRpm:0}rpm,slip{st.SlipAngleDeg:0}(max{maxSlip:0}){(st.ArcadeDrift ? ",drift" : "")}");
                if (trace != null) log.Append(trace);
                if (launch != null) log.Append(launch);
                if (standing) log.Append($" 0-100:{(t100 < 0f ? "n/a" : t100.ToString("0.0") + "s")},surface:{(player.GetWheel(2).Surface != null ? player.GetWheel(2).Surface.Id : "default")}");
                yield return null;
            }

            var clear = WorldEnvironment.WeatherKind.Clear;
            yield return Drive("A2", 0.35f, 70f, 12f, clear, true, 5f, "it1_noon_city");
            // it20 (iteration 25): the road surface from 1.5 m, beside the car (wheel paths, oil, gutters, patches)
            if (chaseCamera != null) chaseCamera.enabled = false;
            {
                var rt0 = player.transform;
                cam.transform.position = rt0.position + rt0.right * 2.8f + Vector3.up * 1.5f - rt0.forward * 1.5f;
                cam.transform.LookAt(rt0.position + rt0.forward * 22f + rt0.right * 1.5f);
                cam.fieldOfView = 55f;
                yield return null;
                yield return null;
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(iterShotDir, "it20_road.png"));
                yield return null;
                // iteration 27: straight down from 5 m (no grazing-angle sky reflection hiding the asphalt's albedo)
                cam.transform.position = rt0.position + rt0.forward * 9f + rt0.right * 1.5f + Vector3.up * 5f;
                cam.transform.rotation = Quaternion.LookRotation(Vector3.down, rt0.forward);
                cam.fieldOfView = 70f;
                yield return null;
                yield return null;
                // iteration 30 control test: a 10 m patch with hand-made UVs (UV0 in metres, UV1.x 0-1 across, 4 lanes) and the
                // worn asphalt material, laid in the shot; if it shows wear and patches the shader works and the city meshes
                // are the problem, if not the material/shader path is
                GameObject testPatch = null;
                Material wornMat = null;
                foreach (var mr in FindObjectsByType<MeshRenderer>())
                    if (mr.sharedMaterial != null && mr.sharedMaterial.name == "M_Asphalt_Worn") { wornMat = mr.sharedMaterial; break; }
                if (wornMat != null)
                {
                    testPatch = new GameObject("RoadWearControl");
                    var c0 = rt0.position + rt0.forward * 9f + rt0.right * 1.5f;
                    c0.y = WorldData.GroundHeight(c0 + Vector3.up * 3f, c0.y) + 0.04f;
                    testPatch.transform.position = c0;
                    testPatch.transform.rotation = Quaternion.LookRotation(rt0.forward, Vector3.up);
                    var tm = new Mesh { name = "RoadWearControl" };
                    tm.vertices = new[] { new Vector3(-2.5f, 0, -5f), new Vector3(2.5f, 0, -5f), new Vector3(-2.5f, 0, 5f), new Vector3(2.5f, 0, 5f) };
                    tm.uv = new[] { new Vector2(0, 0), new Vector2(5, 0), new Vector2(0, 10), new Vector2(5, 10) };
                    tm.uv2 = new[] { new Vector2(0, 4), new Vector2(1, 4), new Vector2(0, 4), new Vector2(1, 4) };
                    tm.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                    tm.RecalculateNormals();
                    tm.RecalculateTangents();
                    tm.RecalculateBounds();
                    testPatch.AddComponent<MeshFilter>().sharedMesh = tm;
                    testPatch.AddComponent<MeshRenderer>().sharedMaterial = wornMat;
                    yield return null;
                    yield return null;
                }
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(iterShotDir, "it20b_road_top.png"));
                yield return null;
                if (testPatch != null) Destroy(testPatch);
                // iteration 32 diagnostic: hide every renderer with an asphalt / concrete-deck slot near the spot; whatever
                // still draws the street there is what the camera really sees
                var hidden = new List<Renderer>();
                var spot = rt0.position + rt0.forward * 9f;
                foreach (var mr in FindObjectsByType<MeshRenderer>())
                {
                    if (!mr.enabled || !mr.bounds.Intersects(new Bounds(spot, new Vector3(60f, 40f, 60f)))) continue;
                    foreach (var m in mr.sharedMaterials)
                        if (m != null && (m.name.Contains("Asphalt") || m.name.Contains("Concrete_Deck"))) { mr.enabled = false; hidden.Add(mr); break; }
                }
                yield return null;
                yield return null;
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(iterShotDir, "it20c_road_hidden.png"));
                yield return null;
                var hiddenNames = new System.Text.StringBuilder();
                foreach (var r in hidden) { if (r == null) continue; r.enabled = true; if (hiddenNames.Length < 300) hiddenNames.Append(r.name).Append(','); }
                log.Append($" roadHidden:{hidden.Count}[{hiddenNames}]");
                // which renderers draw the road here, with which material, and do their meshes carry UV1 yet
                var probePt = rt0.position + rt0.forward * 10f;
                var sb = new System.Text.StringBuilder(" roadUnder:");
                if (Physics.Raycast(probePt + Vector3.up * 5f, Vector3.down, out var gHit, 12f, ~(1 << CarReflectionProbe.CarLayer), QueryTriggerInteraction.Ignore))
                {
                    sb.Append($"(collider {gHit.collider.name}");
                    // the renderer that owns that collider (eased ribbons keep theirs on a child), with its slots and UV1 state
                    var owner = gHit.collider.GetComponentInParent<MeshRenderer>();
                    if (owner == null && gHit.collider.transform.parent != null) owner = gHit.collider.transform.parent.GetComponent<MeshRenderer>();
                    if (owner != null)
                    {
                        var omf = owner.GetComponent<MeshFilter>();
                        var om = omf != null ? omf.sharedMesh : null;
                        sb.Append($" on {owner.name}: ");
                        foreach (var m in owner.sharedMaterials) if (m != null) sb.Append($"{m.name}/tw{m.GetFloat("_TyreWear"):0.0} ");
                        sb.Append($"uv2 {(om != null && om.isReadable ? om.uv2.Length : -1)}/{(om != null ? om.vertexCount : 0)}");
                        if (om != null && om.isReadable && om.uv2.Length > 0) { var u2 = om.uv2; sb.Append($" e.g. {u2[0].x:0.00},{u2[0].y:0} {u2[1].x:0.00},{u2[1].y:0}"); }
                    }
                    sb.Append(")");
                }
                foreach (var mr in FindObjectsByType<MeshRenderer>())
                {
                    var bb = mr.bounds;
                    if (probePt.x < bb.min.x || probePt.x > bb.max.x || probePt.z < bb.min.z || probePt.z > bb.max.z) continue;
                    if (Mathf.Abs(probePt.y - bb.center.y) > bb.extents.y + 6f) continue;
                    var mf = mr.GetComponent<MeshFilter>();
                    var mm = mr.sharedMaterial;
                    if (mm == null || mm.shader == null || !mm.shader.name.StartsWith("Veyra/Road")) continue;
                    var mesh = mf != null ? mf.sharedMesh : null;
                    sb.Append($"[{mr.name}|{mm.name}|tw{mm.GetFloat("_TyreWear"):0.0}|pa{mm.GetFloat("_PatchAmount"):0.0}|uv2:{(mesh != null && mesh.isReadable ? mesh.uv2.Length : -1)}/{(mesh != null ? mesh.vertexCount : 0)}]");
                }
                log.Append(sb);
            }
            if (chaseCamera != null) chaseCamera.enabled = true;
            cam.fieldOfView = 60f;
            yield return Drive("A2", 0.35f, 70f, 19.15f, clear, true, 5f, "it2_golden_hour");   // iteration 21: 19.15 h (18.6 h had the sun at 19 deg, before the golden light)
            yield return Drive("A7", 0.3f, 60f, 23f, clear, true, 5f, "it3_neon_night");
            environment.SetWetness(1f);
            yield return Drive("A7", 0.3f, 60f, 23f, WorldEnvironment.WeatherKind.Rain, true, 6f, "it4_rain_night");
            environment.SetWetness(0f);   // dry roads again for the daytime scenes
            yield return Drive("C1", 0.3f, 160f, 13f, clear, false, 6f, "it5_nitro_speed",
                               // double tap on a full meter = Overdrive (shockwave burst), then hold
                               (t, i) => { if (t > 2.5f) { i.Nitro = !(t > 2.6f && t < 2.72f); i.Throttle = 1f; i.Brake = 0f; } return i; }, 1.4f);   // iteration 13: never brake (a brake tap while steering starts a drift and cancels nitro)
            yield return Drive("C1", 0.5f, 105f, 16f, clear, false, 3.45f, "it6_drift",   // iteration 11: middle of the carriageway, held drift before any barrier
                               (t, i) => { if (t > 2.6f && t < 2.75f) { i.Brake = 1f; i.Steer = 0.8f; } else if (t >= 2.75f) { i.Steer = 0.8f; i.Throttle = 1f; } return i; }, 6f);
            yield return Drive("A1", 0.45f, 55f, 17.2f, clear, true, 6f, "it7_traffic_evening");
            // it8: close three-quarter of the parked car at dusk
            environment.SetTime(19.2f);
            yield return Drive("A2", 0.6f, 30f, 19.2f, clear, false, 2f, "it8_tmp", (t, i) => { i.Throttle = 0f; i.Brake = 1f; return i; });
            if (chaseCamera != null) chaseCamera.enabled = false;
            var ct = player.transform;
            cam.transform.position = ct.position + ct.forward * 4.4f + ct.right * -3.2f + Vector3.up * 1.15f;
            cam.transform.LookAt(ct.position + Vector3.up * 0.6f);
            cam.fieldOfView = 45f;
            yield return new WaitForSeconds(0.5f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(iterShotDir, "it8_car_closeup.png"));
            yield return null;
            if (chaseCamera != null) chaseCamera.enabled = true;
            System.IO.File.Delete(System.IO.Path.Combine(iterShotDir, "it8_tmp.png"));
            // it9: swerve into the roadside at speed, then look at the damage up close
            yield return Drive("A2", 0.5f, 75f, 15.5f, clear, false, 3.4f, "it9_crash", (t, i) => { if (t > 1.5f) { i.Steer = 1f; i.Throttle = 1f; i.Brake = 0f; } return i; });
            if (chaseCamera != null) chaseCamera.enabled = false;
            var cr = player.transform;
            // iteration 8: stop the car first (it was still sliding at 40 km/h and drove into the close-up camera)
            player.Body.linearVelocity = Vector3.zero;
            player.Body.angularVelocity = Vector3.zero;
            // iteration 7: the first clear viewpoint on a 5.5 m circle around the car, front three-quarter first
            // (iterations 3-6 put the camera inside the shop front or the car itself)
            Vector3 aim = cr.position + Vector3.up * 0.6f;
            Vector3 best = cr.position + cr.forward * 4f - cr.right * 4f + Vector3.up * 1.5f;
            foreach (float yaw in new[] { -45f, 45f, -90f, 90f, -135f, 135f, 0f, 180f })
            {
                Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * cr.forward;
                Vector3 c = cr.position + dir * 5.5f + Vector3.up * 1.5f;
                if (!Physics.Linecast(aim, c, ~(1 << CarReflectionProbe.CarLayer), QueryTriggerInteraction.Ignore)) { best = c; break; }
            }
            cam.transform.position = best;
            cam.transform.LookAt(aim);
            cam.fieldOfView = 45f;
            yield return new WaitForSeconds(0.4f);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(iterShotDir, "it9_crash_closeup.png"));
            yield return null;
            if (chaseCamera != null) chaseCamera.enabled = true;
            // it17 (round 2): a hard stop from 200 km/h: brake-light streaks, BRAKE/ABS lamps and the G-meter in the chase
            // view, then the glowing front disc up close
            // BUG-151: log what the stop does to the tyres (peak slip ratio, ABS frames, smoke) to find the haze's cause
            float slip17 = 0f; int abs17 = 0, frames17 = 0;
            bool reset17 = false;
            yield return Drive("C1", 0.25f, 200f, 19.7f, clear, false, 3.7f, "it17_brake",   // iteration 24: at dusk, where glowing discs and light trails read
                               (t, i) =>
                               {
                                   if (t > 2.6f)
                                   {
                                       i.Throttle = 0f; i.Brake = 1f; i.Nitro = false;
                                       if (!reset17) { reset17 = true; player.GetComponent<VehicleFX>()?.ResetSmokePeak(); }   // Drive respawns the car: look it up now
                                       frames17++;
                                       if (player.State.AbsActive) abs17++;
                                       for (int w = 0; w < 4; w++) slip17 = Mathf.Max(slip17, Mathf.Abs(player.GetWheel(w).SlipRatio));
                                   }
                                   return i;
                               }, 1.4f);
            var fx17 = player.GetComponent<VehicleFX>();
            var dmg17 = player.GetComponent<CarDamage>();
            log.Append($" brakeStop:slipMax{slip17:0.00}/abs{abs17}of{frames17}/tyreSmokePeak{(fx17 != null ? fx17.SmokePeak : -1f):0.00}" +
                       $"/tyreSmoke{(fx17 != null ? fx17.SmokeParticles : -1)}/damage{(dmg17 != null ? dmg17.Damage01 : -1f):0.00}/engineSmoke{(dmg17 != null ? dmg17.EngineSmokeParticles : -1)}");
            if (chaseCamera != null) chaseCamera.enabled = false;
            {
                var bt = player.transform;
                player.Body.linearVelocity = Vector3.zero;
                player.Body.angularVelocity = Vector3.zero;
                // iteration 20: re-park in the right-hand lane of the nearest road (iteration 19's stop ended against a
                // wall, so both sides of the car were blocked); the disc heat is kept
                if (WorldData.NearestRoad(bt.position, 80f, out var park))
                {
                    var tng = new Vector3(park.Tangent.x, 0f, park.Tangent.z).normalized;
                    var rgt = new Vector3(tng.z, 0f, -tng.x);
                    var spot = park.Point + rgt * (WorldData.Roads[park.Road].Data.width * 0.28f);
                    float ride = bt.position.y - WorldData.GroundHeight(bt.position + Vector3.up * 3f, bt.position.y - 0.35f, ~(1 << CarReflectionProbe.CarLayer));
                    spot.y = WorldData.GroundHeight(spot + Vector3.up * 3f, spot.y, ~(1 << CarReflectionProbe.CarLayer)) + Mathf.Clamp(ride, 0f, 1.2f);
                    player.Body.isKinematic = true;
                    player.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(tng, Vector3.up));
                    player.Body.position = spot;
                    player.Body.rotation = player.transform.rotation;
                    yield return null;
                }
                // iteration 19: the side with a clear view (iteration 18 put the camera behind the median barrier)
                Vector3 wheelPos = bt.position, camPos = bt.position;
                foreach (float side in new[] { -1f, 1f })
                {
                    wheelPos = bt.position + bt.forward * (player.Definition.Wheelbase * 0.5f) + bt.right * (side * player.Definition.Front.TrackWidth * 0.5f) + Vector3.up * 0.35f;
                    camPos = wheelPos + bt.right * (side * 2.3f) + bt.forward * 0.7f + Vector3.up * 0.2f;
                    if (!Physics.Linecast(wheelPos + bt.right * (side * 0.4f), camPos, ~(1 << CarReflectionProbe.CarLayer), QueryTriggerInteraction.Ignore)) break;
                }
                cam.transform.position = camPos;
                cam.transform.LookAt(wheelPos - Vector3.up * 0.25f);
                cam.fieldOfView = 40f;
                yield return null;
                yield return null;
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(iterShotDir, "it17_brake_disc.png"));
                yield return null;
                player.Body.isKinematic = false;
                var dfx = player.GetComponent<CarDetailFX>();
                log.Append($" brakeHeatFL:{(dfx != null ? dfx.Heat01(0) : -1f):0.00}");
            }
            if (chaseCamera != null) chaseCamera.enabled = true;
            cam.fieldOfView = 60f;
            // it19 (iteration 23): the nitro effects while boosting (it5 is captured after the meter has run out)
            yield return Drive("C1", 0.3f, 190f, 18.9f, clear, false, 2.2f, "it19_nitro_fx",
                               (t, i) => { if (t > 1.2f) { i.Nitro = true; i.Throttle = 1f; i.Brake = 0f; } return i; }, 1.4f);
            // it21 (iteration 26): through a speed camera at full chat (banner, flash, stars)
            {
                // the location's "road" is a street name, not an id: take the road nearest the camera (iteration 26)
                LocationJson trap = null;
                RoadPath tr = null;
                float sT = 0f;
                foreach (var l in WorldData.Locations)
                    if (l.category == "Speed trap" && WorldData.NearestRoad(l.Position, 60f, out var th))
                    {
                        var cand = WorldData.Roads[th.Road];
                        if (cand.Path.Closed || th.S < 260f) continue;
                        trap = l; tr = cand; sT = th.S;
                        break;
                    }
                if (trap != null)
                {
                    float s0 = Mathf.Max(0f, sT - 210f);
                    int before = SpeedTraps.Triggers;
                    yield return Drive(tr.Data.id, s0 / tr.Path.Length, 230f, 14f, clear, false, 2.5f, "it21_tmp",
                                       (t, i) => { i.Throttle = 1f; i.Brake = 0f; i.Nitro = t > 0.5f; return i; }, 1.4f);
                    float tw2 = Time.realtimeSinceStartup;
                    while (SpeedTraps.Triggers == before && Time.realtimeSinceStartup - tw2 < 5f) yield return null;
                    float tw3 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - tw3 < 0.45f) yield return null;
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(iterShotDir, "it21_speedtrap.png"));
                    yield return null;
                    System.IO.File.Delete(System.IO.Path.Combine(iterShotDir, "it21_tmp.png"));
                    log.Append($" speedTrap:{(SpeedTraps.Triggers > before ? SpeedTraps.LastResult : "missed")}");
                }
                // G5 (round 3): an activity hook fires in the player run: drive past the first garage that sits on a road
                LocationJson gar = null;
                RoadPath gr = null;
                float sG = 0f;
                foreach (var l in WorldData.Locations)
                    if (l.category == "Garage" && WorldData.NearestRoad(l.Position, 25f, out var gh))
                    {
                        var cand = WorldData.Roads[gh.Road];
                        if (cand.Path.Closed || gh.S < 120f) continue;
                        gar = l; gr = cand; sG = gh.S;
                        break;
                    }
                if (gar != null)
                {
                    save.Events.RemoveAll(e => e.Id == "ACT:" + gar.id);   // the 30 s autosave kept earlier runs' discoveries
                    int before = ActivityHooks.Events;
                    yield return Drive(gr.Data.id, Mathf.Max(0f, sG - 90f) / gr.Path.Length, 140f, 10f, clear, false, 2.5f, "it22_tmp",
                                       (t, i) => { i.Throttle = 0.6f; i.Brake = 0f; return i; }, 1.4f);
                    float tw4 = Time.realtimeSinceStartup;
                    while (ActivityHooks.Events == before && Time.realtimeSinceStartup - tw4 < 4f) yield return null;
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(iterShotDir, "it22_activity.png"));
                    yield return null;
                    System.IO.File.Delete(System.IO.Path.Combine(iterShotDir, "it22_tmp.png"));
                    log.Append($" activity:{(ActivityHooks.Events > before ? ActivityHooks.LastEvent : "missed " + gar.id)}");
                }
            }
            var crashDamage = player.GetComponent<CarDamage>();
            float crashDamage01 = crashDamage != null ? crashDamage.Damage01 : -1f;
            // it11: a long cruise with traffic on the longest arterial/collector (streaming hitches: parked cars, furniture
            // cells, walkers); per-system costs for the short scenes and for the cruise are logged separately ([IterCost])
            Debug.Log(VeyraCity.Core.FrameCost.Report().Replace("[IterCost]", "[IterCost] scenes"));
            VeyraCity.Core.FrameCost.Reset();
            string longest = null;
            float longestLen = 0f;
            foreach (var r in WorldData.Roads)
                if ((r.Data.cls == "AR" || r.Data.cls == "CO") && !r.Path.Closed && r.Path.Length > longestLen) { longestLen = r.Path.Length; longest = r.Data.id; }
            if (longest != null)
            {
                var hitchMs = new List<float>(4096);
                float c0 = Time.realtimeSinceStartup;
                var cruise = Drive(longest, 0.03f, 100f, 13f, clear, true, 28f, "it11_cruise");
                while (cruise.MoveNext()) { if (Time.realtimeSinceStartup - c0 > 1.5f) hitchMs.Add(Time.unscaledDeltaTime * 1000f); yield return cruise.Current; }
                int h33 = 0, h50 = 0;
                foreach (var ms in hitchMs) { if (ms > 33f) h33++; if (ms > 50f) h50++; }
                hitchMs.Sort();
                Debug.Log($"[IterCruise] road {longest} ({longestLen:0} m): frames {hitchMs.Count}, p99 {(hitchMs.Count > 0 ? hitchMs[(int)(hitchMs.Count * 0.99f)] : 0):0.0} ms, over33 {h33}, over50 {h50}");
                Debug.Log(VeyraCity.Core.FrameCost.Report().Replace("[IterCost]", "[IterCost] cruise"));
            }
            // it10: standing start on a city arterial (in-game launch check against the EditMode harness numbers)
            if (chaseCamera != null) chaseCamera.enabled = true;
            yield return Drive("A2", 0.2f, 260f, 13f, clear, false, 7f, "it10_launch",
                               (t, i) => { i.Throttle = 1f; i.Brake = 0f; return i; }, 2.6f, true);
            log.Append($" damage:{crashDamage01:0.00}");   // of the it9 crash car (later scenes respawn the player)
            // it14 - it16 (round 2): skyline at golden hour, the river front, the skyline at night (buildings, roofs, water, sky)
            Vector3 groundPos = player != null ? player.transform.position : Vector3.zero;
            Quaternion groundRot = player != null ? player.transform.rotation : Quaternion.identity;
            // terrain height anywhere (WorldData.GroundHeight only probes 30 m above its point)
            float Ground(Vector3 p) => Physics.Raycast(new Vector3(p.x, 900f, p.z), Vector3.down, out var gh, 1200f, ~(1 << CarReflectionProbe.CarLayer), QueryTriggerInteraction.Ignore) ? gh.point.y : 0f;
            if (player != null)
            {
                if (chaseCamera != null) chaseCamera.enabled = false;
                hud.Visible = false;   // iteration 20: clean beauty shots (the cluster showed a parked car's dial)
                environment.SetWeather(clear);
                Vector3 downtown = new Vector3(190f, 0f, -432f);
                var dd = WorldData.DistrictAt(downtown);
                foreach (var d in WorldData.Districts?.districts ?? new DistrictJson[0])
                    if (d.name.Contains("Downtown") && d.polygon != null && d.polygon.Length >= 6)
                    {
                        float sx = 0f, sz = 0f; int n = d.polygon.Length / 2;
                        for (int k = 0; k < n; k++) { sx += d.polygon[k * 2]; sz += d.polygon[k * 2 + 1]; }
                        downtown = new Vector3(sx / n, 0f, sz / n);
                    }
                Vector3 eye = downtown + new Vector3(-520f, 0f, -380f);
                eye.y = Ground(eye) + 90f;
                Vector3 look = downtown + Vector3.up * 40f;
                foreach (var (hour, shot) in new[] { (19.3f, "it14_skyline_golden"), (22.5f, "it16_skyline_night") })
                {
                    environment.SetTime(hour);
                    player.transform.position = eye + Vector3.up * 400f;   // keep streaming centred near the view
                    player.Body.position = player.transform.position;
                    player.Body.isKinematic = true;
                    cam.transform.position = eye;
                    cam.transform.LookAt(look);
                    cam.fieldOfView = 55f;
                    float tw0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - tw0 < 2.5f) yield return null;   // rooftop and furniture cells build
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(iterShotDir, shot + ".png"));
                    yield return null;
                }
                // river front: a low view along the river's middle reach
                var river = WorldData.Districts?.river;
                if (river != null && river.Length >= 8)
                {
                    int mid = (river.Length / 4) * 2;
                    var a0 = new Vector3(river[mid], 0f, river[mid + 1]);
                    var a1 = new Vector3(river[mid + 2], 0f, river[mid + 3]);
                    var along = (a1 - a0).normalized;
                    var side = new Vector3(along.z, 0f, -along.x);
                    // iteration 19: above the water near one bank, looking down the river (iteration 18 sat inside the
                    // bank's grass at the 2 m fallback height and looked up through the clumps)
                    var bank = a0 + side * 12f;
                    float water = Mathf.Min(Ground(a0), Ground(a0 + along * 60f));
                    bank.y = Mathf.Max(water, Ground(bank)) + 7f;
                    environment.SetTime(19.0f);
                    player.transform.position = bank + Vector3.up * 400f;
                    player.Body.position = player.transform.position;
                    cam.transform.position = bank;
                    cam.transform.LookAt(a0 + along * 160f + Vector3.up * (water - bank.y + 4f));
                    cam.fieldOfView = 60f;
                    float tw1 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - tw1 < 2f) yield return null;
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(iterShotDir, "it15_riverfront.png"));
                    yield return null;
                    // iteration 29: the same view at night (city glow on the water)
                    environment.SetTime(22.5f);
                    float tw4 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - tw4 < 1.5f) yield return null;
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(iterShotDir, "it15b_river_night.png"));
                    yield return null;
                }
                // back on the road where the car was (the map scenes route from the car's position)
                player.transform.SetPositionAndRotation(groundPos, groundRot);
                player.Body.position = groundPos;
                player.Body.rotation = groundRot;
                player.Body.isKinematic = false;
                player.Body.linearVelocity = Vector3.zero;
                hud.Visible = true;
                if (chaseCamera != null) chaseCamera.enabled = true;
                cam.fieldOfView = 60f;
                for (int f = 0; f < 3; f++) yield return null;
            }
            // it12 / it13: the world map (aerial base, roads, labels, icons), then a waypoint with its GPS route
            if (worldMap != null && player != null)
            {
                environment.SetTime(13f);
                worldMap.Open(player.transform);
                SetPaused(true);
                float tw = Time.realtimeSinceStartup;
                while (!worldMap.AerialReady && Time.realtimeSinceStartup - tw < 8f) yield return null;
                var pp = player.transform.position;
                worldMap.Frame(new Vector2(pp.x + 300f, pp.z), 3.6f);
                for (int f = 0; f < 3; f++) yield return null;
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(iterShotDir, "it12_map.png"));
                yield return null;
                // iteration 19: the waypoint on the road point nearest 1.4 km north-east (iteration 18 picked a hillside)
                Vector3 target = pp + new Vector3(1100f, 0f, 900f), wp = target;
                float bestD = float.MaxValue;
                foreach (var r in WorldData.Roads)
                    for (float s = 0f; s < r.Path.Length; s += 40f)
                    {
                        var q = r.Path.PositionAt(s);
                        float d = (new Vector2(q.x, q.z) - new Vector2(target.x, target.z)).sqrMagnitude;
                        if (d < bestD) { bestD = d; wp = q; }
                    }
                worldMap.PlaceWaypoint(wp);
                worldMap.Frame(new Vector2((pp.x + wp.x) * 0.5f, (pp.z + wp.z) * 0.5f), 1.25f);
                for (int f = 0; f < 3; f++) yield return null;
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(iterShotDir, "it13_map_route.png"));
                yield return null;
                // iteration 25: the atlas style of the same area, a little further out
                worldMap.SetView(true);
                worldMap.Frame(new Vector2((pp.x + wp.x) * 0.5f, (pp.z + wp.z) * 0.5f), 2.2f);
                for (int f = 0; f < 3; f++) yield return null;
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(iterShotDir, "it13b_map_atlas.png"));
                yield return null;
                worldMap.SetView(false);
                // frame time with the map open (iteration 18's overlay read 16-17 FPS right after the route solve)
                float mapMs = 0f;
                for (int f = 0; f < 60; f++) { yield return null; mapMs += Time.unscaledDeltaTime * 1000f / 60f; }
                log.Append($" roadWear:{RoadWear.MeshesDone}meshes/{RoadWear.VerticesDone}v skyRefl:{SkyReflection.Renders} mapAerial:{worldMap.AerialReady} route:{WorldMap.RouteLength:0}m roadNodes:{RoadGraph.NodeCount} mapBuildings:{WorldMap.BuildingCount} mapFrame:{mapMs:0.0}ms");
                worldMap.Close();
                SetPaused(false);
                // it18 (iteration 22): back in the car with the waypoint set: the GPS panel and the route on the minimap
                // (iteration 23: the launch scene's full-throttle script was still bound and lit up the tyres)
                playerDriver.Bind(player, new ScriptedDriver { Vehicle = player, Policy = v => new DriverIntent { Handbrake = 1f } }, db.Casual);   // iteration 24: a held brake engaged reverse
                player.Body.linearVelocity = Vector3.zero;
                float tg = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - tg < 1.2f) yield return null;
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(iterShotDir, "it18_gps.png"));
                yield return null;
                log.Append($" gps:{(WorldMap.HasInstruction ? (WorldMap.Instruction.Turn < 0 ? "L" : WorldMap.Instruction.Turn > 0 ? "R" : "S") + WorldMap.Instruction.Distance.ToString("0") + "m/" + WorldMap.Instruction.Street : "none")}");
            }
            log.Append($" zebraCrossings:{Pedestrians.CrossingsStarted}");
            Debug.Log(log.ToString());
            Debug.Log(perf.ToString());
            Debug.Log($"[IterGC] driving scenes: {(gcN > 0 ? gcSum / 1024f / gcN : 0f):0.0} KB/frame over {gcN} frames (recorder {(gcRec.Valid ? "valid" : "unavailable")})");
            gcRec.Dispose();
            if (abFeature != null)
            {
                SetFeature(true);
                string Stat(List<float> l)
                {
                    if (l.Count == 0) return "n/a";
                    l.Sort();
                    float sum = 0f;
                    foreach (var x in l) sum += x;
                    return $"avg {sum / l.Count:0.00} ms, p95 {l[(int)(l.Count * 0.95f)]:0.0} ms ({l.Count} frames)";
                }
                Debug.Log($"[IterAB] {abFeature}: on {Stat(abOn)} | off {Stat(abOff)} | GC on {(gcOnN > 0 ? gcOnSum / 1024f / gcOnN : 0f):0.0} KB/frame, off {(gcOffN > 0 ? gcOffSum / 1024f / gcOffN : 0f):0.0} KB/frame");
            }
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit();
        }

        /// <summary>
        /// Cards A8/A9 evidence (-veyraCarShots &lt;dir&gt;): every drivable car with a model on Meridian Boulevard at noon
        /// (chase camera + front three-quarter), then scripted handling runs on the Falcon Apron drift pad (DFT-04):
        /// a brake-tap drift held for 3 s and a nitro run, logged as [Showcase] telemetry and captured.
        /// </summary>
        System.Collections.IEnumerator CarShowcase()
        {
            System.IO.Directory.CreateDirectory(carShotDir);
            yield return null;
            EnterFreeRoam(false);
            menu.Hide();
            SetPaused(false);
            traffic.Active = false;
            environment.AutoWeather = false;
            environment.SetWeather(WorldEnvironment.WeatherKind.Clear);
            environment.AnimateTime = false;
            environment.SetTime(12f);
            Pose RoadPose(string roadId, float frac, float side)
            {
                RoadPath road = null;
                foreach (var r in WorldData.Roads) if (r.Data.id == roadId) road = r;
                if (road == null) return new Pose(Vector3.zero, Quaternion.identity);
                float s = road.Path.Length * frac;
                var t = road.Path.TangentAt(s);
                var right = new Vector3(t.z, 0f, -t.x).normalized;
                var p = road.Path.PositionAt(s) + right * side;
                p.y = WorldData.GroundHeight(p + Vector3.up * 3f, p.y) + 0.45f;
                return new Pose(p, Quaternion.LookRotation(new Vector3(t.x, 0f, t.z)));
            }
            var cam = chaseCamera != null ? chaseCamera.GetComponent<Camera>() : Camera.main;
            string previous = save.CurrentCar;
            int shots = 0;
            foreach (var entry in db.Cars)
            {
                if (entry.Model == null || entry.AIOnly) continue;
                save.CurrentCar = entry.Id;
                SpawnPlayer(RoadPose("A2", 0.55f, 2.6f));
                yield return new WaitForSecondsRealtime(1.6f);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(carShotDir, $"car_{entry.Id}_chase.png"));
                yield return null;
                yield return null;
                if (chaseCamera != null) chaseCamera.enabled = false;
                var ct = player.transform;
                cam.transform.position = ct.position + ct.forward * 4.9f + ct.right * -3.6f + Vector3.up * 1.35f;
                cam.transform.LookAt(ct.position + Vector3.up * 0.65f);
                cam.fieldOfView = 50f;
                yield return null;
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(carShotDir, $"car_{entry.Id}_front34.png"));
                yield return null;
                yield return null;
                if (chaseCamera != null) chaseCamera.enabled = true;
                shots += 2;
                Debug.Log($"[Showcase] {entry.Id}: shots taken");
            }

            // Handling runs on the apron drift pad, heading east.
            foreach (string id in new[] { "v12_goblin", "phoenix_455", "kiri_86" })
            {
                if (!Array.Exists(db.Cars, c => c.Id == id)) continue;
                save.CurrentCar = id;
                var start = new Vector3(2300f, 0f, -400f);
                start.y = WorldData.GroundHeight(start + Vector3.up * 50f, 8f) + 0.45f;
                SpawnPlayer(new Pose(start, Quaternion.LookRotation(Vector3.right)));
                playerNitro.ResetState(NitroSystem.MeterMax);
                var script = new ScriptedDriver { Vehicle = player };
                playerDriver.Bind(player, script, db.Casual);
                float t0 = Time.time;
                script.Policy = v =>
                {
                    float t = Time.time - t0;
                    if (t < 5f) return new DriverIntent { Throttle = 1f };                                   // launch
                    if (t < 5.15f) return new DriverIntent { Steer = 0.8f, Brake = 1f };                      // brake tap
                    if (t < 8.5f) return new DriverIntent { Steer = 0.8f, Throttle = 1f };                    // hold the drift
                    if (t < 9.5f) return new DriverIntent { Throttle = 1f };                                  // straighten up
                    return new DriverIntent { Throttle = 1f, Nitro = true };                                  // nitro
                };
                var sb = new System.Text.StringBuilder($"[Showcase] {id} run:");
                for (float t = 0f; t < 12.5f; t += 0.5f)
                {
                    yield return new WaitForSeconds(0.5f);
                    var st = player.State;
                    sb.Append($" t{t + 0.5f:0.0}={st.SpeedKmh:0}km/h,slip{st.SlipAngleDeg:0}{(st.ArcadeDrift ? ",D" : "")}");
                    if (Mathf.Approximately(t + 0.5f, 7f) || Mathf.Approximately(t + 0.5f, 8f) || Mathf.Approximately(t + 0.5f, 11.5f))
                    {
                        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(carShotDir, $"run_{id}_{t + 0.5f:00.0}s.png"));
                        shots++;
                    }
                }
                Debug.Log(sb.ToString());
            }
            save.CurrentCar = previous;
            Debug.Log($"[Showcase] done: {shots} screenshots in {carShotDir}");
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit();
        }
        float shotEvery = 6f;
        bool tour, noTraffic;

        void ParseArgs()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-veyraDemoRace" && i + 1 < args.Length) { demo = true; demoRaceId = args[i + 1]; }
                if (args[i] == "-veyraDemoFreeRoam") { demo = true; demoRaceId = null; }
                if (args[i] == "-veyraTour") tour = true;
                if (args[i] == "-veyraReviewShots" && i + 1 < args.Length) reviewDir = args[i + 1];
                if (args[i] == "-veyraCarShots" && i + 1 < args.Length) carShotDir = args[i + 1];
                if (args[i] == "-veyraIterShots" && i + 1 < args.Length) iterShotDir = args[i + 1];
                if (args[i] == "-veyraIterCar" && i + 1 < args.Length) iterCar = args[i + 1];
                if (args[i] == "-veyraTimeScale" && i + 1 < args.Length && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ts)) { baseTimeScale = Mathf.Clamp(ts, 0.1f, 8f); Time.timeScale = baseTimeScale; Time.maximumDeltaTime = MaxCatchUpSeconds * baseTimeScale; }
                if (args[i] == "-veyraShots" && i + 1 < args.Length) shotDir = args[i + 1];
                if (args[i] == "-veyraShotEvery" && i + 1 < args.Length) float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out shotEvery);
                // route validation without street life in the lanes (the autopilot does not avoid other cars)
                if (args[i] == "-veyraNoParked") { var pc = GetComponentInChildren<ParkedCars>(); if (pc != null) pc.Active = false; }
                if (args[i] == "-veyraNoTraffic") noTraffic = true;
                // Phase E follow-up (D2): street life off one system at a time, to measure each one's CPU cost on the routes
                if (args[i] == "-veyraNoPeds") { var ps = GetComponentInChildren<Pedestrians>(true); if (ps != null) ps.gameObject.SetActive(false); }
            }
            if (!string.IsNullOrEmpty(shotDir)) StartCoroutine(Shots());
            if (tour) { demo = true; StartCoroutine(Tour()); }
            if (!string.IsNullOrEmpty(reviewDir)) { demo = true; tour = true; StartCoroutine(ReviewShots()); }
            if (!string.IsNullOrEmpty(carShotDir)) { demo = true; tour = true; StartCoroutine(CarShowcase()); }
            if (!string.IsNullOrEmpty(iterShotDir)) { demo = true; tour = true; StartCoroutine(IterationShots()); }
        }

        System.Collections.IEnumerator Shots()
        {
            System.IO.Directory.CreateDirectory(shotDir);
            int n = 0;
            while (true)
            {
                yield return new WaitForSecondsRealtime(Mathf.Max(1f, shotEvery));
                string label = Current.ToString();
                if (Current == State.Race && race.Player != null) label += $"_{race.State}_P{race.Player.Position}";
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(shotDir, $"shot_{n++:000}_{label}.png"));
            }
        }

        /// <summary>Automated walkthrough for build verification: menu, garage, a race on autopilot, free roam.</summary>
        System.Collections.IEnumerator Tour()
        {
            yield return null;
            ShowMainMenu();
            yield return new WaitForSecondsRealtime(7f);
            EnterGarage();
            yield return new WaitForSecondsRealtime(4f);
            ShowUpgrades(0);
            yield return new WaitForSecondsRealtime(4f);
            var def = Career.QuickRaces().Find(r => r.RouteId == (demoRaceId ?? "RACE-16"));
            garageRoot.gameObject.SetActive(false);
            if (garageDisplay != null) Destroy(garageDisplay);
            StartRace(def, null, null, autopilot: true);
            Debug.Log("[Tour] race started " + def.Name);
        }

        void StartDemo()
        {
            if (string.IsNullOrEmpty(demoRaceId))
            {
                EnterFreeRoam(true);
                SetPlayerAutopilot(true);
                return;
            }
            var def = Career.QuickRaces().Find(r => r.RouteId == demoRaceId) ?? Career.QuickRaces()[0];
            StartRace(def, null, null, autopilot: true);
        }

        // ------------------------------------------------------------------ settings
        void ApplySettings()
        {
            var st = save.Settings;
            VehicleAudio.MasterVolume = st.MasterVolume;
            VehicleAudio.EngineVolume = st.EngineVolume;
            AudioListener.volume = 1f;
            QualitySettings.vSyncCount = st.VSync ? 1 : 0;
            Application.targetFrameRate = st.FpsCap > 0 ? st.FpsCap : -1;
            if (keepDisplay || automated) { }
            else if (!Application.isEditor && st.ResolutionIndex >= 0)
            {
                var rs = Screen.resolutions;
                if (st.ResolutionIndex < rs.Length)
                    Screen.SetResolution(rs[st.ResolutionIndex].width, rs[st.ResolutionIndex].height, st.Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            }
            else if (!Application.isEditor) Screen.fullScreenMode = st.Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            float[] scale = { 0.75f, 0.9f, 1f, 1f };
            float[] shadows = { 60f, 110f, 160f, 240f };
            int[] msaa = { 1, 2, 4, 4 };
            float[] props = { 0.5f, 0.75f, 1f, 1.4f };
            float[] far = { 1800f, 2600f, 3500f, 5000f };
            int q = Mathf.Clamp(st.Quality, 0, 3);
            if (urp != null)
            {
                urp.renderScale = scale[q];
                urp.shadowDistance = shadows[q];
                urp.msaaSampleCount = msaa[q];
            }
            if (InstancedPropRenderer.Instance != null) InstancedPropRenderer.Instance.DistanceScale = props[q];
            if (cam != null) cam.farClipPlane = far[q];
            if (environment != null) environment.StreetLightCount = q <= 0 ? 12 : q == 1 ? 20 : 28;
            if (chaseCamera != null) chaseCamera.SpeedEffectsIntensity = st.CameraEffects;
            hud.Metric = st.Metric;
            traffic.Density = new[] { 0f, 0.45f, 0.8f, 1.2f }[Mathf.Clamp(st.TrafficDensity, 0, 3)];
            if (playerDriver != null) playerDriver.Preset = st.AssistLevel == 0 ? db.Casual : db.Advanced;
        }

        // ------------------------------------------------------------------ player car
        void OnCellLoaded(string cell, UnityEngine.SceneManagement.Scene scene)
        {
            if (this == null || db == null || roadSurfaceRuntime == null || !scene.IsValid()) return;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var s in root.GetComponentsInChildren<VehicleSurface>(true))
                    if (s.Surface == db.DryAsphalt) s.Surface = roadSurfaceRuntime;
        }

        void SpawnPlayer(Pose pose)
        {
            DespawnPlayer();
            var entry = db.Car(save.CurrentCar);
            var owned = save.Owned(entry.Id) ?? new OwnedCar { CarId = entry.Id, Paint = entry.DefaultPaint };
            var def = Upgrade.Build(entry, owned);
            player = SpawnCar(entry, def, owned.Paint, pose, true);
            playerRoot = player.gameObject;
            VeyraCity.World.Streaming.CellStreamer.Track(player.Body);   // A2: cells stream around the car; ground guard on teleports
            playerDriver = player.gameObject.AddComponent<VehicleDriver>();
            playerDriver.Bind(player, input, save.Settings.AssistLevel == 0 ? db.Casual : db.Advanced);
            playerDrift = player.gameObject.AddComponent<DriftScorer>();
            playerDrift.Tuning = db.Nitro;
            playerDrift.Bind(player);
            playerNitro = player.gameObject.AddComponent<NitroSystem>();
            playerNitro.Tuning = db.Nitro;
            playerNitro.PowerMultiplier = Upgrade.NitroMul[owned.Upgrades[Upgrade.Nitro]];
            playerNitro.ChargeMultiplier = Upgrade.NitroMul[owned.Upgrades[Upgrade.Nitro]];
            playerNitro.Bind(player, playerDriver, playerDrift);
            player.gameObject.AddComponent<VehicleAudio>().Bind(player, playerNitro, true);
            player.gameObject.AddComponent<VehicleFX>().Bind(player, playerNitro, true);
            player.gameObject.AddComponent<CarReflectionProbe>().Bind(player);   // paint reflects the real street
            player.gameObject.AddComponent<NitroBurstFX>().Bind(player, playerNitro, chaseCamera);   // perfect/overdrive nitro burst
            var detailFx = player.gameObject.AddComponent<CarDetailFX>();
            detailFx.Bind(player, playerNitro);   // glowing brake discs, nitro / brake light streaks
            if (player.TryGetComponent<CarLampFX>(out var playerLamps)) playerLamps.AddRealLights();   // headlight pool on the road, tail/brake spill behind
            hud.Bind(player, playerNitro, playerDrift);
            hud.BrakeHeat = detailFx.Heat01;   // per-wheel brake temperature on the cluster
            var dmgSrc = player.GetComponent<CarDamage>();
            hud.Damage = dmgSrc != null ? (System.Func<float>)(() => dmgSrc != null ? dmgSrc.Damage01 : 0f) : null;   // iteration 27
            if (worldMap != null) worldMap.Track(player.transform);   // the GPS follows the new car
            if (roadWear != null) roadWear.Player = player.transform;
            if (speedTraps != null) { speedTraps.Player = player; speedTraps.Hud = hud; }
            if (activities != null) { activities.Player = player; activities.Hud = hud; activities.Save = () => save; activities.Persist = () => { if (string.IsNullOrEmpty(iterShotDir)) SaveSystem.Save(save); }; }   // captures never write the profile
            traffic.PlayerCar = player;
            var parked = GetComponentInChildren<ParkedCars>();
            if (parked != null) parked.Player = player;
            var signals = GetComponentInChildren<TrafficLights>();
            if (signals != null) signals.Player = player;
            var trees = GetComponentInChildren<StreetTrees>();
            if (trees != null) trees.Player = player;
            var walkers = GetComponentInChildren<Pedestrians>();
            if (walkers != null) walkers.Player = player;
            var furniture = GetComponentInChildren<StreetFurniture>();
            if (furniture != null) furniture.Player = player;
            var rooftops = GetComponentInChildren<RooftopDetails>();
            if (rooftops != null) rooftops.Player = player;
            traffic.PlayerNitro = playerNitro;
            traffic.NearMiss -= OnNearMiss;
            traffic.NearMiss += OnNearMiss;
            if (chaseCamera != null) chaseCamera.SetTarget(player);
        }

        void OnNearMiss() => hud.ShowToast("NEAR MISS  +5", UIKit.Amber, 1.4f);

        VehicleController SpawnCar(CarEntry entry, VehicleDefinition def, Color paint, Pose pose, bool interpolate)
        {
            GameObject template = entry.Model;
            GameObject temp = null;
            if (template == null)
            {
                var d = def.Chassis.Dimensions;
                var style = entry.Id.Contains("strata") ? ProceduralCar.Style.Hatch : entry.Id.Contains("hammer") ? ProceduralCar.Style.Muscle
                          : entry.Id.Contains("vento") ? ProceduralCar.Style.Super : entry.Id.Contains("sovereign") ? ProceduralCar.Style.GT : ProceduralCar.Style.Sedan;
                temp = ProceduralCar.BuildForVehicle(d.z, d.x, d.y, def.Wheelbase, def.Front.TrackWidth, def.Rear.TrackWidth, def.Front.Tyre.Radius, def.Rear.Tyre.Radius, style);
                temp.SetActive(false);
                template = temp;
            }
            var vc = VehicleFactory.Create(def, template, db.DryAsphalt, pose.position, pose.rotation, interpolate, db.PlaceholderBody, db.PlaceholderWheel);
            var model = vc.transform.Find("Model");
            if (model != null) model.gameObject.SetActive(true);
            if (temp != null) Destroy(temp);
            ApplyPaint(vc.gameObject, paint);
            vc.Feel = db.Feel;                                            // arcade handling layer (drift, stability, air, walls)
            vc.gameObject.AddComponent<VehicleBodyLean>().Bind(vc);      // visual body roll / pitch
            vc.gameObject.AddComponent<CarContactShadow>().Bind(vc);     // soft ground shadow that plants the car
            vc.gameObject.AddComponent<CarSprayFX>().Bind(vc);           // wet-road spray behind the rear wheels
            vc.gameObject.AddComponent<CarDamage>().Bind(vc);            // dents from hard hits
            var lamps = vc.gameObject.AddComponent<CarLampFX>();          // head/tail lamp glow and night light trails
            lamps.Bind(() => vc != null ? vc.State.Speed : 0f, () => vc != null ? Mathf.Max(vc.State.AppliedBrake, vc.Controls.Brake) : 0f, true);
            return vc;
        }

        static readonly Dictionary<(Material, Color32), Material> paintCache = new Dictionary<(Material, Color32), Material>();

        /// <summary>
        /// The paint copy of <paramref name="source"/> in <paramref name="paint"/>, created once per (material, colour) and reused
        /// on every respawn (BUG-046: a new copy per renderer per spawn was never destroyed).
        /// </summary>
        static Material PaintedCopy(Material source, Color paint)
        {
            var key = (source, (Color32)paint);
            if (paintCache.TryGetValue(key, out var copy) && copy != null) return copy;
            copy = new Material(source) { name = source.name + "_Paint" };
            copy.SetColor("_BaseColor", paint);
            paintCache[key] = copy;
            return copy;
        }

        static void ApplyPaint(GameObject car, Color paint)
        {
            // Imported cars name their paint materials M_Paint_* (liveries are M_Livery_* and keep their artwork);
            // only legacy models without such materials fall back to "every non-glass material on a Body renderer".
            bool named = false;
            foreach (var r in car.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && (m.name.StartsWith("M_Paint") || m.name.StartsWith("M_Livery"))) named = true;
            foreach (var r in car.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    bool isPaint = m.name.StartsWith("M_Paint") || m.name.StartsWith("Traffic_Body") || m.name.Contains("Placeholder_CarBody")
                                   || (!named && r.name.StartsWith("Body") && !m.name.Contains("Glass"));
                    if (!isPaint) continue;
                    mats[i] = PaintedCopy(m, paint);
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        void DespawnPlayer()
        {
            if (playerRoot != null)
            {
                // Deactivate now: Destroy is deferred to the end of the frame, and a replacement car spawned at the
                // same pose this frame would be shoved out of the old car's colliders (it starts rolling at 10+ km/h).
                playerRoot.SetActive(false);
                Destroy(playerRoot);
            }
            playerRoot = null;
            player = null;
            playerDriver = null;
            playerNitro = null;
            playerDrift = null;
        }

        void SetPlayerAutopilot(bool on)
        {
            if (player == null) return;
            if (!on) { playerDriver.Bind(player, input, db.Casual); return; }
            var ai = player.GetComponent<AIRacerDriver>();
            if (ai == null) ai = player.gameObject.AddComponent<AIRacerDriver>();
            if (race.Player != null) race.Player.AutoRespawn = true;
            if (race.State != RaceSession.Phase.Idle && race.Player != null) ai.Init(player, race, race.Player, playerNitro, 0.95f);
            else
            {
                // free-roam demo: drive the Ring
                var ring = WorldData.Route("RACE-02");
                race.Setup(new RaceDefinition { Id = "DEMO", Name = "Demo", RouteId = "RACE-02", Kind = RaceKind.Circuit, Laps = 1 }, ring.Path);
                ai.Init(player, race, null, playerNitro, 0.9f);
            }
            playerDriver.Bind(player, ai, db.AIPreset);
        }

        Pose HomePose()
        {
            var home = WorldData.Location("GAR-01");
            Vector3 p = home != null ? home.Position : Vector3.zero;
            if (WorldData.NearestRoad(p, 200f, out var hit))
            {
                var pos = hit.Point;
                pos.y = WorldData.GroundHeight(pos, pos.y) + 0.4f;
                return new Pose(pos, Quaternion.LookRotation(new Vector3(hit.Tangent.x, 0f, hit.Tangent.z)));
            }
            return new Pose(p + Vector3.up, Quaternion.identity);
        }

        // ------------------------------------------------------------------ main menu
        void ShowMainMenu()
        {
            Current = State.MainMenu;
            Time.timeScale = baseTimeScale;
            SetPaused(false);
            hud.Visible = false;
            traffic.Active = false;
            ClearOpponents();
            DespawnPlayer();
            if (chaseCamera != null) chaseCamera.enabled = false;
            if (environment != null) { environment.AnimateTime = false; environment.SetTime(22f); environment.SetWeather(WorldEnvironment.WeatherKind.Clear); }
            var items = new List<MenuEntry>();
            bool hasProgress = save.Events.Count > 0;
            items.Add(new MenuEntry(hasProgress ? "Continue" : "Start career", () => { Click(); ShowCareer(); }, "Your story in Veyra: five crews, sixteen routes, one city.\n\nArrive in Westhaven with a tired silver Arden Kite and earn your name."));
            items.Add(new MenuEntry("Free roam", () => { Click(); EnterFreeRoam(false); }, "Drive the whole city: 8 districts, 165 km of road.\n\nGlowing markers start events. Traffic, near misses and drifting charge your nitro."));
            items.Add(new MenuEntry("Quick race", () => { Click(); ShowQuickRace(); }, "Any of the sixteen routes against AI, with no career progress."));
            items.Add(new MenuEntry("Garage", () => { Click(); EnterGarage(); }, "Choose and buy cars, fit upgrades, change paint."));
            items.Add(new MenuEntry("Settings", () => { Click(); ShowSettings(ShowMainMenu); }, "Graphics, audio, controls and assists."));
            items.Add(new MenuEntry("Credits", () => { Click(); ShowCredits(); }, "Who made what."));
            items.Add(new MenuEntry("Quit", () => { SaveSystem.Save(save); Application.Quit(); }, "Progress is saved automatically."));
            menu.Show("VEYRA CITY", "OPEN-WORLD STREET RACING", items, null, null, 0, $"{UIKit.Money(save.Cash)}   LV {save.Level}", 0.25f);
        }

        void ShowCredits()
        {
            string txt = "<b>VEYRA CITY</b>  (pre-release build)\n\nGame design, world data and code: the Veyra City project.\n\n<b>Vehicle models</b>\n";
            foreach (var c in db.Cars) if (!string.IsNullOrEmpty(c.Credit)) txt += $"{c.Name}: {c.Credit}\n";
            txt += "Arden Kite: original model built in Blender for this project.\n\n<b>Engine</b>: Unity 6.6 (URP).\nAll audio is synthesised at runtime.\nAll place, crew and car names are fictional.";
            menu.Show("CREDITS", "", new List<MenuEntry> { new MenuEntry("Back", ShowMainMenu) }, txt, ShowMainMenu);
        }

        // ------------------------------------------------------------------ free roam
        void EnterFreeRoam(bool keepPosition)
        {
            Current = State.FreeRoam;
            menu.Hide();
            SetPaused(false);
            ClearOpponents();
            race.Abort();
            hud.SetRace(null);
            hud.Visible = true;
            if (chaseCamera != null) chaseCamera.enabled = true;
            if (player == null || !keepPosition) SpawnPlayer(HomePose());
            playerNitro.PassiveRegen = 0f;
            playerDrift.RouteProgress = null;
            traffic.Active = save.Settings.TrafficDensity > 0;
            foreach (var m in markers) if (m.Visual != null) m.Visual.SetActive(true);
            if (environment != null)
            {
                int tod = save.Settings.TimeOfDay;
                environment.AnimateTime = tod == 0;
                environment.SetTime(tod == 1 ? 23f : tod == 2 ? 19.3f : tod == 3 ? 13f : save.FreeRoamTime);
                environment.SetWeather(WorldEnvironment.WeatherKind.Clear);
                environment.AutoWeather = true;
            }
            UpdateWallet();
            hud.SetStaticMarkers(MarkerList());
            if (!save.SeenStory.Contains("WELCOME") && !automated)
            {
                save.SeenStory.Add("WELCOME");
                menu.ShowDialog("Juno Reyes", "Welcome to Veyra. The glowing columns are events: drive into one and press Enter (or D-pad Up). Esc / Start opens the map of events and the garage.", null);
            }
        }

        IEnumerable<(Vector3, Color, float)> MarkerList()
        {
            foreach (var m in markers)
            {
                bool career = m.Career != null;
                bool unlocked = !career || Career.ChapterUnlocked(save, m.Chapter);
                if (!unlocked) continue;
                yield return (m.Position, career ? UIKit.Accent2 : UIKit.Accent, career ? 16f : 11f);
            }
        }

        void BuildMarkers()
        {
            var root = new GameObject("EventMarkers").transform;
            root.SetParent(transform, false);
            var used = new HashSet<string>();
            var mat = RuntimeMaterials.NewUnlitTransparent("EventMarker", true); // null: RuntimeMaterials logged why
            if (mat != null) mat.SetColor("_BaseColor", new Color(1f, 0.25f, 0.62f, 0.22f));
            foreach (var ch in Career.Chapters)
                foreach (var e in ch.Events)
                {
                    if (used.Contains(e.Race.RouteId)) continue;
                    var route = WorldData.Route(e.Race.RouteId);
                    if (route == null) continue;
                    used.Add(e.Race.RouteId);
                    var p = route.Path.PositionAt(10f);
                    var m = new Marker { Race = e.Race, Career = e, Chapter = ch, Position = p };
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Destroy(go.GetComponent<Collider>());
                    go.name = "Marker_" + e.Race.RouteId;
                    go.transform.SetParent(root, false);
                    go.transform.position = p + Vector3.up * 30f;
                    go.transform.localScale = new Vector3(5f, 30f, 5f);
                    var r = go.GetComponent<Renderer>();
                    if (mat != null) r.sharedMaterial = mat;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    m.Visual = go;
                    markers.Add(m);
                }
        }

        void Update()
        {
            if (noTraffic && traffic != null && traffic.Active) traffic.Active = false;   // -veyraNoTraffic (route validation)
            UpdateWorldMapInput();
            float dt = Time.unscaledDeltaTime;
            SkidMarks.Flush();
            save.PlaySeconds += Time.deltaTime;
            switch (Current)
            {
                case State.MainMenu: OrbitCamera(menuOrbitCentre != null ? menuOrbitCentre.position : new Vector3(170f, 60f, -670f), 520f, 160f, 0.025f); break;
                case State.Garage: GarageCamera(); break;
                case State.FreeRoam: FreeRoamUpdate(); break;
            }
            if (environment != null && Current == State.FreeRoam) save.FreeRoamTime = environment.TimeOfDay;
            saveTimer += dt;
            if (saveTimer > 30f) { saveTimer = 0f; SaveSystem.Save(save); }
            if (roadSurfaceRuntime != null && db.DryAsphalt != null && db.WetAsphalt != null && environment != null)
                roadSurfaceRuntime.GripMultiplier = Mathf.Lerp(db.DryAsphalt.GripMultiplier, db.WetAsphalt.GripMultiplier, environment.Wetness);
        }

        void FreeRoamUpdate()
        {
            if (paused || menu.AnyOpen || player == null) { hud.SetPrompt(""); return; }
            nearMarker = null;
            var pos = player.transform.position;
            foreach (var m in markers)
            {
                if (m.Visual == null) continue;
                bool unlocked = m.Career == null || Career.ChapterUnlocked(save, m.Chapter);
                m.Visual.SetActive(unlocked);
                if (!unlocked) continue;
                if ((m.Position - pos).sqrMagnitude < 18f * 18f) { nearMarker = m; break; }
            }
            if (nearMarker != null)
            {
                var rec = save.Record(nearMarker.Race.Id, false);
                string best = rec != null && rec.BestPosition > 0 ? $"  -  best {UIKit.Ordinal(rec.BestPosition)}" : "";
                hud.SetPrompt($"{nearMarker.Race.Name.ToUpperInvariant()}  ({nearMarker.Chapter.Title}){best}\nPress ENTER / D-pad Up to race");
                if (InteractPressed()) OpenCareerEvent(nearMarker.Chapter, nearMarker.Career);
            }
            else hud.SetPrompt("");
        }

        static bool InteractPressed()
        {
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            return (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) || (gp != null && gp.dpad.up.wasPressedThisFrame);
        }

        void UpdateWallet() => hud.SetWallet($"{UIKit.Money(save.Cash)}     LV {save.Level}");

        // ------------------------------------------------------------------ pause / reset
        /// <summary>M (or the gamepad View button) opens the full-screen map in free roam and races and pauses the game;
        /// M or Esc closes it.</summary>
        void UpdateWorldMapInput()
        {
            if (worldMap == null) return;
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            bool toggle = (kb != null && kb.mKey.wasPressedThisFrame) || (gp != null && gp.selectButton.wasPressedThisFrame);
            bool esc = kb != null && kb.escapeKey.wasPressedThisFrame;
            if (worldMap.IsOpen)
            {
                if (toggle || esc || (gp != null && gp.buttonEast.wasPressedThisFrame)) { worldMap.Close(); SetPaused(false); }
                return;
            }
            if (!toggle || paused || menu.DialogOpen || player == null) return;
            if (Current != State.FreeRoam && Current != State.Race) return;
            worldMap.Open(player.transform);
            SetPaused(true);
        }

        void OnPausePressed()
        {
            if (worldMap != null && worldMap.IsOpen) return;   // Esc closes the map instead (UpdateWorldMapInput)
            if (menu.DialogOpen) return;
            switch (Current)
            {
                case State.FreeRoam:
                case State.Race:
                    if (paused) Resume(); else ShowPause();
                    break;
            }
        }

        float baseTimeScale = 1f;

        void SetPaused(bool p)
        {
            paused = p;
            Time.timeScale = p ? 0f : baseTimeScale;
        }

        void Resume()
        {
            menu.Hide();
            SetPaused(false);
        }

        void ShowPause()
        {
            SetPaused(true);
            var items = new List<MenuEntry> { new MenuEntry("Resume", Resume) };
            if (Current == State.Race)
            {
                items.Add(new MenuEntry("Restart race", () => { SetPaused(false); StartRace(currentRace, currentCareer, currentChapter); }));
                items.Add(new MenuEntry("Retire to free roam", () => { SetPaused(false); EndRaceToFreeRoam(); }));
            }
            else
            {
                items.Add(new MenuEntry("Career events", () => ShowCareer(ShowPause)));
                items.Add(new MenuEntry("Quick race", () => ShowQuickRace(ShowPause)));
                items.Add(new MenuEntry("Garage", () => { SetPaused(false); EnterGarage(); }));
                items.Add(new MenuEntry("Reset car to road", () => { Resume(); ResetToRoad(); }));
            }
            items.Add(new MenuEntry("Settings", () => ShowSettings(ShowPause)));
            items.Add(new MenuEntry("Controls", () => ShowControls(ShowPause)));
            items.Add(new MenuEntry("Main menu", () => { SaveSystem.Save(save); ShowMainMenu(); }));
            menu.Show("PAUSED", Current == State.Race ? currentRace?.Name : "FREE ROAM", items, null, Resume, 0, $"{UIKit.Money(save.Cash)}   LV {save.Level}");
        }

        void OnResetPressed()
        {
            if (paused || player == null) return;
            if (Current == State.Race && race.Player != null && (race.State == RaceSession.Phase.Racing)) race.Respawn(race.Player);
            else if (Current == State.FreeRoam) ResetToRoad();
        }

        void ResetToRoad()
        {
            if (player == null) return;
            var p = player.transform.position;
            if (WorldData.NearestRoad(p, 400f, out var hit))
            {
                var pos = hit.Point;
                pos.y = WorldData.GroundHeight(pos + Vector3.up * 3f, pos.y) + 0.5f;
                var fwd = new Vector3(hit.Tangent.x, 0f, hit.Tangent.z);
                if (Vector3.Dot(fwd, player.transform.forward) < 0f) fwd = -fwd;
                player.ResetTo(pos, Quaternion.LookRotation(fwd));
            }
        }

        // ------------------------------------------------------------------ career / quick race menus
        void ShowCareer(Action back = null)
        {
            back = back ?? ShowMainMenu;
            if (Current == State.MainMenu && !save.SeenStory.Contains("CH0_intro"))
            {
                if (!save.SeenStory.Contains("WELCOME")) save.SeenStory.Add("WELCOME");
                EnterFreeRoam(false);
                SetPaused(true);
                PlayStory("CH0_intro", Career.Chapters[0].Intro, () => ShowCareer(ShowPause));
                return;
            }
            var items = new List<MenuEntry>();
            foreach (var ch in Career.Chapters)
            {
                bool unlocked = Career.ChapterUnlocked(save, ch);
                bool done = Career.ChapterComplete(save, ch);
                var chapter = ch;
                string info = unlocked ? $"<b>{ch.Title}</b>\n{ch.Crew}\n\n" : $"<b>{ch.Title}</b>\n\nLocked: win {ch.WinsRequired} events and finish the previous chapter's gate event.";
                if (unlocked) foreach (var e in ch.Events) info += $"{(save.Record(e.Race.Id, false)?.Wins > 0 ? "✓" : "•")}  {e.Race.Name}{(e.Gate ? "  (gate)" : "")}\n";
                items.Add(new MenuEntry(ch.Title, unlocked ? () => ShowChapter(chapter, back) : (Action)null, info, done ? "COMPLETE" : unlocked ? "" : "LOCKED") { Enabled = unlocked });
            }
            items.Add(new MenuEntry("Back", back));
            menu.Show("CAREER", $"{save.Wins} WINS   -   STREET CRED LV {save.Level}", items, null, back, 0, UIKit.Money(save.Cash));
        }

        void ShowChapter(Chapter ch, Action back)
        {
            if (!save.SeenStory.Contains(ch.Id + "_intro") && ch.Intro != null)
            {
                menu.Hide();
                PlayStory(ch.Id + "_intro", ch.Intro, () => ShowChapter(ch, back));
                return;
            }
            var items = new List<MenuEntry>();
            foreach (var e in ch.Events)
            {
                var ev = e;
                var rec = save.Record(e.Race.Id, false);
                var route = WorldData.Route(e.Race.RouteId);
                string len = route != null ? $"{route.Path.Length * (route.Path.Closed ? e.Race.Laps : 1) / 1000f:0.0} km" : "";
                string info = $"<b>{e.Race.Name}</b>\n{e.Race.Kind}  -  {len}{(e.Race.Laps > 1 ? $"  -  {e.Race.Laps} laps" : "")}\n{(e.Race.Rain ? "Rain  -  " : "")}{TimeLabel(e.Race.TimeOfDay)}\n\n{e.Blurb}\n\n1st: {UIKit.Money(e.Race.CashFor(1))}   +{e.Race.Reputation} rep\n" +
                              (rec != null ? $"\nBest: {(rec.BestPosition > 0 ? UIKit.Ordinal(rec.BestPosition) : "-")}   {UIKit.FormatTime(rec.BestTime)}" : "");
                items.Add(new MenuEntry(e.Race.Name, () => { Click(); StartRace(ev.Race, ev, ch); }, info, rec?.Wins > 0 ? "WON" : e.Gate ? "GATE" : ""));
            }
            items.Add(new MenuEntry("Back", () => ShowCareer(back)));
            menu.Show(ch.Title.ToUpperInvariant(), ch.Crew, items, null, () => ShowCareer(back));
        }

        void OpenCareerEvent(Chapter ch, CareerEvent e)
        {
            SetPaused(true);
            ShowChapter(ch, ShowPause);
        }

        static string TimeLabel(float h) => h >= 20f || h < 5f ? "Night" : h >= 17.5f ? "Golden hour" : h < 8f ? "Dawn" : "Day";

        void ShowQuickRace(Action back = null)
        {
            back = back ?? ShowMainMenu;
            var items = new List<MenuEntry>();
            foreach (var r in Career.QuickRaces())
            {
                var def = r;
                var route = WorldData.Route(r.RouteId);
                if (route == null) continue;
                string info = $"<b>{r.Name}</b>\n{r.Kind}  -  {route.Path.Length * (route.Path.Closed ? r.Laps : 1) / 1000f:0.0} km{(r.Laps > 1 ? $"  -  {r.Laps} laps" : "")}\nDistrict route {r.RouteId}\n\nQuick races pay a third of career money.";
                items.Add(new MenuEntry(r.Name, () => { Click(); StartRace(def, null, null); }, info, $"{route.Path.Length / 1000f:0.0} km"));
            }
            items.Add(new MenuEntry("Back", back));
            menu.Show("QUICK RACE", "ALL SIXTEEN ROUTES", items, null, back);
        }

        void PlayStory(string key, StoryLine[] lines, Action done, int index = 0)
        {
            if (lines == null || index >= lines.Length)
            {
                if (!save.SeenStory.Contains(key)) save.SeenStory.Add(key);
                SaveSystem.Save(save);
                done?.Invoke();
                return;
            }
            menu.ShowDialog(lines[index].Speaker, lines[index].Text, () => PlayStory(key, lines, done, index + 1));
        }

        // ------------------------------------------------------------------ races
        public void StartRace(RaceDefinition def, CareerEvent ce, Chapter ch, bool autopilot = false)
        {
            var route = WorldData.Route(def.RouteId);
            if (route == null) { Debug.LogError("Unknown route " + def.RouteId); return; }
            menu.Hide();
            SetPaused(false);
            Current = State.Race;
            currentRace = def;
            currentCareer = ce;
            currentChapter = ch;
            ClearOpponents();
            traffic.Active = def.Traffic;
            traffic.Clear();
            foreach (var m in markers) if (m.Visual != null) m.Visual.SetActive(false);
            hud.SetPrompt("");
            hud.SetStaticMarkers(null);
            if (environment != null)
            {
                environment.AnimateTime = false;
                environment.AutoWeather = false;
                environment.SetTime(def.TimeOfDay);
                environment.SetWeather(def.Rain ? WorldEnvironment.WeatherKind.Rain : WorldEnvironment.WeatherKind.Clear);
            }
            race.Setup(def, route.Path);
            int n = Mathf.Clamp(def.Opponents, 0, 7);
            if (player == null) SpawnPlayer(race.GridPose(n));
            else
            {
                var gp = race.GridPose(n);
                player.ResetTo(gp.position, gp.rotation);
            }
            if (chaseCamera != null) { chaseCamera.enabled = true; chaseCamera.SetTarget(player); }
            hud.Visible = true;
            playerNitro.ResetState(NitroSystem.RaceStartAmount);
            playerNitro.PassiveRegen = 1f;
            playerDrift.ResetAll();
            var playerPart = race.AddParticipant("You", player, playerDriver, true, Color.white);
            playerDrift.RouteProgress = () => Vector3.Dot(player.Body.linearVelocity.normalized, race.Path.TangentAt(playerPart.S));
            float[] diff = { 0.90f, 0.95f, 0.99f };
            float skillBase = def.AISkill * diff[Mathf.Clamp(save.Settings.Difficulty, 0, 2)] / 0.95f;
            var pool = new List<CarEntry>();
            foreach (var c in db.Cars) pool.Add(c);
            var playerStats = Upgrade.Evaluate(player.Definition);
            pool.Sort((a, b) => Mathf.Abs(Upgrade.Evaluate(a.Definition).Pi - playerStats.Pi).CompareTo(Mathf.Abs(Upgrade.Evaluate(b.Definition).Pi - playerStats.Pi)));
            for (int i = 0; i < n; i++)
            {
                var entry = pool[Mathf.Min(i % 3, pool.Count - 1)];
                var aiDef = Instantiate(entry.Definition);
                // Match the field to the player's build so races stay contested without rubber-banding the physics.
                float ratio = Mathf.Clamp(playerStats.Pi / Mathf.Max(100f, Upgrade.Evaluate(aiDef).Pi), 0.85f, 1.25f);
                var curve = (Vector2[])aiDef.Engine.TorqueCurve.Clone();
                for (int k = 0; k < curve.Length; k++) curve[k].y *= ratio;
                aiDef.Engine.TorqueCurve = curve;
                var pose = race.GridPose(i);
                var car = SpawnCar(entry, aiDef, Color.HSVToRGB(UnityEngine.Random.value, 0.75f, 0.85f), pose, false);
                if (car.TryGetComponent<CarLampFX>(out var aiLamps)) aiLamps.AddRealLights();
                car.name = RivalNames[i % RivalNames.Length];
                var drv = car.gameObject.AddComponent<VehicleDriver>();
                var ai = car.gameObject.AddComponent<AIRacerDriver>();
                var nit = car.gameObject.AddComponent<NitroSystem>();
                nit.PassiveRegen = 1f;
                nit.Tuning = db.Nitro;
                var part = race.AddParticipant(car.name, car, drv, false, RivalColors[i % RivalColors.Length]);
                ai.Init(car, race, part, nit, Mathf.Clamp(skillBase + UnityEngine.Random.Range(-0.02f, 0.02f), 0.82f, 1.03f));
                ai.Aggression = UnityEngine.Random.value;
                drv.Bind(car, ai, db.AIPreset);
                nit.Bind(car, drv, null);
                nit.ResetState(NitroSystem.RaceStartAmount);
                car.gameObject.AddComponent<VehicleAudio>().Bind(car, nit, false);
                car.gameObject.AddComponent<VehicleFX>().Bind(car, nit, i < 2);
                opponents.Add(car.gameObject);
            }
            hud.SetRace(race);
            if (autopilot || demo) SetPlayerAutopilot(true);
            else playerDriver.Bind(player, input, save.Settings.AssistLevel == 0 ? db.Casual : db.Advanced);
            race.BeginCountdown(3.5f);
            hud.ShowToast(def.Name.ToUpperInvariant(), UIKit.Accent, 3f);
            var rec = save.Record(def.Id, true);
            rec.Starts++;
        }

        void OnCountdownTick(int n)
        {
            if (n >= 1 && n <= 3) { hud.BigMessage(n.ToString(), UIKit.TextMain, 0.9f); Play(beepLow, 0.8f); }
        }

        void OnParticipantFinished(RaceParticipant p)
        {
            if (!p.IsPlayer) return;
            hud.BigMessage(UIKit.Ordinal(p.Position).ToUpperInvariant(), p.Position == 1 ? UIKit.Good : UIKit.TextMain, 3f);
            p.DriftScore = playerDrift.TotalBanked;
            p.NitroUsed = playerNitro.TotalUsed;
            SetPlayerAutopilot(true); // AI brings the car home while the field finishes
        }

        void OnRaceFinished()
        {
            Current = State.Results;
            var p = race.Player;
            if (p == null) { EndRaceToFreeRoam(); return; }
            int pos = p.Position;
            var rec = save.Record(currentRace.Id, true);
            bool firstWin = pos == 1 && rec.Wins == 0;
            int cash = currentRace.CashFor(pos);
            if (!firstWin && rec.Wins > 0) cash = Mathf.RoundToInt(cash * 0.4f);
            if (p.Collisions == 0) cash += 1500;
            int xp = 120 + Mathf.Max(0, race.Participants.Count - pos) * 60 + Mathf.RoundToInt(p.DriftScore / 2000f);
            int rep = pos <= 3 ? Mathf.RoundToInt(currentRace.Reputation * (pos == 1 ? 1f : pos == 2 ? 0.6f : 0.35f)) : 0;
            save.Cash += cash;
            save.Xp += xp;
            if (currentChapter != null && currentChapter.CrewIndex >= 0) save.CrewRep[currentChapter.CrewIndex] += rep;
            if (pos == 1) rec.Wins++;
            if (rec.BestPosition == 0 || pos < rec.BestPosition) rec.BestPosition = pos;
            if (!p.Projected && (rec.BestTime <= 0f || p.FinishTime < rec.BestTime)) rec.BestTime = p.FinishTime;
            SaveSystem.Save(save);

            string table = "<b>RESULTS</b>\n\n";
            foreach (var q in race.Participants)
                table += $"{UIKit.Ordinal(q.Position),-5} {(q.IsPlayer ? "<color=#33DDFF>" : "")}{q.Name}{(q.IsPlayer ? "</color>" : "")}   {UIKit.FormatTime(q.FinishTime)}{(q.Projected ? " *" : "")}{(race.IsCircuit && q.BestLap < float.MaxValue ? $"   best {UIKit.FormatTime(q.BestLap)}" : "")}\n";
            table += $"\n<b>REWARDS</b>\n{UIKit.Money(cash)}{(p.Collisions == 0 ? "  (incl. clean race +$1,500)" : "")}\n+{xp} XP   +{rep} rep\nDrift {Mathf.RoundToInt(p.DriftScore):N0}   Nitro used {Mathf.RoundToInt(p.NitroUsed)}   Collisions {p.Collisions}";
            if (race.Participants.Exists(q => q.Projected)) table += "\n* projected finish time";
            var items = new List<MenuEntry>
            {
                new MenuEntry("Continue", () => AfterResults(pos == 1 && firstWin)),
                new MenuEntry("Retry", () => StartRace(currentRace, currentCareer, currentChapter)),
            };
            menu.Show(pos == 1 ? "VICTORY" : UIKit.Ordinal(pos).ToUpperInvariant() + " PLACE", currentRace.Name, items, table, null, 0, $"{UIKit.Money(save.Cash)}   LV {save.Level}", 0.5f);
            Debug.Log($"[Race] {currentRace.Name} finished: player {pos}/{race.Participants.Count} time {p.FinishTime:0.00}s respawns {p.Respawns} collisions {p.Collisions}; " +
                      string.Join(", ", race.Participants.ConvertAll(q => $"{q.Name} {q.FinishTime:0.0}{(q.Projected ? "*" : "")} r{q.Respawns}")));
            if (demo) StartCoroutine(DemoContinue(pos == 1 && firstWin));
        }

        System.Collections.IEnumerator DemoContinue(bool firstWin)
        {
            yield return new WaitForSecondsRealtime(6f);
            AfterResults(false);
            SetPlayerAutopilot(true);
        }

        void AfterResults(bool firstWin)
        {
            var ch = currentChapter;
            var ce = currentCareer;
            EndRaceToFreeRoam();
            if (firstWin && ce != null && ce.Gate && ch != null && Career.ChapterComplete(save, ch) && ch.Outro != null && !save.SeenStory.Contains(ch.Id + "_outro"))
            {
                SetPaused(true);
                PlayStory(ch.Id + "_outro", ch.Outro, () => { SetPaused(false); hud.ShowToast("NEW EVENTS UNLOCKED", UIKit.Accent2, 3f); });
            }
        }

        void EndRaceToFreeRoam()
        {
            menu.Hide();
            ClearOpponents();
            race.Abort();
            if (player != null) playerDriver.Bind(player, input, save.Settings.AssistLevel == 0 ? db.Casual : db.Advanced);
            var ai = player != null ? player.GetComponent<AIRacerDriver>() : null;
            if (ai != null) Destroy(ai);
            EnterFreeRoam(true);
        }

        void ClearOpponents()
        {
            foreach (var o in opponents) if (o != null) Destroy(o);
            opponents.Clear();
        }

        // ------------------------------------------------------------------ garage
        void BuildGarage()
        {
            garageRoot = new GameObject("Garage").transform;
            garageRoot.SetParent(transform, false);
            garageRoot.position = new Vector3(0f, 1500f, 0f);
            Material M(Color c, float smooth, float metal, Color? em = null)
            {
                var m = RuntimeMaterials.NewLit("Garage"); // FxLit: _EMISSION is on in the template (black), so no keyword change
                if (m == null) return null;               // RuntimeMaterials logged why
                m.SetColor("_BaseColor", c);
                m.SetFloat("_Smoothness", smooth);
                m.SetFloat("_Metallic", metal);
                if (em.HasValue) m.SetColor("_EmissionColor", em.Value);
                return m;
            }
            void Box(string name, Vector3 pos, Vector3 size, Material m)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = name;
                b.transform.SetParent(garageRoot, false);
                b.transform.localPosition = pos;
                b.transform.localScale = size;
                b.GetComponent<Renderer>().sharedMaterial = m;
            }
            var floor = M(new Color(0.16f, 0.16f, 0.17f), 0.82f, 0f);
            var wall = M(new Color(0.07f, 0.075f, 0.085f), 0.35f, 0.2f);
            var strip = M(Color.black, 0.5f, 0f, new Color(2.2f, 2.4f, 2.8f));
            var neon = M(Color.black, 0.5f, 0f, new Color(0.2f, 1.6f, 2.4f));
            Box("Floor", new Vector3(0, -0.05f, 0), new Vector3(26, 0.1f, 26), floor);
            Box("Ceiling", new Vector3(0, 7f, 0), new Vector3(26, 0.2f, 26), wall);
            Box("WallN", new Vector3(0, 3.5f, 13f), new Vector3(26, 7, 0.3f), wall);
            Box("WallS", new Vector3(0, 3.5f, -13f), new Vector3(26, 7, 0.3f), wall);
            Box("WallE", new Vector3(13f, 3.5f, 0), new Vector3(0.3f, 7, 26), wall);
            Box("WallW", new Vector3(-13f, 3.5f, 0), new Vector3(0.3f, 7, 26), wall);
            for (int i = -2; i <= 2; i++) Box("Strip" + i, new Vector3(i * 3.2f, 6.85f, 0), new Vector3(0.25f, 0.06f, 14f), strip);
            Box("NeonN", new Vector3(0, 1.2f, 12.8f), new Vector3(18f, 0.08f, 0.05f), neon);
            Box("NeonW", new Vector3(-12.8f, 1.2f, 0), new Vector3(0.05f, 0.08f, 18f), neon);
            var turn = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            turn.name = "Turntable";
            turn.transform.SetParent(garageRoot, false);
            turn.transform.localPosition = new Vector3(0, 0.02f, 0);
            turn.transform.localScale = new Vector3(7.5f, 0.03f, 7.5f);
            turn.GetComponent<Renderer>().sharedMaterial = M(new Color(0.25f, 0.26f, 0.28f), 0.9f, 0.85f);
            foreach (var (p, c, inten) in new[] { (new Vector3(0, 6.5f, 0), new Color(1f, 0.97f, 0.92f), 60f), (new Vector3(-6, 5f, -6), new Color(0.6f, 0.85f, 1f), 30f), (new Vector3(6, 5f, 5), new Color(1f, 0.55f, 0.8f), 26f) })
            {
                var l = new GameObject("GarageLight").AddComponent<Light>();
                l.transform.SetParent(garageRoot, false);
                l.transform.localPosition = p;
                l.type = LightType.Point;
                l.range = 18f;
                l.intensity = inten;
                l.color = c;
                l.shadows = LightShadows.Soft;
            }
            var probe = new GameObject("GarageProbe").AddComponent<ReflectionProbe>();
            probe.transform.SetParent(garageRoot, false);
            probe.transform.localPosition = new Vector3(0, 2f, 0);
            probe.size = new Vector3(26, 7, 26);
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.importance = 10;
            garageRoot.gameObject.SetActive(false);
        }

        void EnterGarage()
        {
            Current = State.Garage;
            menu.Hide();
            SetPaused(false);
            traffic.Active = false;
            ClearOpponents();
            race.Abort();
            DespawnPlayer();
            hud.Visible = false;
            if (chaseCamera != null) chaseCamera.enabled = false;
            garageRoot.gameObject.SetActive(true);
            garageRoot.GetComponentInChildren<ReflectionProbe>().RenderProbe();
            ShowDisplayCar(save.CurrentCar);
            ShowGarageMenu();
        }

        void ShowDisplayCar(string carId)
        {
            if (garageDisplay != null) Destroy(garageDisplay);
            var entry = db.Car(carId);
            var owned = save.Owned(carId);
            var def = Upgrade.Build(entry, owned);
            var car = SpawnCar(entry, def, owned != null ? owned.Paint : entry.DefaultPaint, new Pose(garageRoot.position + Vector3.up * 0.05f, Quaternion.Euler(0, 200f, 0)), false);
            car.Body.isKinematic = true;
            car.enabled = false;
            garageDisplay = car.gameObject;
        }

        void ShowGarageMenu(int sel = 0)
        {
            var entry = db.Car(save.CurrentCar);
            var items = new List<MenuEntry>
            {
                new MenuEntry("My cars", () => ShowMyCars(), CarInfo(entry, save.Owned(entry.Id))),
                new MenuEntry("Upgrades", () => ShowUpgrades(0), "Engine, nitro, tyres, brakes, suspension and weight. Every part changes the physics, not just a number."),
                new MenuEntry("Paint", () => ShowPaint(), "Change the colour of your current car."),
                new MenuEntry("Dealership", () => ShowDealer(), "Buy new cars. Higher levels unlock faster machines."),
                new MenuEntry("Drive", () => { garageRoot.gameObject.SetActive(false); if (garageDisplay != null) Destroy(garageDisplay); EnterFreeRoam(false); }, "Back to the city, starting at your lock-up on Juniper Lane."),
                new MenuEntry("Main menu", () => { garageRoot.gameObject.SetActive(false); if (garageDisplay != null) Destroy(garageDisplay); ShowMainMenu(); }),
            };
            menu.Show("GARAGE", entry.Name.ToUpperInvariant(), items, null, () => { garageRoot.gameObject.SetActive(false); if (garageDisplay != null) Destroy(garageDisplay); EnterFreeRoam(false); }, sel, $"{UIKit.Money(save.Cash)}   LV {save.Level}", 0.05f);
        }

        string CarInfo(CarEntry entry, OwnedCar owned)
        {
            var d = Upgrade.Build(entry, owned);
            var s = Upgrade.Evaluate(d);
            string ups = "";
            if (owned != null) for (int i = 0; i < Upgrade.Count; i++) if (owned.Upgrades[i] > 0) ups += $"\n  {Upgrade.Names[i]}: {Upgrade.LevelNames[i][owned.Upgrades[i]]}";
            string txt = $"<b>{entry.Name}</b>   <color=#FFAA22>{s.Class} {Mathf.RoundToInt(s.Pi)}</color>\n{entry.Maker}\n\n{entry.Description}\n\n" +
                         $"Power  {Mathf.RoundToInt(s.PowerKw * 1.341f)} hp\nWeight  {Mathf.RoundToInt(s.Mass)} kg\nDrive  {d.Layout}\nTop speed  ~{Mathf.RoundToInt(s.TopKmh)} km/h\nGrip  {s.Grip:0.00}";
            if (ups.Length > 0) txt += "\n\nFitted:" + ups;
            if (!string.IsNullOrEmpty(entry.Credit)) txt += $"\n\n<size=20>Model: {entry.Credit}</size>";
            Destroy(d);
            return txt;
        }

        void ShowMyCars()
        {
            var items = new List<MenuEntry>();
            foreach (var oc in save.Cars)
            {
                var e = db.Car(oc.CarId);
                if (e == null) continue;
                var id = oc.CarId;
                items.Add(new MenuEntry(e.Name, () => { save.CurrentCar = id; SaveSystem.Save(save); ShowDisplayCar(id); ShowGarageMenu(); }, CarInfo(e, oc), id == save.CurrentCar ? "CURRENT" : ""));
            }
            items.Add(new MenuEntry("Back", () => ShowGarageMenu()));
            menu.Show("MY CARS", $"{save.Cars.Count} OWNED", items, null, () => ShowGarageMenu(), 0, UIKit.Money(save.Cash), 0.05f);
        }

        void ShowDealer()
        {
            var items = new List<MenuEntry>();
            foreach (var e in db.Cars)
            {
                if (e.AIOnly) continue;
                var entry = e;
                bool owned = save.Owned(e.Id) != null;
                bool levelOk = save.Level >= e.UnlockLevel;
                bool afford = save.Cash >= e.Price;
                string right = owned ? "OWNED" : !levelOk ? $"LV {e.UnlockLevel}" : UIKit.Money(e.Price);
                items.Add(new MenuEntry(e.Name, () =>
                {
                    if (owned) { save.CurrentCar = entry.Id; ShowDisplayCar(entry.Id); ShowGarageMenu(); return; }
                    if (!levelOk || !afford) { hud.ShowToast(!levelOk ? "LEVEL TOO LOW" : "NOT ENOUGH CASH", UIKit.Bad); return; }
                    save.Cash -= entry.Price;
                    save.Cars.Add(new OwnedCar { CarId = entry.Id, Paint = entry.DefaultPaint });
                    save.CurrentCar = entry.Id;
                    SaveSystem.Save(save);
                    ShowDisplayCar(entry.Id);
                    ShowGarageMenu();
                }, CarInfo(e, null) + $"\n\nPrice {UIKit.Money(e.Price)}   Unlocks at level {e.UnlockLevel}", right) { Enabled = owned || levelOk, Tint = owned ? UIKit.Good : (Color?)null });
            }
            items.Add(new MenuEntry("Back", () => ShowGarageMenu()));
            menu.Show("DEALERSHIP", "NEW AND USED", items, null, () => ShowGarageMenu(), 0, UIKit.Money(save.Cash), 0.05f);
        }

        void ShowUpgrades(int sel)
        {
            var entry = db.Car(save.CurrentCar);
            var owned = save.Owned(entry.Id);
            if (owned == null) { ShowGarageMenu(); return; }
            var items = new List<MenuEntry>();
            for (int i = 0; i < Upgrade.Count; i++)
            {
                int cat = i;
                int lvl = owned.Upgrades[i];
                bool max = lvl >= Upgrade.MaxLevel;
                int cost = max ? 0 : Upgrade.Cost(entry, cat, lvl + 1);
                string info = $"<b>{Upgrade.Names[i]}</b>\nFitted: {Upgrade.LevelNames[i][lvl]}\n" + (max ? "\nFully upgraded." : $"Next: {Upgrade.LevelNames[i][lvl + 1]}  -  {UIKit.Money(cost)}") + "\n\n" + CarInfo(entry, owned);
                items.Add(new MenuEntry($"{Upgrade.Names[i]}  ({lvl}/{Upgrade.MaxLevel})", () =>
                {
                    if (max) return;
                    if (save.Cash < cost) { hud.ShowToast("NOT ENOUGH CASH", UIKit.Bad); return; }
                    save.Cash -= cost;
                    owned.Upgrades[cat]++;
                    SaveSystem.Save(save);
                    Click();
                    ShowDisplayCar(entry.Id);
                    ShowUpgrades(cat);
                }, info, max ? "MAX" : UIKit.Money(cost)));
            }
            items.Add(new MenuEntry("Back", () => ShowGarageMenu(1)));
            menu.Show("UPGRADES", entry.Name.ToUpperInvariant(), items, null, () => ShowGarageMenu(1), sel, UIKit.Money(save.Cash), 0.05f);
        }

        static readonly (string name, Color c)[] Paints =
        {
            ("Arden Silver", new Color(0.62f, 0.64f, 0.66f)), ("Midnight Black", new Color(0.03f, 0.03f, 0.035f)), ("Pearl White", new Color(0.92f, 0.92f, 0.9f)),
            ("Crown Red", new Color(0.6f, 0.03f, 0.03f)), ("Neon Mile Magenta", new Color(0.75f, 0.06f, 0.42f)), ("Azure Blue", new Color(0.05f, 0.25f, 0.65f)),
            ("Ironport Orange", new Color(0.85f, 0.32f, 0.04f)), ("Apex Green", new Color(0.05f, 0.35f, 0.18f)), ("Sodium Amber", new Color(0.95f, 0.62f, 0.08f)),
            ("Gunmetal", new Color(0.2f, 0.21f, 0.23f)), ("Cyan Flash", new Color(0.05f, 0.7f, 0.8f)), ("Redline Crimson", new Color(0.38f, 0.02f, 0.05f)),
        };

        void ShowPaint()
        {
            var entry = db.Car(save.CurrentCar);
            var owned = save.Owned(entry.Id);
            var items = new List<MenuEntry>();
            foreach (var p in Paints)
            {
                var col = p.c;
                items.Add(new MenuEntry(p.name, () =>
                {
                    if (owned == null) return;
                    if (save.Cash < 500) { hud.ShowToast("NOT ENOUGH CASH", UIKit.Bad); return; }
                    save.Cash -= 500;
                    owned.Paint = col;
                    SaveSystem.Save(save);
                    ShowDisplayCar(entry.Id);
                    ShowGarageMenu(2);
                }, $"<b>{p.name}</b>\n\nRespray: $500", "$500") { Tint = Color.Lerp(col, Color.white, 0.25f) });
            }
            items.Add(new MenuEntry("Back", () => ShowGarageMenu(2)));
            menu.Show("PAINT", entry.Name.ToUpperInvariant(), items, null, () => ShowGarageMenu(2), 0, UIKit.Money(save.Cash), 0.05f);
        }

        void GarageCamera()
        {
            if (cam == null || garageRoot == null) return;
            orbitAngle += Time.unscaledDeltaTime * 12f;
            if (garageDisplay != null) garageDisplay.transform.rotation = Quaternion.Euler(0f, 200f + orbitAngle * 0.5f, 0f);
            var c = garageRoot.position + Vector3.up * 0.7f;
            var pos = c + Quaternion.Euler(0f, 35f, 0f) * new Vector3(-2.6f, 1.15f, -6.4f);
            cam.transform.position = pos;
            cam.transform.LookAt(c + Vector3.right * -0.9f);
            cam.fieldOfView = 45f;
        }

        void OrbitCamera(Vector3 centre, float radius, float height, float speed)
        {
            if (cam == null) return;
            orbitAngle += Time.unscaledDeltaTime * speed * 57.3f;
            var pos = centre + Quaternion.Euler(0f, orbitAngle, 0f) * new Vector3(0f, height, -radius);
            cam.transform.position = pos;
            cam.transform.LookAt(centre + Vector3.up * 60f);
            cam.fieldOfView = 55f;
        }

        // ------------------------------------------------------------------ settings & controls
        void ShowSettings(Action back, int sel = 0)
        {
            var st = save.Settings;
            string[] q = { "Low", "Medium", "High", "Ultra" };
            string[] fps = { "Unlimited", "60", "120", "144" };
            int[] fpsVal = { 0, 60, 120, 144 };
            string[] traf = { "Off", "Light", "Normal", "Heavy" };
            string[] diff = { "Easy", "Normal", "Hard" };
            string[] tod = { "Dynamic", "Night", "Golden hour", "Day" };
            var res = Screen.resolutions;
            int fpsIdx = Array.IndexOf(fpsVal, st.FpsCap); if (fpsIdx < 0) fpsIdx = 0;
            var items = new List<MenuEntry>();
            void Add(string label, string value, Action<int> adjust, string info)
            {
                int idx = items.Count;
                var mi = new MenuEntry(label, () => adjust(1), info, value);
                mi.OnAdjust = d => adjust(d);
                items.Add(mi);
            }
            Action refresh = () => { ApplySettings(); SaveSystem.Save(save); ShowSettings(back, menu.SelectedIndex); };
            Add("Graphics quality", q[st.Quality], d => { st.Quality = Mathf.Clamp(st.Quality + d, 0, 3); refresh(); }, "Render scale, shadow distance, anti-aliasing, draw distance and vegetation density.");
            Add("Resolution", st.ResolutionIndex < 0 || st.ResolutionIndex >= res.Length ? "Native" : $"{res[st.ResolutionIndex].width}x{res[st.ResolutionIndex].height}", d => { st.ResolutionIndex = Mathf.Clamp(st.ResolutionIndex + d, -1, res.Length - 1); refresh(); }, "Screen resolution.");
            Add("Display mode", st.Fullscreen ? "Fullscreen" : "Windowed", d => { st.Fullscreen = !st.Fullscreen; refresh(); }, "Borderless fullscreen or a window.");
            Add("V-Sync", st.VSync ? "On" : "Off", d => { st.VSync = !st.VSync; refresh(); }, "Sync to the monitor refresh rate.");
            Add("Frame rate cap", fps[fpsIdx], d => { fpsIdx = (fpsIdx + d + fps.Length) % fps.Length; st.FpsCap = fpsVal[fpsIdx]; refresh(); }, "Limit the frame rate when V-Sync is off.");
            Add("Master volume", $"{Mathf.RoundToInt(st.MasterVolume * 100)}%", d => { st.MasterVolume = Mathf.Clamp01(st.MasterVolume + d * 0.1f); refresh(); }, "Overall volume.");
            Add("Engine volume", $"{Mathf.RoundToInt(st.EngineVolume * 100)}%", d => { st.EngineVolume = Mathf.Clamp(st.EngineVolume + d * 0.1f, 0f, 1.5f); refresh(); }, "Engine loudness relative to everything else.");
            Add("Driving assists", st.AssistLevel == 0 ? "Casual" : "Advanced", d => { st.AssistLevel = 1 - st.AssistLevel; refresh(); }, "Casual: speed-sensitive steering, counter-steer and drift hold, full traction and stability control.\nAdvanced: lighter steering help, less traction control, more slide.");
            Add("Units", st.Metric ? "km/h" : "mph", d => { st.Metric = !st.Metric; refresh(); }, "Speed units.");
            Add("Camera effects", $"{Mathf.RoundToInt(st.CameraEffects * 100)}%", d => { st.CameraEffects = Mathf.Clamp01(st.CameraEffects + d * 0.25f); refresh(); }, "Speed field-of-view widening and shake (comfort setting).");
            Add("Traffic", traf[st.TrafficDensity], d => { st.TrafficDensity = Mathf.Clamp(st.TrafficDensity + d, 0, 3); refresh(); }, "Ambient traffic density in free roam.");
            Add("AI difficulty", diff[st.Difficulty], d => { st.Difficulty = Mathf.Clamp(st.Difficulty + d, 0, 2); refresh(); }, "AI skill: Easy 0.88, Normal 0.94, Hard 0.99 of the racing line limit.");
            Add("Free roam time", tod[st.TimeOfDay], d => { st.TimeOfDay = (st.TimeOfDay + d + 4) % 4; refresh(); }, "Dynamic runs a 24-minute day; or lock the time.");
            items.Add(new MenuEntry("Back", back));
            menu.Show("SETTINGS", "← → or Enter to change", items, null, back, sel);
        }

        void ShowControls(Action back)
        {
            string txt = "<b>KEYBOARD</b>\nW / Up  throttle      S / Down  brake (hold to reverse)\nA D / Left Right  steer      Space  handbrake\nLeft Shift  nitro (tap again in the green window for Clean Boost; double-tap when full for Overdrive)\nE / Q  shift (manual)      V  camera      C  look back      R  reset\nEnter  start event      Esc  pause\n\n" +
                         "<b>GAMEPAD</b>\nRT throttle   LT brake   Left stick steer   A handbrake   B nitro\nRB/LB shift   Y camera   R3 look back   View reset   D-pad Up start event   Menu pause";
            menu.Show("CONTROLS", "", new List<MenuEntry> { new MenuEntry("Back", back) }, txt, back);
        }

        void Click() => Play(click, 0.4f);

        void Play(AudioClip c, float v)
        {
            if (uiAudio != null && c != null) uiAudio.PlayOneShot(c, v * save.Settings.MasterVolume);
        }

        void OnApplicationQuit()
        {
            if (save != null) SaveSystem.Save(save);
        }
    }
}
