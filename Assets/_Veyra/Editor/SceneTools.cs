using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VeyraCity.EditorTools
{
    /// <summary>Creates the Phase 1 foundation test scene and registers it in the build settings.</summary>
    public static class SceneTools
    {
        public const string FoundationScenePath = "Assets/_Veyra/Scenes/Foundation.unity";

        [MenuItem("Veyra/Create Foundation Scene")]
        public static void EnsureFoundationSceneMenu() => EnsureFoundationScene(force: false);

        /// <summary>Batch-mode entry point: -executeMethod VeyraCity.EditorTools.SceneTools.EnsureFoundationSceneBatch</summary>
        public static void EnsureFoundationSceneBatch()
        {
            bool ok = EnsureFoundationScene(force: false);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        public static bool EnsureFoundationScene(bool force)
        {
            if (!force && File.Exists(FoundationScenePath))
            {
                RegisterInBuildSettings();
                Debug.Log($"[Veyra] Foundation scene already exists: {FoundationScenePath}");
                return true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(FoundationScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.SetPositionAndRotation(new Vector3(0f, 2.1f, -7.5f), Quaternion.Euler(8f, 0f, 0f));
                cam.fieldOfView = 62f; // chase-camera standard from the art direction guide (60-64 deg)
                cam.farClipPlane = 2000f;
            }

            var sun = Object.FindAnyObjectByType<Light>();
            if (sun != null) sun.transform.rotation = Quaternion.Euler(30f, 245f, 0f); // "hero day" sun from the art guide

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground_200m";
            ground.transform.localScale = new Vector3(20f, 1f, 20f);

            // Scale references: a 4.3 x 1.78 x 1.3 m car-sized box and 1.75 m person-sized capsules.
            var car = GameObject.CreatePrimitive(PrimitiveType.Cube);
            car.name = "ScaleRef_Car_4.3m";
            car.transform.position = new Vector3(0f, 0.65f, 0f);
            car.transform.localScale = new Vector3(1.78f, 1.3f, 4.3f);
            for (int i = 0; i < 6; i++)
            {
                var p = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                p.name = $"ScaleRef_Person_1.75m_{i}";
                p.transform.position = new Vector3(-6f + i * 2.4f, 0.875f, 10f);
                p.transform.localScale = new Vector3(0.5f, 0.875f, 0.5f);
            }

            bool saved = EditorSceneManager.SaveScene(scene, FoundationScenePath);
            AssetDatabase.Refresh();
            if (saved) RegisterInBuildSettings();
            Debug.Log(saved ? $"[Veyra] Created {FoundationScenePath}" : $"[Veyra] FAILED to save {FoundationScenePath}");
            return saved;
        }

        /// <summary>Adds the Foundation scene to the build list if missing. Never removes other scenes (BUG-015).</summary>
        static void RegisterInBuildSettings()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == FoundationScenePath)) return;
            scenes.Add(new EditorBuildSettingsScene(FoundationScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
        }
    }
}
