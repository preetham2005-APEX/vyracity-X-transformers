using UnityEngine;

namespace VeyraCity.Nitro
{
    /// <summary>
    /// BUG-087 (round 3, D2): the nitro mode table and timings as a versioned asset instead of constants in NitroSystem.
    /// Defaults are the values NITRO_BALANCE_SPEC section 2 set and NitroSystem used as constants; NitroSystem falls back
    /// to <see cref="Defaults"/> when no asset is assigned. Index by <see cref="NitroMode"/>: Off, Standard, CleanBoost,
    /// Overdrive.
    /// </summary>
    [CreateAssetMenu(menuName = "Veyra/Tuning/Nitro Tuning", fileName = "NitroTuning")]
    public sealed class NitroTuning : ScriptableObject
    {
        [Tooltip("Engine torque multiplier per mode (Off, Standard, Clean Boost, Overdrive)")]
        public float[] TorqueMul = { 1f, 1.35f, 1.55f, 1.90f };
        [Tooltip("Top-speed extension per mode, fraction of the car's estimated top speed")]
        public float[] SpeedCap = { 0f, 0.05f, 0.07f, 0.10f };
        [Tooltip("Extra thrust per mode at standstill, m/s² (scaled by mass, fades toward the speed cap)")]
        public float[] ThrustAccel = { 0f, 2.4f, 3.1f, 4.4f };
        [Tooltip("Meter units drained per second in Standard mode (Clean Boost drains 85 % of this)")]
        public float StandardDrain = 20f;
        [Tooltip("Clean Boost window after activation, seconds (open, close)")]
        public float CleanWindowOpen = 0.60f, CleanWindowClose = 0.85f;
        [Tooltip("Double-tap window for Overdrive, seconds")] public float OverdriveTapWindow = 0.30f;
        [Tooltip("Overdrive length and cooldown, seconds")] public float OverdriveDuration = 2f, OverdriveCooldown = 1f;
        [Tooltip("Upper bound on the boost torque multiplier after upgrades (BUG-084)")] public float MaxTorqueMul = 2.5f;

        [Header("Drift scoring")]
        [Tooltip("A slide scores above this body slip angle (deg) and speed (km/h)")] public float DriftMinAngleDeg = 12f, DriftMinSpeedKmh = 30f;
        [Tooltip("Slip angle where scoring stops growing, deg")] public float DriftMaxScoredAngleDeg = 60f;

        static NitroTuning defaults;
        /// <summary>An in-memory instance with the default values (used when no asset is assigned).</summary>
        public static NitroTuning Defaults => defaults != null ? defaults : (defaults = CreateInstance<NitroTuning>());

        public float Torque(NitroMode m) => At(TorqueMul, m, 1f);
        public float Cap(NitroMode m) => At(SpeedCap, m, 0f);
        public float Thrust(NitroMode m) => At(ThrustAccel, m, 0f);
        static float At(float[] a, NitroMode m, float fallback) => a != null && (int)m < a.Length ? a[(int)m] : fallback;
    }
}
