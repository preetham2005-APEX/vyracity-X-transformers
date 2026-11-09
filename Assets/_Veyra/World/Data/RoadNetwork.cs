using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting;
using VeyraCity.Core;

namespace VeyraCity.World.Data
{
    /// <summary>
    /// <c>road_network_geometry.json</c> (Appendix A). World coordinates: x east, y north, z up, metres from the SW corner.
    /// Parsed with Newtonsoft because JsonUtility cannot read arrays of arrays. Unknown members are an error (schema drift fails loudly).
    /// </summary>
    [Preserve]
    public sealed class RoadNetworkData
    {
        [JsonProperty("units")] public string Units;
        [JsonProperty("coordinates")] public string Coordinates;
        [JsonProperty("roads")] public List<RoadRecord> Roads;
        [JsonProperty("structures")] public List<StructureRecord> Structures;
        [JsonProperty("interchanges")] public List<InterchangeRecord> Interchanges;
        [JsonProperty("river_centreline")] public float[][] RiverCentreline;
        /// <summary>[min, max] river width in metres.</summary>
        [JsonProperty("river_width_m")] public float[] RiverWidthM;
        [JsonProperty("lake_polygon")] public float[][] LakePolygon;
        [JsonProperty("lake_level_m")] public float LakeLevelM;
        [JsonProperty("coastline")] public float[][] Coastline;
        [JsonProperty("districts")] public Dictionary<string, DistrictRecord> Districts;
        [JsonProperty("race_routes")] public List<RaceRouteRecord> RaceRoutes;
        [JsonProperty("shortcuts")] public List<ShortcutRecord> Shortcuts;

        public RoadRecord Road(string id) => Roads?.Find(r => r.Id == id);
    }

