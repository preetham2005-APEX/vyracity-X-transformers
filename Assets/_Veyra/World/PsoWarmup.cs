using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace VeyraCity.World
{
    /// <summary>
    /// Shader / pipeline-state warm-up against first-use hitches (the vehicle session measured 82–405 ms frames while cruising
    /// that none of the per-frame systems explain: D3D12 compiles a PSO the first time a material/mesh/render-state combination
    /// is drawn). Uses Unity 6's GraphicsStateCollection:
    /// <list type="bullet">
    /// <item>Trace: <c>VeyraCity.exe -veyraTracePSO &lt;file.graphicsstate&gt;</c> records every graphics state the player uses
    /// (run it with the autotest routes and the review captures) and saves the file on quit.</item>
    /// <item>Warm-up: at start the player loads <c>Resources/PSO/VC_PSO_&lt;GraphicsDeviceType&gt;</c> (built from the traces by
    /// Veyra/Rendering/Build PSO Collection) and compiles every state once, before the first frame of driving.
    /// <c>-veyraNoPsoWarmup</c> skips it (A/B).</item>
    /// </list>
    /// </summary>
    public static class PsoWarmup
    {
        static GraphicsStateCollection trace;
        static string tracePath;

        /// <summary>Milliseconds the warm-up took in this run (-1 when it did not run); logged and reported by the autotest.</summary>
        public static double WarmupMs { get; private set; } = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.FindIndex(args, a => a.Equals("-veyraTracePSO", StringComparison.OrdinalIgnoreCase));
            if (i >= 0 && i + 1 < args.Length)
            {
                tracePath = args[i + 1];
                trace = new GraphicsStateCollection();
                trace.BeginTrace();
                Application.quitting += SaveTrace;
                Debug.Log($"[PSO] tracing graphics states for {SystemInfo.graphicsDeviceType} -> {tracePath}");
                return;
            }
            if (Array.Exists(args, a => a.Equals("-veyraNoPsoWarmup", StringComparison.OrdinalIgnoreCase)))
            {
                Debug.Log("[PSO] warm-up skipped (-veyraNoPsoWarmup)");
                return;
            }
            var collection = Resources.Load<GraphicsStateCollection>("PSO/VC_PSO_" + SystemInfo.graphicsDeviceType);
            if (collection == null)
            {
                Debug.Log($"[PSO] no warm-up collection for {SystemInfo.graphicsDeviceType}");
                return;
            }
            var t = Stopwatch.StartNew();
            collection.WarmUp().Complete();
            WarmupMs = t.Elapsed.TotalMilliseconds;
            Debug.Log($"[PSO] warmed up {collection.completedWarmupCount}/{collection.totalGraphicsStateCount} graphics states " +
                      $"({collection.variantCount} variants) for {SystemInfo.graphicsDeviceType} in {WarmupMs:F0} ms");
        }

        static void SaveTrace()
        {
            if (trace == null) return;
            trace.EndTrace();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(tracePath)));
            bool ok = trace.SaveToFile(tracePath);
            Debug.Log($"[PSO] trace {(ok ? "saved" : "NOT saved")}: {trace.totalGraphicsStateCount} graphics states, {trace.variantCount} variants -> {tracePath}");
            trace = null;
        }
    }
}
