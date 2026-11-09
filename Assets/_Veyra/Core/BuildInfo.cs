using System;
using System.IO;
using UnityEngine;

namespace VeyraCity.Core
{
    /// <summary>Build metadata written by the editor BuildScript to StreamingAssets/build_info.json.</summary>
    [Serializable]
    public sealed class BuildInfo
    {
        public const string FileName = "build_info.json";

        public string version = "0.0.0-editor";
        public string gitCommit = "unknown";
        public string unityVersion = "";
        public string buildTimeUtc = "";
        public string scriptingBackend = "";
        public bool development;

        public static BuildInfo FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonUtility.FromJson<BuildInfo>(json); }
            catch (ArgumentException) { return null; }
        }

        public string ToJson() => JsonUtility.ToJson(this, true);

        public static BuildInfo LoadOrDefault()
        {
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, FileName);
                if (File.Exists(path))
                {
                    var info = FromJson(File.ReadAllText(path));
                    if (info != null) return info;
                }
            }
            catch (IOException e) { Debug.LogWarning($"[Veyra] Could not read {FileName}: {e.Message}"); }
            return new BuildInfo { unityVersion = Application.unityVersion };
        }

        public override string ToString() =>
            $"{version} ({gitCommit}, {scriptingBackend}{(development ? ", development" : "")}, built {buildTimeUtc}, Unity {unityVersion})";
    }
}
