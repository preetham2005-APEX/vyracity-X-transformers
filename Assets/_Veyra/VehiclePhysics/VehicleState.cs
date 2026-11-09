using UnityEngine;

namespace VeyraCity.VehiclePhysics
{
    /// <summary>Per-wheel simulation state, refreshed every physics step (telemetry source).</summary>
    public struct WheelState
    {
        public bool Grounded;
        /// <summary>Tyre normal load, N.</summary>
        public float Load;
        public float Compression;
        public float SuspensionLength;
        public float SlipRatio;
        public float SlipAngleDeg;
        public float CombinedSlip;
        public float Fx;
        public float Fy;
        /// <summary>rad/s (+ = rolling forward).</summary>
        public float AngularVelocity;
        public float SteerAngleDeg;
        public float DriveTorque;
        public float BrakeTorque;
        public float SpinAngleDeg;
        public Vector3 ContactPoint;
        public Vector3 ContactNormal;
        public SurfaceDefinition Surface;
    }

    /// <summary>Whole-vehicle state for assists, cameras, HUD and telemetry.</summary>
    public struct VehicleState
    {
        public float Speed;
        /// <summary>m/s along the body's forward axis.</summary>
        public float ForwardSpeed;
        public float LateralSpeed;
        /// <summary>rad/s, + = turning right.</summary>
        public float YawRate;
        /// <summary>Body slip (drift) angle in degrees; + when the velocity points right of the heading.</summary>
        public float SlipAngleDeg;
        /// <summary>Direction of travel of the front-axle midpoint relative to the heading, degrees (+ = right).
        /// Steering the front wheels to this angle aligns them with their direction of travel (counter-steer in a drift).</summary>
        public float FrontSlipAngleDeg;
        /// <summary>Physical maximum road-wheel angle of this vehicle, degrees.</summary>
        public float MaxSteerDeg;
        public int Gear;
        public float EngineRpm;
        public float EngineTorque;
        public bool Shifting;
        public bool AtLimiter;
        public float LongitudinalG;
        public float LateralG;
        public int GroundedWheels;
        public float AppliedThrottle;
        public float AppliedBrake;
        public float AppliedHandbrake;
        public float SteerAngleDeg;
        public float TcsCut;
        public bool AbsActive;
        public float SimTime;
        public float SpeedKmh => Speed * 3.6f;
    }
}
