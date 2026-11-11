using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Hitch attribution from a player profile: record with
    /// <c>VeyraCity.exe -profiler-enable -profiler-log-file Logs/PROF/run.raw ...</c> (development build), then
    /// <c>Unity -batchmode -executeMethod VeyraCity.EditorTools.ProfileDump.Run -veyraProfile Logs/PROF/run.raw</c>
    /// writes Logs/PROF/&lt;name&gt;.txt: the 15 slowest frames with their top main-thread markers (self and total time),
    /// and the markers with the highest total time over the whole capture.
    /// </summary>
    public static class ProfileDump
    {
        public static void Run()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-veyraProfile");
            string path = i >= 0 && i + 1 < args.Length ? args[i + 1] : "Logs/PROF/run.raw";
            string report = Dump(path);
            File.WriteAllText(Path.ChangeExtension(path, ".txt"), report);
            Debug.Log(report);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        public static string Dump(string path)
        {
            var sb = new StringBuilder($"Profile {path}\n");
            if (!File.Exists(path)) return sb.Append("missing").ToString();
            ProfilerDriver.ClearAllFrames();
            if (!ProfilerDriver.LoadProfile(Path.GetFullPath(path), false)) return sb.Append("could not load").ToString();
            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
            sb.AppendLine($"frames {first}..{last}");
            var frames = new List<(int f, float ms)>();
            var totals = new Dictionary<string, double>();
            for (int f = first; f <= last; f++)
            {
                using (var v = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
                {
                    if (v == null || !v.valid) continue;
                    frames.Add((f, v.frameTimeMs));
                    var kids = new List<int>();
                    v.GetItemChildren(v.GetRootItemID(), kids);
                    foreach (var k in kids) Walk(v, k, 0, totals);
                }
            }
            sb.AppendLine($"frames read {frames.Count}, avg {frames.Average(x => x.ms):0.0} ms, frames over 50 ms {frames.Count(x => x.ms > 50f)}");
            sb.AppendLine("\nTop markers by self time over the capture (ms):");
            foreach (var kv in totals.OrderByDescending(k => k.Value).Take(25)) sb.AppendLine($"  {kv.Value,10:0} {kv.Key}");
            sb.AppendLine("\nSlowest frames, top markers by self time:");
            foreach (var (f, ms) in frames.OrderByDescending(x => x.ms).Take(15))
            {
                var t = new Dictionary<string, double>();
                using (var v = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
                {
                    var kids = new List<int>();
                    v.GetItemChildren(v.GetRootItemID(), kids);
                    foreach (var k in kids) Walk(v, k, 0, t);
                }
                sb.AppendLine($"  frame {f}: {ms:0.0} ms | " + string.Join(", ", t.OrderByDescending(k => k.Value).Take(6).Select(k => $"{k.Key} {k.Value:0.0}")));
            }
            return sb.ToString();
        }

        /// <summary>Accumulates self time per marker name.</summary>
        static void Walk(HierarchyFrameDataView v, int id, int depth, Dictionary<string, double> self)
        {
            string name = v.GetItemName(id);
            double s = v.GetItemColumnDataAsDouble(id, HierarchyFrameDataView.columnSelfTime);
            self[name] = (self.TryGetValue(name, out var x) ? x : 0.0) + s;
            if (depth > 40) return;
            var kids = new List<int>();
            v.GetItemChildren(id, kids);
            foreach (var k in kids) Walk(v, k, depth + 1, self);
        }
    }
}
