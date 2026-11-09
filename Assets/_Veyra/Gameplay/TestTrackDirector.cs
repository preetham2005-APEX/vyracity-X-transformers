using System;
using System.IO;
using UnityEngine;
using VeyraCity.Assists;
using VeyraCity.Input;
using VeyraCity.UI;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Physics test track session: spawns the selected vehicle build, wires player input → assists →
    /// physics, the chase camera, the telemetry overlay and the CSV recorder.
    /// Launch flag <c>-veyraDemoDrive</c> replaces the player with a scripted demo drive (build verification).
    /// </summary>
    public sealed class TestTrackDirector : MonoBehaviour
    {
        [Serializable]
        public struct VehicleEntry
        {
            public VehicleDefinition Definition;
            [Tooltip("Optional visual model (CLAUDE.md §4 names). Empty = placeholder body.")] public GameObject VisualPrefab;
        }

        [SerializeField] VehicleEntry[] vehicles = Array.Empty<VehicleEntry>();
        [SerializeField] AssistPreset[] assistPresets = Array.Empty<AssistPreset>();
        [SerializeField] SurfaceDefinition defaultSurface;
        [SerializeField] Transform spawnPoint;
        [SerializeField] ChaseCamera chaseCamera;
        [SerializeField] Material placeholderBody;
        [SerializeField] Material placeholderWheel;

        PlayerDriverInput input;
        TelemetryOverlay overlay;
        TelemetryRecorder recorder;
        ScriptedDriver demo;
        VehicleController current;
        VehicleDriver driver;
        int vehicleIndex;
        int presetIndex;
        bool paused;

        public VehicleController Current => current;

        void Start()
        {
            // Explicit null checks: Unity's fake-null objects defeat the ?? operator.
            input = GetComponent<PlayerDriverInput>();
            if (input == null) input = gameObject.AddComponent<PlayerDriverInput>();
            overlay = GetComponent<TelemetryOverlay>();
            if (overlay == null) overlay = gameObject.AddComponent<TelemetryOverlay>();
            overlay.Visible = Debug.isDebugBuild || Application.isEditor;
            input.CameraCyclePressed += () => { if (chaseCamera != null) chaseCamera.Cycle(); };
            input.ResetPressed += ResetCar;
            input.PausePressed += TogglePause;
            input.TelemetryPressed += () => overlay.Visible = !overlay.Visible;
            input.NextVehiclePressed += () => Spawn((vehicleIndex + 1) % Mathf.Max(1, vehicles.Length));
            input.ToggleAssistsPressed += CyclePreset;
            input.RecordTelemetryPressed += ToggleRecording;

            if (Array.Exists(Environment.GetCommandLineArgs(), a => a.Equals("-veyraDemoDrive", StringComparison.OrdinalIgnoreCase)))
                demo = new ScriptedDriver { Policy = DemoPolicy };

            Spawn(0);
        }

        void Update()
        {
            if (chaseCamera != null) chaseCamera.LookBack = input != null && input.LookBackHeld;
        }

        void Spawn(int index)
        {
            if (vehicles.Length == 0) { Debug.LogError("[Veyra] TestTrackDirector has no vehicles."); return; }
            Vector3 pos = spawnPoint != null ? spawnPoint.position : Vector3.zero;
            Quaternion rot = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;
            if (current != null)
            {
                pos = current.transform.position;
                rot = Quaternion.Euler(0f, current.transform.eulerAngles.y, 0f);
                StopRecording();
                Destroy(current.gameObject);
            }
            vehicleIndex = index;
            var entry = vehicles[index];
            current = VehicleFactory.Create(entry.Definition, entry.VisualPrefab, defaultSurface, pos + Vector3.up * 0.05f, rot,
                                            interpolate: true, placeholderBody, placeholderWheel);
            driver = current.gameObject.AddComponent<VehicleDriver>();
            if (demo != null) demo.Vehicle = current;
            driver.Bind(current, demo != null ? demo : (IDriverIntentSource)input, CurrentPreset());
            if (chaseCamera != null) chaseCamera.SetTarget(current);
            overlay.Bind(current, driver);
            Debug.Log($"[Veyra] Spawned {entry.Definition.BuildId} (tuning v{entry.Definition.TuningVersion}, {entry.Definition.TuningHash()}) " +
                      $"model={(entry.VisualPrefab != null ? entry.VisualPrefab.name : "placeholder")} preset={CurrentPreset()?.PresetName}");
        }

        AssistPreset CurrentPreset() => assistPresets.Length > 0 ? assistPresets[presetIndex % assistPresets.Length] : null;

        void CyclePreset()
        {
            if (assistPresets.Length == 0) return;
            presetIndex = (presetIndex + 1) % assistPresets.Length;
            if (driver != null) driver.Preset = CurrentPreset();
        }

        void ResetCar()
        {
            if (current == null) return;
            Vector3 p = current.transform.position;
            Quaternion yaw = Quaternion.Euler(0f, current.transform.eulerAngles.y, 0f);
            current.ResetTo(new Vector3(p.x, Mathf.Max(p.y, 0f) + 0.4f, p.z), yaw);
            if (driver != null) driver.Processor.Reset();
        }

        void TogglePause()
        {
            paused = !paused;
            Time.timeScale = paused ? 0f : 1f;
            overlay.StatusLine = paused ? "PAUSED" : "";
        }

        void ToggleRecording()
        {
            if (recorder != null && recorder.Recording) { StopRecording(); return; }
            recorder = new TelemetryRecorder(current)
            {
                AssistPreset = CurrentPreset()?.PresetName ?? "none",
                RenderHz = Screen.currentResolution.refreshRateRatio.numerator > 0 ? (float)Screen.currentResolution.refreshRateRatio.value : 0f,
                Label = "test_track",
            };
            recorder.Start(1f / Time.fixedDeltaTime);
            overlay.StatusLine = "REC";
        }

        void StopRecording()
        {
            if (recorder == null || !recorder.Recording) return;
            recorder.Stop();
            string path = Path.Combine(TelemetryRecorder.DefaultFolder(),
                $"{DateTime.Now:yyyyMMdd_HHmmss}_{current.Definition.BuildId}.csv");
            Debug.Log($"[Veyra] Telemetry saved: {recorder.Save(path)} ({recorder.Rows} rows)");
            overlay.StatusLine = "";
        }

        void OnDestroy() => StopRecording();

        /// <summary>Demo drive: launch down the straight, then hold about 110 km/h with a lane-kept weave.</summary>
        static DriverIntent DemoPolicy(VehicleController v)
        {
            var s = v.State;
            float t = s.SimTime;
            var intent = new DriverIntent();
            if (t < 1f) return intent;
            float kmh = s.ForwardSpeed * 3.6f;
            intent.Throttle = kmh < 110f ? 1f : 0.35f;
            // Lane keeping around a slow ±5 m weave on the 30 m straight (x = 0 is the centre line).
            float x = v.Body.position.x;
            float heading = Mathf.DeltaAngle(0f, v.transform.eulerAngles.y);
            float targetX = t > 7f ? 5f * Mathf.Sin((t - 7f) * 0.55f) : 0f;
            intent.Steer = Mathf.Clamp((targetX - x) * 0.06f - heading * 0.05f, -0.6f, 0.6f);
            return intent;
        }
    }
}
