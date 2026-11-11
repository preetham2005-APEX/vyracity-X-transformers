using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// World iteration 21 (BUG-121): placement-data props (lamps, trees, bushes) whose base lies inside the RENDERED
    /// carriageway stand in the traffic lanes since the B roads draw the wider drive surface. The data is the authority,
    /// so nothing is moved: such placements are skipped (removed from the InstancedPropRenderer cells) and the prop
    /// colliders are rebuilt so no capsule is left on the road.
    /// "Inside" = the base point and four points 0.75 m around it all lie on M_Asphalt triangles within 2 m of the base
    /// height (a lamp on the kerb line keeps its place). Idempotent: a second run removes nothing.
    /// Report: Logs/ITER/propclear.txt (every removed placement).
    /// </summary>
    public static class PropRoadClear
    {
        const float Grid = 10f;
        const float Inset = 0.75f;

        public static string Run()
        {
            var ipr = Object.FindAnyObjectByType<InstancedPropRenderer>();
            if (ipr == null) return "propclear: no InstancedPropRenderer in the open scene";
            GameObject world = null;
            foreach (var r in ipr.gameObject.scene.GetRootGameObjects()) if (r.name == "World") world = r;
            if (world == null) return "propclear: no World root";

            var grid = new Dictionary<long, List<Vector3>>();
            long Key(float x, float z) => ((long)Mathf.FloorToInt(x / Grid) << 32) ^ (uint)Mathf.FloorToInt(z / Grid);
            int meshes = 0;
            foreach (var mf in world.GetComponentsInChildren<MeshFilter>(false))
            {
                if (mf.sharedMesh == null || mf.name.EndsWith("_LOD1") || mf.name.EndsWith("_LOD2")) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled) continue;
                var sm = mr.sharedMaterials;
                var mesh = mf.sharedMesh;
                Vector3[] vv = null;
                var l2w = mf.transform.localToWorldMatrix;
                for (int si = 0; si < mesh.subMeshCount && si < sm.Length; si++)
                {
                    var m = sm[si];
                    if (m == null || !m.name.StartsWith("M_Asphalt")) continue;
                    if (vv == null) { vv = mesh.vertices; meshes++; }
                    var tt = mesh.GetTriangles(si);
                    for (int i = 0; i < tt.Length; i += 3)
                    {
                        var a = l2w.MultiplyPoint3x4(vv[tt[i]]); var b = l2w.MultiplyPoint3x4(vv[tt[i + 1]]); var c = l2w.MultiplyPoint3x4(vv[tt[i + 2]]);
                        float x0 = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), x1 = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                        float z0 = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), z1 = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                        if (x1 - x0 > 200f || z1 - z0 > 200f) continue;
                        for (float gx = Mathf.Floor(x0 / Grid) * Grid; gx <= x1; gx += Grid)
                            for (float gz = Mathf.Floor(z0 / Grid) * Grid; gz <= z1; gz += Grid)
                            {
                                long k = Key(gx + 0.01f, gz + 0.01f);
                                if (!grid.TryGetValue(k, out var lst)) grid[k] = lst = new List<Vector3>();
                                lst.Add(a); lst.Add(b); lst.Add(c);
                            }
                    }
                }
            }

            bool OnAsphalt(float x, float z, float y)
            {
                if (!grid.TryGetValue(Key(x, z), out var lst)) return false;
                for (int i = 0; i < lst.Count; i += 3)
                {
                    var a = lst[i]; var b = lst[i + 1]; var c = lst[i + 2];
                    float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(d) < 1e-6f) continue;
                    float w0 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d;
                    float w1 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d;
                    float w2 = 1f - w0 - w1;
                    if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
                    if (Mathf.Abs(w0 * a.y + w1 * b.y + w2 * c.y - y) < 2f) return true;
                }
                return false;
            }

            var report = new StringBuilder("prototype,unity_x,unity_y,unity_z\n");
            var perProto = new Dictionary<string, int>();
            int removed = 0, kept = 0;
            var cells = new List<InstancedPropRenderer.Cell>();
            foreach (var cell in ipr.Cells)
            {
                if (cell.Placements == null || cell.Prototype < 0 || cell.Prototype >= ipr.Prototypes.Length) { cells.Add(cell); continue; }
                string name = ipr.Prototypes[cell.Prototype].Name;
                var p = new List<Vector4>(); var s = new List<float>();
                for (int i = 0; i < cell.Placements.Length; i++)
                {
                    var q = cell.Placements[i];
                    bool inside = OnAsphalt(q.x, q.z, q.y) && OnAsphalt(q.x + Inset, q.z, q.y) && OnAsphalt(q.x - Inset, q.z, q.y)
                                  && OnAsphalt(q.x, q.z + Inset, q.y) && OnAsphalt(q.x, q.z - Inset, q.y);
                    if (inside)
                    {
                        removed++;
                        perProto[name] = perProto.TryGetValue(name, out int n) ? n + 1 : 1;
                        report.Append($"{name},{q.x:F1},{q.y:F2},{q.z:F1}\n");
                        continue;
                    }
                    kept++;
                    p.Add(q);
                    s.Add(cell.Scales != null && i < cell.Scales.Length ? cell.Scales[i] : 1f);
                }
                if (p.Count == 0) continue;
                cell.Placements = p.ToArray();
                cell.Scales = s.ToArray();
                cells.Add(cell);
            }
            ipr.Cells = cells.ToArray();
            UnityEditor.EditorUtility.SetDirty(ipr);
            Directory.CreateDirectory("Logs/ITER");
            File.WriteAllText("Logs/ITER/propclear.txt", report.ToString());
            string colliders = PropColliderBuilder.Build(false);
            string by = string.Join(", ", perProto.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"));
            return $"Prop road clear: {meshes} asphalt meshes, {removed} placements on the carriageway skipped ({by}), {kept:N0} kept; {colliders}";
        }
    }
}
