using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;
using VeyraCity.Core;

namespace VeyraCity.World.Data
{
    /// <summary>
    /// A building footprint. Reads both <c>crown_downtown_building_placements.csv</c> (282 rows, design package) and the
    /// data supplement <c>world_building_placements.csv</c> (2,708 rows); fields missing from a file stay null/NaN
    /// (UnityYawDeg is derived as -rot_deg when absent, Appendix A).
    /// </summary>
    [Preserve, Serializable]
    public sealed class BuildingPlacement
    {
        public string Id, Name, LandmarkId, DistrictId, Kind, Shape, StyleKit, Typology, Cell, Chunk;
        public float XM, YM, FootprintWM, FootprintLM, RotDeg, UnityYawDeg, HeightM;
        public float GroundCenterM = float.NaN, GroundMinM = float.NaN;
        /// <summary>Unity base height: unity_y_base (supplement) or unity_y_ground (Crown Downtown file).</summary>
        public float UnityX, UnityYBase, UnityZ, Seed;
        public Vector3 Unity => new Vector3(UnityX, UnityYBase, UnityZ);
    }

    [Preserve, Serializable]
    public sealed class VegetationRecord
    {
        public string Type;
        public float XM, YM, ZM, UnityX, UnityY, UnityZ, Scale;
        public Vector3 Unity => new Vector3(UnityX, UnityY, UnityZ);
    }

    [Preserve, Serializable]
    public sealed class StreetFurnitureRecord
    {
        public string Prefab, RoadId;
        public float XM, YM, ZM, UnityX, UnityY, UnityZ, HeadingDeg;
        public Vector3 Unity => new Vector3(UnityX, UnityY, UnityZ);
        /// <summary>The package's lamp prefabs have their arm along local +X: yaw = -heading.</summary>
        public float UnityYawDeg => WorldCoordinates.YawFromHeading(HeadingDeg);
    }

    /// <summary>Ironport container stack (data supplement): 40 ft containers, long axis east-west.</summary>
    [Preserve, Serializable]
    public sealed class ContainerPlacement
    {
        public string Id, LengthAxis, ColorHex, Cell, Chunk;
        public float XM, YM, LengthM, WidthM, GroundM, UnityX, UnityYBase, UnityZ;
        public int Tiers;
    }

    /// <summary>Ship-to-shore gantry crane (data supplement).</summary>
    [Preserve, Serializable]
    public sealed class CranePlacement
    {
        public string Id, Cell;
        public float XM, YM, HeightM, GroundM, UnityX, UnityYBase, UnityZ;
    }

    /// <summary>Header-driven readers for the placement CSVs in <c>Assets/_Veyra/Data</c>. All throw FormatException on bad data.</summary>
    public static class PlacementReader
    {
        public static List<BuildingPlacement> Buildings(string csvText, string source)
        {
            var t = VeyraCsv.Parse(csvText, source);
            t.Require("name", "typology", "x_m", "y_m", "footprint_w_m", "footprint_l_m", "rot_deg", "height_m", "seed", "unity_x", "unity_z");
            string baseCol = t.Has("unity_y_base") ? "unity_y_base" : t.Has("unity_y_ground") ? "unity_y_ground" : null;
            if (baseCol == null) throw new FormatException($"{source}: needs unity_y_base or unity_y_ground");
            var list = new List<BuildingPlacement>(t.Count);
            for (int i = 0; i < t.Count; i++)
            {
                float rot = t.Float(i, "rot_deg");
                list.Add(new BuildingPlacement
                {
                    Id = t.StrOpt(i, "id"), Name = t.Str(i, "name"), LandmarkId = t.StrOpt(i, "landmark_id"),
                    DistrictId = t.StrOpt(i, "district_id"), Kind = t.StrOpt(i, "kind"), Shape = t.StrOpt(i, "shape"),
                    StyleKit = t.StrOpt(i, "style_kit"), Typology = t.Str(i, "typology"),
                    XM = t.Float(i, "x_m"), YM = t.Float(i, "y_m"),
                    FootprintWM = t.Float(i, "footprint_w_m"), FootprintLM = t.Float(i, "footprint_l_m"),
                    RotDeg = rot, UnityYawDeg = t.FloatOpt(i, "unity_yaw_deg", -rot), HeightM = t.Float(i, "height_m"),
                    GroundCenterM = t.FloatOpt(i, "ground_center_m", float.NaN), GroundMinM = t.FloatOpt(i, "ground_min_m", float.NaN),
                    UnityX = t.Float(i, "unity_x"), UnityYBase = t.Float(i, baseCol), UnityZ = t.Float(i, "unity_z"),
                    Cell = t.StrOpt(i, "cell"), Chunk = t.StrOpt(i, "chunk"), Seed = t.Float(i, "seed"),
                });
            }
            return list;
        }

