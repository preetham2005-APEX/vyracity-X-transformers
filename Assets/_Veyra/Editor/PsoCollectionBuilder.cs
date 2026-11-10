using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine.Rendering;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// "Veyra/Rendering/Build PSO Collection": merges the player traces (Logs/PSO/*.graphicsstate, written by
    /// <c>VeyraCity.exe -veyraTracePSO &lt;file&gt;</c>, see World/PsoWarmup) per graphics API into
    /// Assets/_Veyra/Resources/PSO/VC_PSO_&lt;GraphicsDeviceType&gt;.graphicsstate, which the player warms up at start.
    /// Idempotent: the output is rebuilt from the traces each run.
    /// </summary>
    public static class PsoCollectionBuilder
    {
        public const string TraceDir = "Logs/PSO";
        public const string OutDir = "Assets/_Veyra/Resources/PSO";

        [MenuItem("Veyra/Rendering/Build PSO Collection")]
        static void Menu() => UnityEngine.Debug.Log(Build());

        public static string Build()
        {
            var log = new StringBuilder("PSO collection\n");
            if (!Directory.Exists(TraceDir)) return log.Append("no traces in " + TraceDir).ToString();
            var merged = new Dictionary<GraphicsDeviceType, GraphicsStateCollection>();
            foreach (var f in Directory.GetFiles(TraceDir, "*.graphicsstate"))
            {
                var t = new GraphicsStateCollection();
                if (!t.LoadFromFile(f)) { log.AppendLine("could not load " + f); continue; }
                if (!merged.TryGetValue(t.graphicsDeviceType, out var m))
                {
                    m = new GraphicsStateCollection { graphicsDeviceType = t.graphicsDeviceType, runtimePlatform = t.runtimePlatform, qualityLevelName = t.qualityLevelName };
                    merged[t.graphicsDeviceType] = m;
                }
                m.Append(t);
                log.AppendLine($"{Path.GetFileName(f)}: {t.graphicsDeviceType}, {t.variantCount} variants, {t.totalGraphicsStateCount} states");
            }
            Directory.CreateDirectory(OutDir);
            foreach (var kv in merged)
            {
                string path = $"{OutDir}/VC_PSO_{kv.Key}.graphicsstate";
                kv.Value.SaveToFile(path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                log.AppendLine($"-> {path}: {kv.Value.variantCount} variants, {kv.Value.totalGraphicsStateCount} graphics states");
            }
            return log.ToString();
        }
    }
}
