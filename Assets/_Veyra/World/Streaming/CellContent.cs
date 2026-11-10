using UnityEngine;

namespace VeyraCity.World.Streaming
{
    /// <summary>
    /// Card W7 time slicing: on the root of each generated cell scene. The distribution tool saves the cell's objects inactive
    /// and lists them here, so loading a cell does not wake (Awake, physics, renderers) everything in one frame:
    /// <see cref="CellStreamer"/> activates <see cref="Objects"/> a few at a time within a per-frame budget, and deactivates
    /// them the same way before the scene unloads. The boot loader activates them all at once behind the loading screen.
    /// </summary>
    public sealed class CellContent : MonoBehaviour
    {
        public string Cell;
        [Tooltip("Objects that were active in the authoring scene, in activation order (terrain and drive surface first)")]
        public GameObject[] Objects = new GameObject[0];

        public void ActivateAll()
        {
            foreach (var o in Objects) if (o != null && !o.activeSelf) o.SetActive(true);
        }
    }
}
