using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Phase C: "Veyra/Buildings/Build Kitbash Catalog" → Assets/_Veyra/Data/KitbashCatalog.asset from every
    /// Art/Models/Kitbash/&lt;source&gt;/kitbash_manifest.json (written by Tools/Blender/process_city_map.py --mode kitbash).
    /// Style tags use the placement data's style_kit vocabulary (GlassModern, ArtDeco, ResidentialMidrise, CoastalStucco,
    /// BrickIndustrial, MarketTownBrick, ModernLowrise, SuburbanRetail) so BuildingAssembler can match records directly.
    /// Excluded (kept in the catalog with a note, never assembled): props and fragments (height &lt; 6 m or footprint &lt; 6 m),
    /// oversize pieces (height &gt; 350 m or footprint &gt; 120 m), and the Japanese street and Cyberpunk packs until their signage
    /// is checked for real brands and text (CLAUDE.md: original fictional brands only) and the monolithic pack is segmented.
    /// Licence ids resolve to rows of Docs/THIRD_PARTY_LICENSE_REGISTER.csv (KB-* rows). Report: Logs/C/kitbash_catalog.txt.
    /// Re-runnable: the asset is rebuilt in place.
    /// </summary>
    public static class KitbashCatalogBuilder
    {
        public const string CatalogPath = "Assets/_Veyra/Data/KitbashCatalog.asset";
        const string Root = "Assets/_Veyra/Art/Models/Kitbash";

        /// <summary>source → (licence register id, styles for tall pieces, styles for low pieces, districts, excluded reason)</summary>
        static readonly Dictionary<string, (string lic, string[] tall, string[] low, string[] districts, string exclude)> Packs =
            new Dictionary<string, (string, string[], string[], string[], string)>
        {
            // Veyra's own building kit (ArtSource/Blender/Buildings, 88 models, 2026-11-14). Original content, so the
            // licence id is VC-ORIGINAL with a matching row in Docs/THIRD_PARTY_LICENSE_REGISTER.csv - without it the
            // entries read as UNREGISTERED. Without these lines the catalog gives the entries no styles or districts,
            // StyleMatch fails, and nothing is placed at all.
            // tall / low are chosen by Height >= 30 m. Only NeonMile and IronportTanks use the split (brick blocks
            // 30-62 m vs low-rise 16-21 m; process columns 52 m vs tanks 12-19 m); the rest list the same both sides.
            ["Veyra_Original_Suburban"] = ("VC-ORIGINAL", new[] { "Suburban" }, new[] { "Suburban" }, new[] { "D5" }, null),
            ["Veyra_Original_Downtown"] = ("VC-ORIGINAL", new[] { "GlassModern", "ArtDeco" }, new[] { "GlassModern", "ArtDeco" }, new[] { "D1" }, null),
            ["Veyra_Original_NeonMile"] = ("VC-ORIGINAL", new[] { "BrickIndustrial" }, new[] { "ModernLowrise" }, new[] { "D6" }, null),
            ["Veyra_Original_Coast"] = ("VC-ORIGINAL", new[] { "CoastalStucco" }, new[] { "CoastalStucco" }, new[] { "D2" }, null),
            ["Veyra_Original_MarketTown"] = ("VC-ORIGINAL", new[] { "MarketTownBrick" }, new[] { "MarketTownBrick" }, new[] { "D8" }, null),
            ["Veyra_Original_Midrise"] = ("VC-ORIGINAL", new[] { "ResidentialMidrise" }, new[] { "ResidentialMidrise" }, new[] { "D1", "D5" }, null),
            ["Veyra_Original_Ironport"] = ("VC-ORIGINAL", new[] { "Industrial" }, new[] { "Industrial" }, new[] { "D3", "D7" }, null),
            ["Veyra_Original_IronportTanks"] = ("VC-ORIGINAL", new[] { "IndustrialRefinery" }, new[] { "IndustrialTank" }, new[] { "D3" }, null),
            ["Veyra_Original_Alpine"] = ("VC-ORIGINAL", new[] { "Alpine" }, new[] { "Alpine" }, new[] { "D4" }, null),
            ["Veyra_Original_Rural"] = ("VC-ORIGINAL", new[] { "Rural" }, new[] { "Rural" }, new[] { "D8" }, null),
            ["Veyra_Original_Aviation"] = ("VC-ORIGINAL", new[] { "Aviation" }, new[] { "Aviation" }, new[] { "D7" }, null),
            ["Veyra_Original_Retail"] = ("VC-ORIGINAL", new[] { "SuburbanRetail" }, new[] { "SuburbanRetail" }, new[] { "D5" }, null),
            ["Veyra_Original_Roadside"] = ("VC-ORIGINAL", new[] { "Roadside" }, new[] { "Roadside" }, new[] { "D4" }, null),

            ["Downtown_Buildings_Set"] = ("KB-01", new[] { "GlassModern" }, new[] { "ModernLowrise" }, new[] { "D1", "D6" }, null),
            ["Atlanta_Corporate_Office"] = ("KB-02", new[] { "GlassModern" }, new[] { "ModernLowrise" }, new[] { "D1" }, null),
            ["Realistic_Chicago_Buildings"] = ("KB-03", new[] { "ArtDeco" }, new[] { "BrickIndustrial" }, new[] { "D1" }, null),
            ["New_York_City"] = ("KB-04", new[] { "ArtDeco" }, new[] { "MarketTownBrick", "BrickIndustrial" }, new[] { "D1", "D6", "D3" }, null),
            ["San_Francisco_City"] = ("KB-05", new[] { "GlassModern" }, new[] { "ModernLowrise" }, new[] { "D1" }, null),
            ["Brownstone_Building_Set"] = ("KB-06", new[] { "ResidentialMidrise" }, new[] { "MarketTownBrick" }, new[] { "D1", "D2", "D8" }, null),
            ["CCity_Building_Set_1"] = ("KB-07", new[] { "ResidentialMidrise" }, new[] { "ModernLowrise", "CoastalStucco" }, new[] { "D2", "D6" }, null),
            ["Full_Gameready_City_Retexture"] = ("KB-08", new[] { "ResidentialMidrise" }, new[] { "ModernLowrise" }, new[] { "D1", "D2", "D6" }, null),
            ["Modular_City_Builder_Starter_Kit"] = ("KB-09", new[] { "ResidentialMidrise" }, new[] { "ModernLowrise", "SuburbanRetail" }, new[] { "D2", "D5", "D6" }, null),
            ["Japanese_Street_At_Night"] = ("KB-10", new[] { "ModernLowrise" }, new[] { "ModernLowrise" }, new[] { "D6" }, "signage not yet checked for real brands/text (A6 rule)"),
            ["Cyberpunk_City"] = ("KB-11", new[] { "GlassModern" }, new[] { "ModernLowrise" }, new[] { "D6" }, "monolithic pack: segmentation and provenance check pending (roadmap 1B)"),
            ["City_Road_Layout_Mockup"] = ("KB-12", new string[0], new string[0], new string[0], "road layout mockup, not a building"),
        };

        [MenuItem("Veyra/Buildings/Build Kitbash Catalog")]
        public static void Menu() => Debug.Log(Build());

        public static string Build()
        {
            var sb = new StringBuilder($"Kitbash catalog {DateTime.Now:yyyy-MM-dd HH:mm}\n");
            var cat = AssetDatabase.LoadAssetAtPath<KitbashCatalog>(CatalogPath);
            if (cat == null) { cat = ScriptableObject.CreateInstance<KitbashCatalog>(); AssetDatabase.CreateAsset(cat, CatalogPath); }
            cat.Entries.Clear();
            int usable = 0, missing = 0;
            // visual review of the contact sheet (Veyra/Buildings/Kitbash Contact Sheet): id -> reason, Data/kitbash_review.json
            var review = new Dictionary<string, string>();
            const string ReviewPath = "Assets/_Veyra/Data/kitbash_review.json";
            if (File.Exists(ReviewPath))
                foreach (var kv in JObject.Parse(File.ReadAllText(ReviewPath))) review[kv.Key] = (string)kv.Value;
            var styleCount = new Dictionary<string, int>();
            foreach (var mf in Directory.GetFiles(Root, "kitbash_manifest.json", SearchOption.AllDirectories).OrderBy(x => x))
            {
                var j = JObject.Parse(File.ReadAllText(mf));
                string source = (string)j["source"];
                Packs.TryGetValue(source, out var pack);
                foreach (var p in j["pieces"] ?? new JArray())
                {
                    string file = ((string)p["file"]).Replace('\\', '/');
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(file);
                    var fp = p["footprint_m"];
                    var e = new KitbashCatalog.Entry
                    {
                        Id = (string)p["name"],
                        Prefab = go,
                        Source = source,
                        Footprint = new Vector2((float)fp[0], (float)fp[1]),
                        Height = (float)p["height_m"],
                        LicenceId = pack.lic ?? "UNREGISTERED",
                        Triangles = (p["triangles"] ?? new JArray()).Select(t => (int)t).ToArray(),
                        Collider = string.Join(",", (p["collision"] ?? new JArray()).Select(c => ((string)c).Split('_')[0]).Distinct()),
                        Districts = pack.districts ?? new string[0],
                        SourceMaterials = (p["materials"] ?? new JArray()).Select(m => (string)m["source"]).ToArray(),
                    };
                    e.Styles = e.Height >= 30f ? pack.tall ?? new string[0] : pack.low ?? new string[0];
                    e.FloorHeight = e.Styles.Contains("GlassModern") ? 4.0f : 3.4f;
                    string type = (string)p["type"];
                    if (go == null) { e.Excluded = true; e.Note = "FBX not imported"; missing++; }
                    else if (pack.exclude != null) { e.Excluded = true; e.Note = pack.exclude; }
                    else if (type != "Building") { e.Excluded = true; e.Note = "prop piece"; }
                    else if (e.Height < 6f || Mathf.Min(e.Footprint.x, e.Footprint.y) < 6f) { e.Excluded = true; e.Note = "fragment (under 6 m)"; }
                    else if (e.Height > 350f || Mathf.Max(e.Footprint.x, e.Footprint.y) > 120f) { e.Excluded = true; e.Note = "oversize"; }
                    else if ((source == "New_York_City" || source == "San_Francisco_City") && e.Height > 150f)
                    { e.Excluded = true; e.Note = "possible likeness of a real-world landmark tower (Veyra City is fictional)"; }
                    else if (e.Triangles.Length > 0 && e.Triangles[0] > 60000) { e.Excluded = true; e.Note = "LOD0 over the 60k per-building budget"; }
                    else if (review.TryGetValue(e.Id, out var why)) { e.Excluded = true; e.Note = why.Contains("untextured") ? "contact sheet review: untextured" : why.Contains("frame") || why.Contains("skeletal") ? "contact sheet review: incomplete segment (frame/skeleton)" : "contact sheet review: fragment, pole, slab or tank"; }
                    if (!e.Excluded)
                    {
                        usable++;
                        foreach (var s in e.Styles) styleCount[s] = styleCount.TryGetValue(s, out int n) ? n + 1 : 1;
                    }
                    cat.Entries.Add(e);
                }
            }
            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
            sb.AppendLine($"{cat.Entries.Count} pieces, {usable} usable buildings, {missing} FBX missing");
            sb.AppendLine("usable by style: " + string.Join(", ", styleCount.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}")));
            foreach (var g in cat.Entries.Where(x => x.Excluded).GroupBy(x => x.Note)) sb.AppendLine($"excluded ({g.Key}): {g.Count()}");
            Directory.CreateDirectory("Logs/C");
            File.WriteAllText("Logs/C/kitbash_catalog.txt", sb.ToString());
            return sb.ToString();
        }
    }
}
