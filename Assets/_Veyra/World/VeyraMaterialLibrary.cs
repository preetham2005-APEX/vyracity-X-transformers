using System;
using System.Collections.Generic;
using UnityEngine;

namespace VeyraCity.World
{
    /// <summary>Material groups of the <see cref="VeyraMaterialLibrary"/>; a key's category is its value / 100.</summary>
    public enum VeyraMatCategory
    {
        None = 0, Road = 1, Sidewalk = 2, Building = 3, Glass = 4, Neon = 5, Props = 6, Vegetation = 7, Water = 8, Vehicle = 9,
        Effects = 10, Debug = 11,
    }

    /// <summary>
    /// Material slots. The value range sets the category (Road 100–199, Sidewalk 200–299, … Effects 1000–1099, Debug 1100–1199);
    /// never renumber a key, serialized slots store the number. Card F1 adds the materials the code creates at runtime; card F2
    /// adds the world palette (roads, sidewalks, buildings, glass, neon, props, vegetation, water).
    /// </summary>
    public enum VeyraMat
    {
        None = 0,

        // Road (100–199): Veyra/Road shader (card F2a)
        RoadAsphaltNew = 100,
        RoadAsphaltWorn = 101,
        RoadAsphaltPatched = 102,
        RoadConcreteDeck = 103,
        RoadCobblestone = 104,
        RoadGravel = 105,
        RoadDirt = 106,
        RoadMarkingWhite = 110,
        RoadMarkingYellow = 111,

        // Sidewalk (200–299): Veyra/Road shader (card F2a)
        SidewalkConcrete = 200,
        SidewalkKerbGranite = 201,

        // Building (300–399): Veyra/Facade walls (card F2b); roofs use Veyra/Road (wetness)
        FacadeConcrete = 300,
        FacadeConcreteLight = 301,
        FacadeLimestoneDeco = 302,
        FacadeBrickRed = 303,
        FacadeBrickDark = 304,
        FacadeStuccoWhite = 305,
        FacadeMetalPanel = 306,
        FacadeHouseRender = 307,
        FacadeWood = 308,
        RoofGravel = 320,
        RoofMembrane = 321,

        // Glass (400–499): Veyra/Facade curtain walls and shop fronts with interior mapping (card F2b)
        GlassCurtainBlueGrey = 400,
        GlassCurtainBronze = 401,
        StorefrontGlass = 402,

        // Neon (500–599): Veyra/Neon (card F2c)
        NeonCyan = 500,
        NeonMagenta = 501,
        NeonAmber = 502,
        NeonWhite = 503,
        LedScreen = 510,
        SignBacklit = 511,

        // Props (600–699): URP/Lit (card F2c)
        MetalPainted = 600,
        MetalPaintedRed = 601,
        MetalGalvanized = 602,
        PlasticDark = 603,
        Rubber = 604,
        WoodWeathered = 605,

        // Vegetation (700–799): URP/Lit (card F2c)
        Leaves = 700,
        Bark = 701,

        // Vehicle (900–999)
        VehiclePlaceholderBody = 900,
        VehiclePlaceholderWheel = 901,
        /// <summary>URP Complex Lit with clear coat and metallic flakes (card F2c).</summary>
        CarPaint = 902,

        // Effects (1000–1099): templates cloned by RuntimeMaterials for materials made at runtime.
        /// <summary>URP/Lit opaque, _EMISSION on with black emission (garage, procedural traffic cars).</summary>
        FxLit = 1000,
        /// <summary>URP/Unlit transparent additive (event markers).</summary>
        FxUnlitAdditive = 1001,
        /// <summary>URP/Unlit transparent alpha-blended.</summary>
        FxUnlitAlpha = 1002,
        /// <summary>URP/Particles/Unlit alpha-blended (tyre smoke, skid marks, rain).</summary>
        FxParticleAlpha = 1003,
        /// <summary>URP/Particles/Unlit additive (nitro flame and trails, sparks, speed streaks).</summary>
        FxParticleAdditive = 1004,

