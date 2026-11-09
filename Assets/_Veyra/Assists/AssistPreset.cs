using UnityEngine;

namespace VeyraCity.Assists
{
    /// <summary>
    /// Assist preset (VEHICLE_HANDLING_SPEC §3). Assists never add grip; they only shape inputs and
    /// throttle/brake intervention. Every assist can be toggled individually.
    /// </summary>
    [CreateAssetMenu(menuName = "Veyra/Assist Preset", fileName = "AssistPreset")]
    public sealed class AssistPreset : ScriptableObject
    {
        public string PresetName = "Casual";

        [Header("Steering")]
        [Tooltip("Speed (m/s) at which the available steering lock halves")] public float SteeringSpeedRef = 22f;
        [Tooltip("Never reduce steering below this fraction of full lock")] [Range(0.05f, 1f)] public float MinSteerFraction = 0.12f;
        [Tooltip("Counter-steer assist gain (0 = off)")] [Range(0f, 1.5f)] public float CounterSteer = 0.7f;

        [Header("Traction and brakes")]
        public bool TractionControl = true;
        [Tooltip("Driven-wheel slip target as a multiple of the tyre's peak slip ratio")] public float TcsPeakSlipFactor = 1.2f;
        public bool Abs = true;

        [Header("Stability and drift")]
        [Tooltip("Throttle intervention strength when the body slip angle exceeds the limit (0 = off)")] [Range(0f, 1f)] public float StabilityControl = 0.6f;
        [Tooltip("Body slip angle (deg) where stability control starts to act")] public float StabilitySlipLimitDeg = 50f;
        [Tooltip("Drift assist: holds the drift angle band and prevents spins (0 = off)")] [Range(0f, 1f)] public float DriftAssist = 1f;
        [Tooltip("Target drift band, degrees")] public Vector2 DriftBandDeg = new Vector2(25f, 45f);
        [Tooltip("While drift assist holds a slide: driven-wheel slip limit as a multiple of peak slip (0 = unlimited)")] public float DriftTcsPeakSlipFactor = 3f;

        [Header("Accessibility")]
        public bool AutoAccelerate;
    }
}
