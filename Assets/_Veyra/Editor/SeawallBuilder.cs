using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.Core;
using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Phase B (W3): "Veyra/World/Build Seawalls". Concrete seawalls and quay walls along the data coastline
    /// (districts.json coastline, Unity x/z) where the city meets the sea: every stretch in D1 Crown Downtown, D3 Ironport and
    /// D6 Neon Mile, and in D2 Azure Coast where the land 8 m inland stands 2.5 m or more above the sea (cliffs and promenades;
    /// beaches stay open). The wall runs along the coastline at 4 m steps from 0.25 m above the land behind it down to 4 m below
    /// the sea, with a 0.6 m capstone; collision is a non-convex MeshCollider (COL_seawall) so cars cannot drop into the sea.
    /// Output: World/Seawalls/Chunk_&lt;Cell&gt;_&lt;ij&gt; (one mesh per chunk, LOD0 + LOD1), meshes in World/Generated/Seawalls.
    /// Idempotent. Rivers: not built yet (the river polyline has no banks/width in the data; recorded in BUG_TRACKER).
    /// </summary>
    public static class SeawallBuilder
    {
        const string Dir = "Assets/_Veyra/World/Generated/Seawalls";
        const float Step = 4f, SeaY = -0.15f, Bottom = -4f, Cap = 0.6f;

        [MenuItem("Veyra/World/Build Seawalls")]
        public static void Menu() => Debug.Log(Build());

        sealed class MB { public List<Vector3> V = new List<Vector3>(); public List<Vector2> UV = new List<Vector2>(); public List<int> T = new List<int>(); }

        public static string Build()
        {
            WorldData.EnsureLoaded();
            var world = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == "World");
            if (world == null) return "FAIL: no World root";
            var old = world.transform.Find("Seawalls");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("Seawalls") { isStatic = true }.transform;
            root.SetParent(world.transform, false);
            Directory.CreateDirectory(Dir);
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Veyra/World/Generated/Materials/M_Concrete_Deck.mat")
                      ?? AssetDatabase.LoadAssetAtPath<Material>("Assets/_Veyra/World/Generated/Materials/Concrete.mat");
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
            var c = WorldData.Districts.coastline;
            var pts = new List<Vector3>();
            for (int i = 0; i + 1 < c.Length; i += 2) pts.Add(new Vector3(c[i], 0f, c[i + 1]));
            var chunks = new Dictionary<string, (MB vis, MB col)>();
            float built = 0f;
            var perDistrict = new Dictionary<string, float>();
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var a = pts[i]; var b = pts[i + 1];
                float len = Vector3.Distance(a, b);
                var dir = (b - a) / Mathf.Max(0.01f, len);
                var n = new Vector3(dir.z, 0f, -dir.x);
                for (float s = 0f; s < len; s += Step)
                {
                    var p0 = a + dir * s; var p1 = a + dir * Mathf.Min(len, s + Step);
                    var mid = (p0 + p1) * 0.5f;
                    // land side: the side whose ground 8 m out is higher
                    float gL = Ground(mid + n * 8f), gR = Ground(mid - n * 8f);
                    if (float.IsNaN(gL) && float.IsNaN(gR)) continue;
                    var land = (float.IsNaN(gR) || (!float.IsNaN(gL) && gL >= gR)) ? n : -n;
                    float landY = Mathf.Max(float.IsNaN(gL) ? -99f : gL, float.IsNaN(gR) ? -99f : gR);
                    var d = WorldData.DistrictAt(mid + land * 8f);
                    string id = d != null ? d.id : "";
                    bool urban = id == "D1" || id == "D3" || id == "D6";
                    if (!(urban || (id == "D2" && landY - SeaY >= 2.5f))) continue;
                    if (landY < 0.4f) continue;   // nothing to retain
                    float top0 = Mathf.Max(Ground(p0 + land * 3f), 0.6f) + 0.25f, top1 = Mathf.Max(Ground(p1 + land * 3f), 0.6f) + 0.25f;
                    if (float.IsNaN(top0)) top0 = landY + 0.25f;
                    if (float.IsNaN(top1)) top1 = landY + 0.25f;
                    var w = WorldCoordinates.FromUnity(mid);
                    string chunk = WorldCoordinates.ChunkOf(Mathf.Clamp(w.x, 0f, 7999f), Mathf.Clamp(w.y, 0f, 5999f));
                    if (!chunks.TryGetValue(chunk, out var mb)) chunks[chunk] = mb = (new MB(), new MB());
                    var sea = -land;
                    // sea face, capstone (land side 0.6 m), and its back face down to the ground
                    Quad(mb.vis, p0 + Vector3.up * Bottom, p1 + Vector3.up * Bottom, p1 + Vector3.up * top1, p0 + Vector3.up * top0, s, Bottom, top0, sea);
                    Quad(mb.vis, p0 + Vector3.up * top0, p1 + Vector3.up * top1, p1 + land * Cap + Vector3.up * top1, p0 + land * Cap + Vector3.up * top0, s, 0f, Cap, Vector3.up);
                    Quad(mb.vis, p0 + land * Cap + Vector3.up * top0, p1 + land * Cap + Vector3.up * top1, p1 + land * Cap + Vector3.up * (top1 - 1.5f), p0 + land * Cap + Vector3.up * (top0 - 1.5f), s, top0 - 1.5f, top0, land);
                    Quad(mb.col, p0 + Vector3.up * Bottom, p1 + Vector3.up * Bottom, p1 + Vector3.up * (top1 + 0.9f), p0 + Vector3.up * (top0 + 0.9f), s, 0f, 1f, sea);
                    built += Vector3.Distance(p0, p1);
                    perDistrict[id] = perDistrict.TryGetValue(id, out float x) ? x + Vector3.Distance(p0, p1) : Vector3.Distance(p0, p1);
                }
            }
            var keep = new HashSet<string>();
            foreach (var kv in chunks)
            {
                var go = new GameObject(kv.Key) { isStatic = true };
                go.transform.SetParent(root, false);
                var vis = new GameObject("Seawall_LOD0") { isStatic = true };
                vis.transform.SetParent(go.transform, false);
                vis.AddComponent<MeshFilter>().sharedMesh = Save($"SW_{kv.Key}", kv.Value.vis, true, keep);
                var mr = vis.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.On;
                var lg = go.AddComponent<LODGroup>();
                lg.SetLODs(new[] { new LOD(0.01f, new Renderer[] { mr }) });   // a wall strip: one level, culled when tiny
                var col = new GameObject("COL_seawall") { isStatic = true };
                col.transform.SetParent(go.transform, false);
                col.AddComponent<MeshCollider>().sharedMesh = Save($"SW_{kv.Key}_col", kv.Value.col, false, keep);
            }
            foreach (var f in Directory.GetFiles(Dir, "*.asset"))
                if (!keep.Contains(Path.GetFileNameWithoutExtension(f))) AssetDatabase.DeleteAsset(f.Replace('\\', '/'));
            AssetDatabase.SaveAssets();
            string r = $"Seawalls: {built:N0} m in {chunks.Count} chunks (" + string.Join(", ", perDistrict.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value:N0} m")) + ")\n";
            Directory.CreateDirectory("Logs/B1");
            File.WriteAllText("Logs/B1/seawalls.txt", r);
            return r;
        }

        /// <summary>Quad a-b-c-d (counter-clockwise seen from <paramref name="facing"/>), UV u = along metres, v = height.</summary>
        static void Quad(MB m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float s, float v0, float v1, Vector3 facing)
        {
            int i = m.V.Count;
            m.V.Add(a); m.V.Add(b); m.V.Add(c); m.V.Add(d);
            float L = Vector3.Distance(a, b);
            m.UV.Add(new Vector2(s / 4f, v0 / 4f)); m.UV.Add(new Vector2((s + L) / 4f, v0 / 4f)); m.UV.Add(new Vector2((s + L) / 4f, v1 / 4f)); m.UV.Add(new Vector2(s / 4f, v1 / 4f));
            var n = Vector3.Cross(b - a, d - a);
            // Unity's front face (and RecalculateNormals) is cross(v1 - v0, v2 - v0)
            if (Vector3.Dot(n, facing) >= 0f) { m.T.Add(i); m.T.Add(i + 1); m.T.Add(i + 2); m.T.Add(i); m.T.Add(i + 2); m.T.Add(i + 3); }
            else { m.T.Add(i); m.T.Add(i + 2); m.T.Add(i + 1); m.T.Add(i); m.T.Add(i + 3); m.T.Add(i + 2); }
        }

        static Mesh Save(string name, MB mb, bool render, HashSet<string> keep)
        {
            string path = $"{Dir}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh { name = name };
            mesh.Clear();
            mesh.indexFormat = mb.V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(mb.V);
            mesh.SetUVs(0, mb.UV);
            mesh.SetTriangles(mb.T, 0);
            mesh.RecalculateNormals();
            if (render) mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            if (created) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
            keep.Add(name);
            return mesh;
        }
    }
}
