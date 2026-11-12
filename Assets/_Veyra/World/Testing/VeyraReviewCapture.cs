using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using VeyraCity.Gameplay;
using VeyraCity.World;
using VeyraCity.World.Data;
using VeyraCity.World.Streaming;

namespace VeyraCity.Testing
{
    /// <summary>
    /// Card S5 runtime review capture (Development player or Play mode):
    /// <code>VeyraCity.exe -reviewCapture [-reviewDir &lt;folder&gt;] [-reviewCard &lt;label&gt;] [-reviewFilter CAM_06,CAM_13]</code>
    /// Enters free roam (no traffic), then for each camera of Resources/Testing/VeyraAutoTestConfig → ReviewCameras: places the
    /// player's car for CHASE shots (parked, kinematic) or hides it for FIXED shots, sets the camera's hour and wet roads,
    /// waits for lighting to settle, renders 2560 x 1440 to &lt;camera&gt;.png, writes contact_sheet.png and review.txt, quits.
    /// With -autotest, VeyraAutoTest runs the route first and then calls <see cref="CaptureAll"/>.
    /// </summary>
    public sealed class VeyraReviewCapture : MonoBehaviour
    {
        public static bool Running { get; private set; }
        public static string LastReport { get; private set; }

        static string Arg(string[] a, string name)
        {
            int i = Array.FindIndex(a, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < a.Length && !a[i + 1].StartsWith("-") ? a[i + 1] : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var a = Environment.GetCommandLineArgs();
            bool capture = Array.Exists(a, x => x.Equals("-reviewCapture", StringComparison.OrdinalIgnoreCase));
            bool autotest = Array.Exists(a, x => x.Equals("-autotest", StringComparison.OrdinalIgnoreCase));
            if (!capture || autotest) return; // with -autotest the test runner calls CaptureAll after its route
            Application.runInBackground = true;
            // Card L1: "-quality PC_Medium" captures the cameras on another quality tier.
            string q = Arg(a, "-quality");
            if (!string.IsNullOrEmpty(q))
            {
                int qi = Array.FindIndex(QualitySettings.names, n => n.Equals(q, StringComparison.OrdinalIgnoreCase));
                if (qi >= 0) QualitySettings.SetQualityLevel(qi, true);
                else Debug.LogWarning($"[ReviewCapture] unknown quality level '{q}'");
            }
            var c = Begin(Arg(a, "-reviewDir"), Arg(a, "-reviewCard") ?? "review", Arg(a, "-reviewFilter"), quitWhenDone: true);
            c.times = ParseTimes(Arg(a, "-reviewTimes"));
        }

        /// <summary>
        /// Card F3: "-reviewTimes 12,19,21,23,23w" captures every (filtered) camera at each listed hour instead of its own;
        /// a trailing "w" makes that pass wet and rainy. Null when the option is absent or empty.
        /// </summary>
        public static List<(float hour, bool wet)> ParseTimes(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return null;
            var list = new List<(float, bool)>();
            foreach (var raw in spec.Split(','))
            {
                string t = raw.Trim();
                bool wet = t.EndsWith("w", StringComparison.OrdinalIgnoreCase);
                if (wet) t = t.Substring(0, t.Length - 1);
                if (float.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float h))
                    list.Add((Mathf.Repeat(h, 24f), wet));
            }
            return list.Count > 0 ? list : null;
        }

        /// <summary>Starts a capture run on a new GameObject (player flag or the Editor window in Play mode).</summary>
        public static VeyraReviewCapture Begin(string outDir, string card, string filter, bool quitWhenDone)
        {
            var go = new GameObject("[VeyraReviewCapture]");
            DontDestroyOnLoad(go);
            var c = go.AddComponent<VeyraReviewCapture>();
            c.outDir = outDir; c.card = card; c.filter = filter; c.quit = quitWhenDone;
            return c;
        }

        string outDir, card, filter;
        bool quit;
        List<(float hour, bool wet)> times;

