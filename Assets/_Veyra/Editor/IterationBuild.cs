using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using VeyraCity.EditorTools;

/// <summary>
/// Dev Mono build for the visual iteration captures (Tools/visual_iteration.sh) into its own folder, Builds/IterMono, so
/// iteration builds never overwrite Builds/DevMono while another session's player runs from it.
/// <c>-executeMethod IterationBuild.BuildIterMono</c>
/// </summary>
public static class IterationBuild
{
    public const string IterMonoDir = "Builds/IterMono";

    [MenuItem("Veyra/Build/Iteration Dev Mono (Builds\\IterMono)")]
    public static void BuildIterMono()
    {
        var result = BuildScript.BuildPlayer(il2cpp: false, development: true, outDir: IterMonoDir);
        bool ok = result == BuildResult.Succeeded;
        if (ok) Debug.Log($"[IterationBuild] {result}");
        else Debug.LogError($"[IterationBuild] {result}");
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    /// <summary>
    /// Rebuilds the PSO warm-up collection from every trace in Logs/PSO (the F/L-card session's route traces plus the
    /// round-2 capture trace written by <c>VeyraCity.exe -veyraIterShots ... -veyraTracePSO Logs/PSO/trace_round2.graphicsstate</c>).
    /// <c>-executeMethod IterationBuild.RebuildPsoCollection</c>
    /// </summary>
    public static void RebuildPsoCollection()
    {
        Debug.Log("[IterationBuild] " + PsoCollectionBuilder.Build());
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
