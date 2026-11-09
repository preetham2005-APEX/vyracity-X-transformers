using UnityEngine;

namespace VeyraCity.VehiclePhysics
{
    /// <summary>Engine, clutch and gearbox state. Pure C# (no MonoBehaviour), unit-testable.</summary>
    public sealed class Powertrain
    {
        public const float RadPerSecToRpm = 60f / (2f * Mathf.PI);

        readonly EngineSpec engine;
        readonly GearboxSpec gearbox;

        public int Gear { get; private set; } = 1;          // -1 reverse, 0 neutral, 1..N
        public float EngineRpm { get; private set; }
        public bool Shifting => shiftTimer > 0f;
        public bool ClutchLocked { get; private set; }
        public bool AtLimiter { get; private set; }
        public int GearCount => gearbox.Ratios?.Length ?? 0;
        /// <summary>Engine torque produced this step (before gearing), N·m.</summary>
        public float EngineTorque { get; private set; }

        float shiftTimer;
        int pendingGear;
        float limiterCut;

        public Powertrain(EngineSpec engine, GearboxSpec gearbox)
        {
            this.engine = engine;
            this.gearbox = gearbox;
            EngineRpm = engine.IdleRpm;
        }

        public static float SampleTorqueCurve(Vector2[] curve, float rpm)
        {
            if (curve == null || curve.Length == 0) return 0f;
            if (rpm <= curve[0].x) return curve[0].y;
            for (int i = 1; i < curve.Length; i++)
            {
                if (rpm <= curve[i].x)
                {
                    float t = (rpm - curve[i - 1].x) / Mathf.Max(1e-3f, curve[i].x - curve[i - 1].x);
                    return Mathf.Lerp(curve[i - 1].y, curve[i].y, t);
                }
            }
            return curve[curve.Length - 1].y;
        }

        public float GearRatio(int gear)
        {
            if (gear > 0 && gearbox.Ratios != null && gear <= gearbox.Ratios.Length) return gearbox.Ratios[gear - 1];
            if (gear < 0) return -Mathf.Abs(gearbox.ReverseRatio);
            return 0f;
        }

        public float TotalRatio => GearRatio(Gear) * gearbox.FinalDrive;

        public void ForceGear(int gear)
        {
            Gear = Mathf.Clamp(gear, -1, GearCount);
            shiftTimer = 0f;
        }

        void RequestShift(int target)
        {
            target = Mathf.Clamp(target, -1, GearCount);
            if (target == Gear || Shifting) return;
            pendingGear = target;
            shiftTimer = Mathf.Max(0.01f, gearbox.ShiftTime);
            Gear = 0; // neutral while shifting
        }

        /// <summary>
        /// Advances gear logic and returns the torque delivered to the driven axle(s), N·m (after gearing),
        /// plus the engine inertia reflected to the driven wheels (kg·m², total).
        /// </summary>
        /// <param name="drivenWheelOmega">Mean angular velocity of the driven wheels, rad/s.</param>
        /// <param name="declutch">Open the clutch (e.g. hard braking with a closed throttle, as an automatic would).</param>
        public float Step(float dt, float throttle, float torqueScale, float drivenWheelOmega,
                          bool wantsReverse, int manualRequest, out float reflectedInertia, bool declutch = false)
        {
            reflectedInertia = 0f;
            throttle = Mathf.Clamp01(throttle);

            // Gear selection.
            if (Shifting)
            {
                shiftTimer -= dt;
                if (shiftTimer <= 0f) { shiftTimer = 0f; Gear = pendingGear; }
            }
            else
            {
                float forwardWheelRpm = drivenWheelOmega * RadPerSecToRpm;
                if (wantsReverse && Gear >= 0)
                {
                    if (Mathf.Abs(forwardWheelRpm) * gearbox.FinalDrive < 400f) RequestShift(-1);
                }
                else if (!wantsReverse && Gear <= 0)
                {
                    RequestShift(1);
                }
                else if (Gear > 0)
                {
                    float rpmNow = forwardWheelRpm * GearRatio(Gear) * gearbox.FinalDrive;
                    if (gearbox.Automatic)
                    {
                        if (rpmNow > gearbox.ShiftUpRpm && Gear < GearCount) RequestShift(Gear + 1);
                        else if (Gear > 1 && rpmNow < gearbox.ShiftDownRpm)
                        {
                            float rpmAfter = forwardWheelRpm * GearRatio(Gear - 1) * gearbox.FinalDrive;
                            if (rpmAfter < gearbox.ShiftUpRpm * 0.92f) RequestShift(Gear - 1);
                        }
                    }
                    else if (manualRequest != 0)
                    {
                        RequestShift(Mathf.Clamp(Gear + manualRequest, 1, GearCount));
                    }
                }
            }

            float ratio = TotalRatio;
            float rpmFromWheels = drivenWheelOmega * ratio * RadPerSecToRpm; // positive when driving in the gear's direction
            float idle = engine.IdleRpm;
            float freeRevTarget = idle + throttle * (engine.LimiterRpm - idle);

            if (Gear == 0 || Shifting || declutch)
            {
                ClutchLocked = false;
                EngineRpm = Mathf.MoveTowards(EngineRpm, freeRevTarget, 9000f * dt);
                EngineTorque = 0f;
                AtLimiter = false;
                return 0f;
            }

            float torque;
            if (rpmFromWheels < engine.LaunchRpm && throttle > 0.05f)
            {
                // Clutch slipping: the engine holds launch rpm and the clutch transmits engine torque.
                ClutchLocked = false;
                float slipRpm = Mathf.Max(rpmFromWheels, idle + throttle * (engine.LaunchRpm - idle));
                EngineRpm = Mathf.MoveTowards(EngineRpm, slipRpm, 12000f * dt);
                torque = SampleTorqueCurve(engine.TorqueCurve, EngineRpm) * throttle * torqueScale;
            }
            else if (rpmFromWheels < idle)
            {
                // Rolling too slowly with a closed throttle: clutch open, engine idles.
                ClutchLocked = false;
                EngineRpm = Mathf.MoveTowards(EngineRpm, idle, 6000f * dt);
                torque = 0f;
            }
            else
            {
                ClutchLocked = true;
                EngineRpm = rpmFromWheels;
                if (throttle > 0.01f)
                    torque = SampleTorqueCurve(engine.TorqueCurve, EngineRpm) * throttle * torqueScale;
                else
                    torque = -engine.EngineBrakeTorque * Mathf.Clamp01(EngineRpm / Mathf.Max(1f, engine.LimiterRpm));
                reflectedInertia = engine.Inertia * ratio * ratio;
            }

            // Rev limiter with a little hysteresis.
            if (EngineRpm >= engine.LimiterRpm) limiterCut = 0.06f;
            if (limiterCut > 0f)
            {
                limiterCut -= dt;
                if (torque > 0f) torque = 0f;
            }
            AtLimiter = limiterCut > 0f;
            EngineTorque = torque;

            float eff = torque >= 0f ? gearbox.Efficiency : 1f;
            return torque * ratio * eff;
        }
    }
}
