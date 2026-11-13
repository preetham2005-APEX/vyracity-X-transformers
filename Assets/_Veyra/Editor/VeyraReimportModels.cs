using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Force-reimports every model under the Blender pipeline's roots (Art/Models, Art/World/Chunks).
    /// <para>
    /// Needed because a model's material assignment depends on two things outside the .fbx — Tools/Blender/material_map.json
    /// and the VeyraMaterialLibrary asset — and the custom dependency that should reimport on a map change does not always
    /// fire (it is registered from <c>EditorApplication.delayCall</c>, which is unreliable in batch mode). A model imported
    /// before the library existed keeps its source material names for ever and fails ChunkImportTests' library-material gate,
    /// with no indication that the cause is staleness rather than a bad mapping.
    /// </para>
    /// Run after editing material_map.json or rebuilding the library. Idempotent.
    /// </summary>
    public static class VeyraReimportModels
    {
        static readonly string[] Roots = { VeyraModelImportDefaults.ModelsRoot, VeyraModelImportDefaults.ChunksRoot };

        [MenuItem("Veyra/Models/Reimport Pipeline Models")]
        public static void ReimportAll()
        {
            var paths = new List<string>();
            foreach (string root in Roots)
            {
                string dir = root.TrimEnd('/');
                if (!AssetDatabase.IsValidFolder(dir)) continue;
                paths.AddRange(AssetDatabase.FindAssets("t:Model", new[] { dir })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => !string.IsNullOrEmpty(p)));
            }
            paths = paths.Distinct().ToList();
            if (paths.Count == 0) { Debug.Log("[Veyra] Reimport: no models under " + string.Join(", ", Roots)); return; }

            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < paths.Count; i++) AssetDatabase.ImportAsset(paths[i], ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.Refresh();
            Debug.Log($"[Veyra] Reimport: {paths.Count} models reimported under {string.Join(", ", Roots)}");
        }
    }
}
