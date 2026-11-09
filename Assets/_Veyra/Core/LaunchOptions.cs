using System;
using System.Globalization;

namespace VeyraCity.Core
{
    /// <summary>
    /// Command-line options used for automated verification runs of a player build.
    /// Example: VeyraCity.exe -veyraAutoQuit 20 -veyraScreenshot E:\shot.png -veyraScreenshotAt 8 -veyraStats E:\stats.json
    /// </summary>
    public sealed class LaunchOptions
    {
        public float AutoQuitSeconds = -1f;      // <= 0: never quit automatically
        public string ScreenshotPath;            // null: no screenshot
        public float ScreenshotAtSeconds = 5f;
        public string StatsPath;                 // null: stats are only logged
        public float WarmupSeconds = 3f;         // frames before this are excluded from stats
        public bool ShowOverlay;

        public bool IsAutomatedRun => AutoQuitSeconds > 0f;

        public static LaunchOptions Parse(string[] args)
        {
            var o = new LaunchOptions();
            if (args == null) return o;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (a.ToLowerInvariant())
                {
                    case "-veyraautoquit": if (TryFloat(next, out var q)) { o.AutoQuitSeconds = q; i++; } break;
                    case "-veyrascreenshot": if (IsValue(next)) { o.ScreenshotPath = next; i++; } break;
                    case "-veyrascreenshotat": if (TryFloat(next, out var s)) { o.ScreenshotAtSeconds = s; i++; } break;
                    case "-veyrastats": if (IsValue(next)) { o.StatsPath = next; i++; } break;
                    case "-veyrawarmup": if (TryFloat(next, out var w)) { o.WarmupSeconds = Math.Max(0f, w); i++; } break;
                    case "-veyraoverlay": o.ShowOverlay = true; break;
                }
            }
            return o;
        }

        static bool IsValue(string s) => !string.IsNullOrEmpty(s) && !s.StartsWith("-", StringComparison.Ordinal);

        static bool TryFloat(string s, out float value) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
