using UnityEngine;

namespace VeyraCity.World.Streaming
{
    /// <summary>
    /// Card W7 HLOD: the far proxy of one cell (merged low-resolution terrain, building masses and road ribbons, built by
    /// Veyra/World/Distribute To Cells). <see cref="CellStreamer"/> shows it only while that cell's scene is not loaded.
    /// </summary>
    public sealed class CellProxy : MonoBehaviour
    {
        [Tooltip("Cell index = column + row * 8 (row 0 = grid row 1, north)")]
        public int Index = -1;
        public string Cell;
        public int Triangles;
    }
}
