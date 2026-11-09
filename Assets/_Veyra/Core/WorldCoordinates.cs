using UnityEngine;

namespace VeyraCity.Core
{
    /// <summary>
    /// Canonical Veyra world coordinates (from VEYRA_CITY_WORLD_DESIGN): metres from the south-west corner of the
    /// 8 x 6 km world, x east, y north, z = elevation. Unity: X = x - 4000, Y = elevation, Z = y - 3000 (+Z is north).
    /// Map grid: 1 km cells A-H (west to east) x 1-6 (north to south). Render/collision chunks: 250 m,
    /// <c>Chunk_&lt;Cell&gt;_&lt;i&gt;&lt;j&gt;</c> with i = 0-3 west to east and j = 0-3 south to north inside the cell.
    /// This is the playbook's "VeyraCoords" (card S3); use it everywhere instead of re-deriving the formulas.
    /// </summary>
    public static class WorldCoordinates
    {
        public const float WorldWidthM = 8000f;
        public const float WorldHeightM = 6000f;
        public const float OriginOffsetX = 4000f;
        public const float OriginOffsetY = 3000f;
        public const float CellSizeM = 1000f;
        public const float ChunkSizeM = 250f;
        const string Columns = "ABCDEFGH";

        public static Vector3 ToUnity(float worldX, float worldY, float elevation) =>
            new Vector3(worldX - OriginOffsetX, elevation, worldY - OriginOffsetY);

        /// <summary>World (x east, y north) of a Unity position; the elevation is <c>unity.y</c>.</summary>
        public static Vector2 FromUnity(Vector3 unity) =>
            new Vector2(unity.x + OriginOffsetX, unity.z + OriginOffsetY);

        public static Vector2 Normalised(float worldX, float worldY) =>
            new Vector2(worldX / WorldWidthM, worldY / WorldHeightM);

        public static bool InsideWorld(float worldX, float worldY) =>
            worldX >= 0f && worldY >= 0f && worldX < WorldWidthM && worldY < WorldHeightM;

        /// <summary>Grid cell label such as "E4", or null when outside the world.</summary>
        public static string GridCell(float worldX, float worldY)
        {
            if (!InsideWorld(worldX, worldY)) return null;
            int col = Mathf.FloorToInt(worldX / CellSizeM);
            int row = 6 - Mathf.FloorToInt(worldY / CellSizeM);
            return $"{Columns[col]}{row}";
        }

        /// <summary>Playbook name for <see cref="GridCell"/>: "A1".."H6" (A1 = x 0-1000, y 5000-6000), null outside.</summary>
        public static string CellOf(float worldX, float worldY) => GridCell(worldX, worldY);

        /// <summary>
        /// 250 m chunk name <c>Chunk_&lt;Cell&gt;_&lt;i&gt;&lt;j&gt;</c>: i = (x mod 1000) / 250 west to east,
        /// j = (y mod 1000) / 250 south to north. Null outside the world.
        /// </summary>
        public static string ChunkOf(float worldX, float worldY)
        {
            if (!InsideWorld(worldX, worldY)) return null;
            int col = Mathf.FloorToInt(worldX / CellSizeM);
            int rowFromSouth = Mathf.FloorToInt(worldY / CellSizeM);
            int i = Mathf.Min(3, Mathf.FloorToInt((worldX - col * CellSizeM) / ChunkSizeM));
            int j = Mathf.Min(3, Mathf.FloorToInt((worldY - rowFromSouth * CellSizeM) / ChunkSizeM));
            return $"Chunk_{Columns[col]}{6 - rowFromSouth}_{i}{j}";
        }

        /// <summary>
        /// Unity yaw (degrees about +Y) for a data heading (degrees counter-clockwise from east):
        /// -heading for prefabs whose arm or forward axis is local +X (the package's lamps);
        /// 90 - heading for prefabs that face local +Z. Buildings: unity_yaw_deg = -rot_deg.
        /// </summary>
        public static float YawFromHeading(float headingDeg, bool prefabFacesPlusZ = false) =>
            prefabFacesPlusZ ? 90f - headingDeg : -headingDeg;
    }
}
