namespace VeyraCity.VehiclePhysics
{
    /// <summary>
    /// What a driver (player, AI or test script) wants, before assists.
    /// Produced by the Input and AI layers; consumed by the assist layer.
    /// </summary>
    public struct DriverIntent
    {
        /// <summary>-1 full left … +1 full right.</summary>
        public float Steer;
        /// <summary>0…1</summary>
        public float Throttle;
        /// <summary>0…1. Held at a standstill, it engages reverse (see the input spec).</summary>
        public float Brake;
        /// <summary>0…1</summary>
        public float Handbrake;
        public bool Nitro;
        /// <summary>One-shot requests (manual gearbox). A source clears them after ReadIntent.</summary>
        public bool ShiftUp;
        public bool ShiftDown;
    }

    /// <summary>
    /// A source of driver intent. ReadIntent is called once per physics step;
    /// one-shot flags are cleared by the source when read.
    /// </summary>
    public interface IDriverIntentSource
    {
        DriverIntent ReadIntent();
    }

    /// <summary>Controls after the assist layer; this is all the physics sees.</summary>
    public struct VehicleControls
    {
        /// <summary>-1…1 as a fraction of the physical maximum steering lock.</summary>
        public float Steer;
        public float Throttle;
        public float Brake;
        public float Handbrake;
        /// <summary>+1 shift up, -1 shift down, 0 none (manual gearbox only).</summary>
        public int GearRequest;
        public bool WantsReverse;
        public bool AbsEnabled;
        /// <summary>Traction control slip target as a multiple of the driven tyres' peak slip ratio; 0 = off.</summary>
        public float TcsPeakSlipFactor;
        /// <summary>Multiplier on engine torque (nitro). 1 = none.</summary>
        public float TorqueScale;
        /// <summary>Extra longitudinal thrust in newtons (nitro top-speed extension). 0 = none.</summary>
        public float ExtraThrust;
        /// <summary>The driver's raw steering input −1…1 before speed-sensitive limiting (drift and air control use it).</summary>
        public float SteerIntent;
        /// <summary>One-step request to start an arcade drift (a brake tap while steering).</summary>
        public bool DriftRequest;

        public static VehicleControls Neutral => new VehicleControls { TorqueScale = 1f };
    }
}
