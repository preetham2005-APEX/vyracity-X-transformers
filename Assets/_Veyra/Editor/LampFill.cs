using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VeyraCity.World;
using VeyraCity.World.Data;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// World iteration 31 (BUG-129, from Lighting D1: several Crown Downtown / Neon Mile streets, e.g. Meridian at CAM_07, have
    /// no lamp in the placement data, so the street-light pool has nothing to light there). WorldBuildBatch step `lampfill`:
    /// along every AR / CO / SV road inside D1 or D6, wherever no data lamp stands within 40 m, an urban LED lamp
    /// (streetlamp_urban_led_10m, the data prototype) every 32 m, alternating sides, on the kerb line (half-width + 0.7 m)
    /// facing the carriageway; none on bridges / tunnels, within 20 m of a road end (junctions), or in a building footprint.
    /// Added as InstancedPropRenderer placements (so StreetLightPool and the prop colliders treat them like the data lamps);
    /// marked by a cell tag so a re-run first removes its own lamps (idempotent). Prop colliders rebuilt. Report
    /// Logs/C/lampfill.txt (every added lamp).
    /// DERIVED, NOT DATA: these lamps are not in the placement CSVs; they follow a deterministic rule from the road network
    /// data (centreline, class, district polygon, 32 m spacing) and are never hand-placed. Run `propclear` after this step so
    /// none stands inside the rendered carriageway. Listed as a data-authority deviation in PROJECT_STATUS for the user.
    /// </summary>
    public static class LampFill
    {
        const string Proto = "streetlamp_urban_led_10m";
        const float Gap = 40f, Step = 32f;

        public static string Run()
        {
            WorldData.EnsureLoaded();
            var ipr = Object.FindAnyObjectByType<InstancedPropRenderer>();
            if (ipr == null) return "lampfill: no InstancedPropRenderer\n";
            int pi = System.Array.FindIndex(ipr.Prototypes, p => p != null && p.Name == Proto);
            if (pi < 0) return $"lampfill: no prototype {Proto}\n";
            // remove our own earlier lamps: cells flagged by a NaN-free marker scale of exactly 1.0001
            int removed = 0;
            var cells = new List<InstancedPropRenderer.Cell>();
            foreach (var c in ipr.Cells)
            {
                if (c.Prototype == pi && c.Scales != null && c.Scales.Length > 0 && c.Scales.All(s => Mathf.Approximately(s, 1.0001f))) { removed += c.Placements.Length; continue; }
                cells.Add(c);
            }
            // every lamp (any lamp prototype) in a 20 m grid
            var lampGrid = new HashSet<long>();
            long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
            foreach (var c in cells)
            {
                if (c.Placements == null || c.Prototype < 0 || c.Prototype >= ipr.Prototypes.Length || !ipr.Prototypes[c.Prototype].IsLamp) continue;
                foreach (var p in c.Placements) lampGrid.Add(Key(Mathf.FloorToInt(p.x / 20f), Mathf.FloorToInt(p.z / 20f)));
            }
            bool LampNear(Vector3 p)
            {
                int cx = Mathf.FloorToInt(p.x / 20f), cz = Mathf.FloorToInt(p.z / 20f), r = Mathf.CeilToInt(Gap / 20f);
                for (int dx = -r; dx <= r; dx++) for (int dz = -r; dz <= r; dz++) if (lampGrid.Contains(Key(cx + dx, cz + dz))) return true;
                return false;
            }
            var buildings = PlacementReader.Buildings(VeyraDataValidator.LoadText("world_building_placements.csv"), "world_building_placements.csv");
            bool InBuilding(Vector3 p)
            {
                foreach (var b in buildings)
                {
                    float dx = p.x - b.UnityX, dz = p.z - b.UnityZ;
                    if (dx * dx + dz * dz > 120f * 120f) continue;
                    var local = Quaternion.Euler(0f, -b.UnityYawDeg, 0f) * new Vector3(dx, 0f, dz);
                    if (Mathf.Abs(local.x) < b.FootprintWM * 0.5f + 0.5f && Mathf.Abs(local.z) < b.FootprintLM * 0.5f + 0.5f) return true;
                }
                return false;
            }
            var added = new List<Vector4>();
            var rep = new StringBuilder("road,unity_x,unity_y,unity_z,yaw\n");
            var classes = new HashSet<string> { "AR", "CO", "SV" };
            foreach (var road in WorldData.Roads.Where(r => classes.Contains(r.Data.cls)))
            {
                float len = road.Path.Length; int k = 0;
                for (float s = 20f; s < len - 20f; s += Step, k++)
                {
                    var p = road.Path.PositionAt(s);
                    var dist = WorldData.DistrictAt(p);
                    if (dist == null || !(dist.id.StartsWith("D1") || dist.id.StartsWith("D6"))) continue;
                    if (road.StructureAt(road.Path.SegmentAt(s)) != StructureKind.None) continue;
                    var t = road.Path.TangentAt(s); t.y = 0f; t.Normalize();
                    var right = new Vector3(t.z, 0f, -t.x);
                    float side = (k & 1) == 0 ? 1f : -1f;
                    var q = p + right * side * (road.HalfWidth + 0.7f);
                    if (LampNear(q) || InBuilding(q)) continue;
                    // the lamp's arm reaches along its local +Z towards the road (data lamps: LightOffset z > 0)
                    var toRoad = -right * side;
                    float yaw = Mathf.Atan2(toRoad.x, toRoad.z) * Mathf.Rad2Deg;
                    float gy = WorldData.GroundHeight(q, p.y);
                    var v = new Vector4(q.x, Mathf.Max(gy, p.y) + 0.12f - 0.1f, q.z, yaw);
                    added.Add(v);
                    lampGrid.Add(Key(Mathf.FloorToInt(q.x / 20f), Mathf.FloorToInt(q.z / 20f)));
                    rep.Append($"{road.Data.id},{v.x:F1},{v.y:F2},{v.z:F1},{yaw:F0}\n");
                }
            }
            // one cell per 250 m chunk (tight bounds for culling)
            foreach (var grp in added.GroupBy(v => (Mathf.FloorToInt(v.x / 250f), Mathf.FloorToInt(v.z / 250f))))
            {
                var pl = grp.ToArray();
                var b = new Bounds(new Vector3(pl[0].x, pl[0].y, pl[0].z), Vector3.one);
                foreach (var q in pl) b.Encapsulate(new Vector3(q.x, q.y, q.z));
                b.Expand(new Vector3(16f, 40f, 16f));
                cells.Add(new InstancedPropRenderer.Cell { Prototype = pi, Bounds = b, Placements = pl, Scales = pl.Select(_ => 1.0001f).ToArray() });
            }
            ipr.Cells = cells.ToArray();
            EditorUtility.SetDirty(ipr);
            Directory.CreateDirectory("Logs/C");
            File.WriteAllText("Logs/C/lampfill.txt", rep.ToString());
            string col = PropColliderBuilder.Build(false);
            return $"Lamp fill (BUG-129): {added.Count} urban LED lamps added on D1/D6 AR/CO/SV streets with no data lamp within {Gap} m ({removed} earlier fill lamps replaced); {col}\n";
        }
    }
}
