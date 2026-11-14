using System.Collections.Generic;
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
    /// Realism §1.1 (D2): drive back onto the road from the verge. The ground is the verge (asphalt shoulder, kerb, grass,
    /// dirt, gravel); a slab whose top is <see cref="Step"/> m higher is the road, its edge along z at x = 0. The car starts on
    /// the verge at 10 / 40 / 80 km/h heading at the edge at 15° / 45° / 85°, holds speed and heading, and passes when its
    /// whole body is on the road (x &lt; -3) with three or more wheels down. Run at 50 / 100 / 120 Hz, with the wheel sphere
    /// cast and with the old centre ray (WheelSphereShare 0) for comparison; every case is logged.
    /// </summary>
    public class DriveOffOnTests
    {
        const string KiteDef = "Assets/_Veyra/Data/Vehicles/Kite_Stock_RWD.asset";
        const string CasualPreset = "Assets/_Veyra/Data/Assists/Casual.asset";
        const string Surfaces = "Assets/_Veyra/Data/Surfaces/";
        const float Step = 0.15f;
        static readonly float[] Speeds = { 10f, 40f, 80f };
        static readonly float[] Angles = { 15f, 45f, 85f };
        static readonly string[] Verges = { "dry_asphalt", "kerb", "grass", "dirt", "gravel" };

        struct Outcome { public bool Ok; public float Seconds; public string Why; public float MinNormalY; }

        static Outcome Run(float hz, float kmh, float angleDeg, string verge, float sphereShare, float step = Step)
        {
            var def = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(KiteDef);
            var preset = AssetDatabase.LoadAssetAtPath<AssistPreset>(CasualPreset);
            var vergeSurface = AssetDatabase.LoadAssetAtPath<SurfaceDefinition>(Surfaces + verge + ".asset")
                               ?? AssetDatabase.LoadAssetAtPath<SurfaceDefinition>(Surfaces + "concrete.asset");
            var road = AssetDatabase.LoadAssetAtPath<SurfaceDefinition>(Surfaces + "dry_asphalt.asset");
            if (def == null || preset == null || vergeSurface == null || road == null) Assert.Ignore("vehicle data assets missing");
            float old = VehicleController.WheelSphereShare;
            VehicleController.WheelSphereShare = sphereShare;
            try
            {
                using var h = VehicleTestHarness.Create(def, preset, vergeSurface, hz);
                var slab = h.AddWall(new Vector3(-100f, step - 0.5f, 0f), new Vector3(200f, 1f, 400f), 0f);   // road: x < 0, top at step
                slab.AddComponent<VehicleSurface>().Surface = road;
                float a = angleDeg * Mathf.Deg2Rad;
                var heading = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));   // angleDeg to the edge line (z axis)
                // start so the car reaches the edge after ~15 m of travel along its heading
                Vector3 start = new Vector3(15f * Mathf.Sin(a) + 2f, 0.05f, -15f * Mathf.Cos(a));
                h.Vehicle.ResetTo(start, Quaternion.LookRotation(heading, Vector3.up));
                h.Settle(0.6f);
                float v = kmh / 3.6f;
                h.Vehicle.Body.linearVelocity = heading * v;
                float integral = 0f;
                h.Script.Policy = car =>
                {
                    float e = v - car.State.Speed;
                    integral = Mathf.Clamp(integral + e * h.Dt, -5f, 5f);
                    float u = 0.4f * e + 0.15f * integral;
                    var fwd = Vector3.ProjectOnPlane(car.transform.forward, Vector3.up).normalized;
                    float yawErr = Vector3.SignedAngle(fwd, heading, Vector3.up);
                    return new DriverIntent { Throttle = Mathf.Clamp01(u), Brake = u < -0.3f && car.State.Speed > 1f ? Mathf.Clamp01(-u) : 0f, Steer = Mathf.Clamp(yawErr / 25f, -1f, 1f) };
                };
                float timeout = 20f / Mathf.Max(v, 2f) * 2.5f + 6f;
                float stopped = 0f, minNy = 1f;
                float t = h.RunUntil(car =>
                {
                    for (int w = 0; w < 4; w++) { var ws = car.GetWheel(w); if (ws.Grounded) minNy = Mathf.Min(minNy, ws.ContactNormal.y); }
                    stopped = car.State.Speed < 0.5f ? stopped + h.Dt : 0f;
                    return stopped > 2f || (car.transform.position.x < -3f && car.State.GroundedWheels >= 3);
                }, timeout);
                var p = h.Vehicle.transform.position;
                if (t >= 0f && p.x < -3f) return new Outcome { Ok = true, Seconds = t, MinNormalY = minNy };
                return new Outcome { Ok = false, Seconds = t, MinNormalY = minNy, Why = t < 0f ? $"timeout at x {p.x:0.0}" : $"stopped at x {p.x:0.0}" };
            }
            finally { VehicleController.WheelSphereShare = old; }
        }

        /// <summary>Coordinator check 1: the contact normal the sphere wheel actually reports at 0.15 / 0.20 / 0.25 m steps
        /// (lowest normal.y over all grounded wheels while crossing), the evidence for the 0.35 wall cut-off.</summary>
        [Test]
        public void StepContactNormals_AreAboveTheWallCutoff()
        {
            var log = new StringBuilder("[DriveOn] step contact normals (40 km/h, 85°, grass verge, 100 Hz; wall cut-off normal.y < " + VehicleController.MinGroundNormalY + "):\n");
            foreach (float step in new[] { 0.15f, 0.20f, 0.25f })
            {
                var s = Run(100f, 40f, 85f, "grass", 0.85f, step);
                var r = Run(100f, 40f, 85f, "grass", 0f, step);
                log.Append($"  step {step:0.00} m: sphere min normal.y {s.MinNormalY:0.000} {(s.Ok ? "climbed" : "FAIL " + s.Why)} | ray min normal.y {r.MinNormalY:0.000} {(r.Ok ? "climbed" : "FAIL " + r.Why)}\n");
                Assert.Greater(s.MinNormalY, VehicleController.MinGroundNormalY - 1e-3f, $"step {step} produced a contact the cut-off would treat as a wall");
            }
            Debug.Log(log.ToString());
        }

        [Test]
        public void DriveBackOntoTheRoad_Matrix()
        {
            var log = new StringBuilder($"[DriveOn] step {Step:0.00} m; pass = whole car on the road\n");
            int sphereOk = 0, rayOk = 0, total = 0;
            var required = new List<string>();
            foreach (float hz in new[] { 50f, 100f, 120f })
                foreach (var verge in Verges)
                    foreach (float kmh in Speeds)
                        foreach (float ang in Angles)
                        {
                            var s = Run(hz, kmh, ang, verge, 0.85f);
                            var r = hz == 100f ? Run(hz, kmh, ang, verge, 0f) : default;
                            total++;
                            if (s.Ok) sphereOk++;
                            if (hz == 100f && r.Ok) rayOk++;
                            log.Append($"  {hz,3:0} Hz {verge,-11} {kmh,3:0} km/h {ang,2:0}°: sphere {(s.Ok ? $"ok {s.Seconds:0.0}s" : "FAIL " + s.Why)}");
                            if (hz == 100f) log.Append($" | ray {(r.Ok ? $"ok {r.Seconds:0.0}s" : "FAIL " + r.Why)}");
                            log.Append('\n');
                            // the cases a player must always manage: 40+ km/h at 45° or steeper, on any verge, at any rate
                            if (!s.Ok && kmh >= 40f && ang >= 45f) required.Add($"{hz:0} Hz {verge} {kmh:0} km/h {ang:0}°");
                            // the sphere must never do worse than the ray it replaces
                            if (hz == 100f && r.Ok && !s.Ok) required.Add($"regression vs ray: {verge} {kmh:0} km/h {ang:0}°");
                        }
            log.Append($"  sphere {sphereOk}/{total} (all rates), ray {rayOk}/{total / 3} at 100 Hz");
            Debug.Log(log.ToString());
            Assert.IsEmpty(required, "drive-on failures:\n" + string.Join("\n", required));
        }
    }
}
