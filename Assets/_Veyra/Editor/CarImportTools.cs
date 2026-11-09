using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.Gameplay;
using VeyraCity.VehiclePhysics;
using EditorMenu = UnityEditor.MenuItem;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Cards A8/A9: imports the cars processed by Tools/Blender/process_downloaded_car.py (ArtSource/Vehicles/&lt;id&gt;/)
    /// and registers them in the GameDatabase.
    ///  * textures → Assets/_Veyra/Art/Vehicles/&lt;id&gt;/Textures (normal maps marked, metallic-smoothness linear)
    ///  * URP materials from materials.json → .../Materials (paint = Complex Lit clear coat named M_Paint_* so the
    ///    garage and traffic can tint it; textured liveries are M_Livery_* and never tinted; glass transparent)
    ///  * the FBX, its materials remapped to those assets, and a prefab with LODs
    ///  * a VehicleDefinition per drivable car in Data/Vehicles/Catalog, derived from car_map's base handling and
    ///    scaled to the model's measured wheelbase, track, tyres and body size, so physics matches the visual
    ///  * GameDatabase.Cars (dealership, garage, free roam, AI racers) and GameDatabase.TrafficModels
    /// Re-runnable: everything is overwritten in place, nothing is duplicated.
    /// </summary>
    public static class CarImportTools
    {
        const string ArtRoot = "E:/VeyraCity/ArtSource/Vehicles";
        const string DownloadRoot = "E:/VeyraCity/ArtSource/Downloads";
        const string AssetRoot = "Assets/_Veyra/Art/Vehicles";
        const string CatalogRoot = "Assets/_Veyra/Data/Vehicles/Catalog";
        const string DbPath = "Assets/_Veyra/Data/GameDatabase.asset";

        [EditorMenu("Veyra/Vehicles/Import Downloaded Cars")]
        public static void ImportMenu() => ImportAll();

        /// <summary>Batch entry: -executeMethod VeyraCity.EditorTools.CarImportTools.ImportBatch</summary>
        public static void ImportBatch()
        {
            try
            {
                int failures = ImportAll();
                EditorApplication.Exit(failures == 0 ? 0 : 2);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static int ImportAll()
        {
            var map = JObject.Parse(File.ReadAllText(Path.Combine(ArtRoot, "car_map.json")));
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DbPath);
            if (db == null) throw new InvalidOperationException("GameDatabase missing at " + DbPath);
            db.Feel = EnsureFeel();
            var cars = db.Cars.ToList();
            var traffic = (db.TrafficModels ?? Array.Empty<GameObject>()).Where(t => t != null).ToList();
            var log = new List<string>();
            int failures = 0;
            Directory.CreateDirectory(CatalogRoot);
            foreach (JObject m in map["cars"])
            {
                string id = (string)m["id"];
                try
                {
                    string result = ImportCar(m, cars, traffic);
                    log.Add(result);
                    Debug.Log("[CarImport] " + result);
                }
                catch (Exception e)
                {
                    failures++;
                    log.Add($"{id}: FAILED {e.Message}");
                    Debug.LogError($"[CarImport] {id}: {e}");
                }
            }
            db.Cars = cars.ToArray();
            db.TrafficModels = traffic.ToArray();
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs/A8");
            File.WriteAllLines("Logs/A8/unity_import.txt", log);
            Debug.Log($"[CarImport] done: {log.Count - failures} imported, {failures} failed; {db.Cars.Length} cars, {db.TrafficModels.Length} traffic models");
            return failures;
        }

        static string ImportCar(JObject m, List<CarEntry> cars, List<GameObject> traffic)
        {
            string id = (string)m["id"];
            string srcDir = $"{ArtRoot}/{id}";
            var report = JObject.Parse(File.ReadAllText($"{srcDir}/process_report.json"));
            if (!(bool)report["wheels_ok"]) throw new InvalidOperationException("wheels not found in the Blender step");
            var licence = JObject.Parse(File.ReadAllText($"{DownloadRoot}/{Path.GetFileNameWithoutExtension((string)m["src"])}.licence.json"));
            var matsJson = JObject.Parse(File.ReadAllText($"{srcDir}/materials.json"));
            string dst = $"{AssetRoot}/{id}";
            Directory.CreateDirectory($"{dst}/Textures");
            Directory.CreateDirectory($"{dst}/Materials");

            // ---- textures ----
            var normalFiles = new HashSet<string>();
            var linearFiles = new HashSet<string>();
            foreach (var p in matsJson.Properties())
            {
                var e = (JObject)p.Value;
                if (e["normalTex"] is JObject nt) normalFiles.Add((string)nt["file"]);
                if (e["msTex"] is JObject ms) linearFiles.Add((string)ms["file"]);
            }
            foreach (var f in Directory.GetFiles($"{srcDir}/textures"))
            {
                string to = $"{dst}/Textures/{Path.GetFileName(f)}";
                File.Copy(f, to, true);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var f in Directory.GetFiles($"{dst}/Textures").Where(x => !x.EndsWith(".meta")))
            {
                string path = f.Replace('\\', '/');
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                if (ti == null) continue;
                string name = Path.GetFileName(path);
                bool changed = false;
                void Set<T>(T current, T wanted, Action<T> apply) { if (!EqualityComparer<T>.Default.Equals(current, wanted)) { apply(wanted); changed = true; } }
                Set(ti.textureType, normalFiles.Contains(name) ? TextureImporterType.NormalMap : TextureImporterType.Default, v => ti.textureType = v);
                Set(ti.sRGBTexture, !(normalFiles.Contains(name) || linearFiles.Contains(name)), v => ti.sRGBTexture = v);
                Set(ti.maxTextureSize, 2048, v => ti.maxTextureSize = v);
                Set(ti.textureCompression, TextureImporterCompression.CompressedHQ, v => ti.textureCompression = v);
                Set(ti.mipmapEnabled, true, v => ti.mipmapEnabled = v);
                Set(ti.anisoLevel, 4, v => ti.anisoLevel = v);
                if (changed) ti.SaveAndReimport();
            }

            // ---- materials ----
            Color? paintColour = null;
            var remap = new Dictionary<string, Material>();
            int paintIndex = 0;
            foreach (var p in matsJson.Properties())
                remap[p.Name] = BuildMaterial(id, dst, p.Name, (JObject)p.Value, ref paintIndex, ref paintColour);

            // ---- model ----
            string fbxAsset = $"{dst}/{id}.fbx";
            File.Copy((string)report["fbx"], fbxAsset, true);
            AssetDatabase.ImportAsset(fbxAsset, ImportAssetOptions.ForceSynchronousImport);
            var imp = (ModelImporter)AssetImporter.GetAtPath(fbxAsset);
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (var kv in remap)
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            imp.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxAsset);
            CheckAxes(model, id);
            var unmapped = model.GetComponentsInChildren<Renderer>(true)
                .Where(r => !r.name.StartsWith("UCX_") && !r.name.StartsWith("UBX_"))
                .SelectMany(r => r.sharedMaterials).Where(x => x == null || !AssetDatabase.GetAssetPath(x).StartsWith($"{dst}/Materials/"))
                .Select(x => x == null ? "<null>" : x.name).Distinct().ToList();
            if (unmapped.Count > 0) throw new InvalidOperationException("materials not remapped: " + string.Join(", ", unmapped));

            string prefabPath = $"{dst}/{id}.prefab";
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            SetupLods(inst);
            var prefab = PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
            UnityEngine.Object.DestroyImmediate(inst);

            var roles = m["roles"]?.Select(x => (string)x).ToList() ?? new List<string> { "player" };
            string credit = (string)licence["credit"] + (m["strip"] != null || m["fixLivery"] != null ? " (modified: brand badges removed" + (m["fixLivery"] != null ? ", livery edited" : "") + ")" : "");
            string summary = $"{id}: model ok ({remap.Count} materials)";
            if (roles.Contains("traffic"))
            {
                traffic.RemoveAll(t => t != null && t.name == prefab.name);
                traffic.Add(prefab);
                summary += ", traffic";
            }
            if (roles.Contains("player"))
            {
                var def = BuildDefinition(id, m, report);
                cars.RemoveAll(c => c.Id == id);
                cars.Add(new CarEntry
                {
                    Id = id, Name = (string)m["name"], Maker = (string)m["maker"], Description = (string)m["description"],
                    Price = (int?)m["price"] ?? 20000, UnlockLevel = (int?)m["level"] ?? 1,
                    Definition = def, Model = prefab, DefaultPaint = paintColour ?? new Color(0.6f, 0.62f, 0.64f),
                    Credit = credit, AIOnly = false, Starter = false,
                });
                summary += $", player (wb {def.Wheelbase:0.00} m, track {def.Front.TrackWidth:0.00}/{def.Rear.TrackWidth:0.00}, r {def.Front.Tyre.Radius:0.000}, {def.Chassis.Mass:0} kg)";
            }
            return summary;
        }

        /// <summary>The default (Arcade) handling-feel asset; created with the code defaults if missing, never overwritten.</summary>
        public static HandlingFeel EnsureFeel()
        {
            const string path = "Assets/_Veyra/Data/Vehicles/HandlingFeel_Arcade.asset";
            var feel = AssetDatabase.LoadAssetAtPath<HandlingFeel>(path);
            if (feel != null) return feel;
            feel = ScriptableObject.CreateInstance<HandlingFeel>();
            feel.StyleName = "Arcade";
            AssetDatabase.CreateAsset(feel, path);
            const string simPath = "Assets/_Veyra/Data/Vehicles/HandlingFeel_Simulation.asset";
            if (AssetDatabase.LoadAssetAtPath<HandlingFeel>(simPath) == null)
            {
                var sim = ScriptableObject.CreateInstance<HandlingFeel>();
                sim.StyleName = "Simulation";
                sim.SlideDamping = 0f; sim.YawTracking = 0f; sim.DownforceG = 0f; sim.DriftEnabled = false;
                sim.AirLevelGain = 0f; sim.AirDamping = 0f; sim.AirYawControl = 0f; sim.WallGlanceMaxDeg = 0f;
                AssetDatabase.CreateAsset(sim, simPath);
            }
            return feel;
        }

        // ------------------------------------------------------------------------------------------ materials
        static Material BuildMaterial(string id, string dst, string name, JObject e, ref int paintIndex, ref Color? paintColour)
        {
            string role = (string)e["role"] ?? "trim";
            bool textured = e["baseColorTex"] is JObject;
            bool paint = role == "paint" && !textured;
            bool livery = role == "paint" && textured;
            string lower = name.ToLowerInvariant();
            // Opaque lamp materials (head, tail, indicator lenses and reflectors): emissive with their own artwork,
            // named M_Lamp_* so VehicleFX can light them at night and under braking.
            bool lamp = !paint && !livery && ((string)e["alphaMode"] ?? "OPAQUE") != "BLEND"
                        && (lower.Contains("light") || lower.Contains("lamp") || lower.Contains("optic"));
            bool clearcoat = paint || livery || (e["clearcoat"] != null && (float)e["clearcoat"] > 0.01f);
            string safe = new string(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
            string matName = paint ? $"M_Paint_{id}_{paintIndex++}" : livery ? $"M_Livery_{id}_{safe}" : lamp ? $"M_Lamp_{id}_{safe}" : $"M_{id}_{safe}";
            string path = $"{dst}/Materials/{matName}.mat";
            var shader = Shader.Find(clearcoat ? "Universal Render Pipeline/Complex Lit" : "Universal Render Pipeline/Lit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
            else if (mat.shader != shader) mat.shader = shader;
            mat.shaderKeywords = Array.Empty<string>();

            var bc = Factor(e["baseColorFactor"], Color.white);
            var baseColour = new Color(bc.r, bc.g, bc.b).gamma; // glTF factors are linear
            baseColour.a = bc.a;
            if (paint && paintColour == null) paintColour = baseColour;
            mat.SetColor("_BaseColor", baseColour);
            var baseTex = Tex(dst, e["baseColorTex"], out var st, out var so);
            mat.SetTexture("_BaseMap", baseTex);
            mat.SetTextureScale("_BaseMap", st);
            mat.SetTextureOffset("_BaseMap", so);

            float metallic = (float?)e["metallic"] ?? 0f, smooth = (float?)e["smoothness"] ?? 0.5f;
            var ms = Tex(dst, e["msTex"], out _, out _);
            if (ms != null)
            {
                mat.SetTexture("_MetallicGlossMap", ms);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_SmoothnessTextureChannel", 0f);
                mat.SetFloat("_Smoothness", 1f);
            }
            else
            {
                mat.SetTexture("_MetallicGlossMap", null);
                mat.SetFloat("_Metallic", metallic);
                mat.SetFloat("_Smoothness", Mathf.Clamp01(smooth));
            }
            if (paint || livery)
            {
                // Car paint: a glossy base under a clear coat (Complex Lit), so the body reflects the city.
                if (ms == null) mat.SetFloat("_Smoothness", Mathf.Max(0.55f, smooth));
                mat.EnableKeyword("_CLEARCOAT");
                mat.SetFloat("_ClearCoatMask", Mathf.Clamp01((float?)e["clearcoat"] ?? 1f));
                mat.SetFloat("_ClearCoatSmoothness", Mathf.Clamp((float?)e["clearcoatSmoothness"] ?? 0.92f, 0.85f, 0.98f));
            }
            else if (clearcoat)
            {
                mat.EnableKeyword("_CLEARCOAT");
                mat.SetFloat("_ClearCoatMask", Mathf.Clamp01((float)e["clearcoat"]));
                mat.SetFloat("_ClearCoatSmoothness", Mathf.Clamp01((float?)e["clearcoatSmoothness"] ?? 0.9f));
            }

            var nrm = Tex(dst, e["normalTex"], out _, out _);
            mat.SetTexture("_BumpMap", nrm);
            if (nrm != null) { mat.EnableKeyword("_NORMALMAP"); mat.SetFloat("_BumpScale", (float?)e["normalScale"] ?? 1f); }

            var em = Factor(e["emissiveFactor"], Color.black);
            var emTex = Tex(dst, e["emissiveTex"], out _, out _);
            if (lamp && emTex == null && em.maxColorComponent < 0.001f)
            {
                mat.SetTexture("_EmissionMap", baseTex);
                mat.SetColor("_EmissionColor", Color.white * 0.15f);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else if (emTex != null || em.maxColorComponent > 0.001f)
            {
                mat.SetTexture("_EmissionMap", emTex);
                mat.SetColor("_EmissionColor", emTex != null && em.maxColorComponent < 0.001f ? Color.white : new Color(em.r, em.g, em.b));
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }

            string alpha = (string)e["alphaMode"] ?? "OPAQUE";
            bool doubleSided = (bool?)e["doubleSided"] ?? false;
            mat.SetFloat("_Cull", doubleSided || alpha == "BLEND" ? (float)CullMode.Off : (float)CullMode.Back);
            if (alpha == "BLEND")
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)RenderQueue.Transparent;
                if (role == "glass") mat.SetFloat("_Smoothness", Mathf.Max(0.9f, mat.GetFloat("_Smoothness")));
            }
            else
            {
                mat.SetFloat("_Surface", 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.One);
                mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
                mat.SetFloat("_ZWrite", 1f);
                mat.SetOverrideTag("RenderType", alpha == "MASK" ? "TransparentCutout" : "Opaque");
                bool clip = alpha == "MASK";
                mat.SetFloat("_AlphaClip", clip ? 1f : 0f);
                if (clip) { mat.EnableKeyword("_ALPHATEST_ON"); mat.SetFloat("_Cutoff", (float?)e["alphaCutoff"] ?? 0.5f); }
                mat.renderQueue = clip ? (int)RenderQueue.AlphaTest : -1;
            }
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Color Factor(JToken t, Color fallback)
        {
            if (t is not JArray a || a.Count < 3) return fallback;
            return new Color((float)a[0], (float)a[1], (float)a[2], a.Count > 3 ? (float)a[3] : 1f);
        }

        static Texture2D Tex(string dst, JToken t, out Vector2 scale, out Vector2 offset)
        {
            scale = Vector2.one;
            offset = Vector2.zero;
            if (t is not JObject o || o["file"] == null) return null;
            if (o["tiling"] is JArray ta && ta.Count == 2) scale = new Vector2((float)ta[0], (float)ta[1]);
            if (o["offset"] is JArray oa && oa.Count == 2) offset = new Vector2((float)oa[0], (float)oa[1]);
            return AssetDatabase.LoadAssetAtPath<Texture2D>($"{dst}/Textures/{(string)o["file"]}");
        }

        // ------------------------------------------------------------------------------------------ model checks
        static void CheckAxes(GameObject model, string id)
        {
            var t = model.GetComponentsInChildren<Transform>(true);
            Transform Find(string n) => t.FirstOrDefault(x => x.name == n);
            var fl = Find("Wheel_FL");
            var rr = Find("Wheel_RR");
            if (fl == null || rr == null) throw new InvalidOperationException("Wheel_FL/Wheel_RR missing in the imported FBX");
            Vector3 a = model.transform.InverseTransformPoint(fl.position), b = model.transform.InverseTransformPoint(rr.position);
            // CLAUDE.md: the car's front faces Unity +Z, its left side is -X.
            if (!(a.z > b.z) || !(a.x < b.x))
                throw new InvalidOperationException($"axis check failed: Wheel_FL {a} vs Wheel_RR {b} (front must be +Z, left -X)");
        }

        static void SetupLods(GameObject root)
        {
            float[] h = { 0.30f, 0.10f, 0.012f };
            var lods = new List<LOD>();
            for (int i = 0; i < 3; i++)
            {
                var body = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Body_LOD" + i);
                if (body == null) continue;
                lods.Add(new LOD(h[i], body.GetComponentsInChildren<Renderer>(true)));
            }
            if (lods.Count == 0) return;
            var g = root.GetComponent<LODGroup>();
            if (g == null) g = root.AddComponent<LODGroup>();
            g.fadeMode = LODFadeMode.CrossFade;
            g.animateCrossFading = true;
            g.SetLODs(lods.ToArray());
            g.RecalculateBounds();
            // Wheels and calipers sit outside the LOD group: always drawn while the car is in view.
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("UCX_") || r.name.StartsWith("UBX_")) r.enabled = false;
        }

        // ------------------------------------------------------------------------------------------ handling
        static VehicleDefinition BuildDefinition(string id, JObject m, JObject rep)
        {
            var baseDef = AssetDatabase.LoadAssetAtPath<VehicleDefinition>((string)m["baseDef"]);
            if (baseDef == null) throw new InvalidOperationException("base definition missing: " + (string)m["baseDef"]);
            string defPath = $"{CatalogRoot}/{id}.asset";
            var def = UnityEngine.Object.Instantiate(baseDef);
            def.name = id;
            def.DisplayName = (string)m["name"];
            def.BuildId = "DL_" + id;
            def.Wheelbase = (float)rep["wheelbase"];
            def.Front.TrackWidth = (float)rep["track_front"];
            def.Rear.TrackWidth = (float)rep["track_rear"];
            float rf = (float)rep["radius_front"], rr = (float)rep["radius_rear"];
            // Wheel inertia scales with r² (same tyre construction as the template).
            def.Front.Tyre.WheelInertia *= Mathf.Pow(rf / Mathf.Max(0.2f, baseDef.Front.Tyre.Radius), 2f);
            def.Rear.Tyre.WheelInertia *= Mathf.Pow(rr / Mathf.Max(0.2f, baseDef.Rear.Tyre.Radius), 2f);
            def.Front.Tyre.Radius = rf;
            def.Rear.Tyre.Radius = rr;
            if (rep["width_front"] != null) def.Front.Tyre.Width = (float)rep["width_front"];
            if (rep["width_rear"] != null) def.Rear.Tyre.Width = (float)rep["width_rear"];
            var d = (JArray)rep["dimensions"];
            def.Chassis.Dimensions = new Vector3((float)d[0], (float)d[1], (float)d[2]);
            float mass = (float?)m["mass"] ?? baseDef.Chassis.Mass;
            float massRatio = mass / Mathf.Max(1f, baseDef.Chassis.Mass);
            def.Chassis.Mass = mass;
            // Springs and dampers follow the mass so ride height and damping ratio stay where the template had them.
            foreach (bool front in new[] { true, false })
            {
                var axle = front ? def.Front : def.Rear;
                axle.Suspension.SpringRate *= massRatio;
                axle.Suspension.BumpDamping *= Mathf.Sqrt(massRatio);
                axle.Suspension.ReboundDamping *= Mathf.Sqrt(massRatio);
                axle.Suspension.AntiRollRate *= massRatio;
                axle.Suspension.BumpStopRate *= massRatio;
                axle.Tyre.NominalLoad *= massRatio;
                if (front) def.Front = axle; else def.Rear = axle;
            }
            def.Brakes.MaxTorque *= massRatio;
            // Body drag: boxy classics, vans and trucks push far more air than the template (top speed follows).
            def.Aero.DragArea *= (float?)m["dragScale"] ?? 1f;
            float ts = (float?)m["torqueScale"] ?? 1f;
            if (Mathf.Abs(ts - 1f) > 1e-3f)
            {
                var c = (Vector2[])def.Engine.TorqueCurve.Clone();
                for (int i = 0; i < c.Length; i++) c[i].y *= ts;
                def.Engine.TorqueCurve = c;
            }
            // Height of the centre of mass follows the body height (tall vans and trucks roll more).
            def.Chassis.CoMHeight = Mathf.Clamp(baseDef.Chassis.CoMHeight * def.Chassis.Dimensions.y / Mathf.Max(0.8f, baseDef.Chassis.Dimensions.y), 0.38f, 0.95f);
            var existing = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(defPath);
            if (existing != null)
            {
                int version = existing.TuningVersion;
                EditorUtility.CopySerialized(def, existing);
                existing.name = id;
                existing.TuningVersion = version + 1;
                UnityEngine.Object.DestroyImmediate(def);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(def, defPath);
            return def;
        }
    }
}
