using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.Core;
using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// World iteration 18 (CAM_08, CAM_12: the motorway, expressway and highways had no lighting; GTA-style motorways are
    /// lined with tall lamp masts that carry the night look). WorldBuildBatch step `hwlights`. Along every MW, EX and HW road
    /// (road network data): a 12 m galvanised mast every 45 m, staggered left/right, standing 1 m outside the carriageway at grade
    /// (base 3 m down) or on the parapet of a deck with a 2.4 m arm over the carriageway and a flat luminaire (Lamp_Head, which WorldEnvironment
    /// already lights at night). None in tunnels or within 25 m of a road end (junctions). Heights from the road profile.
    /// Merged per 250 m chunk under World/HighwayLights with an LODGroup; no colliders (on the barrier line, which the
    /// barrier itself guards). Idempotent. Report: Logs/C/highway_lights.txt.
    /// </summary>
    public static class HighwayLightsBuilder
    {
        const string MeshDir = "Assets/_Veyra/World/Generated/HighwayLights";
        const string MatDir = "Assets/_Veyra/World/Generated/Materials";
        static readonly HashSet<string> Classes = new HashSet<string> { "MW", "EX", "HW" };

        sealed class MB { public List<Vector3> V = new List<Vector3>(); public List<Vector2> UV = new List<Vector2>(); public List<int> T = new List<int>(); }

        public static string Run()
        {
            WorldData.EnsureLoaded();
            var world = EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == "World");
            if (world == null) return "FAIL: no World root\n";
            var old = world.transform.Find("HighwayLights");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("HighwayLights") { isStatic = true }.transform;
            root.SetParent(world.transform, false);
            Directory.CreateDirectory(MeshDir);
            var pole = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/Lamp_Pole.mat");
            var head = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/Lamp_Head.mat");
            if (pole == null || head == null) return "FAIL: Lamp_Pole / Lamp_Head materials missing\n";
            var poolMat = PoolMaterial();
            var chunks = new Dictionary<string, (MB pole, MB head, MB pool)>();
            (MB pole, MB head, MB pool) Get(Vector3 p)
            {
                var w = WorldCoordinates.FromUnity(p);
                string c = WorldCoordinates.ChunkOf(Mathf.Clamp(w.x, 0f, 7999f), Mathf.Clamp(w.y, 0f, 5999f));
                if (!chunks.TryGetValue(c, out var mb)) chunks[c] = mb = (new MB(), new MB(), new MB());
                return mb;
            }
            int masts = 0, roads = 0;
            for (int r = 0; r < WorldData.Roads.Count; r++)
            {
                var road = WorldData.Roads[r];
                if (!Classes.Contains(road.Data.cls)) continue;
                roads++;
                float len = road.Path.Length;
                int k = 0;
                for (float s = 25f; s < len - 25f; s += 45f, k++)
                {
                    var kind = road.StructureAt(road.Path.SegmentAt(s));
                    if (kind == StructureKind.Tunnel) continue;
                    bool deck = kind == StructureKind.Bridge || kind == StructureKind.Elevated;
                    var p = road.Path.PositionAt(s);
                    var t = road.Path.TangentAt(s); t.y = 0f; t.Normalize();
                    var right = new Vector3(t.z, 0f, -t.x);
                    float side = (k & 1) == 0 ? 1f : -1f;
                    // at grade 1 m outside the carriageway edge (base 3 m down, so it never floats over an embankment);
                    // on a deck on the parapet (the outer 0.4 m of the deck edge)
                    var b = p + right * side * (deck ? road.HalfWidth - 0.2f : road.HalfWidth + 1.0f);
                    var mb = Get(b);
                    var inward = -right * side;
                    // mast: tapered square section 0.26 -> 0.14 m, from 0.5 m below the road to 12 m
                    Tapered(mb.pole, b + Vector3.down * (deck ? 0.2f : 3f), b + Vector3.up * 12f, 0.26f, 0.14f, t);
                    // arm: rising 0.4 m over 2.4 m towards the carriageway
                    var a0 = b + Vector3.up * 11.6f; var a1 = a0 + inward * (deck ? 2.4f : 3.4f) + Vector3.up * 0.4f;
                    Beam(mb.pole, a0, a1, 0.09f);
                    // luminaire: flat box under the arm tip, facing down
                    var hc = a1 + inward * 0.2f - Vector3.up * 0.12f;
                    Box(mb.head, hc, inward, t, 0.85f, 0.14f, 0.38f);
                    // iteration 19: the light the luminaire throws on the carriageway (night-only additive pool, 16 x 11 m)
                    if (poolMat != null)
                    {
                        // inside the carriageway only: outer edge on the road edge (iteration 19: a pool overhanging the
                        // edge read as a lit board at the barrier, CAM_12); 0.12 m up clears the crown
                        float halfAcross = Mathf.Min(5f, road.HalfWidth * 0.6f);
                        var edge = p + right * side * road.HalfWidth;
                        var pc = new Vector3(edge.x, p.y + 0.12f, edge.z) + inward * (halfAcross + 0.2f);
                        var ac = inward * halfAcross; var al = t * 8f;
                        int vi = mb.pool.V.Count;
                        mb.pool.V.Add(pc - ac - al); mb.pool.V.Add(pc + ac - al); mb.pool.V.Add(pc + ac + al); mb.pool.V.Add(pc - ac + al);
                        mb.pool.UV.Add(new Vector2(0, 0)); mb.pool.UV.Add(new Vector2(1, 0)); mb.pool.UV.Add(new Vector2(1, 1)); mb.pool.UV.Add(new Vector2(0, 1));
                        if (Vector3.Cross(mb.pool.V[vi + 1] - mb.pool.V[vi], mb.pool.V[vi + 2] - mb.pool.V[vi]).y >= 0f) mb.pool.T.AddRange(new[] { vi, vi + 1, vi + 2, vi, vi + 2, vi + 3 });
                        else mb.pool.T.AddRange(new[] { vi, vi + 2, vi + 1, vi, vi + 3, vi + 2 });
                    }
                    masts++;
                }
            }
            var keep = new HashSet<string>();
            foreach (var kv in chunks)
            {
                var go = new GameObject(kv.Key) { isStatic = true };
                go.transform.SetParent(root, false);
                foreach (var (mb, mat, name) in new[] { (kv.Value.pole, pole, "Masts"), (kv.Value.head, head, "Luminaires"), (kv.Value.pool, poolMat, "LightPools") })
                {
                    if (mb.T.Count == 0 || mat == null) continue;
                    string mname = $"HL_{kv.Key}_{name}";
                    var mesh = Save(mname, mb);
                    keep.Add(mname);
                    var g = new GameObject(name) { isStatic = true };
                    g.transform.SetParent(go.transform, false);
                    g.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = g.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = mat == pole ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    var lg = g.AddComponent<LODGroup>();
                    lg.SetLODs(new[] { new LOD(mat == pole ? 0.006f : 0.002f, new Renderer[] { mr }) });
                    lg.RecalculateBounds();
                }
            }
            foreach (var f in Directory.GetFiles(MeshDir, "*.asset"))
                if (!keep.Contains(Path.GetFileNameWithoutExtension(f))) AssetDatabase.DeleteAsset(f.Replace('\\', '/'));
            AssetDatabase.SaveAssets();
            string rep = $"Highway lights: {masts} masts on {roads} MW/EX/HW roads in {chunks.Count} chunks\n";
            Directory.CreateDirectory("Logs/C");
            File.WriteAllText("Logs/C/highway_lights.txt", rep);
            return rep;
        }

        /// <summary>
        /// Iteration 19: warm road-light pool material (Veyra/World/GlowPool, radial soft blob), registered as a night-only
        /// emissive. Saved while unregistered (BUG-122: the save guard drops edits to registered emissives).
        /// </summary>
        static Material PoolMaterial()
        {
            const string texPath = "Assets/_Veyra/Art/Textures/Neon/T_Road_LightPool.png";
            if (!File.Exists(texPath))
            {
                const int S = 128;
                var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
                var px = new Color32[S * S];
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                        float d = Mathf.Sqrt(u * u + v * v);
                        float a = Mathf.Clamp01(1f - d); a = a * a * (3f - 2f * a); a *= a;
                        byte c = (byte)(a * 255f);
                        px[y * S + x] = new Color32(c, c, c, 255);
                    }
                tex.SetPixels32(px);
                File.WriteAllBytes(texPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(texPath);
                var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
                ti.wrapMode = TextureWrapMode.Clamp; ti.sRGBTexture = false;
                ti.SaveAndReimport();
            }
            var shader = Shader.Find("Veyra/World/GlowPool");   // Editor code
            if (shader == null) return null;
            string path = $"{MatDir}/M_Road_LightPool.mat";
            var env = Object.FindAnyObjectByType<WorldEnvironment>(FindObjectsInactive.Include);
            if (env != null) env.Emissives.RemoveAll(e => e.Material != null && e.Material.name == "M_Road_LightPool");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader) { name = "M_Road_LightPool" }; AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            var emis = new Color(1f, 0.82f, 0.58f) * 0.55f;
            m.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            m.SetColor("_EmissionColor", emis);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            if (env != null) { env.Emissives.Add(new WorldEnvironment.EmissiveEntry { Material = m, BaseEmission = emis, NightOnly = 1f }); EditorUtility.SetDirty(env); }
            return m;
        }

        static void Quad(MB m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
        {
            int i = m.V.Count;
            m.V.Add(a); m.V.Add(b); m.V.Add(c); m.V.Add(d);
            float w = (b - a).magnitude, h = (d - a).magnitude;
            m.UV.Add(new Vector2(0, 0)); m.UV.Add(new Vector2(w, 0)); m.UV.Add(new Vector2(w, h)); m.UV.Add(new Vector2(0, h));
            var n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, facing) >= 0f) m.T.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            else m.T.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }

        /// <summary>Square-section shaft from p0 (width w0) to p1 (width w1), sides aligned with fwd.</summary>
        static void Tapered(MB m, Vector3 p0, Vector3 p1, float w0, float w1, Vector3 fwd)
        {
            var ax = Vector3.Cross(Vector3.up, fwd).normalized; var az = fwd.normalized;
            Vector3 C(Vector3 c, float w, int x, int z) => c + ax * x * w * 0.5f + az * z * w * 0.5f;
            int[,] sides = { { -1, -1, 1, -1 }, { 1, -1, 1, 1 }, { 1, 1, -1, 1 }, { -1, 1, -1, -1 } };
            for (int s = 0; s < 4; s++)
            {
                var a = C(p0, w0, sides[s, 0], sides[s, 1]); var b = C(p0, w0, sides[s, 2], sides[s, 3]);
                var c = C(p1, w1, sides[s, 2], sides[s, 3]); var d = C(p1, w1, sides[s, 0], sides[s, 1]);
                var mid = (a + b) * 0.5f - p0; mid.y = 0f;
                Quad(m, a, b, c, d, mid);
            }
            Quad(m, C(p1, w1, -1, -1), C(p1, w1, 1, -1), C(p1, w1, 1, 1), C(p1, w1, -1, 1), Vector3.up);
        }

        static void Beam(MB m, Vector3 a, Vector3 b, float w)
        {
            var dir = (b - a).normalized;
            var side = Vector3.Cross(Vector3.up, dir).normalized * w * 0.5f;
            var up = Vector3.Cross(dir, side).normalized * w * 0.5f;
            Vector3[] r0 = { a - side - up, a + side - up, a + side + up, a - side + up };
            Vector3[] r1 = { b - side - up, b + side - up, b + side + up, b - side + up };
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                var mid = (r0[i] + r0[j]) * 0.5f - a;
                Quad(m, r0[i], r0[j], r1[j], r1[i], mid);
            }
        }

        static void Box(MB m, Vector3 c, Vector3 ax, Vector3 az, float sx, float sy, float sz)
        {
            ax = ax.normalized * sx * 0.5f; az = az.normalized * sz * 0.5f; var ay = Vector3.up * sy * 0.5f;
            Vector3 P(int x, int y, int z) => c + ax * x + ay * y + az * z;
            Quad(m, P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), -az);
            Quad(m, P(1, -1, 1), P(-1, -1, 1), P(-1, 1, 1), P(1, 1, 1), az);
            Quad(m, P(-1, -1, 1), P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), -ax);
            Quad(m, P(1, -1, -1), P(1, -1, 1), P(1, 1, 1), P(1, 1, -1), ax);
            Quad(m, P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), Vector3.up);
            Quad(m, P(-1, -1, 1), P(1, -1, 1), P(1, -1, -1), P(-1, -1, -1), Vector3.down);
        }

        static Mesh Save(string name, MB mb)
        {
            string path = $"{MeshDir}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh { name = name };
            mesh.Clear();
            mesh.indexFormat = mb.V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(mb.V); mesh.SetUVs(0, mb.UV); mesh.SetTriangles(mb.T, 0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            if (created) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
