using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Batch entry for the World track: runs the named world builders in order on the authoring scene and saves it once.
    /// <code>Unity.exe -batchmode -quit -projectPath E:\VeyraCity -executeMethod VeyraCity.EditorTools.WorldBuildBatch.Run -veyraSteps roads,grass,terrainmaps</code>
    /// Steps: roads (BUG-111 road render meshes), grass (grass masks), terrainmaps (terrain layer PBR maps), trees (authored trees).
    /// Report: Logs/B1/world_build.txt. Exit 0 on success, 1 on any exception.
    /// </summary>
    public static class WorldBuildBatch
    {
        public static void Run()
        {
            int code = 0;
            var log = new StringBuilder($"World build batch {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            try
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.IndexOf(args, "-veyraSteps");
                string steps = i >= 0 && i + 1 < args.Length ? args[i + 1] : "roads,grass,terrainmaps";
                var scene = EditorSceneManager.OpenScene(CellDistributor.AuthoringScene, OpenSceneMode.Single);
                foreach (var raw in steps.Split(','))
                {
                    string s = raw.Trim().ToLowerInvariant();
                    var t0 = DateTime.Now;
                    switch (s)
                    {
                        case "roads": log.Append(RoadRenderBuilder.Build()); break;
                        case "grass": log.Append(GrassMasks.Apply()); break;
                        case "terrainmaps": log.Append(TerrainLayerMaps.Run()); break;
                        case "trees": log.Append(TreeSwap.Run()); break;
                        case "seawalls": log.Append(SeawallBuilder.Build()); break;
                        case "catalog": log.Append(KitbashCatalogBuilder.Build()); break;
                        case "assemble": log.Append(BuildingAssembler.Run()); break;
                        default: log.AppendLine($"unknown step '{s}'"); code = 1; break;
                    }
                    log.AppendLine($"[{s}] {(DateTime.Now - t0).TotalSeconds:0} s");
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }
            catch (Exception e) { Debug.LogException(e); log.AppendLine("EXCEPTION " + e); code = 1; }
            Directory.CreateDirectory("Logs/B1");
            File.WriteAllText("Logs/B1/world_build.txt", log.ToString());
            Debug.Log(log.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
