using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using VeyraCity.Gameplay;
using VeyraCity.World;
using VeyraCity.World.Data;

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
            Begin(Arg(a, "-reviewDir"), Arg(a, "-reviewCard") ?? "review", Arg(a, "-reviewFilter"), quitWhenDone: true);
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
            yield return CaptureAll(gf, cfg.ReviewCameras, net, dir, filter, card + " (player)");
            Done(0);
        }

        void Done(int code)
        {
            if (quit) Application.Quit(code);
            Destroy(gameObject);
        }

        /// <summary>Captures every (filtered) camera into <paramref name="dir"/>; the report is in <see cref="LastReport"/>.</summary>
        public static IEnumerator CaptureAll(GameFlow gf, ReviewCameraSet set, RoadNetworkData net, string dir, string filter, string title)
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
            foreach (var e in set.Filter(filter))
            {
                var pose = ReviewCameraRig.Pose(e, net);
                if (!pose.ok) { log.AppendLine($"FAIL  {e.name}: {pose.error}"); shots.Add((e.name + " (FAILED)", null)); continue; }
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
                    env.SetWeather(WorldEnvironment.WeatherKind.Clear);
                    env.SetTime(e.hour);
                    env.SetWetness(e.wet ? 1f : 0f);
                }
                ReviewCameraRig.Apply(cam, pose);
                yield return new WaitForSecondsRealtime(0.8f); // lighting, LOD and streaming settle
                if (env != null && e.wet) env.SetWetness(1f);
                // The review camera renders on demand only, so its motion history still holds the previous shot's pose and the
                // jump reads as camera motion (S5: every player chase shot came out motion-blurred). Render it at this pose on
                // two consecutive frames first so the history matches.
                for (int warm = 0; warm < 2; warm++) { ReviewCameraRig.WarmUp(cam); yield return null; }
                var tex = ReviewCameraRig.Render(cam);
                string file = Path.Combine(dir, e.name + ".png");
                ReviewCameraRig.SavePng(tex, file);
                shots.Add((e.name, tex));
                log.AppendLine($"OK    {e.name}: {(e.kind == ReviewCameraSet.Kind.Chase ? "CHASE" : "FIXED")} cam {pose.camPos.ToString("F1")} fov {pose.fov} {e.time} {e.hour:00.00} h{(e.wet ? " wet" : "")} | {pose.note} -> {file}");
            }
            var sheet = ReviewCameraRig.ContactSheet(shots, title);
            string sheetPath = Path.Combine(dir, "contact_sheet.png");
            ReviewCameraRig.SavePng(sheet, sheetPath);
            log.AppendLine($"contact sheet: {sheetPath} ({shots.Count} cameras)");
            foreach (var s in shots) if (s.image != null) Destroy(s.image);
            Destroy(sheet);
            Destroy(cam.gameObject);
            if (car != null) car.Body.isKinematic = wasKinematic;
            if (env != null) { env.SetTime(oldTime); env.SetWetness(0f); }
            LastReport = log.ToString();
            File.WriteAllText(Path.Combine(dir, "review.txt"), LastReport, new UTF8Encoding(false));
            Debug.Log("[ReviewCapture] " + LastReport);
            Running = false;
        }
    }
}
