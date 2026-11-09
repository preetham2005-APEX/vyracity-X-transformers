using System;
using UnityEngine;
using VeyraCity.Assists;
using VeyraCity.VehiclePhysics;
using Object = UnityEngine.Object;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Runs a vehicle in script-simulation mode on a flat ground plane, stepping driver → vehicle → PhysX
    /// manually at a chosen fixed rate. Results are repeatable and independent of the render frame
    /// rate, and a 30 s test completes in well under a second.
    /// </summary>
    public sealed class VehicleTestHarness : IDisposable
    {
        public VehicleController Vehicle { get; }
        public VehicleDriver Driver { get; }
        public ScriptedDriver Script { get; } = new ScriptedDriver();
        public float Dt { get; }
        public float Time { get; private set; }

        readonly GameObject ground;
        readonly SimulationMode previousMode;
        bool disposed;

        VehicleTestHarness(VehicleDefinition def, AssistPreset preset, SurfaceDefinition surface, float fixedHz, HandlingFeel feel = null)
        {
            Dt = 1f / Mathf.Max(10f, fixedHz);
            previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;

            ground = new GameObject("TestGround");
            var box = ground.AddComponent<BoxCollider>();
            box.size = new Vector3(20000f, 1f, 20000f);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.AddComponent<VehicleSurface>().Surface = surface;

            Vehicle = VehicleFactory.Create(def, null, surface, new Vector3(0f, 0.05f, 0f), Quaternion.identity, interpolate: false);
            Vehicle.ManualStepping = true;
            Vehicle.Feel = feel;
            Script.Vehicle = Vehicle;
            Driver = Vehicle.gameObject.AddComponent<VehicleDriver>();
            Driver.Bind(Vehicle, Script, preset);
            Physics.SyncTransforms();
        }

        public static VehicleTestHarness Create(VehicleDefinition def, AssistPreset preset, SurfaceDefinition surface, float fixedHz = 100f, HandlingFeel feel = null)
            => new VehicleTestHarness(def, preset, surface, fixedHz, feel);

        /// <summary>Adds a static wall (no Rigidbody) for collision tests: centre, size and yaw in degrees.</summary>
        public GameObject AddWall(Vector3 centre, Vector3 size, float yawDeg)
        {
            var wall = new GameObject("TestWall");
            wall.transform.SetPositionAndRotation(centre, Quaternion.Euler(0f, yawDeg, 0f));
            wall.AddComponent<BoxCollider>().size = size;
            wall.transform.SetParent(ground.transform, true);
            Physics.SyncTransforms();
            return wall;
        }

        public void Step(int steps = 1)
        {
            for (int i = 0; i < steps; i++)
            {
                Driver.Step(Dt);
                Vehicle.Simulate(Dt);
                Physics.Simulate(Dt);
                Time += Dt;
            }
        }

        public int StepsFor(float seconds) => Mathf.Max(1, Mathf.RoundToInt(seconds / Dt));

        /// <summary>Lets the car settle on its springs with no input.</summary>
        public void Settle(float seconds = 1.5f)
        {
            Script.Policy = null;
            Script.Intent = default;
            Step(StepsFor(seconds));
        }

        /// <summary>Runs until the predicate is true or the timeout elapses. Returns the elapsed seconds, or -1 on timeout.</summary>
        public float RunUntil(Func<VehicleController, bool> done, float timeoutSeconds)
        {
            float start = Time;
            int max = StepsFor(timeoutSeconds);
            for (int i = 0; i < max; i++)
            {
                Step();
                if (done(Vehicle)) return Time - start;
            }
            return -1f;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (Vehicle != null) Object.DestroyImmediate(Vehicle.gameObject);
            if (ground != null) Object.DestroyImmediate(ground);
            Physics.simulationMode = previousMode;
        }
    }
}
