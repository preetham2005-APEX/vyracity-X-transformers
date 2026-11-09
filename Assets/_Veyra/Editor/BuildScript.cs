using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using VeyraCity.Core;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Command-line Windows build.
    /// Unity.exe -batchmode -quit -projectPath E:\VeyraCity -executeMethod VeyraCity.EditorTools.BuildScript.BuildWindows
    ///   [-veyraBackend mono|il2cpp] [-veyraDevelopment] [-veyraOutput E:\VeyraCity\Builds\Windows_Mono] [-veyraVersion 0.1.0]
    /// Exit code 0 = build succeeded, 1 = failed.
    /// </summary>
    public static class BuildScript
    {
        public const string ProductName = "VEYRA CITY";
        public const string ExeName = "VeyraCity.exe";

        public static void BuildWindows()
        {
            int code;
            try { code = Build(Environment.GetCommandLineArgs()) ? 0 : 1; }
            catch (Exception e) { Debug.LogError($"[Veyra] Build threw: {e}"); code = 1; }
            EditorApplication.Exit(code);
        }

        [MenuItem("Veyra/Build/Windows (Mono, development)")]
        public static void BuildMonoDevMenu() => Build(new[] { "-veyraBackend", "mono", "-veyraDevelopment" });

        public static string[] GetScenes() =>
            EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path)).Select(s => s.path).ToArray();

        public static bool Build(string[] args)
        {
            string backend = Arg(args, "-veyraBackend") ?? "mono";
            bool il2cpp = backend.Equals("il2cpp", StringComparison.OrdinalIgnoreCase);
            bool development = args.Contains("-veyraDevelopment");
            string version = Arg(args, "-veyraVersion") ?? "0.1.0-foundation";
            string outDir = Arg(args, "-veyraOutput") ?? Path.GetFullPath($"Builds/Windows_{(il2cpp ? "IL2CPP" : "Mono")}{(development ? "_Dev" : "")}");
            return BuildPlayer(il2cpp, development, outDir, version) == BuildResult.Succeeded;
        }

        /// <summary>
        /// Builds the Windows x64 player (Script Debugging off) and writes <c>Logs/build_&lt;timestamp&gt;.log</c> with the
        /// BuildReport summary. The scripting backend is restored afterwards so ProjectSettings stay unchanged (BUG-014).
        /// </summary>
        public static BuildResult BuildPlayer(bool il2cpp, bool development, string outDir, string version = "0.1.0-foundation")
        {
            SceneTools.EnsureFoundationScene(force: false);
            string[] scenes = GetScenes();
            if (scenes.Length == 0) { Debug.LogError("[Veyra] No scenes in build settings."); return BuildResult.Failed; }

            PlayerSettings.productName = ProductName;
            PlayerSettings.bundleVersion = version;
            var previousBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
            try { return BuildWithBackend(il2cpp, development, version, Path.GetFullPath(outDir), scenes); }
            finally { PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, previousBackend); } // don't leave ProjectSettings changed
        }

        static BuildResult BuildWithBackend(bool il2cpp, bool development, string version, string outDir, string[] scenes)
        {

            var info = new BuildInfo
            {
                version = version,
                gitCommit = ReadGitCommit(),
                unityVersion = Application.unityVersion,
                buildTimeUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                scriptingBackend = il2cpp ? "IL2CPP" : "Mono",
                development = development
            };
            Directory.CreateDirectory(Application.streamingAssetsPath);
            File.WriteAllText(Path.Combine(Application.streamingAssetsPath, BuildInfo.FileName), info.ToJson());
            AssetDatabase.Refresh();

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(outDir, ExeName),
                target = BuildTarget.StandaloneWindows64,
                options = development ? BuildOptions.Development : BuildOptions.None
            };

            Debug.Log($"[Veyra] Building {info} -> {options.locationPathName} with scenes: {string.Join(", ", scenes)}");
            var started = DateTime.Now;
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            string summary = $"result={s.result} size={s.totalSize / (1024f * 1024f):0.0}MB time={s.totalTime.TotalSeconds:0}s errors={s.totalErrors} warnings={s.totalWarnings} output={s.outputPath}";
            Debug.Log($"[Veyra] BUILD {summary}");
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "build_report.txt"), $"{info}\n{summary}\n");
            WriteBuildLog(report, info, scenes, started);
            return s.result;
        }

        /// <summary>Logs/build_&lt;timestamp&gt;.log: result, size, duration, errors/warnings with their messages, scenes, version.</summary>
        static void WriteBuildLog(BuildReport report, BuildInfo info, string[] scenes, DateTime started)
        {
            var s = report.summary;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"VEYRA CITY build — {started:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"result: {s.result}");
            sb.AppendLine($"target: {s.platform} | backend: {info.scriptingBackend} | development: {info.development} | script debugging: off");
            sb.AppendLine($"version: {info.version} | git: {info.gitCommit} | unity: {info.unityVersion}");
            sb.AppendLine($"output: {s.outputPath}");
            sb.AppendLine($"size_mb: {s.totalSize / (1024.0 * 1024.0):0.0}");
            sb.AppendLine($"duration_s: {s.totalTime.TotalSeconds:0.0}");
            sb.AppendLine($"errors: {s.totalErrors} | warnings: {s.totalWarnings}");
            sb.AppendLine("shader variant count: not exposed by BuildReport in Unity 6000.6 (see the Editor log's shader compilation lines)");
            sb.AppendLine("scenes: " + string.Join(", ", scenes));
            int shown = 0;
            foreach (var step in report.steps)
                foreach (var m in step.messages)
                    if ((m.type == LogType.Error || m.type == LogType.Exception || m.type == LogType.Warning) && shown++ < 40)
                        sb.AppendLine($"[{m.type}] {step.name}: {m.content.Split('\n')[0]}");
            sb.AppendLine("steps:");
            foreach (var step in report.steps) sb.AppendLine($"  {step.duration.TotalSeconds,7:0.0}s  {step.name}");
            string dir = Path.GetFullPath("Logs");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"build_{started:yyyyMMdd_HHmmss}.log");
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[Veyra] Build log: {path}");
        }

        static string Arg(string[] args, string name)
        {
            int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : null;
        }

        /// <summary>Reads the current commit from .git without needing git on PATH.</summary>
        public static string ReadGitCommit()
        {
            try
            {
                string gitDir = Path.GetFullPath(".git");
                string headFile = Path.Combine(gitDir, "HEAD");
                if (!File.Exists(headFile)) return "no-git";
                string head = File.ReadAllText(headFile).Trim();
                if (!head.StartsWith("ref: ")) return head.Length >= 8 ? head.Substring(0, 8) : head;
                string refPath = Path.Combine(gitDir, head.Substring(5).Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(refPath)) return File.ReadAllText(refPath).Trim().Substring(0, 8);
                string packed = Path.Combine(gitDir, "packed-refs");
                if (File.Exists(packed))
                {
                    string refName = head.Substring(5);
                    var line = File.ReadAllLines(packed).FirstOrDefault(l => l.EndsWith(" " + refName));
                    if (line != null) return line.Substring(0, 8);
                }
                return "no-commits";
            }
            catch (IOException) { return "unknown"; }
        }
    }
}
