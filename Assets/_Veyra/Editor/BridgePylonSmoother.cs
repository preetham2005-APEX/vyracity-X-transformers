using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// World iteration 11 (bridges): the blockout steel of the cable-stayed bridges (World/CityMeshes/*/steel) draws each
    /// inclined pylon leg as a stack of axis-aligned boxes, offset a little per storey, which reads as a staircase
    /// (CAM_14). This step finds those stacks (position-welded connected components that are box-like, taller than
    /// 1.5 m, and sit on top of each other) and replaces every stack of 4 or more with one straight tapered prism fitted
    /// through the boxes' own centres and sizes (least squares over height), so the leg keeps its data position, lean,
    /// taper, base and top. Every other triangle of the mesh is kept as it is.
    /// The smoothed mesh is saved under World/Generated/Meshes/Bridges and assigned to the MeshFilter (render only;
    /// colliders are left unchanged). Idempotent: a smoothed mesh has no stacks left; a rebuilt blockout is smoothed
    /// again. Report: Logs/B1/pylons.txt.
    /// </summary>
    public static class BridgePylonSmoother
    {
        const string MeshDir = "Assets/_Veyra/World/Generated/Meshes/Bridges";

        struct Part { public List<int> Tris; public Bounds B; }

        public static string Run()
        {
            var sb = new StringBuilder("Bridge pylons (iteration 11)\n");
            var world = EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == "World");
            var cm = world != null ? world.transform.Find("CityMeshes") : null;
            if (cm == null) return "FAIL: no World/CityMeshes\n";
            Directory.CreateDirectory(MeshDir);
            int meshes = 0, legs = 0;
            foreach (var mf in cm.GetComponentsInChildren<MeshFilter>(true))
            {
                if ((mf.name != "steel" && mf.name != "concrete") || mf.sharedMesh == null) continue;
                var src = mf.sharedMesh;
                if (src.bounds.size.y < 40f) continue;
                var res = Smooth(src, mf.transform, out int n, sb);
                if (n == 0) continue;
                string path = $"{MeshDir}/{src.name.Replace("_smoothed", "")}_smoothed.asset";
                var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (old != null && old != src) AssetDatabase.DeleteAsset(path);
                if (old == src)
                {
                    // the filter already holds this asset: write in place
                    src.Clear(); src.indexFormat = res.indexFormat;
                    src.SetVertices(res.vertices); src.SetNormals(res.normals); src.SetUVs(0, res.uv); src.SetTriangles(res.triangles, 0);
                    src.RecalculateBounds(); src.RecalculateTangents();
                    EditorUtility.SetDirty(src);
                }
                else
                {
                    res.name = Path.GetFileNameWithoutExtension(path);
                    AssetDatabase.CreateAsset(res, path);
                    mf.sharedMesh = res;
                }
                meshes++; legs += n;
                sb.AppendLine($"  {GetPath(mf.transform)}: {n} legs smoothed");
            }
            AssetDatabase.SaveAssets();
            sb.AppendLine($"{legs} pylon legs rebuilt as straight tapered prisms in {meshes} meshes");
            Directory.CreateDirectory("Logs/B1");
            File.WriteAllText("Logs/B1/pylons.txt", sb.ToString());
            return sb.ToString();
        }

        static string GetPath(Transform t) { string p = t.name; for (var q = t.parent; q != null; q = q.parent) p = q.name + "/" + p; return p; }

        static Mesh Smooth(Mesh src, Transform tr, out int legs, StringBuilder log)
        {
            legs = 0;
            var v = src.vertices; var t = src.triangles;
            var nrm = src.normals; var uv = src.uv;
            // weld positions (1 cm) and union triangles that share a welded vertex
            var key = new Dictionary<Vector3Int, int>();
            var weld = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var k = Vector3Int.RoundToInt(v[i] * 100f);
                if (!key.TryGetValue(k, out int w)) { w = key.Count; key[k] = w; }
                weld[i] = w;
            }
            var parent = Enumerable.Range(0, key.Count).ToArray();
            int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
            for (int i = 0; i < t.Length; i += 3)
            {
                int a = Find(weld[t[i]]), b = Find(weld[t[i + 1]]), c = Find(weld[t[i + 2]]);
                parent[b] = a; parent[Find(c)] = Find(a);
            }
            var parts = new Dictionary<int, Part>();
            for (int i = 0; i < t.Length; i += 3)
            {
                int r = Find(weld[t[i]]);
                if (!parts.TryGetValue(r, out var p)) p = new Part { Tris = new List<int>(), B = new Bounds(v[t[i]], Vector3.zero) };
                p.Tris.Add(i);
                for (int k = 0; k < 3; k++) p.B.Encapsulate(v[t[i + k]]);
                parts[r] = p;
            }
            {
                if (src.name.Contains("_-11")) {
                var hist = parts.Values.GroupBy(p => p.Tris.Count).OrderByDescending(g => g.Count()).Take(8).Select(g => $"{g.Key} tris x{g.Count()}");
                log.AppendLine($"  mesh {src.name}: {v.Length} verts, {t.Length / 3} tris, {parts.Count} parts; " + string.Join(", ", hist));
                foreach (var p in parts.Values.OrderByDescending(p => p.B.size.y).Take(40)) log.AppendLine($"    part {p.Tris.Count} tris, bounds min {p.B.min} size {p.B.size}");
                for (int i = 0; i < Mathf.Min(t.Length, 0); i += 3) log.AppendLine($"    tri {i / 3}: {v[t[i]]} {v[t[i + 1]]} {v[t[i + 2]]}");
            }}
            // prism segments (box or hexagonal, 8-48 triangles), taller than 1.5 m, narrower than 12 m
            var segs = parts.Values.Where(p => p.Tris.Count >= 8 && p.Tris.Count <= 48 && p.B.size.y > 1.5f && p.B.size.x < 12f && p.B.size.z < 12f)
                                   .OrderBy(p => p.B.min.y).ToList();
            var used = new HashSet<int>();
            var removeTris = new HashSet<int>();
            var addV = new List<Vector3>(); var addN = new List<Vector3>(); var addUV = new List<Vector2>(); var addT = new List<int>();
            for (int s = 0; s < segs.Count; s++)
            {
                if (used.Contains(s)) continue;
                var chain = new List<int> { s };
                int cur = s;
                while (true)
                {
                    var cb = segs[cur].B;
                    int next = -1; float best = float.MaxValue;
                    for (int q = cur + 1; q < segs.Count; q++)
                    {
                        if (used.Contains(q) || chain.Contains(q)) continue;
                        var qb = segs[q].B;
                        if (qb.min.y > cb.max.y + 0.3f) break;
                        // the next storey starts at the top of this one (blockout segments overlap by up to 1 m)
                        if (qb.min.y < cb.max.y - 1.2f || qb.min.y < cb.min.y + 1f) continue;
                        if (Mathf.Abs(qb.size.x - cb.size.x) > 0.6f) continue;
                        float d = new Vector2(qb.center.x - cb.center.x, qb.center.z - cb.center.z).magnitude;
                        float lim = Mathf.Max(cb.size.x, cb.size.z) * 0.9f;
                        if (d < lim && d < best) { best = d; next = q; }
                    }
                    if (next < 0) break;
                    chain.Add(next); cur = next;
                }
                if (chain.Count < 4) continue;
                foreach (int q in chain) used.Add(q);
                // least-squares fit of centre x, z and size x, z against height
                var ys = chain.Select(q => segs[q].B.center.y).ToArray();
                float Fit(System.Func<Bounds, float> f, float y)
                {
                    float my = ys.Average(), mv = chain.Average(q => f(segs[q].B));
                    float num = 0f, den = 0f;
                    for (int k = 0; k < chain.Count; k++) { float dy = ys[k] - my; num += dy * (f(segs[chain[k]].B) - mv); den += dy * dy; }
                    return mv + (den > 1e-4f ? num / den : 0f) * (y - my);
                }
                float y0 = segs[chain[0]].B.min.y, y1 = segs[chain[chain.Count - 1]].B.max.y;
                Vector3 C(float y) => new Vector3(Fit(b => b.center.x, y), y, Fit(b => b.center.z, y));
                Vector2 S(float y) => new Vector2(Mathf.Max(0.3f, Fit(b => b.size.x, y)), Mathf.Max(0.3f, Fit(b => b.size.z, y)));
                Tube(addV, addN, addUV, addT, C(y0), S(y0), C(y1), S(y1), 12);
                foreach (int q in chain) foreach (int ti in segs[q].Tris) removeTris.Add(ti);
                legs++;
                log.AppendLine($"    leg {legs}: {chain.Count} boxes, y {y0:0.0}-{y1:0.0}, base {S(y0).x:0.0}x{S(y0).y:0.0} m, top {S(y1).x:0.0}x{S(y1).y:0.0} m");
            }
            if (legs == 0) return null;
            // keep every other triangle with its own vertices
            var outV = new List<Vector3>(); var outN = new List<Vector3>(); var outUV = new List<Vector2>(); var outT = new List<int>();
            var map = new Dictionary<int, int>();
            for (int i = 0; i < t.Length; i += 3)
            {
                if (removeTris.Contains(i)) continue;
                for (int k = 0; k < 3; k++)
                {
                    int o = t[i + k];
                    if (!map.TryGetValue(o, out int n))
                    {
                        n = outV.Count; map[o] = n;
                        outV.Add(v[o]); outN.Add(nrm.Length == v.Length ? nrm[o] : Vector3.up); outUV.Add(uv.Length == v.Length ? uv[o] : Vector2.zero);
                    }
                    outT.Add(n);
                }
            }
            int baseIdx = outV.Count;
            outV.AddRange(addV); outN.AddRange(addN); outUV.AddRange(addUV);
            outT.AddRange(addT.Select(i => i + baseIdx));
            var m = new Mesh { indexFormat = outV.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(outV); m.SetNormals(outN); m.SetUVs(0, outUV); m.SetTriangles(outT, 0);
            m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        /// <summary>Round tapered leg (elliptical section from the fitted sizes), smooth normals, capped; UVs in metres.</summary>
        static void Tube(List<Vector3> V, List<Vector3> N, List<Vector2> UV, List<int> T, Vector3 c0, Vector2 s0, Vector3 c1, Vector2 s1, int sides)
        {
            float h = (c1 - c0).magnitude;
            int k0 = V.Count;
            for (int i = 0; i <= sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var p0 = c0 + new Vector3(d.x * s0.x, 0f, d.z * s0.y) * 0.5f;
                var p1 = c1 + new Vector3(d.x * s1.x, 0f, d.z * s1.y) * 0.5f;
                var n = new Vector3(d.x / Mathf.Max(0.1f, s0.x), 0f, d.z / Mathf.Max(0.1f, s0.y)).normalized;
                V.Add(p0); V.Add(p1); N.Add(n); N.Add(n);
                float u = i / (float)sides * Mathf.PI * (s0.x + s0.y) * 0.5f;
                UV.Add(new Vector2(u, 0f)); UV.Add(new Vector2(u, h));
            }
            for (int i = 0; i < sides; i++)
            {
                int a = k0 + i * 2, b = a + 2;
                // seen from outside, (a, a+1, b+1) runs clockwise for the angle increasing counter-clockwise from above
                T.AddRange(new[] { a, a + 1, b + 1, a, b + 1, b });
            }
            foreach (var (c, sz, up) in new[] { (c1, s1, 1f), (c0, s0, -1f) })
            {
                int kc = V.Count;
                V.Add(c); N.Add(Vector3.up * up); UV.Add(Vector2.zero);
                for (int i = 0; i <= sides; i++)
                {
                    float a = i / (float)sides * Mathf.PI * 2f;
                    var p = c + new Vector3(Mathf.Cos(a) * sz.x, 0f, Mathf.Sin(a) * sz.y) * 0.5f;
                    V.Add(p); N.Add(Vector3.up * up); UV.Add(new Vector2(p.x, p.z));
                }
                for (int i = 0; i < sides; i++)
                {
                    if (up > 0f) T.AddRange(new[] { kc, kc + 2 + i, kc + 1 + i });
                    else T.AddRange(new[] { kc, kc + 1 + i, kc + 2 + i });
                }
            }
        }

        /// <summary>Four-sided tapered prism from a bottom rectangle to a top rectangle (flat-shaded, capped), UVs in metres.</summary>
        static void Prism(List<Vector3> V, List<Vector3> N, List<Vector2> UV, List<int> T, Vector3 c0, Vector2 s0, Vector3 c1, Vector2 s1)
        {
            Vector3[] Ring(Vector3 c, Vector2 s) => new[]
            {
                c + new Vector3(-s.x, 0, -s.y) * 0.5f, c + new Vector3(s.x, 0, -s.y) * 0.5f,
                c + new Vector3(s.x, 0, s.y) * 0.5f, c + new Vector3(-s.x, 0, s.y) * 0.5f,
            };
            var a = Ring(c0, s0); var b = Ring(c1, s1);
            float h = (c1 - c0).magnitude;
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                var p0 = a[i]; var p1 = a[j]; var p2 = b[j]; var p3 = b[i];
                var n = Vector3.Cross(p3 - p0, p1 - p0).normalized;
                if (Vector3.Dot(n, (p0 + p1) * 0.5f - c0) < 0f) n = -n;
                int k = V.Count;
                V.Add(p0); V.Add(p1); V.Add(p2); V.Add(p3);
                for (int q = 0; q < 4; q++) N.Add(n);
                float w = (p1 - p0).magnitude;
                UV.Add(new Vector2(0, 0)); UV.Add(new Vector2(w, 0)); UV.Add(new Vector2(w, h)); UV.Add(new Vector2(0, h));
                // outward winding (Unity: clockwise seen from outside)
                if (Vector3.Dot(Vector3.Cross(p3 - p0, p2 - p0), n) > 0f) T.AddRange(new[] { k, k + 3, k + 2, k, k + 2, k + 1 });
                else T.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 });
            }
            foreach (var (r, up) in new[] { (b, 1f), (a, -1f) })
            {
                int k = V.Count;
                V.AddRange(r);
                for (int q = 0; q < 4; q++) { N.Add(Vector3.up * up); UV.Add(new Vector2(r[q].x, r[q].z)); }
                if (up > 0f) T.AddRange(new[] { k, k + 3, k + 2, k, k + 2, k + 1 });
                else T.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 });
            }
        }
    }
}
