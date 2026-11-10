using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;
using VeyraCity.World.Data;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card C2: "Veyra/Physics/Validate Road Profiles". Samples the drivable collider height every 1 m along every road's
    /// centre and both outer lanes (like a wheel: raycast down from 0.6 m above the eased profile, vehicle mask, triggers ignored,
    /// first drivable hit) and checks:
    /// step between 1 m samples ≤ 0.05 m + grade allowance (class grade × 1 m); grade (over 4 m) ≤ class limit + 0.5 %;
    /// grade change ≤ 1.2 % per 4 m (structures' expansion joints included). Writes Logs/C2/&lt;label&gt;/road_profile_report.csv
    /// (per-road counts), violations.csv (first 5,000), summary.txt, and for each of the 17 structures a CSV and a PNG plot
    /// of collider height (centre lane) against the original profile from s0 − 200 m to s1 + 200 m (BUG-001 before / after).
    /// </summary>
    public static class RoadProfileValidator
    {
        [MenuItem("Veyra/Physics/Validate Road Profiles")]
        static void Menu() => Debug.Log(Run("after"));

        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        internal static bool ColliderHeight(Vector3 p, out float y)
        {
            y = 0f;
            var hits = Physics.RaycastAll(p + Vector3.up * 0.6f, Vector3.down, 6.6f, ~0, QueryTriggerInteraction.Ignore);
            // The wheels take the first surface below them (VehicleController.CastWheel: nearest hit along the ray), so the
            // measured height is the highest drivable hit below a wheel mount 0.6 m above the eased profile (down to -6 m).
            bool found = false;
            foreach (var h in hits)
            {
                if (h.collider.GetComponentInParent<VehicleSurface>() == null) continue;
                if (!found || h.point.y > y) { y = h.point.y; found = true; }
            }
            return found;
        }

        public static string Run(string label)
        {
            string dir = Path.Combine("Logs", "C2", label);
            Directory.CreateDirectory(dir);
            Physics.SyncTransforms();
            RoadProfileEaser.Reset();
            // "before": the scene still has the original profile, so the rays follow it; afterwards they follow the eased one.
            float k = label == "before" ? 0f : 1f;
            var per = new StringBuilder("road,cls,length_m,samples,missing,steps,grade_over,grade_change_over,max_step_m,max_grade_pct\n");
            var viol = new StringBuilder("road,lane,s_m,kind,value\n");
            int totalSteps = 0, totalGrade = 0, totalChange = 0, totalMissing = 0, roadsBad = 0, violRows = 0;
            var roads = WorldData.Roads;
            for (int r = 0; r < roads.Count; r++)
            {
                var road = roads[r];
                if (road.Data.cls == "RW") continue;
                float g = RoadClasses.TryGet(road.Data.cls, out var info) && info.MaxGrade > 0f ? info.MaxGrade : 0.08f;
                float len = road.Path.Length;
                int n = Mathf.FloorToInt(len);
                int steps = 0, gradeOver = 0, changeOver = 0, missing = 0;
                float maxStep = 0f, maxGrade = 0f;
                float lane = Mathf.Max(0f, road.HalfWidth - 2f);
                for (int laneIdx = -1; laneIdx <= 1; laneIdx++)
                {
                    var y = new float[n + 1];
                    var ok = new bool[n + 1];
                    for (int i = 0; i <= n; i++)
                    {
                        float s = i;
                        var c = road.Path.PositionAt(s);
                        var ahead = road.Path.PositionAt(Mathf.Min(len, s + 1f)) - road.Path.PositionAt(Mathf.Max(0f, s - 1f));
                        var right = new Vector3(ahead.z, 0f, -ahead.x).normalized;
                        var p = c + right * (laneIdx * lane);
                        p.y = c.y + k * RoadProfileEaser.Delta(r, s);
                        ok[i] = ColliderHeight(p, out y[i]);
                        if (!ok[i]) missing++;
                    }
                    for (int i = 1; i <= n; i++)
                    {
                        if (!ok[i] || !ok[i - 1]) continue;
                        float st = Mathf.Abs(y[i] - y[i - 1]);
                        maxStep = Mathf.Max(maxStep, st);
                        if (st > 0.05f + g * 1f) { steps++; if (violRows++ < 5000) viol.AppendLine($"{road.Data.id},{laneIdx},{i},step,{F(st)}"); }
                    }
                    float prevGrade = float.NaN;
                    for (int i = 4; i <= n; i += 4)
                    {
                        if (!ok[i] || !ok[i - 4]) { prevGrade = float.NaN; continue; }
                        float grade = (y[i] - y[i - 4]) / 4f;
                        maxGrade = Mathf.Max(maxGrade, Mathf.Abs(grade));
                        if (Mathf.Abs(grade) > g + 0.005f) { gradeOver++; if (violRows++ < 5000) viol.AppendLine($"{road.Data.id},{laneIdx},{i},grade,{F(grade * 100f)}"); }
                        if (!float.IsNaN(prevGrade) && Mathf.Abs(grade - prevGrade) > 0.012f) { changeOver++; if (violRows++ < 5000) viol.AppendLine($"{road.Data.id},{laneIdx},{i},grade_change,{F((grade - prevGrade) * 100f)}"); }
                        prevGrade = grade;
                    }
                }
                totalSteps += steps; totalGrade += gradeOver; totalChange += changeOver; totalMissing += missing;
                if (steps + gradeOver > 0) roadsBad++;
                per.AppendLine($"{road.Data.id},{road.Data.cls},{F(len)},{3 * (n + 1)},{missing},{steps},{gradeOver},{changeOver},{F(maxStep)},{F(maxGrade * 100f)}");
            }
            // BUG-001: the 17 structures, centre lane, s0 - 200 .. s1 + 200.
            var net = RoadNetwork.Parse(File.ReadAllText("Assets/_Veyra/Data/road_network_geometry.json"));
            var sb = new StringBuilder();
            int structSteps = 0;
            foreach (var st in net.Structures)
            {
                int r = Enumerable.Range(0, roads.Count).FirstOrDefault(k => roads[k].Data.id == st.Road);
                if (roads[r].Data.id != st.Road) continue;
                var road = roads[r];
                var csv = new StringBuilder("s_m,profile_y,eased_y,collider_y\n");
                float a = Mathf.Max(0f, st.S0 - 200f), b = Mathf.Min(road.Path.Length, st.S1 + 200f);
                int cnt = Mathf.FloorToInt(b - a) + 1;
                var col = new float[cnt];
                var prof = new float[cnt];
                var okA = new bool[cnt];
                int stSteps = 0;
                float stMax = 0f;
                for (int i = 0; i < cnt; i++)
                {
                    float s = a + i;
                    var c = road.Path.PositionAt(s);
                    // Right-hand carriageway centre (half of the half width), not the centre line: medians and their barriers
                    // sit on the centre line of divided and boulevard roads.
                    var ahead = road.Path.PositionAt(Mathf.Min(road.Path.Length, s + 1f)) - road.Path.PositionAt(Mathf.Max(0f, s - 1f));
                    var side = new Vector3(ahead.z, 0f, -ahead.x).normalized * (road.HalfWidth * 0.5f);
                    prof[i] = c.y;
                    float eased = c.y + k * RoadProfileEaser.Delta(r, s);
                    okA[i] = ColliderHeight(new Vector3(c.x + side.x, eased, c.z + side.z), out col[i]);
                    csv.AppendLine($"{F(s)},{F(c.y)},{F(eased)},{(okA[i] ? F(col[i]) : "")}");
                    if (i > 0 && okA[i] && okA[i - 1])
                    {
                        float d = Mathf.Abs(col[i] - col[i - 1]);
                        stMax = Mathf.Max(stMax, d);
                        if (d > 0.05f + (RoadClasses.TryGet(road.Data.cls, out var ci) ? ci.MaxGrade : 0.08f)) stSteps++;
                    }
                }
                structSteps += stSteps;
                string safe = st.Name.Replace(' ', '_');
                File.WriteAllText(Path.Combine(dir, $"structure_{safe}.csv"), csv.ToString());
                Plot(Path.Combine(dir, $"structure_{safe}.png"), prof, col, okA, (st.S0 - a), (st.S1 - a));
                sb.AppendLine($"{st.Name,-26} {st.Road,-5} {st.Kind,-13} steps {stSteps,4}  max step {stMax,6:F2} m");
            }
            File.WriteAllText(Path.Combine(dir, "road_profile_report.csv"), per.ToString());
            File.WriteAllText(Path.Combine(dir, "violations.csv"), viol.ToString());
            string summary = $"C2 road profile validation ({label}) — {System.DateTime.Now:yyyy-MM-dd HH:mm}\n" +
                             $"roads with step/grade violations: {roadsBad}; steps > 0.05 m + grade: {totalSteps}; grade > limit + 0.5 %: {totalGrade}; " +
                             $"grade change > 1.2 %/4 m: {totalChange}; samples without a drivable collider: {totalMissing}\n" +
                             $"structures (right carriageway centre, s0-200..s1+200): {structSteps} steps\n" + sb +
                             RoadProfileEaser.Report();
            File.WriteAllText(Path.Combine(dir, "summary.txt"), summary);
            return summary;
        }

        /// <summary>Profile (grey) and collider (green, red where a step) heights; structure span shaded blue.</summary>
        static void Plot(string path, float[] prof, float[] col, bool[] ok, float s0, float s1)
        {
            const int W = 1200, H = 400;
            var px = new Color32[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(250, 250, 250, 255);
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < prof.Length; i++) { lo = Mathf.Min(lo, prof[i]); hi = Mathf.Max(hi, prof[i]); if (ok[i]) { lo = Mathf.Min(lo, col[i]); hi = Mathf.Max(hi, col[i]); } }
            lo -= 2f; hi += 2f;
            int X(float i) => Mathf.Clamp((int)(i / Mathf.Max(1, prof.Length - 1) * (W - 1)), 0, W - 1);
            int Y(float v) => Mathf.Clamp((int)((v - lo) / Mathf.Max(0.1f, hi - lo) * (H - 1)), 0, H - 1);
            for (int x = X(s0); x <= X(s1); x++) for (int y = 0; y < H; y++) px[y * W + x] = new Color32(225, 235, 250, 255);
            void Dot(int x, int y, Color32 c) { for (int dy = -1; dy <= 1; dy++) { int yy = Mathf.Clamp(y + dy, 0, H - 1); px[yy * W + x] = c; } }
            for (int i = 0; i < prof.Length; i++)
            {
                Dot(X(i), Y(prof[i]), new Color32(150, 150, 150, 255));
                if (!ok[i]) continue;
                bool step = i > 0 && ok[i - 1] && Mathf.Abs(col[i] - col[i - 1]) > 0.15f;
                Dot(X(i), Y(col[i]), step ? new Color32(230, 30, 30, 255) : new Color32(30, 160, 60, 255));
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
