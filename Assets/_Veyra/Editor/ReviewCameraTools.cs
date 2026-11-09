using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VeyraCity.Testing;
using VeyraCity.World.Data;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card S5: creates/updates Assets/_Veyra/Data/ReviewCameras.asset with the 18 cameras of the playbook's Appendix E and links
    /// it into the runtime test config (so player builds include it). Re-runnable. Compass directions ("northbound") are turned
    /// into +1/-1 from the road's own tangent in road_network_geometry.json; roads, distances and the "at VP-xx" positions are
    /// checked against the data. (The card names Assets/_Veyra/Settings; CLAUDE.md's folder rules put data assets in _Veyra/Data.)
    /// </summary>
    public static class ReviewCameraTools
    {
        public const string AssetPath = "Assets/_Veyra/Data/ReviewCameras.asset";

        // Clock hours per label, from WorldEnvironment's sun curve (rises 6:00, 62° at 13:00, sets 20:00) and the art-direction
        // guide (day look ~30°+, golden hour ~15°): Day 12:00 (~60°), Golden 18:48 (~16°), Dusk 19:45 (~3°), Night 23:00, Morning 07:30.
        public static float HourOf(ReviewCameraSet.TimeLabel t) => t switch
        {
            ReviewCameraSet.TimeLabel.Day => 12f,
            ReviewCameraSet.TimeLabel.Golden => 18.8f,
            ReviewCameraSet.TimeLabel.Dusk => 19.75f,
            ReviewCameraSet.TimeLabel.Night => 23f,
            ReviewCameraSet.TimeLabel.Morning => 7.5f,
            _ => 12f,
        };

        [MenuItem("Veyra/Review/Create Review Cameras")]
        public static void Menu() => Debug.Log(CreateAll());

        static ReviewCameraSet.Entry Fixed(string name, string district, Vector3 pos, bool aboveGround, Vector3 lookAt, float fov, ReviewCameraSet.TimeLabel t, string note = "") =>
            new ReviewCameraSet.Entry { name = name, kind = ReviewCameraSet.Kind.Fixed, district = district, position = pos, heightAboveGround = aboveGround, lookAt = lookAt, fov = fov, time = t, hour = HourOf(t), note = note };

        /// <summary>Until <see cref="CreateAll"/> resolves it, <c>note</c> holds only the compass word ("" when Appendix E gives none).</summary>
        static ReviewCameraSet.Entry Chase(string name, string district, string road, float s, string compass, float ahead, float fov, ReviewCameraSet.TimeLabel t, bool wet) =>
            new ReviewCameraSet.Entry { name = name, kind = ReviewCameraSet.Kind.Chase, district = district, roadId = road, distanceAlongRoad_m = s, direction = 1, lookAhead = ahead, fov = fov, time = t, hour = HourOf(t), wet = wet, note = compass ?? "" };

        /// <summary>The 18 cameras of Appendix E (Unity coordinates; "+h" = above ground).</summary>
        public static List<ReviewCameraSet.Entry> AppendixE()
        {
            var D = ReviewCameraSet.TimeLabel.Day; var G = ReviewCameraSet.TimeLabel.Golden; var N = ReviewCameraSet.TimeLabel.Night;
            var cam3 = new Vector3(350f, 22f, -2180f); var look3 = new Vector3(200f, 130f, -600f);
            return new List<ReviewCameraSet.Entry>
            {
                Fixed("CAM_01_World_Overview", "World", new Vector3(-200f, 3800f, -6200f), false, new Vector3(0f, 0f, 300f), 55f, D),
                Fixed("CAM_02_DT_Aerial_Day", "D1", new Vector3(-1050f, 1150f, -2350f), false, new Vector3(250f, 60f, -500f), 40f, D),
                Fixed("CAM_03_DT_Skyline_Golden", "D1", cam3, false, look3, 50f, G, "over Crown Bay"),
                Fixed("CAM_04_DT_Skyline_Night", "D1", cam3, false, look3, 50f, N, "as CAM_03"),
                Fixed("CAM_05_DT_Plaza_Closeup", "D1", new Vector3(38f, 1.75f, -508f), true, new Vector3(60f, 33f, -440f), 64f, D),
                Chase("CAM_06_DT_Drive_Crown", "D1", "A1", 300f, "northbound", 30f, 62f, D, false),
                Chase("CAM_07_DT_Drive_Meridian_Night", "D1", "A2", 760f, "eastbound", 30f, 62f, N, true),
                Chase("CAM_08_R1_Viaduct_Night", "D1", "R1", 650f, null, 60f, 64f, N, false),
                Fixed("CAM_09_BR_Overlook_Golden", "D4", new Vector3(560f, 6f, 1490f), true, new Vector3(250f, 60f, -700f), 55f, G, "at VP-01"),
                Chase("CAM_10_BR_Halcyon_Drive", "D4", "A18", 2380f, null, 35f, 62f, D, false),
                Fixed("CAM_11_BR_Switchbacks_Aerial", "D4", new Vector3(1800f, 560f, 750f), false, new Vector3(2390f, 200f, 1400f), 42f, D),
                Chase("CAM_12_H2_Highline_Golden", "D4", "H2", 3950f, "westbound", 60f, 62f, G, false),
                Chase("CAM_13_NM_Lumen_Night", "D6", "A7", 700f, null, 40f, 62f, N, true),
                Fixed("CAM_14_AC_Lighthouse_Golden", "D2", new Vector3(-190f, 2f, -2570f), true, new Vector3(535f, 40f, -2605f), 55f, G, "at VP-03, looking at the bridge"),
                Fixed("CAM_15_IP_Berth_Dusk", "D3", new Vector3(1600f, 2f, -2700f), true, new Vector3(2300f, 40f, -2580f), 60f, ReviewCameraSet.TimeLabel.Dusk, "on Berth Road, looking at the cranes"),
                Fixed("CAM_16_WH_Street_Day", "D5", new Vector3(-2260f, 1.7f, -610f), true, new Vector3(-2200f, 8f, -550f), 64f, D, "Westhaven Plaza"),
                Fixed("CAM_17_FA_Dragway_Aerial", "D7", new Vector3(2500f, 120f, -1100f), false, new Vector3(2900f, 8f, -600f), 45f, D),
                Fixed("CAM_18_RV_MirrorLake_Morning", "D8", new Vector3(-2950f, 3f, 1800f), true, new Vector3(-2950f, 160f, 2600f), 55f, ReviewCameraSet.TimeLabel.Morning, "at VP-08"),
            };
        }

        public static string CreateAll()
        {
            var net = RoadNetwork.Parse(VeyraDataValidator.LoadText("road_network_geometry.json"));
            var reg = LocationRegistry.Parse(VeyraDataValidator.LoadText("location_registry.csv"));
            var log = new StringBuilder();
            var cams = AppendixE();
            foreach (var c in cams.Where(c => c.kind == ReviewCameraSet.Kind.Chase))
            {
                var road = net.Road(c.roadId) ?? throw new InvalidOperationException($"{c.name}: unknown road {c.roadId}");
                var path = RouteAutopilot.RoadPath(road);
                if (c.distanceAlongRoad_m < 0f || c.distanceAlongRoad_m > path.Length) throw new InvalidOperationException($"{c.name}: {c.distanceAlongRoad_m} m is outside {c.roadId} (0–{path.Length:0} m)");
                var t = path.TangentAt(c.distanceAlongRoad_m);
                string compass = c.note;
                c.direction = compass == "northbound" ? (t.z >= 0f ? 1 : -1)
                    : compass == "southbound" ? (t.z >= 0f ? -1 : 1)
                    : compass == "eastbound" ? (t.x >= 0f ? 1 : -1)
                    : compass == "westbound" ? (t.x >= 0f ? -1 : 1)
                    : 1;
                c.note = $"{c.roadId} {road.Name} ({road.Cls}), {(string.IsNullOrEmpty(compass) ? "profile direction (Appendix E gives none)" : compass)} → direction {c.direction:+0;-0}; profile tangent ({t.x:0.00}, {t.z:0.00})" + (c.name == "CAM_08_R1_Viaduct_Night" ? "; Harbor Viaduct" : "");
                log.AppendLine($"{c.name}: {c.note}");
            }
            foreach (var (cam, vp) in new[] { ("CAM_09_BR_Overlook_Golden", "VP-01"), ("CAM_14_AC_Lighthouse_Golden", "VP-03"), ("CAM_18_RV_MirrorLake_Morning", "VP-08") })
            {
                var c = cams.First(x => x.name == cam);
                var l = reg.Get(vp);
                if (l == null) { log.AppendLine($"WARN {cam}: {vp} not in the registry"); continue; }
                var u = l.UnityFromWorld;
                float d = Vector2.Distance(new Vector2(u.x, u.z), new Vector2(c.position.x, c.position.z));
                log.AppendLine($"{cam}: Appendix E (X, Z) ({c.position.x:0}, {c.position.z:0}) vs registry {vp} {l.Name} ({u.x:0}, {u.z:0}): {d:0.0} m apart{(d > 25f ? "  WARN > 25 m" : "")}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            var set = AssetDatabase.LoadAssetAtPath<ReviewCameraSet>(AssetPath);
            if (set == null) { set = ScriptableObject.CreateInstance<ReviewCameraSet>(); AssetDatabase.CreateAsset(set, AssetPath); }
            set.Cameras = cams;
            EditorUtility.SetDirty(set);
            var cfg = AssetDatabase.LoadAssetAtPath<VeyraAutoTestConfig>(TestRouteTools.ConfigPath);
            if (cfg == null) { TestRouteTools.CreateAll(); cfg = AssetDatabase.LoadAssetAtPath<VeyraAutoTestConfig>(TestRouteTools.ConfigPath); }
            cfg.ReviewCameras = set;
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
            log.Insert(0, $"{AssetPath}: {cams.Count} cameras ({cams.Count(c => c.kind == ReviewCameraSet.Kind.Chase)} chase), linked into {TestRouteTools.ConfigPath}{Environment.NewLine}");
            return log.ToString();
        }
    }
}
