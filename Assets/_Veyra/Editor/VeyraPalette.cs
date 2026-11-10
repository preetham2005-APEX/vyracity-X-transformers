using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VeyraCity.World;
using static VeyraCity.EditorTools.PaletteTextures;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card F2: the production material palette. Every material is created here (idempotent), registered in the
    /// VeyraMaterialLibrary, validated (albedo ranges) and swapped in for the blockout material it replaces, in
    /// VeyraCity.unity and in CityBuilder's material table. Menu Veyra/Materials/Palette/*.
    /// </summary>
    public static class VeyraPalette
    {
        public const string MatDir = VeyraMaterialFactory.Dir;
        public const int TexSize = 2048;
        const string RoadShader = "Veyra/Road";
        const string FacadeShader = "Veyra/Facade";
        const string NeonShader = "Veyra/Neon";
        const string InteriorAtlasName = "Pal_InteriorAtlas";

        public sealed class Spec
        {
            public VeyraMat Key;
            public string Name, Shader, Part;
            public string TexSet;                       // texture set name (Pal_<TexSet>_A/_N/_M)
            public Func<float, float, Sample> Recipe;
            public float TileMeters = 4f;
            public float UvMeters = 1f;                 // metres per mesh UV unit (roads 1; building tops 8)
            public float NormalStrength = 6f;
            public Color BaseColor = Color.white;       // sRGB colour multiplied with the albedo
            public float MinAlbedo = 0.02f, MaxAlbedo = 0.9f;
            public string[] Replaces = Array.Empty<string>();   // blockout material asset names this one replaces
            public string[] CityKeys = Array.Empty<string>();   // CityBuilder material keys it serves
            public Action<Material> Configure;
            public string DetailSet;                    // optional overlay set (albedo + alpha)
            public Func<float, float, Sample> DetailRecipe;
        }

        static void Road(Material m, float wet, float puddles, float macro = 0.08f)
        {
            m.SetFloat("_WetnessResponse", wet);
            m.SetFloat("_PuddleAmount", puddles);
            m.SetFloat("_MacroVariation", macro);
            m.SetFloat("_MacroScale1", 23f);
            m.SetFloat("_MacroScale2", 4.5f);
        }

        /// <summary>Facade set-up: window rect (x width, y sill, z height, w frame / mullion), glass, night lighting, wall tiling.</summary>
        static void Facade(Material m, Vector4 window, bool curtain, Color glass, float litMin, float litMax, bool office, float grime, float seed,
                           Vector2 wallTiling, float bays = 4f, float floors = 4f, float roomDepth = 1.1f, float emission = 2.2f, float interiorDim = 0.32f)
        {
            m.SetVector("_WindowRect", window);
            m.SetFloat("_CurtainWall", curtain ? 1f : 0f);
            m.SetColor("_GlassColor", glass);
            m.SetFloat("_GlassSmoothness", curtain ? 0.95f : 0.92f);
            m.SetFloat("_LitFractionMin", litMin);
            m.SetFloat("_LitFractionMax", litMax);
            m.SetFloat("_Office", office ? 1f : 0f);
            m.SetFloat("_Grime", grime);
            m.SetFloat("_Seed", seed);
            m.SetVector("_WallTiling", new Vector4(wallTiling.x, wallTiling.y, 0f, 0f));
            m.SetFloat("_BaysPerTile", bays);
            m.SetFloat("_FloorsPerTile", floors);
            m.SetFloat("_RoomDepth", roomDepth);
            m.SetFloat("_WindowEmission", emission);
            m.SetFloat("_InteriorDim", interiorDim);
        }

        // Wall textures cover ~3 m; a building UV tile is 12 m x 14 m (BuildingGenerator U, V).
        static readonly Vector2 Wall3m = new Vector2(12f / 3f, 14f / 3f);
        static readonly Vector2 WallBrick = new Vector2(12f / 2.925f, 14f / 3f);

        public static readonly Spec[] Specs =
        {
            // ---------------- F2a roads and sidewalks
            new Spec { Key = VeyraMat.RoadAsphaltNew, Name = "M_Asphalt_New", Shader = RoadShader, Part = "F2a", TexSet = "AsphaltNew", Recipe = PaletteRecipes.AsphaltNew, TileMeters = 4f, NormalStrength = 5f,
                       MinAlbedo = 0.05f, MaxAlbedo = 0.12f, Replaces = new[] { "Road_Asphalt" }, CityKeys = new[] { "asphalt" }, Configure = m => Road(m, 1f, 0.6f) },
            new Spec { Key = VeyraMat.RoadAsphaltWorn, Name = "M_Asphalt_Worn", Shader = RoadShader, Part = "F2a", TexSet = "AsphaltWorn", Recipe = PaletteRecipes.AsphaltWorn, TileMeters = 4f, NormalStrength = 6f,
                       MinAlbedo = 0.05f, MaxAlbedo = 0.12f, Replaces = new[] { "Road_AsphaltWorn" }, CityKeys = new[] { "asphalt_worn" }, Configure = m => Road(m, 1f, 0.75f) },
            new Spec { Key = VeyraMat.RoadAsphaltPatched, Name = "M_Asphalt_Patched", Shader = RoadShader, Part = "F2a", TexSet = "AsphaltWorn", Recipe = PaletteRecipes.AsphaltWorn, TileMeters = 4f, NormalStrength = 6f,
                       MinAlbedo = 0.05f, MaxAlbedo = 0.12f, DetailSet = "AsphaltPatch", DetailRecipe = PaletteRecipes.AsphaltPatch,
                       Configure = m => { Road(m, 1f, 0.8f); m.SetFloat("_PatchAmount", 0.65f); m.SetFloat("_PatchScale", 9f); } },
            new Spec { Key = VeyraMat.RoadConcreteDeck, Name = "M_Concrete_Deck", Shader = RoadShader, Part = "F2a", TexSet = "ConcreteDeck", Recipe = PaletteRecipes.ConcreteDeck, TileMeters = 4f, NormalStrength = 3f,
                       MinAlbedo = 0.25f, MaxAlbedo = 0.40f, Replaces = new[] { "Road_Concrete", "Concrete" }, CityKeys = new[] { "concrete_dark", "concrete" }, Configure = m => Road(m, 0.85f, 0.35f, 0.06f) },
            new Spec { Key = VeyraMat.SidewalkKerbGranite, Name = "M_Kerb_Granite", Shader = RoadShader, Part = "F2a", TexSet = "KerbGranite", Recipe = PaletteRecipes.KerbGranite, TileMeters = 1f, NormalStrength = 2f,
                       MinAlbedo = 0.15f, MaxAlbedo = 0.35f, Replaces = new[] { "Kerb_Granite" }, CityKeys = new[] { "kerb" }, Configure = m => Road(m, 0.7f, 0f, 0.05f) },
            new Spec { Key = VeyraMat.SidewalkConcrete, Name = "M_Sidewalk_Concrete", Shader = RoadShader, Part = "F2a", TexSet = "SidewalkConcrete", Recipe = PaletteRecipes.SidewalkConcrete, TileMeters = 3.2f, NormalStrength = 4f,
                       MinAlbedo = 0.25f, MaxAlbedo = 0.40f, Replaces = new[] { "Sidewalk" }, CityKeys = new[] { "sidewalk" }, Configure = m => Road(m, 0.8f, 0.3f, 0.06f) },
            new Spec { Key = VeyraMat.RoadCobblestone, Name = "M_Cobblestone", Shader = RoadShader, Part = "F2a", TexSet = "Cobblestone", Recipe = PaletteRecipes.Cobblestone, TileMeters = 2f, NormalStrength = 9f,
                       MinAlbedo = 0.10f, MaxAlbedo = 0.30f, Configure = m => Road(m, 0.9f, 0.5f) },
            new Spec { Key = VeyraMat.RoadGravel, Name = "M_Gravel", Shader = RoadShader, Part = "F2a", TexSet = "Gravel", Recipe = PaletteRecipes.Gravel, TileMeters = 2f, NormalStrength = 8f,
                       MinAlbedo = 0.10f, MaxAlbedo = 0.30f, Replaces = new[] { "Road_Gravel" }, CityKeys = new[] { "gravel" }, Configure = m => Road(m, 0.6f, 0.2f) },
            new Spec { Key = VeyraMat.RoadDirt, Name = "M_Dirt", Shader = RoadShader, Part = "F2a", TexSet = "Dirt", Recipe = PaletteRecipes.Dirt, TileMeters = 4f, NormalStrength = 4f,
                       MinAlbedo = 0.08f, MaxAlbedo = 0.25f, Configure = m => Road(m, 0.8f, 0.7f, 0.1f) },
            new Spec { Key = VeyraMat.RoadMarkingWhite, Name = "M_Road_Marking_White", Shader = RoadShader, Part = "F2a", TexSet = "MarkingPaint", Recipe = PaletteRecipes.MarkingPaint, TileMeters = 2f, NormalStrength = 2f,
                       MinAlbedo = 0.45f, MaxAlbedo = 0.8f, Replaces = new[] { "Marking_White" }, CityKeys = new[] { "marking_white" }, Configure = m => Road(m, 0.9f, 0.2f, 0.03f) },
            new Spec { Key = VeyraMat.RoadMarkingYellow, Name = "M_Road_Marking_Yellow", Shader = RoadShader, Part = "F2a", TexSet = "MarkingPaint", Recipe = PaletteRecipes.MarkingPaint, TileMeters = 2f, NormalStrength = 2f,
                       BaseColor = new Color(0.95f, 0.74f, 0.2f), MinAlbedo = 0.25f, MaxAlbedo = 0.7f, Replaces = new[] { "Marking_Yellow" }, CityKeys = new[] { "marking_yellow" }, Configure = m => Road(m, 0.9f, 0.2f, 0.03f) },

            // ---------------- F2b facades, glass and roofs
            new Spec { Key = VeyraMat.FacadeConcrete, Name = "M_Facade_Concrete", Shader = FacadeShader, Part = "F2b", TexSet = "FacadeConcrete", Recipe = PaletteRecipes.FacadeConcrete, NormalStrength = 3f,
                       MinAlbedo = 0.25f, MaxAlbedo = 0.40f, Replaces = new[] { "Facade_Mid" }, CityKeys = new[] { "facade_mid" },
                       Configure = m => Facade(m, new Vector4(0.70f, 0.14f, 0.62f, 0.03f), false, new Color(0.3f, 0.36f, 0.42f), 0.35f, 0.75f, true, 0.5f, 1f, Wall3m) },
            new Spec { Key = VeyraMat.FacadeConcreteLight, Name = "M_Facade_Concrete_Light", Shader = FacadeShader, Part = "F2b", TexSet = "FacadeConcreteLight", Recipe = PaletteRecipes.FacadeConcreteLight, NormalStrength = 3f,
                       MinAlbedo = 0.25f, MaxAlbedo = 0.45f, Replaces = new[] { "Facade_Light" }, CityKeys = new[] { "facade_light" },
                       Configure = m => Facade(m, new Vector4(0.62f, 0.14f, 0.60f, 0.03f), false, new Color(0.32f, 0.38f, 0.44f), 0.3f, 0.7f, true, 0.45f, 2f, Wall3m) },
            new Spec { Key = VeyraMat.FacadeLimestoneDeco, Name = "M_Facade_Limestone_Deco", Shader = FacadeShader, Part = "F2b", TexSet = "FacadeLimestone", Recipe = PaletteRecipes.FacadeLimestone, NormalStrength = 5f,
                       MinAlbedo = 0.30f, MaxAlbedo = 0.55f, Replaces = new[] { "Facade_StoneDeco" }, CityKeys = new[] { "stone_deco" },
                       Configure = m => Facade(m, new Vector4(0.38f, 0.16f, 0.62f, 0.035f), false, new Color(0.3f, 0.33f, 0.36f), 0.3f, 0.65f, false, 0.6f, 3f, Wall3m) },
            new Spec { Key = VeyraMat.FacadeBrickRed, Name = "M_Facade_Brick_Red", Shader = FacadeShader, Part = "F2b", TexSet = "FacadeBrickRed", Recipe = PaletteRecipes.FacadeBrickRed, NormalStrength = 6f,
                       MinAlbedo = 0.15f, MaxAlbedo = 0.30f, Replaces = new[] { "Facade_Brick" }, CityKeys = new[] { "facade_brick" },
                       Configure = m => Facade(m, new Vector4(0.50f, 0.16f, 0.56f, 0.035f), false, new Color(0.3f, 0.34f, 0.37f), 0.35f, 0.7f, false, 0.55f, 4f, WallBrick) },
            new Spec { Key = VeyraMat.FacadeBrickDark, Name = "M_Facade_Brick_Dark", Shader = FacadeShader, Part = "F2b", TexSet = "FacadeBrickDark", Recipe = PaletteRecipes.FacadeBrickDark, NormalStrength = 6f,
                       MinAlbedo = 0.15f, MaxAlbedo = 0.30f,
                       Configure = m => Facade(m, new Vector4(0.48f, 0.16f, 0.56f, 0.035f), false, new Color(0.3f, 0.34f, 0.37f), 0.35f, 0.7f, false, 0.6f, 5f, WallBrick) },
            new Spec { Key = VeyraMat.FacadeStuccoWhite, Name = "M_Facade_Stucco_White", Shader = FacadeShader, Part = "F2b", TexSet = "FacadeStucco", Recipe = PaletteRecipes.FacadeStucco, NormalStrength = 2f,
                       MinAlbedo = 0.50f, MaxAlbedo = 0.80f, Replaces = new[] { "Facade_Stucco" }, CityKeys = new[] { "facade_stucco" },
                       Configure = m => Facade(m, new Vector4(0.45f, 0.18f, 0.52f, 0.035f), false, new Color(0.3f, 0.36f, 0.4f), 0.35f, 0.7f, false, 0.4f, 6f, Wall3m) },
            new Spec { Key = VeyraMat.FacadeMetalPanel, Name = "M_Facade_MetalPanel", Shader = FacadeShader, Part = "F2b", TexSet = "FacadeMetalPanel", Recipe = PaletteRecipes.FacadeMetalPanel, NormalStrength = 4f,
                       MinAlbedo = 0.35f, MaxAlbedo = 0.70f, Replaces = new[] { "Facade_MetalClad" }, CityKeys = new[] { "metal_clad" },
                       Configure = m => Facade(m, new Vector4(0.30f, 0.55f, 0.22f, 0.03f), false, new Color(0.3f, 0.34f, 0.38f), 0.1f, 0.4f, true, 0.35f, 7f, Wall3m) },
            new Spec { Key = VeyraMat.FacadeHouseRender, Name = "M_Facade_House_Render", Shader = FacadeShader, Part = "F2b", TexSet = "FacadeHouse", Recipe = PaletteRecipes.FacadeHouse, NormalStrength = 2f,
                       MinAlbedo = 0.35f, MaxAlbedo = 0.60f, Replaces = new[] { "Facade_House" }, CityKeys = new[] { "house_wall" },
                       Configure = m => Facade(m, new Vector4(0.36f, 0.20f, 0.42f, 0.04f), false, new Color(0.3f, 0.34f, 0.36f), 0.35f, 0.75f, false, 0.45f, 8f, Wall3m, roomDepth: 1.0f) },
            new Spec { Key = VeyraMat.FacadeWood, Name = "M_Facade_Wood", Shader = FacadeShader, Part = "F2b", TexSet = "FacadeWood", Recipe = PaletteRecipes.FacadeWood, NormalStrength = 5f,
                       MinAlbedo = 0.10f, MaxAlbedo = 0.30f, Replaces = new[] { "Facade_Wood" }, CityKeys = new[] { "wood_wall" },
                       Configure = m => Facade(m, new Vector4(0.30f, 0.20f, 0.42f, 0.04f), false, new Color(0.3f, 0.33f, 0.35f), 0.3f, 0.7f, false, 0.4f, 9f, Wall3m, roomDepth: 1.0f) },
            new Spec { Key = VeyraMat.GlassCurtainBlueGrey, Name = "M_Glass_Curtain_BlueGrey", Shader = FacadeShader, Part = "F2b", TexSet = "Spandrel", Recipe = PaletteRecipes.Spandrel, NormalStrength = 1f,
                       MinAlbedo = 0.03f, MaxAlbedo = 0.20f, Replaces = new[] { "Glass_Blue" }, CityKeys = new[] { "glass_blue" },
                       Configure = m => Facade(m, new Vector4(0.9f, 0.14f, 0.86f, 0.02f), true, new Color(0.32f, 0.42f, 0.5f), 0.3f, 0.7f, true, 0.15f, 10f, Wall3m, roomDepth: 1.2f) },
            new Spec { Key = VeyraMat.GlassCurtainBronze, Name = "M_Glass_Curtain_Bronze", Shader = FacadeShader, Part = "F2b", TexSet = "Spandrel", Recipe = PaletteRecipes.Spandrel, NormalStrength = 1f,
                       MinAlbedo = 0.03f, MaxAlbedo = 0.20f, Replaces = new[] { "Glass_Bronze" }, CityKeys = new[] { "glass_bronze" },
                       Configure = m => Facade(m, new Vector4(0.9f, 0.14f, 0.86f, 0.02f), true, new Color(0.5f, 0.38f, 0.25f), 0.3f, 0.7f, true, 0.15f, 11f, Wall3m, roomDepth: 1.2f) },
            new Spec { Key = VeyraMat.StorefrontGlass, Name = "M_Storefront_Glass", Shader = FacadeShader, Part = "F2b", TexSet = "Spandrel", Recipe = PaletteRecipes.Spandrel, NormalStrength = 1f,
                       MinAlbedo = 0.03f, MaxAlbedo = 0.20f, Replaces = new[] { "Storefront" }, CityKeys = new[] { "storefront" },
                       Configure = m => Facade(m, new Vector4(0.9f, 0.14f, 0.86f, 0.025f), true, new Color(0.4f, 0.45f, 0.48f), 0.6f, 0.95f, false, 0.35f, 12f, new Vector2(4f, 1.5f), bays: 3f, floors: 1f, roomDepth: 0.8f, emission: 2.6f, interiorDim: 0.45f) },
            new Spec { Key = VeyraMat.RoofGravel, Name = "M_Roof_Gravel", Shader = RoadShader, Part = "F2b", TexSet = "RoofGravel", Recipe = PaletteRecipes.RoofGravel, TileMeters = 2f, UvMeters = 8f, NormalStrength = 8f,
                       MinAlbedo = 0.12f, MaxAlbedo = 0.30f, Configure = m => Road(m, 0.7f, 0.5f) },
            new Spec { Key = VeyraMat.RoofMembrane, Name = "M_Roof_Membrane", Shader = RoadShader, Part = "F2b", TexSet = "RoofMembrane", Recipe = PaletteRecipes.RoofMembrane, TileMeters = 4f, UvMeters = 8f, NormalStrength = 3f,
                       MinAlbedo = 0.05f, MaxAlbedo = 0.15f, Replaces = new[] { "Roof_Dark" }, CityKeys = new[] { "roof_dark" }, Configure = m => Road(m, 1f, 0.8f, 0.06f) },
        };

        public static IEnumerable<Spec> Part(string part) => Specs.Where(s => part == null || s.Part == part);

        // ------------------------------------------------------------------ menus

        [MenuItem("Veyra/Materials/Palette/Build F2a Roads")]
        static void BuildRoadsMenu() => Debug.Log("[VeyraPalette] " + Build("F2a"));

        [MenuItem("Veyra/Materials/Palette/Build F2b Facades")]
        static void BuildFacadesMenu() => Debug.Log("[VeyraPalette] " + Build("F2b"));

        [MenuItem("Veyra/Materials/Palette/Remap World (all built parts)")]
        static void RemapMenu() => Debug.Log("[VeyraPalette] " + RemapWorld(null));

        [MenuItem("Veyra/Materials/Palette/Validate Albedo")]
        static void ValidateMenu() => Debug.Log("[VeyraPalette] " + ValidateAlbedo(null));

        // ------------------------------------------------------------------ build

        /// <summary>Creates textures (when missing or <paramref name="regenerateTextures"/>), materials and library slots for one part.</summary>
        public static string Build(string part, bool regenerateTextures = false)
        {
            var sb = new StringBuilder();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var sets = new Dictionary<string, Set>();
            foreach (var spec in Part(part))
            {
                if (spec.TexSet != null && !sets.ContainsKey(spec.TexSet))
                    sets[spec.TexSet] = TextureSet(spec.TexSet, spec.Recipe, spec.NormalStrength, regenerateTextures, sb, false);
                if (spec.DetailSet != null && !sets.ContainsKey(spec.DetailSet))
                    sets[spec.DetailSet] = TextureSet(spec.DetailSet, spec.DetailRecipe, spec.NormalStrength, regenerateTextures, sb, true);
            }
            var lib = AssetDatabase.LoadAssetAtPath<VeyraMaterialLibrary>(VeyraMaterialFactory.LibraryPath);
            if (lib == null) return "no VeyraMaterialLibrary: run Veyra/Materials/Build Material Library first";
            Texture2D atlas = null;
            if (Part(part).Any(x => x.Shader == FacadeShader))
            {
                string ap = $"{PaletteTextures.Dir}/{InteriorAtlasName}.png";
                if (regenerateTextures || !File.Exists(ap))
                {
                    var asw = System.Diagnostics.Stopwatch.StartNew();
                    atlas = MakeColor(InteriorAtlasName, TexSize, PaletteRecipes.InteriorAtlas);
                    sb.AppendLine($"texture {InteriorAtlasName} {TexSize}² (4 x 4 rooms) in {asw.Elapsed.TotalSeconds:0.0} s");
                }
                else atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(ap);
            }
            foreach (var spec in Part(part))
            {
                var m = EnsureMaterial(spec, spec.TexSet != null ? sets[spec.TexSet] : null, spec.DetailSet != null ? sets[spec.DetailSet] : null, out bool created);
                if (atlas != null && m.HasProperty("_InteriorAtlas")) { m.SetTexture("_InteriorAtlas", atlas); EditorUtility.SetDirty(m); }
                SetSlot(lib, spec.Key, m);
                sb.AppendLine($"{(created ? "created" : "updated")} {spec.Key,-22} {AssetDatabase.GetAssetPath(m)} ({spec.Shader}, tile {spec.TileMeters} m{(spec.Replaces.Length > 0 ? ", replaces " + string.Join("/", spec.Replaces) : "")})");
            }
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            sb.AppendLine($"part {part ?? "all"}: {Part(part).Count()} materials in {sw.Elapsed.TotalSeconds:0.0} s; library {lib.Slots.Count} slots, missing: {string.Join(", ", lib.MissingKeys())}");
            return sb.ToString();
        }

        static Set TextureSet(string name, Func<float, float, Sample> recipe, float normalStrength, bool regenerate, StringBuilder sb, bool alpha)
        {
            string a = $"{PaletteTextures.Dir}/Pal_{name}_A.png";
            if (!regenerate && File.Exists(a))
                return new Set
                {
                    Albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(a),
                    Normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{PaletteTextures.Dir}/Pal_{name}_N.png"),
                    Mask = AssetDatabase.LoadAssetAtPath<Texture2D>($"{PaletteTextures.Dir}/Pal_{name}_M.png"),
                };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var set = Make("Pal_" + name, TexSize, normalStrength, recipe, alpha);
            sb.AppendLine($"textures Pal_{name}_A/_N/_M {TexSize}² in {sw.Elapsed.TotalSeconds:0.0} s");
            return set;
        }

        static Material EnsureMaterial(Spec spec, Set set, Set detail, out bool created)
        {
            Directory.CreateDirectory(MatDir);
            string path = $"{MatDir}/{spec.Name}.mat";
            var shader = Shader.Find(spec.Shader);
            if (shader == null) throw new InvalidOperationException($"shader {spec.Shader} not found (compile errors?)");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            created = m == null;
            if (created) { m = new Material(shader) { name = spec.Name }; AssetDatabase.CreateAsset(m, path); }
            if (m.shader != shader) m.shader = shader;
            if (set != null)
            {
                m.SetTexture("_BaseMap", set.Albedo);
                if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", set.Normal);
                if (m.HasProperty("_MaskMap")) m.SetTexture("_MaskMap", set.Mask);
                float t = spec.UvMeters / Mathf.Max(0.01f, spec.TileMeters);
                m.SetTextureScale("_BaseMap", new Vector2(t, t));
                m.SetTextureOffset("_BaseMap", Vector2.zero);
            }
            if (detail != null && m.HasProperty("_DetailMap"))
            {
                m.SetTexture("_DetailMap", detail.Albedo);
                float t = 1f / Mathf.Max(0.01f, spec.TileMeters);
                m.SetTextureScale("_DetailMap", new Vector2(t, t));
            }
            m.SetColor("_BaseColor", spec.BaseColor);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 1f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 1f);
            if (m.HasProperty("_OcclusionStrength")) m.SetFloat("_OcclusionStrength", 1f);
            if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", 1f);
            spec.Configure?.Invoke(m);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        public static void SetSlot(VeyraMaterialLibrary lib, VeyraMat key, Material m)
        {
            var slot = lib.Slots.FirstOrDefault(s => s != null && s.Key == key);
            if (slot == null) lib.Slots.Add(new VeyraMaterialLibrary.Slot { Key = key, Material = m });
            else slot.Material = m;
            lib.Slots = lib.Slots.Where(s => s != null && s.Key != VeyraMat.None).OrderBy(s => (int)s.Key).ToList();
        }

        // ------------------------------------------------------------------ CityBuilder hook

        /// <summary>CityBuilder: replaces its blockout materials by the palette materials (keys of <see cref="Spec.CityKeys"/>).</summary>
        public static void OverrideCityMaterials(Dictionary<string, Material> table)
        {
            var lib = AssetDatabase.LoadAssetAtPath<VeyraMaterialLibrary>(VeyraMaterialFactory.LibraryPath);
            if (lib == null) return;
            foreach (var spec in Specs)
                if (lib.TryGet(spec.Key, out var m))
                    foreach (var k in spec.CityKeys) table[k] = m;
        }

        // ------------------------------------------------------------------ world remap

        /// <summary>
        /// Swaps blockout materials for palette materials (of <paramref name="part"/>, or every part already in the library) in the
        /// open VeyraCity scene: renderer slots and every material reference of scene components; WorldEnvironment drops its
        /// per-material wet/emissive entries for them (the Veyra shaders read the globals). Saves the scene.
        /// </summary>
        public static string RemapWorld(string part)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != "Assets/_Veyra/Scenes/VeyraCity.unity") return "open Assets/_Veyra/Scenes/VeyraCity.unity first";
            var lib = AssetDatabase.LoadAssetAtPath<VeyraMaterialLibrary>(VeyraMaterialFactory.LibraryPath);
            var map = new Dictionary<Material, Material>();
            var sb = new StringBuilder();
            foreach (var spec in Part(part))
            {
                if (!lib.TryGet(spec.Key, out var target)) continue;
                foreach (var oldName in spec.Replaces)
                {
                    var old = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{oldName}.mat");
                    if (old != null && old != target) map[old] = target;
                }
            }
            if (map.Count == 0) return "nothing to remap";
            int slots = 0, renderers = 0, refs = 0;
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && map.TryGetValue(mats[i], out var t)) { mats[i] = t; changed = true; slots++; }
                if (changed) { r.sharedMaterials = mats; renderers++; EditorUtility.SetDirty(r); }
            }
            int wetRemoved = 0, emRemoved = 0;
            foreach (var env in UnityEngine.Object.FindObjectsByType<WorldEnvironment>(FindObjectsInactive.Include))
            {
                wetRemoved += env.WetSurfaces.RemoveAll(w => w.Material != null && map.ContainsKey(w.Material));
                emRemoved += env.Emissives.RemoveAll(e => e.Material != null && map.ContainsKey(e.Material));
                env.ScrollMaterials.RemoveAll(m => m != null && map.ContainsKey(m));
                EditorUtility.SetDirty(env);
            }
            foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                if (mb == null || mb is WorldEnvironment) continue;
                var so = new SerializedObject(mb);
                var it = so.GetIterator();
                bool any = false;
                while (it.Next(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue is Material om && map.TryGetValue(om, out var nm))
                    { it.objectReferenceValue = nm; any = true; refs++; }
                if (any) so.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            foreach (var kv in map) sb.AppendLine($"  {kv.Key.name} -> {kv.Value.name}");
            sb.Insert(0, $"remapped {map.Count} materials: {slots} renderer slots on {renderers} renderers, {refs} component references; WorldEnvironment entries removed: {wetRemoved} wet, {emRemoved} emissive (now driven by the Veyra globals)\n");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ validation

        /// <summary>Mean linear albedo (luminance) of a material: its base map (read from the PNG) times its base colour.</summary>
        public static float MeanAlbedo(Material m)
        {
            var c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor").linear : Color.white;
            float tint = Lum(c);
            var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") as Texture2D : null;
            if (tex == null) return tint;
            string path = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return tint;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            t.LoadImage(File.ReadAllBytes(path));
            var px = t.GetPixels32();
            UnityEngine.Object.DestroyImmediate(t);
            double sum = 0; int n = 0;
            for (int i = 0; i < px.Length; i += 7)
            {
                var p = px[i];
                sum += 0.2126 * Mathf.GammaToLinearSpace(p.r / 255f) * c.r + 0.7152 * Mathf.GammaToLinearSpace(p.g / 255f) * c.g + 0.0722 * Mathf.GammaToLinearSpace(p.b / 255f) * c.b;
                n++;
            }
            return (float)(sum / Math.Max(1, n));
        }

        /// <summary>Checks every built palette material against its albedo range (card F2 E). Returns a report; FAIL lines mark violations.</summary>
        public static string ValidateAlbedo(string part)
        {
            var lib = AssetDatabase.LoadAssetAtPath<VeyraMaterialLibrary>(VeyraMaterialFactory.LibraryPath);
            var sb = new StringBuilder();
            int fails = 0, n = 0;
            foreach (var spec in Part(part))
            {
                if (lib == null || !lib.TryGet(spec.Key, out var m)) { sb.AppendLine($"SKIP  {spec.Name}: not built"); continue; }
                float a = MeanAlbedo(m);
                bool ok = a >= spec.MinAlbedo && a <= spec.MaxAlbedo && a >= 0.02f && a <= 0.9f;
                if (!ok) fails++;
                n++;
                sb.AppendLine($"{(ok ? "PASS" : "FAIL")}  {spec.Name,-26} mean linear albedo {a:0.000} (range {spec.MinAlbedo:0.00}–{spec.MaxAlbedo:0.00})");
            }
            sb.Insert(0, $"albedo validation: {n} materials, {fails} outside their range\n");
            return sb.ToString();
        }
    }
}
