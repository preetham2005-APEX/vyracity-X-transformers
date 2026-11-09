using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VeyraCity.Core;
using VeyraCity.EditorTools;
using VeyraCity.World.Data;

namespace VeyraCity.Tests
{
    /// <summary>Card S3: CSV reader, coordinate helpers and the imported Veyra data.</summary>
    public class VeyraCsvTests
    {
        [Test]
        public void QuotedFields_CommasQuotesNewlines_BomAndCrLf()
        {
            string text = "﻿id,name,desc\r\n1,\"Spire, the\",\"He said \"\"hi\"\"\"\r\n2,Plain,\"two\nlines\"\r\n\r\n";
            var t = VeyraCsv.Parse(text, "test.csv");
            Assert.AreEqual(new[] { "id", "name", "desc" }, t.Header);
            Assert.AreEqual(2, t.Count);
            Assert.AreEqual("Spire, the", t.Str(0, "name"));
            Assert.AreEqual("He said \"hi\"", t.Str(0, "desc"));
            Assert.AreEqual("two\nlines", t.Str(1, "desc"));
            Assert.AreEqual(1, t.Int(0, "id"));
        }

        [Test]
        public void EmptyFieldsAndNoTrailingNewline()
        {
            var t = VeyraCsv.Parse("a,b,c\n1,,3", "t.csv");
            Assert.AreEqual("", t.Str(0, "b"));
            Assert.AreEqual(3f, t.Float(0, "c"));
            Assert.AreEqual(-1f, t.FloatOpt(0, "b", -1f));
        }

        [Test]
        public void BadData_FailsLoudly()
        {
            Assert.Throws<FormatException>(() => VeyraCsv.Parse("a,b\n1,2,3\n", "t.csv"));          // too many fields
            Assert.Throws<FormatException>(() => VeyraCsv.Parse("a,b\n1,\"open\n", "t.csv"));        // unterminated quote
            var t = VeyraCsv.Parse("a,b\nx,2\n", "t.csv");
            Assert.Throws<FormatException>(() => t.Float(0, "a"));                                  // not a number
            Assert.Throws<FormatException>(() => t.Require("a", "missing"));                        // missing column
        }
    }

    public class CoordinateHelperTests
    {
        [TestCase(4170f, 2330f, "E4", "Chunk_E4_01")]   // LM-01 Veyra Spire (matches the supplement's chunk column)
        [TestCase(0f, 0f, "A6", "Chunk_A6_00")]         // south-west corner
        [TestCase(7999.9f, 5999.9f, "H1", "Chunk_H1_33")] // north-east corner
        [TestCase(1000f, 5000f, "B1", "Chunk_B1_00")]   // cell boundaries belong to the east/north cell
        [TestCase(1249.9f, 5750f, "B1", "Chunk_B1_03")]
        public void CellAndChunk(float x, float y, string cell, string chunk)
        {
            Assert.AreEqual(cell, WorldCoordinates.CellOf(x, y));
            Assert.AreEqual(chunk, WorldCoordinates.ChunkOf(x, y));
        }

        [Test]
        public void ChunkOutsideWorld_IsNull()
        {
            Assert.IsNull(WorldCoordinates.ChunkOf(-0.1f, 10f));
            Assert.IsNull(WorldCoordinates.ChunkOf(10f, 6000f));
        }

        [Test]
        public void BuildingGeneratorChunk_UsesCoreHelperAndClampsAtEdges()
        {
            Assert.AreEqual("Chunk_E4_01", BuildingGenerator.ChunkOf(170f, -670f));
            Assert.AreEqual("Chunk_A6_00", BuildingGenerator.ChunkOf(-4100f, -3100f));  // outside → clamped
            Assert.AreEqual("Chunk_H1_33", BuildingGenerator.ChunkOf(4000f, 3000f));    // exactly on the far edge
        }

        [Test]
        public void YawFromHeading()
        {
            Assert.AreEqual(-30f, WorldCoordinates.YawFromHeading(30f));
            Assert.AreEqual(60f, WorldCoordinates.YawFromHeading(30f, prefabFacesPlusZ: true));
        }
    }

    /// <summary>Reads the real files in Assets/_Veyra/Data (imported by Tools/World/import_veyra_data.py).</summary>
    public class VeyraDataImportTests
    {
        [Test]
        public void Registry_Has209Locations_AndReferenceLandmarks()
        {
            var reg = LocationRegistry.Parse(VeyraDataValidator.LoadText("location_registry.csv"));
            Assert.AreEqual(VeyraDataValidator.ExpectedLocations, reg.Count);
            var spire = reg.Get("LM-01").UnityFromWorld;
            Assert.AreEqual(170f, spire.x, 0.05f); Assert.AreEqual(9.8f, spire.y, 0.5f); Assert.AreEqual(-670f, spire.z, 0.05f);
            var lighthouse = reg.Get("LM-06").UnityFromWorld;
            Assert.AreEqual(-130f, lighthouse.x, 0.05f); Assert.AreEqual(9.4f, lighthouse.y, 0.5f); Assert.AreEqual(-2640f, lighthouse.z, 0.05f);
            foreach (var l in reg.All) Assert.AreEqual(l.GridCell, WorldCoordinates.CellOf(l.XM, l.YM), l.Id);
        }

        [Test]
        public void RoadNetwork_Counts()
        {
            var net = RoadNetwork.Parse(VeyraDataValidator.LoadText("road_network_geometry.json"));
            Assert.AreEqual(93, net.Roads.Count);
            Assert.AreEqual(165.2f, net.Roads.Sum(r => r.LengthM) / 1000f, 0.05f);
            Assert.AreEqual(17, net.Structures.Count);
            Assert.AreEqual(24, net.Interchanges.Count);
            Assert.AreEqual(16, net.RaceRoutes.Count);
            Assert.AreEqual(8, net.Districts.Count);
            Assert.AreEqual(150f, net.LakeLevelM);
        }

        [Test]
        public void CrownDowntownPlacements_282()
        {
            var b = PlacementReader.Buildings(VeyraDataValidator.LoadText("crown_downtown_building_placements.csv"), "crown_downtown_building_placements.csv");
            Assert.AreEqual(282, b.Count);
            var spire = b.First(x => x.Name == "Veyra Spire");
            Assert.AreEqual(342f, spire.HeightM);
            Assert.AreEqual(-spire.RotDeg, spire.UnityYawDeg); // derived when the column is absent
        }

        [Test]
        public void Supplement_ChunkColumn_MatchesCoreHelper_ForAllBuildings()
        {
            var b = PlacementReader.Buildings(VeyraDataValidator.LoadText("world_building_placements.csv"), "world_building_placements.csv");
            Assert.AreEqual(2708, b.Count);
            var bad = b.Where(x => x.Chunk != WorldCoordinates.ChunkOf(x.XM, x.YM) || x.Chunk != BuildingGenerator.ChunkOf(x.UnityX, x.UnityZ)).Take(5).ToList();
            Assert.IsEmpty(bad, string.Join("; ", bad.Select(x => $"{x.Id} {x.Chunk}")));
        }

        [Test]
        public void Validator_Passes()
        {
            var r = VeyraDataValidator.Run(writeLog: false);
            Assert.IsTrue(r.Ok, r.Report);
        }
    }
}
