using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VeyraCity.Testing;
using VeyraCity.World;
using VeyraCity.World.Data;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card S5 capture in Edit mode, one camera per editor update (objects created in an update are not drawn by Camera.Render
    /// until a later one): place → wait → render 2560 x 1440 → next camera; then a labelled 6 x 3 contact sheet. Afterwards the
    /// scene's time of day and wetness are restored and the temporary camera and car are destroyed; if the scene had no unsaved
    /// changes before, it is reloaded so the capture leaves nothing behind. In Play mode it starts the runtime capture instead.
    /// Output: Screenshots/&lt;yyyy-MM-dd&gt;_&lt;card&gt;[_&lt;scene&gt;]/&lt;camera&gt;.png (+ contact_sheet.png, review.txt), Logs/&lt;card&gt;/review_&lt;scene&gt;.txt.
    /// </summary>
    public static class ReviewCaptureRunner
    {
        public const string CarPrefab = "Assets/_Veyra/Art/Vehicles/ArdenKite/ArdenKite_Stock.prefab";
        const int SettleUpdates = 3;
        static readonly Vector3 ParkedCar = new Vector3(0f, -500f, 0f);

        public static bool IsRunning => state != null || VeyraReviewCapture.Running;
        public static string LastReport { get; private set; } = "";
        public static string LastOutputDir { get; private set; } = "";

        sealed class State
        {
            public List<ReviewCameraSet.Entry> cams;
            public RoadNetworkData net;
            public string dir, card, sceneName, scenePath, title;
            public bool sceneWasDirty;
            public WorldEnvironment env;
            public float envTime, envWet;
            public Camera cam;
            public GameObject car;
            public int index, wait;
            public bool placed;
            public ReviewPose pose;
            public readonly List<(string label, Texture2D image)> shots = new List<(string, Texture2D)>();
            public readonly StringBuilder log = new StringBuilder();
        }

        static State state;

        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        /// <summary>Starts a capture of the active scene. Returns a one-line status.</summary>
        public static string Start(string card, string filter = null)
        {
            if (IsRunning) return "a capture is already running";
            var set = AssetDatabase.LoadAssetAtPath<ReviewCameraSet>(ReviewCameraTools.AssetPath);
            if (set == null) { ReviewCameraTools.CreateAll(); set = AssetDatabase.LoadAssetAtPath<ReviewCameraSet>(ReviewCameraTools.AssetPath); }
            var scene = EditorSceneManager.GetActiveScene();
            string suffix = scene.name == "VeyraCity" ? null : scene.name;
            if (EditorApplication.isPlaying)
            {
                string pdir = ReviewCameraRig.OutputDir(ProjectRoot, card, (suffix ?? "VeyraCity") + "_play");
                VeyraReviewCapture.Begin(pdir, card, filter, quitWhenDone: false);
                LastOutputDir = pdir;
                return $"Play mode: runtime capture started -> {pdir}";
            }
            var s = new State
            {
                cams = set.Filter(filter),
                net = RoadNetwork.Parse(VeyraDataValidator.LoadText("road_network_geometry.json")),
                card = card,
                sceneName = scene.name,
                scenePath = scene.path,
                sceneWasDirty = scene.isDirty,
                dir = ReviewCameraRig.OutputDir(ProjectRoot, card, suffix),
                title = $"{card} {scene.name} (edit mode)",
                env = UnityEngine.Object.FindAnyObjectByType<WorldEnvironment>(),
            };
            if (s.cams.Count == 0) return $"no camera matches '{filter}'";
            if (s.env != null) { s.envTime = s.env.TimeOfDay; s.envWet = s.env.Wetness; }
            s.cam = ReviewCameraRig.CreateCamera();
            s.cam.gameObject.hideFlags = HideFlags.HideAndDontSave;
            if (s.cams.Any(c => c.kind == ReviewCameraSet.Kind.Chase))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CarPrefab);
                if (prefab != null) s.car = UnityEngine.Object.Instantiate(prefab);
                else
                {
                    s.car = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    s.car.transform.localScale = new Vector3(1.8f, 1.3f, 4.3f); // 4.3 m car proxy
                }
                s.car.name = "[ReviewCar]";
                s.car.hideFlags = HideFlags.HideAndDontSave;
                foreach (var col in s.car.GetComponentsInChildren<Collider>(true)) col.enabled = false; // never hit by the placement raycasts
                foreach (var rb in s.car.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
                // Keep it active and park it out of every view: a renderer switched on just before a render is not drawn in
                // Edit mode until the scene updates (S5: CAM_06 lost its car that way).
                s.car.transform.position = ParkedCar;
            }
            Directory.CreateDirectory(s.dir);
            s.log.AppendLine($"Review capture — {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {s.title} — scene {s.scenePath} — Unity {Application.unityVersion}");
            s.log.AppendLine($"lighting: {(s.env != null ? "WorldEnvironment.SetTime/SetWetness per camera" : "no WorldEnvironment in the scene (time of day not applied)")}; car: {(s.car != null ? (AssetDatabase.LoadAssetAtPath<GameObject>(CarPrefab) != null ? "Arden Kite prefab" : "4.3 m proxy box") : "none")}");
            state = s;
            EditorApplication.update += Tick;
            return $"started: {s.cams.Count} cameras -> {s.dir}";
        }

        static void Tick()
        {
            var s = state;
            if (s == null) { EditorApplication.update -= Tick; return; }
            try
            {
                if (s.index >= s.cams.Count) { Finish(); return; }
                var e = s.cams[s.index];
                if (!s.placed)
                {
                    s.pose = ReviewCameraRig.Pose(e, s.net);
                    if (!s.pose.ok)
                    {
                        s.log.AppendLine($"FAIL  {e.name}: {s.pose.error}");
                        s.shots.Add((e.name + " (FAILED)", null));
                        s.index++;
                        return;
                    }
                    if (s.car != null)
                    {
                        if (s.pose.hasCar) s.car.transform.SetPositionAndRotation(s.pose.carPos, s.pose.carRot);
                        else s.car.transform.SetPositionAndRotation(ParkedCar, Quaternion.identity);
                    }
                    if (s.env != null) { s.env.SetTime(e.hour); s.env.SetWetness(e.wet ? 1f : 0f); }
                    ReviewCameraRig.Apply(s.cam, s.pose);
                    EditorApplication.QueuePlayerLoopUpdate(); // let the scene update (transforms, lighting) before the render
                    SceneView.RepaintAll();
                    s.placed = true;
                    s.wait = SettleUpdates;
                    return;
                }
                if (--s.wait > 0) return;
                var tex = ReviewCameraRig.Render(s.cam);
                string file = Path.Combine(s.dir, e.name + ".png");
                ReviewCameraRig.SavePng(tex, file);
                s.shots.Add((e.name, tex));
                s.log.AppendLine($"OK    {e.name}: {(e.kind == ReviewCameraSet.Kind.Chase ? "CHASE" : "FIXED")} cam {s.pose.camPos.ToString("F1")} fov {s.pose.fov} {e.time} {e.hour:00.00} h{(e.wet ? " wet" : "")} | {s.pose.note} -> {Path.GetFileName(file)}");
                s.placed = false;
                s.index++;
            }
            catch (Exception ex)
            {
                s.log.AppendLine("ERROR " + ex);
                Debug.LogException(ex);
                Finish();
            }
        }

        static void Finish()
        {
            var s = state;
            EditorApplication.update -= Tick;
            state = null;
            if (s == null) return;
            try
            {
                var sheet = ReviewCameraRig.ContactSheet(s.shots, s.title);
                string sheetPath = Path.Combine(s.dir, "contact_sheet.png");
                ReviewCameraRig.SavePng(sheet, sheetPath);
                UnityEngine.Object.DestroyImmediate(sheet);
                s.log.AppendLine($"contact sheet: {sheetPath} ({s.shots.Count} cameras, {s.shots.Count(x => x.image != null)} rendered)");
            }
            finally
            {
                foreach (var shot in s.shots) if (shot.image != null) UnityEngine.Object.DestroyImmediate(shot.image);
                if (s.env != null) { s.env.SetTime(s.envTime); s.env.SetWetness(s.envWet); }
                if (s.cam != null) UnityEngine.Object.DestroyImmediate(s.cam.gameObject);
                if (s.car != null) UnityEngine.Object.DestroyImmediate(s.car);
                var scene = EditorSceneManager.GetActiveScene();
                if (!s.sceneWasDirty && scene.isDirty && !string.IsNullOrEmpty(s.scenePath) && scene.path == s.scenePath)
                {
                    EditorSceneManager.OpenScene(s.scenePath, OpenSceneMode.Single); // discard what the capture touched
                    s.log.AppendLine("scene reloaded from disk (the capture had dirtied it; there were no unsaved changes before)");
                }
                LastReport = s.log.ToString();
                LastOutputDir = s.dir;
                File.WriteAllText(Path.Combine(s.dir, "review.txt"), LastReport, new UTF8Encoding(false));
                string logDir = Path.Combine(ProjectRoot, "Logs", s.card);
                Directory.CreateDirectory(logDir);
                File.WriteAllText(Path.Combine(logDir, $"review_{s.sceneName}.txt"), LastReport, new UTF8Encoding(false));
                Debug.Log("[ReviewCapture] " + LastReport);
            }
        }
    }
}