    [Preserve]
    public sealed class RoadRecord
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("name")] public string Name;
        /// <summary>Road class code: MW, EX, HW, AR, CO, MT, RU, GR, SV, RW.</summary>
        [JsonProperty("cls")] public string Cls;
        [JsonProperty("closed")] public bool Closed;
        [JsonProperty("length_m")] public float LengthM;
        [JsonProperty("centreline_xy")] public float[][] CentrelineXY;
        /// <summary>[x, y, z] every ~4 m along the road. The single source of road heights (C2 eases it; BUG-001/034/035).</summary>
        [JsonProperty("profile_xyz_every_4m")] public float[][] ProfileXYZ;
        /// <summary>Per profile sample: "", elevated, tunnel, bridge_sea, bridge_river or viaduct.</summary>
        [JsonProperty("profile_structure")] public string[] ProfileStructure;
        /// <summary>The package's p99 grade (a fraction), not the true maximum (BUG-034).</summary>
        [JsonProperty("max_grade")] public float MaxGrade;

        public int SampleCount => ProfileXYZ?.Length ?? 0;
        public Vector3 ProfileUnity(int i) => WorldCoordinates.ToUnity(ProfileXYZ[i][0], ProfileXYZ[i][1], ProfileXYZ[i][2]);
        public RoadClassInfo ClassInfo => RoadClasses.Get(Cls);
    }

    [Preserve]
    public sealed class StructureRecord
    {
        [JsonProperty("road")] public string Road;
        /// <summary>Distance along the road (m) where the structure starts / ends.</summary>
        [JsonProperty("s0")] public float S0;
        [JsonProperty("s1")] public float S1;
        /// <summary>bridge_river, bridge_sea, elevated, tunnel or viaduct.</summary>
        [JsonProperty("kind")] public string Kind;
        [JsonProperty("name")] public string Name;
        [JsonProperty("length")] public float Length;
        [JsonProperty("x")] public float X;
        [JsonProperty("y")] public float Y;
    }

    [Preserve]
    public sealed class InterchangeRecord
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("x")] public float X;
        [JsonProperty("y")] public float Y;
        [JsonProperty("roads")] public string[] Roads;
        [JsonProperty("desc")] public string Desc;
        [JsonProperty("grade_separated")] public bool GradeSeparated;
    }

    [Preserve]
    public sealed class DistrictRecord
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("polygon")] public float[][] Polygon;
        [JsonProperty("color")] public string Color;

        /// <summary>Even-odd point-in-polygon test in world metres.</summary>
        public bool Contains(float x, float y)
        {
            bool inside = false;
            var p = Polygon;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
            {
                float xi = p[i][0], yi = p[i][1], xj = p[j][0], yj = p[j][1];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
            }
            return inside;
        }
    }

    [Preserve]
    public sealed class RaceRouteRecord
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("name")] public string Name;
        /// <summary>e.g. "Sprint" or "Circuit (3 laps)"; the lap count only exists inside this text.</summary>
        [JsonProperty("kind")] public string Kind;
        [JsonProperty("roads")] public string[] Roads;
        [JsonProperty("length_m")] public float LengthM;
        [JsonProperty("start")] public float[] Start;
        /// <summary>Null for circuits.</summary>
        [JsonProperty("finish")] public float[] Finish;
        [JsonProperty("district")] public string District;
        [JsonProperty("valid")] public bool Valid;
    }

    [Preserve]
    public sealed class ShortcutRecord
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("district")] public string District;
        /// <summary>2D points only (no heights in the package).</summary>
        [JsonProperty("pts")] public float[][] Points;
        [JsonProperty("desc")] public string Desc;
        [JsonProperty("length_m")] public float LengthM;
    }

    /// <summary>Road class defaults from the design (road_network.md / Appendix A). Grade limits as fractions.</summary>
    public readonly struct RoadClassInfo
    {
        public readonly string Code, Use, Lanes;
        public readonly float WidthM, SpeedKmh, MaxGrade;
        public RoadClassInfo(string code, string use, string lanes, float width, float speed, float maxGrade)
        { Code = code; Use = use; Lanes = lanes; WidthM = width; SpeedKmh = speed; MaxGrade = maxGrade; }
    }

    public static class RoadClasses
    {
        /// <summary>Source: VEYRA_CITY_WORLD_DESIGN/06_DESIGN_DOCUMENTATION/road_network.md (RU 10 % and RW 0.8 % are not in CLAUDE.md's grade list).</summary>
        public static readonly RoadClassInfo[] All =
        {
            new RoadClassInfo("MW", "Motorway", "3+3 divided", 32f, 110f, 0.05f),
            new RoadClassInfo("EX", "Expressway", "2+2 divided", 24f, 100f, 0.085f),
            new RoadClassInfo("HW", "Highway", "2+2 / 1+1 in mountains", 18f, 90f, 0.09f),
            new RoadClassInfo("AR", "Arterial boulevard", "2+2 + turn lane", 20f, 60f, 0.08f),
            new RoadClassInfo("CO", "Local / collector street", "1+1 + parking", 11f, 40f, 0.10f),
            new RoadClassInfo("MT", "Mountain road", "1+1, no shoulder", 7.5f, 60f, 0.15f),
            new RoadClassInfo("RU", "Rural road", "1+1", 7f, 70f, 0.10f),
            new RoadClassInfo("GR", "Gravel road", "single track + passing bays", 5.5f, 50f, 0.15f),
            new RoadClassInfo("SV", "Service / industrial road", "1+1 wide", 12f, 40f, 0.05f),
            new RoadClassInfo("RW", "Runway / taxiway", "open surface", 45f, 0f, 0.008f),
        };

        public static bool TryGet(string code, out RoadClassInfo info)
        {
            foreach (var c in All) if (c.Code == code) { info = c; return true; }
            info = default; return false;
        }

        public static RoadClassInfo Get(string code) =>
            TryGet(code, out var info) ? info : throw new ArgumentException($"Unknown road class '{code}'");
    }

    public static class RoadNetwork
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            FloatParseHandling = FloatParseHandling.Double,
            Culture = System.Globalization.CultureInfo.InvariantCulture,
        };

        /// <summary>Parses the JSON and checks that every required array is present; throws FormatException otherwise.</summary>
        public static RoadNetworkData Parse(string json, string source = "road_network_geometry.json")
        {
            RoadNetworkData d;
            try { d = JsonConvert.DeserializeObject<RoadNetworkData>(json, Settings); }
            catch (JsonException e) { throw new FormatException($"{source}: {e.Message}", e); }
            if (d == null) throw new FormatException($"{source}: empty document");
            void Need(object o, string name) { if (o == null) throw new FormatException($"{source}: missing '{name}'"); }
            Need(d.Roads, "roads"); Need(d.Structures, "structures"); Need(d.Interchanges, "interchanges");
            Need(d.RaceRoutes, "race_routes"); Need(d.Districts, "districts"); Need(d.Coastline, "coastline");
            Need(d.RiverCentreline, "river_centreline"); Need(d.LakePolygon, "lake_polygon"); Need(d.Shortcuts, "shortcuts");
            foreach (var r in d.Roads)
            {
                if (string.IsNullOrEmpty(r.Id)) throw new FormatException($"{source}: a road has no id");
                if (r.ProfileXYZ == null || r.ProfileXYZ.Length < 2) throw new FormatException($"{source}: road {r.Id} has fewer than 2 profile samples");
                if (r.ProfileStructure == null || r.ProfileStructure.Length != r.ProfileXYZ.Length)
                    throw new FormatException($"{source}: road {r.Id} profile_structure length {r.ProfileStructure?.Length} != profile length {r.ProfileXYZ.Length}");
                foreach (var p in r.ProfileXYZ) if (p == null || p.Length != 3) throw new FormatException($"{source}: road {r.Id} has a profile point without 3 values");
                if (r.CentrelineXY == null || r.CentrelineXY.Length < 2) throw new FormatException($"{source}: road {r.Id} has no centreline");
                if (!RoadClasses.TryGet(r.Cls, out _)) throw new FormatException($"{source}: road {r.Id} has unknown class '{r.Cls}'");
            }
            return d;
        }
    }
}
