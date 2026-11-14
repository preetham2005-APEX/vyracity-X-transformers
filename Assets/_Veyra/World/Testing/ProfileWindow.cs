using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Profiling;

namespace VeyraCity.Testing
{
    /// <summary>
    /// BUG-170 (a full R_BRIDGES profiler log is 8.8 GB, too large for the Editor to load): development players only,
    /// <c>-veyraProfWindow start_s,frames[;start_s,frames...] -veyraProfFile path</c> turns the binary profiler log on at each
    /// start time (seconds since start-up) for that many frames, writing path_0.raw, path_1.raw ... Each slice stays a few
    /// hundred MB and parses with the WorldBuildBatch step `profparse`. Inert without the argument.
    /// </summary>
    public sealed class ProfileWindow : MonoBehaviour
    {
        (float start, int frames)[] windows;
        string basePath;
        int current = -1, framesLeft, index;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Debug.isDebugBuild) return;
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, "-veyraProfWindow"), f = Array.IndexOf(a, "-veyraProfFile");
            if (i < 0 || i + 1 >= a.Length) return;
            var parts = a[i + 1].Split(';');
            var list = new (float, int)[parts.Length];
            for (int k = 0; k < parts.Length; k++)
            {
                var p = parts[k].Split(',');
                list[k] = (float.Parse(p[0], CultureInfo.InvariantCulture), int.Parse(p[1], CultureInfo.InvariantCulture));
            }
            var go = new GameObject("ProfileWindow");
            DontDestroyOnLoad(go);
            var w = go.AddComponent<ProfileWindow>();
            w.windows = list;
            w.basePath = f >= 0 && f + 1 < a.Length ? a[f + 1] : "prof";
            Profiler.maxUsedMemory = 256 * 1024 * 1024;
        }

        void Update()
        {
            if (current >= 0)
            {
                if (--framesLeft > 0) return;
                Profiler.enabled = false;
                Profiler.enableBinaryLog = false;
                Profiler.logFile = "";
                Debug.Log($"[ProfileWindow] slice {current} closed at {Time.realtimeSinceStartup:0.0} s");
                current = -1;
                return;
            }
            if (index >= windows.Length || Time.realtimeSinceStartup < windows[index].start) return;
            current = index;
            framesLeft = windows[index].frames;
            Profiler.logFile = $"{basePath}_{index}";   // Unity appends .raw
            Profiler.enableBinaryLog = true;
            Profiler.enabled = true;
            Debug.Log($"[ProfileWindow] slice {index} opened at {Time.realtimeSinceStartup:0.0} s for {framesLeft} frames -> {Profiler.logFile}.raw");
            index++;
        }

        void OnDestroy() { Profiler.enabled = false; Profiler.enableBinaryLog = false; }
    }
}
