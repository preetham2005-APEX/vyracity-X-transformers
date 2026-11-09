using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VeyraCity.Gameplay;
using VeyraCity.VehiclePhysics;
using EditorMenu = UnityEditor.MenuItem;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Imports cars processed by Tools/Blender/process_downloaded_car.py (ArtSource/Vehicles/&lt;id&gt;/) and
    /// registers them in the GameDatabase. ArtSource/Vehicles/car_map.json lists, per id: display name,
    /// maker line, base handling definition, price, unlock level, role (player/traffic) and licence credit.
    /// The vehicle definition is derived from the base one with the model's measured wheelbase, track,
    /// wheel radius and body size, so the physics matches the visual.
    /// </summary>
    public static class CarImportTools
    {
        [Serializable] public sealed class MapEntry { public string id, name, maker, description, baseDef, role, credit, color; public int price, level; public float torqueScale = 1f, mass; }
        [Serializable] sealed class MapFile { public MapEntry[] cars; }
        [Serializable] sealed class Hub { public float[] centre; public float radius, width; }
        [Serializable] sealed class Report { public string id, fbx; public bool wheels_ok; public float[] dimensions; public float wheelbase, track_front, track_rear, radius_front, radius_rear; }

        const string ArtRoot = "E:/VeyraCity/ArtSource/Vehicles";
        const string AssetRoot = "Assets/_Veyra/Art/Vehicles/Downloaded";

        [EditorMenu("Veyra/Import Downloaded Cars")]
        public static void ImportMenu() => ImportAll();

        public static void ImportBatch()
        {
            try { ImportAll(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void ImportAll()
        {
            var mapPath = Path.Combine(ArtRoot, "car_map.json");
            if (!File.Exists(mapPath)) { Debug.LogWarning("[CarImport] no car_map.json"); return; }
            var map = JsonUtility.FromJson<MapFile>(File.ReadAllText(mapPath));
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/_Veyra/Data/GameDatabase.asset");
            var cars = db.Cars.ToList();
            var traffic = (db.TrafficModels ?? Array.Empty<GameObject>()).ToList();
            foreach (var m in map.cars)
            {
                string dir = Path.Combine(ArtRoot, m.id);
                string reportPath = Path.Combine(dir, "process_report.json");
                if (!File.Exists(reportPath)) { Debug.LogWarning($"[CarImport] {m.id}: not processed yet"); continue; }
                var rep = JsonUtility.FromJson<Report>(File.ReadAllText(reportPath));
                string dstDir = $"{AssetRoot}/{m.id}";
                Directory.CreateDirectory(dstDir);
                string fbxAsset = $"{dstDir}/{m.id}.fbx";
                File.Copy(rep.fbx, fbxAsset, true);
                AssetDatabase.ImportAsset(fbxAsset, ImportAssetOptions.ForceSynchronousImport);
                var imp = (ModelImporter)AssetImporter.GetAtPath(fbxAsset);
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
                imp.importCameras = false;
                imp.importLights = false;
                imp.animationType = ModelImporterAnimationType.None;
                imp.importAnimation = false;
                imp.isReadable = false;
                imp.meshCompression = ModelImporterMeshCompression.Off;
                imp.SaveAndReimport();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAsset);
                string prefabPath = $"{dstDir}/{m.id}.prefab";
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                SetupLods(inst);
                var prefab = PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
                UnityEngine.Object.DestroyImmediate(inst);
                if (m.role == "traffic")
                {
                    if (!traffic.Contains(prefab)) traffic.Add(prefab);
                    continue;
                }
                var baseDef = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(m.baseDef);
                if (baseDef == null) { Debug.LogWarning($"[CarImport] {m.id}: base def {m.baseDef} missing"); continue; }
                var defPath = $"Assets/_Veyra/Data/Vehicles/Catalog/DL_{m.id}.asset";
                var def = UnityEngine.Object.Instantiate(baseDef);
                def.DisplayName = m.name;
                def.BuildId = "DL_" + m.id;
                if (rep.wheels_ok)
                {
                    def.Wheelbase = rep.wheelbase;
                    def.Front.TrackWidth = rep.track_front;
                    def.Rear.TrackWidth = rep.track_rear;
                    def.Front.Tyre.Radius = rep.radius_front;
                    def.Rear.Tyre.Radius = rep.radius_rear;
                }
                if (rep.dimensions != null && rep.dimensions.Length == 3) def.Chassis.Dimensions = new Vector3(rep.dimensions[0], rep.dimensions[1], rep.dimensions[2]);
                if (m.mass > 0f) def.Chassis.Mass = m.mass;
                if (Mathf.Abs(m.torqueScale - 1f) > 1e-3f)
                {
                    var c = (Vector2[])def.Engine.TorqueCurve.Clone();
                    for (int i = 0; i < c.Length; i++) c[i].y *= m.torqueScale;
                    def.Engine.TorqueCurve = c;
                }
                var existing = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(defPath);
                if (existing != null) { EditorUtility.CopySerialized(def, existing); UnityEngine.Object.DestroyImmediate(def); def = existing; }
                else AssetDatabase.CreateAsset(def, defPath);
                cars.RemoveAll(c => c.Id == m.id);
                ColorUtility.TryParseHtmlString(m.color ?? "#8899AA", out var paint);
                cars.Add(new CarEntry
                {
                    Id = m.id, Name = m.name, Maker = m.maker, Description = m.description, Price = m.price, UnlockLevel = m.level,
                    Definition = def, Model = prefab, DefaultPaint = paint, Credit = m.credit, AIOnly = m.role == "ai",
                });
                Debug.Log($"[CarImport] {m.id}: wheels {(rep.wheels_ok ? "ok" : "MISSING (static wheels)")}, wb {rep.wheelbase:0.00} m");
            }
            db.Cars = cars.ToArray();
            db.TrafficModels = traffic.ToArray();
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
        }

        static void SetupLods(GameObject root)
        {
            var lods = new List<LOD>();
            float[] h = { 0.25f, 0.08f, 0.01f };
            // wheels and other parts stay visible in every LOD
            var others = root.GetComponentsInChildren<Renderer>(true).Where(r => !r.name.StartsWith("Body_LOD") && !r.name.StartsWith("UCX_") && !r.name.StartsWith("UBX_")).ToList();
            for (int i = 0; i < 3; i++)
            {
                var body = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Body_LOD" + i);
                if (body == null) continue;
                var rs = body.GetComponentsInChildren<Renderer>(true).ToList();
                rs.AddRange(others);
                lods.Add(new LOD(h[i], rs.ToArray()));
            }
            if (lods.Count > 0)
            {
                var g = root.AddComponent<LODGroup>();
                g.SetLODs(lods.ToArray());
                g.RecalculateBounds();
            }
        }
    }
}
