using UnityEngine;

namespace VeyraCity.Input
{
    /// <summary>
    /// Device-specific shaping of raw driving input (INPUT_AND_ACCESSIBILITY_SPEC §2).
    /// Keyboard: rate-limited steering with a faster return and counter-direction snap; ramped pedals.
    /// Gamepad: inner/outer dead zones, response curve and sensitivity. Pure C#; frame-rate independent.
    /// </summary>
    public sealed class DriverInputFilter
    {
        public float KeyboardSteerRise = 3f;
        public float KeyboardSteerReturn = 5f;
        public float KeyboardSteerCounter = 8f;
        public float KeyboardPedalRampSeconds = 0.12f;
        public float KeyboardPedalReleaseSeconds = 0.06f;
        public float StickInnerDeadZone = 0.08f;
        public float StickOuterDeadZone = 0.95f;
        public float StickExponent = 1.5f;
        public float StickSensitivity = 1f;

        public float Steer { get; private set; }

        public static float KeyboardSteerStep(float current, float target, float dt, float rise, float ret, float counter)
        {
            float rate;
            if (Mathf.Abs(target) < 1e-3f) rate = ret;
            else if (Mathf.Abs(current) > 1e-3f && Mathf.Sign(target) != Mathf.Sign(current)) rate = counter;
            else rate = rise;
            return Mathf.MoveTowards(current, target, rate * dt);
        }

        public static float ApplyStickShaping(float x, float inner, float outer, float exponent, float sensitivity = 1f)
        {
            float a = Mathf.Abs(x);
            if (a <= inner) return 0f;
            float n = Mathf.Clamp01((a - inner) / Mathf.Max(1e-3f, outer - inner));
            return Mathf.Sign(x) * Mathf.Clamp01(Mathf.Pow(n, Mathf.Max(0.1f, exponent)) * sensitivity);
        }

        public static float PedalStep(float current, float target, float dt, float rampSeconds, float releaseSeconds)
        {
            float rate = target > current ? 1f / Mathf.Max(1e-3f, rampSeconds) : 1f / Mathf.Max(1e-3f, releaseSeconds);
            return Mathf.MoveTowards(current, target, rate * dt);
        }

        public float UpdateSteer(float raw, bool digital, float dt)
        {
            if (digital)
                Steer = KeyboardSteerStep(Steer, Mathf.Clamp(raw, -1f, 1f), dt, KeyboardSteerRise, KeyboardSteerReturn, KeyboardSteerCounter);
            else
                Steer = ApplyStickShaping(raw, StickInnerDeadZone, StickOuterDeadZone, StickExponent, StickSensitivity);
            return Steer;
        }

        public float UpdatePedal(float current, float raw, bool digital, float dt)
        {
            raw = Mathf.Clamp01(raw);
            return digital ? PedalStep(current, raw, dt, KeyboardPedalRampSeconds, KeyboardPedalReleaseSeconds) : raw;
        }

        public void Reset() => Steer = 0f;
    }
}
