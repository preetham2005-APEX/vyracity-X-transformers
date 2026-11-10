using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card B3: import rules for the Blender pipeline's models (B2 chunks in Art/World/Chunks, kit pieces and props in
    /// Art/Models). Runs after <see cref="VeyraModelImportDefaults"/> (first-import defaults) and
    /// <see cref="CollisionPrefixPostprocessor"/> (UCX_ → convex MeshCollider, UBX_ → BoxCollider).
    /// <list type="bullet">
    /// <item>Preprocess (every import): scale factor 1, Convert Units on, Bake Axis Conversion on, Read/Write off, normals
    /// imported, tangents Mikk, no cameras / lights / animation. Materials are imported only to read their source names.</item>
    /// <item>Materials: each source material name goes through Tools/Blender/material_map.json (the same table B2 writes its
    /// hints from) to a <see cref="VeyraMat"/> slot of the VeyraMaterialLibrary; unmapped or empty slots get the orange
    /// <see cref="VeyraMat.DebugMissing"/> material and are logged.</item>
    /// <item>Collision: COL_* → non-convex MeshCollider with a <see cref="VehicleSurface"/> (COL_Road → dry asphalt,
    /// COL_Ground → grass); renderers removed from every COL_/UCX_/UBX_ node. Layer Default (the wheel raycast uses all
    /// layers, VehicleController.CastWheel).</item>
    /// <item>Static flags: Contribute GI, Occludee Static, Reflection Probe Static, Occluder Static on building renderers;
    /// Batching Static off (L1 decides on the GPU Resident Drawer). Renderers receive GI from light probes.</item>
    /// <item>LODGroup: cross-fade, screen-relative transitions LOD0 0.35, LOD1 0.12, cull 0.01 (0.005 for landmarks);
    /// two-level small props 0.15 / cull 0.02.</item>
    /// </list>
    /// </summary>
    public sealed class VeyraModelPostprocessor : AssetPostprocessor
    {
        public const string MaterialMapFile = "Tools/Blender/material_map.json";
        public const string SurfacesDir = "Assets/_Veyra/Data/Surfaces";
        const string MaterialMapDependency = "VeyraMaterialMap";

        public static readonly string[] Categories = { "Road_Surface", "Sidewalk", "Building", "Prop", "Terrain", "Vegetation" };
        static readonly Regex ChunkName = new Regex(@"^Chunk_[A-H][1-6]_\d{2}_(Road_Surface|Sidewalk|Building|Prop|Terrain|Vegetation)_", RegexOptions.Compiled);
        static readonly Regex KitName = new Regex(@"^VC_KB_.+_(Building|Prop)_\d{3}", RegexOptions.Compiled);
        static readonly Regex LodSuffix = new Regex(@"_LOD(\d)$", RegexOptions.Compiled);
        static readonly Regex Numbered = new Regex(@"\.\d{3}$", RegexOptions.Compiled);

        // RegisterCustomDependency is not allowed while the AssetDatabase imports (a static constructor can run inside an
        // import), so it is registered after the domain reload, outside any import.
        [InitializeOnLoadMethod]
        static void ScheduleRegistration() => EditorApplication.delayCall += RegisterMaterialMapDependency;

        /// <summary>Models re-import when material_map.json changes (custom dependency on the file's hash).</summary>
        public static void RegisterMaterialMapDependency()
        {
            string full = Path.Combine(ProjectRoot, MaterialMapFile);
            var hash = File.Exists(full) ? Hash128.Compute(File.ReadAllText(full)) : new Hash128();
            AssetDatabase.RegisterCustomDependency(MaterialMapDependency, hash);
        }

        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        public override uint GetVersion() => 5;
        public override int GetPostprocessOrder() => 100;

        public static bool Applies(string path) => VeyraModelImportDefaults.Applies(path);

        void OnPreprocessModel()
        {
            if (!Applies(assetPath) || !(assetImporter is ModelImporter mi)) return;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.bakeAxisConversion = true;
            mi.isReadable = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importBlendShapes = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }

        // ------------------------------------------------------------------------------------------------ material map

        public sealed class MaterialRule
        {
            public Regex Pattern;
            public string Key;
            public string Category;
        }

        public sealed class MaterialMap
        {
            public readonly List<MaterialRule> Rules = new List<MaterialRule>();
            public readonly Dictionary<string, string> Defaults = new Dictionary<string, string>();

            /// <summary>VeyraMat key name for a source material of the given category (first matching rule, then the default).</summary>
            public string KeyFor(string material, string category)
            {
                foreach (var r in Rules)
                    if ((r.Category == null || r.Category == category) && r.Pattern.IsMatch(material)) return r.Key;
                return category != null && Defaults.TryGetValue(category, out var k) ? k : null;
            }
        }

        public static MaterialMap LoadMaterialMap()
        {
            var map = new MaterialMap();
            string full = Path.Combine(ProjectRoot, MaterialMapFile);
            if (!File.Exists(full)) return map;
            var json = JObject.Parse(File.ReadAllText(full));
            foreach (var r in json["rules"] ?? new JArray())
                map.Rules.Add(new MaterialRule
                {
                    Pattern = new Regex((string)r["pattern"], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                    Key = (string)r["key"],
                    Category = (string)r["category"],
                });
            if (json["category_defaults"] is JObject d)
                foreach (var kv in d) map.Defaults[kv.Key] = (string)kv.Value;
            return map;
        }

        /// <summary>Segment category of a node from its B2 name (chunk or kit piece), or null.</summary>
        public static string CategoryOf(string nodeName)
        {
            var m = ChunkName.Match(nodeName);
            if (m.Success) return m.Groups[1].Value;
            m = KitName.Match(nodeName);
            return m.Success ? m.Groups[1].Value : null;
        }

        static string CleanMaterialName(string n)
        {
            n = n.Replace(" (Instance)", "");
            return Numbered.Replace(n, "");
        }

        // ------------------------------------------------------------------------------------------------ postprocess

        void OnPostprocessModel(GameObject root)
        {
            if (!Applies(assetPath)) return;
            context.DependsOnCustomDependency(MaterialMapDependency);
            context.DependsOnSourceAsset(VeyraMaterialFactory.LibraryPath);
            // S4's axis-test fixtures (Models/_Test) keep their marker colours; only pipeline content is remapped.
            bool remap = !assetPath.StartsWith(VeyraModelImportDefaults.ModelsRoot + "_Test/");
            var lib = remap ? AssetDatabase.LoadAssetAtPath<VeyraMaterialLibrary>(VeyraMaterialFactory.LibraryPath) : null;
            var map = LoadMaterialMap();
            bool isChunk = assetPath.StartsWith(VeyraModelImportDefaults.ChunksRoot);
            bool landmark = assetPath.IndexOf("Landmark", StringComparison.OrdinalIgnoreCase) >= 0;
            var misses = new List<string>();
            Material missing = null;
            if (lib != null) lib.TryGet(VeyraMat.DebugMissing, out missing);

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var go = t.gameObject;
                string n = go.name;
                bool col = n.StartsWith("COL_"), ucx = n.StartsWith("UCX_"), ubx = n.StartsWith("UBX_");
                if (col || ucx || ubx)
                {
                    go.layer = 0;
                    if (col) MakeMeshCollider(go, n);
                    var r = go.GetComponent<MeshRenderer>();
                    if (r != null) UnityEngine.Object.DestroyImmediate(r, true);
                    var f = go.GetComponent<MeshFilter>();
                    if (!col && !ucx && f != null) UnityEngine.Object.DestroyImmediate(f, true);
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccludeeStatic);
                    continue;
                }
                var renderer = go.GetComponent<MeshRenderer>();
                string cat = CategoryOf(n);
                var flags = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
                if (cat == "Building" && renderer != null) flags |= StaticEditorFlags.OccluderStatic;
                GameObjectUtility.SetStaticEditorFlags(go, flags);
                if (renderer == null) continue;
                renderer.receiveGI = ReceiveGI.LightProbes;
                if (lib == null) continue;
                var mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string src = mats[i] != null ? CleanMaterialName(mats[i].name) : "";
                    string key = map.KeyFor(src, cat);
                    Material m = null;
                    if (key != null && Enum.TryParse(key, out VeyraMat k)) lib.TryGet(k, out m);
                    if (m == null)
                    {
                        misses.Add($"{n}: '{src}' ({cat ?? "no category"}) -> {key ?? "no rule"}");
                        m = missing;
                    }
                    if (m != null) mats[i] = m;
                }
                renderer.sharedMaterials = mats;
            }

            foreach (var g in root.GetComponentsInChildren<LODGroup>(true))
                SetLodHeights(g, landmark);
            if (misses.Count > 0)
                Debug.Log($"[Veyra B3] {assetPath}: {misses.Count} material slot(s) without a library material (orange M_Debug_Missing):\n  " + string.Join("\n  ", misses));
            if (lib == null && remap)
                Debug.Log($"[Veyra B3] {assetPath}: VeyraMaterialLibrary not found at {VeyraMaterialFactory.LibraryPath}; materials left as imported.");
        }

        void MakeMeshCollider(GameObject go, string n)
        {
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            var mc = go.GetComponent<MeshCollider>();
            if (mc == null) mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
            string surfaceId = n.StartsWith("COL_Ground") ? "grass" : "dry_asphalt";
            string surfacePath = $"{SurfacesDir}/{surfaceId}.asset";
            context.DependsOnSourceAsset(surfacePath);
            var surface = AssetDatabase.LoadAssetAtPath<SurfaceDefinition>(surfacePath);
            if (surface != null)
            {
                var vs = go.GetComponent<VehicleSurface>();
                if (vs == null) vs = go.AddComponent<VehicleSurface>();
                vs.Surface = surface;
            }
            UnityEngine.Object.DestroyImmediate(mf, true);
        }

        /// <summary>Screen-relative transitions (card B3): LOD0 0.35, LOD1 0.12, cull 0.01 / 0.005 (landmarks); 2 levels 0.15 / 0.02.</summary>
        public static void SetLodHeights(LODGroup g, bool landmark)
        {
            var lods = g.GetLODs();
            if (lods.Length == 0) return;
            float cull = landmark ? 0.005f : 0.01f;
            float[] h = lods.Length >= 3 ? new[] { 0.35f, 0.12f, cull } : lods.Length == 2 ? new[] { 0.15f, 0.02f } : new[] { cull };
            for (int i = 0; i < lods.Length; i++)
            {
                lods[i].screenRelativeTransitionHeight = i < h.Length ? h[i] : Mathf.Max(0.001f, h[h.Length - 1] * Mathf.Pow(0.5f, i - h.Length + 1));
                lods[i].fadeTransitionWidth = 0.2f;
            }
            g.fadeMode = LODFadeMode.CrossFade;
            g.animateCrossFading = true;
            g.SetLODs(lods);
        }

        /// <summary>Level index of a node named *_LOD&lt;n&gt;, or -1.</summary>
        public static int LodIndex(string nodeName)
        {
            var m = LodSuffix.Match(nodeName);
            return m.Success ? m.Groups[1].Value[0] - '0' : -1;
        }
    }
}
