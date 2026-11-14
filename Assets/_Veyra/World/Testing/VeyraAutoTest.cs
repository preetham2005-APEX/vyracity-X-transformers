using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using VeyraCity.Core;
using VeyraCity.Gameplay;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;
using VeyraCity.World.Data;
using VeyraCity.World.Streaming;

namespace VeyraCity.Testing
{
    /// <summary>
    /// Card V0 runtime test runner, active only with <c>-autotest</c>:
    /// <code>VeyraCity.exe -autotest &lt;seconds, 0 = until the route is complete&gt; -route &lt;name&gt; -statsPath &lt;file&gt; [-timeOfDay &lt;h&gt;] [-quality &lt;name&gt;] [-vsync 0|1] [-reviewCapture]</code>
    /// Enters free roam (no traffic), binds a <see cref="RouteAutopilot"/> to the Arden Kite, drives every segment of the route
    /// (each starts with a teleport onto the drivable surface at target speed), collects frame, memory, log and vehicle metrics,
    /// writes stats.json and quits. Frames in the 2 s after a teleport are reported separately (teleport_windows).
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class VeyraAutoTest : MonoBehaviour
    {
        public sealed class Options
        {
            public bool Enabled, ReviewCapture;
            public float Seconds;
            public string Route = "R_SMOKE", StatsPath, Quality, ReviewDir, ReviewFilter;
            public float TimeOfDay = float.NaN;
            /// <summary>-vsync 0|1 (card L1/V1: perf numbers need a VSync-off run for headroom); -1 keeps the game setting.</summary>
            public int VSync = -1;

            public static Options Parse(string[] a)
            {
                var o = new Options();
                for (int i = 0; i < a.Length; i++)
                {
                    string next = i + 1 < a.Length ? a[i + 1] : null;
                    switch (a[i].ToLowerInvariant())
                    {
                        case "-autotest": o.Enabled = true; if (TryF(next, out var s)) { o.Seconds = Mathf.Max(0f, s); i++; } break;
                        case "-route": if (next != null) { o.Route = next; i++; } break;
                        case "-statspath": if (next != null) { o.StatsPath = next; i++; } break;
                        case "-timeofday": if (TryF(next, out var h)) { o.TimeOfDay = Mathf.Repeat(h, 24f); i++; } break;
                        case "-quality": if (next != null) { o.Quality = next; i++; } break;
                        case "-vsync": if (TryF(next, out var vs)) { o.VSync = vs > 0.5f ? 1 : 0; i++; } break;
                        case "-reviewcapture": o.ReviewCapture = true; break;
                        case "-reviewdir": if (next != null) { o.ReviewDir = next; i++; } break;
                        case "-reviewfilter": if (next != null) { o.ReviewFilter = next; i++; } break;
                    }
                }
                return o;
            }

            static bool TryF(string s, out float v) { v = 0f; return s != null && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v); }
        }

        public const float SettleSeconds = 1.0f;       // vehicle metrics ignore the first second after a teleport
        public const float PerfWindowSeconds = 2.0f;   // frame stats ignore the first 2 s after a teleport (reported separately)
        public const float WallDropKmh = 30f, WallWindowS = 0.2f;
        const float MaxUnlimitedSeconds = 1800f;
        /// <summary>JSON has no NaN: "no measurement" is written as -999.</summary>
        public const float NoValue = -999f;

