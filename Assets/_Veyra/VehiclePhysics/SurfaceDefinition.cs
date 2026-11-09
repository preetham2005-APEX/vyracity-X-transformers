using UnityEngine;

namespace VeyraCity.VehiclePhysics
{
    /// <summary>Grip and rolling resistance of a driving surface (VEHICLE_HANDLING_SPEC §2).</summary>
    [CreateAssetMenu(menuName = "Veyra/Surface Definition", fileName = "Surface")]
    public sealed class SurfaceDefinition : ScriptableObject
    {
        [Tooltip("Stable id, e.g. dry_asphalt")] public string Id = "dry_asphalt";
        [Tooltip("Multiplier on tyre friction")] public float GripMultiplier = 1f;
        [Tooltip("Multiplier on rolling resistance")] public float RollingResistanceMultiplier = 1f;
    }
}
