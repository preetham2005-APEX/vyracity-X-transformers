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
    }
}
