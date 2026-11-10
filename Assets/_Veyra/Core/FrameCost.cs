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

        struct Worst { public float FrameMs; public string Top; public bool Gc; }

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
                foreach (var s in order)
                {
                    if (s.FrameMs <= 0.0) continue;
                    s.TotalMs += s.FrameMs;
                    s.Frames++;
                    if (s.FrameMs > s.MaxMs) s.MaxMs = s.FrameMs;
                    if (s.FrameMs > 8.0) s.Over8++;
                    if (top == null || s.FrameMs > top.FrameMs) top = s;
                }
                if (frameMs > 33f && (worst.Count < 12 || frameMs > worst[worst.Count - 1].FrameMs))
                {
                    string desc = top != null ? $"{top.Name} {top.FrameMs:0.0}" : "none";
                    worst.Add(new Worst { FrameMs = frameMs, Top = desc, Gc = hadGc });
                    worst.Sort((a, b) => b.FrameMs.CompareTo(a.FrameMs));
                    if (worst.Count > 12) worst.RemoveAt(worst.Count - 1);
                }
                foreach (var s in order) s.FrameMs = 0.0;
            }
            else gcCount = System.GC.CollectionCount(0);
            frame = f;
        }

        public static void Reset()
        {
            slots.Clear();
            order.Clear();
            worst.Clear();
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
            sb.Append(" | worst frames:");
            foreach (var w in worst) sb.Append($" {w.FrameMs:0}ms[{w.Top}{(w.Gc ? ",GC" : "")}]");
            return sb.ToString();
        }
    }
}
