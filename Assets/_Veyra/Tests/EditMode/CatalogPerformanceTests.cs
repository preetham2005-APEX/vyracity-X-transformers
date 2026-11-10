using System.IO;
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
    /// Straight-line performance of every car in the GameDatabase on flat dry asphalt with the Arcade feel and the
    /// Casual assists: 0–100 km/h, speed after 30 s, 100–0 km/h braking distance. Writes Logs/A8/catalog_performance.txt.
    /// </summary>
    public class CatalogPerformanceTests
    {
        [Test]
        public void EveryCar_Accelerates_TopsOut_AndBrakes()
        {
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/_Veyra/Data/GameDatabase.asset");
            var preset = AssetDatabase.LoadAssetAtPath<AssistPreset>("Assets/_Veyra/Data/Assists/Casual.asset");
            var surface = AssetDatabase.LoadAssetAtPath<SurfaceDefinition>("Assets/_Veyra/Data/Surfaces/dry_asphalt.asset");
            if (db == null || preset == null || surface == null) Assert.Ignore("data assets missing");
            var sb = new StringBuilder("car | layout | kg | 0-100 km/h s | km/h after 30 s | 100-0 m\n");
            int failures = 0;
            foreach (var c in db.Cars)
            {
                if (c.Definition == null) continue;
                float t100, v30, brake;
                using (var h = VehicleTestHarness.Create(c.Definition, preset, surface, 100f, db.Feel))
                {
                    h.Settle();
                    h.Script.Policy = v => new DriverIntent { Throttle = 1f };
                    t100 = h.RunUntil(v => v.State.SpeedKmh >= 100f, 30f);
                    h.Step(h.StepsFor(Mathf.Max(0f, 30f - h.Time)));
                    v30 = h.Vehicle.State.SpeedKmh;
                }
                using (var h = VehicleTestHarness.Create(c.Definition, preset, surface, 100f, db.Feel))
                {
                    h.Settle();
                    h.Vehicle.ForceForwardSpeed(100f / 3.6f);
                    var p0 = h.Vehicle.transform.position;
                    h.Script.Policy = v => new DriverIntent { Brake = 1f };
                    h.RunUntil(v => v.State.ForwardSpeed < 0.3f, 15f);
                    brake = Vector3.Distance(p0, h.Vehicle.transform.position);
                }
                bool slow = t100 < 0f || t100 > 16f;
                bool truck = c.Definition.Chassis.Mass > 3000f || c.Definition.Chassis.Mass < 800f;
                if ((slow && !truck) || brake > 60f) failures++;
                sb.AppendLine($"{c.Id} | {c.Definition.Layout} | {c.Definition.Chassis.Mass:0} | {(t100 < 0f ? "n/a" : t100.ToString("0.0"))} | {v30:0} | {brake:0.0}");
            }
            Directory.CreateDirectory("Logs/A8");
            File.WriteAllText("Logs/A8/catalog_performance.txt", sb.ToString());
            Debug.Log("[Catalog]\n" + sb);
            Assert.AreEqual(0, failures, "cars too slow (0-100 over 16 s) or braking over 60 m:\n" + sb);
        }

        /// <summary>
        /// Cornering of every car (Arcade feel, Casual assists, flat dry asphalt), compared against the arcade references:
        /// skidpad lateral grip from a slow full-lock ramp at 80 km/h (GTA V sports cars hold roughly 1.1–1.3 g, Asphalt's
        /// cars more; real road cars 0.85–1.05 g), turn-in time to 90 % of the steady yaw rate after a 30 % step at 100 km/h
        /// (arcade target well under 0.5 s), and whether a full-lock ramp spins the car (it must not with Casual assists).
        /// Writes Logs/A8/handling_benchmark.txt.
        /// </summary>
        [Test]
        public void EveryCar_Corners_TurnsIn_AndStaysPointed()
        {
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/_Veyra/Data/GameDatabase.asset");
            var preset = AssetDatabase.LoadAssetAtPath<AssistPreset>("Assets/_Veyra/Data/Assists/Casual.asset");
            var surface = AssetDatabase.LoadAssetAtPath<SurfaceDefinition>("Assets/_Veyra/Data/Surfaces/dry_asphalt.asset");
            if (db == null || preset == null || surface == null) Assert.Ignore("data assets missing");
            var sb = new StringBuilder("car | kg | skidpad g @80 | slip deg at peak | spun | turn-in 90% s @100 | steady yaw deg/s | lift-off g @180\n");
            int failures = 0;
            foreach (var c in db.Cars)
            {
                if (c.Definition == null) continue;
                float peakG = 0f, slipAtPeak = 0f, maxSlip = 0f;
                using (var h = VehicleTestHarness.Create(c.Definition, preset, surface, 100f, db.Feel))
                {
                    h.Settle();
                    h.Vehicle.ForceForwardSpeed(80f / 3.6f);
                    float t0 = h.Time;
                    h.Script.Policy = v => new DriverIntent
                    {
                        Steer = Mathf.Clamp01((h.Time - t0) / 5f),
                        Throttle = Mathf.Clamp01((80f - v.State.SpeedKmh) / 10f + 0.3f),
                    };
                    int n = h.StepsFor(6f);
                    for (int i = 0; i < n; i++)
                    {
                        h.Step();
                        var st = h.Vehicle.State;
                        maxSlip = Mathf.Max(maxSlip, Mathf.Abs(st.SlipAngleDeg));
                        if (Mathf.Abs(st.LateralG) > peakG) { peakG = Mathf.Abs(st.LateralG); slipAtPeak = st.SlipAngleDeg; }
                    }
                }
                bool spun = maxSlip > 35f;
                float turnIn = -1f, steadyYaw;
                using (var h = VehicleTestHarness.Create(c.Definition, preset, surface, 100f, db.Feel))
                {
                    h.Settle();
                    h.Vehicle.ForceForwardSpeed(100f / 3.6f);
                    h.Script.Policy = v => new DriverIntent { Steer = 0.3f, Throttle = Mathf.Clamp01((100f - v.State.SpeedKmh) / 10f + 0.3f) };
                    float t0 = h.Time;
                    var yaw = new System.Collections.Generic.List<(float t, float y)>();
                    int n = h.StepsFor(2.5f);
                    for (int i = 0; i < n; i++) { h.Step(); yaw.Add((h.Time - t0, Mathf.Abs(h.Vehicle.State.YawRate))); }
                    steadyYaw = 0f;
                    int k0 = yaw.Count * 4 / 5;
                    for (int i = k0; i < yaw.Count; i++) steadyYaw += yaw[i].y;
                    steadyYaw /= Mathf.Max(1, yaw.Count - k0);
                    foreach (var (t, y) in yaw) if (y >= 0.9f * steadyYaw) { turnIn = t; break; }
                }
                // Lift-off: coast from 180 km/h for one second (BUG-079: drag-limited cars braked at 0.4 g).
                float liftG;
                using (var h = VehicleTestHarness.Create(c.Definition, preset, surface, 100f, db.Feel))
                {
                    h.Settle();
                    h.Vehicle.ForceForwardSpeed(180f / 3.6f);
                    h.Script.Policy = v => new DriverIntent();
                    h.Step(h.StepsFor(0.2f));
                    float v0 = h.Vehicle.State.Speed;
                    h.Step(h.StepsFor(1f));
                    liftG = (v0 - h.Vehicle.State.Speed) / 9.81f;
                }
                bool truck = c.Definition.Chassis.Mass > 3000f;
                bool bad = (!truck && liftG > 0.3f) || spun || (!truck && (peakG < 0.75f || peakG > 1.7f)) || turnIn < 0f || turnIn > 0.9f;
                if (bad) failures++;
                sb.AppendLine($"{c.Id} | {c.Definition.Chassis.Mass:0} | {peakG:0.00} | {slipAtPeak:0.0} | {(spun ? "SPUN" : "no")} | {turnIn:0.00} | {steadyYaw * Mathf.Rad2Deg:0.0} | {liftG:0.00}{(bad ? "  <-- out of band" : "")}");
            }
            Directory.CreateDirectory("Logs/A8");
            File.WriteAllText("Logs/A8/handling_benchmark.txt", sb.ToString());
            Debug.Log("[Handling]\n" + sb);
            Assert.AreEqual(0, failures, "cars outside the arcade handling band:\n" + sb);
        }

        /// <summary>
        /// Regression (visual iteration 9): a car spawned 0.45 m above the road at full throttle (the game's spawn height)
        /// spun its wheels in the drop, the automatic upshifted to 2nd on the free-spinning wheels, and traction control's
        /// 25 % floor kept slip near 7 for ~2.5 s (in-game 0-100 km/h over 7 s for a 5 s car). The dropped launch must now
        /// reach 100 km/h within 0.8 s of a settled launch.
        /// </summary>
        [Test]
        public void DroppedLaunch_DoesNotBogDownInWheelspin()
        {
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/_Veyra/Data/GameDatabase.asset");
            var preset = AssetDatabase.LoadAssetAtPath<AssistPreset>("Assets/_Veyra/Data/Assists/Casual.asset");
            var surface = AssetDatabase.LoadAssetAtPath<SurfaceDefinition>("Assets/_Veyra/Data/Surfaces/dry_asphalt.asset");
            if (db == null || preset == null || surface == null) Assert.Ignore("data assets missing");
            var sb = new StringBuilder();
            int failures = 0;
            foreach (var id in new[] { "stanton_sidewinder", "phoenix_455", "kiri_86", "v12_goblin", "coupe_33_hotrod_1" })
            {
                var c = System.Array.Find(db.Cars, x => x.Id == id);
                if (c == null || c.Definition == null) continue;
                float settled, dropped;
                using (var h = VehicleTestHarness.Create(c.Definition, preset, surface, 100f, db.Feel))
                {
                    h.Settle();
                    h.Script.Policy = v => new DriverIntent { Throttle = 1f };
                    settled = h.RunUntil(v => v.State.SpeedKmh >= 100f, 30f);
                }
                using (var h = VehicleTestHarness.Create(c.Definition, preset, surface, 100f, db.Feel))
                {
                    var pos = new Vector3(0f, 0.45f, 0f);
                    h.Vehicle.transform.position = pos;
                    h.Vehicle.Body.position = pos;
                    Physics.SyncTransforms();
                    h.Script.Policy = v => new DriverIntent { Throttle = 1f };
                    dropped = h.RunUntil(v => v.State.SpeedKmh >= 100f, 30f);
                }
                bool bad = settled < 0f || dropped < 0f || dropped > settled + 0.8f;
                if (bad) failures++;
                sb.AppendLine($"{id}: settled {settled:0.00} s, dropped {dropped:0.00} s{(bad ? "  <-- bogged down" : "")}");
            }
            Debug.Log("[Launch]\n" + sb);
            Assert.AreEqual(0, failures, sb.ToString());
        }
    }
}
