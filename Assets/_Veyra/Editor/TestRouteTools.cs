using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VeyraCity.Testing;
using VeyraCity.World;
using VeyraCity.World.Data;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card V0: creates/updates the test routes (Assets/_Veyra/Data/TestRoutes/R_*.asset) and the runtime config
    /// (Assets/_Veyra/Resources/Testing/VeyraAutoTestConfig.asset). Re-runnable: assets are updated in place.
    /// R_SMOKE and R_BRIDGES use the card's segments; R_DOWNTOWN_NIGHT is derived from RACE-16's roads in the data.
    /// </summary>
    public static class TestRouteTools
    {
        public const string RouteDir = "Assets/_Veyra/Data/TestRoutes";
        public const string ConfigPath = "Assets/_Veyra/Resources/Testing/VeyraAutoTestConfig.asset";
        public const string RoadJsonPath = VeyraDataValidator.DataDir + "/road_network_geometry.json";

        [MenuItem("Veyra/Test/Create Test Routes")]
        public static void Menu() => Debug.Log(CreateAll());

        public static string CreateAll()
        {
            var net = RoadNetwork.Parse(VeyraDataValidator.LoadText("road_network_geometry.json"));
            var log = new StringBuilder();

            var smoke = new List<VeyraTestRoute.Segment>
            {
                Seg("A1", 1350f, 2075f, 60f, "Crown Avenue across Crown Bridge"),
                Seg("A2", 1580f, 1820f, 60f, "Meridian Boulevard across Meridian Bridge"),
            };

            float Fast(string road) { var c = net.Road(road)?.Cls; return c == "MW" || c == "EX" || c == "HW" ? 90f : 60f; }
            var bridgeSpans = new (string road, float s0, float s1, string note)[]
            {
                ("R1", 0f, 2900f, "Harbor Viaduct + Sable Mouth Span"),
                ("R1", 5400f, 6300f, "Kestrel Tunnel"),
                ("R1", 7130f, 7540f, "West Sable Bridge"),
                ("C1", 3600f, 7260f, "Lantern Strait Bridge + Gantry Expressway"),
                ("H2", 0f, 1500f, "Corvo Tunnel + Corvo Creek Viaduct"),
                ("H2", 1330f, 2860f, "Blackridge Tunnel"),
                ("H2", 3690f, 4300f, "Highline Viaduct"),
                ("A1", 1476f, 1889f, "Crown Bridge"),
                ("A2", 1483f, 1919f, "Meridian Bridge"),
                ("A4", 1337f, 1786f, "Sable Drawbridge"),
                ("A10", 1304f, 1744f, "Union Street Bridge"),
                ("A22", 1562f, 1880f, "Regent Street Bridge (to road end)"),
                ("H3", 584f, 957f, "Millbrook Bridge"),
                ("A15", 2020f, 2386f, "Outflow Bridge"),
            };
            var bridges = bridgeSpans.Select(b => Seg(b.road, b.s0, b.s1, Fast(b.road), b.note)).ToList();

            var night = Race16Lap(net, 70f, log);

            Directory.CreateDirectory(RouteDir);
            var routes = new List<VeyraTestRoute>
            {
                Save("R_SMOKE", "≈ 60 s smoke drive: Crown Avenue over Crown Bridge, then Meridian Boulevard over Meridian Bridge (card V0).", 12f, smoke, net, log),
                Save("R_BRIDGES", "Every structure with 150 m approaches, 90 km/h on MW/EX/HW and 60 km/h elsewhere (card V0).", 12f, bridges, net, log),
                Save("R_DOWNTOWN_NIGHT", "RACE-16 Midnight Crown Circuit (A22, A2, A1, A4): one lap at 70 km/h at 23:00 (card V0).", 23f, night, net, log),
            };

            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            var cfg = AssetDatabase.LoadAssetAtPath<VeyraAutoTestConfig>(ConfigPath);
            if (cfg == null) { cfg = ScriptableObject.CreateInstance<VeyraAutoTestConfig>(); AssetDatabase.CreateAsset(cfg, ConfigPath); }
            cfg.RoadNetworkJson = AssetDatabase.LoadAssetAtPath<TextAsset>(RoadJsonPath);
            cfg.Routes = routes;
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
            log.AppendLine($"Config {ConfigPath}: road JSON {(cfg.RoadNetworkJson != null ? "linked" : "MISSING")}, {routes.Count} routes");
            return log.ToString();
        }

        static VeyraTestRoute.Segment Seg(string road, float s0, float s1, float kmh, string note) =>
            new VeyraTestRoute.Segment { roadId = road, sStart_m = s0, sEnd_m = s1, targetSpeed_kmh = kmh, note = note };

        static VeyraTestRoute Save(string name, string description, float hour, List<VeyraTestRoute.Segment> segs, RoadNetworkData net, StringBuilder log)
        {
            foreach (var s in segs)
            {
                var road = net.Road(s.roadId) ?? throw new InvalidOperationException($"{name}: unknown road {s.roadId}");
                float len = RouteAutopilot.RoadPath(road).Length;
                if (Mathf.Min(s.sStart_m, s.sEnd_m) < 0f || Mathf.Max(s.sStart_m, s.sEnd_m) > len + 1f)
                    throw new InvalidOperationException($"{name}: segment {s} is outside road {s.roadId} (0–{len:0} m)");
            }
            string path = $"{RouteDir}/{name}.asset";
            var r = AssetDatabase.LoadAssetAtPath<VeyraTestRoute>(path);
            if (r == null) { r = ScriptableObject.CreateInstance<VeyraTestRoute>(); AssetDatabase.CreateAsset(r, path); }
            r.Description = description;
            r.DefaultTimeOfDay = hour;
            r.Segments = segs;
            EditorUtility.SetDirty(r);
            log.AppendLine($"{name}: {segs.Count} segments, {r.TotalLengthM / 1000f:0.00} km, ≈ {r.NominalSeconds:0} s at target speed, {hour:00}:00");
            foreach (var s in segs)
            {
                var road = net.Road(s.roadId);
                var covered = net.Structures.Where(st => st.Road == s.roadId && st.S1 >= Mathf.Min(s.sStart_m, s.sEnd_m) && st.S0 <= Mathf.Max(s.sStart_m, s.sEnd_m)).Select(st => st.Name);
                log.AppendLine($"   {s} ({road.Cls}) {s.note} | structures: {string.Join(", ", covered)}");
            }
            return r;
        }

        /// <summary>One lap of RACE-16 from its start point: junction stations are the closest approach of consecutive roads.</summary>
        static List<VeyraTestRoute.Segment> Race16Lap(RoadNetworkData net, float kmh, StringBuilder log)
        {
            var rr = net.RaceRoutes.FirstOrDefault(r => r.Id == "RACE-16") ?? throw new InvalidOperationException("RACE-16 not in the road JSON");
            var roads = rr.Roads;
            var start = Core.WorldCoordinates.ToUnity(rr.Start[0], rr.Start[1], 0f);
            int hint = 0;
            float sStart = RouteAutopilot.RoadPath(net.Road(roads[0])).Project(start, ref hint, 0);
            var segs = new List<VeyraTestRoute.Segment>();
            float sIn = sStart;
            for (int k = 0; k < roads.Length; k++)
            {
                string a = roads[k], b = roads[(k + 1) % roads.Length];
                Junction(net.Road(a), net.Road(b), out float sa, out float sb, out float gap);
                log.AppendLine($"   RACE-16 junction {a}→{b}: {a} s={sa:0}, {b} s={sb:0} (gap {gap:0.0} m)");
                if (gap > 15f) throw new InvalidOperationException($"RACE-16: roads {a} and {b} do not meet (closest {gap:0} m)");
                segs.Add(Seg(a, Mathf.Round(sIn), Mathf.Round(sa), kmh, k == 0 ? "RACE-16 start → " + b : "→ " + b));
                sIn = sb;
            }
            segs.Add(Seg(roads[0], Mathf.Round(sIn), Mathf.Round(sStart), kmh, "→ RACE-16 start/finish"));
            float lap = segs.Sum(s => s.LengthM);
            log.AppendLine($"   RACE-16 lap from data: {lap:0} m (design length {rr.LengthM:0} m, kind '{rr.Kind}')");
            return segs;
        }

        static void Junction(RoadRecord a, RoadRecord b, out float sa, out float sb, out float gap)
        {
            var pa = RouteAutopilot.RoadPath(a);
            var pb = RouteAutopilot.RoadPath(b);
            float best = float.MaxValue; int ia = 0, ib = 0;
            for (int i = 0; i < pa.Count; i++)
                for (int j = 0; j < pb.Count; j++)
                {
                    float dx = pa.Points[i].x - pb.Points[j].x, dz = pa.Points[i].z - pb.Points[j].z;
                    float d = dx * dx + dz * dz;
                    if (d < best) { best = d; ia = i; ib = j; }
                }
            sa = pa.S[ia]; sb = pb.S[ib]; gap = Mathf.Sqrt(best);
        }
    }
}
