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
    }
}
