using System;
using UnityEngine;
using UnityEngine.InputSystem;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Input
{
    /// <summary>
    /// Player driving input through the Unity Input System (keyboard and gamepad together).
    /// Produces <see cref="DriverIntent"/>; never touches the vehicle directly.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class PlayerDriverInput : MonoBehaviour, IDriverIntentSource
    {
        public enum DeviceKind { Keyboard, Gamepad }

        InputActionAsset asset;
        InputAction steer, throttle, brake, handbrake, nitro, lookBack, cameraCycle, shiftUp, shiftDown, reset, pause;
        InputAction telemetry, nextVehicle, toggleAssists, recordTelemetry;
        readonly DriverInputFilter filter = new DriverInputFilter();

        float throttleOut, brakeOut, handbrakeOut;
        bool nitroHeld, pendingShiftUp, pendingShiftDown;

        public DriverInputFilter Filter => filter;
        public InputActionAsset Actions => asset;
        public DeviceKind LastDevice { get; private set; } = DeviceKind.Keyboard;
        public bool LookBackHeld { get; private set; }
        public float Steer => filter.Steer;
        public float Throttle => throttleOut;
        public float Brake => brakeOut;

        public event Action CameraCyclePressed, ResetPressed, PausePressed;
        public event Action TelemetryPressed, NextVehiclePressed, ToggleAssistsPressed, RecordTelemetryPressed;
        public event Action<DeviceKind> DeviceChanged;

        public static InputActionAsset CreateDefaultActions() => InputActionAsset.FromJson(VeyraControlsJson.Json);

        void Awake()
        {
            asset = CreateDefaultActions();
            var d = asset.FindActionMap("Driving", true);
            steer = d.FindAction("Steer", true);
            throttle = d.FindAction("Throttle", true);
            brake = d.FindAction("Brake", true);
            handbrake = d.FindAction("Handbrake", true);
            nitro = d.FindAction("Nitro", true);
            lookBack = d.FindAction("LookBack", true);
            cameraCycle = d.FindAction("CameraCycle", true);
            shiftUp = d.FindAction("ShiftUp", true);
            shiftDown = d.FindAction("ShiftDown", true);
            reset = d.FindAction("Reset", true);
            pause = d.FindAction("Pause", true);
            var dbg = asset.FindActionMap("Debug", true);
            telemetry = dbg.FindAction("Telemetry", true);
            nextVehicle = dbg.FindAction("NextVehicle", true);
            toggleAssists = dbg.FindAction("ToggleAssists", true);
            recordTelemetry = dbg.FindAction("RecordTelemetry", true);
        }

        void OnEnable() => asset?.Enable();
        void OnDisable() => asset?.Disable();
        void OnDestroy() { if (asset != null) Destroy(asset); }

        static bool IsDigital(InputAction a) => a.activeControl == null || a.activeControl.device is Keyboard;

        void TrackDevice(InputAction a)
        {
            var dev = a.activeControl?.device;
            if (dev == null) return;
            var kind = dev is Gamepad ? DeviceKind.Gamepad : DeviceKind.Keyboard;
            if (kind != LastDevice) { LastDevice = kind; DeviceChanged?.Invoke(kind); }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            TrackDevice(steer); TrackDevice(throttle); TrackDevice(brake);

            filter.UpdateSteer(steer.ReadValue<float>(), IsDigital(steer), dt);
            throttleOut = filter.UpdatePedal(throttleOut, throttle.ReadValue<float>(), IsDigital(throttle), dt);
            brakeOut = filter.UpdatePedal(brakeOut, brake.ReadValue<float>(), IsDigital(brake), dt);
            handbrakeOut = handbrake.IsPressed() ? 1f : 0f;
            nitroHeld = nitro.IsPressed();
            LookBackHeld = lookBack.IsPressed();

            if (shiftUp.WasPressedThisFrame()) pendingShiftUp = true;
            if (shiftDown.WasPressedThisFrame()) pendingShiftDown = true;
            if (cameraCycle.WasPressedThisFrame()) CameraCyclePressed?.Invoke();
            if (reset.WasPressedThisFrame()) ResetPressed?.Invoke();
            if (pause.WasPressedThisFrame()) PausePressed?.Invoke();
            if (telemetry.WasPressedThisFrame()) TelemetryPressed?.Invoke();
            if (nextVehicle.WasPressedThisFrame()) NextVehiclePressed?.Invoke();
            if (toggleAssists.WasPressedThisFrame()) ToggleAssistsPressed?.Invoke();
            if (recordTelemetry.WasPressedThisFrame()) RecordTelemetryPressed?.Invoke();
        }

        public DriverIntent ReadIntent()
        {
            var intent = new DriverIntent
            {
                Steer = filter.Steer,
                Throttle = throttleOut,
                Brake = brakeOut,
                Handbrake = handbrakeOut,
                Nitro = nitroHeld,
                ShiftUp = pendingShiftUp,
                ShiftDown = pendingShiftDown,
            };
            pendingShiftUp = pendingShiftDown = false;
            return intent;
        }
    }
}
