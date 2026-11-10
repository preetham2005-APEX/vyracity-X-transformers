using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using VeyraCity.VehiclePhysics;
using VeyraCity.World.Data;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card C1: "Veyra/Physics/Validate Drivable". For every road of road_network_geometry.json, every 2 m along the profile
    /// (profile_xyz_every_4m, interpolated, plus the C2 easing) and every 1 m across the carriageway, a ray goes down from 2.5 m above the profile
    /// height with the vehicle's mask (all layers, triggers ignored: VehicleController.CastWheel). Each sample is:
    /// <list type="bullet">
    /// <item>ok: first hit is a drivable collider (VehicleSurface in its parents) of the expected surface (gravel on GR roads,
    /// asphalt or concrete elsewhere), within 0.5 m of the profile height;</item>
    /// <item>miss: nothing within 6 m below; blocked: the first hit is a collider without a VehicleSurface (a wall or box in
    /// the carriageway, BUG-051); wrong_surface; off_profile: the drivable hit is more than 0.5 m from the profile height.</item>
    /// </list>
    /// The median of divided roads (MW, EX: |offset| &lt; 1.5 m) is skipped. Writes Logs/drivable_report.csv (one row per
    /// failing sample plus a per-road summary), Logs/drivable_summary.txt and a top-down heat map Logs/drivable_misses.png
    /// (1 px = 4 m; green ok, red miss, magenta blocked, orange wrong surface, yellow off profile).
    /// </summary>
    public static class DrivableValidator
    {
        const float Step = 2f, Across = 1f, Above = 2.5f, Below = 6f, ProfileTolerance = 0.5f, Median = 1.5f;

        public enum Result { Ok, Miss, Blocked, WrongSurface, OffProfile }

        public sealed class Summary
        {
            public int Samples, Ok, Miss, Blocked, WrongSurface, OffProfile;
            public readonly Dictionary<string, int[]> PerRoad = new Dictionary<string, int[]>();
            public override string ToString() =>
                $"{Samples:N0} samples: ok {Ok:N0}, miss {Miss:N0}, blocked {Blocked:N0}, wrong surface {WrongSurface:N0}, off profile {OffProfile:N0} " +
                $"({(Samples > 0 ? 100.0 * Ok / Samples : 0):F2} % ok)";
        }

        [MenuItem("Veyra/Physics/Validate Drivable")]
        static void Menu() => Debug.Log(Run("Logs"));

        public static string Run(string outDir)
        {
            var net = RoadNetwork.Parse(File.ReadAllText("Assets/_Veyra/Data/road_network_geometry.json"));
            Physics.SyncTransforms();
            var s = new Summary();
            var csv = new StringBuilder("road,cls,s_m,offset_m,x,y,z_profile,result,hit_collider,hit_y,surface\n");
            const int W = 2000, H = 1500; // 1 px = 4 m over the 8 x 6 km map
            var img = new Color32[W * H];
            for (int i = 0; i < img.Length; i++) img[i] = new Color32(24, 24, 28, 255);
            var cache = new Dictionary<Collider, SurfaceDefinition>();
            // BUG-108: measure against the eased profile (card C2), not the raw data: eased bridge approaches sit up to 8.5 m
            // above the data, so rays from 2.5 m above the raw profile started under the road (false misses and grass hits)
            RoadProfileEaser.Reset();
            var eased = new Dictionary<string, int>();
            for (int i = 0; i < VeyraCity.World.WorldData.Roads.Count; i++) eased[VeyraCity.World.WorldData.Roads[i].Data.id] = i;
            foreach (var road in net.Roads)
            {
                if (road.Closed && road.Cls == "RW") continue;
                var info = RoadClasses.Get(road.Cls);
                bool divided = road.Cls == "MW" || road.Cls == "EX";
                float half = info.WidthM / 2f - 0.75f;
                var counts = new int[5];
                var p = road.ProfileXYZ;
                float length = (p.Length - 1) * 4f;
                int hint = -1;
                for (float sAlong = 0f; sAlong <= length; sAlong += Step)
                {
                    float f = sAlong / 4f;
                    int i0 = Mathf.Min((int)f, p.Length - 2);
                    float t = f - i0;
                    var a = new Vector3(p[i0][0], p[i0][1], p[i0][2]);
                    var b = new Vector3(p[i0 + 1][0], p[i0 + 1][1], p[i0 + 1][2]);
                    var c = Vector3.Lerp(a, b, t); // data frame: x east, y north, z up
                    if (eased.TryGetValue(road.Id, out int wi))
                    {
                        // the eased delta lives on WorldData's path, whose station need not match this profile's (direction,
                        // start): project the sample onto it, tracking a segment hint along the road
                        var wpath = VeyraCity.World.WorldData.Roads[wi].Path;
                        var u = new Vector3(c.x - 4000f, c.z, c.y - 3000f);
                        if (hint < 0)
                        {
                            float bd = float.MaxValue;
                            for (int k = 0; k < wpath.Count; k++)
                            {
                                float d2 = (new Vector2(wpath.Points[k].x - u.x, wpath.Points[k].z - u.z)).sqrMagnitude;
                                if (d2 < bd) { bd = d2; hint = k; }
                            }
                        }
                        c.z += RoadProfileEaser.Delta(wi, wpath.Project(u, ref hint, 40));
                    }
                    var dir = new Vector2(b.x - a.x, b.y - a.y);
                    if (dir.sqrMagnitude < 1e-6f) continue;
                    dir.Normalize();
                    var right = new Vector2(dir.y, -dir.x);
                    for (float o = -half; o <= half + 1e-3f; o += Across)
                    {
                        if (divided && Mathf.Abs(o) < Median) continue;
                        var xy = new Vector2(c.x, c.y) + right * o;
                        var origin = new Vector3(xy.x - 4000f, c.z + Above, xy.y - 3000f);
                        var r = Sample(origin, c.z, road.Cls, cache, out var hit, out var surf);
                        s.Samples++;
                        counts[(int)r]++;
                        switch (r)
                        {
                            case Result.Ok: s.Ok++; break;
                            case Result.Miss: s.Miss++; break;
                            case Result.Blocked: s.Blocked++; break;
                            case Result.WrongSurface: s.WrongSurface++; break;
                            default: s.OffProfile++; break;
                        }
                        int px = Mathf.Clamp((int)(xy.x / 4f), 0, W - 1), py = Mathf.Clamp((int)(xy.y / 4f), 0, H - 1);
                        var col = r == Result.Ok ? new Color32(40, 170, 70, 255) : r == Result.Miss ? new Color32(255, 30, 30, 255)
                            : r == Result.Blocked ? new Color32(255, 0, 255, 255) : r == Result.WrongSurface ? new Color32(255, 140, 0, 255) : new Color32(255, 230, 0, 255);
                        ref var dst = ref img[py * W + px];
                        if (r != Result.Ok || dst.r != 255) dst = col; // every failure colour has r = 255: a failure is never painted over by ok
                        if (r != Result.Ok)
                            csv.Append(road.Id).Append(',').Append(road.Cls).Append(',').Append(F(sAlong)).Append(',').Append(F(o)).Append(',')
                               .Append(F(xy.x)).Append(',').Append(F(xy.y)).Append(',').Append(F(c.z)).Append(',').Append(r).Append(',')
                               .Append(hit.collider != null ? hit.collider.name : "").Append(',').Append(hit.collider != null ? F(hit.point.y) : "").Append(',')
                               .Append(surf != null ? surf.Id : "").Append('\n');
                    }
                }
                s.PerRoad[road.Id] = counts;
            }
            Directory.CreateDirectory(outDir);
            var sb = new StringBuilder();
            sb.AppendLine("C1 Validate Drivable — " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine(s.ToString());
            sb.AppendLine("road  ok  miss  blocked  wrong_surface  off_profile  (roads with failures)");
            foreach (var kv in s.PerRoad)
                if (kv.Value[1] + kv.Value[2] + kv.Value[3] + kv.Value[4] > 0)
                    sb.AppendLine($"{kv.Key,-6} {kv.Value[0],7} {kv.Value[1],6} {kv.Value[2],8} {kv.Value[3],14} {kv.Value[4],12}");
            File.WriteAllText(Path.Combine(outDir, "drivable_report.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(outDir, "drivable_summary.txt"), sb.ToString());
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.SetPixels32(img);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(outDir, "drivable_misses.png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return sb.ToString();
        }

        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        static Result Sample(Vector3 origin, float profileZ, string cls, Dictionary<Collider, SurfaceDefinition> cache, out RaycastHit hit, out SurfaceDefinition surf)
        {
            surf = null;
            if (!Physics.Raycast(origin, Vector3.down, out hit, Above + Below, ~0, QueryTriggerInteraction.Ignore)) return Result.Miss;
            if (!cache.TryGetValue(hit.collider, out surf))
            {
                var tag = hit.collider.GetComponentInParent<VehicleSurface>();
                surf = tag != null ? tag.Surface : null;
                cache[hit.collider] = surf;
            }
            if (surf == null) return Result.Blocked;
            string id = surf.Id ?? "";
            bool gravelRoad = cls == "GR";
            bool expected = gravelRoad ? id.Contains("gravel") || id.Contains("dirt") : id.Contains("asphalt") || id.Contains("concrete") || id.Contains("cobble") || id.Contains("metal") || id.Contains("kerb");
            if (!expected) return Result.WrongSurface;
            return Mathf.Abs(hit.point.y - profileZ) > ProfileTolerance ? Result.OffProfile : Result.Ok;
        }
    }
}
