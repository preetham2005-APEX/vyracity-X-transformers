using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace VeyraCity.Core
{
    /// <summary>
    /// Created automatically before the first scene runs. Logs the device report, records frame statistics,
    /// shows the developer overlay and drives automated verification runs (see <see cref="LaunchOptions"/>).
    /// </summary>
    public sealed class VeyraBootstrap : MonoBehaviour
    {
        public static VeyraBootstrap Instance { get; private set; }
        public BuildInfo Build { get; private set; }
        public LaunchOptions Options { get; private set; }

        readonly FrameStats _stats = new FrameStats();
        float _startTime;
        bool _statsWritten;

        // Developer overlay (IMGUI; the shipping HUD will use UI Toolkit).
        bool _overlay;
        float _smoothedMs;
        GUIStyle _style;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateOnStartup()
        {
            if (Instance != null) return;
            var go = new GameObject("[VeyraBootstrap]");
            DontDestroyOnLoad(go);
            go.AddComponent<VeyraBootstrap>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Options = LaunchOptions.Parse(Environment.GetCommandLineArgs());
            Build = BuildInfo.LoadOrDefault();
            _overlay = Options.ShowOverlay || Debug.isDebugBuild;
            _startTime = Time.realtimeSinceStartup;
            Application.logMessageReceived += OnLog;
            Debug.Log(DeviceReport.Build(Build));
            if (Options.IsAutomatedRun) StartCoroutine(AutomatedRun());
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            if (Instance == this) Instance = null;
        }

        void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Exception) _exceptions++;
        }
        int _exceptions;

        void Update()
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            _smoothedMs = _smoothedMs <= 0f ? ms : Mathf.Lerp(_smoothedMs, ms, 0.05f);
            if (Time.realtimeSinceStartup - _startTime >= Options.WarmupSeconds) _stats.Add(ms);
        }

        /// <summary>Toggle hook for the Input module (F3 binding lives in the Input System action map, not in Core).</summary>
        public void ToggleOverlay() => _overlay = !_overlay;

        IEnumerator AutomatedRun()
        {
            if (!string.IsNullOrEmpty(Options.ScreenshotPath))
            {
                yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, Options.ScreenshotAtSeconds));
                string dir = Path.GetDirectoryName(Options.ScreenshotPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                ScreenCapture.CaptureScreenshot(Options.ScreenshotPath);
                Debug.Log($"[Veyra] Screenshot requested: {Options.ScreenshotPath}");
            }
            float remaining = Options.AutoQuitSeconds - (Time.realtimeSinceStartup - _startTime);
            if (remaining > 0f) yield return new WaitForSecondsRealtime(remaining);
            WriteStats();
            Debug.Log("[Veyra] Automated run complete; quitting.");
            Application.Quit(_exceptions == 0 ? 0 : 3);
        }

        void OnApplicationQuit() => WriteStats();

        void WriteStats()
        {
            if (_statsWritten) return;
            _statsWritten = true;
            var summary = _stats.Summarize();
            var report = new StatsReport
            {
                build = Build,
                gpu = SystemInfo.graphicsDeviceName,
                gpuVendor = SystemInfo.graphicsDeviceVendor,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                resolution = $"{Screen.width}x{Screen.height}",
                quality = QualitySettings.names[QualitySettings.GetQualityLevel()],
                vSyncCount = QualitySettings.vSyncCount,
                exceptions = _exceptions,
                frames = summary
            };
            string json = JsonUtility.ToJson(report, true);
            Debug.Log($"[Veyra] Frame stats: {summary.frames} frames over {summary.seconds:0.0}s | avg {summary.avgFps:0.0} FPS ({summary.avgMs:0.00} ms) | p95 {summary.p95Ms:0.00} ms | p99 {summary.p99Ms:0.00} ms | max {summary.maxMs:0.0} ms | hitches>50ms {summary.hitchesOver50Ms} | exceptions {_exceptions}");
            if (string.IsNullOrEmpty(Options.StatsPath)) return;
            try
            {
                string dir = Path.GetDirectoryName(Options.StatsPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(Options.StatsPath, json);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"[Veyra] Could not write stats to {Options.StatsPath}: {e.Message}");
            }
        }

        void OnGUI()
        {
            if (!_overlay) return;
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true };
            float fps = _smoothedMs > 0f ? 1000f / _smoothedMs : 0f;
            string text = $"<b>VEYRA CITY</b>  {Build.version}\n{fps:0} FPS  ({_smoothedMs:0.0} ms)\n{SystemInfo.graphicsDeviceName}\n{Screen.width}x{Screen.height}  {QualitySettings.names[QualitySettings.GetQualityLevel()]}";
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(8, 8, 380, 96), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(16, 12, 370, 92), text, _style);
        }

        [Serializable]
        sealed class StatsReport
        {
            public BuildInfo build;
            public string gpu, gpuVendor, graphicsApi, resolution, quality;
            public int vSyncCount, exceptions;
            public FrameStats.Summary frames;
        }
    }
}
