using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// BUG-039 (card F1): WorldEnvironment is [ExecuteAlways] and animates shared materials (emission, wet roads, sky, water
    /// scroll) for the time-of-day preview, which used to change .mat files on disk. Before any material asset is saved, this
    /// writes the reference state (noon, clear, dry) into those materials, so the files never hold a preview; right after the
    /// save the current time and weather are applied again.
    /// </summary>
    public sealed class WorldEnvironmentSaveGuard : AssetModificationProcessor
    {
        static bool reapplyQueued;

        static string[] OnWillSaveAssets(string[] paths)
        {
            if (paths == null || !paths.Any(p => p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))) return paths;
            var envs = UnityEngine.Object.FindObjectsByType<WorldEnvironment>(FindObjectsInactive.Include);
            if (envs.Length == 0) return paths;
            foreach (var env in envs) env.WriteReferenceMaterialState();
            if (!reapplyQueued)
            {
                reapplyQueued = true;
                EditorApplication.delayCall += () =>
                {
                    reapplyQueued = false;
                    foreach (var env in UnityEngine.Object.FindObjectsByType<WorldEnvironment>(FindObjectsInactive.Include))
                        if (env != null && env.isActiveAndEnabled) env.ReapplyCurrentState();
                };
            }
            return paths;
        }
    }
}
