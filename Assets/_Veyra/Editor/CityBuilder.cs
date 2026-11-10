using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VeyraCity.Assists;
using VeyraCity.Gameplay;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;
using EditorMenu = UnityEditor.MenuItem;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Builds the playable city scene from WorldSource (output of Tools/World/build_world_data.py):
    /// 48 terrain tiles with splat-mapped layers and tunnel holes, road / building / structure meshes in
    /// 250 m chunks with colliders and driving surfaces, sea and river water, instanced vegetation and
    /// street furniture, lighting, post-processing, the game database and the game flow.
    /// </summary>
    public static class CityBuilder
    {
        public const string ScenePath = "Assets/_Veyra/Scenes/VeyraCity.unity";
        public const string DbPath = "Assets/_Veyra/Data/GameDatabase.asset";
        const string Gen = "Assets/_Veyra/World/Generated";
        const string CatalogDir = "Assets/_Veyra/Data/Vehicles/Catalog";
        const float Cell = 250f;
        static string SourceDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "WorldSource"));

        [EditorMenu("Veyra/Build City Scene")]
        public static void BuildMenu() => Build();

        /// <summary>Re-link the scene's GameFlow to the game database (after a database rebuild).</summary>
        public static void RelinkBatch()
        {
            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath);
                var flow = UnityEngine.Object.FindAnyObjectByType<GameFlow>();
                var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DbPath);
                if (flow == null || db == null) throw new Exception("GameFlow or GameDatabase missing");
                var so = new SerializedObject(flow);
                so.FindProperty("db").objectReferenceValue = db;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[CityBuilder] relinked GameFlow.db -> " + AssetDatabase.GetAssetPath(db));
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        /// <summary>Refresh only the game database (cars, presets, material templates) without rebuilding the city.</summary>
        public static void UpdateDatabaseBatch()
        {
            try { BuildDatabase(); AssetDatabase.SaveAssets(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void BuildBatch()
        {
            try { Build(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void Build()
        {
            var t0 = DateTime.Now;
            Directory.CreateDirectory(Gen + "/Meshes");
            Directory.CreateDirectory(Gen + "/Terrain");
            Directory.CreateDirectory(Gen + "/Materials");
            Directory.CreateDirectory(CatalogDir);
            ConfigurePipeline();
            PlayerSettings.bakeCollisionMeshes = true;
            var db = BuildDatabase();
            var mats = BuildMaterials();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var worldRoot = new GameObject("World").transform;
            var env = BuildLighting(mats);
            var terrainRoot = BuildTerrain(worldRoot, db);
            var meshStats = BuildMeshes(worldRoot, mats, db);
            meshStats += "; " + BuildingGenerator.Build(worldRoot, mats, db, Gen);
            GrassBuilder.AddGrass(worldRoot, Gen);
            BuildWater(worldRoot, mats);
            var props = BuildProps(worldRoot, mats);
            var cam = BuildCamera();
            Debug.Log(RoadProfileApply.Apply(false)); // card C2 (BUG-001): eased bridge approaches, embankments
            env.ScrollMaterials.Add(mats["water"]);
            foreach (var key in new[] { "facade_light", "facade_mid", "facade_brick", "facade_stucco", "glass_blue", "glass_bronze", "stone_deco", "house_wall", "wood_wall", "storefront" })
                env.Emissives.Add(new WorldEnvironment.EmissiveEntry { Material = mats[key], BaseEmission = mats[key].GetColor("_EmissionColor"), NightOnly = 1f });
            foreach (var key in new[] { "lamp_head", "lamp_sodium" })
                env.Emissives.Add(new WorldEnvironment.EmissiveEntry { Material = mats[key], BaseEmission = mats[key].GetColor("_EmissionColor"), NightOnly = 1f });
            foreach (var key in new[] { "neon_cyan", "neon_magenta", "neon_amber", "light_warm", "sodium", "marker_red" })
                env.Emissives.Add(new WorldEnvironment.EmissiveEntry { Material = mats[key], BaseEmission = mats[key].GetColor("_EmissionColor"), NightOnly = 0.7f });
            foreach (var key in new[] { "asphalt", "asphalt_worn", "concrete_dark", "sidewalk", "kerb", "marking_white", "marking_yellow" })
                env.WetSurfaces.Add(new WorldEnvironment.WetEntry { Material = mats[key], DrySmoothness = mats[key].GetFloat("_Smoothness"), WetSmoothness = 0.96f, DryColor = mats[key].GetColor("_BaseColor") });

            var game = new GameObject("Game");
            var flow = game.AddComponent<GameFlow>();
            // Reload: texture imports above can re-import the database and invalidate the earlier instance.
            db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DbPath);
            if (db == null) throw new Exception("GameDatabase failed to load after build");
            var so = new SerializedObject(flow);
            so.FindProperty("db").objectReferenceValue = db;
            so.FindProperty("chaseCamera").objectReferenceValue = cam;
            so.FindProperty("environment").objectReferenceValue = env;
            var orbit = new GameObject("MenuOrbitCentre").transform;
            orbit.position = new Vector3(170f, 40f, -670f);
            orbit.SetParent(game.transform, false);
            so.FindProperty("menuOrbitCentre").objectReferenceValue = orbit;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log(VeyraPostVolumes.SetupScene(env)); // card L2: Day/Golden/Night + district and tunnel volumes
            RegisterScene();
            AssetDatabase.SaveAssets();
            Debug.Log($"[CityBuilder] Built {ScenePath} in {(DateTime.Now - t0).TotalSeconds:0.0} s. {meshStats}; props {props}");
        }

        static void RegisterScene()
        {
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ------------------------------------------------------------------------------- pipeline
        static void ConfigurePipeline()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                if (rd == null) continue;
                rd.renderingMode = RenderingMode.ForwardPlus;
                EditorUtility.SetDirty(rd);
            }
            // Card L1 / BUG-045: the URP assets belong to the quality tiers (VeyraQualityTiers); the city build no longer
            // overwrites every asset with one set of values.
            Debug.Log(VeyraQualityTiers.Apply());
        }

        // ------------------------------------------------------------------------------- database
        static T LoadAsset<T>(string path) where T : UnityEngine.Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null) throw new Exception("Missing asset " + path);
            return a;
        }

        static VehicleDefinition Derive(VehicleDefinition src, string file, string name, string id, Action<VehicleDefinition> edit)
        {
            string path = $"{CatalogDir}/{file}.asset";
            var d = UnityEngine.Object.Instantiate(src);
            d.DisplayName = name;
            d.BuildId = id;
            edit(d);
            var existing = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(path);
            if (existing != null) { EditorUtility.CopySerialized(d, existing); UnityEngine.Object.DestroyImmediate(d); return existing; }
            AssetDatabase.CreateAsset(d, path);
            return d;
        }

        static void ScaleTorque(VehicleDefinition d, float k)
        {
            var c = (Vector2[])d.Engine.TorqueCurve.Clone();
            for (int i = 0; i < c.Length; i++) c[i].y *= k;
            d.Engine.TorqueCurve = c;
        }

        static GameDatabase BuildDatabase()
        {
            const string dbPath = DbPath;
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(dbPath);
            if (db == null && File.Exists(dbPath)) AssetDatabase.DeleteAsset(dbPath); // stale asset with a missing script
            if (db == null) { db = ScriptableObject.CreateInstance<GameDatabase>(); AssetDatabase.CreateAsset(db, dbPath); }
            db.Casual = LoadAsset<AssistPreset>("Assets/_Veyra/Data/Assists/Casual.asset");
            db.Advanced = LoadAsset<AssistPreset>("Assets/_Veyra/Data/Assists/Advanced.asset");
            const string aiPath = "Assets/_Veyra/Data/Assists/AI.asset";
            var ai = AssetDatabase.LoadAssetAtPath<AssistPreset>(aiPath);
            if (ai == null) { ai = ScriptableObject.CreateInstance<AssistPreset>(); AssetDatabase.CreateAsset(ai, aiPath); }
            ai.PresetName = "AI";
            ai.SteeringSpeedRef = 999f;
            ai.MinSteerFraction = 1f;
            ai.CounterSteer = 0.5f;
            ai.TractionControl = true;
            ai.TcsPeakSlipFactor = 1.15f;
            ai.Abs = true;
            ai.StabilityControl = 0.8f;
            ai.StabilitySlipLimitDeg = 20f;
            ai.DriftAssist = 0f;
            ai.AutoAccelerate = false;
            EditorUtility.SetDirty(ai);
            db.AIPreset = ai;
            db.DryAsphalt = LoadAsset<SurfaceDefinition>("Assets/_Veyra/Data/Surfaces/dry_asphalt.asset");
            db.WetAsphalt = LoadAsset<SurfaceDefinition>("Assets/_Veyra/Data/Surfaces/wet_asphalt.asset");
            db.Concrete = LoadAsset<SurfaceDefinition>("Assets/_Veyra/Data/Surfaces/concrete.asset");
            db.Gravel = LoadAsset<SurfaceDefinition>("Assets/_Veyra/Data/Surfaces/gravel.asset");
            db.Grass = LoadAsset<SurfaceDefinition>("Assets/_Veyra/Data/Surfaces/grass.asset");
            db.Dirt = LoadAsset<SurfaceDefinition>("Assets/_Veyra/Data/Surfaces/dirt.asset");
            VeyraMaterialFactory.BuildLibrary(); // runtime material templates (card F1)
            db.PlaceholderBody = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Veyra/Art/TestTrack/M_Placeholder_CarBody.mat");
            db.PlaceholderWheel = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Veyra/Art/TestTrack/M_Placeholder_Wheel.mat");

            var kiteStock = LoadAsset<VehicleDefinition>("Assets/_Veyra/Data/Vehicles/Kite_Stock_RWD.asset");
            var kiteStreet = LoadAsset<VehicleDefinition>("Assets/_Veyra/Data/Vehicles/Kite_Street_RWD.asset");
            var fwd = LoadAsset<VehicleDefinition>("Assets/_Veyra/Data/Vehicles/Greybox_FWD.asset");
            var awd = LoadAsset<VehicleDefinition>("Assets/_Veyra/Data/Vehicles/Greybox_AWD.asset");
            var kiteModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Veyra/Art/Vehicles/ArdenKite/ArdenKite_Stock.prefab");

            var strata = Derive(fwd, "Arden_Strata", "Arden Strata", "Strata_FWD", d => { ScaleTorque(d, 1.12f); d.Chassis.Dimensions = new Vector3(1.78f, 1.42f, 4.05f); });
            var volk = Derive(awd, "Volkhart_4S", "Volkhart 4S", "Volkhart4S_AWD", d => { d.Chassis.Mass = 1610f; ScaleTorque(d, 1.15f); d.Chassis.Dimensions = new Vector3(1.86f, 1.44f, 4.75f); });
            var hammer = Derive(kiteStreet, "Rourke_Hammerhead", "Rourke Hammerhead", "Hammerhead_RWD", d =>
            {
                d.Chassis.Mass = 1540f; ScaleTorque(d, 2.25f); d.Wheelbase = 2.78f; d.Chassis.Dimensions = new Vector3(1.92f, 1.32f, 4.85f);
                d.Front.TrackWidth = 1.6f; d.Rear.TrackWidth = 1.6f; d.Aero.DragArea = 0.78f; d.Aero.DownforceArea = 0.2f;
                d.Front.Tyre.Mu = 1.1f; d.Rear.Tyre.Mu = 1.12f; d.Brakes.MaxTorque *= 1.25f;
                d.Front.Suspension.SpringRate *= 1.25f; d.Rear.Suspension.SpringRate *= 1.25f;
            });
            var sovereign = Derive(kiteStreet, "Marlowe_Sovereign", "Marlowe Sovereign", "Sovereign_RWD", d =>
            {
                d.Chassis.Mass = 1460f; ScaleTorque(d, 1.95f); d.Wheelbase = 2.7f; d.Chassis.Dimensions = new Vector3(1.9f, 1.28f, 4.6f);
                d.Front.TrackWidth = 1.6f; d.Rear.TrackWidth = 1.62f; d.Aero.DragArea = 0.64f; d.Aero.DownforceArea = 0.5f;
                d.Front.Tyre.Mu = 1.16f; d.Rear.Tyre.Mu = 1.18f; d.Brakes.MaxTorque *= 1.3f;
                d.Front.Suspension.SpringRate *= 1.3f; d.Rear.Suspension.SpringRate *= 1.3f;
            });
            var vento = Derive(awd, "Castellan_Vento_R", "Castellan Vento R", "VentoR_AWD", d =>
            {
                d.Chassis.Mass = 1390f; ScaleTorque(d, 2.35f); d.Chassis.Dimensions = new Vector3(1.98f, 1.16f, 4.55f);
                d.Aero.DragArea = 0.66f; d.Aero.DownforceArea = 1.15f; d.Front.Tyre.Mu = 1.24f; d.Rear.Tyre.Mu = 1.26f;
                d.Brakes.MaxTorque *= 1.45f; d.Chassis.CoMHeight -= 0.06f;
                d.Front.Suspension.SpringRate *= 1.5f; d.Rear.Suspension.SpringRate *= 1.5f;
                d.Front.Suspension.AntiRollRate *= 1.4f; d.Rear.Suspension.AntiRollRate *= 1.4f;
            });

            db.Cars = new[]
            {
                new CarEntry { Id = "kite", Name = "Arden Kite", Maker = "Arden Motor Works  -  2004 coupe", Price = 9000, UnlockLevel = 1, Starter = true, Definition = kiteStock, Model = kiteModel, DefaultPaint = new Color(0.62f, 0.64f, 0.66f), Description = "Light, rear-drive and honest. 140 hp, 1,150 kg. The car the story starts with." },
                new CarEntry { Id = "kite_r", Name = "Arden Kite R", Maker = "Arden Motor Works  -  street build", Price = 26000, UnlockLevel = 2, Definition = kiteStreet, Model = kiteModel, DefaultPaint = new Color(0.05f, 0.25f, 0.65f), Description = "The Kite built properly: lower, wider, stickier, 230 hp. A drift weapon." },
                new CarEntry { Id = "strata", Name = "Arden Strata", Maker = "Arden Motor Works  -  hot hatch", Price = 18000, UnlockLevel = 1, Definition = strata, DefaultPaint = new Color(0.85f, 0.32f, 0.04f), Description = "Front-drive turbo hatch. Point it, squeeze it, it goes. Forgiving in the wet." },
                new CarEntry { Id = "volkhart", Name = "Volkhart 4S", Maker = "Volkhart  -  sleeper sedan", Price = 42000, UnlockLevel = 3, Definition = volk, DefaultPaint = new Color(0.2f, 0.21f, 0.23f), Description = "All-wheel-drive four-door that embarrasses sports cars. The Nightshift's favourite." },
                new CarEntry { Id = "hammerhead", Name = "Rourke Hammerhead", Maker = "Rourke  -  V8 muscle coupe", Price = 58000, UnlockLevel = 5, Definition = hammer, DefaultPaint = new Color(0.6f, 0.03f, 0.03f), Description = "Big V8, long hood, rear tyres that never last. Iron Wolves royalty." },
                new CarEntry { Id = "sovereign", Name = "Marlowe Sovereign", Maker = "Marlowe  -  grand tourer", Price = 85000, UnlockLevel = 7, Definition = sovereign, DefaultPaint = new Color(0.92f, 0.92f, 0.9f), Description = "Front-engine GT for long coastal nights. Fast, stable and beautiful." },
                new CarEntry { Id = "vento", Name = "Castellan Vento R", Maker = "Castellan  -  supercar", Price = 165000, UnlockLevel = 10, Definition = vento, DefaultPaint = new Color(0.38f, 0.02f, 0.05f), Description = "Mid-engine all-wheel-drive missile with real downforce. Redline Syndicate territory." },
            };
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            return db;
        }

        // ------------------------------------------------------------------------------- materials
        static Material Mat(string name, Color color, float smooth, float metal = 0f, Texture2D albedo = null, Texture2D normal = null, Vector2? tiling = null, Texture2D emission = null, Color? emColor = null, bool instancing = false, bool transparent = false, Texture2D gloss = null)
        {
            string path = $"{Gen}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find("Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            m.SetTexture("_BaseMap", albedo);
            var t = tiling ?? Vector2.one;
            m.SetTextureScale("_BaseMap", t);
            if (normal != null) { m.SetTexture("_BumpMap", normal); m.SetTextureScale("_BumpMap", t); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 1f); }
            else { m.SetTexture("_BumpMap", null); m.DisableKeyword("_NORMALMAP"); }
            if (gloss != null)
            {
                // URP Lit: smoothness = map alpha x _Smoothness, so wetness can raise _Smoothness toward a mirror finish.
                m.SetTexture("_MetallicGlossMap", gloss);
                m.SetFloat("_SmoothnessTextureChannel", 0f);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            else { m.SetTexture("_MetallicGlossMap", null); m.DisableKeyword("_METALLICSPECGLOSSMAP"); }
            if (emission != null || emColor.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetTexture("_EmissionMap", emission);
                m.SetColor("_EmissionColor", emColor ?? Color.white);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); }
            if (transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            m.enableInstancing = instancing;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        static Dictionary<string, Material> BuildMaterials()
        {
            var d = new Dictionary<string, Material>();
            // Roads and paving: albedo + normal + gloss (smoothness in metallic alpha). _Smoothness is the dry scale; rain raises it.
            var asph = ProcTextures.Asphalt("Asphalt", new Color(0.22f, 0.22f, 0.23f), 11, 0.3f);
            var worn = ProcTextures.Asphalt("AsphaltWorn", new Color(0.27f, 0.27f, 0.27f), 13, 0.8f);
            var conc = ProcTextures.Paving("ConcreteRoad", new Color(0.46f, 0.46f, 0.45f), 17, 2);
            var side = ProcTextures.Paving("Sidewalk", new Color(0.3f, 0.298f, 0.292f), 19, 8);
            var kerb = ProcTextures.Granite("KerbGranite", new Color(0.44f, 0.44f, 0.45f), 23);
            var glossAsph = ProcTextures.MetalGloss("Asphalt_MS", 29, 0f, 0.35f, 6);
            var glossPave = ProcTextures.MetalGloss("Paving_MS", 31, 0f, 0.25f, 12);
            d["asphalt"] = Mat("Road_Asphalt", Color.white, 0.38f, 0f, asph.albedo, asph.normal, new Vector2(1f / 8f, 1f / 8f), gloss: glossAsph);
            d["asphalt_worn"] = Mat("Road_AsphaltWorn", Color.white, 0.32f, 0f, worn.albedo, worn.normal, new Vector2(1f / 8f, 1f / 8f), gloss: glossAsph);
            d["concrete_dark"] = Mat("Road_Concrete", Color.white, 0.34f, 0f, conc.albedo, conc.normal, new Vector2(1f / 10f, 1f / 10f), gloss: glossPave);
            d["gravel"] = Mat("Road_Gravel", Color.white, 0.12f, 0f, ProcTextures.Plain("Gravel", new Color(0.5f, 0.45f, 0.38f), 23, 32, 0.25f), null, new Vector2(1f / 4f, 1f / 4f));
            d["sidewalk"] = Mat("Sidewalk", Color.white, 0.26f, 0f, side.albedo, side.normal, new Vector2(1f / 12f, 1f / 12f), gloss: glossPave);
            d["kerb"] = Mat("Kerb_Granite", Color.white, 0.42f, 0f, kerb.albedo, kerb.normal, new Vector2(1f / 2f, 1f / 2f), gloss: glossPave);
            d["marking_white"] = Mat("Marking_White", new Color(0.9f, 0.9f, 0.88f), 0.5f, 0f, null, null, null, null, new Color(0.1f, 0.1f, 0.1f));
            d["marking_yellow"] = Mat("Marking_Yellow", new Color(0.92f, 0.7f, 0.12f), 0.5f, 0f, null, null, null, null, new Color(0.1f, 0.06f, 0f));
            var concrete = ProcTextures.Plain("Concrete", new Color(0.6f, 0.59f, 0.57f), 29, 16, 0.12f);
            d["concrete"] = Mat("Concrete", Color.white, 0.25f, 0f, concrete, null, new Vector2(1f / 6f, 1f / 6f));
            d["steel"] = Mat("Steel", new Color(0.42f, 0.44f, 0.46f), 0.55f, 0.85f);
            d["steel_painted_red"] = Mat("SteelRed", new Color(0.55f, 0.08f, 0.06f), 0.45f, 0.4f);
            d["wood"] = Mat("Wood", new Color(0.42f, 0.3f, 0.2f), 0.2f);
            d["crane_paint"] = Mat("CranePaint", new Color(0.16f, 0.36f, 0.58f), 0.45f, 0.35f);
            // Emissive lights and neon (HDR): cyan #00FFFF, magenta #FF007F, amber/sodium #FFAA00.
            d["marker_red"] = Mat("MarkerRed", new Color(0.4f, 0f, 0f), 0.5f, 0f, null, null, null, null, new Color(4f, 0.1f, 0.05f));
            d["light_warm"] = Mat("LightWarm", new Color(1f, 0.85f, 0.6f), 0.5f, 0f, null, null, null, null, new Color(3f, 2.2f, 1.2f));
            d["sodium"] = Mat("Sodium", Hex("#FFAA00"), 0.5f, 0f, null, null, null, null, Hex("#FFAA00") * 4f);
            d["neon_cyan"] = Mat("NeonCyan", Hex("#00FFFF"), 0.5f, 0f, null, null, null, null, Hex("#00FFFF") * 5f);
            d["neon_magenta"] = Mat("NeonMagenta", Hex("#FF007F"), 0.5f, 0f, null, null, null, null, Hex("#FF007F") * 5f);
            d["neon_amber"] = Mat("NeonAmber", Hex("#FFAA00"), 0.5f, 0f, null, null, null, null, Hex("#FFAA00") * 5f);
            // Facades: one texture = 12 m x 14 m (4 x 4 window modules); emission = lit windows at night.
            Color em = new Color(1.7f, 1.5f, 1.25f);
            (Texture2D albedo, Texture2D emission) F(string n, Color wall, Color glass, float ww, float wh, int seed, bool curtain, float lit) => ProcTextures.Facade(n, wall, glass, ww, wh, seed, curtain, lit);
            var fLight = F("FacadeLight", new Color(0.72f, 0.71f, 0.68f), new Color(0.13f, 0.16f, 0.2f), 0.62f, 0.62f, 31, false, 0.33f);
            var fMid = F("FacadeMid", new Color(0.45f, 0.46f, 0.48f), new Color(0.1f, 0.13f, 0.17f), 0.7f, 0.66f, 37, false, 0.3f);
            var fBrick = F("FacadeBrick", new Color(0.45f, 0.24f, 0.17f), new Color(0.12f, 0.14f, 0.16f), 0.5f, 0.58f, 41, false, 0.4f);
            var fStucco = F("FacadeStucco", new Color(0.86f, 0.8f, 0.68f), new Color(0.15f, 0.17f, 0.19f), 0.45f, 0.52f, 43, false, 0.45f);
            var fStone = F("FacadeStone", new Color(0.66f, 0.6f, 0.5f), new Color(0.1f, 0.11f, 0.13f), 0.38f, 0.66f, 71, false, 0.36f);
            var fHouse = F("HouseWall", new Color(0.82f, 0.78f, 0.7f), new Color(0.12f, 0.14f, 0.16f), 0.36f, 0.4f, 73, false, 0.5f);
            var fWood = F("WoodWall", new Color(0.46f, 0.33f, 0.22f), new Color(0.12f, 0.13f, 0.14f), 0.3f, 0.4f, 79, false, 0.45f);
            var gBlue = F("GlassBlue", new Color(0.18f, 0.2f, 0.23f), new Color(0.12f, 0.22f, 0.32f), 0.9f, 0.85f, 47, true, 0.28f);
            var gBronze = F("GlassBronze", new Color(0.24f, 0.2f, 0.16f), new Color(0.3f, 0.22f, 0.14f), 0.9f, 0.85f, 53, true, 0.3f);
            var shop = ProcTextures.Storefront("Storefront", 83);
            d["facade_light"] = Mat("Facade_Light", Color.white, 0.25f, 0f, fLight.albedo, null, null, fLight.emission, em);
            d["facade_mid"] = Mat("Facade_Mid", Color.white, 0.3f, 0f, fMid.albedo, null, null, fMid.emission, em);
            d["facade_brick"] = Mat("Facade_Brick", Color.white, 0.18f, 0f, fBrick.albedo, null, null, fBrick.emission, em);
            d["facade_stucco"] = Mat("Facade_Stucco", Color.white, 0.2f, 0f, fStucco.albedo, null, null, fStucco.emission, em);
            d["stone_deco"] = Mat("Facade_StoneDeco", Color.white, 0.28f, 0f, fStone.albedo, null, null, fStone.emission, em);
            d["house_wall"] = Mat("Facade_House", Color.white, 0.2f, 0f, fHouse.albedo, null, null, fHouse.emission, em);
            d["wood_wall"] = Mat("Facade_Wood", Color.white, 0.2f, 0f, fWood.albedo, null, null, fWood.emission, em);
            d["glass_blue"] = Mat("Glass_Blue", Color.white, 0.92f, 0.65f, gBlue.albedo, null, null, gBlue.emission, em);
            d["glass_bronze"] = Mat("Glass_Bronze", Color.white, 0.9f, 0.7f, gBronze.albedo, null, null, gBronze.emission, em);
            d["storefront"] = Mat("Storefront", Color.white, 0.85f, 0.2f, shop.albedo, null, null, shop.emission, new Color(0.8f, 0.75f, 0.7f));
            d["metal_clad"] = Mat("Facade_MetalClad", Color.white, 0.4f, 0.55f, ProcTextures.Corrugated("MetalClad", new Color(0.58f, 0.6f, 0.62f), 89), null, null);
            d["tank_white"] = Mat("Tank_White", Color.white, 0.45f, 0.4f, ProcTextures.Corrugated("TankWhite", new Color(0.82f, 0.82f, 0.8f), 97), null, null);
            var roof = ProcTextures.Plain("Roof", new Color(0.22f, 0.22f, 0.23f), 59, 16, 0.2f);
            d["roof_dark"] = Mat("Roof_Dark", Color.white, 0.2f, 0f, roof, null, new Vector2(1f, 1f));
            d["roof_tile"] = Mat("Roof_Tile", new Color(0.62f, 0.3f, 0.22f), 0.25f, 0f, ProcTextures.Plain("RoofTile", Color.white, 61, 32, 0.25f), null, new Vector2(1f, 1f));
            d["roof_metal"] = Mat("Roof_Metal", Color.white, 0.45f, 0.6f, ProcTextures.Corrugated("RoofMetal", new Color(0.55f, 0.57f, 0.58f), 67), null, new Vector2(1f, 1f));
            // Ironport containers: the eight colours of the placement data, plus grey fallback.
            foreach (var hx in new[] { "2e5e9a", "8e3b6e", "c9b036", "d98b2b", "3c4650", "7a7f86", "b23a2e", "2f7f72" })
                d["container_" + hx] = Mat("Container_" + hx, Color.white, 0.35f, 0.3f, ProcTextures.Corrugated("Corr_" + hx, Hex("#" + hx), hx.GetHashCode() & 1023), null, null);
            d["container_grey"] = d["container_7a7f86"];
            // GLB container colours still referenced by older names
            d["container_orange"] = d["container_d98b2b"]; d["container_teal"] = d["container_2f7f72"]; d["container_red"] = d["container_b23a2e"]; d["container_blue"] = d["container_2e5e9a"];
            var wn = ProcTextures.WaterNormal("Water_N");
            d["water"] = Mat("Water", new Color(0.015f, 0.05f, 0.07f), 0.96f, 0.25f, null, wn, new Vector2(1f / 40f, 1f / 40f));
            // props
            d["bark"] = Mat("Bark", new Color(0.26f, 0.19f, 0.14f), 0.15f, 0f, null, null, null, null, null, true);
            d["pine"] = Mat("Foliage_Pine", new Color(0.08f, 0.18f, 0.1f), 0.12f, 0f, null, null, null, null, null, true);
            d["leaf"] = Mat("Foliage_Leaf", new Color(0.16f, 0.3f, 0.12f), 0.15f, 0f, null, null, null, null, null, true);
            d["palm"] = Mat("Foliage_Palm", new Color(0.2f, 0.36f, 0.14f), 0.2f, 0f, null, null, null, null, null, true);
            d["palm"].SetFloat("_Cull", 0f);
            d["lamp_pole"] = Mat("Lamp_Pole", new Color(0.18f, 0.19f, 0.2f), 0.45f, 0.7f, null, null, null, null, null, true);
            d["lamp_head"] = Mat("Lamp_Head", new Color(0.95f, 0.92f, 0.85f), 0.6f, 0f, null, null, null, null, new Color(6f, 5.4f, 4.2f), true);
            d["lamp_sodium"] = Mat("Lamp_Sodium", Hex("#FFAA00"), 0.6f, 0f, null, null, null, null, Hex("#FFAA00") * 6f, true);
            VeyraPalette.OverrideCityMaterials(d); // card F2: palette materials replace the blockout ones they cover
            AssetDatabase.SaveAssets();
            return d;
        }

        // ------------------------------------------------------------------------------- lighting
        static WorldEnvironment BuildLighting(Dictionary<string, Material> mats)
        {
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.intensity = 2f;
            sunGo.transform.rotation = Quaternion.Euler(40f, 210f, 0f);
            RenderSettings.sun = sun;

            string skyPath = $"{Gen}/Materials/Sky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, skyPath); }
            sky.SetFloat("_SunSize", 0.03f);
            sky.SetFloat("_AtmosphereThickness", 1f);
            sky.SetColor("_GroundColor", new Color(0.18f, 0.18f, 0.2f));
            RenderSettings.skybox = sky;

            string profPath = $"{Gen}/PostProfile.asset";
            AssetDatabase.DeleteAsset(profPath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profPath);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.value = TonemappingMode.ACES;
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.value = 0.9f;
            bloom.intensity.value = 1.2f;
            bloom.scatter.value = 0.7f;
            var ca = profile.Add<ColorAdjustments>(true);
            ca.contrast.value = 14f;
            ca.saturation.value = 10f;
            ca.postExposure.value = 0.3f;
            var vig = profile.Add<Vignette>(true);
            vig.intensity.value = 0.24f;
            vig.smoothness.value = 0.45f;
            var mb = profile.Add<MotionBlur>(true);
            mb.intensity.value = 0.18f;
            mb.quality.value = MotionBlurQuality.Medium;
            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.value = -6f;
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            var volGo = new GameObject("PostProcess");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            var envGo = new GameObject("Environment");
            var env = envGo.AddComponent<WorldEnvironment>();
            env.Sun = sun;
            env.SkyboxMaterial = sky;
            env.PostVolume = vol;
            env.TimeOfDay = 21.5f;
            return env;
        }

        static ChaseCamera BuildCamera()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 3500f;
            cam.fieldOfView = 60f;
            cam.allowHDR = true;
            go.AddComponent<AudioListener>();
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            go.transform.position = new Vector3(170f, 200f, -1200f);
            return go.AddComponent<ChaseCamera>();
        }

        // ------------------------------------------------------------------------------- terrain
        [Serializable] class TerrainMeta { public int resolution; public float tileSize, heightOffset, heightRange; public TileMeta[] tiles; }
        [Serializable] class TileMeta { public string name; public float x0, z0; }

        static Transform BuildTerrain(Transform worldRoot, GameDatabase db)
        {
            var root = new GameObject("Terrain").transform;
            root.SetParent(worldRoot, false);
            string dir = Path.Combine(SourceDir, "terrain");
            var meta = JsonUtility.FromJson<TerrainMeta>(File.ReadAllText(Path.Combine(dir, "terrain_meta.json")));
            var layers = BuildTerrainLayers();
            Texture2D Load(string file)
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                t.LoadImage(File.ReadAllBytes(file));
                return t;
            }
            var splat0 = Load(Path.Combine(dir, "splatmap_0_4096x3072.png"));
            var splat1 = Load(Path.Combine(dir, "splatmap_1_4096x3072.png"));
            var c0 = splat0.GetPixels32();
            var c1 = splat1.GetPixels32();
            int sw = splat0.width, sh = splat0.height;
            UnityEngine.Object.DestroyImmediate(splat0);
            UnityEngine.Object.DestroyImmediate(splat1);
            var terrainMat = AssetDatabase.LoadAssetAtPath<Material>($"{Gen}/Materials/Terrain.mat");
            if (terrainMat == null) { terrainMat = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit")); AssetDatabase.CreateAsset(terrainMat, $"{Gen}/Materials/Terrain.mat"); }
            int res = meta.resolution;
            const int amRes = 256;
            var terrains = new Dictionary<string, Terrain>();
            foreach (var tile in meta.tiles)
            {
                string tdPath = $"{Gen}/Terrain/T_{tile.name}.asset";
                var td = AssetDatabase.LoadAssetAtPath<TerrainData>(tdPath);
                if (td == null) { td = new TerrainData(); AssetDatabase.CreateAsset(td, tdPath); }
                td.heightmapResolution = res;
                td.alphamapResolution = amRes;
                td.baseMapResolution = 512;
                td.SetDetailResolution(32, 16);
                td.size = new Vector3(meta.tileSize, meta.heightRange, meta.tileSize);
                var raw = File.ReadAllBytes(Path.Combine(dir, $"terrain_{tile.name}.raw"));
                var h = new float[res, res];
                for (int y = 0; y < res; y++)
                    for (int x = 0; x < res; x++)
                    {
                        int k = (y * res + x) * 2;
                        h[y, x] = (raw[k] | (raw[k + 1] << 8)) / 65535f;
                    }
                td.SetHeights(0, 0, h);
                var holesRaw = File.ReadAllBytes(Path.Combine(dir, $"holes_{tile.name}.bin"));
                var holes = new bool[res - 1, res - 1];
                bool anyHole = false;
                for (int y = 0; y < res - 1; y++)
                    for (int x = 0; x < res - 1; x++)
                    {
                        bool hole = holesRaw[y * 512 + x] != 0;
                        holes[y, x] = !hole;
                        anyHole |= hole;
                    }
                if (anyHole) td.SetHoles(0, 0, holes);
                td.terrainLayers = layers;
                var am = new float[amRes, amRes, 8];
                float wx0 = tile.x0 + 4000f, wz0 = tile.z0 + 3000f;
                for (int y = 0; y < amRes; y++)
                    for (int x = 0; x < amRes; x++)
                    {
                        float wx = wx0 + (x + 0.5f) / amRes * meta.tileSize, wz = wz0 + (y + 0.5f) / amRes * meta.tileSize;
                        int px = Mathf.Clamp((int)(wx / 8000f * sw), 0, sw - 1), py = Mathf.Clamp((int)(wz / 6000f * sh), 0, sh - 1);
                        var a = c0[py * sw + px];
                        var b = c1[py * sw + px];
                        float[] w = { a.r, a.g, a.b, a.a, b.r, b.g, b.b, b.a };
                        float sum = 0f;
                        foreach (var v in w) sum += v;
                        // The source splat maps are almost empty, so land cover is derived from the terrain itself:
                        // rock on steep slopes, sand at the shoreline, forest floor up the mountains, grass/lawn/dirt mixed by noise.
                        float nu = (x + 0.5f) / amRes, nv = (y + 0.5f) / amRes;
                        float steep = td.GetSteepness(nu, nv);
                        float hgt = td.GetInterpolatedHeight(nu, nv) + meta.heightOffset;
                        float n1 = Mathf.PerlinNoise(wx * 0.012f, wz * 0.012f), n2 = Mathf.PerlinNoise(wx * 0.05f + 31f, wz * 0.05f);
                        var pw = new float[8];
                        pw[2] = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(26f, 40f, steep)) * 3f;              // rock
                        pw[3] = hgt < 3f && hgt > -8f ? Mathf.InverseLerp(3f, 0.8f, hgt) * 3f : 0f;               // sand
                        pw[1] = Mathf.InverseLerp(55f, 140f, hgt) * (0.6f + n1);                                  // forest floor
                        pw[0] = 0.8f + n1 * 0.6f;                                                                 // grass
                        pw[7] = Mathf.Clamp01(n2 - 0.45f) * 1.5f * Mathf.InverseLerp(120f, 20f, hgt);            // lawn (lower land)
                        pw[5] = Mathf.Clamp01(0.55f - n2) * 0.9f + Mathf.InverseLerp(18f, 30f, steep) * 0.6f;    // dirt patches, slope edges
                        float psum = 0f;
                        for (int l = 0; l < 8; l++) { pw[l] += w[l] / 255f * 4f; psum += pw[l]; }
                        for (int l = 0; l < 8; l++) am[y, x, l] = pw[l] / psum;
                    }
                td.SetAlphamaps(0, 0, am);
                EditorUtility.SetDirty(td);
                var go = Terrain.CreateTerrainGameObject(td);
                go.name = "Terrain_" + tile.name;
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(tile.x0, meta.heightOffset, tile.z0);
                var t = go.GetComponent<Terrain>();
                t.materialTemplate = terrainMat;
                t.drawInstanced = true;
                t.heightmapPixelError = 4f;
                t.basemapDistance = 450f;
                t.groupingID = 7;
                t.allowAutoConnect = true;
                t.drawTreesAndFoliage = false;
                t.shadowCastingMode = ShadowCastingMode.TwoSided;
                go.AddComponent<VehicleSurface>().Surface = db.Grass;
                go.isStatic = true;
                terrains[tile.name] = t;
            }
            AssetDatabase.SaveAssets();
            return root;
        }

        static TerrainLayer[] BuildTerrainLayers()
        {
            (string name, Color c, float tile, int freq, float contrast, float smooth)[] spec =
            {
                ("Grass", new Color(0.27f, 0.33f, 0.16f), 9f, 16, 0.22f, 0.05f),
                ("ForestFloor", new Color(0.18f, 0.16f, 0.11f), 7f, 16, 0.25f, 0.05f),
                ("Rock", new Color(0.4f, 0.39f, 0.37f), 14f, 8, 0.3f, 0.15f),
                ("Sand", new Color(0.72f, 0.65f, 0.5f), 10f, 32, 0.1f, 0.1f),
                ("Paving", new Color(0.42f, 0.42f, 0.41f), 6f, 32, 0.12f, 0.2f),
                ("Dirt", new Color(0.36f, 0.29f, 0.21f), 8f, 16, 0.2f, 0.05f),
                ("Farmland", new Color(0.38f, 0.33f, 0.2f), 12f, 8, 0.25f, 0.05f),
                ("Lawn", new Color(0.24f, 0.36f, 0.15f), 6f, 32, 0.15f, 0.08f),
            };
            var layers = new TerrainLayer[spec.Length];
            for (int i = 0; i < spec.Length; i++)
            {
                var s = spec[i];
                string path = $"{Gen}/Terrain/L_{s.name}.terrainlayer";
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (layer == null) { layer = new TerrainLayer(); AssetDatabase.CreateAsset(layer, path); }
                layer.diffuseTexture = ProcTextures.Plain("Terrain_" + s.name, s.c, 100 + i * 7, s.freq, s.contrast);
                layer.tileSize = new Vector2(s.tile, s.tile);
                layer.smoothness = s.smooth;
                layer.smoothnessSource = TerrainLayerSmoothnessSource.ConstantOnly; // never read smoothness from diffuse alpha (it is 1 = mirror)
                layer.metallic = 0f;
                EditorUtility.SetDirty(layer);
                layers[i] = layer;
            }
            return layers;
        }

        // ------------------------------------------------------------------------------- meshes
        enum Group { Road, Sidewalk, Structure, Building, Decal, Light, Water }

        static Group GroupOf(string mat)
        {
            switch (mat)
            {
                case "asphalt": case "asphalt_worn": case "concrete_dark": case "gravel": return Group.Road;
                case "sidewalk": case "kerb": return Group.Sidewalk;
                case "marking_white": case "marking_yellow": return Group.Decal;
                case "light_warm": case "sodium": case "neon_cyan": case "neon_magenta": case "marker_red": return Group.Light;
                case "water": return Group.Water;
                case "concrete": case "steel": case "steel_painted_red": case "wood": return Group.Structure;
                default: return Group.Building;
            }
        }

        sealed class Builder
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<int> T = new List<int>();
            public readonly Dictionary<int, int> Remap = new Dictionary<int, int>();
        }

        static string BuildMeshes(Transform worldRoot, Dictionary<string, Material> mats, GameDatabase db)
        {
            var root = new GameObject("CityMeshes").transform;
            root.SetParent(worldRoot, false);
            foreach (var old in Directory.GetFiles(Gen + "/Meshes", "*.asset")) AssetDatabase.DeleteAsset(old.Replace('\\', '/'));
            // Card C2: the meshes are rebuilt from the blockout, so the eased-mesh marker of RoadProfileApply no longer applies;
            // Build() eases the new meshes once the props exist.
            if (File.Exists(RoadProfileApply.Marker)) File.Delete(RoadProfileApply.Marker);
            RoadProfileEaser.Reset();
            int meshCount = 0, triCount = 0, colliderCount = 0, cutKerbs = 0, cutRoads = 0, cutStructs = 0;
            WorldData.EnsureLoaded();
            // key: material|cellX|cellZ
            var visual = new Dictionary<string, Builder>();
            var collide = new Dictionary<string, Builder>(); // key: surface|cellX|cellZ
            foreach (var file in Directory.GetFiles(Path.Combine(SourceDir, "meshes"), "*.vmesh").OrderBy(f => f))
            {
                using (var br = new BinaryReader(File.OpenRead(file)))
                {
                    if (new string(br.ReadChars(4)) != "VMSH") throw new Exception("bad vmesh " + file);
                    br.ReadUInt32();
                    uint prims = br.ReadUInt32();
                    for (int p = 0; p < prims; p++)
                    {
                        string node = new string(br.ReadChars((int)br.ReadUInt32()));
                        string mat = new string(br.ReadChars((int)br.ReadUInt32()));
                        uint vc = br.ReadUInt32(), ic = br.ReadUInt32(), flags = br.ReadUInt32();
                        var pos = new Vector3[vc];
                        for (int i = 0; i < vc; i++) pos[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        Vector3[] nrm = null;
                        if ((flags & 1) != 0) { nrm = new Vector3[vc]; for (int i = 0; i < vc; i++) nrm[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()); }
                        if ((flags & 2) != 0) for (int i = 0; i < vc; i++) { br.ReadSingle(); br.ReadSingle(); }
                        var idx = new int[ic];
                        for (int i = 0; i < ic; i++) idx[i] = (int)br.ReadUInt32();
                        if (!mats.ContainsKey(mat)) mat = "concrete";
                        var group = GroupOf(mat);
                        if (group == Group.Water) continue; // water handled separately (sea plane + river)
                        if (group == Group.Building) continue; // buildings come from world_building_placements.csv
                        float lift = group == Group.Decal ? 0.035f : group == Group.Road ? 0.012f : 0f;
                        string surf = group == Group.Road ? (mat == "gravel" ? "gravel" : mat == "concrete_dark" ? "concrete" : "asphalt") : group == Group.Sidewalk || group == Group.Structure || group == Group.Building ? "concrete" : null;
                        for (int t = 0; t + 2 < idx.Length; t += 3)
                        {
                            var a = pos[idx[t]]; var b = pos[idx[t + 1]]; var c = pos[idx[t + 2]];
                            var centre = (a + b + c) / 3f;
                            if (group == Group.Sidewalk && OnCarriageway(centre)) { cutKerbs++; continue; }
                            if (group == Group.Road && LosesJunction(centre)) { cutRoads++; continue; }
                            if (group == Group.Structure && StructureInLane(a, b, c, centre)) { cutStructs++; continue; }
                            int cx = Mathf.FloorToInt(centre.x / Cell), cz = Mathf.FloorToInt(centre.z / Cell);
                            var faceN = Vector3.Cross(b - a, c - a);
                            string visMat = group == Group.Sidewalk && faceN.sqrMagnitude > 1e-10f && Mathf.Abs(faceN.normalized.y) < 0.6f ? "kerb" : mat;
                            string key = $"{visMat}|{cx}|{cz}";
                            if (!visual.TryGetValue(key, out var vb)) visual[key] = vb = new Builder();
                            var fn = Vector3.Cross(b - a, c - a);
                            fn = fn.sqrMagnitude > 1e-10f ? fn.normalized : Vector3.up;
                            for (int k = 0; k < 3; k++)
                            {
                                int src = idx[t + k];
                                var v = pos[src] + Vector3.up * lift;
                                var n = nrm != null ? nrm[src] : fn;
                                vb.V.Add(v);
                                vb.N.Add(n);
                                vb.UV.Add(ComputeUV(v, fn, group));
                                vb.T.Add(vb.V.Count - 1);
                            }
                            if (surf != null)
                            {
                                string ck = $"{surf}|{cx}|{cz}";
                                if (!collide.TryGetValue(ck, out var cb)) collide[ck] = cb = new Builder();
                                for (int k = 0; k < 3; k++)
                                {
                                    int src = idx[t + k];
                                    cb.V.Add(pos[src] + Vector3.up * lift);
                                    cb.T.Add(cb.V.Count - 1);
                                }
                            }
                        }
                    }
                }
            }
            var cellRoots = new Dictionary<string, Transform>();
            Transform CellRoot(string cx, string cz)
            {
                string k = cx + "_" + cz;
                if (cellRoots.TryGetValue(k, out var tr)) return tr;
                tr = new GameObject("Cell_" + k).transform;
                tr.SetParent(root, false);
                tr.gameObject.isStatic = true;
                cellRoots[k] = tr;
                return tr;
            }
            foreach (var kv in visual)
            {
                var parts = kv.Key.Split('|');
                var b = kv.Value;
                var mesh = new Mesh { name = $"M_{parts[0]}_{parts[1]}_{parts[2]}", indexFormat = b.V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                WeldAndSet(mesh, b);
                AssetDatabase.CreateAsset(mesh, $"{Gen}/Meshes/{mesh.name}.asset");
                var go = new GameObject(parts[0]);
                go.transform.SetParent(CellRoot(parts[1], parts[2]), false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mats[parts[0]];
                var grp = GroupOf(parts[0]);
                r.shadowCastingMode = grp == Group.Decal || grp == Group.Road ? ShadowCastingMode.Off : ShadowCastingMode.On;
                go.isStatic = true;
                if (grp == Group.Decal || grp == Group.Light)
                {
                    var lod = go.AddComponent<LODGroup>();
                    lod.SetLODs(new[] { new LOD(0.004f, new Renderer[] { r }) });
                }
                meshCount++;
                triCount += b.T.Count / 3;
            }
            foreach (var kv in collide)
            {
                var parts = kv.Key.Split('|');
                var b = kv.Value;
                var mesh = new Mesh { name = $"C_{parts[0]}_{parts[1]}_{parts[2]}", indexFormat = b.V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                WeldAndSet(mesh, b, false);
                AssetDatabase.CreateAsset(mesh, $"{Gen}/Meshes/{mesh.name}.asset");
                var go = new GameObject("Collider_" + parts[0]);
                go.transform.SetParent(CellRoot(parts[1], parts[2]), false);
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                go.AddComponent<VehicleSurface>().Surface = parts[0] == "gravel" ? db.Gravel : parts[0] == "concrete" ? db.Concrete : db.DryAsphalt;
                go.isStatic = true;
                colliderCount++;
            }
            AssetDatabase.SaveAssets();
            return $"{meshCount} render meshes ({triCount:N0} tris), {colliderCount} collider meshes, {cutKerbs} sidewalk/kerb, {cutRoads} overlapping road and {cutStructs} in-lane structure triangles cut";
        }

        /// <summary>True when a point lies on a road's driving surface (the blockout runs cross-street sidewalks over junctions).</summary>
        static bool OnCarriageway(Vector3 p)
        {
            if (!WorldData.NearestRoad(p, 25f, out var hit)) return false;
            return hit.Distance < hit.RoadPath.HalfWidth - 0.35f && Mathf.Abs(p.y - hit.Point.y) < 1.5f;
        }

        static readonly List<WorldData.RoadHit> hits = new List<WorldData.RoadHit>();

        /// <summary>
        /// Where carriageways overlap (junctions and the blockout's flattened interchanges), keep only the surface
        /// that matches the highest-class road; a lower road's surface sitting above or below it becomes a wall.
        /// </summary>
        static bool LosesJunction(Vector3 p)
        {
            WorldData.RoadsAt(p, 0.3f, 3.5f, hits);
            if (hits.Count < 2) return false;
            var best = hits[0];
            foreach (var h in hits)
                if (WorldData.Rank(h.RoadPath) > WorldData.Rank(best.RoadPath) || (WorldData.Rank(h.RoadPath) == WorldData.Rank(best.RoadPath) && h.Distance < best.Distance)) best = h;
            return Mathf.Abs(p.y - best.Point.y) > 0.3f;
        }

        /// <summary>Structure pieces standing inside a lane (not median barriers of divided roads, not bridge decks).</summary>
        static bool StructureInLane(Vector3 a, Vector3 b, Vector3 c, Vector3 centre)
        {
            WorldData.RoadsAt(centre, 0.8f, 6f, hits);
            foreach (var h in hits)
            {
                float top = Mathf.Max(a.y, Mathf.Max(b.y, c.y)) - h.Point.y;
                float bottom = Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - h.Point.y;
                if (bottom < -0.5f || top < 0.25f || bottom > 2.2f) continue; // decks, piers below, flush pieces, ceilings and gantries above
                if (h.RoadPath.Divided && h.Distance < 1.2f) continue; // central barrier stays
                return true;
            }
            return false;
        }

        static Vector2 ComputeUV(Vector3 v, Vector3 n, Group g)
        {
            if (g == Group.Building)
            {
                if (Mathf.Abs(n.y) > 0.7f) return new Vector2(0.02f, 0.05f); // roofs/tops: plain wall area of the facade texture
                var axis = Vector3.Cross(Vector3.up, n).normalized;
                float u = Vector3.Dot(v, axis);
                return new Vector2(u / 12f, v.y / 14f);
            }
            if (g == Group.Structure)
            {
                if (Mathf.Abs(n.y) > 0.7f) return new Vector2(v.x, v.z);
                var axis = Vector3.Cross(Vector3.up, n).normalized;
                return new Vector2(Vector3.Dot(v, axis), v.y);
            }
            return new Vector2(v.x, v.z);
        }

        /// <summary>Welds identical vertices (position + normal + uv) to shrink meshes, then uploads.</summary>
        static void WeldAndSet(Mesh mesh, Builder b, bool render = true)
        {
            var map = new Dictionary<(int, int, int, int, int), int>();
            var v = new List<Vector3>(b.V.Count / 2);
            var n = new List<Vector3>(b.V.Count / 2);
            var uv = new List<Vector2>(b.V.Count / 2);
            var t = new List<int>(b.T.Count);
            for (int i = 0; i < b.V.Count; i++)
            {
                var p = b.V[i];
                int nx = render ? Mathf.RoundToInt(b.N[i].x * 50f) : 0, ny = render ? Mathf.RoundToInt(b.N[i].y * 50f) * 101 + (render ? Mathf.RoundToInt(b.N[i].z * 50f) : 0) : 0;
                var key = (Mathf.RoundToInt(p.x * 200f), Mathf.RoundToInt(p.y * 200f), Mathf.RoundToInt(p.z * 200f), nx, ny + (render ? Mathf.RoundToInt(b.UV[i].x * 97f) * 1000 : 0));
                if (!map.TryGetValue(key, out int idx))
                {
                    idx = v.Count;
                    map[key] = idx;
                    v.Add(p);
                    if (render) { n.Add(b.N[i]); uv.Add(b.UV[i]); }
                }
                t.Add(idx);
            }
            mesh.indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(v);
            if (render) { mesh.SetNormals(n); mesh.SetUVs(0, uv); }
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            if (render) mesh.RecalculateTangents();
        }

        // ------------------------------------------------------------------------------- water
        static void BuildWater(Transform worldRoot, Dictionary<string, Material> mats)
        {
            var seaMesh = new Mesh { name = "SeaPlane" };
            const int SN = 24;
            var sv = new List<Vector3>(); var suv = new List<Vector2>(); var st = new List<int>();
            for (int z = 0; z <= SN; z++)
                for (int x = 0; x <= SN; x++)
                {
                    var p = new Vector3(-12000f + x * 24000f / SN, 0f, -10000f + z * 20000f / SN);
                    sv.Add(p); suv.Add(new Vector2(p.x, p.z));
                }
            for (int z = 0; z < SN; z++)
                for (int x = 0; x < SN; x++)
                {
                    int i = z * (SN + 1) + x;
                    st.Add(i); st.Add(i + SN + 1); st.Add(i + 1);
                    st.Add(i + 1); st.Add(i + SN + 1); st.Add(i + SN + 2);
                }
            seaMesh.SetVertices(sv); seaMesh.SetUVs(0, suv); seaMesh.SetTriangles(st, 0);
            seaMesh.RecalculateNormals(); seaMesh.RecalculateTangents(); seaMesh.RecalculateBounds();
            AssetDatabase.CreateAsset(seaMesh, $"{Gen}/Meshes/SeaPlane.asset");
            var sea = new GameObject("Sea");
            sea.transform.SetParent(worldRoot, false);
            sea.transform.position = new Vector3(0f, -0.15f, 0f);
            sea.AddComponent<MeshFilter>().sharedMesh = seaMesh;
            var r = sea.AddComponent<MeshRenderer>();
            r.sharedMaterial = mats["water"];
            r.shadowCastingMode = ShadowCastingMode.Off;
            sea.isStatic = true;
            // River and lake surfaces from the district GLBs.
            var root = new GameObject("InlandWater").transform;
            root.SetParent(worldRoot, false);
            var lake = MirrorLake.LoadOrNull(); // BUG-057: Mirror Lake from lake_polygon at the outflow's elevation
            foreach (var file in Directory.GetFiles(Path.Combine(SourceDir, "meshes"), "*.vmesh").OrderBy(f => f))
            {
                using (var br = new BinaryReader(File.OpenRead(file)))
                {
                    br.ReadChars(4); br.ReadUInt32();
                    uint prims = br.ReadUInt32();
                    for (int p = 0; p < prims; p++)
                    {
                        new string(br.ReadChars((int)br.ReadUInt32()));
                        string mat = new string(br.ReadChars((int)br.ReadUInt32()));
                        uint vc = br.ReadUInt32(), ic = br.ReadUInt32(), flags = br.ReadUInt32();
                        var pos = new Vector3[vc];
                        for (int i = 0; i < vc; i++) pos[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        if ((flags & 1) != 0) br.ReadBytes((int)vc * 12);
                        if ((flags & 2) != 0) br.ReadBytes((int)vc * 8);
                        var idx = new int[ic];
                        for (int i = 0; i < ic; i++) idx[i] = (int)br.ReadUInt32();
                        if (mat != "water" || vc < 3) continue;
                        bool inland = false;
                        foreach (var v in pos) if (v.y > 0.6f) { inland = true; break; }
                        if (!inland) continue;
                        var verts = pos.Select(v => v + Vector3.up * MirrorLake.WaterLift).ToList();
                        var uvs = pos.Select(v => new Vector2(v.x, v.z)).ToList();
                        var tris = idx.ToList();
                        MirrorLake.Patch(verts, uvs, tris, lake); // only the D8 primitive holds the package's lake rectangle
                        var mesh = new Mesh { name = "Water_" + Path.GetFileNameWithoutExtension(file) + "_" + p, indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                        mesh.SetVertices(verts);
                        mesh.SetUVs(0, uvs);
                        mesh.SetTriangles(tris, 0);
                        mesh.RecalculateNormals();
                        mesh.RecalculateTangents();
                        AssetDatabase.CreateAsset(mesh, $"{Gen}/Meshes/{mesh.name}.asset");
                        var go = new GameObject(mesh.name);
                        go.transform.SetParent(root, false);
                        go.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var mr = go.AddComponent<MeshRenderer>();
                        mr.sharedMaterial = mats["water"];
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                        go.isStatic = true;
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------- props
        static int BuildProps(Transform worldRoot, Dictionary<string, Material> mats)
        {
            var go = new GameObject("Props");
            go.transform.SetParent(worldRoot, false);
            var ipr = go.AddComponent<InstancedPropRenderer>();
            var protos = new List<InstancedPropRenderer.Prototype>();
            var index = new Dictionary<string, int>();
            void Proto(string id, Mesh m, Material[] mm, Mesh low, Material[] lowM, float lod, float draw, float shadow, bool lamp = false, Vector3 lightOff = default)
            {
                index[id] = protos.Count;
                protos.Add(new InstancedPropRenderer.Prototype { Name = id, Mesh = m, Materials = mm, LowMesh = low, LowMaterials = lowM, LodDistance = lod, DrawDistance = draw, ShadowDistance = shadow, IsLamp = lamp, LightOffset = lightOff });
            }
            var bark = mats["bark"]; var pine = mats["pine"]; var leaf = mats["leaf"]; var palm = mats["palm"]; var pole = mats["lamp_pole"]; var head = mats["lamp_head"]; var sod = mats["lamp_sodium"];
            Proto("pine_tree_14m", PropMeshes.Pine(14f, 8, 3), new[] { bark, pine }, PropMeshes.Pine(14f, 5, 1), new[] { bark, pine }, 90f, 1400f, 160f);
            Proto("deciduous_tree_10m", PropMeshes.Deciduous(10f, 2), new[] { bark, leaf }, PropMeshes.Deciduous(10f, 1), new[] { bark, leaf }, 90f, 1100f, 140f);
            Proto("bush_2m", PropMeshes.Bush(2f), new[] { leaf }, null, null, 60f, 300f, 40f);
            Proto("palm_tree_12m", PropMeshes.Palm(12f), new[] { bark, palm }, null, null, 120f, 900f, 120f);
            Proto("streetlamp_urban_led_10m", PropMeshes.Lamp(10f, 1.8f, 1), new[] { pole, head }, null, null, 150f, 700f, 60f, true, new Vector3(0f, 9.8f, 1.6f));
            Proto("streetlamp_sodium_highmast_25m", PropMeshes.Lamp(25f, 1.4f, 3), new[] { pole, sod }, null, null, 250f, 1400f, 80f, true, new Vector3(0f, 24.6f, 1.2f));
            Proto("streetlamp_coastal_6m", PropMeshes.Lamp(6f, 0.6f, 1), new[] { pole, head }, null, null, 120f, 500f, 50f, true, new Vector3(0f, 5.8f, 0.5f));
            var cells = new Dictionary<(int, int, int), (List<Vector4> p, List<float> s)>();
            int total = 0;
            var ci = CultureInfo.InvariantCulture;
            foreach (var file in Directory.GetFiles(Path.Combine(SourceDir, "placement"), "*.csv"))
            {
                var lines = File.ReadAllLines(file);
                if (lines.Length < 2) continue;
                var header = lines[0].Split(',');
                int iType = 0, iX = Array.IndexOf(header, "unity_x"), iY = Array.IndexOf(header, "unity_y"), iZ = Array.IndexOf(header, "unity_z");
                int iH = Array.IndexOf(header, "heading_deg"), iS = Array.IndexOf(header, "scale");
                for (int l = 1; l < lines.Length; l++)
                {
                    var f = lines[l].Split(',');
                    if (f.Length <= iZ || !index.TryGetValue(f[iType], out int proto)) continue;
                    float x = float.Parse(f[iX], ci), y = float.Parse(f[iY], ci), z = float.Parse(f[iZ], ci);
                    float yaw = iH >= 0 ? float.Parse(f[iH], ci) : (Mathf.Abs(x * 13.37f + z * 7.11f) % 360f);
                    float sc = iS >= 0 ? float.Parse(f[iS], ci) : 1f;
                    var key = (proto, Mathf.FloorToInt(x / Cell), Mathf.FloorToInt(z / Cell));
                    if (!cells.TryGetValue(key, out var c)) cells[key] = c = (new List<Vector4>(), new List<float>());
                    c.p.Add(new Vector4(x, y - 0.1f, z, yaw));
                    c.s.Add(sc);
                    total++;
                }
            }
            var outCells = new List<InstancedPropRenderer.Cell>();
            foreach (var kv in cells)
            {
                var (p, s) = kv.Value;
                for (int start = 0; start < p.Count; start += 1000)
                {
                    int n = Mathf.Min(1000, p.Count - start);
                    var pl = p.GetRange(start, n).ToArray();
                    var sl = s.GetRange(start, n).ToArray();
                    var b = new Bounds(new Vector3(pl[0].x, pl[0].y, pl[0].z), Vector3.one);
                    foreach (var q in pl) b.Encapsulate(new Vector3(q.x, q.y, q.z));
                    b.Expand(new Vector3(16f, 40f, 16f));
                    outCells.Add(new InstancedPropRenderer.Cell { Prototype = kv.Key.Item1, Bounds = b, Placements = pl, Scales = sl });
                }
            }
            ipr.Prototypes = protos.ToArray();
            ipr.Cells = outCells.ToArray();
            return total;
        }
    }

    /// <summary>Low-poly procedural meshes for instanced vegetation and street lamps (two submeshes: trunk/pole, foliage/head).</summary>
    public static class PropMeshes
    {
        const string Dir = "Assets/_Veyra/World/Generated/Meshes/Props";

        static Mesh Save(Mesh m)
        {
            Directory.CreateDirectory(Dir);
            string path = $"{Dir}/{m.name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        sealed class MB
        {
            public List<Vector3> V = new List<Vector3>();
            public List<Vector3> N = new List<Vector3>();
            public List<int>[] T = { new List<int>(), new List<int>() };

            public void Cylinder(int sub, Vector3 a, Vector3 b, float ra, float rb, int seg, bool cap = false)
            {
                var axis = (b - a).normalized;
                var side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                var up2 = Vector3.Cross(side, axis);
                int baseI = V.Count;
                for (int i = 0; i <= seg; i++)
                {
                    float ang = i / (float)seg * Mathf.PI * 2f;
                    var dir = side * Mathf.Cos(ang) + up2 * Mathf.Sin(ang);
                    V.Add(a + dir * ra); N.Add(dir);
                    V.Add(b + dir * rb); N.Add(dir);
                }
                for (int i = 0; i < seg; i++)
                {
                    int k = baseI + i * 2;
                    T[sub].Add(k); T[sub].Add(k + 1); T[sub].Add(k + 2);
                    T[sub].Add(k + 1); T[sub].Add(k + 3); T[sub].Add(k + 2);
                }
                if (cap && rb > 0.001f)
                {
                    int c = V.Count;
                    V.Add(b); N.Add(axis);
                    for (int i = 0; i <= seg; i++) { V.Add(V[baseI + i * 2 + 1]); N.Add(axis); }
                    for (int i = 0; i < seg; i++) { T[sub].Add(c); T[sub].Add(c + 1 + i + 1); T[sub].Add(c + 1 + i); }
                }
            }

            public void Blob(int sub, Vector3 c, Vector3 r, int rings, int seg, int seed)
            {
                int baseI = V.Count;
                for (int y = 0; y <= rings; y++)
                {
                    float phi = y / (float)rings * Mathf.PI;
                    for (int x = 0; x <= seg; x++)
                    {
                        float th = x / (float)seg * Mathf.PI * 2f;
                        var d = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                        float j = 0.85f + 0.3f * Mathf.PerlinNoise(x * 0.9f + seed, y * 0.9f + seed * 0.3f);
                        V.Add(c + Vector3.Scale(d, r) * j);
                        N.Add(d);
                    }
                }
                for (int y = 0; y < rings; y++)
                    for (int x = 0; x < seg; x++)
                    {
                        int a = baseI + y * (seg + 1) + x, b = a + seg + 1;
                        T[sub].Add(a); T[sub].Add(a + 1); T[sub].Add(b);
                        T[sub].Add(a + 1); T[sub].Add(b + 1); T[sub].Add(b);
                    }
            }

            public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int i = V.Count;
                var n = Vector3.Cross(b - a, d - a).normalized;
                V.Add(a); V.Add(b); V.Add(c); V.Add(d);
                N.Add(n); N.Add(n); N.Add(n); N.Add(n);
                T[sub].Add(i); T[sub].Add(i + 1); T[sub].Add(i + 2);
                T[sub].Add(i); T[sub].Add(i + 2); T[sub].Add(i + 3);
            }

            public Mesh Build(string name, int subs = 2)
            {
                var m = new Mesh { name = name };
                m.SetVertices(V);
                m.SetNormals(N);
                m.subMeshCount = subs;
                for (int s = 0; s < subs; s++) m.SetTriangles(T[s], s);
                m.RecalculateBounds();
                return Save(m);
            }
        }

        public static Mesh Pine(float h, int seg, int tiers)
        {
            var b = new MB();
            b.Cylinder(0, Vector3.zero, new Vector3(0, h * 0.35f, 0), 0.22f, 0.14f, Mathf.Max(4, seg / 2));
            for (int t = 0; t < tiers; t++)
            {
                float y0 = h * (0.18f + t * 0.24f), y1 = h * (tiers == 1 ? 1f : 0.62f + t * 0.17f);
                float r = h * 0.24f * (1f - t * 0.22f);
                b.Cylinder(1, new Vector3(0, y0, 0), new Vector3(0, y1, 0), r, 0.02f, seg);
            }
            return b.Build($"Pine_{seg}_{tiers}");
        }

        public static Mesh Deciduous(float h, int detail)
        {
            var b = new MB();
            b.Cylinder(0, Vector3.zero, new Vector3(0, h * 0.5f, 0), 0.25f, 0.16f, 6);
            int rings = detail > 1 ? 6 : 4, seg = detail > 1 ? 9 : 6;
            b.Blob(1, new Vector3(0, h * 0.62f, 0), new Vector3(h * 0.3f, h * 0.24f, h * 0.3f), rings, seg, 3);
            if (detail > 1)
            {
                b.Blob(1, new Vector3(h * 0.15f, h * 0.78f, h * 0.05f), new Vector3(h * 0.2f, h * 0.17f, h * 0.2f), 5, 8, 7);
                b.Blob(1, new Vector3(-h * 0.12f, h * 0.74f, -h * 0.1f), new Vector3(h * 0.19f, h * 0.16f, h * 0.19f), 5, 8, 11);
            }
            return b.Build($"Deciduous_{detail}");
        }

        public static Mesh Bush(float h)
        {
            var b = new MB();
            b.Blob(0, new Vector3(0, h * 0.4f, 0), new Vector3(h * 0.6f, h * 0.45f, h * 0.6f), 4, 7, 5);
            return b.Build("Bush", 1);
        }

        public static Mesh Palm(float h)
        {
            var b = new MB();
            Vector3 prev = Vector3.zero;
            for (int i = 1; i <= 6; i++)
            {
                float t = i / 6f;
                var p = new Vector3(Mathf.Sin(t * 1.2f) * h * 0.08f, h * t, 0f);
                b.Cylinder(0, prev, p, 0.24f - t * 0.08f, 0.24f - (t + 0.16f) * 0.08f, 6);
                prev = p;
            }
            for (int f = 0; f < 8; f++)
            {
                float a = f / 8f * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var side = Vector3.Cross(dir, Vector3.up) * 0.45f;
                var tip = prev + dir * h * 0.38f + Vector3.down * h * 0.12f;
                var mid = prev + dir * h * 0.2f + Vector3.up * 0.3f;
                b.Quad(1, prev - side * 0.3f, mid - side, mid + side, prev + side * 0.3f);
                b.Quad(1, mid - side, tip, tip, mid + side);
            }
            return b.Build("Palm");
        }

        public static Mesh Lamp(float h, float arm, int heads)
        {
            var b = new MB();
            b.Cylinder(0, Vector3.zero, new Vector3(0, h, 0), h > 15f ? 0.35f : 0.12f, h > 15f ? 0.18f : 0.08f, 8, true);
            for (int i = 0; i < heads; i++)
            {
                float a = heads == 1 ? 0f : i / (float)heads * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var top = new Vector3(0, h - 0.2f, 0);
                var end = top + dir * arm;
                b.Cylinder(0, top, end, 0.06f, 0.05f, 6);
                var hc = end + Vector3.down * 0.12f;
                var s = Vector3.Cross(dir, Vector3.up).normalized;
                float w = h > 15f ? 0.6f : 0.32f, l = h > 15f ? 0.9f : 0.7f;
                b.Quad(1, hc - s * w - dir * l * 0.5f, hc - s * w + dir * l * 0.5f, hc + s * w + dir * l * 0.5f, hc + s * w - dir * l * 0.5f);
                b.Quad(1, hc + s * w - dir * l * 0.5f, hc + s * w + dir * l * 0.5f, hc - s * w + dir * l * 0.5f, hc - s * w - dir * l * 0.5f);
            }
            return b.Build($"Lamp_{h}_{heads}");
        }
    }
}
