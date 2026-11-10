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
    /// BUG-108: one drivable collision surface for the whole road network, so the car never meets the steps where blockout
    /// pieces, C2 ease ribbons, artefact patches and crossing roads overlap at different heights (Logs/BUG108/wall_probe.txt).
    /// <list type="number">
    /// <item>Every road except runways gets a collision-only ribbon (non-convex MeshCollider, VehicleSurface, no renderer) on its
    /// eased profile (<see cref="RoadProfileEaser"/>) + 3.2 cm, flat across the carriageway, in 100 m chunks under World/DriveSurface.</item>
    /// <item>Junctions: where a road's centre line enters the carriageway of a stronger road (class rank, then width) within
    /// 2.4 m of height (a real grade separation has 4.5 m or more), the weaker road takes the stronger road's surface there
    /// and ramps back to its own profile along the road (~2.5 % extra grade, 6–80 m), so crossing surfaces meet without a step.</item>
    /// <item>Road colliders (blockout CityMeshes, ease/patch/deck ribbons) lose the triangles that stand in a carriageway: any
    /// sampled point more than 4 cm above the drive surface (up to +2.5 m), or a steep face reaching within -1.2 / +2.5 m
    /// (kerbs, slabs, patches and crossing ribbons in the lane); surfaces under the drive surface stay; the median of divided
    /// roads (|offset| &lt; 1.5 m) keeps its barriers. The original mesh is kept on <see cref="DriveSurfaceSource"/>.</item>
    /// <item>Terrain inside any carriageway (+1 m) that rises above 0.3 m below the drive surface is lowered to that (lowest of
    /// all roads there); in tunnels only up to 6 m above the road, so the hill over the bore stays.</item>
    /// </list>
    /// Render meshes are untouched. Idempotent: originals are restored and everything is rebuilt; terrain only goes down to the
    /// same targets.
    /// </summary>
    public static class DriveSurface
    {
        const string RootName = "DriveSurface";
        public const string MeshDir = "Assets/_Veyra/World/Generated/DriveSurface";
        const string TerrainBackup = "Assets/_Veyra/World/Generated/TerrainBackup~";
        const float Step = 2f, Raise = 0.032f, MaxDy = 2.5f, ChunkLength = 100f, ColumnSpacing = 2.5f, MedianHalf = 1.5f;
        const float ClipBelow = 1.2f, ClipAbove = 2.5f, CarveDepth = 0.3f, TunnelEnvelope = 6f, BlendMax = 2.4f, Proud = 0.04f, SeamMargin = 10f, RawDy = 12f;

        static int[] order, position;
        static float[][] centre; // centre surface height per road per Step sample
        static List<int>[] winners;
        /// <summary>Per (weaker road, stronger road): where the weaker road's centre line lies inside the stronger carriageway
        /// at a similar height, the offset to the stronger surface there, and the nearest such sample before/after each sample.</summary>
        sealed class Junction { public bool[] Inside; public float[] Offset; public int[] Prev, Next; }
        static Dictionary<(int, int), Junction> junctions;

        [MenuItem("Veyra/Physics/Build Drive Surface (BUG-108)")]
        static void Menu()
        {
            if (!EnsureCityScene()) return;
            Debug.Log(Build());
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        /// <summary>Batch entry: <c>-executeMethod VeyraCity.EditorTools.DriveSurface.BatchBuild</c> (saves the scene).</summary>
        public static void BatchBuild()
        {
            int code = 0;
            try
            {
                EnsureCityScene();
                string log = Build();
                var scene = EditorSceneManager.GetActiveScene();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "BUG108", "drive_surface.txt")), log);
                Debug.Log(log);
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        /// <summary>Batch entry: C1 Validate Drivable and C2 Validate Road Profile on the scene with the drive surface
        /// (<c>-executeMethod VeyraCity.EditorTools.DriveSurface.BatchValidate</c>; Logs/BUG108/validators.txt).</summary>
        public static void BatchValidate()
        {
            int code = 0;
            try
            {
                EnsureCityScene();
                var sb = new StringBuilder();
                sb.AppendLine(DrivableValidator.Run(Path.Combine("Logs", "BUG108", "drivable")));
                sb.AppendLine(RoadProfileValidator.Run("after_drive_surface"));
                File.WriteAllText(Path.Combine("Logs", "BUG108", "validators.txt"), sb.ToString());
                Debug.Log(sb.ToString());
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        static bool EnsureCityScene()
        {
            const string path = "Assets/_Veyra/Scenes/VeyraCity.unity";
            if (EditorSceneManager.GetActiveScene().path == path) return true;
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            EditorSceneManager.OpenScene(path);
            return true;
        }

        public static string Build()
        {
            var log = new StringBuilder($"BUG-108 drive surface {DateTime.Now:yyyy-MM-dd HH:mm}\n");
            WorldData.EnsureLoaded();
            RoadProfileEaser.Reset();
            GameObject world = null;
            foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects()) if (go.name == "World") world = go;
            if (world == null) throw new InvalidOperationException("no World root in the open scene");

            // 0. restore clipped colliders, remove the previous drive surface
            int restored = 0;
            foreach (var src in world.GetComponentsInChildren<DriveSurfaceSource>(true))
            {
                var mc = src.GetComponent<MeshCollider>();
                if (mc != null && src.Original != null) { mc.sharedMesh = src.Original; mc.enabled = src.WasEnabled; restored++; }
                UnityEngine.Object.DestroyImmediate(src);
            }
            var old = world.transform.Find(RootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            Physics.SyncTransforms();
            Directory.CreateDirectory(MeshDir);

            var roads = WorldData.Roads;
            int n = roads.Count;
            order = Enumerable.Range(0, n).Where(r => roads[r].Data.cls != "RW")
                .OrderByDescending(r => WorldData.Rank(roads[r])).ThenByDescending(r => roads[r].Data.width).ThenBy(r => r).ToArray();
            position = Enumerable.Repeat(int.MaxValue, n).ToArray();
            for (int i = 0; i < order.Length; i++) position[order[i]] = i;
            centre = new float[n][];
            winners = new List<int>[n];

            // 1. junction partners: stronger roads whose carriageway this road's centre line enters at a similar height
            var hits = new List<WorldData.RoadHit>();
            int junctionPairs = 0;
            foreach (int r in order)
            {
                winners[r] = new List<int>();
                var path = roads[r].Path;
                for (float s = 0f; s <= path.Length; s += Step)
                {
                    var p = path.PositionAt(s);
                    float own = Own(r, s);
                    p.y = own - Raise;
                    WorldData.RoadsAt(p, 0f, RawDy, hits); // raw-profile filter; the eased heights decide below
                    foreach (var h in hits)
                        if (h.Road != r && position[h.Road] < position[r] && !winners[r].Contains(h.Road)
                            && Mathf.Abs(Own(h.Road, h.S) - own) < BlendMax) winners[r].Add(h.Road);
                }
                junctionPairs += winners[r].Count;
            }

            // 2. junction profiles and centre heights, strongest roads first (weaker roads blend towards them)
            float maxBlend = 0f;
            junctions = new Dictionary<(int, int), Junction>();
            foreach (int r in order)
            {
                var path = roads[r].Path;
                int count = Mathf.FloorToInt(path.Length / Step) + 2;
                centre[r] = new float[count];
                var hints = new Dictionary<int, int>();
                foreach (int q in winners[r])
                {
                    var j = new Junction { Inside = new bool[count], Offset = new float[count], Prev = new int[count], Next = new int[count] };
                    for (int k = 0; k < count; k++)
                    {
                        float s = Mathf.Min(k * Step, path.Length);
                        var c = path.PositionAt(s);
                        if (Nearest(q, c, hints, out float sq, out float dist) && dist <= WorldData.Roads[q].HalfWidth)
                        {
                            float off = Centre(q, sq) - Own(r, s);
                            if (Mathf.Abs(off) < BlendMax) { j.Inside[k] = true; j.Offset[k] = off; }
                        }
                    }
                    int last = -1;
                    for (int k = 0; k < count; k++) { if (j.Inside[k]) last = k; j.Prev[k] = last; }
                    last = -1;
                    for (int k = count - 1; k >= 0; k--) { if (j.Inside[k]) last = k; j.Next[k] = last; }
                    junctions[(r, q)] = j;
                }
                hints.Clear();
                for (int k = 0; k < count; k++)
                {
                    float s = Mathf.Min(k * Step, path.Length);
                    float own = Own(r, s);
                    float h = Height(r, s, path.PositionAt(s), hints);
                    centre[r][k] = h;
                    maxBlend = Mathf.Max(maxBlend, Mathf.Abs(h - own));
                }
            }

            // 3. collision ribbons
            var root = new GameObject(RootName).transform;
            root.SetParent(world.transform, false);
            root.gameObject.isStatic = true;
            var dry = AssetDatabase.LoadAssetAtPath<SurfaceDefinition>("Assets/_Veyra/Data/Surfaces/dry_asphalt.asset");
            var gravel = AssetDatabase.LoadAssetAtPath<SurfaceDefinition>("Assets/_Veyra/Data/Surfaces/gravel.asset");
            if (dry == null || gravel == null) throw new InvalidOperationException("Data/Surfaces dry_asphalt or gravel missing");
            var keepMeshes = new HashSet<string>();
            int chunks = 0; long tris = 0;
            foreach (int r in order)
            {
                var road = roads[r];
                var path = road.Path;
                int rows = Mathf.FloorToInt(path.Length / Step) + 1;
                int cols = Mathf.Max(2, Mathf.CeilToInt(road.Data.width / ColumnSpacing)) + 1;
                int perChunk = Mathf.RoundToInt(ChunkLength / Step);
                var hints = new Dictionary<int, int>();
                for (int c0 = 0, chunk = 0; c0 < rows - 1; c0 += perChunk, chunk++)
                {
                    int c1 = Mathf.Min(rows - 1, c0 + perChunk);
                    var v = new List<Vector3>((c1 - c0 + 1) * cols);
                    var t = new List<int>();
                    for (int k = c0; k <= c1; k++)
                    {
                        float s = Mathf.Min(k * Step, path.Length);
                        var c = path.PositionAt(s);
                        var tan = path.TangentAt(s); tan.y = 0f;
                        if (tan.sqrMagnitude < 1e-6f) tan = Vector3.forward;
                        tan.Normalize();
                        var right = new Vector3(tan.z, 0f, -tan.x);
                        for (int j = 0; j < cols; j++)
                        {
                            float lat = -road.HalfWidth + road.Data.width * j / (cols - 1);
                            var p = c + right * lat;
                            p.y = Height(r, s, p, hints);
                            v.Add(p);
                        }
                    }
                    for (int k = 0; k < c1 - c0; k++)
                        for (int j = 0; j < cols - 1; j++)
                        {
                            int a = k * cols + j, b = (k + 1) * cols + j, cc = a + 1, d = b + 1;
                            AddUp(v, t, a, b, cc);
                            AddUp(v, t, cc, b, d);
                        }
                    string name = $"DS_{road.Data.id}_{chunk:D3}";
                    var mesh = SaveMesh(name, v, t);
                    keepMeshes.Add(name);
                    var go = new GameObject(name);
                    go.transform.SetParent(root, false);
                    go.isStatic = true;
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = mesh;
                    go.AddComponent<VehicleSurface>().Surface = road.Data.cls == "GR" ? gravel : dry;
                    chunks++; tris += t.Count / 3;
                }
            }
            log.AppendLine($"roads {order.Length} (runways skipped), junction partners {junctionPairs}, drive-surface chunks {chunks}, triangles {tris:N0}; largest junction blend {maxBlend:F2} m; colliders restored first {restored}");

            // 4. clip road colliders inside carriageways
            log.Append(ClipColliders(world.transform, root, keepMeshes));

            // 5. terrain inside carriageways
            log.Append(CarveTerrain(world.transform));

            foreach (var f in Directory.GetFiles(MeshDir, "*.asset"))
                if (!keepMeshes.Contains(Path.GetFileNameWithoutExtension(f))) AssetDatabase.DeleteAsset(f.Replace('\\', '/'));
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        /// <summary>Adds the triangle facing up (wheel rays do not hit back faces), whatever a kink in the centre line did to the rows.</summary>
        static void AddUp(List<Vector3> v, List<int> t, int a, int b, int c)
        {
            if (Vector3.Cross(v[b] - v[a], v[c] - v[a]).y < 0f) { t.Add(a); t.Add(c); t.Add(b); }
            else { t.Add(a); t.Add(b); t.Add(c); }
        }

        static float Own(int r, float s) => WorldData.Roads[r].Path.PositionAt(s).y + RoadProfileEaser.Delta(r, s) + Raise;

        static float Centre(int q, float s)
        {
            var c = centre[q];
            float f = Mathf.Clamp(s / Step, 0f, c.Length - 1);
            int i = Mathf.Min(c.Length - 2, Mathf.FloorToInt(f));
            return Mathf.Lerp(c[i], c[i + 1], f - i);
        }

        /// <summary>
        /// Drive-surface height of road r at station s, world point p, continuous everywhere: (1) where r's centre line lies
        /// inside a stronger road's carriageway (a junction) r takes the offset to that surface, ramped out along r over
        /// L = |offset| / 2.5 % (6–80 m, smoothstep), so a road that leaves another slowly (H2's start) is not pinned to it;
        /// (2) points at or beside the stronger carriageway then blend to its surface at that very point by their own distance
        /// from it (full inside, none 10 m out; faded where the two differ by 1–2 m), so the two surfaces meet without a seam.
        /// </summary>
        static float Height(int r, float s, Vector3 p, Dictionary<int, int> hints)
        {
            float own = Own(r, s);
            // 1. along-road profile: junction offsets on r's centre line, ramped out along r
            float rampW = 0f, ramp = own;
            int k = Mathf.Clamp(Mathf.RoundToInt(s / Step), 0, centre[r] != null ? centre[r].Length - 1 : int.MaxValue);
            foreach (int q in winners[r])
            {
                if (!junctions.TryGetValue((r, q), out var j)) continue;
                int kk = Mathf.Min(k, j.Inside.Length - 1);
                if (j.Inside[kk]) { rampW = 1f; ramp = own + j.Offset[kk]; break; }
                int kb = -1;
                if (j.Prev[kk] >= 0) kb = j.Prev[kk];
                if (j.Next[kk] >= 0 && (kb < 0 || j.Next[kk] - kk < kk - kb)) kb = j.Next[kk];
                if (kb < 0) continue;
                float O = j.Offset[kb];
                float L = Mathf.Clamp(Mathf.Abs(O) / 0.025f, 6f, 80f);
                float d = Mathf.Abs(kk - kb) * Step;
                if (d >= L) continue;
                float u = 1f - d / L;
                float w = u * u * (3f - 2f * u);
                if (w > rampW) { rampW = w; ramp = own + O * w; }
            }
            // 2. at and beside a stronger carriageway, its surface at this very point (not at r's centre), blended by this
            // point's distance from that carriageway (full inside, none 10 m out) and faded where the two differ by 1-2 m, so
            // the surfaces meet without a seam and nothing switches abruptly
            float bestW = 0f, exact = ramp;
            foreach (int q in winners[r])
            {
                if (!Nearest(q, p, hints, out float sq, out float dist)) continue;
                float outside = Mathf.Max(0f, dist - WorldData.Roads[q].HalfWidth);
                if (outside >= SeamMargin) continue;
                float tq = Centre(q, sq);
                float wd = 1f - Mathf.Clamp01(Mathf.Abs(tq - ramp) - 1f);
                if (wd <= 0f) continue;
                float u = 1f - outside / SeamMargin;
                float w = u * u * (3f - 2f * u) * wd;
                if (w > bestW) { bestW = w; exact = tq; }
            }
            return Mathf.Lerp(ramp, exact, bestW);
        }

        /// <summary>Nearest point of road q's centre line to p (horizontal), tracked with a per-road segment hint.</summary>
        static bool Nearest(int q, Vector3 p, Dictionary<int, int> hints, out float sq, out float dist)
        {
            var qp = WorldData.Roads[q].Path;
            float qHalf = WorldData.Roads[q].HalfWidth;
            bool known = hints.TryGetValue(q, out int hint);
            sq = 0f; dist = float.MaxValue;
            if (known)
            {
                sq = qp.Project(p, ref hint, 40);
                var cw = qp.PositionAt(sq);
                dist = new Vector2(p.x - cw.x, p.z - cw.z).magnitude;
            }
            if (!known || dist > qHalf + 60f)
            {
                // first contact, or the windowed projection lost track (the road meets q again elsewhere): global search
                hint = 0; float bd = float.MaxValue;
                for (int i = 0; i < qp.Count; i++)
                {
                    float d2 = (new Vector2(qp.Points[i].x - p.x, qp.Points[i].z - p.z)).sqrMagnitude;
                    if (d2 < bd) { bd = d2; hint = i; }
                }
                sq = qp.Project(p, ref hint, 40);
                var cg = qp.PositionAt(sq);
                dist = new Vector2(p.x - cg.x, p.z - cg.z).magnitude;
            }
            hints[q] = hint;
            return true;
        }

        static Mesh SaveMesh(string name, List<Vector3> v, List<int> t)
        {
            string path = $"{MeshDir}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh { name = name };
            mesh.Clear();
            mesh.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (created) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>Removes collider triangles that stand inside a carriageway near its drive surface.</summary>
        static string ClipColliders(Transform world, Transform dsRoot, HashSet<string> keepMeshes)
        {
            var hits = new List<WorldData.RoadHit>();
            int touched = 0, disabled = 0; long removed = 0, kept = 0;
            foreach (var mc in world.GetComponentsInChildren<MeshCollider>(true))
            {
                if (mc.transform.IsChildOf(dsRoot) || mc.convex || mc.sharedMesh == null) continue;
                bool road = mc.name.StartsWith("Collider_") || mc.GetComponentInParent<VehicleSurface>() != null;
                if (!road) continue;
                var src = mc.sharedMesh;
                var lv = src.vertices;
                var wv = new Vector3[lv.Length];
                var m = mc.transform.localToWorldMatrix;
                for (int i = 0; i < lv.Length; i++) wv[i] = m.MultiplyPoint3x4(lv[i]);
                var outTris = new List<int>();
                int dropped = 0;
                for (int sub = 0; sub < src.subMeshCount; sub++)
                {
                    var tri = src.GetTriangles(sub);
                    for (int i = 0; i < tri.Length; i += 3)
                    {
                        if (Intrudes(wv[tri[i]], wv[tri[i + 1]], wv[tri[i + 2]], hits)) { dropped++; continue; }
                        outTris.Add(tri[i]); outTris.Add(tri[i + 1]); outTris.Add(tri[i + 2]);
                    }
                }
                kept += outTris.Count / 3;
                if (dropped == 0) continue;
                removed += dropped;
                touched++;
                var tag = mc.gameObject.AddComponent<DriveSurfaceSource>();
                tag.Original = src;
                tag.WasEnabled = mc.enabled;
                if (outTris.Count == 0) { mc.enabled = false; disabled++; continue; }
                string name = $"Clip_{src.name}_{Mathf.Abs(GlobalObjectId.GetGlobalObjectIdSlow(mc).targetObjectId.GetHashCode()):X8}";
                var mesh = SaveMesh(name, lv.ToList(), outTris);
                keepMeshes.Add(name);
                mc.sharedMesh = mesh;
            }
            return $"road colliders clipped: {touched} (disabled when empty: {disabled}); triangles removed {removed:N0}, kept {kept:N0}\n";
        }

        /// <summary>
        /// True when the triangle stands in a carriageway: some sample point of it (vertices, edge midpoints, centroid, and a
        /// ~2 m barycentric grid on big triangles) lies inside a carriageway (0.3 m in from the edge, not on a divided road's
        /// median) and above the drive surface by more than 4 cm (up to +2.5 m), or the triangle is a steep face with a point
        /// between -1.2 and +2.5 m of the surface. Surfaces lying under the drive surface are kept (the wheels never reach them).
        /// </summary>
        static bool Intrudes(Vector3 a, Vector3 b, Vector3 c, List<WorldData.RoadHit> hits)
        {
            var nrm = Vector3.Cross(b - a, c - a);
            bool steep = nrm.sqrMagnitude > 1e-8f && Mathf.Abs(nrm.normalized.y) < 0.7f;
            float edge = Mathf.Max((b - a).magnitude, Mathf.Max((c - b).magnitude, (a - c).magnitude));
            int n = Mathf.Clamp(Mathf.CeilToInt(edge / 2f), 2, 80);
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= n - i; j++)
                {
                    float u = (float)i / n, v = (float)j / n;
                    var p = a + (b - a) * u + (c - a) * v;
                    if (PointIntrudes(p, steep, hits)) return true;
                }
            return PointIntrudes((a + b + c) / 3f, steep, hits);
        }

        static bool PointIntrudes(Vector3 p, bool steep, List<WorldData.RoadHit> hits)
        {
            // WorldData compares with the raw data profile; eased approaches sit up to 8.5 m above it, so the vertical test
            // below uses the drive surface and this filter only skips far-off levels
            WorldData.RoadsAt(p, 0.3f, RawDy, hits);
            foreach (var h in hits)
            {
                if (WorldData.Roads[h.Road].Data.cls == "RW" || centre[h.Road] == null) continue;
                if (WorldData.Roads[h.Road].Divided && h.Distance < MedianHalf) continue;
                float surface = Centre(h.Road, h.S);
                float lo = steep ? surface - ClipBelow : surface + Proud;
                if (p.y > lo && p.y < surface + ClipAbove) return true;
            }
            return false;
        }

        /// <summary>Lowers terrain that rises into any carriageway (+2.5 m: one terrain sample past the edge) to 0.3 m below the drive surface (in tunnels only up to 6 m above it).</summary>
        static string CarveTerrain(Transform world)
        {
            var terrains = world.GetComponentsInChildren<Terrain>(false);
            var heights = new Dictionary<TerrainData, float[,]>();
            var target = new Dictionary<(TerrainData, int, int), float>();
            foreach (int r in order)
            {
                var road = WorldData.Roads[r];
                float reach = road.HalfWidth + 2.5f;
                var path = road.Path;
                for (float s = 0f; s <= path.Length; s += Step)
                {
                    var c = path.PositionAt(s);
                    float surf = Centre(r, s);
                    int si = path.SegmentAt(s);
                    bool tunnel = road.StructureAt(si) == StructureKind.Tunnel;
                    float envelope = tunnel ? TunnelEnvelope : float.MaxValue; // above a tunnel the hill stays
                    foreach (var t in terrains)
                    {
                        var td = t.terrainData; var tp = t.transform.position; var size = td.size;
                        if (c.x < tp.x - reach || c.x > tp.x + size.x + reach || c.z < tp.z - reach || c.z > tp.z + size.z + reach) continue;
                        int res = td.heightmapResolution;
                        if (!heights.TryGetValue(td, out var h)) { Backup(td); h = td.GetHeights(0, 0, res, res); heights[td] = h; }
                        float cell = size.x / (res - 1);
                        int x0 = Mathf.Max(0, Mathf.FloorToInt((c.x - reach - tp.x) / cell)), x1 = Mathf.Min(res - 1, Mathf.CeilToInt((c.x + reach - tp.x) / cell));
                        int z0 = Mathf.Max(0, Mathf.FloorToInt((c.z - reach - tp.z) / cell)), z1 = Mathf.Min(res - 1, Mathf.CeilToInt((c.z + reach - tp.z) / cell));
                        for (int z = z0; z <= z1; z++)
                            for (int x = x0; x <= x1; x++)
                            {
                                float wx = tp.x + x * cell, wz = tp.z + z * cell;
                                if ((wx - c.x) * (wx - c.x) + (wz - c.z) * (wz - c.z) > reach * reach) continue;
                                float cur = tp.y + h[z, x] * size.y;
                                if (cur <= surf - CarveDepth + 0.02f || cur > surf + envelope) continue;
                                var key = (td, z, x);
                                float want = surf - CarveDepth;
                                if (!target.TryGetValue(key, out float prev) || want < prev) target[key] = want;
                            }
                    }
                }
            }
            var changed = new HashSet<TerrainData>();
            foreach (var kv in target)
            {
                var (td, z, x) = kv.Key;
                float ty = 0f;
                foreach (var t in terrains) if (t.terrainData == td) { ty = t.transform.position.y; break; }
                heights[td][z, x] = Mathf.Clamp01((kv.Value - ty) / td.size.y);
                changed.Add(td);
            }
            foreach (var td in changed) { td.SetHeights(0, 0, heights[td]); EditorUtility.SetDirty(td); }
            return $"terrain lowered in carriageways: {target.Count:N0} samples on {changed.Count} tile(s) (backups in {TerrainBackup})\n";
        }

        static void Backup(TerrainData td)
        {
            string src = AssetDatabase.GetAssetPath(td);
            if (string.IsNullOrEmpty(src)) return;
            Directory.CreateDirectory(TerrainBackup);
            string dst = Path.Combine(TerrainBackup, Path.GetFileName(src));
            if (!File.Exists(dst)) File.Copy(src, dst);
        }
    }
}
