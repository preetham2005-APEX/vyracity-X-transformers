using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting;

namespace VeyraCity.World.Data
{
    /// <summary>
    /// <c>terrain_tiles_manifest.json</c>: 48 tiles of 513 x 513 unsigned 16-bit little-endian RAW, rows south to north.
    /// Height (m) = -60 + v / 65535 * 560. The RAW files live in <c>ArtSource/VeyraData/terrain/tiles_raw16</c> (editor-only).
    /// </summary>
    [Preserve]
    public sealed class TerrainManifestData
    {
        [JsonProperty("format")] public string Format;
        [JsonProperty("height_mapping")] public string HeightMapping;
        [JsonProperty("unity_terrain_height")] public float UnityTerrainHeight;
        [JsonProperty("tile_world_size_m")] public float TileWorldSizeM;
        [JsonProperty("sea_level_m")] public float SeaLevelM;
        [JsonProperty("mirror_lake_level_m")] public float MirrorLakeLevelM;
        [JsonProperty("tiles")] public List<TerrainTileRecord> Tiles;

        public const int Resolution = 513;
        public const long RawBytes = Resolution * Resolution * 2L;
        public const float HeightOffsetM = -60f;
        public const float HeightRangeM = 560f;
        public static float HeightFromRaw(ushort v) => HeightOffsetM + v / 65535f * HeightRangeM;

        public static TerrainManifestData Parse(string json, string source = "terrain_tiles_manifest.json")
        {
            TerrainManifestData d;
            try
            {
                d = JsonConvert.DeserializeObject<TerrainManifestData>(json, new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
            }
            catch (JsonException e) { throw new FormatException($"{source}: {e.Message}", e); }
            if (d?.Tiles == null) throw new FormatException($"{source}: no tiles");
            return d;
        }
    }

    [Preserve]
    public sealed class TerrainTileRecord
    {
        /// <summary>Cell label, e.g. "A6".</summary>
        [JsonProperty("tile")] public string Tile;
        /// <summary>Relative to the terrain folder, e.g. "tiles_raw16/terrain_A6.raw".</summary>
        [JsonProperty("file")] public string File;
        [JsonProperty("resolution")] public int Resolution;
        [JsonProperty("world_x_m")] public float[] WorldXM;
        [JsonProperty("world_y_m")] public float[] WorldYM;
        [JsonProperty("unity_position")] public float[] UnityPosition;
        [JsonProperty("unity_size")] public float[] UnitySize;
        [JsonProperty("min_height_m")] public float MinHeightM;
        [JsonProperty("max_height_m")] public float MaxHeightM;
        [JsonProperty("land_fraction")] public float LandFraction;

        public Vector3 UnityPos => new Vector3(UnityPosition[0], UnityPosition[1], UnityPosition[2]);
    }
}