        // Debug (1100–1199)
        /// <summary>URP/Unlit opaque white, for runtime debug geometry.</summary>
        DebugUnlit = 1100,
    }

    /// <summary>
    /// Every material runtime code may use, as serialized references (CLAUDE.md: runtime code never calls Shader.Find or builds
    /// materials from shader names, because shaders that no built asset references are stripped from players: BUG-002).
    /// A scene object holds the library (GameFlow, WorldEnvironment or <see cref="VeyraMaterialsHolder"/>) and activates it;
    /// components created at runtime use <see cref="Active"/>. Asset: Assets/_Veyra/World/Generated/Materials/VeyraMaterialLibrary.asset,
    /// filled by the editor tool Veyra/Materials/Build Material Library (VeyraMaterialFactory).
    /// </summary>
    [CreateAssetMenu(menuName = "Veyra/Material Library")]
    public sealed class VeyraMaterialLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class Slot
        {
            public VeyraMat Key;
            public Material Material;
        }

        [Tooltip("Sorted by key, so the list reads grouped by category")]
        public List<Slot> Slots = new List<Slot>();

        [Tooltip("Assets/_Veyra/Art/Shaders/VC_Variants.shadervariants: also in Graphics Settings > Preloaded Shaders; GameFlow warms it up while loading")]
        public ShaderVariantCollection Variants;

        /// <summary>The library of the loaded scene, set by the scene object that holds it.</summary>
        public static VeyraMaterialLibrary Active { get; private set; }

        Dictionary<VeyraMat, Material> map;
        readonly HashSet<VeyraMat> reported = new HashSet<VeyraMat>();

        public static VeyraMatCategory CategoryOf(VeyraMat key) => (VeyraMatCategory)((int)key / 100);

        /// <summary>Makes <paramref name="library"/> the active one; logs an error naming <paramref name="holder"/> when it is null.</summary>
        public static void Activate(VeyraMaterialLibrary library, UnityEngine.Object holder)
        {
            if (library == null)
            {
                Debug.LogError($"[VeyraMaterialLibrary] {(holder != null ? holder.name : "a scene object")} has no VeyraMaterialLibrary assigned; " +
                               "runtime materials (effects, markers, garage, traffic) will be missing. Run Veyra/Materials/Build Material Library.", holder);
                return;
            }
            Active = library;
        }

        void OnEnable()
        {
            map = null;
            reported.Clear();
        }

        void OnValidate() => map = null;

        void BuildMap()
        {
            map = new Dictionary<VeyraMat, Material>();
            foreach (var s in Slots)
                if (s != null && s.Key != VeyraMat.None && s.Material != null) map[s.Key] = s.Material;
        }

        public bool TryGet(VeyraMat key, out Material material)
        {
            if (map == null) BuildMap();
            return map.TryGetValue(key, out material) && material != null;
        }

        /// <summary>The material in slot <paramref name="key"/>, or null with one error per key naming the caller (<paramref name="context"/>).</summary>
        public Material Get(VeyraMat key, UnityEngine.Object context = null)
        {
            if (TryGet(key, out var m)) return m;
            if (reported.Add(key))
                Debug.LogError($"[VeyraMaterialLibrary] slot {key} ({CategoryOf(key)}) is empty in '{name}'" +
                               (context != null ? $", requested by {context.name}" : "") + ". Run Veyra/Materials/Build Material Library.", context != null ? context : this);
            return null;
        }

        /// <summary>Keys of the enum that have no material (an empty list means the library is complete).</summary>
        public List<VeyraMat> MissingKeys()
        {
            var missing = new List<VeyraMat>();
            foreach (VeyraMat k in Enum.GetValues(typeof(VeyraMat)))
                if (k != VeyraMat.None && !TryGet(k, out _)) missing.Add(k);
            return missing;
        }
    }
}
