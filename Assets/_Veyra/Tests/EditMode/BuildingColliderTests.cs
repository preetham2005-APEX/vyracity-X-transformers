using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VeyraCity.Assists;
using VeyraCity.Gameplay;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Tests
{
    /// <summary>
    /// Card C3: no tunnelling into buildings up to 200 km/h. The Kite (with the game's collision mode for the player car,
    /// ContinuousDynamic, VehicleFactory) is launched at 150 and 200 km/h into a 0.2 m storefront-thin wall and into a 20 m
    /// building block; its centre must never pass the wall face.
    /// </summary>
    public class BuildingColliderTests
    {
        [TestCase(150f, 0.2f)]
        [TestCase(200f, 0.2f)]
        [TestCase(200f, 20f)]
        public void Car_DoesNotTunnelThroughWall(float kmh, float thickness)
        {
            var def = AssetDatabase.LoadAssetAtPath<VehicleDefinition>("Assets/_Veyra/Data/Vehicles/Kite_Stock_RWD.asset");
            var preset = AssetDatabase.LoadAssetAtPath<AssistPreset>("Assets/_Veyra/Data/Assists/Casual.asset");
            var surface = AssetDatabase.LoadAssetAtPath<SurfaceDefinition>("Assets/_Veyra/Data/Surfaces/dry_asphalt.asset");
            if (def == null || preset == null || surface == null) Assert.Ignore("vehicle data assets missing");
            using var h = VehicleTestHarness.Create(def, preset, surface, 100f);
            h.Settle(1f);
            const float face = 25f;
            h.AddWall(new Vector3(0f, 2f, face + thickness / 2f), new Vector3(30f, 4f, thickness), 0f);
            var rb = h.Vehicle.GetComponent<Rigidbody>();
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = h.Vehicle.transform.forward * (kmh / 3.6f);
            h.Script.Policy = v => new DriverIntent { Throttle = 1f };
            float maxZ = float.MinValue;
            for (int i = 0; i < h.StepsFor(2f); i++)
            {
                h.Step();
                maxZ = Mathf.Max(maxZ, h.Vehicle.transform.position.z);
            }
            Debug.Log($"[C3] {kmh} km/h into a {thickness} m wall: max centre z {maxZ:F2} m (wall face {face} m), final speed {h.Vehicle.State.Speed * 3.6f:F0} km/h");
            Assert.Less(maxZ, face, $"car centre passed the wall face at {kmh} km/h ({thickness} m wall)");
        }
    }
}
