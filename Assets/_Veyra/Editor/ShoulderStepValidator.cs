using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// CLAUDE.md "no vertical step > 0.05 m between adjacent collider surfaces" at road edges (master prompt §1.1).
    /// WorldBuildBatch step `edgesteps` (run on a saved scene: colliders loaded from disk answer raycasts in batch mode).
    /// Every 10 m along every road (runways, structures and junction mouths excepted), on both sides, rays go down from
    /// 3 m above the drive-surface edge every 5 cm from 1 m inside the edge to 12 m outside it. Where two adjacent samples
    /// hit DIFFERENT colliders more than 0.05 m apart in height, that is a seam step. Slopes steeper than 45° on one collider
    /// are counted separately (steep, not a step). Writes Logs/Shoulders/edge_steps.csv (every step) and edge_steps.txt.
    /// </summary>
    public static class ShoulderStepValidator
    {
        const float Along = 10f, Dx = 0.05f, Inside = 1f, Outside = 12f, MaxStep = 0.05f;

        public static string Run()
        {
            WorldData.EnsureLoaded();
            Physics.SyncTransforms();
            var csv = new StringBuilder("road,cls,s_m,side,lat_m,x,z,y_inner,y_outer,step_m,inner_collider,outer_collider\n");
            var hits = new List<WorldData.RoadHit>();
            int lines = 0, seamSteps = 0, steep = 0, linesWithStep = 0;
            var perClass = new Dictionary<string, int[]>();
            var buf = new RaycastHit[8];
            for (int r = 0; r < WorldData.Roads.Count; r++)
            {
                var road = WorldData.Roads[r];
                if (road.Data.cls == "RW") continue;
                var path = road.Path;
                if (!perClass.TryGetValue(road.Data.cls, out var pc)) perClass[road.Data.cls] = pc = new int[2];
                for (float s = 5f; s < path.Length - 5f; s += Along)
                {
                    if (road.StructureAt(path.SegmentAt(s)) != StructureKind.None) continue;
                    var c = path.PositionAt(s);
                    var tan = path.TangentAt(s); tan.y = 0f; if (tan.sqrMagnitude < 1e-6f) continue; tan.Normalize();
                    var right = new Vector3(tan.z, 0f, -tan.x);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var edge = c + right * side * road.HalfWidth;
                        WorldData.RoadsAt(edge + right * side * 1.2f, -0.5f, 3f, hits);
                        if (hits.Any(h => h.Road != r)) continue;   // junction mouth
                        lines++; pc[0]++;
                        float prevY = float.NaN; Collider prevC = null; bool any = false;
                        for (float lat = -Inside; lat <= Outside; lat += Dx)
                        {
                            var o = edge + right * side * lat; o.y = c.y + 3f;
                            int n = Physics.RaycastNonAlloc(o, Vector3.down, buf, 12f, ~0, QueryTriggerInteraction.Ignore);
                            if (n == 0) { prevY = float.NaN; prevC = null; continue; }
                            int best = 0; for (int i = 1; i < n; i++) if (buf[i].distance < buf[best].distance) best = i;
                            float y = buf[best].point.y; var col = buf[best].collider;
                            if (!float.IsNaN(prevY))
                            {
                                float dy = Mathf.Abs(y - prevY);
                                if (dy > MaxStep && col != prevC)
                                {
                                    seamSteps++; any = true;
                                    if (seamSteps <= 20000)
                                        csv.Append($"{road.Data.id},{road.Data.cls},{s:F0},{(side < 0 ? "L" : "R")},{lat:F2},{o.x:F1},{o.z:F1},{prevY:F2},{y:F2},{y - prevY:F2},{prevC.name},{col.name}\n");
                                }
                                else if (dy > Dx) steep++;
                            }
                            prevY = y; prevC = col;
                        }
                        if (any) { linesWithStep++; pc[1]++; }
                    }
                }
            }
            var sb = new StringBuilder($"Edge steps {DateTime.Now:yyyy-MM-dd HH:mm}: {lines:N0} edge lines (every {Along} m, both sides), " +
                                       $"{linesWithStep:N0} with a collider seam step > {MaxStep} m ({(lines > 0 ? 100.0 * linesWithStep / lines : 0):F2} %), " +
                                       $"{seamSteps:N0} seam steps in all; {steep:N0} samples steeper than 45° on one collider (not steps)\n");
            foreach (var kv in perClass.OrderBy(k => k.Key)) sb.AppendLine($"  {kv.Key}: {kv.Value[1]:N0} of {kv.Value[0]:N0} edge lines with a step");
            Directory.CreateDirectory("Logs/Shoulders");
            File.WriteAllText("Logs/Shoulders/edge_steps.csv", csv.ToString());
            File.WriteAllText("Logs/Shoulders/edge_steps.txt", sb.ToString());
            return sb.ToString();
        }
    }
}
