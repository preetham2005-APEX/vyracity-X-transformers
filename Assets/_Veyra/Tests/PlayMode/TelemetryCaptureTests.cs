using System.IO;
using NUnit.Framework;
using UnityEngine;
using VeyraCity.Gameplay;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Tests
{
    /// <summary>
    /// Records full per-step telemetry CSVs for key manoeuvres (VEHICLE_HANDLING_SPEC §6 evidence).
    /// Output: Logs/evidence/physics/telemetry_*.csv
    /// </summary>
    public class TelemetryCaptureTests
    {
        static T Load<T>(string path) where T : Object
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
#else
            return null;
#endif
        }

        static string Out(string name) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "evidence", "physics", $"telemetry_{name}.csv"));

        static void Capture(string name, string vehicleId, string preset, System.Action<VehicleTestHarness> drive)
        {
            var def = Load<VehicleDefinition>($"Assets/_Veyra/Data/Vehicles/{vehicleId}.asset");
            var p = preset == null ? null : Load<VeyraCity.Assists.AssistPreset>($"Assets/_Veyra/Data/Assists/{preset}.asset");
            var dry = Load<SurfaceDefinition>("Assets/_Veyra/Data/Surfaces/dry_asphalt.asset");
            Assert.IsNotNull(def);
            using var h = VehicleTestHarness.Create(def, p, dry, 100f);
            h.Settle();
            using var rec = new TelemetryRecorder(h.Vehicle) { AssistPreset = preset ?? "none", Label = name };
            rec.Start(100f);
            drive(h);
            rec.Stop();
            string path = rec.Save(Out(name));
            Debug.Log($"[Veyra] Telemetry {name}: {rec.Rows} rows → {path}");
            Assert.Greater(rec.Rows, 10);
        }

        [Test]
        public void Capture_StreetLaunch() => Capture("street_0_100", "Kite_Street_RWD", "Casual", h =>
        {
            h.Script.Intent = new DriverIntent { Throttle = 1f };
            h.RunUntil(v => v.State.ForwardSpeed >= 100f / 3.6f, 20f);
        });

        [Test]
        public void Capture_StockBraking() => Capture("stock_100_0", "Kite_Stock_RWD", "Casual", h =>
        {
            h.Vehicle.ForceForwardSpeed(100f / 3.6f);
            h.Vehicle.ForceGear(3);
            h.Script.Intent = new DriverIntent { Brake = 1f };
            h.RunUntil(v => v.State.Speed < 0.3f, 10f);
        });

        [Test]
        public void Capture_StreetDriftTransition() => Capture("street_drift", "Kite_Street_RWD", "Casual", h =>
        {
            // 70 km/h, handbrake flick to initiate, then hold throttle with counter-steer from the assist.
            // Same inputs as matrix test T06.
            h.Vehicle.ForceForwardSpeed(70f / 3.6f);
            h.Vehicle.ForceGear(2);
            h.Script.Intent = new DriverIntent { Steer = -1f, Handbrake = 1f, Throttle = 0.5f };
            h.Step(h.StepsFor(0.45f));
            h.Script.Intent = new DriverIntent { Steer = -0.5f, Throttle = 0.8f };
            h.Step(h.StepsFor(6f));
        });
    }
}
