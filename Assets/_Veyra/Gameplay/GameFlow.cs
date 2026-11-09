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
    [DefaultExecutionOrder(-1000)] // material templates must exist before any OnEnable/Start in the scene
    public sealed class GameFlow : MonoBehaviour
    {
        public enum State { Boot, MainMenu, FreeRoam, Garage, Race, Results }

        [SerializeField] GameDatabase db;
        [SerializeField] ChaseCamera chaseCamera;
        [SerializeField] WorldEnvironment environment;
        [SerializeField] Transform menuOrbitCentre;

        public static GameFlow Instance { get; private set; }
        public State Current { get; private set; } = State.Boot;
        public SaveData Save => save;
        public VehicleController PlayerCar => player;
        public VehicleDriver PlayerDriver => playerDriver;
        public GameDatabase Database => db;
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
        MenuController menu;
        TrafficSystem traffic;
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
            if (db != null)
            {
                RuntimeMaterials.Lit = db.LitTemplate;
                RuntimeMaterials.Unlit = db.UnlitAdditiveTemplate;
                RuntimeMaterials.ParticleAlpha = db.ParticleAlphaTemplate;
                RuntimeMaterials.ParticleAdditive = db.ParticleAdditiveTemplate;
            }
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-veyraSaveDir") SaveSystem.OverrideFolder = a[i + 1];
            foreach (var arg in a)
            {
                // Automated verification runs must keep ticking without focus and keep the window size they were given.
                if (arg.StartsWith("-veyraTour") || arg.StartsWith("-veyraDemo") || arg.StartsWith("-veyraAutoQuit") || arg.StartsWith("-veyraShots") || arg == "-autotest") { automated = true; Application.runInBackground = true; }
                if (arg.StartsWith("-screen-")) keepDisplay = true;
            }
            cam = chaseCamera != null ? chaseCamera.GetComponent<Camera>() : Camera.main;
        }

        void Start()
        {
            if (db != null)
            {
                RuntimeMaterials.Lit = db.LitTemplate;
                RuntimeMaterials.Unlit = db.UnlitAdditiveTemplate;
                RuntimeMaterials.ParticleAlpha = db.ParticleAlphaTemplate;
                RuntimeMaterials.ParticleAdditive = db.ParticleAdditiveTemplate;
            }
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
            hud.Build();
            menu = gameObject.AddComponent<MenuController>();
            menu.Build();
            traffic = new GameObject("Traffic").AddComponent<TrafficSystem>();
            traffic.transform.SetParent(transform, false);
            traffic.CarPrefabs = db != null ? db.TrafficModels : Array.Empty<GameObject>();
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
        float shotEvery = 6f;
        bool tour;

        void ParseArgs()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-veyraDemoRace" && i + 1 < args.Length) { demo = true; demoRaceId = args[i + 1]; }
                if (args[i] == "-veyraDemoFreeRoam") { demo = true; demoRaceId = null; }
                if (args[i] == "-veyraTour") tour = true;
                if (args[i] == "-veyraReviewShots" && i + 1 < args.Length) reviewDir = args[i + 1];
                if (args[i] == "-veyraTimeScale" && i + 1 < args.Length && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ts)) { baseTimeScale = Mathf.Clamp(ts, 0.1f, 8f); Time.timeScale = baseTimeScale; Time.maximumDeltaTime = 0.1f * baseTimeScale; }
                if (args[i] == "-veyraShots" && i + 1 < args.Length) shotDir = args[i + 1];
                if (args[i] == "-veyraShotEvery" && i + 1 < args.Length) float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out shotEvery);
            }
            if (!string.IsNullOrEmpty(shotDir)) StartCoroutine(Shots());
            if (tour) { demo = true; StartCoroutine(Tour()); }
            if (!string.IsNullOrEmpty(reviewDir)) { demo = true; tour = true; StartCoroutine(ReviewShots()); }
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
        void SpawnPlayer(Pose pose)
        {
            DespawnPlayer();
            var entry = db.Car(save.CurrentCar);
            var owned = save.Owned(entry.Id) ?? new OwnedCar { CarId = entry.Id, Paint = entry.DefaultPaint };
            var def = Upgrade.Build(entry, owned);
            player = SpawnCar(entry, def, owned.Paint, pose, true);
            playerRoot = player.gameObject;
            playerDriver = player.gameObject.AddComponent<VehicleDriver>();
            playerDriver.Bind(player, input, save.Settings.AssistLevel == 0 ? db.Casual : db.Advanced);
            playerDrift = player.gameObject.AddComponent<DriftScorer>();
            playerDrift.Bind(player);
            playerNitro = player.gameObject.AddComponent<NitroSystem>();
            playerNitro.PowerMultiplier = Upgrade.NitroMul[owned.Upgrades[Upgrade.Nitro]];
            playerNitro.ChargeMultiplier = Upgrade.NitroMul[owned.Upgrades[Upgrade.Nitro]];
            playerNitro.Bind(player, playerDriver, playerDrift);
            player.gameObject.AddComponent<VehicleAudio>().Bind(player, playerNitro, true);
            player.gameObject.AddComponent<VehicleFX>().Bind(player, playerNitro, true);
            hud.Bind(player, playerNitro, playerDrift);
            traffic.PlayerCar = player;
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
            return vc;
        }

        static void ApplyPaint(GameObject car, Color paint)
        {
            foreach (var r in car.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    bool isPaint = m.name.StartsWith("M_Paint") || m.name.StartsWith("Traffic_Body") || m.name.Contains("Placeholder_CarBody") || (r.name.StartsWith("Body") && !m.name.Contains("Glass"));
                    if (!isPaint) continue;
                    var inst = new Material(m) { name = m.name + "_Paint" };
                    inst.SetColor("_BaseColor", paint);
                    mats[i] = inst;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        void DespawnPlayer()
        {
            if (playerRoot != null) Destroy(playerRoot);
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
            var mat = RuntimeMaterials.NewUnlitTransparent("EventMarker", true);
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
                    if (mat != null)
                    {
                        var mi = new Material(mat);
                        mi.SetColor("_BaseColor", new Color(1f, 0.25f, 0.62f, 0.22f));
                        r.sharedMaterial = mi;
                    }
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    m.Visual = go;
                    markers.Add(m);
                }
        }

        void Update()
        {
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
        void OnPausePressed()
        {
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
                car.name = RivalNames[i % RivalNames.Length];
                var drv = car.gameObject.AddComponent<VehicleDriver>();
                var ai = car.gameObject.AddComponent<AIRacerDriver>();
                var nit = car.gameObject.AddComponent<NitroSystem>();
                nit.PassiveRegen = 1f;
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
                var m = RuntimeMaterials.NewLit("Garage");
                m.SetColor("_BaseColor", c);
                m.SetFloat("_Smoothness", smooth);
                m.SetFloat("_Metallic", metal);
                if (em.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", em.Value); }
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
