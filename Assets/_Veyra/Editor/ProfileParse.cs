using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// BUG-170 (CPU main-thread spikes): WorldBuildBatch step `profparse` loads a player Profiler log
    /// (-veyraProf path.raw, recorded with -profiler-enable -profiler-log-file) and writes Logs/ITER/profparse.txt:
    /// the 25 worst main-thread frames with their top markers (two levels, merged by name, total ms), and the markers that
    /// carry the most time summed over all frames above 50 ms.
    /// </summary>
    public static class ProfileParse
    {
        public static string Run(string path)
        {
            if (!File.Exists(path)) return $"profparse: no file {path}\n";
            ProfilerDriver.ClearAllFrames();
            if (!ProfilerDriver.LoadProfile(path, false)) return $"profparse: could not load {path}\n";
            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
            var frames = new List<(int idx, float ms)>();
            for (int f = first; f <= last; f++)
                using (var v = ProfilerDriver.GetRawFrameDataView(f, 0))
                    if (v != null && v.valid) frames.Add((f, v.frameTimeMs));
            var sb = new StringBuilder($"profile {path}: frames {first}-{last} ({frames.Count} valid)\n");
            if (frames.Count == 0) return sb.ToString();
            var sorted = frames.Select(x => x.ms).OrderBy(x => x).ToList();
            sb.AppendLine($"frame ms: p50 {sorted[sorted.Count / 2]:0.0}, p95 {sorted[(int)(sorted.Count * 0.95)]:0.0}, max {sorted[sorted.Count - 1]:0.0}, over 50 ms {sorted.Count(x => x > 50f)}");
            var spikeTotals = new Dictionary<string, float>();
            var children = new List<int>(); var grand = new List<int>();
            foreach (var (idx, ms) in frames.OrderByDescending(x => x.ms).Take(25))
            {
                using (var h = ProfilerDriver.GetHierarchyFrameDataView(idx, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
                {
                    sb.AppendLine($"-- frame {idx}: {ms:0.0} ms");
                    h.GetItemChildren(h.GetRootItemID(), children);
                    foreach (var c in children.OrderByDescending(c => h.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnTotalTime)).Take(4))
                    {
                        float t = h.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnTotalTime);
                        if (t < 2f) continue;
                        sb.AppendLine($"   {h.GetItemName(c)} {t:0.0}");
                        h.GetItemChildren(c, grand);
                        foreach (var g in grand.OrderByDescending(g => h.GetItemColumnDataAsFloat(g, HierarchyFrameDataView.columnTotalTime)).Take(5))
                        {
                            float tg = h.GetItemColumnDataAsFloat(g, HierarchyFrameDataView.columnTotalTime);
                            if (tg < 2f) continue;
                            sb.AppendLine($"      {h.GetItemName(g)} {tg:0.0}");
                            var deeper = new List<int>(); h.GetItemChildren(g, deeper);
                            foreach (var d in deeper.OrderByDescending(d => h.GetItemColumnDataAsFloat(d, HierarchyFrameDataView.columnTotalTime)).Take(3))
                            {
                                float td = h.GetItemColumnDataAsFloat(d, HierarchyFrameDataView.columnTotalTime);
                                if (td >= 5f) sb.AppendLine($"         {h.GetItemName(d)} {td:0.0}");
                            }
                        }
                    }
                }
            }
            // markers summed over every frame above 50 ms (self time, all levels)
            foreach (var (idx, ms) in frames.Where(x => x.ms > 50f))
                using (var h = ProfilerDriver.GetHierarchyFrameDataView(idx, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnSelfTime, false))
                {
                    var stack = new Stack<int>(); stack.Push(h.GetRootItemID());
                    var kids = new List<int>();
                    while (stack.Count > 0)
                    {
                        int id = stack.Pop();
                        h.GetItemChildren(id, kids);
                        foreach (var k in kids) stack.Push(k);
                        if (id == h.GetRootItemID()) continue;
                        float self = h.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime);
                        if (self <= 0.5f) continue;
                        string n = h.GetItemName(id);
                        spikeTotals[n] = spikeTotals.TryGetValue(n, out var cur) ? cur + self : self;
                    }
                }
            sb.AppendLine("== self time summed over frames > 50 ms (top 30)");
            foreach (var kv in spikeTotals.OrderByDescending(kv => kv.Value).Take(30)) sb.AppendLine($"   {kv.Value,9:0.0} ms  {kv.Key}");
            // optional: -veyraProfFrames a,b,c dumps the subtree of every marker whose name contains "Probe" or "Reflection"
            // (all depths, >= 0.5 ms) for those frames (Lighting D1: which probe / camera fills the 100+ ms probe ticks)
            var argv = System.Environment.GetCommandLineArgs();
            int fi = System.Array.IndexOf(argv, "-veyraProfFrames");
            if (fi >= 0 && fi + 1 < argv.Length)
                foreach (var tok in argv[fi + 1].Split(','))
                {
                    if (!int.TryParse(tok, out int fidx) || fidx < first || fidx > last) continue;
                    using (var h = ProfilerDriver.GetHierarchyFrameDataView(fidx, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
                    {
                        sb.AppendLine($"== probe subtree, frame {fidx}");
                        void Dump(int id, int depth, bool inside)
                        {
                            var kids2 = new List<int>(); h.GetItemChildren(id, kids2);
                            foreach (var k in kids2.OrderByDescending(k => h.GetItemColumnDataAsFloat(k, HierarchyFrameDataView.columnTotalTime)))
                            {
                                float t = h.GetItemColumnDataAsFloat(k, HierarchyFrameDataView.columnTotalTime);
                                string n = h.GetItemName(k);
                                bool now = inside || n.Contains("Probe") || n.Contains("Reflection");
                                if (now && t >= 0.5f) sb.AppendLine($"{new string(' ', 2 + depth * 2)}{n} {t:0.0}");
                                if (depth < 14 && t >= 0.5f) Dump(k, depth + 1, now);
                            }
                        }
                        Dump(h.GetRootItemID(), 0, false);
                    }
                }
            Directory.CreateDirectory("Logs/ITER");
            File.WriteAllText("Logs/ITER/profparse.txt", sb.ToString());
            return $"profparse: {frames.Count} frames -> Logs/ITER/profparse.txt\n";
        }
    }
}
