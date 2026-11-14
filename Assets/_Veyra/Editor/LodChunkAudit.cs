using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// BUG-037 / BUG-038 (CLAUDE.md quality rules: every mesh has LODs, LOD0 / LOD1 ≈ 40 % / LOD2 ≈ 10 %, small props &lt; 2 m
    /// LOD0 + LOD1; no single mesh > 250 m or > 300k triangles). WorldBuildBatch step `lodaudit`, read-only, on the authoring
    /// scene's World root: groups every MeshRenderer by its top-level World group and reports
    /// <list type="bullet">
    /// <item>oversize: renderer bounds wider than 250 m in x or z, or a mesh over 300k triangles;</item>
    /// <item>no LOD: a renderer that is in no LODGroup;</item>
    /// <item>short chain: an LODGroup with fewer levels than the rule asks (3, or 2 for objects under 2 m), or whose LOD1 /
    /// LOD2 triangle ratio is far from 40 % / 10 % (over 70 % / 30 %).</item>
    /// </list>
    /// Writes Logs/LOD/lod_audit.txt (summary per group) and lod_audit.csv (one row per offender, worst first).
    /// </summary>
    public static class LodChunkAudit
    {
        public static string Run()
        {
            var world = EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == "World");
            if (world == null) return "FAIL: no World root\n";
            var inGroup = new HashSet<Renderer>();
            var groups = world.GetComponentsInChildren<LODGroup>(true);
            var csv = new StringBuilder("issue,group,path,detail\n");
            var per = new Dictionary<string, int[]>();   // renderers, oversize, noLod, shortChain
            string Top(Transform t) { while (t.parent != null && t.parent != world.transform) t = t.parent; return t.name; }
            string Path(Transform t) { var s = t.name; for (var p = t.parent; p != null && p != world.transform; p = p.parent) s = p.name + "/" + s; return s; }
            int[] P(string g) { if (!per.TryGetValue(g, out var a)) per[g] = a = new int[4]; return a; }
            int Tris(Renderer r) { var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) return 0; long n = 0; var m = mf.sharedMesh; for (int i = 0; i < m.subMeshCount; i++) n += m.GetIndexCount(i) / 3; return (int)n; }
            foreach (var g in groups)
            {
                var lods = g.GetLODs();
                foreach (var l in lods) foreach (var r in l.renderers) if (r != null) inGroup.Add(r);
                if (lods.Length == 0 || lods[0].renderers.Length == 0) continue;
                var b = new Bounds(); bool first = true;
                foreach (var r in lods[0].renderers) if (r != null) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
                bool small = b.size.magnitude < 2f;
                int need = small ? 2 : 3;
                int t0 = lods[0].renderers.Where(r => r != null).Sum(Tris);
                string grp = Top(g.transform);
                if (lods.Length < need)
                {
                    P(grp)[3]++;
                    csv.Append($"short_chain,{grp},{Path(g.transform)},{lods.Length} level(s) (needs {need}); LOD0 {t0} tris\n");
                }
                else if (t0 > 500)
                {
                    int t1 = lods[1].renderers.Where(r => r != null).Sum(Tris);
                    int t2 = lods.Length > 2 ? lods[2].renderers.Where(r => r != null).Sum(Tris) : 0;
                    if (t1 > t0 * 0.7f || (lods.Length > 2 && t2 > t0 * 0.3f))
                    {
                        P(grp)[3]++;
                        csv.Append($"weak_ratio,{grp},{Path(g.transform)},LOD0 {t0} / LOD1 {t1} / LOD2 {t2}\n");
                    }
                }
            }
            foreach (var r in world.GetComponentsInChildren<MeshRenderer>(true))
            {
                string grp = Top(r.transform);
                P(grp)[0]++;
                var bs = r.bounds.size; int tris = Tris(r);
                if (bs.x > 250f || bs.z > 250f || tris > 300000)
                {
                    P(grp)[1]++;
                    csv.Append($"oversize,{grp},{Path(r.transform)},{bs.x:0} x {bs.z:0} m, {tris} tris\n");
                }
                if (!inGroup.Contains(r)) { P(grp)[2]++; if (P(grp)[2] <= 30) csv.Append($"no_lod,{grp},{Path(r.transform)},{tris} tris\n"); }
            }
            var sb = new StringBuilder("LOD / chunk audit (BUG-037 / BUG-038)\ngroup: renderers | oversize (>250 m or >300k tris) | not in an LODGroup | short or weak LOD chain\n");
            foreach (var kv in per.OrderByDescending(k => k.Value[1] + k.Value[2] + k.Value[3]))
                sb.AppendLine($"  {kv.Key}: {kv.Value[0]} | {kv.Value[1]} | {kv.Value[2]} | {kv.Value[3]}");
            sb.AppendLine($"total: {per.Values.Sum(a => a[0])} renderers, {per.Values.Sum(a => a[1])} oversize, {per.Values.Sum(a => a[2])} without LOD, {per.Values.Sum(a => a[3])} short/weak chains");
            Directory.CreateDirectory("Logs/LOD");
            File.WriteAllText("Logs/LOD/lod_audit.txt", sb.ToString());
            File.WriteAllText("Logs/LOD/lod_audit.csv", csv.ToString());
            return sb.ToString();
        }
    }
}
