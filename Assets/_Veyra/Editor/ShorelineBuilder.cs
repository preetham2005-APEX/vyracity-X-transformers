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
    /// World iteration 6 (Phase B water shoreline): "Veyra/World/Build Shoreline". Where water meets natural ground:
    /// <list type="bullet">
    /// <item>Boulders along the sea coastline wherever no seawall stands (the land behind is under 2.5 m or outside D1/D3/D6,
    /// the same rule as <see cref="SeawallBuilder"/>), clustered 0.5–2 m below to 0.6 m above the water line.</item>
    /// <item>Mirror Lake (data lake polygon, outflow level): boulders on its rim and reed clumps (crossed cards, 1.1–1.9 m,
    /// the grass blade texture) in the shallows.</item>
    /// </list>
    /// Boulders: 4 noise-displaced icospheres (procedural, original), the 1024 px rock albedo + normal map
    /// (M_Shore_Rock). Positions from the data polylines and the terrain heights only; seeded. Merged per 250 m chunk under
    /// World/Shoreline (streamed). No collision on reeds; boulders get none either (they stand off the drivable network).
    /// Idempotent. Report: Logs/B1/shoreline.txt.
    /// </summary>
    public static class ShorelineBuilder
    {
        const string MeshDir = "Assets/_Veyra/World/Generated/Shoreline";
        const string MatDir = "Assets/_Veyra/World/Generated/Materials";
        const float SeaY = -0.15f;

        sealed class MB { public List<Vector3> V = new List<Vector3>(); public List<Vector3> N = new List<Vector3>(); public List<Vector2> UV = new List<Vector2>(); public List<int> T = new List<int>(); }

        [MenuItem("Veyra/World/Build Shoreline")]
        public static void Menu() => Debug.Log(Build());

        public static string Build()
        {
            WorldData.EnsureLoaded();
            var world = EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == "World");
            if (world == null) return "FAIL: no World root";
            var old = world.transform.Find("Shoreline");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("Shoreline") { isStatic = true }.transform;
            root.SetParent(world.transform, false);
            Directory.CreateDirectory(MeshDir);
            var rockMat = RockMaterial();
            var reedMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Veyra/World/Generated/Grass/M_Grass.mat");
            var terrains = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include);
            float Ground(Vector3 p)
            {
                foreach (var t in terrains)
                {
                    var o = t.GetPosition(); var s = t.terrainData.size;
                    if (p.x >= o.x && p.x <= o.x + s.x && p.z >= o.z && p.z <= o.z + s.z) return t.SampleHeight(p) + o.y;
                }
                return float.NaN;
            }
            var rocks = Enumerable.Range(0, 4).Select(i => Boulder(i)).ToArray();
            var chunks = new Dictionary<string, (MB rock, MB reed, MB foam)>();
            var foamMat = FoamMaterial();
            (MB rock, MB reed, MB foam) Get(Vector3 p)
            {
                var w = WorldCoordinates.FromUnity(p);
                string c = WorldCoordinates.ChunkOf(Mathf.Clamp(w.x, 0f, 7999f), Mathf.Clamp(w.y, 0f, 5999f));
                if (!chunks.TryGetValue(c, out var mb)) chunks[c] = mb = (new MB(), new MB(), new MB());
                return mb;
            }
            var rng = new System.Random(4242);
            int seaRocks = 0, lakeRocks = 0, reeds = 0; float foamM = 0f;

            // sea coast without a seawall
            var c = WorldData.Districts.coastline;
            for (int i = 0; i + 3 < c.Length; i += 2)
            {
                var a = new Vector3(c[i], 0f, c[i + 1]); var b = new Vector3(c[i + 2], 0f, c[i + 3]);
                float len = Vector3.Distance(a, b);
                var dir = (b - a) / Mathf.Max(0.01f, len);
                var n = new Vector3(dir.z, 0f, -dir.x);
                for (float s = 0f; s < len; s += 5f + (float)rng.NextDouble() * 6f)
                {
                    var p = a + dir * s;
                    float gL = Ground(p + n * 8f), gR = Ground(p - n * 8f);
                    if (float.IsNaN(gL) && float.IsNaN(gR)) continue;
                    var land = (float.IsNaN(gR) || (!float.IsNaN(gL) && gL >= gR)) ? n : -n;
                    float landY = Mathf.Max(float.IsNaN(gL) ? -99f : gL, float.IsNaN(gR) ? -99f : gR);
                    var d = WorldData.DistrictAt(p + land * 8f);
                    string id = d != null ? d.id : "";
                    bool walled = (id == "D1" || id == "D3" || id == "D6" || (id == "D2" && landY - SeaY >= 2.5f)) && landY >= 0.4f;
                    if (walled) continue;
                    // iteration 10: surf foam band, 6 m seaward to 2 m landward of the data coastline, just above the water
                    // line (the landward part sinks under the terrain where the beach rises)
                    {
                        float step = Mathf.Min(6f, len - s);
                        if (step > 0.5f)
                        {
                            var p1 = p + dir * step;
                            var fm = Get(p).foam; int i0 = fm.V.Count; float y = SeaY + 0.04f;
                            float u0 = foamM / 9f, u1 = (foamM + step) / 9f;
                            fm.V.Add(new Vector3(p.x, y, p.z) - land * 6f); fm.V.Add(new Vector3(p1.x, y, p1.z) - land * 6f);
                            fm.V.Add(new Vector3(p1.x, y, p1.z) + land * 2f); fm.V.Add(new Vector3(p.x, y, p.z) + land * 2f);
                            for (int q = 0; q < 4; q++) fm.N.Add(Vector3.up);
                            fm.UV.Add(new Vector2(u0, 0f)); fm.UV.Add(new Vector2(u1, 0f)); fm.UV.Add(new Vector2(u1, 1f)); fm.UV.Add(new Vector2(u0, 1f));
                            bool cw = Vector3.Cross(dir, land).y < 0f;
                            if (cw) fm.T.AddRange(new[] { i0, i0 + 3, i0 + 2, i0, i0 + 2, i0 + 1 });
                            else fm.T.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
                            foamM += step;
                        }
                    }
                    int k = 1 + rng.Next(3);
                    for (int q = 0; q < k; q++)
                    {
                        var pos = p + land * (float)(rng.NextDouble() * 4 - 1.5) + dir * (float)(rng.NextDouble() * 3 - 1.5);
                        float g = Ground(pos);
                        float y = Mathf.Clamp(float.IsNaN(g) ? SeaY : g, SeaY - 2f, SeaY + 0.6f);
                        float size = 0.6f + (float)rng.NextDouble() * 1.9f;
                        Place(Get(pos).rock, rocks[rng.Next(rocks.Length)], new Vector3(pos.x, y - size * 0.25f, pos.z), size, (float)rng.NextDouble() * 360f, (float)rng.NextDouble());
                        seaRocks++;
                    }
                }
            }
            // Mirror Lake rim and shallows
            var lake = MirrorLake.LoadOrNull();
            if (lake != null && lake.PolygonXZ != null)
            {
                var poly = lake.PolygonXZ;
                for (int i = 0; i < poly.Count; i++)
                {
                    var a = new Vector3(poly[i].x, 0f, poly[i].y); var b = new Vector3(poly[(i + 1) % poly.Count].x, 0f, poly[(i + 1) % poly.Count].y);
                    float len = Vector3.Distance(a, b);
                    var dir = (b - a) / Mathf.Max(0.01f, len);
                    for (float s = 0f; s < len; s += 2f + (float)rng.NextDouble() * 2.5f)   // iteration 7: twice as dense (CAM_18 showed a bare rim)
                    {
                        var p = a + dir * s;
                        float g = Ground(p);
                        if (float.IsNaN(g)) continue;
                        if (rng.NextDouble() < 0.6)
                        {
                            float size = 0.5f + (float)rng.NextDouble() * 1.4f;
                            Place(Get(p).rock, rocks[rng.Next(rocks.Length)], new Vector3(p.x, Mathf.Min(g, lake.LevelM + 0.3f) - size * 0.3f, p.z), size, (float)rng.NextDouble() * 360f, (float)rng.NextDouble());
                            lakeRocks++;
                        }
                        // reeds just inside the water line (2-4 m towards the lake centre)
                        var centre = new Vector3(poly.Average(v => v.x), 0f, poly.Average(v => v.y));
                        var inward = (centre - p); inward.y = 0f; inward.Normalize();
                        int clumps = 1 + rng.Next(3);
                        for (int q = 0; q < clumps; q++)
                        {
                            var rp = p + inward * (1.5f + (float)rng.NextDouble() * 3f) + dir * (float)(rng.NextDouble() * 3 - 1.5);
                            Reeds(Get(rp).reed, new Vector3(rp.x, lake.LevelM - 0.3f, rp.z), 1.4f + (float)rng.NextDouble() * 1.0f, (float)rng.NextDouble() * 180f);
                            reeds++;
                        }
                    }
                }
            }
            var keep = new HashSet<string>();
            foreach (var kv in chunks)
            {
                var go = new GameObject(kv.Key) { isStatic = true };
                go.transform.SetParent(root, false);
                void Add(MB mb, string name, Material mat, ShadowCastingMode sh)
                {
                    if (mb.T.Count == 0 || mat == null) return;
                    var mesh = Save($"SH_{kv.Key}_{name}", mb);
                    keep.Add(mesh.name);
                    var r = new GameObject(name) { isStatic = true };
                    r.transform.SetParent(go.transform, false);
                    r.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = r.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = sh;
                    var lg = r.AddComponent<LODGroup>();
                    lg.SetLODs(new[] { new LOD(name == "Reeds" ? 0.03f : name == "SurfFoam" ? 0.02f : 0.008f, new Renderer[] { mr }) });
                }
                Add(kv.Value.rock, "Boulders", rockMat, ShadowCastingMode.On);
                Add(kv.Value.reed, "Reeds", reedMat, ShadowCastingMode.Off);
                Add(kv.Value.foam, "SurfFoam", foamMat, ShadowCastingMode.Off);
            }
            foreach (var f in Directory.GetFiles(MeshDir, "*.asset"))
                if (!keep.Contains(Path.GetFileNameWithoutExtension(f))) AssetDatabase.DeleteAsset(f.Replace(Path.DirectorySeparatorChar, '/'));
            AssetDatabase.SaveAssets();
            string rep = $"Shoreline: {seaRocks} sea boulders (natural coast), {lakeRocks} lake boulders, {reeds} reed clumps, {foamM:0} m of surf foam in {chunks.Count} chunks\n";
            Directory.CreateDirectory("Logs/B1");
            File.WriteAllText("Logs/B1/shoreline.txt", rep);
            return rep;
        }

        /// <summary>Iteration 10: transparent surf foam (procedural streak texture, alpha fades out to sea).</summary>
        static Material FoamMaterial()
        {
            const string texPath = "Assets/_Veyra/Art/Textures/Water/T_Shore_Foam.png";
            Directory.CreateDirectory(Path.GetDirectoryName(texPath));
            if (!File.Exists(texPath))
            {
                const int W = 512, H = 256;
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
                var px = new Color32[W * H];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float v = y / (float)(H - 1);
                        float u = x / (float)W;
                        // tileable along u: noise on a circle
                        float ang = u * Mathf.PI * 2f;
                        float cx = Mathf.Cos(ang) * 3f, cy = Mathf.Sin(ang) * 3f;
                        float n1 = Mathf.PerlinNoise(cx + 10f, cy + v * 6f);
                        float n2 = Mathf.PerlinNoise(cx * 3f + 40f, cy * 3f + v * 18f);
                        // wave lines parallel to the shore at v ~ 0.35, 0.55, 0.72 plus a solid wash at the land edge
                        float lines = 0f;
                        foreach (float c0 in new[] { 0.35f, 0.55f, 0.72f })
                        {
                            float c1 = c0 + (n1 - 0.5f) * 0.12f;
                            lines = Mathf.Max(lines, Mathf.Clamp01(1f - Mathf.Abs(v - c1) / 0.035f));
                        }
                        float wash = Edge(0.72f, 0.95f, v);
                        float a = Mathf.Max(lines * Edge(0.25f, 0.7f, n2 + 0.2f), wash * (0.55f + 0.45f * n2));
                        a *= Edge(0f, 0.25f, v);
                        byte b = (byte)(235 + 20 * n2);
                        px[y * W + x] = new Color32(b, b, (byte)Mathf.Min(255, b + 6), (byte)(Mathf.Clamp01(a * 0.85f) * 255));
                    }
                tex.SetPixels32(px);
                File.WriteAllBytes(texPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(texPath);
                var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
                ti.alphaIsTransparency = true; ti.wrapModeU = TextureWrapMode.Repeat; ti.wrapModeV = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
            string path = $"{MatDir}/M_Shore_Foam.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_Shore_Foam" }; AssetDatabase.CreateAsset(m, path); }   // Editor code
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            m.SetColor("_BaseColor", new Color(0.92f, 0.95f, 0.96f, 1f));
            m.SetFloat("_Smoothness", 0.35f);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent + 5;
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>GLSL-style smoothstep(e0, e1, x) (Mathf.SmoothStep interpolates between its first two arguments instead).</summary>
        static float Edge(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }

        static Material RockMaterial()
        {
            string path = $"{MatDir}/M_Shore_Rock.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_Shore_Rock" }; AssetDatabase.CreateAsset(m, path); }   // Editor code
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Veyra/Art/Terrain/T_L_Rock_A.png"));
            m.SetTextureScale("_BaseMap", new Vector2(0.35f, 0.35f));
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Veyra/Art/Terrain/T_L_Rock_N.png");
            if (nrm != null) { m.SetTexture("_BumpMap", nrm); m.EnableKeyword("_NORMALMAP"); }
            m.SetColor("_BaseColor", new Color(0.85f, 0.83f, 0.8f));
            m.SetFloat("_Smoothness", 0.22f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>A unit boulder: icosphere (2 subdivisions) displaced by seeded noise, flattened base.</summary>
        static Mesh Boulder(int seed)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var src = tmp.GetComponent<MeshFilter>().sharedMesh;
            var v = src.vertices; var t = src.triangles;
            Object.DestroyImmediate(tmp);
            var rng = new System.Random(seed * 31 + 7);
            float sx = 0.8f + (float)rng.NextDouble() * 0.5f, sy = 0.5f + (float)rng.NextDouble() * 0.3f, sz = 0.8f + (float)rng.NextDouble() * 0.5f;
            var outV = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var p = v[i];
                float n = ProcTextures.Fbm(p.x * 0.5f + 0.5f + seed, p.z * 0.5f + 0.5f, 3, 3, seed + 3) - 0.5f;
                p *= 1f + n * 0.7f;
                p = new Vector3(p.x * sx, Mathf.Max(p.y, -0.15f) * sy, p.z * sz);
                outV[i] = p;
            }
            var m = new Mesh { name = "Boulder_" + seed };
            m.vertices = outV; m.triangles = t;
            m.RecalculateNormals();
            return m;
        }

        static void Place(MB mb, Mesh m, Vector3 pos, float size, float yaw, float tilt)
        {
            var q = Quaternion.Euler(tilt * 12f, yaw, tilt * 8f);
            int b = mb.V.Count;
            var v = m.vertices; var n = m.normals;
            for (int i = 0; i < v.Length; i++)
            {
                var w = pos + q * (v[i] * size);
                mb.V.Add(w); mb.N.Add(q * n[i]);
                mb.UV.Add(new Vector2(w.x + w.y * 0.7f, w.z + w.y * 0.5f) * 0.5f);
            }
            foreach (int i in m.triangles) mb.T.Add(b + i);
        }

        /// <summary>Three crossed reed cards (grass blade texture), height h.</summary>
        static void Reeds(MB mb, Vector3 p, float h, float yaw)
        {
            for (int k = 0; k < 3; k++)
            {
                float a = (yaw + k * 60f) * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.45f;
                int i = mb.V.Count;
                mb.V.Add(p - d); mb.V.Add(p + d); mb.V.Add(p + d + Vector3.up * h); mb.V.Add(p - d + Vector3.up * h);
                for (int q = 0; q < 4; q++) mb.N.Add(Vector3.up);
                mb.UV.Add(new Vector2(0, 0)); mb.UV.Add(new Vector2(1, 0)); mb.UV.Add(new Vector2(1, 1)); mb.UV.Add(new Vector2(0, 1));
                mb.T.AddRange(new[] { i, i + 3, i + 2, i, i + 2, i + 1 });
            }
        }

        static Mesh Save(string name, MB mb)
        {
            string path = $"{MeshDir}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh { name = name };
            mesh.Clear();
            mesh.indexFormat = mb.V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(mb.V); mesh.SetNormals(mb.N); mesh.SetUVs(0, mb.UV); mesh.SetTriangles(mb.T, 0);
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            if (created) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
