using System;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Deterministic driver for automated tests and demos: either a fixed intent or a function of
    /// (time, state). Repeatable input is a requirement of PHYSICS_TEST_REPORT.md.
    /// </summary>
    public sealed class ScriptedDriver : IDriverIntentSource
    {
        public DriverIntent Intent;
        public Func<VehicleController, DriverIntent> Policy;
        public VehicleController Vehicle;

        public DriverIntent ReadIntent()
        {
            var i = Policy != null && Vehicle != null ? Policy(Vehicle) : Intent;
            Intent.ShiftUp = Intent.ShiftDown = false;
            return i;
        }
    }
}
