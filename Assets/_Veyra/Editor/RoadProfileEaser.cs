using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using VeyraCity.World;
using VeyraCity.World.Data;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card C2 (BUG-001): one source of truth for road heights. The Veyra profiles (WorldData roads = profile_xyz_every_4m in the
    /// Unity frame) contain vertical steps at structure joints, where a bridge deck or tunnel meets a terrain-following approach
    /// (4–8.5 m at the river-bridge abutments). The blockout meshes reproduce them exactly.
    /// <para>Easing: at every joint whose step exceeds the class grade over one sample (+5 cm) and is at most
    /// <see cref="MaxEasedStep"/>, the structure side stays fixed (deck / portal height) and the approach side moves by
    /// Δz·(3t² − 2t³), t = 1 − d/L, over L = max(40 m, |Δz| / (g_max − 0.005)). Then the approach zone is smoothed (two passes of
    /// [1 2 1]/4) to keep the grade change near 1.2 % per 4 m. Steps that join two non-structure samples get the same easing on
    /// the later side. Larger steps (BUG-035: A19 +53 m at a junction) are data errors and are reported, not eased.</para>
    /// <para><see cref="Delta"/> gives the height change along a road; <see cref="RoadProfileApply"/> lays a ribbon on the eased
    /// profile over each eased zone (<see cref="Eased.Zones"/>), moves the props and raises the terrain.</para>
    /// </summary>
    public static class RoadProfileEaser
    {
        public const float MaxEasedStep = 15f;
        public const float MinStep = 1f;

        public sealed class Eased
        {
            public float[] Delta;   // per profile point: eased − original height
            public int Joints, EasedJoints, DataErrors;
            public float MaxStep;
            /// <summary>Eased zones (S range along the road) including 20 m on the fixed side of each joint.</summary>
            public readonly List<Vector2> Zones = new List<Vector2>();
        }

        static Dictionary<int, Eased> cache;

        public static void Reset() => cache = null;

        static float GradeLimit(string cls) => RoadClasses.TryGet(cls, out var c) && c.MaxGrade > 0f ? c.MaxGrade : 0.08f;

        public static Eased For(int roadIndex)
        {
            cache ??= new Dictionary<int, Eased>();
            if (cache.TryGetValue(roadIndex, out var e)) return e;
            var road = WorldData.Roads[roadIndex];
            e = Compute(road);
            cache[roadIndex] = e;
            return e;
        }

        static Eased Compute(RoadPath road)
        {
            var pts = road.Path.Points;
            var S = road.Path.S;
            int n = pts.Length;
            var h = new float[n];
            for (int i = 0; i < n; i++) h[i] = pts[i].y;
            var z = (float[])h.Clone();
            var touched = new bool[n];
            float g = GradeLimit(road.Data.cls);
            var res = new Eased();
            if (road.Data.cls == "RW") { res.Delta = new float[n]; return res; } // runways / taxiways: flat aprons, not graded roads
            for (int i = 0; i + 1 < n; i++)
            {
                float ds = Mathf.Max(0.5f, S[i + 1] - S[i]);
                float step = h[i + 1] - h[i];
                // Only real joints (BUG-001 abutments: 3.3-8.5 m); sub-metre grade noise of the data is BUG-034's, not eased here.
                if (Mathf.Abs(step) <= Mathf.Max(MinStep, g * ds * 3f)) continue;
                res.Joints++;
                res.MaxStep = Mathf.Max(res.MaxStep, Mathf.Abs(step));
                if (Mathf.Abs(step) > MaxEasedStep) { res.DataErrors++; continue; }
                var sa = road.StructureAt(i);
                var sb = road.StructureAt(i + 1);
                // Fixed side: the structure (deck / portal); with structures on both or neither side, ease the later side.
                bool easeBefore = sa == StructureKind.None && sb != StructureKind.None;
                int joint = easeBefore ? i : i + 1;          // first approach sample next to the joint
                float target = easeBefore ? h[i + 1] : h[i]; // height the approach must reach at the joint
                float dz = target - z[joint];
                float L = Mathf.Max(40f, Mathf.Abs(dz) / Mathf.Max(0.01f, g - 0.005f));
                int dirn = easeBefore ? -1 : 1;
                for (int k = joint; k >= 0 && k < n; k += dirn)
                {
                    float d = Mathf.Abs(S[k] - S[joint]);
                    if (d > L) break;
                    if (road.StructureAt(k) != StructureKind.None && k != joint) break; // never move another structure
                    float t = 1f - d / L;
                    z[k] = z[k] + dz * t * t * (3f - 2f * t);
                    touched[k] = true;
                }
                res.EasedJoints++;
                float sj = S[joint];
                res.Zones.Add(easeBefore ? new Vector2(sj - L - 5f, sj + 25f) : new Vector2(sj - 25f, sj + L + 5f));
            }
            // Smooth crests and sags inside the eased zones only (structures and untouched road keep their heights).
            for (int pass = 0; pass < 2; pass++)
            {
                var src = (float[])z.Clone();
                for (int i = 1; i + 1 < n; i++)
                    if (touched[i] && road.StructureAt(i) == StructureKind.None)
                        z[i] = 0.25f * src[i - 1] + 0.5f * src[i] + 0.25f * src[i + 1];
            }
            res.Delta = new float[n];
            for (int i = 0; i < n; i++) res.Delta[i] = z[i] - h[i];
            return res;
        }

        /// <summary>Height change at distance <paramref name="s"/> along road <paramref name="roadIndex"/> (linear between samples).</summary>
        public static float Delta(int roadIndex, float s)
        {
            var e = For(roadIndex);
            var S = WorldData.Roads[roadIndex].Path.S;
            int n = S.Length;
            if (n == 0) return 0f;
            if (s <= S[0]) return e.Delta[0];
            if (s >= S[n - 1]) return e.Delta[n - 1];
            int lo = 0, hi = n - 1;
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (S[mid] <= s) lo = mid; else hi = mid; }
            float t = (s - S[lo]) / Mathf.Max(1e-4f, S[hi] - S[lo]);
            return Mathf.Lerp(e.Delta[lo], e.Delta[hi], t);
        }

        public static bool InZone(int road, float s)
        {
            foreach (var z in For(road).Zones) if (s >= z.x && s <= z.y) return true;
            return false;
        }

        /// <summary>Report of every eased joint (Logs/C2/profile_easing.txt).</summary>
        public static string Report()
        {
            var sb = new StringBuilder("C2 road profile easing\nroad  cls  joints  eased  data_errors  max_step_m  max_delta_m\n");
            int joints = 0, eased = 0, errs = 0;
            for (int r = 0; r < WorldData.Roads.Count; r++)
            {
                var e = For(r);
                if (e.Joints == 0) continue;
                float md = 0f;
                foreach (var d in e.Delta) md = Mathf.Max(md, Mathf.Abs(d));
                joints += e.Joints; eased += e.EasedJoints; errs += e.DataErrors;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-5} {1,-4} {2,6} {3,6} {4,12} {5,11:F2} {6,12:F2}",
                    WorldData.Roads[r].Data.id, WorldData.Roads[r].Data.cls, e.Joints, e.EasedJoints, e.DataErrors, e.MaxStep, md));
            }
            sb.AppendLine($"total: {joints} joints over the class grade, {eased} eased, {errs} data errors (> {MaxEasedStep} m, not eased)");
            return sb.ToString();
        }
    }
}
