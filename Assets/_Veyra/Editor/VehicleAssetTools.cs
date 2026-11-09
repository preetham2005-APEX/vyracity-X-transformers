using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Imports the Blender-built Arden Kite (ArtSource → Assets), creates its URP materials
    /// (clear-coat paint, glass, emissive lights), remaps the FBX materials, adds an LOD group and saves
    /// a prefab variant that VehicleFactory can drive. Rerunnable; the Blender source stays the authority.
    /// </summary>
    public static class VehicleAssetTools
    {
        public const string KiteDir = "Assets/_Veyra/Art/Vehicles/ArdenKite";
        public const string KiteFbx = KiteDir + "/ArdenKite_Stock.fbx";
        public const string KitePrefab = KiteDir + "/ArdenKite_Stock.prefab";
        const string MaterialsDir = KiteDir + "/Materials";

        static string SourceFbx => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ArtSource", "Vehicles", "ArdenKite", "export", "ArdenKite_Stock.fbx"));

        [MenuItem("Veyra/Vehicles/Import Arden Kite (Stock)")]
        public static void ImportKiteMenu() => ImportKite();

        public static void ImportKiteBatch()
        {
            int code = 0;
            try { ImportKite(); TestTrackBuilder.Build(); }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        public static void ImportKite()
        {
            if (!File.Exists(SourceFbx)) throw new FileNotFoundException("Blender export missing (run Tools/Blender/build_arden_kite.py)", SourceFbx);
            Directory.CreateDirectory(KiteDir);
            Directory.CreateDirectory(MaterialsDir);
            File.Copy(SourceFbx, KiteFbx, overwrite: true);
            AssetDatabase.ImportAsset(KiteFbx, ImportAssetOptions.ForceUpdate);

            var mats = CreateMaterials();
            var importer = (ModelImporter)AssetImporter.GetAtPath(KiteFbx);
            foreach (var kv in mats)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
            importer.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(KiteFbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                SetupLods(inst);
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    bool glassOrLens = r.sharedMaterials.Any(m => m != null && (m.name == "M_Glass"));
                    if (glassOrLens) r.shadowCastingMode = ShadowCastingMode.Off;
                }
                PrefabUtility.SaveAsPrefabAsset(inst, KitePrefab);
            }
            finally { Object.DestroyImmediate(inst); }

            Report();
        }

        static void SetupLods(GameObject root)
        {
            var all = root.GetComponentsInChildren<Renderer>(true);
            Renderer Find(string n) => all.FirstOrDefault(r => r.name == n);
            var b0 = Find("Body_LOD0");
            var b1 = Find("Body_LOD1");
            var b2 = Find("Body_LOD2");
            if (b0 == null || b1 == null || b2 == null) { Debug.LogWarning("[Veyra] Body LODs missing; no LODGroup"); return; }
            var others = all.Where(r => r != b0 && r != b1 && r != b2).ToArray();
            bool IsWheelOrGlass(Renderer r) => r.name.StartsWith("Wheel_") || r.name == "Glass" || r.name.StartsWith("Lights_");
            var lodGroup = root.GetComponent<LODGroup>();
            if (lodGroup == null) lodGroup = root.AddComponent<LODGroup>();
            lodGroup.SetLODs(new[]
            {
                new LOD(0.30f, new[] { b0 }.Concat(others).ToArray()),
                new LOD(0.10f, new[] { b1 }.Concat(others.Where(r => r.name != "Interior")).ToArray()),
                new LOD(0.012f, new[] { b2 }.Concat(others.Where(IsWheelOrGlass)).ToArray()),
            });
            lodGroup.RecalculateBounds();
        }

        static Dictionary<string, Material> CreateMaterials()
        {
            var d = new Dictionary<string, Material>
            {
                ["M_Paint"] = Paint("M_Paint", new Color(0.60f, 0.62f, 0.64f), 0.85f, 0.55f),
                ["M_Glass"] = Glass("M_Glass", new Color(0.06f, 0.08f, 0.10f, 0.32f)),
                ["M_Rubber"] = Lit("M_Rubber", new Color(0.035f, 0.035f, 0.037f), 0f, 0.28f),
                ["M_Rim"] = Lit("M_Rim", new Color(0.74f, 0.75f, 0.77f), 1f, 0.78f),
                ["M_Chrome"] = Lit("M_Chrome", new Color(0.92f, 0.92f, 0.94f), 1f, 0.95f),
                ["M_PlasticBlack"] = Lit("M_PlasticBlack", new Color(0.028f, 0.028f, 0.032f), 0f, 0.38f),
                ["M_Interior"] = Lit("M_Interior", new Color(0.065f, 0.065f, 0.07f), 0f, 0.25f),
                ["M_Disc"] = Lit("M_Disc", new Color(0.42f, 0.42f, 0.43f), 0.9f, 0.45f),
                ["M_Caliper"] = Lit("M_Caliper", new Color(0.62f, 0.05f, 0.04f), 0f, 0.62f),
                ["M_LightHead"] = Lit("M_LightHead", new Color(0.88f, 0.9f, 0.93f), 0.4f, 0.95f, new Color(1f, 0.97f, 0.9f) * 0.8f),
                ["M_LightTail"] = Lit("M_LightTail", new Color(0.45f, 0.02f, 0.02f), 0f, 0.9f, new Color(1f, 0.06f, 0.03f) * 1.4f),
                ["M_LightIndicator"] = Lit("M_LightIndicator", new Color(0.7f, 0.35f, 0.02f), 0f, 0.9f, new Color(1f, 0.45f, 0.05f) * 0.25f),
                ["M_LightReverse"] = Lit("M_LightReverse", new Color(0.82f, 0.82f, 0.84f), 0f, 0.92f),
            };
            AssetDatabase.SaveAssets();
            return d;
        }

        static Material GetOrCreate(string name, string shader)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find(shader);
            if (mat == null)
            {
                mat = new Material(sh) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != sh) mat.shader = sh;
            return mat;
        }

        static Material Lit(string name, Color colour, float metallic, float smoothness, Color? emission = null)
        {
            var m = GetOrCreate(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", colour);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue)
            {
                m.SetColor("_EmissionColor", emission.Value);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else m.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Car paint: URP Complex Lit with a clear-coat layer over a metallic base.</summary>
        static Material Paint(string name, Color colour, float metallic, float baseSmoothness)
        {
            var m = GetOrCreate(name, "Universal Render Pipeline/Complex Lit");
            m.SetColor("_BaseColor", colour);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", baseSmoothness);
            m.SetFloat("_ClearCoatMask", 1f);
            m.SetFloat("_ClearCoatSmoothness", 0.94f);
            m.SetFloat("_ClearCoat", 1f);
            m.EnableKeyword("_CLEARCOAT");
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Tinted transparent glass that keeps full specular reflections (premultiplied alpha).</summary>
        static Material Glass(string name, Color tint)
        {
            var m = GetOrCreate(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", 0.97f);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_BlendModePreserveSpecular", 1f);
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.SetShaderPassEnabled("DepthOnly", false);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Report()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KitePrefab);
            if (prefab == null) { Debug.LogError("[Veyra] Kite prefab not saved"); return; }
            var root = prefab.transform;
            foreach (var n in new[] { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR", "Caliper_FL" })
            {
                var t = FindDeep(root, n);
                Debug.Log(t == null ? $"[Veyra] Kite {n}: MISSING" :
                    $"[Veyra] Kite {n}: pos {root.InverseTransformPoint(t.position).ToString("F3")} rot {t.rotation.eulerAngles.ToString("F1")}");
            }
            var b = new Bounds();
            bool first = true;
            foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.name.StartsWith("Body_LOD0")) continue;
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            int tris = prefab.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null && !f.name.StartsWith("Body_LOD1") && !f.name.StartsWith("Body_LOD2") && !f.name.StartsWith("UCX_"))
                             .Sum(f => (int)(f.sharedMesh.triangles.Length / 3));
            int colliders = prefab.GetComponentsInChildren<Collider>().Length;
            int unassigned = prefab.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).Count(m => m == null || m.shader == null || m.shader.name.Contains("Error"));
            Debug.Log($"[Veyra] Kite Body_LOD0 bounds centre {b.center.ToString("F3")} size {b.size.ToString("F3")}; LOD0 tris {tris}; colliders {colliders}; bad materials {unassigned}");
        }

        static Transform FindDeep(Transform p, string name)
        {
            if (p.name == name) return p;
            foreach (Transform c in p) { var f = FindDeep(c, name); if (f != null) return f; }
            return null;
        }
    }
}
