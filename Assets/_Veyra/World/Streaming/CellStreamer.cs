using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using VeyraCity.Core;

namespace VeyraCity.World.Streaming
{
    /// <summary>
    /// Card W7 / Roadmap A2: streams the 48 one-kilometre cell scenes (Assets/_Veyra/Scenes/Cells/Cell_A1..H6) around a focus point.
    /// <list type="bullet">
    /// <item>Window: cells whose rectangle is within <see cref="LoadRadius"/> of the focus are loaded (≈ 5 × 5 cells), cells
    /// farther than <see cref="UnloadRadius"/> are unloaded. Above <see cref="PrefetchSpeed"/> the point <see cref="LookAhead"/>
    /// metres ahead along the travel direction is a second focus (look-ahead prefetch).</item>
    /// <item>One async load or unload at a time, at <see cref="ThreadPriority.Low"/> background priority, so the main-thread
    /// integration is time-sliced across frames (≈ 2 ms per frame) instead of a single long activation.</item>
    /// <item>Boot: <see cref="CellBootLoader"/> loads every cell before this (main) scene starts, so systems that scan the world once
    /// in Start (ground height field, map, street trees, rooftop details, surface swaps) see all of it; unloading starts after
    /// <see cref="HoldAllSeconds"/>. Systems that keep per-object state subscribe to <see cref="CellLoaded"/>.</item>
    /// <item>Ground guard: a tracked rigidbody (the player car) is held kinematic while the cell under it is not loaded, so a teleport
    /// into an unloaded area never drops the car through missing ground.</item>
    /// <item>HLOD: each cell's far proxy (<see cref="CellProxy"/>, in this scene) is visible only while the cell is not loaded.</item>
    /// </list>
    /// Only active in the generated streaming scenes (<see cref="Streamed"/> is set by Veyra/World/Distribute To Cells); in the
    /// authoring scene VeyraCity.unity every cell is part of the scene and this component does nothing.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class CellStreamer : MonoBehaviour
    {
        public static CellStreamer Instance { get; private set; }

        /// <summary>Raised after a cell scene finished loading (cell name such as "E4", and its scene).</summary>
        public static event Action<string, Scene> CellLoaded;
        /// <summary>Raised before a cell scene unloads.</summary>
        public static event Action<string, Scene> CellUnloading;

        [Tooltip("Set by the distribution tool in the generated main scene; false in the authoring scene")]
        public bool Streamed;
        [Tooltip("Scene names of the cells (index = col + row * 8, row 0 = north row 1)")]
        public string[] CellScenes = new string[48];
        public float LoadRadius = 1600f, UnloadRadius = 2500f;
        [Tooltip("km/h above which the look-ahead point is also a focus")]
        public float PrefetchSpeed = 80f;
        public float LookAhead = 600f;
        [Tooltip("Seconds after start before far cells are unloaded (one-time world scans run first)")]
        public float HoldAllSeconds = 6f;
        [Tooltip("Radius around the tracked body that must be loaded before it may move (ground guard)")]
        public float GuardRadius = 150f;
        [Tooltip("Main-thread budget per frame for activating or deactivating a cell's objects (ms)")]
        public float SliceBudgetMs = 3f;
        [Tooltip("At most this many objects are woken or put to sleep per frame (the engine-side cost of renderers and colliders follows the count)")]
        public int MaxObjectsPerFrame = 40;
        [Tooltip("Log one line per load/unload with its duration and worst frame")]
        public bool LogOps = true;

        public const int Columns = 8, Rows = 6;
        const float Cell = WorldCoordinates.CellSizeM;

        enum CellState : byte { Unloaded, Loading, Loaded, Unloading }
        readonly CellState[] state = new CellState[Columns * Rows];
        readonly float[] lastLoadMs = new float[Columns * Rows];
        readonly CellProxy[] proxies = new CellProxy[Columns * Rows];
        readonly List<int> wanted = new List<int>(64);
        bool busy;
        float startTime;
        Vector3 focus, lastFocus, velocity;
        bool hasFocus, overlay;
        Vector3? forcedFocus;
        Rigidbody tracked;
        bool guardHeld, guardWasKinematic;
        float maxActivationMs, lastOpMs;
        string lastOp = "";
        int loads, unloads;

        public static bool Active => Instance != null && Instance.Streamed;
        /// <summary>Diagnostics: "-veyraNoCellEvents" suppresses CellLoaded / CellUnloading (hitch attribution runs).</summary>
        static readonly bool noEvents = Array.IndexOf(Environment.GetCommandLineArgs(), "-veyraNoCellEvents") >= 0;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            startTime = Time.realtimeSinceStartup;
            foreach (var p in GetComponentsInChildren<CellProxy>(true))
                if (p.Index >= 0 && p.Index < proxies.Length) proxies[p.Index] = p;
            if (!Streamed) return;
            // Cells the boot loader brought in are already loaded.
            for (int i = 0; i < state.Length; i++)
            {
                var s = SceneManager.GetSceneByName(CellScenes[i] ?? "");
                state[i] = s.IsValid() && s.isLoaded ? CellState.Loaded : CellState.Unloaded;
                SetProxy(i);
            }
            // Lighting, fog and Instantiate (the player car!) must go to the main scene. During an async load Awake runs before
            // the scene counts as loaded, so SetActiveScene waits for sceneLoaded, which fires before any Start.
            if (gameObject.scene.isLoaded) SceneManager.SetActiveScene(gameObject.scene);
            else SceneManager.sceneLoaded += OnMainLoaded;
        }

