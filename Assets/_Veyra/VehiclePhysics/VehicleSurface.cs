using UnityEngine;

namespace VeyraCity.VehiclePhysics
{
    /// <summary>Marks a collider (or its children) as a driving surface.</summary>
    [DisallowMultipleComponent]
    public sealed class VehicleSurface : MonoBehaviour
    {
        public SurfaceDefinition Surface;
    }
}
