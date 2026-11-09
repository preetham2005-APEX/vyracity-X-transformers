using System;
using System.Collections.Generic;

namespace VeyraCity.Core
{
    /// <summary>Accumulates frame times (milliseconds) and computes summary statistics. Pure C#, unit-testable.</summary>
    public sealed class FrameStats
    {
        public const float HitchThresholdMs = 50f;
        readonly List<float> _frameMs = new List<float>(8192);

        public int Count => _frameMs.Count;

        public void Add(float frameMs)
        {
            if (frameMs > 0f && !float.IsNaN(frameMs) && !float.IsInfinity(frameMs)) _frameMs.Add(frameMs);
        }

        public Summary Summarize()
        {
            var s = new Summary { frames = _frameMs.Count };
            if (_frameMs.Count == 0) return s;
            var sorted = new List<float>(_frameMs);
            sorted.Sort();
            double total = 0;
            foreach (var f in sorted) total += f;
            s.avgMs = (float)(total / sorted.Count);
            s.avgFps = s.avgMs > 0f ? 1000f / s.avgMs : 0f;
            s.p50Ms = Percentile(sorted, 0.50);
            s.p95Ms = Percentile(sorted, 0.95);
            s.p99Ms = Percentile(sorted, 0.99);
            s.maxMs = sorted[sorted.Count - 1];
            s.onePercentLowFps = s.p99Ms > 0f ? 1000f / s.p99Ms : 0f;
            foreach (var f in sorted) if (f > HitchThresholdMs) s.hitchesOver50Ms++;
            s.seconds = (float)(total / 1000.0);
            return s;
        }

        /// <summary>Nearest-rank percentile on an ascending-sorted list. Uses double precision with a small
        /// tolerance so that e.g. 0.99 x 100 selects rank 99, not 100 (float rounding caused an off-by-one).</summary>
        public static float Percentile(List<float> sortedAscending, double p)
        {
            if (sortedAscending == null || sortedAscending.Count == 0) return 0f;
            int rank = (int)Math.Ceiling(p * sortedAscending.Count - 1e-9) - 1;
            rank = Math.Max(0, Math.Min(sortedAscending.Count - 1, rank));
            return sortedAscending[rank];
        }

        [Serializable]
        public sealed class Summary
        {
            public int frames;
            public float seconds;
            public float avgMs, avgFps, p50Ms, p95Ms, p99Ms, maxMs, onePercentLowFps;
            public int hitchesOver50Ms;
        }
    }
}