        void OnMainLoaded(Scene s, LoadSceneMode mode)
        {
            if (this == null || s != gameObject.scene) return;
            SceneManager.sceneLoaded -= OnMainLoaded;
            SceneManager.SetActiveScene(s);
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnMainLoaded;
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ public API

        public static string CellName(int index) => $"{"ABCDEFGH"[index % Columns]}{index / Columns + 1}";

        /// <summary>Cell index of a Unity position, clamped into the grid.</summary>
        public static int IndexOf(Vector3 unity)
        {
            var w = WorldCoordinates.FromUnity(unity);
            int col = Mathf.Clamp(Mathf.FloorToInt(w.x / Cell), 0, Columns - 1);
            int row = Mathf.Clamp(5 - Mathf.FloorToInt(w.y / Cell), 0, Rows - 1);
            return col + row * Columns;
        }

        /// <summary>Unity-space rectangle (x, z) of a cell.</summary>
        public static Rect CellRect(int index)
        {
            int col = index % Columns, row = index / Columns;
            float x0 = col * Cell - WorldCoordinates.OriginOffsetX;
            float z0 = (5 - row) * Cell - WorldCoordinates.OriginOffsetY;
            return new Rect(x0, z0, Cell, Cell);
        }

        static float Distance(Rect r, Vector3 p)
        {
            float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax);
            float dz = Mathf.Max(r.yMin - p.z, 0f, p.z - r.yMax);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>The rigidbody the ground guard protects (the player car).</summary>
        public static void Track(Rigidbody body)
        {
            if (Instance == null) return;
            Instance.ReleaseGuard();
            Instance.tracked = body;
        }

        /// <summary>Forces the window around a point (teleports, review cameras); null returns to following the camera/car.</summary>
        public static void Focus(Vector3? point)
        {
            if (Instance == null) return;
            Instance.forcedFocus = point;
            if (point.HasValue) { Instance.focus = point.Value; Instance.hasFocus = true; Instance.velocity = Vector3.zero; }
        }

        /// <summary>True when every cell within <paramref name="radius"/> of the point is loaded (always true when not streaming).</summary>
        public static bool IsReady(Vector3 point, float radius = 600f)
        {
            if (!Active) return true;
            var s = Instance;
            for (int i = 0; i < s.state.Length; i++)
                if (Distance(CellRect(i), point) <= radius && s.state[i] != CellState.Loaded) return false;
            return true;
        }

        /// <summary>Focuses on the point and waits (real time) until the cells around it are loaded.</summary>
        public static IEnumerator WaitUntilReady(Vector3 point, float radius = 600f, float timeout = 60f)
        {
            if (!Active) yield break;
            Focus(point);
            float t0 = Time.realtimeSinceStartup;
            while (!IsReady(point, radius) && Time.realtimeSinceStartup - t0 < timeout) yield return null;
            if (!IsReady(point, radius)) Debug.LogWarning($"[CellStreamer] cells around {point} not ready after {timeout:0} s");
        }

        public static int LoadedCount
        {
            get
            {
                if (Instance == null) return 0;
                int n = 0;
                foreach (var st in Instance.state) if (st == CellState.Loaded) n++;
                return n;
            }
        }

        int opHitches;
        /// <summary>Frames over 50 ms while a cell operation ran (load, activation, deactivation, unload): the hitches streaming causes.</summary>
        public static int StreamingHitches => Instance != null ? Instance.opHitches : 0;
        void CountHitch() { if (Time.unscaledDeltaTime > 0.05f) opHitches++; }

        public static float MaxActivationMs => Instance != null ? Instance.maxActivationMs : 0f;
        public static int LoadOps => Instance != null ? Instance.loads : 0;
        public static int UnloadOps => Instance != null ? Instance.unloads : 0;

        // ------------------------------------------------------------------ update

        void Update()
        {
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.f3Key.wasPressedThisFrame) overlay = !overlay;
            if (!Streamed) return;
            UpdateFocus();
            UpdateGuard();
            if (busy) return;
            bool holdAll = Time.realtimeSinceStartup - startTime < HoldAllSeconds;
            ChooseNext(holdAll);
        }

