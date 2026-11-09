using System;
using UnityEngine;

namespace VeyraCity.VehiclePhysics
{
    public enum DriveLayout { RWD, FWD, AWD }
    public enum DiffType { Open, LimitedSlip, Locked }

    [Serializable]
    public struct ChassisSpec
    {
        [Tooltip("kg")] public float Mass;
        [Tooltip("Static share of weight on the front axle, 0..1")] public float FrontWeightFraction;
        [Tooltip("Centre of mass height above the ground, m")] public float CoMHeight;
        [Tooltip("Body width (x), height (y), length (z) in metres; used for inertia and the placeholder collider")] public Vector3 Dimensions;
        [Tooltip("Scales the box inertia tensor (mass is concentrated toward the centre in real cars)")] public float InertiaScale;
    }

    [Serializable]
    public struct SuspensionSpec
    {
        [Tooltip("Mount to wheel centre at full extension, m")] public float RestLength;
        [Tooltip("Usable compression before the bump stop, m")] public float Travel;
        [Tooltip("N/m")] public float SpringRate;
        [Tooltip("N·s/m while compressing")] public float BumpDamping;
        [Tooltip("N·s/m while extending")] public float ReboundDamping;
        [Tooltip("N/m beyond the travel limit")] public float BumpStopRate;
        [Tooltip("N/m of left-right compression difference")] public float AntiRollRate;
        [Tooltip("Height above the contact patch where tyre forces act (roll-centre approximation), m")] public float RollCentreHeight;
    }

    [Serializable]
    public struct TyreSpec
    {
        [Tooltip("Outer radius, m")] public float Radius;
        [Tooltip("Section width, m (visual/collision only)")] public float Width;
        [Tooltip("Wheel + tyre + disc rotational inertia, kg·m²")] public float WheelInertia;
        [Tooltip("Peak friction coefficient at nominal load on dry asphalt")] public float Mu;
        [Tooltip("Lateral peak grip relative to longitudinal")] public float LateralGripScale;
        [Tooltip("Slip ratio at peak longitudinal force")] public float LongPeakSlip;
        [Tooltip("Slip angle at peak lateral force, degrees")] public float LatPeakSlipDeg;
        [Tooltip("Magic-formula shape C, longitudinal (higher = sharper drop after the peak)")] public float LongShape;
        [Tooltip("Magic-formula shape C, lateral")] public float LatShape;
        [Tooltip("Load sensitivity k: mu = mu0 * (1 - k * (Fz/Fnom - 1))")] public float LoadSensitivity;
        [Tooltip("Nominal load for load sensitivity, N")] public float NominalLoad;
        [Tooltip("Rolling resistance coefficient")] public float RollingResistance;
    }

    [Serializable]
    public struct AxleSpec
    {
        [Tooltip("Distance between left and right contact patches, m")] public float TrackWidth;
        public SuspensionSpec Suspension;
        public TyreSpec Tyre;
        public DifferentialSpec Differential;
    }

    [Serializable]
    public struct DifferentialSpec
    {
        public DiffType Type;
        [Tooltip("Locking torque available with no drive torque, N·m (LSD)")] public float Preload;
        [Tooltip("Fraction of axle torque available as locking torque on power (LSD)")] public float PowerLock;
        [Tooltip("Fraction of axle torque available as locking torque on coast (LSD)")] public float CoastLock;
    }

    [Serializable]
    public struct EngineSpec
    {
        [Tooltip("Full-throttle torque curve: x = rpm, y = N·m, ascending rpm")] public Vector2[] TorqueCurve;
        public float IdleRpm;
        [Tooltip("Rev limiter, rpm")] public float LimiterRpm;
        [Tooltip("Engine + flywheel inertia, kg·m²")] public float Inertia;
        [Tooltip("Engine braking torque at the limiter with a closed throttle, N·m")] public float EngineBrakeTorque;
        [Tooltip("Clutch-slip launch rpm at full throttle")] public float LaunchRpm;
    }

    [Serializable]
    public struct GearboxSpec
    {
        public float[] Ratios;
        public float ReverseRatio;
        public float FinalDrive;
        [Tooltip("Driveline efficiency 0..1")] public float Efficiency;
        [Tooltip("Seconds without drive during a shift")] public float ShiftTime;
        public float ShiftUpRpm;
        public float ShiftDownRpm;
        public bool Automatic;
    }

    [Serializable]
    public struct BrakeSpec
    {
        [Tooltip("Total maximum service brake torque across all four wheels, N·m")] public float MaxTorque;
        [Tooltip("Share of brake torque on the front axle, 0..1")] public float FrontBias;
        [Tooltip("Handbrake torque per rear wheel, N·m")] public float HandbrakeTorque;
    }

    [Serializable]
    public struct SteeringSpec
    {
        [Tooltip("Physical maximum road-wheel angle, degrees")] public float MaxAngleDeg;
        [Tooltip("0 = parallel steer, 1 = full Ackermann")] public float Ackermann;
        [Tooltip("Physical steering rack rate limit, degrees per second")] public float RateDegPerSec;
    }

    [Serializable]
    public struct AeroSpec
    {
        [Tooltip("Cd·A, m²")] public float DragArea;
        [Tooltip("Downforce Cl·A, m² (positive = downforce)")] public float DownforceArea;
        [Tooltip("Share of downforce on the front axle, 0..1")] public float FrontDownforceShare;
    }

    /// <summary>
    /// All tuning for one vehicle build. Versioned data asset: no tuning values live in code
    /// except the factory defaults in the editor tooling.
    /// </summary>
    [CreateAssetMenu(menuName = "Veyra/Vehicle Definition", fileName = "VehicleDefinition")]
    public sealed class VehicleDefinition : ScriptableObject
    {
        public string DisplayName = "Vehicle";
        [Tooltip("Stable id used in telemetry, saves and tests, e.g. Kite_Stock_RWD")] public string BuildId = "Vehicle";
        [Tooltip("Bump when tuning changes; recorded in telemetry")] public int TuningVersion = 1;

        public DriveLayout Layout = DriveLayout.RWD;
        [Tooltip("AWD only: share of drive torque sent to the front axle")] [Range(0f, 1f)] public float AwdFrontSplit = 0.4f;
        [Tooltip("Front axle to rear axle, m")] public float Wheelbase = 2.55f;

        public ChassisSpec Chassis;
        public AxleSpec Front;
        public AxleSpec Rear;
        public EngineSpec Engine;
        public GearboxSpec Gearbox;
        public BrakeSpec Brakes;
        public SteeringSpec Steering;
        public AeroSpec Aero;

        public string TuningHash()
        {
            // Stable short hash of the serialized tuning, for telemetry headers.
            string json = JsonUtility.ToJson(this);
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in json) { h ^= c; h *= 16777619; }
                return h.ToString("x8");
            }
        }
    }
}
