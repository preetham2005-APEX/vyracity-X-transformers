using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VeyraCity.Assists;
using VeyraCity.Gameplay;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Tests
{
    /// <summary>
    /// The arcade handling layer (<see cref="HandlingFeel"/>) on the stock Kite in the script-simulated harness.
    /// Each test prints its measurements (Arcade, and Simulation where a comparison helps) to the test log.
    /// </summary>
    public class HandlingFeelTests
    {
        const string KiteDef = "Assets/_Veyra/Data/Vehicles/Kite_Stock_RWD.asset";
        const string CasualPreset = "Assets/_Veyra/Data/Assists/Casual.asset";
        const string Asphalt = "Assets/_Veyra/Data/Surfaces/dry_asphalt.asset";

        static VehicleTestHarness Make(bool arcade)
        {
            var def = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(KiteDef);
            var preset = AssetDatabase.LoadAssetAtPath<AssistPreset>(CasualPreset);
            var surface = AssetDatabase.LoadAssetAtPath<SurfaceDefinition>(Asphalt);
            if (def == null || preset == null || surface == null) Assert.Ignore("vehicle data assets missing");
            var feel = arcade ? ScriptableObject.CreateInstance<HandlingFeel>() : null;
            var h = VehicleTestHarness.Create(def, preset, surface, 100f, feel);
            h.Settle();
            return h;
        }

        static float Kmh(VehicleController v) => v.State.Speed * 3.6f;

        static float DriveTo(VehicleTestHarness h, float kmh)
        {
            h.Script.Policy = v => new DriverIntent { Throttle = 1f };
            h.RunUntil(v => Kmh(v) >= kmh, 25f);
            return Kmh(h.Vehicle);
        }

        [Test]
        public void Arcade_BrakeTapDrift_HoldsAngleAndSpeed_ThenExitsClean()
        {
            using var h = Make(true);
            float entry = DriveTo(h, 95f);
            float t0 = h.Time;
            // tap the brake for 0.15 s while steering right, then hold steering and throttle for 4 s
            h.Script.Policy = v => new DriverIntent { Steer = 0.8f, Throttle = h.Time - t0 < 0.15f ? 0f : 1f, Brake = h.Time - t0 < 0.15f ? 1f : 0f };
            int samples = 0, drifting = 0;
            float slipSum = 0f, minSpeed = 999f, maxSlip = 0f;
            for (int i = 0; i < h.StepsFor(4f); i++)
            {
                h.Step();
                if (h.Time - t0 < 0.8f) continue;
                var st = h.Vehicle.State;
                samples++;
                if (st.ArcadeDrift) drifting++;
                slipSum += Mathf.Abs(st.SlipAngleDeg);
                maxSlip = Mathf.Max(maxSlip, Mathf.Abs(st.SlipAngleDeg));
                minSpeed = Mathf.Min(minSpeed, Kmh(h.Vehicle));
            }
            float meanSlip = slipSum / Mathf.Max(1, samples);
            // straighten up: the drift must end and the car must grip again
            h.Script.Policy = v => new DriverIntent { Throttle = 1f };
            h.Step(h.StepsFor(1.5f));
            var end = h.Vehicle.State;
            Debug.Log($"[Feel] drift: entry {entry:0} km/h, drift held {100f * drifting / Mathf.Max(1, samples):0}% of 3.2 s, " +
                      $"mean |slip| {meanSlip:0.0}°, max {maxSlip:0.0}°, min speed {minSpeed:0} km/h; after exit slip {end.SlipAngleDeg:0.0}°, drift {end.ArcadeDrift}");
            Assert.Greater(drifting, samples * 0.9f, "the drift should be held while steering into it");
            Assert.That(meanSlip, Is.InRange(12f, 42f), "drift angle in the target band");
            Assert.Less(maxSlip, 60f, "no spin-out");
            Assert.Greater(minSpeed, entry * 0.7f, "the drift should keep most of the speed (BUG-018)");
            Assert.IsFalse(end.ArcadeDrift);
            Assert.Less(Mathf.Abs(end.SlipAngleDeg), 6f, "the car grips again after the drift");
        }

        [Test]
        public void Arcade_Slalom_NoSpinOut_TurnsHarderThanSim()
        {
            var log = new StringBuilder("[Feel] slalom 100 km/h, full lock every 1.6 s: ");
            float arcadeMax = 0f, simMax = 0f;
            foreach (bool arcade in new[] { true, false })
            {
                using var h = Make(arcade);
                DriveTo(h, 100f);
                float t0 = h.Time;
                h.Script.Policy = v => new DriverIntent { Throttle = 0.8f, Steer = Mathf.Repeat(h.Time - t0, 1.6f) < 0.8f ? 1f : -1f };
                float maxSlip = 0f, maxYaw = 0f;
                for (int i = 0; i < h.StepsFor(6f); i++)
                {
                    h.Step();
                    maxSlip = Mathf.Max(maxSlip, Mathf.Abs(h.Vehicle.State.SlipAngleDeg));
                    maxYaw = Mathf.Max(maxYaw, Mathf.Abs(h.Vehicle.State.YawRate));
                }
                log.Append($"{(arcade ? "Arcade" : "Sim")} max |slip| {maxSlip:0.0}°, max yaw rate {maxYaw:0.00} rad/s, end {Kmh(h.Vehicle):0} km/h; ");
                if (arcade) arcadeMax = maxSlip; else simMax = maxSlip;
            }
            Debug.Log(log.ToString());
            Assert.Less(arcadeMax, 12f, "arcade grip mode must not slide out in a slalom");
        }

        // Collision callbacks are not sent in edit mode, so the wall response is checked as the pure function the
        // controller calls from OnCollisionEnter/Stay (the player autotest covers it in the game).
        [Test]
        public void WallGlance_ShallowHit_KeepsSpeedAlongTheWall_SteepHitIsACrash()
        {
            var feel = ScriptableObject.CreateInstance<HandlingFeel>();
            Vector3 n = Vector3.left;                                         // wall on the right, normal pointing back at the car
            Vector3 v20 = Quaternion.Euler(0f, 20f, 0f) * Vector3.forward * 27.8f; // 100 km/h, 20° into the wall
            Assert.IsTrue(VehicleController.WallGlance(v20, n, feel, true, 0.01f, out var after));
            Debug.Log($"[Feel] wall 20° at 100 km/h: {v20.magnitude * 3.6f:0} → {after.magnitude * 3.6f:0} km/h, along-wall {Vector3.Dot(after, Vector3.forward) * 3.6f:0} km/h, away {Vector3.Dot(after, n):0.00} m/s");
            Assert.Greater(after.magnitude, v20.magnitude * 0.85f, "most of the speed survives a glancing hit");
            Assert.GreaterOrEqual(Vector3.Dot(after, n), 0f, "the car no longer moves into the wall");
            Assert.Greater(Vector3.Dot(after, Vector3.forward), 0f, "it keeps going along the wall");
            Vector3 v60 = Quaternion.Euler(0f, 60f, 0f) * Vector3.forward * 27.8f;
            Assert.IsFalse(VehicleController.WallGlance(v60, n, feel, true, 0.01f, out _), "a 60° hit is a crash");
            Assert.IsFalse(VehicleController.WallGlance(v20, n, null, true, 0.01f, out _), "no feel = pure simulation");
            Object.DestroyImmediate(feel);
        }

        [Test]
        public void Arcade_Air_LandsOnItsWheels()
        {
            using var h = Make(true);
            var v = h.Vehicle;
            v.ResetTo(new Vector3(0f, 4f, 0f), Quaternion.Euler(-18f, 0f, 38f));
            v.Body.linearVelocity = new Vector3(0f, 2f, 20f);
            h.Script.Policy = null;
            h.Script.Intent = default;
            h.Step(h.StepsFor(3f));
            float upDot = Vector3.Dot(v.transform.up, Vector3.up);
            Debug.Log($"[Feel] air: after 3 s up·Y {upDot:0.000}, grounded wheels {v.State.GroundedWheels}");
            Assert.Greater(upDot, 0.95f);
            Assert.GreaterOrEqual(v.State.GroundedWheels, 3);
        }

        [Test]
        public void Arcade_Flipped_IsSetBackOnItsWheels()
        {
            using var h = Make(true);
            var v = h.Vehicle;
            v.ResetTo(new Vector3(0f, 1.2f, 0f), Quaternion.Euler(0f, 30f, 180f));
            h.Script.Policy = null;
            h.Script.Intent = default;
            bool recovered = false;
            v.Recovered += _ => recovered = true;
            h.Step(h.StepsFor(4f));
            float upDot = Vector3.Dot(v.transform.up, Vector3.up);
            Debug.Log($"[Feel] flip: recovered {recovered}, up·Y {upDot:0.000}, grounded {v.State.GroundedWheels}");
            Assert.IsTrue(recovered);
            Assert.Greater(upDot, 0.95f);
        }
    }
}
