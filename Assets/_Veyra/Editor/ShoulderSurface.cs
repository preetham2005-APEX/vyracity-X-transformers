using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Master prompt §1.1 (the car gets stuck at road edges; kerb, sidewalk and skirt were render-only and DriveSurface only
    /// lowers terrain): WorldBuildBatch step `shoulders`, run after the drive surface. For every road (runways excepted), on
    /// both sides, a collision-only ribbon continues the drive surface (World/DriveSurface, read back from its chunk meshes so
    /// the shoulder starts exactly on the carriageway's collision edge) outwards and meets the terrain:
    /// <list type="bullet">
    /// <item>urban roads (AR, CO, SV): a 0.30 m chamfered kerb ramp up 0.15 m (no collision step; the rendered kerb face is
    /// 15 cm), the sidewalk (3 m AR, 2.5 m CO/SV, 2 % crossfall), then a slope to the terrain height (about 1:2, 0.5-4 m);</item>
    /// <item>rural roads: the 0.8 m shoulder (5 cm fall), then the embankment to the terrain height (about 1:2, 0.5-6 m);</item>
    /// <item>none on structures (bridges, elevated, tunnels: parapets) or where another road's carriageway is beside the edge
    /// (junction mouths, as RoadRenderBuilder opens its kerbs).</item>
    /// </list>
    /// Outer edge = the terrain height there (Terrain.SampleHeight), so the seam to the TerrainCollider has no step. Surfaces:
    /// concrete (kerb + sidewalk) and gravel (rural shoulder). Non-convex MeshColliders, no renderer, 100 m chunks under
    /// World/Shoulders. Idempotent (root and meshes rebuilt). Report Logs/Shoulders/shoulders.txt.
    /// </summary>
    public static class ShoulderSurface
    {
        const string RootName = "Shoulders";
        const string MeshDir = "Assets/_Veyra/World/Generated/Shoulders";
        const float Step = 2f, ColumnSpacing = 2.5f, ChunkLength = 100f;
        static readonly HashSet<string> Urban = new HashSet<string> { "AR", "CO", "SV" };

        public static string Run()
        {
            WorldData.EnsureLoaded();
            var sb = new StringBuilder($"Shoulders {DateTime.Now:yyyy-MM-dd HH:mm}\n");
            var world = EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == "World");
            if (world == null) return "FAIL: no World root\n";
            var ds = world.transform.Find("DriveSurface");
            if (ds == null) return "FAIL: no World/DriveSurface (run the drive surface first)\n";
            var old = world.transform.Find(RootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var root = new GameObject(RootName) { isStatic = true }.transform;
            root.SetParent(world.transform, false);
            Directory.CreateDirectory(MeshDir);
            SurfaceDefinition Surf(string id) => AssetDatabase.LoadAssetAtPath<SurfaceDefinition>($"Assets/_Veyra/Data/Surfaces/{id}.asset");
            var concrete = Surf("concrete"); var gravel = Surf("gravel"); var grassS = Surf("grass");
            if (concrete == null || gravel == null || grassS == null) return "FAIL: Data/Surfaces concrete, gravel or grass missing\n";
            var kerbS = Surf("kerb") ?? concrete;   // D2's kerb surface when present
            // per band: urban kerb ramp, sidewalk, slope to terrain; rural shoulder, embankment
            var bandsUrban = new[] { (kerbS, "K"), (concrete, "S"), (grassS, "E") };
            var bandsRural = new[] { (gravel, "G"), (grassS, "E") };
            var terrains = UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include);
            float TerrainY(Vector3 p)
            {
                foreach (var t in terrains)
                {
                    var o = t.GetPosition(); var s = t.terrainData.size;
                    if (p.x >= o.x && p.x <= o.x + s.x && p.z >= o.z && p.z <= o.z + s.z) return t.SampleHeight(p) + o.y;
                }
                return float.NaN;
            }
            // drive-surface chunks per road id
            var chunks = new Dictionary<string, SortedDictionary<int, Mesh>>();
            foreach (Transform c in ds)
            {
                var mc = c.GetComponent<MeshCollider>();
                if (mc == null || mc.sharedMesh == null || !c.name.StartsWith("DS_")) continue;
                int us = c.name.LastIndexOf('_');
                string id = c.name.Substring(3, us - 3);
                if (!int.TryParse(c.name.Substring(us + 1), out int idx)) continue;
                if (!chunks.TryGetValue(id, out var d)) chunks[id] = d = new SortedDictionary<int, Mesh>();
                d[idx] = mc.sharedMesh;
            }
            // terrain raise targets (world height per heightmap texel, max): the embankment band is filled up to the collision
            // slope, so the visible ground matches the drivable one (master prompt §1.1: nothing raised terrain to the edge)
            var raise = new Dictionary<(Terrain, int, int), float>();
            void Raise(Vector3 a, Vector3 b)
            {
                float len = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
                for (float l = 0f; l <= len; l += 0.5f)
                {
                    var q = Vector3.Lerp(a, b, len > 0f ? l / len : 0f);
                    foreach (var tr in terrains)
                    {
                        var o = tr.GetPosition(); var sz = tr.terrainData.size; int res = tr.terrainData.heightmapResolution;
                        if (q.x < o.x || q.x > o.x + sz.x || q.z < o.z || q.z > o.z + sz.z) continue;
                        int ix = Mathf.RoundToInt((q.x - o.x) / sz.x * (res - 1)), iz = Mathf.RoundToInt((q.z - o.z) / sz.z * (res - 1));
                        var key = (tr, ix, iz); float y = q.y - 0.03f;   // 3 cm under the collider
                        if (!raise.TryGetValue(key, out var cur) || y > cur) raise[key] = y;
                        break;
                    }
                }
            }
            var keep = new HashSet<string>();
            var hits = new List<WorldData.RoadHit>();
            int built = 0, skippedStructure = 0, skippedOpen = 0, noTerrain = 0; long tris = 0;
            for (int r = 0; r < WorldData.Roads.Count; r++)
            {
                var road = WorldData.Roads[r];
                if (road.Data.cls == "RW" || !chunks.TryGetValue(road.Data.id, out var parts)) continue;
                bool urban = Urban.Contains(road.Data.cls);
                float sidewalk = road.Data.cls == "AR" ? 3f : 2.5f;
                int cols = Mathf.Max(2, Mathf.CeilToInt(road.Data.width / ColumnSpacing)) + 1;
                int perChunk = Mathf.RoundToInt(ChunkLength / Step);
                foreach (var kv in parts)
                {
                    var src = kv.Value.vertices;
                    int rows = src.Length / cols;
                    if (rows < 2) continue;
                    var bands = urban ? bandsUrban : bandsRural;
                    var bv = new List<Vector3>[bands.Length]; var bt = new List<int>[bands.Length];
                    for (int q = 0; q < bands.Length; q++) { bv[q] = new List<Vector3>(); bt[q] = new List<int>(); }
                    // per row and side: the profile points (lateral distance from the edge, rise); null when skipped
                    for (int side = 0; side < 2; side++)
                    {
                        Vector3[][] prof = new Vector3[rows][];
                        for (int k = 0; k < rows; k++)
                        {
                            var left = src[k * cols]; var rightE = src[k * cols + cols - 1];
                            var edge = side == 0 ? left : rightE;
                            var other = side == 0 ? rightE : left;
                            var outDir = edge - other; outDir.y = 0f;
                            if (outDir.sqrMagnitude < 1e-4f) continue;
                            outDir.Normalize();
                            float s = Mathf.Min((kv.Key * perChunk + k) * Step, road.Path.Length);
                            if (road.StructureAt(road.Path.SegmentAt(s)) != StructureKind.None) { skippedStructure++; continue; }
                            // junction mouth: another road's carriageway 1.2 m beyond the edge
                            var probe = edge + outDir * 1.2f;
                            WorldData.RoadsAt(probe, -0.5f, 3f, hits);
                            if (hits.Any(h => h.Road != r)) { skippedOpen++; continue; }
                            var pts = new List<Vector3> { edge };
                            Vector3 last;
                            if (urban)
                            {
                                pts.Add(edge + outDir * 0.30f + Vector3.up * 0.15f);
                                pts.Add(edge + outDir * (0.30f + sidewalk) + Vector3.up * (0.15f + sidewalk * 0.02f));
                            }
                            else pts.Add(edge + outDir * 0.8f + Vector3.up * -0.05f);
                            last = pts[pts.Count - 1];
                            float lastD = urban ? 0.30f + sidewalk : 0.8f;
                            // slope to the terrain: run = 2 x drop (or rise), clamped
                            // always one more point, so every row has the same count and rows stitch without gaps
                            float ty = TerrainY(last + outDir * 1f);
                            var meet = last + outDir * 0.5f;
                            if (float.IsNaN(ty)) noTerrain++;
                            else
                            {
                                float run = Mathf.Clamp(Mathf.Abs(ty - last.y) * 2f, 0.5f, urban ? 4f : 10f);   // about 1:2 (27°)
                                meet = last + outDir * run; float my = TerrainY(meet);
                                if (!float.IsNaN(my)) meet.y = my; else noTerrain++;
                                if (!float.IsNaN(my) && my < last.y - 0.1f) Raise(last, meet);   // fill: the ground falls away
                            }
                            pts.Add(meet);
                            prof[k] = pts.ToArray();
                        }
                        // stitch consecutive rows with the same point count
                        for (int k = 0; k + 1 < rows; k++)
                        {
                            var a = prof[k]; var b = prof[k + 1];
                            if (a == null || b == null || a.Length != b.Length) continue;
                            for (int q = 0; q + 1 < a.Length && q < bands.Length; q++)
                            {
                                var vv = bv[q]; var tt = bt[q];
                                int i0 = vv.Count;
                                vv.Add(a[q]); vv.Add(a[q + 1]); vv.Add(b[q]); vv.Add(b[q + 1]);
                                AddUp(vv, tt, i0, i0 + 2, i0 + 1);
                                AddUp(vv, tt, i0 + 1, i0 + 2, i0 + 3);
                            }
                            built++;
                        }
                    }
                    for (int q = 0; q < bands.Length; q++)
                    {
                        var vv = bv[q]; var tt = bt[q]; var surf = bands[q].Item1; string tag = bands[q].Item2;
                        if (tt.Count == 0) continue;
                        string name = $"SH_{road.Data.id}_{kv.Key:D3}_{tag}";
                        var mesh = SaveMesh(name, vv, tt);
                        keep.Add(name);
                        var go = new GameObject(name) { isStatic = true };
                        go.transform.SetParent(root, false);
                        var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mesh;
                        go.AddComponent<VehicleSurface>().Surface = surf;
                        tris += tt.Count / 3;
                    }
                }
            }
            foreach (var f in Directory.GetFiles(MeshDir, "*.asset"))
                if (!keep.Contains(Path.GetFileNameWithoutExtension(f))) AssetDatabase.DeleteAsset(f.Replace('\\', '/'));
            int raisedTexels = 0; float maxRaise = 0f;
            foreach (var grp in raise.GroupBy(kv => kv.Key.Item1))
            {
                var td = grp.Key.terrainData; var o = grp.Key.GetPosition(); int res = td.heightmapResolution;
                var h = td.GetHeights(0, 0, res, res);
                foreach (var kv in grp)
                {
                    int ix = kv.Key.Item2, iz = kv.Key.Item3;
                    float want = (kv.Value - o.y) / td.size.y;
                    if (want > h[iz, ix]) { maxRaise = Mathf.Max(maxRaise, (want - h[iz, ix]) * td.size.y); h[iz, ix] = want; raisedTexels++; }
                }
                td.SetHeights(0, 0, h);
                EditorUtility.SetDirty(td);
            }
            sb.AppendLine($"  terrain raised under the embankment bands: {raisedTexels:N0} heightmap texels on {raise.Select(k => k.Key.Item1).Distinct().Count()} terrains, max {maxRaise:F2} m (never lowered)");
            AssetDatabase.SaveAssets();
            sb.AppendLine($"  {keep.Count} shoulder chunks, {tris:N0} triangles, {built:N0} row segments; skipped rows: structure {skippedStructure:N0}, junction mouth {skippedOpen:N0}; no terrain beyond the edge {noTerrain:N0}");
            Directory.CreateDirectory("Logs/Shoulders");
            File.WriteAllText("Logs/Shoulders/shoulders.txt", sb.ToString());
            return sb.ToString();
        }

        static void AddUp(List<Vector3> v, List<int> t, int a, int b, int c)
        {
            if (Vector3.Cross(v[b] - v[a], v[c] - v[a]).y < 0f) { t.Add(a); t.Add(c); t.Add(b); }
            else { t.Add(a); t.Add(b); t.Add(c); }
        }

        static Mesh SaveMesh(string name, List<Vector3> v, List<int> t)
        {
            string path = $"{MeshDir}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh { name = name };
            mesh.Clear();
            mesh.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(v); mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            if (created) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
