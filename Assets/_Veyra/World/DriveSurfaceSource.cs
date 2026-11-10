using UnityEngine;

namespace VeyraCity.World
{
    /// <summary>
    /// BUG-108: marks a road collider whose triangles inside a carriageway were removed by the editor's DriveSurface pass
    /// (the drive surface replaces them). Keeps the original collision mesh so the pass can restore it and run again
    /// (idempotent). No runtime behaviour.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DriveSurfaceSource : MonoBehaviour
    {
        [Tooltip("The collision mesh before the drive-surface clip")] public Mesh Original;
        [Tooltip("The collider was enabled before the clip (a collider left with no triangles is disabled)")] public bool WasEnabled = true;
    }
}
