using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Roadmap D1 (lighting): builds everything the lighting track keeps in the city scene, all under the scene root
    /// <c>VC_Lighting_D1</c> (nothing else in the scene is touched):
    /// 1. the realtime box reflection probes and their director (<see cref="VeyraReflectionProbes"/>),
    /// 2. BUG-074: the main camera's AA request set to None (STP on the PC URP tiers already does temporal AA).
    /// Idempotent: the probe holder is rebuilt, the camera setting is a plain assignment.
    /// Batch: <c>Unity -batchmode -quit -projectPath E:\VeyraCity -executeMethod VeyraCity.EditorTools.VeyraLightingD1.BuildBatch</c>
    /// (opens the authoring scene, builds, saves it, writes Logs/D1/lighting_build.txt).
    /// </summary>
    public static class VeyraLightingD1
    {
        public const string RootName = "VC_Lighting_D1";
        const string ScenePath = "Assets/_Veyra/Scenes/VeyraCity.unity";

        [MenuItem("Veyra/Lighting/Build D1 Lighting (probes, camera AA)")]
        static void Menu() => Debug.Log(Build());

        public static string Build()
        {
            var log = new StringBuilder("D1 lighting build\n");
            log.Append(VeyraReflectionProbes.Build());
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var cam in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)))
            {
                if (!cam.CompareTag("MainCamera")) continue;
                var data = cam.GetComponent<UniversalAdditionalCameraData>();
                if (data == null || data.antialiasing == AntialiasingMode.None) { log.AppendLine($"camera {cam.name}: AA None (unchanged)"); continue; }
                data.antialiasing = AntialiasingMode.None;
                EditorUtility.SetDirty(data);
                EditorSceneManager.MarkSceneDirty(scene);
                log.AppendLine($"camera {cam.name}: AA SMAA -> None (BUG-074)");
            }
            log.Append(SyncEnvironmentDefaults(scene));
            return log.ToString();
        }

        /// <summary>The D1 tunables of WorldEnvironment are tuned in code; a scene save serializes whatever value they had then
        /// and from then on overrides the code (BUG-127). This copies the code defaults (read from a component added to an
        /// inactive temporary object, so OnEnable never runs) into the scene's WorldEnvironment.</summary>
        static readonly string[] D1Fields = { "PoolGain", "MoonLight", "FogLayerTop", "FarLampDistance", "FarTrafficDistance" };

        static string SyncEnvironmentDefaults(UnityEngine.SceneManagement.Scene scene)
        {
            var log = new StringBuilder();
            var tmp = new GameObject("D1_defaults") { hideFlags = HideFlags.HideAndDontSave };
            tmp.SetActive(false);
            try
            {
                var defaults = tmp.AddComponent<VeyraCity.World.WorldEnvironment>();
                foreach (var env in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<VeyraCity.World.WorldEnvironment>(true)))
                {
                    var so = new SerializedObject(env);
                    var src = new SerializedObject(defaults);
                    foreach (var f in D1Fields)
                    {
                        var p = so.FindProperty(f);
                        var d = src.FindProperty(f);
                        if (p == null || d == null || Mathf.Approximately(p.floatValue, d.floatValue)) continue;
                        log.AppendLine($"WorldEnvironment.{f}: {p.floatValue} -> {d.floatValue}");
                        p.floatValue = d.floatValue;
                    }
                    if (so.ApplyModifiedPropertiesWithoutUndo()) EditorSceneManager.MarkSceneDirty(scene);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(tmp); }
            return log.Length > 0 ? log.ToString() : "WorldEnvironment D1 tunables: code defaults (unchanged)\n";
        }

        public static void BuildBatch()
        {
            int code = 0;
            var log = new StringBuilder();
            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                log.Append(Build());
                if (!EditorSceneManager.SaveScene(scene)) { log.AppendLine("SAVE FAILED"); code = 1; }
                else log.AppendLine($"saved {ScenePath}");
            }
            catch (Exception e) { log.AppendLine("EXCEPTION " + e); code = 1; }
            string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Logs", "D1");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "lighting_build.txt"), log.ToString());
            Debug.Log(log.ToString());
            EditorApplication.Exit(code);
        }
    }
}
