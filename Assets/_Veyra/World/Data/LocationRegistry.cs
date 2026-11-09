using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;
using VeyraCity.Core;

namespace VeyraCity.World.Data
{
    /// <summary>One row of <c>location_registry.csv</c> (all 18 columns, Appendix A). Elevation is terrain height, not deck height (BUG-041).</summary>
    [Preserve, Serializable]
    public sealed class LocationRecord
    {
        public string Id, Name, Category, DistrictId, District;
        public float XM, YM, NormX, NormY, UnityX, UnityY, UnityZ, ElevationM;
        public string GridCell, Road, Crew, Details, Description;

        /// <summary>Unity position as stored in the file.</summary>
        public Vector3 Unity => new Vector3(UnityX, UnityY, UnityZ);
        /// <summary>Unity position derived from x_m, y_m, elevation_m with <see cref="WorldCoordinates.ToUnity"/>.</summary>
        public Vector3 UnityFromWorld => WorldCoordinates.ToUnity(XM, YM, ElevationM);
        public override string ToString() => $"{Id} {Name} ({Category}, {DistrictId}) @ ({XM:F1}, {YM:F1})";
    }

    /// <summary>All 209 locations from <c>Assets/_Veyra/Data/location_registry.csv</c>, by id and by category.</summary>
    public sealed class LocationRegistry
    {
        public static readonly string[] Columns =
        {
            "id", "name", "category", "district_id", "district", "x_m", "y_m", "norm_x", "norm_y",
            "unity_x", "unity_y", "unity_z", "elevation_m", "grid_cell", "road", "crew", "details", "description",
        };

        readonly List<LocationRecord> all;
        readonly Dictionary<string, LocationRecord> byId = new Dictionary<string, LocationRecord>(StringComparer.Ordinal);
        readonly Dictionary<string, List<LocationRecord>> byCategory = new Dictionary<string, List<LocationRecord>>(StringComparer.Ordinal);

        public IReadOnlyList<LocationRecord> All => all;
        public int Count => all.Count;
        public IEnumerable<string> Categories => byCategory.Keys;

        LocationRegistry(List<LocationRecord> records, string source)
        {
            all = records;
            foreach (var r in records)
            {
                if (string.IsNullOrEmpty(r.Id)) throw new FormatException($"{source}: a location has an empty id ({r.Name})");
                if (byId.ContainsKey(r.Id)) throw new FormatException($"{source}: duplicate location id {r.Id}");
                byId[r.Id] = r;
                if (!byCategory.TryGetValue(r.Category, out var list)) byCategory[r.Category] = list = new List<LocationRecord>();
                list.Add(r);
            }
        }

        public LocationRecord Get(string id) => id != null && byId.TryGetValue(id, out var r) ? r : null;

        public IReadOnlyList<LocationRecord> ByCategory(string category) =>
            byCategory.TryGetValue(category, out var list) ? list : (IReadOnlyList<LocationRecord>)Array.Empty<LocationRecord>();

        public static LocationRegistry Parse(string csvText, string source = "location_registry.csv")
        {
            var t = VeyraCsv.Parse(csvText, source);
            t.Require(Columns);
            var list = new List<LocationRecord>(t.Count);
            for (int i = 0; i < t.Count; i++)
            {
                list.Add(new LocationRecord
                {
                    Id = t.Str(i, "id"), Name = t.Str(i, "name"), Category = t.Str(i, "category"),
                    DistrictId = t.Str(i, "district_id"), District = t.Str(i, "district"),
                    XM = t.Float(i, "x_m"), YM = t.Float(i, "y_m"), NormX = t.Float(i, "norm_x"), NormY = t.Float(i, "norm_y"),
                    UnityX = t.Float(i, "unity_x"), UnityY = t.Float(i, "unity_y"), UnityZ = t.Float(i, "unity_z"),
                    ElevationM = t.Float(i, "elevation_m"), GridCell = t.Str(i, "grid_cell"),
                    Road = t.Str(i, "road"), Crew = t.Str(i, "crew"), Details = t.Str(i, "details"), Description = t.Str(i, "description"),
                });
            }
            return new LocationRegistry(list, source);
        }
    }
}
