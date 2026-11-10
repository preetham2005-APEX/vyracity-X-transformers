using UnityEngine;
using UnityEngine.InputSystem;
using VeyraCity.World;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Card F3 debug UI: F1 toggles a panel with sliders for time of day, wetness, rain and wind, applied to the scene's
    /// WorldEnvironment (which drives the Veyra shader globals), plus a read-out of the globals the shaders receive.
    /// Created automatically in development builds and the Editor; absent from release builds.
    /// </summary>
    public sealed class EnvironmentDebugPanel : MonoBehaviour
    {
        bool visible, holdWetness, holdRain;
        float wetness, rain;
        Rect window = new Rect(20f, 20f, 360f, 300f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Debug.isDebugBuild && !Application.isEditor) return;
            if (FindAnyObjectByType<EnvironmentDebugPanel>() != null) return;
            var go = new GameObject("[EnvironmentDebugPanel]");
            DontDestroyOnLoad(go);
            go.AddComponent<EnvironmentDebugPanel>();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) visible = !visible;
            var env = WorldEnvironment.Instance;
            if (env == null) return;
            if (holdWetness) env.SetWetness(wetness);
            if (holdRain) env.SetRainIntensity(rain);
        }

        void OnGUI()
        {
            if (!visible) return;
            window = GUI.Window(0x5643, window, Draw, "Environment (F1)");
        }

        void Draw(int id)
        {
            var env = WorldEnvironment.Instance;
            if (env == null) { GUILayout.Label("No WorldEnvironment in the scene."); GUI.DragWindow(); return; }
            GUILayout.Label($"Time of day {env.TimeOfDay:00.00} h");
            float t = GUILayout.HorizontalSlider(env.TimeOfDay, 0f, 24f);
            if (Mathf.Abs(t - env.TimeOfDay) > 0.001f) env.SetTime(t);
            env.AnimateTime = GUILayout.Toggle(env.AnimateTime, "Animate time");

            holdWetness = GUILayout.Toggle(holdWetness, $"Hold wetness {wetness:0.00} (weather: {env.Weather}, current {env.Wetness:0.00})");
            wetness = GUILayout.HorizontalSlider(holdWetness ? wetness : env.Wetness, 0f, 1f);
            holdRain = GUILayout.Toggle(holdRain, $"Hold rain {rain:0.00} (current {env.RainIntensity:0.00})");
            rain = GUILayout.HorizontalSlider(holdRain ? rain : env.RainIntensity, 0f, 1f);
            GUILayout.Label($"Wind strength {env.WindStrength:0.00}");
            env.WindStrength = GUILayout.HorizontalSlider(env.WindStrength, 0f, 1f);
            GUILayout.BeginHorizontal();
            foreach (WorldEnvironment.WeatherKind w in System.Enum.GetValues(typeof(WorldEnvironment.WeatherKind)))
                if (GUILayout.Button(w.ToString())) env.SetWeather(w);
            GUILayout.EndHorizontal();
            GUILayout.Label($"Shader globals: night {VeyraGlobals.NightFactor:0.00}  wet {VeyraGlobals.Wetness:0.00}  rain {VeyraGlobals.RainIntensity:0.00}  wind {VeyraGlobals.WindStrength:0.00}  ({VeyraGlobals.Writes} writes)");
            GUI.DragWindow();
        }
    }
}