        void UpdateFocus()
        {
            if (forcedFocus.HasValue) { focus = forcedFocus.Value; hasFocus = true; return; }
            Vector3 p;
            if (tracked != null) p = tracked.position;
            else if (Camera.main != null) p = Camera.main.transform.position;
            else return;
            if (hasFocus && Time.deltaTime > 0f)
                velocity = Vector3.Lerp(velocity, (p - lastFocus) / Time.deltaTime, 1f - Mathf.Exp(-Time.deltaTime * 3f));
            if ((p - lastFocus).sqrMagnitude > 250f * 250f) velocity = Vector3.zero;   // a teleport is not travel
            lastFocus = p;
            focus = p;
            hasFocus = true;
        }

        void ChooseNext(bool holdAll)
        {
            if (!hasFocus) return;
            var ahead = focus;
            var flat = new Vector3(velocity.x, 0f, velocity.z);
            bool prefetch = flat.magnitude * 3.6f > PrefetchSpeed;
            if (prefetch) ahead = focus + flat.normalized * LookAhead;

            // Load: nearest wanted cell first (distance to the focus, the look-ahead point counts the same).
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < state.Length; i++)
            {
                if (state[i] != CellState.Unloaded || string.IsNullOrEmpty(CellScenes[i])) continue;
                var r = CellRect(i);
                float d = Distance(r, focus);
                if (prefetch) d = Mathf.Min(d, Distance(r, ahead));
                if (d <= LoadRadius && d < bestD) { bestD = d; best = i; }
            }
            if (best >= 0) { StartCoroutine(Load(best)); return; }
            if (holdAll) return;
            // Unload: farthest cell beyond the unload radius (from both points).
            int worst = -1;
            float worstD = 0f;
            for (int i = 0; i < state.Length; i++)
            {
                if (state[i] != CellState.Loaded) continue;
                var r = CellRect(i);
                float d = Distance(r, focus);
                if (prefetch) d = Mathf.Min(d, Distance(r, ahead));
                if (d > UnloadRadius && d > worstD) { worstD = d; worst = i; }
            }
            if (worst >= 0) StartCoroutine(Unload(worst));
        }

        IEnumerator Load(int i)
        {
            busy = true;
            state[i] = CellState.Loading;
            float t0 = Time.realtimeSinceStartup;
            var prio = Application.backgroundLoadingPriority;
            Application.backgroundLoadingPriority = ThreadPriority.Low;
            var op = SceneManager.LoadSceneAsync(CellScenes[i], LoadSceneMode.Additive);
            if (op == null)
            {
                Debug.LogError($"[CellStreamer] cell scene {CellScenes[i]} is not in the build");
                state[i] = CellState.Unloaded;
                CellScenes[i] = null;
                busy = false;
                yield break;
            }
            float worst = 0f; int frames = 0;
            while (!op.isDone) { yield return null; frames++; worst = Mathf.Max(worst, Time.unscaledDeltaTime * 1000f); CountHitch(); }
            Application.backgroundLoadingPriority = prio;
            var scene = SceneManager.GetSceneByName(CellScenes[i]);
            // time-sliced activation of the cell's objects (saved inactive by the distribution tool)
            var content = Content(scene);
            int activated = 0;
            if (content != null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int inFrame = 0;
                foreach (var o in content.Objects)
                {
                    if (o == null || o.activeSelf) continue;
                    long fc = FrameCost.Start();
                    o.SetActive(true);
                    FrameCost.Stop("Streamer.activate", fc);
                    activated++;
                    if (++inFrame >= MaxObjectsPerFrame || sw.Elapsed.TotalMilliseconds > SliceBudgetMs)
                    { yield return null; frames++; worst = Mathf.Max(worst, Time.unscaledDeltaTime * 1000f); CountHitch(); sw.Restart(); inFrame = 0; }
                }
            }
            state[i] = CellState.Loaded;
            lastLoadMs[i] = (Time.realtimeSinceStartup - t0) * 1000f;
            loads++;
            lastOp = $"load {CellName(i)}";
            lastOpMs = lastLoadMs[i];
            SetProxy(i);
            if (LogOps) Debug.Log($"[CellStreamer] load {CellName(i)}: {lastLoadMs[i]:0} ms over {frames} frames, {activated} objects activated, worst frame {worst:0.0} ms");
            float a0 = Time.realtimeSinceStartup;
            long fcb = FrameCost.Start();
            try { if (!noEvents) CellLoaded?.Invoke(CellName(i), scene); }
            catch (Exception e) { Debug.LogException(e); }
            FrameCost.Stop("Streamer.CellLoaded callbacks", fcb);
            maxActivationMs = Mathf.Max(maxActivationMs, (Time.realtimeSinceStartup - a0) * 1000f);
            busy = false;
        }

        IEnumerator Unload(int i)
        {
            busy = true;
            state[i] = CellState.Unloading;
            var scene = SceneManager.GetSceneByName(CellScenes[i]);
            try { if (!noEvents) CellUnloading?.Invoke(CellName(i), scene); }
            catch (Exception e) { Debug.LogException(e); }
            state[i] = CellState.Unloaded;
            SetProxy(i);   // the proxy covers the cell while it goes away
            float t0 = Time.realtimeSinceStartup;
            float worst = 0f; int frames = 0;
            // time-sliced deactivation first: renderers, colliders and behaviours leave a few per frame
            var content = Content(scene);
            if (content != null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int inFrame = 0;
                for (int k = content.Objects.Length - 1; k >= 0; k--)
                {
                    var o = content.Objects[k];
                    if (o == null || !o.activeSelf) continue;
                    long fc = FrameCost.Start();
                    o.SetActive(false);
                    FrameCost.Stop("Streamer.deactivate", fc);
                    if (++inFrame >= MaxObjectsPerFrame || sw.Elapsed.TotalMilliseconds > SliceBudgetMs)
                    { yield return null; frames++; worst = Mathf.Max(worst, Time.unscaledDeltaTime * 1000f); CountHitch(); sw.Restart(); inFrame = 0; }
                }
            }
            long fcu = FrameCost.Start();
            var op = scene.IsValid() ? SceneManager.UnloadSceneAsync(scene) : null;
            FrameCost.Stop("Streamer.unload call", fcu);
            if (op != null) while (!op.isDone) { yield return null; frames++; worst = Mathf.Max(worst, Time.unscaledDeltaTime * 1000f); CountHitch(); }
            unloads++;
            lastOp = $"unload {CellName(i)}";
            lastOpMs = (Time.realtimeSinceStartup - t0) * 1000f;
            if (LogOps) Debug.Log($"[CellStreamer] unload {CellName(i)}: {lastOpMs:0} ms over {frames} frames, worst frame {worst:0.0} ms");
            busy = false;
        }

        static CellContent Content(Scene scene)
        {
            if (!scene.IsValid()) return null;
            foreach (var r in scene.GetRootGameObjects())
            {
                var c = r.GetComponent<CellContent>();
                if (c != null) return c;
            }
            return null;
        }

        /// <summary>True while no cell operation is running and nothing in the window is left to load or unload.</summary>
        public static bool Settled
        {
            get
            {
                if (!Active) return true;
                var s = Instance;
                if (s.busy || Time.realtimeSinceStartup - s.startTime < s.HoldAllSeconds) return false;
                for (int i = 0; i < s.state.Length; i++)
                {
                    float d = Distance(CellRect(i), s.focus);
                    if (s.state[i] == CellState.Unloaded && d <= s.LoadRadius && !string.IsNullOrEmpty(s.CellScenes[i])) return false;
                    if (s.state[i] == CellState.Loaded && d > s.UnloadRadius) return false;
                }
                return true;
            }
        }

        void SetProxy(int i)
        {
            var p = proxies[i];
            if (p == null) return;
            bool show = Streamed && state[i] != CellState.Loaded;
            if (p.gameObject.activeSelf != show) p.gameObject.SetActive(show);
        }

        // ------------------------------------------------------------------ ground guard

        void UpdateGuard()
        {
            if (tracked == null) { guardHeld = false; return; }
            bool ready = IsReady(tracked.position, GuardRadius);
            if (!ready && !guardHeld)
            {
                guardWasKinematic = tracked.isKinematic;
                tracked.isKinematic = true;
                guardHeld = true;
                Debug.Log($"[CellStreamer] ground guard holds {tracked.name} at {tracked.position} until cell {CellName(IndexOf(tracked.position))} is loaded");
            }
            else if (ready && guardHeld) ReleaseGuard();
        }

        void ReleaseGuard()
        {
            if (!guardHeld) return;
            guardHeld = false;
            if (tracked != null) tracked.isKinematic = guardWasKinematic;
        }

        // ------------------------------------------------------------------ F3 overlay

        readonly StringBuilder sb = new StringBuilder(512);

        void OnGUI()
        {
            if (!overlay) return;
            sb.Clear();
            var w = WorldCoordinates.FromUnity(focus);
            sb.AppendLine($"Streaming {(Streamed ? "on" : "off (authoring scene)")} | focus {WorldCoordinates.CellOf(w.x, w.y) ?? "-"} {WorldCoordinates.ChunkOf(w.x, w.y) ?? ""}{(forcedFocus.HasValue ? " (forced)" : "")} | {velocity.magnitude * 3.6f:0} km/h");
            sb.AppendLine($"Loaded {LoadedCount}/48 | loads {loads} unloads {unloads} | last {lastOp} {lastOpMs:0} ms | max activation callback {maxActivationMs:0.0} ms | guard {(guardHeld ? "HOLD" : "free")}");
            sb.AppendLine($"Mono {UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576} MB | allocated {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576} MB | gfx {UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576} MB");
            for (int row = 0; row < Rows; row++)
            {
                for (int col = 0; col < Columns; col++)
                {
                    int i = col + row * Columns;
                    char c = state[i] == CellState.Loaded ? '#' : state[i] == CellState.Loading ? '+' : state[i] == CellState.Unloading ? '-' : '.';
                    sb.Append(c).Append(' ');
                }
                sb.AppendLine();
            }
            GUI.color = Color.white;
            GUI.Box(new Rect(10, 10, 620, 190), GUIContent.none);
            GUI.Label(new Rect(18, 14, 610, 186), sb.ToString());
        }
    }
}
