using UnityEngine;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Assists
{
    /// <summary>
    /// Turns raw <see cref="DriverIntent"/> into <see cref="VehicleControls"/>.
    /// Pure C# with explicit state, so it is unit-testable and shared by player and AI drivers.
    /// </summary>
    public sealed class AssistProcessor
    {
        /// <summary>Reverse engages only below this speed (2 km/h) …</summary>
        public const float ReverseEngageSpeed = 2f / 3.6f;
        /// <summary>… with the brake held this long (INPUT_AND_ACCESSIBILITY_SPEC §2).</summary>
        public const float ReverseHoldSeconds = 0.35f;

        /// <summary>A brake press shorter than this, released while steering at speed, is a drift tap (Asphalt-style).</summary>
        public const float DriftTapSeconds = 0.3f;

        public bool ReverseMode { get; private set; }
        public bool Drifting { get; private set; }
        float brakeHold;
        float prevSlip;
        float brakeDown = -1f; // seconds the current brake press has lasted (-1 = not pressed)

        public void Reset()
        {
            ReverseMode = false;
            Drifting = false;
            brakeHold = 0f;
            prevSlip = 0f;
            brakeDown = -1f;
        }

        /// <summary>Available fraction of full steering lock at a speed. No preset = no assist = full lock.</summary>
        public static float SpeedSteerFraction(float speed, AssistPreset p)
        {
            if (p == null) return 1f;
            float vRef = Mathf.Max(1f, p.SteeringSpeedRef);
            float x = speed / vRef;
            return Mathf.Max(p.MinSteerFraction, 1f / (1f + x * x));
        }

        public VehicleControls Process(in DriverIntent intent, in VehicleState state, AssistPreset preset, float dt)
        {
            var c = VehicleControls.Neutral;
            float forward = state.ForwardSpeed;
            float throttle = Mathf.Clamp01(intent.Throttle);
            float brake = Mathf.Clamp01(intent.Brake);
            if (preset != null && preset.AutoAccelerate && brake < 0.05f) throttle = 1f;

            // ---- Brake → reverse (held brake at a standstill), throttle cancels ----
            if (!ReverseMode)
            {
                if (brake > 0.05f && Mathf.Abs(forward) < ReverseEngageSpeed && throttle < 0.05f)
                {
                    brakeHold += dt;
                    if (brakeHold >= ReverseHoldSeconds) ReverseMode = true;
                }
                else brakeHold = 0f;
            }
            else if (throttle > 0.05f)
            {
                ReverseMode = false;
                brakeHold = 0f;
            }

            if (ReverseMode)
            {
                c.WantsReverse = true;
                c.Throttle = brake;     // brake input drives backwards
                c.Brake = 0f;
            }
            else
            {
                c.Throttle = throttle;
                c.Brake = brake;
                // Throttle pressed while still rolling backwards: brake first.
                if (throttle > 0.05f && forward < -ReverseEngageSpeed) { c.Brake = Mathf.Max(c.Brake, throttle); c.Throttle = 0f; }
            }

            c.Handbrake = Mathf.Clamp01(intent.Handbrake);
            c.GearRequest = intent.ShiftUp ? 1 : intent.ShiftDown ? -1 : 0;

            // ---- Steering: speed-sensitive lock + counter-steer ----
            float speed = Mathf.Abs(forward);
            float playerSteer = Mathf.Clamp(intent.Steer, -1f, 1f);
            c.SteerIntent = playerSteer;

            // ---- Drift tap: a short brake press released while steering at speed starts an arcade drift ----
            if (brake > 0.3f) brakeDown = brakeDown < 0f ? 0f : brakeDown + dt;
            else
            {
                if (brakeDown >= 0f && brakeDown < DriftTapSeconds && preset != null && preset.DriftTap
                    && Mathf.Abs(playerSteer) > 0.3f && forward > 12f && !ReverseMode)
                    c.DriftRequest = true;
                brakeDown = -1f;
            }

            if (state.ArcadeDrift)
            {
                // The handling-feel layer holds the drift; the assists only pass the steering through.
                Drifting = true;
                prevSlip = state.SlipAngleDeg;
                c.Steer = Mathf.Clamp(playerSteer * SpeedSteerFraction(speed, preset), -1f, 1f);
                c.TcsPeakSlipFactor = preset != null ? preset.DriftTcsPeakSlipFactor : 0f;
                c.AbsEnabled = preset == null || preset.Abs;
                return c;
            }

            float steer = playerSteer * SpeedSteerFraction(speed, preset);
            float slip = state.SlipAngleDeg;
            float maxSteerDeg = state.MaxSteerDeg > 1f ? state.MaxSteerDeg : 34f;
            // Drift logic is gated on total speed: at large angles most of the motion is sideways.
            float travel = Mathf.Max(state.Speed, speed);
            Drifting = travel > 8f && Mathf.Abs(slip) > 12f && !ReverseMode;
            float slipRate = dt > 0f ? (slip - prevSlip) / dt : 0f;
            prevSlip = slip;
            float driftTarget = 0f, driftError = 0f;
            if (preset != null && preset.CounterSteer > 0f && travel > 5f && !ReverseMode)
            {
                // Small slides: steer toward the direction of travel beyond a 4° dead band.
                float excess = Mathf.Sign(slip) * Mathf.Max(0f, Mathf.Abs(slip) - 4f);
                float gripSteer = steer + preset.CounterSteer * Mathf.Clamp(excess / maxSteerDeg, -1f, 1f);

                float driftWeight = preset.DriftAssist > 0f && travel > 8f ? Mathf.Clamp01((Mathf.Abs(slip) - 10f) / 8f) : 0f;
                if (driftWeight > 0f)
                {
                    // Drift-angle hold: the front wheels follow the front axle's direction of travel (true counter-steer),
                    // plus a PD correction toward a target angle. Steering into the drift raises the target, away lowers it.
                    float s = Mathf.Sign(slip);
                    float inward = Mathf.Clamp(-s * playerSteer, -1f, 1f);
                    driftTarget = Mathf.Lerp(preset.DriftBandDeg.x, preset.DriftBandDeg.y, 0.5f + 0.5f * inward);
                    driftError = Mathf.Abs(slip) - driftTarget;
                    float steerDeg = state.FrontSlipAngleDeg + s * (0.55f * driftError + 0.09f * s * slipRate);
                    float driftSteer = Mathf.Clamp(steerDeg / maxSteerDeg, -1f, 1f);
                    steer = Mathf.Lerp(gripSteer, driftSteer, Mathf.Clamp01(preset.CounterSteer + 0.25f) * driftWeight);
                }
                else steer = gripSteer;
            }
            c.Steer = Mathf.Clamp(steer, -1f, 1f);

            // ---- Traction control. While drift assist is active it switches to a higher drift slip target:
            // enough wheelspin to hold the slide, never so much that the rear loses all lateral grip.
            bool driftAssistActive = preset != null && preset.DriftAssist > 0f && Drifting;
            if (preset == null) c.TcsPeakSlipFactor = 0f;
            else if (driftAssistActive) c.TcsPeakSlipFactor = preset.DriftTcsPeakSlipFactor;
            else c.TcsPeakSlipFactor = preset.TractionControl ? preset.TcsPeakSlipFactor : 0f;
            c.AbsEnabled = preset == null || preset.Abs;

            // ---- Stability control: throttle intervention beyond the slip limit ----
            if (preset != null && !ReverseMode)
            {
                float absSlip = Mathf.Abs(slip);
                if (preset.StabilityControl > 0f && absSlip > preset.StabilitySlipLimitDeg)
                {
                    float over = Mathf.Clamp01((absSlip - preset.StabilitySlipLimitDeg) / 15f);
                    c.Throttle *= 1f - preset.StabilityControl * over;
                }
                // Drift assist: trim throttle when the angle runs beyond the target (spin prevention).
                if (preset.DriftAssist > 0f && Drifting && driftError > 2f)
                {
                    float over = Mathf.Clamp01((driftError - 2f) / 14f);
                    c.Throttle *= 1f - preset.DriftAssist * over * 0.9f;
                }
            }
            return c;
        }
    }
}
