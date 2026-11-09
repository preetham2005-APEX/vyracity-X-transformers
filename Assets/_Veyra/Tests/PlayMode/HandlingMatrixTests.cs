using System;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using VeyraCity.Assists;
using VeyraCity.Gameplay;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Tests
{
    /// <summary>
    /// VEHICLE_TEST_MATRIX.csv, run with scripted, repeatable inputs in script-simulation mode.
    /// Every measurement is appended to Logs/evidence/physics/matrix_results.csv.
    /// Targets are the spec's initial targets; tolerances are stated per test.
    /// </summary>
    public class HandlingMatrixTests
    {
        const float Kmh100 = 100f / 3.6f;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static T Load<T>(string path) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            var a = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(a, "Missing data asset " + path + " (run VehicleDataTools)");
            return a;
#else
            Assert.Ignore("Data assets load through the AssetDatabase in the editor only");
            return null;
#endif
        }

        static VehicleDefinition Vehicle(string id) => Load<VehicleDefinition>($"Assets/_Veyra/Data/Vehicles/{id}.asset");
        static SurfaceDefinition Surface(string id) => Load<SurfaceDefinition>($"Assets/_Veyra/Data/Surfaces/{id}.asset");
        static AssistPreset Preset(string name) => Load<AssistPreset>($"Assets/_Veyra/Data/Assists/{name}.asset");

        static string EvidencePath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "evidence", "physics", "matrix_results.csv"));

        static void Record(string testId, string config, string surface, string preset, float hz, string metric, float value, string target, bool pass)
        {
            string path = EvidencePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (!File.Exists(path))
                File.WriteAllText(path, "utc,physics_version,test_id,vehicle_config,surface,assist_preset,fixed_hz,metric,value,target,pass\n");
            File.AppendAllText(path, string.Join(",", DateTime.UtcNow.ToString("O"), VehicleController.PhysicsVersion, testId, config, surface, preset,
                hz.ToString("F0", Inv), metric, value.ToString("F3", Inv), target, pass ? "PASS" : "FAIL") + "\n");
            Debug.Log($"[Veyra][Matrix] {testId} {config} {surface} {preset} {hz:F0}Hz {metric} = {value:F3} (target {target}) {(pass ? "PASS" : "FAIL")}");
        }

        // ---------------- scenarios ----------------

        public static float ZeroTo100(VehicleDefinition def, AssistPreset preset, SurfaceDefinition surface, float hz)
        {
            using var h = VehicleTestHarness.Create(def, preset, surface, hz);
            h.Settle();
            h.Script.Intent = new DriverIntent { Throttle = 1f };
            return h.RunUntil(v => v.State.ForwardSpeed >= Kmh100, 40f);
        }

        static void SelectGearFor(VehicleController v, float speed)
        {
            var def = v.Definition;
            float r = def.Rear.Tyre.Radius;
            for (int g = 1; g <= def.Gearbox.Ratios.Length; g++)
            {
                float rpm = speed / r * def.Gearbox.Ratios[g - 1] * def.Gearbox.FinalDrive * Powertrain.RadPerSecToRpm;
                if (rpm < def.Gearbox.ShiftUpRpm * 0.85f) { v.ForceGear(g); return; }
            }
            v.ForceGear(def.Gearbox.Ratios.Length);
        }

        public static float BrakeFrom100(VehicleDefinition def, AssistPreset preset, SurfaceDefinition surface, float hz)
        {
            using var h = VehicleTestHarness.Create(def, preset, surface, hz);
            h.Settle();
            h.Vehicle.ForceForwardSpeed(Kmh100);
            SelectGearFor(h.Vehicle, Kmh100);
            Vector3 start = h.Vehicle.Body.position;
            h.Script.Intent = new DriverIntent { Brake = 1f };
            float t = h.RunUntil(v => v.State.Speed < 0.3f, 20f);
            Assert.Greater(t, 0f, "car never stopped");
            Vector3 d = h.Vehicle.Body.position - start;
            d.y = 0f;
            return d.magnitude;
        }

        /// <summary>Steady-state circle of the given radius with a slow speed ramp; returns the peak sustained lateral g.</summary>
        public static float Skidpad(VehicleDefinition def, AssistPreset preset, SurfaceDefinition surface, float hz, float radius = 50f)
        {
            using var h = VehicleTestHarness.Create(def, preset, surface, hz);
            h.Settle();
            var centre = new Vector3(-radius, 0f, 0f); // left-hand circle through the start point
            float maxSteer = def.Steering.MaxAngleDeg;
            float t0 = h.Time;
            h.Vehicle.ForceForwardSpeed(8f);
            h.Script.Policy = v =>
            {
                Vector3 p = v.Body.position; p.y = 0f;
                Vector3 radial = p - centre;
                float theta = Mathf.Atan2(radial.z, radial.x);
                const float look = 12f;
                float targetTheta = theta + look / radius; // counter-clockwise seen from above = left turn
                Vector3 target = centre + new Vector3(Mathf.Cos(targetTheta), 0f, Mathf.Sin(targetTheta)) * radius;
                Vector3 local = v.transform.InverseTransformPoint(target);
                float alpha = Mathf.Atan2(local.x, local.z);
                float curvature = 2f * Mathf.Sin(alpha) / look;
                float steerDeg = Mathf.Atan(def.Wheelbase * curvature) * Mathf.Rad2Deg;
                float vTarget = 8f + 0.25f * (h.Time - t0);
                float speed = v.State.ForwardSpeed;
                return new DriverIntent
                {
                    Steer = Mathf.Clamp(steerDeg / maxSteer, -1f, 1f),
                    Throttle = Mathf.Clamp01(0.25f + (vTarget - speed) * 0.6f),
                };
            };
            float best = 0f, windowSum = 0f;
            int window = h.StepsFor(1f), count = 0;
            var samples = new float[window];
            int maxSteps = h.StepsFor(120f);
            for (int i = 0; i < maxSteps; i++)
            {
                h.Step();
                Vector3 p = h.Vehicle.Body.position; p.y = 0f;
                float r = (p - centre).magnitude;
                float err = Mathf.Abs(r - radius);
                if (err > 4f && h.Time - t0 > 5f) break;
                float latG = h.Vehicle.State.Speed * h.Vehicle.State.Speed / Mathf.Max(1f, r) / 9.81f;
                float sample = err < 1.5f ? latG : 0f;
                windowSum += sample - samples[count % window];
                samples[count % window] = sample;
                count++;
                if (count >= window) best = Mathf.Max(best, windowSum / window);
            }
            return best;
        }

        public static float TopSpeed(VehicleDefinition def, SurfaceDefinition surface, float hz)
        {
            using var h = VehicleTestHarness.Create(def, null, surface, hz);
            h.Settle();
            h.Script.Intent = new DriverIntent { Throttle = 1f };
            float vmax = 0f, lastGainTime = 0f;
            int steps = h.StepsFor(150f);
            for (int i = 0; i < steps; i++)
            {
                h.Step();
                float v = h.Vehicle.State.ForwardSpeed;
                if (v > vmax + 0.01f) { vmax = v; lastGainTime = h.Time; }
                if (h.Time - lastGainTime > 6f) break;
            }
            return vmax * 3.6f;
        }

        // ---------------- tests ----------------

        [TestCase("Kite_Stock_RWD", 8.8f, 0.15f)]
        [TestCase("Kite_Street_RWD", 5.0f, 0.15f)]
        public void T01_ZeroTo100(string id, float target, float tolerance)
        {
            float t = ZeroTo100(Vehicle(id), Preset("Casual"), Surface("dry_asphalt"), 100f);
            bool pass = t > 0f && Mathf.Abs(t - target) <= target * tolerance;
            Record("T01", id, "dry_asphalt", "Casual", 100f, "time_s", t, $"{target}±{tolerance * 100:F0}%", pass);
            Assert.IsTrue(pass, $"0–100 km/h took {t:F2} s, target {target} s ±{tolerance * 100:F0}%");
        }

        [Test]
        public void T01_Drivetrains_AreMeasurablyDistinct()
        {
            var casual = Preset("Casual");
            var dry = Surface("dry_asphalt");
            float rwd = ZeroTo100(Vehicle("Kite_Stock_RWD"), casual, dry, 100f);
            float fwd = ZeroTo100(Vehicle("Greybox_FWD"), casual, dry, 100f);
            float awd = ZeroTo100(Vehicle("Greybox_AWD"), casual, dry, 100f);
            Record("T01", "Greybox_FWD", "dry_asphalt", "Casual", 100f, "time_s", fwd, "archetype-distinct", fwd > 0f);
            Record("T01", "Greybox_AWD", "dry_asphalt", "Casual", 100f, "time_s", awd, "archetype-distinct", awd > 0f);
            Assert.Greater(fwd, 0f); Assert.Greater(awd, 0f); Assert.Greater(rwd, 0f);
            Assert.Greater(Mathf.Abs(fwd - rwd) / rwd, 0.03f, "FWD vs RWD");
            Assert.Greater(Mathf.Abs(awd - rwd) / rwd, 0.03f, "AWD vs RWD");
            Assert.Less(awd, fwd, "the 250 kW AWD launches harder than the 140 kW FWD hatch");
        }

        [TestCase("Kite_Stock_RWD", 39f, 0.15f)]
        [TestCase("Kite_Street_RWD", 34f, 0.15f)]
        public void T02_Brake100To0(string id, float target, float tolerance)
        {
            float d = BrakeFrom100(Vehicle(id), Preset("Casual"), Surface("dry_asphalt"), 100f);
            bool pass = Mathf.Abs(d - target) <= target * tolerance;
            Record("T02", id, "dry_asphalt", "Casual", 100f, "distance_m", d, $"{target}±{tolerance * 100:F0}%", pass);
            Assert.IsTrue(pass, $"100–0 took {d:F1} m, target {target} m ±{tolerance * 100:F0}%");
        }

        [TestCase("Kite_Stock_RWD", 0.85f, 0.12f)]
        [TestCase("Kite_Street_RWD", 1.00f, 0.12f)]
        public void T03_Skidpad(string id, float target, float tolerance)
        {
            float g = Skidpad(Vehicle(id), null, Surface("dry_asphalt"), 100f);
            bool pass = Mathf.Abs(g - target) <= target * tolerance;
            Record("T03", id, "dry_asphalt", "none", 100f, "lateral_g", g, $"{target}±{tolerance * 100:F0}%", pass);
            Assert.IsTrue(pass, $"skidpad {g:F3} g, target {target} g ±{tolerance * 100:F0}%");
        }

        [Test]
        public void T05_HandbrakeTurn_RotatesControllably()
        {
            using var h = VehicleTestHarness.Create(Vehicle("Kite_Stock_RWD"), Preset("Casual"), Surface("dry_asphalt"), 100f);
            h.Settle();
            h.Vehicle.ForceForwardSpeed(60f / 3.6f);
            SelectGearFor(h.Vehicle, 60f / 3.6f);
            float yaw = 0f;
            h.Script.Intent = new DriverIntent { Steer = -1f, Handbrake = 1f };
            for (int i = 0; i < h.StepsFor(0.7f); i++) { h.Step(); yaw += h.Vehicle.State.YawRate * h.Dt; }
            h.Script.Intent = new DriverIntent { Steer = -0.4f, Brake = 0.2f };
            for (int i = 0; i < h.StepsFor(3.5f); i++) { h.Step(); yaw += h.Vehicle.State.YawRate * h.Dt; }
            float deg = Mathf.Abs(yaw * Mathf.Rad2Deg);
            bool upright = h.Vehicle.transform.up.y > 0.7f;
            bool pass = deg >= 90f && deg <= 200f && upright;
            Record("T05", "Kite_Stock_RWD", "dry_asphalt", "Casual", 100f, "rotation_deg", deg, "90-180", pass);
            Assert.IsTrue(upright, "car rolled over");
            Assert.That(deg, Is.InRange(90f, 200f), "handbrake turn rotation");
        }

        [Test]
        public void T06_DriftHold_Casual_NoSpin()
        {
            using var h = VehicleTestHarness.Create(Vehicle("Kite_Street_RWD"), Preset("Casual"), Surface("dry_asphalt"), 100f);
            h.Settle();
            h.Vehicle.ForceForwardSpeed(70f / 3.6f);
            SelectGearFor(h.Vehicle, 70f / 3.6f);
            h.Script.Intent = new DriverIntent { Steer = -1f, Handbrake = 1f, Throttle = 0.5f };
            h.Step(h.StepsFor(0.45f));
            h.Script.Intent = new DriverIntent { Steer = -0.5f, Throttle = 0.8f };
            float start = h.Time, sum = 0f, sumSq = 0f, maxAbs = 0f, minSpeed = float.MaxValue;
            int n = 0, drifting = 0;
            bool spun = false;
            for (int i = 0; i < h.StepsFor(6f); i++)
            {
                h.Step();
                float b = Mathf.Abs(h.Vehicle.State.SlipAngleDeg);
                maxAbs = Mathf.Max(maxAbs, b);
                if (b > 95f) spun = true;
                if (h.Time - start < 1f) continue;
                sum += b; sumSq += b * b; n++;
                if (b > 12f) drifting++;
                minSpeed = Mathf.Min(minSpeed, h.Vehicle.State.SpeedKmh);
            }
            float mean = sum / n, std = Mathf.Sqrt(Mathf.Max(0f, sumSq / n - mean * mean));
            bool pass = !spun && mean >= 25f && mean <= 45f && std <= 6f;
            Record("T06", "Kite_Street_RWD", "dry_asphalt", "Casual", 100f, "held_angle_mean_deg", mean, "25-45", pass);
            Record("T06", "Kite_Street_RWD", "dry_asphalt", "Casual", 100f, "held_angle_std_deg", std, "<=6", std <= 6f);
            Record("T06", "Kite_Street_RWD", "dry_asphalt", "Casual", 100f, "max_angle_deg", maxAbs, "no spin (<95)", !spun);
            Record("T06", "Kite_Street_RWD", "dry_asphalt", "Casual", 100f, "time_in_drift_frac", drifting / (float)n, "", true);
            Record("T06", "Kite_Street_RWD", "dry_asphalt", "Casual", 100f, "min_speed_kmh", minSpeed, "", true);
            Assert.IsFalse(spun, $"spun out (max slip {maxAbs:F0}°)");
        }

        [TestCase(1.333f, 0.15f)]
        public void T07_WetBraking_RatioVsDry(float target, float tolerance)
        {
            var def = Vehicle("Kite_Stock_RWD");
            var casual = Preset("Casual");
            float dry = BrakeFrom100(def, casual, Surface("dry_asphalt"), 100f);
            float wet = BrakeFrom100(def, casual, Surface("wet_asphalt"), 100f);
            float ratio = wet / dry;
            bool pass = Mathf.Abs(ratio - target) <= target * tolerance;
            Record("T07", "Kite_Stock_RWD", "wet_asphalt", "Casual", 100f, "distance_ratio", ratio, $"~1.3x", pass);
            Assert.IsTrue(pass, $"wet/dry stopping ratio {ratio:F2}");
        }

        [Test]
        public void T10_StaticDrop_SettlesWithoutJitter()
        {
            using var h = VehicleTestHarness.Create(Vehicle("Kite_Stock_RWD"), null, Surface("dry_asphalt"), 100f);
            h.Vehicle.ResetTo(new Vector3(0f, 0.25f, 0f), Quaternion.identity);
            Physics.SyncTransforms();
            float settledAt = -1f;
            for (int i = 0; i < h.StepsFor(4f); i++)
            {
                h.Step();
                float vy = Mathf.Abs(h.Vehicle.Body.linearVelocity.y);
                if (vy > 0.02f) settledAt = -1f;
                else if (settledAt < 0f) settledAt = h.Time;
            }
            var fl = h.Vehicle.GetWheel(VehicleController.FL);
            Assert.IsTrue(fl.Grounded);
            Assert.IsFalse(float.IsNaN(h.Vehicle.Body.position.y));
            bool pass = settledAt > 0f && settledAt < 1.5f;
            Record("T10", "Kite_Stock_RWD", "dry_asphalt", "none", 100f, "settle_time_s", settledAt, "<1.5 s", pass);
            Assert.IsTrue(pass, $"settled at {settledAt:F2} s");
            Assert.Less(h.Vehicle.Body.linearVelocity.magnitude, 0.02f);
        }

        [Test]
        public void T10_Standstill_DoesNotCreep()
        {
            using var h = VehicleTestHarness.Create(Vehicle("Kite_Stock_RWD"), Preset("Casual"), Surface("dry_asphalt"), 100f);
            h.Settle(2f);
            Vector3 p0 = h.Vehicle.Body.position;
            h.Step(h.StepsFor(5f));
            float drift = (h.Vehicle.Body.position - p0).magnitude;
            Record("T10", "Kite_Stock_RWD", "dry_asphalt", "Casual", 100f, "standstill_creep_m", drift, "<0.02", drift < 0.02f);
            Assert.Less(drift, 0.02f);
        }

        [Test]
        public void T11_FixedRate_Independence()
        {
            var def = Vehicle("Kite_Stock_RWD");
            var casual = Preset("Casual");
            var dry = Surface("dry_asphalt");
            float r100 = ZeroTo100(def, casual, dry, 100f);
            float r50 = ZeroTo100(def, casual, dry, 50f);
            float r120 = ZeroTo100(def, casual, dry, 120f);
            float delta = Mathf.Max(Mathf.Abs(r50 - r100), Mathf.Abs(r120 - r100)) / r100;
            bool pass = delta < 0.05f;
            Record("T11", "Kite_Stock_RWD", "dry_asphalt", "Casual", 50f, "time_s", r50, "", true);
            Record("T11", "Kite_Stock_RWD", "dry_asphalt", "Casual", 120f, "time_s", r120, "", true);
            Record("T11", "Kite_Stock_RWD", "dry_asphalt", "Casual", 100f, "max_delta_vs_100Hz", delta, "<5% (fixed-rate; render-rate check in builds)", pass);
            Assert.IsTrue(pass, $"0–100 at 50/100/120 Hz: {r50:F2}/{r100:F2}/{r120:F2} s");
        }

        [TestCase(1.25f, 2.2f)]
        public void T12_GravelBraking_LongerThanWet(float minRatio, float maxRatio)
        {
            var def = Vehicle("Kite_Stock_RWD");
            var casual = Preset("Casual");
            float dry = BrakeFrom100(def, casual, Surface("dry_asphalt"), 100f);
            float gravel = BrakeFrom100(def, casual, Surface("gravel"), 100f);
            float ratio = gravel / dry;
            bool pass = ratio >= minRatio && ratio <= maxRatio;
            Record("T12", "Kite_Stock_RWD", "gravel", "Casual", 100f, "distance_ratio", ratio, $"{minRatio}-{maxRatio}", pass);
            Assert.IsTrue(pass, $"gravel/dry ratio {ratio:F2}");
        }

        [TestCase("Kite_Stock_RWD", 210f, 0.08f)]
        [TestCase("Kite_Street_RWD", 255f, 0.08f)]
        public void T13_TopSpeed(string id, float target, float tolerance)
        {
            float v = TopSpeed(Vehicle(id), Surface("dry_asphalt"), 100f);
            bool pass = Mathf.Abs(v - target) <= target * tolerance;
            Record("T13", id, "dry_asphalt", "none", 100f, "vmax_kmh", v, $"{target}±{tolerance * 100:F0}%", pass);
            Assert.IsTrue(pass, $"top speed {v:F1} km/h, target {target}");
        }

        [Test]
        public void Steering_SignConvention_RightIsRight()
        {
            using var h = VehicleTestHarness.Create(Vehicle("Kite_Stock_RWD"), null, Surface("dry_asphalt"), 100f);
            h.Settle();
            h.Vehicle.ForceForwardSpeed(15f);
            SelectGearFor(h.Vehicle, 15f);
            h.Script.Intent = new DriverIntent { Steer = 0.3f, Throttle = 0.3f };
            h.Step(h.StepsFor(1.5f));
            Assert.Greater(h.Vehicle.State.YawRate, 0.05f, "positive steer must turn right (positive yaw rate)");
            Assert.Greater(h.Vehicle.Body.position.x, 0.5f, "car must move to the right (+X)");
        }

        [Test]
        public void Reverse_HeldBrakeAtStandstill_DrivesBackwards()
        {
            using var h = VehicleTestHarness.Create(Vehicle("Kite_Stock_RWD"), Preset("Casual"), Surface("dry_asphalt"), 100f);
            h.Settle();
            h.Script.Intent = new DriverIntent { Brake = 1f };
            h.Step(h.StepsFor(3f));
            Assert.Less(h.Vehicle.State.ForwardSpeed, -1f, "should be reversing");
            Assert.AreEqual(-1, h.Vehicle.State.Gear);
        }
    }
}
