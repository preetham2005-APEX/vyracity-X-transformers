using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.Core;
using VeyraCity.Testing;
using VeyraCity.World;
using VeyraCity.World.Data;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// World iteration 15 (CAM_05: Crown Plaza was an empty paved field; the registry describes "a granite pedestrian
    /// plaza with fountains"). WorldBuildBatch step `plaza`. For each landmark plaza in the registry (Category Landmark,
    /// name containing "Plaza"):
    /// <list type="bullet">
    /// <item>a tiered granite fountain on the registry point (octagonal basin 14 m across, water, pedestal, two bowls);</item>
    /// <item>benches (wood slats on steel legs) on a ring facing the fountain, granite planters with a bush on a 14 m grid,
    /// and steel bollards along the plaza edge.</item>
    /// </list>
    /// Everything is placed only on free paving: inside 60 m of the registry point, at least 3 m clear of every building
    /// footprint (data placements) and 3.5 m clear of every road edge (road network); heights from the terrain under the
    /// point. Procedural meshes (original), existing library materials. Fountain basin and planters get box colliders;
    /// benches and bollards none. Merged per material under World/Plazas/&lt;id&gt; with one LODGroup each. Idempotent.
    /// Report: Logs/C/plazas.txt.
    /// </summary>
    public static class PlazaBuilder
    {
        const string MeshDir = "Assets/_Veyra/World/Generated/Plazas";
        const string MatDir = "Assets/_Veyra/World/Generated/Materials";
        const float Radius = 60f;

        sealed class MB { public List<Vector3> V = new List<Vector3>(); public List<Vector2> UV = new List<Vector2>(); public List<int> T = new List<int>(); }

        public static string Run()
        {
            WorldData.EnsureLoaded();
            var sb = new StringBuilder("Plazas (iteration 15)\n");
            var world = EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == "World");
            if (world == null) return "FAIL: no World root\n";
            var old = world.transform.Find("Plazas");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("Plazas") { isStatic = true }.transform;
            root.SetParent(world.transform, false);
            Directory.CreateDirectory(MeshDir);

            var granite = Mat("M_Kerb_Granite"); var water = Mat("M_Water"); var wood = Mat("M_Wood_Weathered") ?? Mat("M_Facade_Wood");
            var steel = Mat("M_Metal_Painted") ?? Mat("M_Metal_Galvanized");
            var bush = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Veyra/Art/Models/Vegetation/VC_Tree_Bush.fbx");
            var reg = LocationRegistry.Parse(VeyraDataValidator.LoadText("location_registry.csv"));
            var buildings = PlacementReader.Buildings(VeyraDataValidator.LoadText("world_building_placements.csv"), "world_building_placements.csv");
            var terrains = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include);
            float Ground(Vector3 p, float fallback)
            {
                foreach (var t in terrains)
                {
                    var o = t.GetPosition(); var s = t.terrainData.size;
                    if (p.x >= o.x && p.x <= o.x + s.x && p.z >= o.z && p.z <= o.z + s.z) return t.SampleHeight(p) + o.y;
                }
                return fallback;
            }
            bool Free(Vector3 p, float clearance)
            {
                foreach (var b in buildings)
                {
                    float dx = p.x - b.UnityX, dz = p.z - b.UnityZ;
                    if (dx * dx + dz * dz > 120f * 120f) continue;
                    var local = Quaternion.Euler(0f, -b.UnityYawDeg, 0f) * new Vector3(dx, 0f, dz);
                    if (Mathf.Abs(local.x) < b.FootprintWM * 0.5f + clearance && Mathf.Abs(local.z) < b.FootprintLM * 0.5f + clearance) return false;
                }
                if (WorldData.NearestRoad(p, 30f, out var hit) && hit.Distance < WorldData.Roads[hit.Road].HalfWidth + 3.5f + clearance - 1f) return false;
                return true;
            }

            var keep = new HashSet<string>();
            foreach (var loc in reg.All.Where(l => l.Category == "Landmark" && l.Name.Contains("Plaza")))
            {
                var c = loc.Unity;
                var parts = new Dictionary<Material, MB>();
                MB Get(Material m) { if (!parts.TryGetValue(m, out var mb)) parts[m] = mb = new MB(); return mb; }
                var colliders = new List<(Vector3 c, Vector3 size, float yaw)>();
                int benches = 0, planters = 0, bollards = 0; bool fountain = false;

                // fountain on the registry point
                if (Free(c, 7.5f))
                {
                    float y = Ground(c, c.y);
                    var p0 = new Vector3(c.x, y, c.z);
                    Ring(Get(granite), p0, 7f, 6.4f, -0.3f, 0.55f, 8);               // basin wall
                    Disc(Get(granite), p0 + Vector3.up * 0.05f, 6.45f, 8);          // basin floor
                    Disc(Get(water), p0 + Vector3.up * 0.42f, 6.45f, 8);            // water
                    Cyl(Get(granite), p0, 1.2f, 0f, 1.25f, 12);                      // pedestal
                    Bowl(Get(granite), p0 + Vector3.up * 1.25f, 2.6f, 0.35f, 16);
                    Disc(Get(water), p0 + Vector3.up * 1.52f, 2.4f, 16);
                    Cyl(Get(granite), p0, 0.38f, 1.25f, 2.7f, 10);                   // column
                    Bowl(Get(granite), p0 + Vector3.up * 2.7f, 1.1f, 0.25f, 12);
                    Disc(Get(water), p0 + Vector3.up * 2.9f, 0.95f, 12);
                    Cyl(Get(granite), p0, 0.18f, 2.7f, 3.4f, 8);                     // finial
                    for (int k = 0; k < 2; k++) colliders.Add((p0 + Vector3.up * 0.15f, new Vector3(14f * Mathf.Cos(Mathf.PI / 8f), 0.9f, 14f * Mathf.Cos(Mathf.PI / 8f)), k * 45f + 22.5f));   // octagon = two squares
                    colliders.Add((p0 + Vector3.up * 1.7f, new Vector3(2.4f, 3.4f, 2.4f), 0f));
                    fountain = true;
                }
                // benches on a ring, facing the fountain
                for (int k = 0; k < 10; k++)
                {
                    float a = k / 10f * Mathf.PI * 2f + 0.15f;
                    var p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 11f;
                    if (!Free(p, 1.2f)) continue;
                    p.y = Ground(p, c.y);
                    var fwd = (c - p); fwd.y = 0f; fwd.Normalize();
                    Bench(Get(wood), Get(steel), p, fwd);
                    benches++;
                }
                // planters with a bush on a 14 m grid between 18 m and Radius, bollards on the plaza edge
                var rng = new System.Random(loc.Id.GetHashCode());
                for (float gx = -Radius; gx <= Radius; gx += 14f)
                    for (float gz = -Radius; gz <= Radius; gz += 14f)
                    {
                        float d = new Vector2(gx, gz).magnitude;
                        if (d < 18f || d > Radius) continue;
                        var p = c + new Vector3(gx + (float)(rng.NextDouble() - 0.5) * 2f, 0f, gz + (float)(rng.NextDouble() - 0.5) * 2f);
                        if (!Free(p, 2f)) continue;
                        p.y = Ground(p, c.y);
                        Box(Get(granite), p + Vector3.up * 0.3f, Vector3.right, Vector3.forward, 2.4f, 0.6f, 2.4f);
                        colliders.Add((p + Vector3.up * 0.3f, new Vector3(2.4f, 0.6f, 2.4f), 0f));
                        if (bush != null)
                        {
                            var g = (GameObject)PrefabUtility.InstantiatePrefab(bush);
                            g.name = "PlanterBush";
                            g.transform.position = p + Vector3.up * 0.55f;
                            g.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f) * g.transform.rotation;
                            g.transform.localScale = Vector3.one * (0.9f + (float)rng.NextDouble() * 0.3f);
                            parentLater.Add(g);
                        }
                        planters++;
                    }
                for (int k = 0; k < 72; k++)
                {
                    float a = k / 72f * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    // walk out to the last free point: the plaza edge in this direction
                    Vector3 last = Vector3.positiveInfinity;
                    for (float r = 8f; r <= Radius + 6f; r += 1.5f) { var q = c + dir * r; if (Free(q, 0.6f)) last = q; else if (r > 14f) break; }
                    if (float.IsInfinity(last.x) || (last - c).magnitude < 16f) continue;
                    last.y = Ground(last, c.y);
                    Cyl(Get(steel), last, 0.11f, 0f, 0.95f, 8);
                    bollards++;
                }

                var go = new GameObject(loc.Id) { isStatic = true };
                go.transform.SetParent(root, false);
                foreach (var kv in parts)
                {
                    if (kv.Value.T.Count == 0 || kv.Key == null) continue;
                    string name = $"PZ_{loc.Id}_{kv.Key.name}";
                    var mesh = Save(name, kv.Value);
                    keep.Add(name);
                    var r = new GameObject(kv.Key.name) { isStatic = true };
                    r.transform.SetParent(go.transform, false);
                    r.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = r.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = kv.Key;
                    mr.shadowCastingMode = kv.Key == water ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    var lg = r.AddComponent<LODGroup>();
                    lg.SetLODs(new[] { new LOD(0.01f, new Renderer[] { mr }) });
                    lg.RecalculateBounds();
                }
                foreach (var g in parentLater) g.transform.SetParent(go.transform, true);
                parentLater.Clear();
                var cols = new GameObject("UBX_Plaza") { isStatic = true };
                cols.transform.SetParent(go.transform, false);
                foreach (var (cc, size, yaw) in colliders)
                {
                    var bx = new GameObject("UBX_part") { isStatic = true };
                    bx.transform.SetParent(cols.transform, false);
                    bx.transform.SetPositionAndRotation(cc, Quaternion.Euler(0f, yaw, 0f));
                    bx.AddComponent<BoxCollider>().size = size;
                }
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
                sb.AppendLine($"  {loc.Id} {loc.Name} ({loc.DistrictId}) at ({c.x:0}, {c.z:0}): fountain {(fountain ? "yes" : "no (point not free)")}, {benches} benches, {planters} planters, {bollards} bollards, {colliders.Count} box colliders");
            }
            foreach (var f in Directory.GetFiles(MeshDir, "*.asset"))
                if (!keep.Contains(Path.GetFileNameWithoutExtension(f))) AssetDatabase.DeleteAsset(f.Replace('\\', '/'));
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs/C");
            File.WriteAllText("Logs/C/plazas.txt", sb.ToString());
            return sb.ToString();
        }

        static readonly List<GameObject> parentLater = new List<GameObject>();

        static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{name}.mat");

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

        static void Box(MB m, Vector3 c, Vector3 ax, Vector3 az, float sx, float sy, float sz)
        {
            ax = ax.normalized * sx * 0.5f; az = az.normalized * sz * 0.5f; var ay = Vector3.up * sy * 0.5f;
            Vector3 P(int x, int y, int z) => c + ax * x + ay * y + az * z;
            Quad(m, P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), -az);
            Quad(m, P(1, -1, 1), P(-1, -1, 1), P(-1, 1, 1), P(1, 1, 1), az);
            Quad(m, P(-1, -1, 1), P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), -ax);
            Quad(m, P(1, -1, -1), P(1, -1, 1), P(1, 1, 1), P(1, 1, -1), ax);
            Quad(m, P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), Vector3.up);
        }

        static Vector3 Dir(float a) => new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));

        /// <summary>Vertical ring wall (outer r0, inner r1) from y0 to y1 with a flat top.</summary>
        static void Ring(MB m, Vector3 c, float r0, float r1, float y0, float y1, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float a0 = i / (float)n * Mathf.PI * 2f + Mathf.PI / n, a1 = (i + 1) / (float)n * Mathf.PI * 2f + Mathf.PI / n;
                var o0 = c + Dir(a0) * r0; var o1 = c + Dir(a1) * r0; var i0 = c + Dir(a0) * r1; var i1 = c + Dir(a1) * r1;
                var mid = Dir((a0 + a1) * 0.5f);
                Quad(m, o0 + Vector3.up * y0, o1 + Vector3.up * y0, o1 + Vector3.up * y1, o0 + Vector3.up * y1, mid);
                Quad(m, i1 + Vector3.up * y0, i0 + Vector3.up * y0, i0 + Vector3.up * y1, i1 + Vector3.up * y1, -mid);
                Quad(m, o0 + Vector3.up * y1, o1 + Vector3.up * y1, i1 + Vector3.up * y1, i0 + Vector3.up * y1, Vector3.up);
            }
        }

        static void Disc(MB m, Vector3 c, float r, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float a0 = i / (float)n * Mathf.PI * 2f + Mathf.PI / n, a1 = (i + 1) / (float)n * Mathf.PI * 2f + Mathf.PI / n;
                int k = m.V.Count;
                m.V.Add(c); m.V.Add(c + Dir(a0) * r); m.V.Add(c + Dir(a1) * r);
                m.UV.Add(new Vector2(c.x, c.z)); m.UV.Add(new Vector2(m.V[k + 1].x, m.V[k + 1].z)); m.UV.Add(new Vector2(m.V[k + 2].x, m.V[k + 2].z));
                var nrm = Vector3.Cross(m.V[k + 1] - c, m.V[k + 2] - c);
                if (nrm.y >= 0f) m.T.AddRange(new[] { k, k + 1, k + 2 }); else m.T.AddRange(new[] { k, k + 2, k + 1 });
            }
        }

        static void Cyl(MB m, Vector3 c, float r, float y0, float y1, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float a0 = i / (float)n * Mathf.PI * 2f, a1 = (i + 1) / (float)n * Mathf.PI * 2f;
                Quad(m, c + Dir(a0) * r + Vector3.up * y0, c + Dir(a1) * r + Vector3.up * y0, c + Dir(a1) * r + Vector3.up * y1, c + Dir(a0) * r + Vector3.up * y1, Dir((a0 + a1) * 0.5f));
            }
            Disc(m, c + Vector3.up * y1, r, n);
        }

        /// <summary>A shallow bowl: flaring outer wall from r*0.35 at the base to r at height h, flat rim, inner floor.</summary>
        static void Bowl(MB m, Vector3 c, float r, float h, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float a0 = i / (float)n * Mathf.PI * 2f, a1 = (i + 1) / (float)n * Mathf.PI * 2f;
                var mid = Dir((a0 + a1) * 0.5f);
                Quad(m, c + Dir(a0) * r * 0.35f, c + Dir(a1) * r * 0.35f, c + Dir(a1) * r + Vector3.up * h, c + Dir(a0) * r + Vector3.up * h, mid - Vector3.up * 0.5f);
                Quad(m, c + Dir(a0) * r + Vector3.up * h, c + Dir(a1) * r + Vector3.up * h, c + Dir(a1) * (r - 0.15f) + Vector3.up * h, c + Dir(a0) * (r - 0.15f) + Vector3.up * h, Vector3.up);
            }
            Disc(m, c + Vector3.up * (h * 0.4f), r - 0.15f, n);
        }

        /// <summary>A 1.9 m bench: three seat slats, two back slats, two steel A-legs; faces fwd.</summary>
        static void Bench(MB wood, MB steel, Vector3 p, Vector3 fwd)
        {
            var right = Vector3.Cross(Vector3.up, fwd).normalized;
            for (int s = 0; s < 3; s++) Box(wood, p + Vector3.up * 0.44f + fwd * (0.16f - s * 0.15f), right, fwd, 1.9f, 0.04f, 0.12f);
            for (int s = 0; s < 2; s++) Box(wood, p + Vector3.up * (0.62f + s * 0.16f) - fwd * 0.25f, right, fwd, 1.9f, 0.11f, 0.035f);
            for (int s = -1; s <= 1; s += 2)
            {
                Box(steel, p + right * s * 0.8f + Vector3.up * 0.21f, right, fwd, 0.05f, 0.42f, 0.42f);
                Box(steel, p + right * s * 0.8f + Vector3.up * 0.62f - fwd * 0.26f, right, fwd, 0.05f, 0.4f, 0.05f);
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
            mesh.SetVertices(mb.V); mesh.SetUVs(0, mb.UV); mesh.SetTriangles(mb.T, 0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            if (created) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