        static Options opts;
        static readonly object logLock = new object();
        static int logExceptions, logErrors, logWarnings, logShaderNull;
        static readonly List<string> firstErrors = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            opts = Options.Parse(Environment.GetCommandLineArgs());
            if (!opts.Enabled) return;
            Application.logMessageReceivedThreaded += OnLog;
            Application.runInBackground = true;
            var go = new GameObject("[VeyraAutoTest]");
            DontDestroyOnLoad(go);
            go.AddComponent<VeyraAutoTest>();
        }

        static void OnLog(string message, string stack, LogType type)
        {
            lock (logLock)
            {
                if (type == LogType.Exception) { logExceptions++; Keep(message); }
                else if (type == LogType.Error || type == LogType.Assert) { logErrors++; Keep(message); }
                else if (type == LogType.Warning) logWarnings++;
                if (message != null && message.IndexOf("shader is null", StringComparison.OrdinalIgnoreCase) >= 0) logShaderNull++;
            }
        }

        static void Keep(string m) { if (firstErrors.Count < 10) firstErrors.Add((m ?? "").Split('\n')[0]); }

        // ------------------------------------------------------------------ run state
        readonly AutoTestReport report = new AutoTestReport();
        readonly FrameStats frames = new FrameStats();
        VehicleController car;
        RouteAutopilot autopilot;
        AutoTestReport.SegmentReport cur;
        VeyraTestRoute route;
        bool driving;
        float routeStartRealtime, settleUntil, perfSettleUntilRealtime, wallCooldownUntil;
        float airTime, prevVy, minKmh;
        bool airLatched, fallLatched;
        readonly float[] ayWindow = new float[5];
        int ayIndex;
        readonly Queue<(float t, float v)> speedHistory = new Queue<(float, float)>();
        readonly List<(float t, string name, bool visible)> recentHits = new List<(float, string, bool)>();
        ProfilerRecorder gcRecorder, gfxRecorder;
        double gcBytes;
        int gcFrames;
        // BUG-090: GPU/CPU frame times and render counters over the driving frames (same frames as `frames`).
        ProfilerRecorder setPassRecorder, drawCallsRecorder, batchesRecorder, trianglesRecorder;
        readonly FrameStats gpuMs = new FrameStats(), cpuMainMs = new FrameStats(), cpuRenderMs = new FrameStats();
        readonly FrameStats setPassCalls = new FrameStats(), drawCalls = new FrameStats(), batchCounts = new FrameStats(), triangleCounts = new FrameStats();
        readonly FrameTiming[] frameTiming = new FrameTiming[1];

        IEnumerator Start()
        {
            report.date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            report.route = opts.Route;
            report.time_limit_s = opts.Seconds;
            report.review_capture = opts.ReviewCapture ? "requested" : "not requested";
            Debug.Log($"[AutoTest] start route={opts.Route} seconds={opts.Seconds} stats={opts.StatsPath} timeOfDay={(float.IsNaN(opts.TimeOfDay) ? "route default" : opts.TimeOfDay.ToString("0.0", CultureInfo.InvariantCulture))} quality={(string.IsNullOrEmpty(opts.Quality) ? "current" : opts.Quality)}");

            float wait = Time.realtimeSinceStartup;
            GameFlow gf;
            while ((gf = GameFlow.Instance) == null || gf.Current == GameFlow.State.Boot)
            {
                if (Time.realtimeSinceStartup - wait > 60f) { yield return Abort("GameFlow did not start within 60 s"); yield break; }
                yield return null;
            }

            var cfg = Resources.Load<VeyraAutoTestConfig>(VeyraAutoTestConfig.ResourcePath);
            if (cfg == null || cfg.RoadNetworkJson == null) { yield return Abort($"Resources/{VeyraAutoTestConfig.ResourcePath} or its road JSON is missing (run Veyra/Test/Create Test Routes)"); yield break; }
            route = cfg.Find(opts.Route);
            if (route == null) { yield return Abort($"unknown route '{opts.Route}'"); yield break; }
            RoadNetworkData net;
            try { net = RoadNetwork.Parse(cfg.RoadNetworkJson.text); }
            catch (Exception e) { net = null; Debug.LogException(e); }
            if (net == null) { yield return Abort("road_network_geometry.json did not parse"); yield break; }

            if (!string.IsNullOrEmpty(opts.Quality))
            {
                int qi = Array.FindIndex(QualitySettings.names, n => n.Equals(opts.Quality, StringComparison.OrdinalIgnoreCase));
                if (qi >= 0) QualitySettings.SetQualityLevel(qi, true);
                else Debug.LogWarning($"[AutoTest] unknown quality level '{opts.Quality}'; keeping {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
            }
            if (opts.VSync >= 0)
            {
                QualitySettings.vSyncCount = opts.VSync;
                Application.targetFrameRate = -1;
            }
            report.time_of_day = float.IsNaN(opts.TimeOfDay) ? route.DefaultTimeOfDay : opts.TimeOfDay;
            var driver = gf.BeginAutomatedFreeRoam(report.time_of_day);
            yield return null;
            yield return null;
            car = gf.PlayerCar;
            if (car == null || driver == null) { yield return Abort("no player car after entering free roam"); yield break; }
            autopilot = new RouteAutopilot(car);
            var preset = gf.Database != null && gf.Database.AIPreset != null ? gf.Database.AIPreset : driver.Preset;
            driver.Bind(car, autopilot, preset);
            car.Collided += OnCollided;
            report.vehicle.car = car.Definition != null ? car.Definition.name : car.name;
            gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            gfxRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory");
            setPassRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            drawCallsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            batchesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            trianglesRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            report.render.frame_timing_enabled = FrameTimingManager.IsFeatureEnabled();

            // Roadmap A2: let the boot-time unload of far cells finish (the menu covers it in a real session)
            if (CellStreamer.Active)
            {
                float s0 = Time.realtimeSinceStartup;
                while (!CellStreamer.Settled && Time.realtimeSinceStartup - s0 < 90f) yield return null;
                report.streaming.wait_s += Time.realtimeSinceStartup - s0;
                Debug.Log($"[AutoTest] streaming settled after {Time.realtimeSinceStartup - s0:0.0} s ({CellStreamer.LoadedCount} cells loaded)");
            }
            FrameCost.Reset();
            report.vehicle.segments_total = route.Segments.Count;
            routeStartRealtime = Time.realtimeSinceStartup;
            string endReason = "route complete";
            for (int i = 0; i < route.Segments.Count; i++)
            {
                var seg = route.Segments[i];
                cur = new AutoTestReport.SegmentReport { index = i, road = seg.roadId, s_start = seg.sStart_m, s_end = seg.sEnd_m, target_kmh = seg.targetSpeed_kmh, note = seg.note, result = "not reached" };
                report.per_segment.Add(cur);
                if (TimeUp()) { endReason = "time limit"; continue; }
                var road = net.Road(seg.roadId);
                if (road == null) { cur.result = "unknown road"; Debug.LogError($"[AutoTest] route {route.name} segment {i}: unknown road {seg.roadId}"); continue; }

                var lane = RouteAutopilot.BuildLanePath(road, seg.sStart_m, seg.sEnd_m, 60f, out float endU);
                float target = seg.targetSpeed_kmh / 3.6f;
                // Roadmap A2: the cells around the spawn must be loaded before the spawn raycasts (not counted in the frame stats)
                if (CellStreamer.Active)
                {
                    float w0 = Time.realtimeSinceStartup;
                    yield return CellStreamer.WaitUntilReady(lane.PositionAt(0f), 700f);
                    float w = Time.realtimeSinceStartup - w0;
                    report.streaming.waits++;
                    report.streaming.wait_s += w;
                    report.streaming.max_wait_s = Mathf.Max(report.streaming.max_wait_s, w);
                }
                Teleport(lane, target);
                CellStreamer.Focus(null);   // follow the car again
                if (i == 0) StreamingSnapshot(true);
                autopilot.Begin(lane, endU, target);
                cur.reached = true;
                cur.result = "running";
                minKmh = float.MaxValue;
                float segStart = Time.time;
                driving = true;
                bool spawnChecked = false;
                float stuck = 0f, waiting = 0f, limit = seg.LengthM / Mathf.Max(1f, target) * 2f + 20f;
                int slowdowns0 = autopilot.ObstacleSlowdowns;
                while (true)
                {
                    yield return null;
                    float el = Time.time - segStart;
                    if (!spawnChecked && el >= SettleSeconds) { CheckSpawn(); spawnChecked = true; }
                    if (autopilot.U >= endU - 3f) { cur.completed = true; cur.result = "completed"; break; }
                    if (el > limit) { cur.result = "timeout"; break; }
                    // waiting behind traffic (autopilot sees a vehicle in the lane) is not "stuck"; the time limit grows with it
                    bool queued = autopilot.ObstacleDistance >= 0f && autopilot.ObstacleDistance < 20f;
                    if (car.State.Speed < 1f && queued) { waiting += Time.deltaTime; limit += Time.deltaTime; if (waiting > 30f) { cur.result = "blocked by traffic"; break; } }
                    else if (car.State.Speed < 1f && el > 4f) { stuck += Time.deltaTime; if (stuck > 6f) { cur.result = "stuck"; break; } }
                    else { stuck = 0f; if (car.State.Speed > 3f) waiting = 0f; }
                    if (TimeUp()) { cur.result = "time limit"; endReason = "time limit"; break; }
                }
                driving = false;
                if (!spawnChecked) CheckSpawn();
                cur.time_s = Time.time - segStart;
                cur.avg_kmh = cur.time_s > 0f ? cur.distance_m / cur.time_s * 3.6f : 0f;
                cur.min_kmh = minKmh == float.MaxValue ? 0f : minKmh;
                if (cur.completed) report.vehicle.segments_completed++;
                Debug.Log($"[AutoTest] segment {i} {seg} -> {cur.result} in {cur.time_s:0.0} s, avg {cur.avg_kmh:0} km/h, min {cur.min_kmh:0}, airborne {cur.airborne_events}, falls {cur.falls_below_surface}, walls {cur.invisible_wall_events}, spawn on road {cur.spawned_on_road} ({cur.spawn_surface}, {cur.spawn_height_above_surface_m:0.00} m), traffic slowdowns {autopilot.ObstacleSlowdowns - slowdowns0}");
            }
            autopilot.Active = false;
            report.route_complete = report.vehicle.segments_completed == report.vehicle.segments_total;
            if (endReason == "route complete" && !report.route_complete)
                endReason = $"all segments attempted, {report.vehicle.segments_completed}/{report.vehicle.segments_total} completed";
            report.materials = MaterialAuditor.Run();
            Debug.Log("[AutoTest] " + MaterialAuditor.Summary(report.materials));
            if (opts.ReviewCapture)
            {
                if (cfg.ReviewCameras != null)
                {
                    // Card S5: the review cameras after the route (not part of the frame statistics, which stopped with the route).
                    string dir = !string.IsNullOrEmpty(opts.ReviewDir) ? opts.ReviewDir : ReviewCameraRig.OutputDir(Path.GetDirectoryName(Application.dataPath) ?? ".", "review", "player");
                    yield return VeyraReviewCapture.CaptureAll(gf, cfg.ReviewCameras, net, dir, opts.ReviewFilter, $"review after {route.name}");
                    report.review_capture = $"{cfg.ReviewCameras.Filter(opts.ReviewFilter).Count} cameras -> {dir}";
                }
                else report.review_capture = "requested, but Resources/Testing/VeyraAutoTestConfig has no ReviewCameras";
            }
            yield return Finish(endReason, 0);
        }

        bool TimeUp()
        {
            float limit = opts.Seconds > 0f ? opts.Seconds : MaxUnlimitedSeconds;
            return Time.realtimeSinceStartup - routeStartRealtime >= limit;
        }

        void Teleport(PolylinePath lane, float speed)
        {
            var p = lane.PositionAt(0f);
            var fwd = lane.PositionAt(4f) - p;
            float surfaceY = p.y;
            var normal = Vector3.up;
            // Spawn on the top drivable surface near the route height, like a wheel would land: card C2 lays eased approach
            // ribbons up to 8.5 m above the data profile, and the old (buried) road below must not win. Fallback: the surface
            // closest to the profile height (terrain, or no VehicleSurface).
            float best = 6f, top = float.MinValue;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 25f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.rigidbody != null && h.rigidbody == car.Body) continue;
                bool drivable = !(h.collider is TerrainCollider) && h.collider.GetComponentInParent<VehicleSurface>() != null;
                if (drivable && h.point.y >= p.y - 6f && h.point.y <= p.y + 9f && h.point.y > top)
                {
                    top = h.point.y; surfaceY = h.point.y; normal = h.normal;
                    continue;
                }
                float d = Mathf.Abs(h.point.y - p.y);
                if (top == float.MinValue && d < best) { best = d; surfaceY = h.point.y; normal = h.normal; }
            }
            var pos = new Vector3(p.x, surfaceY + 0.45f, p.z);
            var dir = Vector3.ProjectOnPlane(fwd, normal);
            car.ResetTo(pos, Quaternion.LookRotation(dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward, normal));
            car.ForceForwardSpeed(speed);
            settleUntil = Time.time + SettleSeconds;
            perfSettleUntilRealtime = Time.realtimeSinceStartup + PerfWindowSeconds;
            report.teleport_windows.count++;
            report.teleport_windows.excluded_s += PerfWindowSeconds;
            speedHistory.Clear();
            recentHits.Clear();
            airTime = 0f; airLatched = false; fallLatched = false;
            prevVy = 0f; Array.Clear(ayWindow, 0, ayWindow.Length);
        }

        void CheckSpawn()
        {
            var pos = car.transform.position;
            string surface = "nothing below";
            bool road = false;
            float height = NoValue;
            foreach (var h in SortedHits(pos + Vector3.up * 2f, 12f))
            {
                if (h.rigidbody != null && h.rigidbody == car.Body) continue;
                var vs = h.collider.GetComponentInParent<VehicleSurface>();
                bool terrain = h.collider is TerrainCollider;
                surface = terrain ? "terrain" : vs != null && vs.Surface != null ? vs.Surface.Id : h.collider.name;
                road = !terrain && vs != null && vs.Surface != null && vs.Surface.Id.IndexOf("grass", StringComparison.OrdinalIgnoreCase) < 0;
                height = pos.y - h.point.y;
                break;
            }
            cur.spawn_surface = surface;
            cur.spawn_height_above_surface_m = height;
            cur.spawned_on_road = road && car.State.GroundedWheels >= 3 && Mathf.Abs(autopilot.LateralError) < 3f && height != NoValue && Mathf.Abs(height) < 1f;
            if (!cur.spawned_on_road) AddEvent("spawn_off_road", $"surface {surface}, height {height:0.00} m, grounded {car.State.GroundedWheels}, lateral {autopilot.LateralError:0.0} m");
        }

        static RaycastHit[] SortedHits(Vector3 from, float distance)
        {
            var hits = Physics.RaycastAll(from, Vector3.down, distance, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            return hits;
        }

        void OnCollided(VehicleController v, Collision c)
        {
            if (!driving || c.collider == null) return;
            var col = c.collider;
            bool drivable = col.GetComponentInParent<VehicleSurface>() != null || col is TerrainCollider;
            bool visible = !drivable && (col.name.StartsWith("UBX_") || col.name.StartsWith("UCX_") || col.attachedRigidbody != null || col.GetComponentInParent<Renderer>() != null);
            recentHits.Add((Time.time, col.name, visible));
        }

        void FixedUpdate()
        {
            if (!driving || car == null || cur == null) return;
            float dt = Time.fixedDeltaTime, t = Time.time;
            var st = car.State;
            float v = st.Speed;
            cur.distance_m += v * dt;
            report.vehicle.distance_m += v * dt;
            bool settled = t >= settleUntil;
            float vy = car.Body.linearVelocity.y;
            ayWindow[ayIndex++ % ayWindow.Length] = (vy - prevVy) / dt;
            prevVy = vy;
            speedHistory.Enqueue((t, v));
            while (speedHistory.Count > 0 && speedHistory.Peek().t < t - WallWindowS) speedHistory.Dequeue();
            recentHits.RemoveAll(h => h.t < t - 0.35f);
            if (!settled) return;

            minKmh = Mathf.Min(minKmh, v * 3.6f);
            cur.max_lateral_error_m = Mathf.Max(cur.max_lateral_error_m, Mathf.Abs(autopilot.LateralError));
            float ay = 0f; foreach (var a in ayWindow) ay += a; ay /= ayWindow.Length;
            float g = Mathf.Abs(ay) / 9.81f;
            cur.max_vertical_accel_g = Mathf.Max(cur.max_vertical_accel_g, g);
            report.vehicle.max_vertical_accel_g = Mathf.Max(report.vehicle.max_vertical_accel_g, g);

            if (st.GroundedWheels == 0) airTime += dt; else { airTime = 0f; airLatched = false; }
            if (airTime >= 0.3f && !airLatched)
            {
                airLatched = true; cur.airborne_events++; report.vehicle.airborne_events++;
                AddEvent("airborne", $"all wheels off the ground for 0.3 s at {v * 3.6f:0} km/h");
            }

            float pathY = autopilot.Path.PositionAt(autopilot.U).y;
            bool below = car.transform.position.y < pathY - 3f;
            if (below && !fallLatched)
            {
                fallLatched = true; cur.falls_below_surface++; report.vehicle.falls_below_surface++;
                AddEvent("fall_below_surface", $"car y {car.transform.position.y:0.0} m, road profile y {pathY:0.0} m");
            }
            else if (!below) fallLatched = false;

            if (t >= wallCooldownUntil)
            {
                float maxV = 0f; foreach (var s in speedHistory) maxV = Mathf.Max(maxV, s.v);
                if ((maxV - v) * 3.6f > WallDropKmh)
                {
                    wallCooldownUntil = t + 1f;
                    bool visible = recentHits.Exists(h => h.visible);
                    string hits = recentHits.Count == 0 ? "no collision" : string.Join(", ", recentHits.ConvertAll(h => h.name + (h.visible ? "(visible)" : "")));
                    string detail = $"{maxV * 3.6f:0} → {v * 3.6f:0} km/h within {WallWindowS} s; contacts: {hits}";
                    if (visible) { cur.visible_impacts++; report.vehicle.visible_impacts++; AddEvent("visible_impact", detail); }
                    else { cur.invisible_wall_events++; report.vehicle.invisible_wall_events++; AddEvent("invisible_wall", detail); }
                }
            }
        }

        void Update()
        {
            if (!driving) return;
            float ms = Time.unscaledDeltaTime * 1000f;
            if (Time.realtimeSinceStartup < perfSettleUntilRealtime)
            {
                var w = report.teleport_windows;
                w.frames++; w.max_ms = Mathf.Max(w.max_ms, ms);
                if (ms > FrameStats.HitchThresholdMs) w.hitches_over_50ms++;
            }
            else
            {
                frames.Add(ms);
                SampleRenderStats();
            }
            if (gcRecorder.Valid) { gcBytes += gcRecorder.LastValue; gcFrames++; }
        }

        /// <summary>BUG-090: one sample per driving frame. FrameTimingManager reports a frame a few frames late; that is fine for distributions.</summary>
        void SampleRenderStats()
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, frameTiming) == 1)
            {
                var t = frameTiming[0];
                gpuMs.Add((float)t.gpuFrameTime);
                cpuMainMs.Add((float)(t.cpuMainThreadFrameTime - t.cpuMainThreadPresentWaitTime));
                cpuRenderMs.Add((float)t.cpuRenderThreadFrameTime);
            }
            if (setPassRecorder.Valid) setPassCalls.Add(setPassRecorder.LastValue);
            if (drawCallsRecorder.Valid) drawCalls.Add(drawCallsRecorder.LastValue);
            if (batchesRecorder.Valid) batchCounts.Add(batchesRecorder.LastValue);
            if (trianglesRecorder.Valid) triangleCounts.Add(trianglesRecorder.LastValue);
        }

        static AutoTestReport.Distribution ToDistribution(FrameStats f)
        {
            var s = f.Summarize();
            var d = new AutoTestReport.Distribution { samples = s.frames };
            if (s.frames == 0) return d;
            d.avg = s.avgMs; d.p95 = s.p95Ms; d.p99 = s.p99Ms; d.max = s.maxMs;
            return d;
        }

        static void DisposeRecorder(ref ProfilerRecorder r)
        {
            if (r.Valid) r.Dispose();
        }

        void AddEvent(string type, string detail)
        {
            var p = car.transform.position;
            report.events.Add(new AutoTestReport.EventRecord
            {
                type = type, segment = cur != null ? cur.index : -1, road = cur?.road, s = autopilot != null ? autopilot.U : 0f, t = Time.time,
                x = p.x, y = p.y, z = p.z, detail = detail,
            });
            if (report.events.Count <= 200) Debug.Log($"[AutoTest] EVENT {type} seg {cur?.index} {cur?.road} u={autopilot?.U:0} at {p}: {detail}");
        }

        IEnumerator Abort(string reason)
        {
            Debug.LogError("[AutoTest] ABORT: " + reason);
            yield return Finish("aborted: " + reason, 2);
        }

        int loadsAtStart, unloadsAtStart, hitchesAtStart;

        void StreamingSnapshot(bool start)
        {
            const float MB = 1024f * 1024f;
            var st = report.streaming;
            st.enabled = CellStreamer.Active;
            float gfx = (gfxRecorder.Valid && gfxRecorder.LastValue > 0 ? gfxRecorder.LastValue : Profiler.GetAllocatedMemoryForGraphicsDriver()) / MB;
            float res = Profiler.GetTotalReservedMemoryLong() / MB;
            if (start) { st.gfx_mb_start = gfx; st.reserved_mb_start = res; loadsAtStart = CellStreamer.LoadOps; unloadsAtStart = CellStreamer.UnloadOps; hitchesAtStart = CellStreamer.StreamingHitches; }
            else
            {
                st.gfx_mb_end = gfx; st.reserved_mb_end = res;
                st.loads = CellStreamer.LoadOps - loadsAtStart;
                st.unloads = CellStreamer.UnloadOps - unloadsAtStart;
                st.cells_loaded_end = CellStreamer.LoadedCount;
                st.max_callback_ms = CellStreamer.MaxActivationMs;
                st.streaming_hitches_over_50ms = CellStreamer.StreamingHitches - hitchesAtStart;
            }
        }

        IEnumerator Finish(string endReason, int exitCode)
        {
            driving = false;
            StreamingSnapshot(false);
            Debug.Log("[AutoTest] " + FrameCost.Report());   // A2: worst frames with their top system (hitch attribution)
            report.end_reason = endReason;
            report.duration_s = routeStartRealtime > 0f ? Time.realtimeSinceStartup - routeStartRealtime : 0f;
            var s = frames.Summarize();
            report.frames = s.frames;
            report.avg_fps = s.avgFps;
            report.fps_1pct_low = s.onePercentLowFps;
            report.frametime_ms.p50 = s.p50Ms; report.frametime_ms.p95 = s.p95Ms; report.frametime_ms.p99 = s.p99Ms; report.frametime_ms.max = s.maxMs;
            report.hitches_over_50ms = s.hitchesOver50Ms;
            report.gc_alloc_kb_per_frame_avg = gcFrames > 0 ? (float)(gcBytes / gcFrames / 1024.0) : -1f;
            const float MB = 1024f * 1024f;
            report.memory_mb.managed = Profiler.GetMonoUsedSizeLong() / MB;
            report.memory_mb.total_reserved = Profiler.GetTotalReservedMemoryLong() / MB;
            long gfx = gfxRecorder.Valid && gfxRecorder.LastValue > 0 ? gfxRecorder.LastValue : Profiler.GetAllocatedMemoryForGraphicsDriver();
            report.memory_mb.gfx = gfx > 0 ? gfx / MB : -1f;
            if (gcRecorder.Valid) gcRecorder.Dispose();
            if (gfxRecorder.Valid) gfxRecorder.Dispose();
            report.render.gpu_ms = ToDistribution(gpuMs);
            report.render.cpu_main_ms = ToDistribution(cpuMainMs);
            report.render.cpu_render_ms = ToDistribution(cpuRenderMs);
            report.render.setpass_calls = ToDistribution(setPassCalls);
            report.render.draw_calls = ToDistribution(drawCalls);
            report.render.batches = ToDistribution(batchCounts);
            report.render.triangles = ToDistribution(triangleCounts);
            DisposeRecorder(ref setPassRecorder);
            DisposeRecorder(ref drawCallsRecorder);
            DisposeRecorder(ref batchesRecorder);
            DisposeRecorder(ref trianglesRecorder);

            var bi = BuildInfo.LoadOrDefault();
            report.build.type = Debug.isDebugBuild ? "development" : "release";
#if ENABLE_IL2CPP
            report.build.backend = "IL2CPP";
#else
            report.build.backend = "Mono";
#endif
            report.build.version = Application.version;
            report.build.git = bi.gitCommit;
            report.build.unity = Application.unityVersion;
            report.gpu = SystemInfo.graphicsDeviceName;
            report.graphics_api = SystemInfo.graphicsDeviceType.ToString();
            report.resolution = $"{Screen.width}x{Screen.height}";
            report.quality = QualitySettings.names[QualitySettings.GetQualityLevel()];
            report.vsync_count = QualitySettings.vSyncCount;

            var segs = report.per_segment.FindAll(p => p.reached);
            report.vehicle.spawned_on_road = segs.Count > 0 && segs.TrueForAll(p => p.spawned_on_road);
            report.vehicle.spawn_height_above_surface_m = segs.Count > 0 ? segs[0].spawn_height_above_surface_m : NoValue;

            yield return null; // let late log messages arrive
            lock (logLock)
            {
                report.exceptions = logExceptions;
                report.errors = logErrors;
                report.warnings = logWarnings;
                report.shader_is_null = logShaderNull;
                report.first_errors = new List<string>(firstErrors);
            }

            string path = string.IsNullOrEmpty(opts.StatsPath) ? Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "autotest_stats.json") : opts.StatsPath;
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
            }
            catch (Exception e) { Debug.LogWarning($"[AutoTest] could not write {path}: {e.Message}"); }

            Debug.Log($"[AutoTest] RESULT route={report.route} end={endReason} segments={report.vehicle.segments_completed}/{report.vehicle.segments_total} " +
                      $"duration={report.duration_s:0.0}s frames={report.frames} avg={report.avg_fps:0.0}fps 1%low={report.fps_1pct_low:0.0} p99={report.frametime_ms.p99:0.00}ms max={report.frametime_ms.max:0.0}ms hitches={report.hitches_over_50ms} " +
                      $"gpuP95={report.render.gpu_ms.p95:0.00}ms cpuMainP95={report.render.cpu_main_ms.p95:0.00}ms setPassP95={report.render.setpass_calls.p95:0} trisP95={report.render.triangles.p95:0} " +
                      $"exceptions={report.exceptions} errors={report.errors} shaderNull={report.shader_is_null} spawnedOnRoad={report.vehicle.spawned_on_road} falls={report.vehicle.falls_below_surface} " +
                      $"airborne={report.vehicle.airborne_events} invisibleWalls={report.vehicle.invisible_wall_events} maxVertG={report.vehicle.max_vertical_accel_g:0.00} distance={report.vehicle.distance_m:0}m stats={path}");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(exitCode);
        }
    }
}
