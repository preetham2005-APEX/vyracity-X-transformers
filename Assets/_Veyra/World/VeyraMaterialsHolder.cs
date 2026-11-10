using UnityEngine;

namespace VeyraCity.World
{
    /// <summary>
    /// Activates the <see cref="VeyraMaterialLibrary"/> in scenes that have neither GameFlow nor WorldEnvironment (test track,
    /// foundation scene), so runtime-created effects find their materials there too. Added by Veyra/Materials/Build Material Library.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class VeyraMaterialsHolder : MonoBehaviour
    {
        [SerializeField] VeyraMaterialLibrary materials;

        public VeyraMaterialLibrary Materials { get => materials; set => materials = value; }

        void Awake() => VeyraMaterialLibrary.Activate(materials, this);
    }
}
