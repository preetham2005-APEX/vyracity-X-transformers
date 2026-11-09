using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using VeyraCity.EditorTools;

/// <summary>
/// Card V0 build entry points (global namespace so the playbook's batch command works as written):
/// <code>"E:\Unity\Editors\6000.6.8f1\Editor\Unity.exe" -batchmode -quit -projectPath E:\VeyraCity -executeMethod VeyraBuild.BuildDevMono -logFile E:\VeyraCity\Logs\unity_build.log</code>
/// (see Tools/build_dev.cmd and Tools/build_release.cmd). Both write Logs/build_&lt;timestamp&gt;.log through <see cref="BuildScript"/>.
/// In batch mode the Editor exits with 0 on success and 1 on failure; from the menu it stays open.
/// </summary>
public static class VeyraBuild
{
    public const string DevMonoDir = "Builds/DevMono";
    public const string ReleaseDir = "Builds/Release";

    /// <summary>Windows x64, Mono, Development Build on, Script Debugging off → Builds/DevMono/VeyraCity.exe.</summary>
    [MenuItem("Veyra/Build/Dev Mono (Builds\\DevMono)")]
    public static void BuildDevMono() => Finish(BuildScript.BuildPlayer(il2cpp: false, development: true, outDir: DevMonoDir));

    /// <summary>Windows x64, IL2CPP, non-development → Builds/Release/VeyraCity.exe.</summary>
    [MenuItem("Veyra/Build/Release IL2CPP (Builds\\Release)")]
    public static void BuildReleaseIL2CPP() => Finish(BuildScript.BuildPlayer(il2cpp: true, development: false, outDir: ReleaseDir));

    static void Finish(BuildResult result)
    {
        bool ok = result == BuildResult.Succeeded;
        if (ok) Debug.Log($"[VeyraBuild] {result}");
        else Debug.LogError($"[VeyraBuild] {result}");
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }
}
