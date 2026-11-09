using NUnit.Framework;
using UnityEngine;
using VeyraCity.Assists;
using VeyraCity.Input;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Tests
{
    public class TyreModelTests
    {
        static TyreSpec Street => new TyreSpec
        {
            Radius = 0.316f, Mu = 1.0f, LateralGripScale = 0.9f, LongPeakSlip = 0.1f, LatPeakSlipDeg = 8f,
            LongShape = 1.75f, LatShape = 1.5f, LoadSensitivity = 0.12f, NominalLoad = 3000f,
        };

        [Test]
        public void Curve_PeaksAtOne_AndFallsOffBeyond()
        {
            Assert.AreEqual(0f, TyreModel.Curve(0f, 1.6f), 1e-6f);
            Assert.AreEqual(1f, TyreModel.Curve(1f, 1.6f), 1e-4f);
            Assert.Less(TyreModel.Curve(0.5f, 1.6f), 1f);
            Assert.Less(TyreModel.Curve(3f, 1.6f), 1f);
            Assert.Greater(TyreModel.Curve(3f, 1.6f), 0.5f, "sliding grip should stay drift-friendly");
        }

        [Test]
        public void PureLongitudinal_PeakForceEqualsMuTimesLoad()
        {
            var f = TyreModel.Evaluate(Street, 0.1f, 0f, 3000f, 1f);
            Assert.AreEqual(3000f, f.Fx, 1f);
            Assert.AreEqual(0f, f.Fy, 1e-3f);
        }

        [Test]
        public void LateralForce_OpposesSlip()
        {
            var right = TyreModel.Evaluate(Street, 0f, 4f * Mathf.Deg2Rad, 3000f, 1f);
            var left = TyreModel.Evaluate(Street, 0f, -4f * Mathf.Deg2Rad, 3000f, 1f);
            Assert.Less(right.Fy, 0f);
            Assert.Greater(left.Fy, 0f);
            Assert.AreEqual(-right.Fy, left.Fy, 1e-3f);
        }

        [Test]
        public void CombinedSlip_StaysInsideFrictionEllipse()
        {
            var t = Street;
            for (float k = -1f; k <= 1f; k += 0.1f)
            for (float a = -30f; a <= 30f; a += 3f)
            {
                var f = TyreModel.Evaluate(t, k, a * Mathf.Deg2Rad, 3000f, 1f);
                float mu = TyreModel.LoadedMu(t, 3000f, 1f);
                float e = (f.Fx / (mu * 3000f)) * (f.Fx / (mu * 3000f)) + (f.Fy / (mu * t.LateralGripScale * 3000f)) * (f.Fy / (mu * t.LateralGripScale * 3000f));
                Assert.LessOrEqual(e, 1.0001f, $"κ={k} α={a}");
            }
        }

        [Test]
        public void LoadSensitivity_ReducesMuAtHighLoad()
        {
            Assert.Less(TyreModel.LoadedMu(Street, 6000f, 1f), TyreModel.LoadedMu(Street, 3000f, 1f));
            Assert.Greater(TyreModel.LoadedMu(Street, 1500f, 1f), TyreModel.LoadedMu(Street, 3000f, 1f));
        }

        [Test]
        public void SurfaceGrip_ScalesForce()
        {
            var dry = TyreModel.Evaluate(Street, 0.1f, 0f, 3000f, 1f);
            var wet = TyreModel.Evaluate(Street, 0.1f, 0f, 3000f, 0.75f);
            Assert.AreEqual(0.75f, wet.Fx / dry.Fx, 1e-3f);
        }

        [Test]
        public void ZeroLoad_GivesNoForce()
        {
            var f = TyreModel.Evaluate(Street, 0.5f, 0.2f, 0f, 1f);
            Assert.AreEqual(0f, f.Fx);
            Assert.AreEqual(0f, f.Fy);
        }
    }

    public class PowertrainTests
    {
        static EngineSpec Engine => new EngineSpec
        {
            TorqueCurve = new[] { new Vector2(1000, 100), new Vector2(4000, 160), new Vector2(7000, 120) },
            IdleRpm = 900, LimiterRpm = 7000, Inertia = 0.15f, EngineBrakeTorque = 40, LaunchRpm = 3000,
        };
        static GearboxSpec Box => new GearboxSpec
        {
            Ratios = new[] { 3f, 2f, 1.4f, 1f }, ReverseRatio = 3.2f, FinalDrive = 4f, Efficiency = 0.9f,
            ShiftTime = 0.2f, ShiftUpRpm = 6500, ShiftDownRpm = 3000, Automatic = true,
        };

        [Test]
        public void TorqueCurve_Interpolates_AndClampsEnds()
        {
            Assert.AreEqual(100f, Powertrain.SampleTorqueCurve(Engine.TorqueCurve, 500f), 1e-3f);
            Assert.AreEqual(130f, Powertrain.SampleTorqueCurve(Engine.TorqueCurve, 2500f), 1e-3f);
            Assert.AreEqual(120f, Powertrain.SampleTorqueCurve(Engine.TorqueCurve, 9000f), 1e-3f);
        }

        [Test]
        public void LockedClutch_DeliversGearedTorque()
        {
            var p = new Powertrain(Engine, Box);
            float wheelOmega = 4000f / (3f * 4f) / Powertrain.RadPerSecToRpm; // 4000 rpm in 1st
            float t = p.Step(0.01f, 1f, 1f, wheelOmega, false, 0, out float inertia);
            Assert.IsTrue(p.ClutchLocked);
            Assert.AreEqual(4000f, p.EngineRpm, 1f);
            Assert.AreEqual(160f * 12f * 0.9f, t, 1f);
            Assert.AreEqual(0.15f * 144f, inertia, 1e-3f);
        }

        [Test]
        public void AutoGearbox_UpshiftsAboveShiftRpm()
        {
            var p = new Powertrain(Engine, Box);
            float wheelOmega = 6800f / 12f / Powertrain.RadPerSecToRpm;
            p.Step(0.01f, 1f, 1f, wheelOmega, false, 0, out _);
            Assert.IsTrue(p.Shifting);
            for (int i = 0; i < 30; i++) p.Step(0.01f, 1f, 1f, wheelOmega, false, 0, out _);
            Assert.AreEqual(2, p.Gear);
        }

        [Test]
        public void Limiter_CutsDriveTorque()
        {
            var p = new Powertrain(Engine, Box);
            p.ForceGear(4);
            float wheelOmega = 7100f / 4f / Powertrain.RadPerSecToRpm;
            float t = p.Step(0.01f, 1f, 1f, wheelOmega, false, 0, out _);
            Assert.LessOrEqual(t, 0f);
            Assert.IsTrue(p.AtLimiter);
        }

        [Test]
        public void ClosedThrottle_GivesEngineBraking()
        {
            var p = new Powertrain(Engine, Box);
            p.ForceGear(2);
            float wheelOmega = 5000f / 8f / Powertrain.RadPerSecToRpm;
            Assert.Less(p.Step(0.01f, 0f, 1f, wheelOmega, false, 0, out _), 0f);
        }

        [Test]
        public void ReverseRequest_SelectsReverseAtStandstill()
        {
            var p = new Powertrain(Engine, Box);
            for (int i = 0; i < 40; i++) p.Step(0.01f, 0f, 1f, 0f, true, 0, out _);
            Assert.AreEqual(-1, p.Gear);
            float t = p.Step(0.01f, 1f, 1f, 0f, true, 0, out _);
            Assert.Less(t, 0f, "reverse torque drives backwards");
        }
    }

    public class InputFilterTests
    {
        [Test]
        public void KeyboardSteer_RisesAtRiseRate()
        {
            float s = DriverInputFilter.KeyboardSteerStep(0f, 1f, 0.1f, 3f, 5f, 8f);
            Assert.AreEqual(0.3f, s, 1e-4f);
        }

        [Test]
        public void KeyboardSteer_CounterDirectionSnapsFaster()
        {
            float s = DriverInputFilter.KeyboardSteerStep(0.5f, -1f, 0.05f, 3f, 5f, 8f);
            Assert.AreEqual(0.1f, s, 1e-4f);
        }

        [Test]
        public void KeyboardSteer_FrameRateIndependent()
        {
            float a = 0f, b = 0f;
            for (int i = 0; i < 30; i++) a = DriverInputFilter.KeyboardSteerStep(a, 1f, 1f / 30f, 3f, 5f, 8f);
            for (int i = 0; i < 144; i++) b = DriverInputFilter.KeyboardSteerStep(b, 1f, 1f / 144f, 3f, 5f, 8f);
            Assert.AreEqual(a, b, 1e-3f);
        }

        [Test]
        public void Stick_DeadZoneAndCurve()
        {
            Assert.AreEqual(0f, DriverInputFilter.ApplyStickShaping(0.05f, 0.08f, 0.95f, 1.5f));
            Assert.AreEqual(1f, DriverInputFilter.ApplyStickShaping(0.97f, 0.08f, 0.95f, 1.5f), 1e-4f);
            Assert.AreEqual(-1f, DriverInputFilter.ApplyStickShaping(-1f, 0.08f, 0.95f, 1.5f), 1e-4f);
            float half = DriverInputFilter.ApplyStickShaping(0.515f, 0.08f, 0.95f, 1.5f);
            Assert.AreEqual(Mathf.Pow(0.5f, 1.5f), half, 1e-3f);
        }

        [Test]
        public void KeyboardPedal_RampsOver120ms()
        {
            float p = DriverInputFilter.PedalStep(0f, 1f, 0.06f, 0.12f, 0.06f);
            Assert.AreEqual(0.5f, p, 1e-4f);
        }

        [Test]
        public void DefaultBindings_ParseAndContainDrivingActions()
        {
            var asset = PlayerDriverInput.CreateDefaultActions();
            try
            {
                var map = asset.FindActionMap("Driving", true);
                foreach (var a in new[] { "Steer", "Throttle", "Brake", "Handbrake", "Nitro", "LookBack", "CameraCycle", "ShiftUp", "ShiftDown", "Reset", "Pause" })
                    Assert.IsNotNull(map.FindAction(a), a);
                Assert.GreaterOrEqual(map.FindAction("Steer").bindings.Count, 7, "A/D + arrows composites + stick");
            }
            finally { Object.DestroyImmediate(asset); }
        }
    }

    public class AssistTests
    {
        static AssistPreset Casual()
        {
            var p = ScriptableObject.CreateInstance<AssistPreset>();
            p.SteeringSpeedRef = 22f; p.MinSteerFraction = 0.12f; p.CounterSteer = 0.7f; p.TractionControl = true; p.TcsPeakSlipFactor = 1.2f;
            p.Abs = true; p.StabilityControl = 0.6f; p.StabilitySlipLimitDeg = 50f; p.DriftAssist = 1f; p.DriftBandDeg = new Vector2(25f, 45f);
            return p;
        }

        [Test]
        public void SteeringLock_ReducesWithSpeed()
        {
            var p = Casual();
            Assert.AreEqual(1f, AssistProcessor.SpeedSteerFraction(0f, p), 1e-4f);
            Assert.AreEqual(0.5f, AssistProcessor.SpeedSteerFraction(22f, p), 1e-4f);
            Assert.Less(AssistProcessor.SpeedSteerFraction(60f, p), 0.15f);
            Assert.AreEqual(1f, AssistProcessor.SpeedSteerFraction(60f, null), "no preset = no assist");
            Object.DestroyImmediate(p);
        }

        [Test]
        public void HeldBrakeAtStandstill_EngagesReverse_ThrottleCancels()
        {
            var proc = new AssistProcessor();
            var state = new VehicleState { ForwardSpeed = 0f };
            var brake = new DriverIntent { Brake = 1f };
            VehicleControls c = default;
            for (int i = 0; i < 30; i++) c = proc.Process(brake, state, null, 0.01f);
            Assert.IsFalse(proc.ReverseMode, "not before 0.35 s");
            for (int i = 0; i < 10; i++) c = proc.Process(brake, state, null, 0.01f);
            Assert.IsTrue(proc.ReverseMode);
            Assert.IsTrue(c.WantsReverse);
            Assert.AreEqual(1f, c.Throttle, "brake drives backwards in reverse");
            c = proc.Process(new DriverIntent { Throttle = 1f }, state, null, 0.01f);
            Assert.IsFalse(proc.ReverseMode);
        }

        [Test]
        public void BrakeWhileMoving_NeverEngagesReverse()
        {
            var proc = new AssistProcessor();
            var state = new VehicleState { ForwardSpeed = 10f };
            for (int i = 0; i < 200; i++) proc.Process(new DriverIntent { Brake = 1f }, state, null, 0.01f);
            Assert.IsFalse(proc.ReverseMode);
        }

        [Test]
        public void CounterSteer_SteersTowardTravelDirection()
        {
            var p = Casual();
            var proc = new AssistProcessor();
            // Small slide: velocity 9° right of the heading → assist adds right (+) steer beyond the 4° dead band.
            var small = proc.Process(default, new VehicleState { ForwardSpeed = 20f, SlipAngleDeg = 9f, MaxSteerDeg = 34f }, p, 0.01f);
            Assert.Greater(small.Steer, 0.05f);
            // Drift: front axle travelling 20° right of the heading → front wheels steer right toward it.
            proc.Reset();
            var drift = proc.Process(default, new VehicleState { ForwardSpeed = 20f, SlipAngleDeg = 30f, FrontSlipAngleDeg = 20f, MaxSteerDeg = 34f }, p, 0.01f);
            Assert.Greater(drift.Steer, 0.3f);
            // No slide: no counter-steer added.
            proc.Reset();
            var straight = proc.Process(default, new VehicleState { ForwardSpeed = 20f, SlipAngleDeg = 2f, MaxSteerDeg = 34f }, p, 0.01f);
            Assert.AreEqual(0f, straight.Steer, 1e-4f);
            Object.DestroyImmediate(p);
        }

        [Test]
        public void TractionControl_RaisesSlipLimitWhileDrifting()
        {
            var p = Casual();
            var proc = new AssistProcessor();
            var grip = proc.Process(new DriverIntent { Throttle = 1f }, new VehicleState { ForwardSpeed = 20f, SlipAngleDeg = 2f }, p, 0.01f);
            Assert.Greater(grip.TcsPeakSlipFactor, 0f);
            var drift = proc.Process(new DriverIntent { Throttle = 1f }, new VehicleState { ForwardSpeed = 20f, Speed = 23f, SlipAngleDeg = 30f }, p, 0.01f);
            Assert.AreEqual(p.DriftTcsPeakSlipFactor, drift.TcsPeakSlipFactor, "drift mode allows more wheelspin, but not unlimited");
            Assert.Greater(drift.TcsPeakSlipFactor, grip.TcsPeakSlipFactor);
            Object.DestroyImmediate(p);
        }
    }
}
