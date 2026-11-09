using UnityEngine;

namespace VeyraCity.Core
{
    /// <summary>
    /// Canonical Veyra world coordinates (from VEYRA_CITY_WORLD_DESIGN): metres from the south-west corner of the
    /// 8 x 6 km world, x east, y north. Unity: X = x - 4000, Y = elevation, Z = y - 3000. Map grid: 1 km cells A-H (west to east) x 1-6 (north to south).
    /// </summary>
    public static class WorldCoordinates
    {
        public const float WorldWidthM = 8000f;
        public const float WorldHeightM = 6000f;
        public const float OriginOffsetX = 4000f;
        public const float OriginOffsetY = 3000f;
        const string Columns = "ABCDEFGH";

        public static Vector3 ToUnity(float worldX, float worldY, float elevation) =>
            new Vector3(worldX - OriginOffsetX, elevation, worldY - OriginOffsetY);

        public static Vector2 FromUnity(Vector3 unity) =>
            new Vector2(unity.x + OriginOffsetX, unity.z + OriginOffsetY);

        public static Vector2 Normalised(float worldX, float worldY) =>
            new Vector2(worldX / WorldWidthM, worldY / WorldHeightM);

        /// <summary>Grid cell label such as "E4", or null when outside the world.</summary>
        public static string GridCell(float worldX, float worldY)
        {
            if (worldX < 0f || worldY < 0f || worldX >= WorldWidthM || worldY >= WorldHeightM) return null;
            int col = Mathf.FloorToInt(worldX / 1000f);
            int row = 6 - Mathf.FloorToInt(worldY / 1000f);
            return $"{Columns[col]}{row}";
        }
    }
}
