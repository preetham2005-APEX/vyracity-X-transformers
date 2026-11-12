using UnityEngine;

namespace VeyraCity.VehiclePhysics
{
    /// <summary>
    /// Driving style layer on top of the raycast tyre model (one asset per style, e.g. Arcade or Simulation).
    /// The per-car character (mass, grip, power, suspension) stays in <see cref="VehicleDefinition"/>; this asset
    /// decides how much the game helps:
    ///  * grip mode (GTA-like): lateral slide damping, yaw-rate tracking toward the steered path, speed downforce;
    ///  * drift mode (Asphalt-like): entered with a brake tap or the handbrake while steering, holds a drift angle
    ///    set by the steering, carves the path and keeps the speed, exits on counter-steer or straightening up;
    ///  * air: auto-levelling and steering control while airborne; flipped cars are set back on their wheels;
    ///  * walls: shallow hits slide along the wall and keep most of the speed instead of stopping dead.
    /// All zeros = pure simulation.
    /// </summary>
    [CreateAssetMenu(menuName = "Veyra/Handling Feel", fileName = "HandlingFeel")]
    public sealed class HandlingFeel : ScriptableObject
    {
        public string StyleName = "Arcade";
        [Tooltip("Bump when tuning changes; recorded in telemetry")] public int TuningVersion = 1;

        [Header("Grip mode")]
        [Tooltip("Lateral slide damping, 1/s: the share of sideways body speed removed per second (0 = off)")] public float SlideDamping = 2.2f;
        [Tooltip("Cap on the slide damping acceleration, in g")] public float SlideDampingMaxG = 0.55f;
        [Tooltip("Yaw-rate tracking gain toward the steered (bicycle-model) yaw rate, 1/s (0 = off)")] public float YawTracking = 5f;
        [Tooltip("Cap on the yaw tracking angular acceleration, rad/s²")] public float YawTrackingMaxAccel = 6f;
        [Tooltip("Steered yaw rate is limited to this many g of lateral acceleration")] public float YawRateLimitG = 1.15f;
        [Tooltip("Tyre peak mu at which the yaw-rate limit is exactly YawRateLimitG; grippier tyres corner harder, a truck's less (iteration 5: one global cap made every car corner at 1.1 g)")]
        public float ReferenceMu = 1.05f;
        [Tooltip("Mass at which yaw tracking runs at its full gain; heavier cars turn in more slowly (gain × √(ref/mass), clamped)")]
        public float ReferenceMass = 1500f;
        [Tooltip("Clamp of the mass-based turn-in factor")] public Vector2 AgilityRange = new Vector2(0.6f, 1.15f);

        [Header("Stability at speed")]
        [Tooltip("Extra downforce at the reference speed, in g (grounded only)")] public float DownforceG = 0.45f;
        [Tooltip("Speed at which the full extra downforce applies, km/h (scales with v²)")] public float DownforceRefKmh = 220f;

        [Header("Drift mode")]
        public bool DriftEnabled = true;
        [Tooltip("Minimum speed to start a drift, km/h")] public float DriftMinSpeedKmh = 45f;
        [Tooltip("Steering needed to start a drift (fraction of full input)")] public float DriftEntrySteer = 0.3f;
        [Tooltip("Drift angle with light steering → full steering into the corner, degrees")] public Vector2 DriftAngleDeg = new Vector2(16f, 36f);
        [Tooltip("Path turn rate at light → full steering, rad/s (how tight the drift carves)")] public Vector2 DriftTurnRate = new Vector2(0.32f, 0.85f);
        [Tooltip("Lateral acceleration limit while carving, g")] public float DriftMaxLatG = 1.25f;
        [Tooltip("Drift angle tracking gain, 1/s")] public float DriftAngleGain = 4f;
        [Tooltip("Yaw-rate tracking gain in a drift, 1/s")] public float DriftYawGain = 9f;
        [Tooltip("Tyre lateral force scale while drifting (tyres stop fighting the slide)")] [Range(0f, 1f)] public float DriftTyreLateral = 0.25f;
        [Tooltip("Speed lost per second while drifting on throttle, as a fraction of the entry speed")] public float DriftSpeedLoss = 0.04f;
        [Tooltip("Seconds of straight steering (or counter-steer) that end a drift")] public float DriftExitDelay = 0.25f;
        [Tooltip("Blend in / out time, seconds")] public float DriftBlendIn = 0.2f, DriftBlendOut = 0.35f;
        [Tooltip("BUG-087: a drift starts ending below this inward steering (fraction), or at once past this counter-steer")] public float DriftExitSteer = 0.12f, DriftCancelCounterSteer = 0.5f;
        [Tooltip("BUG-087: a drift ends below this share of the minimum speed, above this brake input, or after this airtime (s)")] public float DriftExitSpeedShare = 0.6f, DriftExitBrake = 0.6f, DriftExitAirSeconds = 0.6f;
        [Tooltip("BUG-087: share of grip-mode yaw control removed at full handbrake")] [Range(0f, 1f)] public float HandbrakeGripCut = 0.85f;

        [Header("Air")]
        [Tooltip("Auto-level gain while airborne, 1/s² (keeps the wheels down for the landing)")] public float AirLevelGain = 9f;
        [Tooltip("Angular damping while airborne, 1/s")] public float AirDamping = 1.2f;
        [Tooltip("Yaw control from steering while airborne, rad/s²")] public float AirYawControl = 2.5f;

        [Header("Recovery")]
        [Tooltip("Seconds upside down (or on its side) and nearly stopped before the car is set back on its wheels; 0 = never")] public float FlipRecoverSeconds = 1.6f;

        [Header("Walls")]
        [Tooltip("Hits shallower than this keep their speed along the wall, degrees (0 = off)")] public float WallGlanceMaxDeg = 38f;
        [Tooltip("Share of speed lost along the wall on a glancing hit")] [Range(0f, 1f)] public float WallSpeedLoss = 0.1f;
        [Tooltip("Bounce off the wall (share of the speed into it)")] [Range(0f, 1f)] public float WallRestitution = 0.15f;
        [Tooltip("BUG-087: yaw rate kept per 10 ms while scraping a wall (0.4 = 60 % removed)")] [Range(0f, 1f)] public float WallYawKeepPer10ms = 0.4f;
        [Tooltip("BUG-087: on first contact, turn toward the wall tangent when within this angle (rad)")] public float WallAlignMaxRad = 1.2f;
        [Tooltip("BUG-087: alignment yaw rate per radian off the tangent, and its limit (rad/s)")] public float WallAlignGain = 2.5f, WallAlignMaxRate = 2.5f;

        [Header("Visual body lean (VehicleBodyLean)")]
        [Tooltip("BUG-087: degrees of roll / pitch per g, their limits, extra drift roll (deg) and response (1/s)")]
        public float LeanRollPerG = 2.4f, LeanPitchPerG = 1.8f, LeanMaxRoll = 4f, LeanMaxPitch = 2.6f, LeanDriftRoll = 1.5f, LeanResponse = 7f;

        [Header("Weight transfer")]
        [Tooltip("D2: where the feel layer's side forces act, from the centre of mass (0) down to the tyre contact patches (1). "
               + "Above 0 they roll the body and move load onto the outer wheels like real tyre forces do")]
        [Range(0f, 1f)] public float SideForceContactShare = 0.35f;

        [Header("Rigidbody")]
        [Tooltip("BUG-087: rigidbody angular damping and maximum angular velocity")] public float AngularDamping = 0.05f, MaxAngularVelocity = 30f;
    }
}