        public static List<VegetationRecord> Vegetation(string csvText, string source)
        {
            var t = VeyraCsv.Parse(csvText, source);
            t.Require("type", "x_m", "y_m", "z_m", "unity_x", "unity_y", "unity_z", "scale");
            var list = new List<VegetationRecord>(t.Count);
            for (int i = 0; i < t.Count; i++)
                list.Add(new VegetationRecord
                {
                    Type = t.Str(i, "type"), XM = t.Float(i, "x_m"), YM = t.Float(i, "y_m"), ZM = t.Float(i, "z_m"),
                    UnityX = t.Float(i, "unity_x"), UnityY = t.Float(i, "unity_y"), UnityZ = t.Float(i, "unity_z"), Scale = t.Float(i, "scale"),
                });
            return list;
        }

        public static List<StreetFurnitureRecord> StreetFurniture(string csvText, string source)
        {
            var t = VeyraCsv.Parse(csvText, source);
            t.Require("prefab", "x_m", "y_m", "z_m", "unity_x", "unity_y", "unity_z", "heading_deg", "road_id");
            var list = new List<StreetFurnitureRecord>(t.Count);
            for (int i = 0; i < t.Count; i++)
                list.Add(new StreetFurnitureRecord
                {
                    Prefab = t.Str(i, "prefab"), XM = t.Float(i, "x_m"), YM = t.Float(i, "y_m"), ZM = t.Float(i, "z_m"),
                    UnityX = t.Float(i, "unity_x"), UnityY = t.Float(i, "unity_y"), UnityZ = t.Float(i, "unity_z"),
                    HeadingDeg = t.Float(i, "heading_deg"), RoadId = t.Str(i, "road_id"),
                });
            return list;
        }

        public static List<ContainerPlacement> Containers(string csvText, string source)
        {
            var t = VeyraCsv.Parse(csvText, source);
            t.Require("id", "x_m", "y_m", "length_axis", "length_m", "width_m", "tiers", "color_hex", "ground_m", "unity_x", "unity_y_base", "unity_z", "cell", "chunk");
            var list = new List<ContainerPlacement>(t.Count);
            for (int i = 0; i < t.Count; i++)
                list.Add(new ContainerPlacement
                {
                    Id = t.Str(i, "id"), XM = t.Float(i, "x_m"), YM = t.Float(i, "y_m"), LengthAxis = t.Str(i, "length_axis"),
                    LengthM = t.Float(i, "length_m"), WidthM = t.Float(i, "width_m"), Tiers = t.Int(i, "tiers"), ColorHex = t.Str(i, "color_hex"),
                    GroundM = t.Float(i, "ground_m"), UnityX = t.Float(i, "unity_x"), UnityYBase = t.Float(i, "unity_y_base"), UnityZ = t.Float(i, "unity_z"),
                    Cell = t.Str(i, "cell"), Chunk = t.Str(i, "chunk"),
                });
            return list;
        }

        public static List<CranePlacement> Cranes(string csvText, string source)
        {
            var t = VeyraCsv.Parse(csvText, source);
            t.Require("id", "x_m", "y_m", "height_m", "ground_m", "unity_x", "unity_y_base", "unity_z", "cell");
            var list = new List<CranePlacement>(t.Count);
            for (int i = 0; i < t.Count; i++)
                list.Add(new CranePlacement
                {
                    Id = t.Str(i, "id"), XM = t.Float(i, "x_m"), YM = t.Float(i, "y_m"), HeightM = t.Float(i, "height_m"), GroundM = t.Float(i, "ground_m"),
                    UnityX = t.Float(i, "unity_x"), UnityYBase = t.Float(i, "unity_y_base"), UnityZ = t.Float(i, "unity_z"), Cell = t.Str(i, "cell"),
                });
            return list;
        }
    }
}
