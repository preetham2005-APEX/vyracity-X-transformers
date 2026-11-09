using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VeyraCity.Core;
using VeyraCity.World.Data;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card S3: "Veyra/Data/Validate Data". Loads every Veyra data file in <c>Assets/_Veyra/Data</c> (and the terrain RAWs in
    /// <c>ArtSource/VeyraData</c>), checks counts and coordinate formulas, and writes <c>Logs/S3/validate_data.txt</c>.
    /// Parse errors and count mismatches are FAILs (Debug.LogError); known source-data defects are WARNs with their bug ID.
    /// Batch: <c>-executeMethod VeyraCity.EditorTools.VeyraDataValidator.ValidateBatch</c> (exit code 1 on failure).
    /// </summary>
    public static class VeyraDataValidator
    {
        public const string DataDir = "Assets/_Veyra/Data";
        public const string TerrainDir = "ArtSource/VeyraData/terrain";
        public const string MapsDir = "Assets/_Veyra/Art/Textures/Maps";

        // Card S3 / Appendix A expectations.
        public const int ExpectedLocations = 209, ExpectedRoads = 93, ExpectedStructures = 17, ExpectedInterchanges = 24,
            ExpectedRaceRoutes = 16, ExpectedCrownDowntown = 282, ExpectedDistricts = 8, ExpectedTerrainTiles = 48;
        public const float ExpectedRoadKm = 165.2f;
        // Counts measured from the package and the data supplement in the S2/S3 audit (a mismatch means a partial or changed copy).
        public const int ExpectedVegetation = 67499, ExpectedStreetFurniture = 2777, ExpectedWorldBuildings = 2708,
            ExpectedContainers = 6050, ExpectedCranes = 8;

        public sealed class Result
        {
            public bool Ok;
            public int Fails, Warns;
            public string Report;
        }

        [MenuItem("Veyra/Data/Validate Data")]
        public static void Menu() => Run();

        public static void ValidateBatch()
        {
            var r = Run();
            EditorApplication.Exit(r.Ok ? 0 : 1);
        }

        public static string LoadText(string fileName)
        {
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>($"{DataDir}/{fileName}");
            if (ta == null) throw new FileNotFoundException($"{DataDir}/{fileName} is missing or not imported as a TextAsset");
            return ta.text;
        }

        public static IEnumerable<string> DataFiles(string prefix) =>
            Directory.GetFiles(DataDir, prefix + "*.csv").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal);

        public static Result Run(bool writeLog = true)
        {
            // Numbers in the report must not depend on the PC's locale (e.g. en-IN prints 5,26,338).
            var culture = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            try { return RunInvariant(writeLog); }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = culture; }
        }

        static Result RunInvariant(bool writeLog)
        {
            var log = new Report();
            log.Line($"Veyra data validation — {DateTime.Now:yyyy-MM-dd HH:mm:ss} — Unity {Application.unityVersion}");
            LocationRegistry reg = null;
            RoadNetworkData net = null;

            // ---------------------------------------------------------------- locations
            log.Section("location_registry.csv");
            try
            {
                reg = LocationRegistry.Parse(LoadText("location_registry.csv"));
                log.Count("locations", reg.Count, ExpectedLocations);
                int badUnity = 0, badCell = 0, badNorm = 0, badElev = 0;
                foreach (var l in reg.All)
                {
                    var d = l.UnityFromWorld - l.Unity;
                    if (Mathf.Abs(d.x) > 0.06f || Mathf.Abs(d.y) > 0.06f || Mathf.Abs(d.z) > 0.06f) { badUnity++; log.Detail($"unity mismatch {l.Id}: file {l.Unity} vs formula {l.UnityFromWorld}"); }
                    if (WorldCoordinates.CellOf(l.XM, l.YM) != l.GridCell) { badCell++; log.Detail($"grid_cell {l.Id}: file {l.GridCell} vs {WorldCoordinates.CellOf(l.XM, l.YM)}"); }
                    var nrm = WorldCoordinates.Normalised(l.XM, l.YM);
                    if (Mathf.Abs(nrm.x - l.NormX) > 0.0006f || Mathf.Abs(nrm.y - l.NormY) > 0.0006f) badNorm++;
                    if (Mathf.Abs(l.ElevationM - l.UnityY) > 0.06f) badElev++;
                }
                log.Check(badUnity == 0, "unity_x/y/z = (x-4000, elevation, y-3000) for every location", $"{badUnity} locations break the Unity formula");
                log.Check(badCell == 0, "grid_cell = CellOf(x, y) for every location", $"{badCell} grid_cell mismatches");
                log.Check(badNorm == 0, "norm_x/norm_y = x/8000, y/6000", $"{badNorm} normalised mismatches");
                log.Check(badElev == 0, "elevation_m = unity_y", $"{badElev} elevation mismatches");
                Landmark(log, reg, "LM-01", new Vector3(170f, 9.8f, -670f));
                Landmark(log, reg, "LM-06", new Vector3(-130f, 9.4f, -2640f));
                log.Info("categories: " + string.Join(", ", reg.Categories.OrderBy(c => c).Select(c => $"{c} {reg.ByCategory(c).Count}")));
            }
            catch (Exception e) { log.Fail($"location_registry.csv: {e.Message}"); }

            // ---------------------------------------------------------------- road network
            log.Section("road_network_geometry.json");
            try
            {
                net = RoadNetwork.Parse(LoadText("road_network_geometry.json"));
                float km = net.Roads.Sum(r => r.LengthM) / 1000f;
                log.Count("roads", net.Roads.Count, ExpectedRoads);
                log.Check(Mathf.Abs(km - ExpectedRoadKm) <= 0.05f, $"total road length {km:F1} km (expected ≈ {ExpectedRoadKm} km)", $"total road length {km:F2} km, expected ≈ {ExpectedRoadKm} km");
                log.Count("structures", net.Structures.Count, ExpectedStructures);
                log.Count("interchanges", net.Interchanges.Count, ExpectedInterchanges);
                log.Count("race routes", net.RaceRoutes.Count, ExpectedRaceRoutes);
                log.Count("districts", net.Districts.Count, ExpectedDistricts);
                log.Info($"shortcuts {net.Shortcuts.Count}; profile samples {net.Roads.Sum(r => r.SampleCount):N0}; coastline {net.Coastline.Length} pts; river {net.RiverCentreline.Length} pts, width {string.Join("-", net.RiverWidthM)} m; lake {net.LakePolygon.Length} pts at {net.LakeLevelM} m");
                log.Info("roads per class: " + string.Join(", ", net.Roads.GroupBy(r => r.Cls).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()} ({g.Sum(r => r.LengthM) / 1000f:F2} km)")));

                var ids = new HashSet<string>(net.Roads.Select(r => r.Id));
                log.Check(ids.Count == net.Roads.Count, "road ids unique", "duplicate road ids");
                var badStruct = net.Structures.Where(s => !ids.Contains(s.Road) || !(s.S0 >= 0f && s.S0 < s.S1 && s.S1 <= net.Road(s.Road).LengthM + 1f)).Select(s => s.Name).ToList();
                log.Check(badStruct.Count == 0, "every structure lies on an existing road (0 ≤ s0 < s1 ≤ length)", "bad structures: " + string.Join(", ", badStruct));
                var badRefs = net.Interchanges.SelectMany(i => i.Roads.Where(r => !ids.Contains(r)).Select(r => $"{i.Id}->{r}"))
                    .Concat(net.RaceRoutes.SelectMany(rr => rr.Roads.Where(r => !ids.Contains(r)).Select(r => $"{rr.Id}->{r}"))).ToList();
                log.Check(badRefs.Count == 0, "interchanges and race routes reference existing roads", "unknown road references: " + string.Join(", ", badRefs));
                log.Info("structures: " + string.Join("; ", net.Structures.Select(s => $"{s.Name} ({s.Kind}, {s.Road} {s.S0:F0}-{s.S1:F0} m)")));

                // Profile health: the data is known to violate CLAUDE.md (C2 fixes it). Reported, not failed.
                var steps = new List<string>();
                var grades = new List<string>();
                foreach (var r in net.Roads)
                {
                    var limit = r.ClassInfo.MaxGrade;
                    float maxStep = 0f; int stepAt = 0, overLimit = 0, segs = 0;
                    for (int i = 1; i < r.ProfileXYZ.Length; i++)
                    {
                        var a = r.ProfileXYZ[i - 1]; var b = r.ProfileXYZ[i];
                        float dz = b[2] - a[2], dxy = Mathf.Sqrt((b[0] - a[0]) * (b[0] - a[0]) + (b[1] - a[1]) * (b[1] - a[1]));
                        if (Mathf.Abs(dz) > Mathf.Abs(maxStep)) { maxStep = dz; stepAt = i; }
                        if (dxy > 0.5f) { segs++; if (Mathf.Abs(dz) / dxy > limit + 0.005f) overLimit++; }
                    }
                    if (Mathf.Abs(maxStep) > 1f) steps.Add($"{r.Id} {maxStep:+0.00;-0.00} m at sample {stepAt} (s≈{stepAt * 4} m)");
                    if (overLimit > 0) grades.Add($"{r.Id}({r.Cls}) {overLimit}/{segs}");
                }
                log.Warn($"{steps.Count} roads have a vertical step > 1 m between adjacent 4 m samples (BUG-001, BUG-035; C2): " + string.Join("; ", steps));
                log.Warn($"{grades.Count} roads have 4 m segments above their class grade limit + 0.5 % (BUG-034; C2): " + string.Join(", ", grades));
            }
            catch (Exception e) { log.Fail($"road_network_geometry.json: {e.Message}"); }

            // ---------------------------------------------------------------- districts vs registry (BUG-005)
            if (reg != null && net != null)
            {
                var outside = reg.All.Where(l => net.Districts.TryGetValue(l.DistrictId, out var d) && !d.Contains(l.XM, l.YM)).Select(l => l.Id).ToList();
                var unknown = reg.All.Where(l => !net.Districts.ContainsKey(l.DistrictId)).Select(l => l.Id).ToList();
                log.Check(unknown.Count == 0, "every location's district_id exists in the road JSON districts", "unknown district ids: " + string.Join(", ", unknown));
                log.Warn($"{outside.Count} locations lie outside their own district polygon (BUG-005): {string.Join(", ", outside)}");
            }

            // ---------------------------------------------------------------- buildings
            log.Section("building placements");
            try
            {
                var dt = PlacementReader.Buildings(LoadText("crown_downtown_building_placements.csv"), "crown_downtown_building_placements.csv");
                log.Count("Crown Downtown placements", dt.Count, ExpectedCrownDowntown);
                int bad = dt.Count(b => !Near(b.UnityX, b.XM - 4000f) || !Near(b.UnityZ, b.YM - 3000f));
                log.Check(bad == 0, "Crown Downtown unity_x/z = x-4000, y-3000", $"{bad} Crown Downtown rows break the Unity formula");
                log.Info("Crown Downtown typologies: " + string.Join(", ", dt.GroupBy(b => b.Typology).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}")));
            }
            catch (Exception e) { log.Fail($"crown_downtown_building_placements.csv: {e.Message}"); }

            try
            {
                var wb = PlacementReader.Buildings(LoadText("world_building_placements.csv"), "world_building_placements.csv");
                log.Count("world building placements (data supplement)", wb.Count, ExpectedWorldBuildings);
                int badF = wb.Count(b => !Near(b.UnityX, b.XM - 4000f) || !Near(b.UnityZ, b.YM - 3000f));
                int badCell = wb.Count(b => b.Cell != WorldCoordinates.CellOf(b.XM, b.YM));
                int badChunk = wb.Count(b => b.Chunk != WorldCoordinates.ChunkOf(b.XM, b.YM));
                log.Check(badF == 0, "supplement unity_x/z formula", $"{badF} supplement rows break the Unity formula");
                log.Check(badCell == 0 && badChunk == 0, "supplement cell/chunk columns = CellOf/ChunkOf for all rows", $"{badCell} cell and {badChunk} chunk mismatches in the supplement");
                log.Info("supplement per district: " + string.Join(", ", wb.GroupBy(b => b.DistrictId).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}")));
                var c = PlacementReader.Containers(LoadText("container_placements.csv"), "container_placements.csv");
                log.Count("container stacks (data supplement)", c.Count, ExpectedContainers);
                int badC = c.Count(x => x.Chunk != WorldCoordinates.ChunkOf(x.XM, x.YM) || x.Cell != WorldCoordinates.CellOf(x.XM, x.YM));
                log.Check(badC == 0, "container cell/chunk columns", $"{badC} container cell/chunk mismatches");
                log.Info($"container boxes counting tiers: {c.Sum(x => x.Tiers):N0}");
                var cr = PlacementReader.Cranes(LoadText("crane_placements.csv"), "crane_placements.csv");
                log.Count("cranes (data supplement)", cr.Count, ExpectedCranes);
            }
            catch (Exception e) { log.Fail($"data supplement: {e.Message}"); }

            // ---------------------------------------------------------------- vegetation / street furniture
            log.Section("placement_data (vegetation, street furniture)");
            try
            {
                int veg = 0, fur = 0, badV = 0, badF = 0, vegFiles = 0, furFiles = 0;
                var perType = new Dictionary<string, int>();
                foreach (var f in DataFiles("vegetation_"))
                {
                    var rows = PlacementReader.Vegetation(LoadText(f), f); vegFiles++; veg += rows.Count;
                    badV += rows.Count(r => !Near(r.UnityX, r.XM - 4000f) || !Near(r.UnityZ, r.YM - 3000f) || !Near(r.UnityY, r.ZM));
                    foreach (var r in rows) perType[r.Type] = perType.TryGetValue(r.Type, out var n) ? n + 1 : 1;
                }
                foreach (var f in DataFiles("street_furniture_"))
                {
                    var rows = PlacementReader.StreetFurniture(LoadText(f), f); furFiles++; fur += rows.Count;
                    badF += rows.Count(r => !Near(r.UnityX, r.XM - 4000f) || !Near(r.UnityZ, r.YM - 3000f) || !Near(r.UnityY, r.ZM));
                    foreach (var r in rows) perType[r.Prefab] = perType.TryGetValue(r.Prefab, out var n) ? n + 1 : 1;
                }
                log.Count("vegetation files", vegFiles, 8);
                log.Count("street-furniture files", furFiles, 8);
                log.Count("vegetation instances", veg, ExpectedVegetation);
                log.Count("street-furniture instances", fur, ExpectedStreetFurniture);
                log.Check(badV == 0 && badF == 0, "placement unity_x/y/z formula for every row", $"{badV} vegetation and {badF} furniture rows break the Unity formula");
                log.Info("types: " + string.Join(", ", perType.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value:N0}")));
            }
            catch (Exception e) { log.Fail($"placement_data: {e.Message}"); }

            // ---------------------------------------------------------------- terrain
            log.Section("terrain_tiles_manifest.json + ArtSource/VeyraData RAW tiles");
            try
            {
                var tm = TerrainManifestData.Parse(LoadText("terrain_tiles_manifest.json"));
                log.Count("terrain tiles", tm.Tiles.Count, ExpectedTerrainTiles);
                int missing = 0, badSize = 0, badPos = 0, badLabel = 0;
                foreach (var t in tm.Tiles)
                {
                    var path = Path.Combine(TerrainDir, t.File);
                    if (!File.Exists(path)) { missing++; log.Detail("missing " + path); }
                    else if (new FileInfo(path).Length != TerrainManifestData.RawBytes) badSize++;
                    var expect = new Vector3(t.WorldXM[0] - 4000f, -60f, t.WorldYM[0] - 3000f);
                    if ((t.UnityPos - expect).sqrMagnitude > 0.0001f || t.Resolution != TerrainManifestData.Resolution) badPos++;
                    if (WorldCoordinates.CellOf(t.WorldXM[0] + 1f, t.WorldYM[0] + 1f) != t.Tile) badLabel++;
                }
                log.Check(missing == 0 && badSize == 0, $"all {tm.Tiles.Count} RAW tiles present in {TerrainDir} at {TerrainManifestData.RawBytes:N0} bytes", $"{missing} RAW tiles missing, {badSize} with the wrong size");
                log.Check(badPos == 0 && badLabel == 0, "tile unity_position = (x0-4000, -60, y0-3000), resolution 513, label = CellOf", $"{badPos} tiles with bad position/resolution, {badLabel} with a bad label");
                log.Info($"terrain height range {tm.Tiles.Min(t => t.MinHeightM):F2} to {tm.Tiles.Max(t => t.MaxHeightM):F2} m; sea {tm.SeaLevelM} m; Mirror Lake {tm.MirrorLakeLevelM} m");
            }
            catch (Exception e) { log.Fail($"terrain: {e.Message}"); }

            log.Check(File.Exists($"{MapsDir}/master_world_map.png"), "master_world_map.png present in " + MapsDir, "master_world_map.png missing in " + MapsDir);

            // ---------------------------------------------------------------- summary
            bool ok = log.Fails == 0;
            log.Summary(ok
                ? $"VALIDATE DATA: PASS — {reg?.Count} locations, {net?.Roads.Count} roads ({net?.Roads.Sum(r => r.LengthM) / 1000f:F1} km), {net?.Structures.Count} structures, {net?.Interchanges.Count} interchanges, {net?.RaceRoutes.Count} race routes, {ExpectedCrownDowntown} Crown Downtown placements; {log.Warns} known-data warnings"
                : $"VALIDATE DATA: FAIL — {log.Fails} failure(s), {log.Warns} warning(s)");

            var result = new Result { Ok = ok, Fails = log.Fails, Warns = log.Warns, Report = log.ToString() };
            if (writeLog)
            {
                var dir = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Logs", "S3");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "validate_data.txt"), result.Report, new UTF8Encoding(false));
            }
            if (ok) Debug.Log(result.Report); else Debug.LogError(result.Report);
            return result;
        }

        static bool Near(float a, float b, float tol = 0.06f) => Mathf.Abs(a - b) <= tol;

        static void Landmark(Report log, LocationRegistry reg, string id, Vector3 expected)
        {
            var l = reg.Get(id);
            if (l == null) { log.Fail($"{id} not in the registry"); return; }
            var u = WorldCoordinates.ToUnity(l.XM, l.YM, l.ElevationM);
            bool ok = Mathf.Abs(u.x - expected.x) <= 0.05f && Mathf.Abs(u.z - expected.z) <= 0.05f && Mathf.Abs(u.y - expected.y) <= 0.5f;
            log.Check(ok, $"{id} {l.Name} → Unity {u.ToString("F1")} (expected {expected.ToString("F1")})", $"{id} {l.Name} → Unity {u.ToString("F2")}, expected {expected.ToString("F1")}");
        }

        sealed class Report
        {
            readonly StringBuilder sb = new StringBuilder();
            readonly StringBuilder head = new StringBuilder();
            public int Fails, Warns;
            int details;
            public void Line(string s) => sb.AppendLine(s);
            public void Section(string s) => sb.AppendLine().AppendLine("== " + s);
            public void Info(string s) => sb.AppendLine("INFO  " + s);
            public void Warn(string s) { Warns++; sb.AppendLine("WARN  " + s); }
            public void Fail(string s) { Fails++; sb.AppendLine("FAIL  " + s); Debug.LogError("[Validate Data] " + s); }
            public void Check(bool ok, string pass, string fail) { if (ok) sb.AppendLine("PASS  " + pass); else Fail(fail); }
            public void Count(string what, int actual, int expected) =>
                Check(actual == expected, $"{what}: {actual.ToString("N0", CultureInfo.InvariantCulture)}", $"{what}: {actual.ToString("N0", CultureInfo.InvariantCulture)}, expected {expected.ToString("N0", CultureInfo.InvariantCulture)}");
            public void Detail(string s) { if (details++ < 20) sb.AppendLine("      " + s); }
            public void Summary(string s) => head.AppendLine(s);
            public override string ToString() => head.ToString() + sb;
        }
    }
}