        IEnumerator Start()
        {
            float wait = Time.realtimeSinceStartup;
            GameFlow gf;
            while ((gf = GameFlow.Instance) == null || gf.Current == GameFlow.State.Boot)
            {
                if (Time.realtimeSinceStartup - wait > 60f) { Debug.LogError("[ReviewCapture] GameFlow did not start within 60 s"); Done(2); yield break; }
                yield return null;
            }
            var cfg = Resources.Load<VeyraAutoTestConfig>(VeyraAutoTestConfig.ResourcePath);
            if (cfg == null || cfg.ReviewCameras == null || cfg.RoadNetworkJson == null) { Debug.LogError("[ReviewCapture] Resources/Testing/VeyraAutoTestConfig has no review cameras or road JSON (Veyra/Review/Create Review Cameras)"); Done(2); yield break; }
            var net = RoadNetwork.Parse(cfg.RoadNetworkJson.text);
            if (gf.Current != GameFlow.State.FreeRoam) gf.BeginAutomatedFreeRoam(12f);
            yield return null;
            yield return null;
            string dir = string.IsNullOrEmpty(outDir) ? ReviewCameraRig.OutputDir(Path.GetDirectoryName(Application.dataPath) ?? ".", card, "player") : outDir;
            yield return CaptureAll(gf, WithExtras(cfg.ReviewCameras), net, dir, filter, card + " (player)", times);
            Done(0);
        }

        /// <summary>
        /// World iteration 26: "-reviewExtra NAME:px,py,pz:lx,ly,lz[;...]" adds fixed cameras for this run only (Y absolute;
        /// a runtime copy of the set, the asset is never changed). Used to review new landmarks no listed camera sees.
        /// </summary>
        static ReviewCameraSet WithExtras(ReviewCameraSet set)
        {
            string spec = Arg(Environment.GetCommandLineArgs(), "-reviewExtra");
            if (string.IsNullOrWhiteSpace(spec)) return set;
            var copy = ScriptableObject.CreateInstance<ReviewCameraSet>();
            copy.Cameras = new List<ReviewCameraSet.Entry>(set.Cameras);
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            Vector3 V(string s) { var f = s.Split(','); return new Vector3(float.Parse(f[0], ci), float.Parse(f[1], ci), float.Parse(f[2], ci)); }
            foreach (var item in spec.Split(';'))
            {
                var p = item.Split(':');
                if (p.Length < 3) continue;
                try { copy.Cameras.Add(new ReviewCameraSet.Entry { name = p[0], kind = ReviewCameraSet.Kind.Fixed, district = "World", position = V(p[1]), lookAt = V(p[2]), fov = 55f, hour = 12f }); }
                catch (FormatException) { Debug.LogWarning($"[ReviewCapture] bad -reviewExtra entry '{item}'"); }
            }
            return copy;
        }

        void Done(int code)
        {
            if (quit) Application.Quit(code);
            Destroy(gameObject);
        }

