using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace VeyraCity.Core
{
    /// <summary>
    /// Lightweight per-system frame-cost recorder (visual iteration 9, hitch hunting): systems wrap their per-frame work in
    /// <see cref="Start"/> / <see cref="Stop"/>; per frame the times are summed per system, and the recorder keeps each
    /// system's worst frame, how many frames it spent over 8 ms, and the worst whole frames with their top contributors and
    /// whether a garbage collection ran in them. Allocation-free on the hot path after warm-up. Always on (the cost is a
    /// Stopwatch read per call); <see cref="Report"/> formats the summary for the log.
    /// </summary>
    public static class FrameCost
    {
        sealed class Slot { public string Name; public double FrameMs, MaxMs, TotalMs; public int Over8, Frames; }

        struct Worst { public float FrameMs; public string Top; public bool Gc; public float Cov; }

        // How much of each frame the instrumented systems actually account for. Without this the worst-frame lines
        // name a system that may be a small slice of the frame (a 103 ms cruise frame whose top system is 21 ms), and
        // reading that as "the cause" sends the next card after the wrong 20 %.
        static double covTotalMs, frameTotalMs;
        static int covFrames;

        static readonly Dictionary<string, Slot> slots = new Dictionary<string, Slot>();
        static readonly List<Slot> order = new List<Slot>();
        static readonly List<Worst> worst = new List<Worst>();
        static int frame = -1, gcCount;
        static readonly double TickMs = 1000.0 / Stopwatch.Frequency;

        public static long Start() => Stopwatch.GetTimestamp();

        public static void Stop(string name, long start)
        {
            long now = Stopwatch.GetTimestamp();
            Roll();
            if (!slots.TryGetValue(name, out var s)) { s = new Slot { Name = name }; slots[name] = s; order.Add(s); }
            s.FrameMs += (now - start) * TickMs;
        }

        /// <summary>Closes the previous frame on the first call of a new one.</summary>
        static void Roll()
        {
            int f = Time.frameCount;
            if (f == frame) return;
            if (frame >= 0)
            {
                // Time.unscaledDeltaTime now is the length of the frame that just ended (approximately)
                float frameMs = Time.unscaledDeltaTime * 1000f;
                int gc = System.GC.CollectionCount(0);
                bool hadGc = gc != gcCount;
                gcCount = gc;
                Slot top = null;
                double covered = 0.0;
                foreach (var s in order)
                {
                    if (s.FrameMs <= 0.0) continue;
                    covered += s.FrameMs;
                    s.TotalMs += s.FrameMs;
                    s.Frames++;
                    if (s.FrameMs > s.MaxMs) s.MaxMs = s.FrameMs;
                    if (s.FrameMs > 8.0) s.Over8++;
                    if (top == null || s.FrameMs > top.FrameMs) top = s;
                }
                covTotalMs += covered;
                frameTotalMs += frameMs;
                covFrames++;
                if (frameMs > 33f && (worst.Count < 12 || frameMs > worst[worst.Count - 1].FrameMs))
                {
                    string desc = top != null ? $"{top.Name} {top.FrameMs:0.0}" : "none";
                    worst.Add(new Worst { FrameMs = frameMs, Top = desc, Gc = hadGc, Cov = (float)covered });
                    worst.Sort((a, b) => b.FrameMs.CompareTo(a.FrameMs));
                    if (worst.Count > 12) worst.RemoveAt(worst.Count - 1);
                }
                foreach (var s in order) s.FrameMs = 0.0;
            }
            else gcCount = System.GC.CollectionCount(0);
            frame = f;
        }

        static readonly Dictionary<string, int> counts = new Dictionary<string, int>();

        /// <summary>Counts an event (e.g. "CarProbe.cycles"); the totals print at the end of <see cref="Report"/>.</summary>
        public static void Count(string name)
        {
            counts.TryGetValue(name, out int n);
            counts[name] = n + 1;
        }

        public static void Reset()
        {
            counts.Clear();
            slots.Clear();
            order.Clear();
            worst.Clear();
            covTotalMs = frameTotalMs = 0.0;
            covFrames = 0;
            frame = -1;
        }

        public static string Report()
        {
            Roll();
            var sb = new StringBuilder("[IterCost]");
            var sorted = new List<Slot>(order);
            sorted.Sort((a, b) => b.MaxMs.CompareTo(a.MaxMs));
            foreach (var s in sorted)
                sb.Append($" {s.Name}:max{s.MaxMs:0.0}/avg{(s.Frames > 0 ? s.TotalMs / s.Frames : 0):0.00}/over8x{s.Over8}");
            if (covFrames > 0)
                sb.Append($" | covered {covTotalMs / covFrames:0.00} of {frameTotalMs / covFrames:0.00} ms/frame ({100.0 * covTotalMs / System.Math.Max(0.001, frameTotalMs):0}%)");
            // cov = how many ms of that frame the instrumented systems account for; frame - cov is unattributed
            // (rendering, physics steps, GC, engine work). A big gap means the named system is not the cause.
            sb.Append(" | worst frames:");
            foreach (var w in worst) sb.Append($" {w.FrameMs:0}ms[{w.Top}{(w.Gc ? ",GC" : "")},cov{w.Cov:0}]");
            if (counts.Count > 0)
            {
                sb.Append(" | counts:");
                foreach (var kv in counts) sb.Append($" {kv.Key}={kv.Value}");
            }
            return sb.ToString();
        }
    }
}