        /// <summary>Captures every (filtered) camera into <paramref name="dir"/>; the report is in <see cref="LastReport"/>.</summary>
        public static IEnumerator CaptureAll(GameFlow gf, ReviewCameraSet set, RoadNetworkData net, string dir, string filter, string title,
                                             IList<(float hour, bool wet)> times = null)
        {
            Running = true;
            var log = new StringBuilder();
            log.AppendLine($"Review capture — {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {title} — {Application.version} {(Debug.isDebugBuild ? "development" : "release")} — {Screen.width}x{Screen.height}");
            var shots = new List<(string label, Texture2D image)>();
            var cam = ReviewCameraRig.CreateCamera();
            var car = gf != null ? gf.PlayerCar : null;
            var env = gf != null ? gf.WorldEnv : null;
            bool wasKinematic = car != null && car.Body.isKinematic;
            float oldTime = env != null ? env.TimeOfDay : 12f;
            Directory.CreateDirectory(dir);
            // Each job: a camera at its own hour/wetness, or (card F3, -reviewTimes) at every listed hour.
            var jobs = new List<(ReviewCameraSet.Entry e, float hour, bool wet, string label)>();
            if (times == null) foreach (var e in set.Filter(filter)) jobs.Add((e, e.hour, e.wet, e.name));
            else foreach (var t in times) foreach (var e in set.Filter(filter))
                    jobs.Add((e, t.hour, t.wet, $"{e.name}_{t.hour:00.##}h{(t.wet ? "_wet" : "")}"));
            foreach (var (e, hour, wet, label) in jobs)
            {
                // Roadmap A2: load the cells around the shot first (the pose snaps to colliders); the streamed world is what
                // the player sees, HLOD proxies beyond the loaded window included.
                if (CellStreamer.Active)
                {
                    Vector3 at = e.position;
                    if (e.kind == ReviewCameraSet.Kind.Chase)
                    {
                        var road = net?.Road(e.roadId);
                        if (road != null) at = RouteAutopilot.BuildLanePath(road, e.distanceAlongRoad_m, e.distanceAlongRoad_m + 20f, 20f, out _).PositionAt(0f);
                    }
                    yield return CellStreamer.WaitUntilReady(at, 900f);
                    if (car != null) CellStreamer.Track(null);   // the review parks the car itself; no ground guard
                }
                var pose = ReviewCameraRig.Pose(e, net);
                if (!pose.ok) { log.AppendLine($"FAIL  {label}: {pose.error}"); shots.Add((label + " (FAILED)", null)); continue; }
                if (car != null)
                {
                    car.Body.isKinematic = false;
                    if (pose.hasCar) car.ResetTo(pose.carPos + Vector3.up * 0.02f, pose.carRot);
                    else car.ResetTo(new Vector3(0f, -400f, 0f), Quaternion.identity); // out of every fixed view
                    car.Body.isKinematic = true;
                }
                if (env != null)
                {
                    env.AnimateTime = false;
                    env.AutoWeather = false;
                    env.SetWeather(wet && times != null ? WorldEnvironment.WeatherKind.Rain : WorldEnvironment.WeatherKind.Clear);
                    env.SetTime(hour);
                    env.SetWetness(wet ? 1f : 0f);
                    env.SetRainIntensity(wet && times != null ? 1f : 0f);
                }
                ReviewCameraRig.Apply(cam, pose);
                // D1: the light pool, lamp sprites, haze and fog altitude follow the camera that renders the shot.
                if (env != null) { env.ViewCamera = cam; env.ReapplyCurrentState(); }
                yield return new WaitForSecondsRealtime(0.8f); // lighting, LOD and streaming settle
                if (env != null && wet) env.SetWetness(1f);
                // The review camera renders on demand only, so its motion history still holds the previous shot's pose and the
                // jump reads as camera motion (S5: every player chase shot came out motion-blurred). Render it at this pose on
                // two consecutive frames first so the history matches.
                for (int warm = 0; warm < 2; warm++) { ReviewCameraRig.WarmUp(cam); yield return null; }
                var tex = ReviewCameraRig.Render(cam);
                string file = Path.Combine(dir, label + ".png");
                ReviewCameraRig.SavePng(tex, file);
                shots.Add((label, tex));
                log.AppendLine($"OK    {label}: {(e.kind == ReviewCameraSet.Kind.Chase ? "CHASE" : "FIXED")} cam {pose.camPos.ToString("F1")} fov {pose.fov} {hour:00.00} h{(wet ? " wet" : "")} | night factor {VeyraGlobals.NightFactor:0.00}, wetness {VeyraGlobals.Wetness:0.00}, rain {VeyraGlobals.RainIntensity:0.00} | {pose.note} -> {file}");
            }
            var sheet = ReviewCameraRig.ContactSheet(shots, title);
            string sheetPath = Path.Combine(dir, "contact_sheet.png");
            ReviewCameraRig.SavePng(sheet, sheetPath);
            log.AppendLine($"contact sheet: {sheetPath} ({shots.Count} cameras)");
            log.AppendLine(MaterialAuditor.Summary(MaterialAuditor.Run())); // card F1: pink/missing materials in the loaded world
            foreach (var s in shots) if (s.image != null) Destroy(s.image);
            Destroy(sheet);
            Destroy(cam.gameObject);
            if (car != null) car.Body.isKinematic = wasKinematic;
            if (CellStreamer.Active) { CellStreamer.Focus(null); if (car != null) CellStreamer.Track(car.Body); }
            if (env != null) env.ViewCamera = null;
            if (env != null) { env.SetTime(oldTime); env.SetWetness(0f); env.SetRainIntensity(0f); env.SetWeather(WorldEnvironment.WeatherKind.Clear); }
            LastReport = log.ToString();
            File.WriteAllText(Path.Combine(dir, "review.txt"), LastReport, new UTF8Encoding(false));
            Debug.Log("[ReviewCapture] " + LastReport);
            Running = false;
        }
    }
}
