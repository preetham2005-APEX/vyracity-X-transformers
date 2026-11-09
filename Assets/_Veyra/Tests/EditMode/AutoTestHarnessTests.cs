using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VeyraCity.EditorTools;
using VeyraCity.Testing;
using VeyraCity.World.Data;

namespace VeyraCity.Tests
{
    /// <summary>Card V0: command-line parsing, lane paths and the test-route assets.</summary>
    public class AutoTestHarnessTests
    {
        [Test]
        public void Options_ParseCardCommandLine()
        {
            var o = VeyraAutoTest.Options.Parse(new[] { "VeyraCity.exe", "-screen-width", "2560", "-autotest", "60", "-route", "R_SMOKE", "-statsPath", @"E:\VeyraCity\Logs\stats.json", "-timeOfDay", "23", "-quality", "PC", "-reviewCapture" });
            Assert.IsTrue(o.Enabled);
            Assert.AreEqual(60f, o.Seconds);
            Assert.AreEqual("R_SMOKE", o.Route);
            Assert.AreEqual(@"E:\VeyraCity\Logs\stats.json", o.StatsPath);
            Assert.AreEqual(23f, o.TimeOfDay);
            Assert.AreEqual("PC", o.Quality);
            Assert.IsTrue(o.ReviewCapture);
            Assert.IsFalse(VeyraAutoTest.Options.Parse(new[] { "VeyraCity.exe" }).Enabled);
            Assert.AreEqual(0f, VeyraAutoTest.Options.Parse(new[] { "-autotest", "0", "-route", "R_BRIDGES" }).Seconds);
        }

        static RoadRecord StraightEastRoad(string cls, float lengthM)
        {
            int n = Mathf.RoundToInt(lengthM / 4f) + 1;
            var pts = new float[n][];
            var st = new string[n];
            for (int i = 0; i < n; i++) { pts[i] = new[] { 1000f + i * 4f, 3000f, 10f }; st[i] = ""; }
            return new RoadRecord { Id = "T", Cls = cls, ProfileXYZ = pts, ProfileStructure = st, CentrelineXY = new[] { new[] { 1000f, 3000f }, new[] { 1000f + lengthM, 3000f } } };
        }

        [Test]
        public void LanePath_IsRightOfTravel_BothDirections()
        {
            var road = StraightEastRoad("CO", 400f);          // CO is 11 m wide → lane offset 2.75 m
            float offset = RouteAutopilot.LaneOffset(road);
            Assert.AreEqual(2.75f, offset, 1e-4f);

            var east = RouteAutopilot.BuildLanePath(road, 100f, 300f, 20f, out float endE);
            // Data y = 3000 is Unity z = 0, data x = 1100 is Unity x = -2900.
            Assert.AreEqual(-offset, east.PositionAt(0f).z, 0.01f, "eastbound: right of travel is south (−Z)");
            Assert.AreEqual(100f - 3000f, east.PositionAt(0f).x, 0.01f);
            Assert.AreEqual(200f, endE, 2.5f);
            Assert.AreEqual(10f, east.PositionAt(50f).y, 1e-3f, "heights come from the profile");

            var west = RouteAutopilot.BuildLanePath(road, 300f, 100f, 20f, out float endW);
            Assert.AreEqual(offset, west.PositionAt(0f).z, 0.01f, "westbound: right of travel is north (+Z)");
            Assert.AreEqual(300f - 3000f, west.PositionAt(0f).x, 0.01f);
            Assert.AreEqual(200f, endW, 2.5f);
            Assert.Greater(west.PositionAt(0f).x, west.PositionAt(10f).x, "westbound path runs towards −X");
        }

        [Test]
        public void TestRoutes_CreatedFromData()
        {
            TestRouteTools.CreateAll();
            var cfg = AssetDatabase.LoadAssetAtPath<VeyraAutoTestConfig>(TestRouteTools.ConfigPath);
            Assert.IsNotNull(cfg);
            Assert.IsNotNull(cfg.RoadNetworkJson);
            Assert.AreEqual(cfg, Resources.Load<VeyraAutoTestConfig>(VeyraAutoTestConfig.ResourcePath));
            var smoke = cfg.Find("R_SMOKE");
            Assert.AreEqual(2, smoke.Segments.Count);
            Assert.AreEqual(965f, smoke.TotalLengthM, 0.5f);
            var bridges = cfg.Find("R_BRIDGES");
            Assert.AreEqual(14, bridges.Segments.Count);
            Assert.IsTrue(bridges.Segments.Where(s => s.roadId == "R1" || s.roadId == "C1" || s.roadId.StartsWith("H")).All(s => s.targetSpeed_kmh == 90f));
            var night = cfg.Find("R_DOWNTOWN_NIGHT");
            Assert.AreEqual(23f, night.DefaultTimeOfDay);
            Assert.AreEqual(new[] { "A22", "A2", "A1", "A4", "A22" }, night.Segments.Select(s => s.roadId).ToArray());
            Assert.That(night.TotalLengthM, Is.InRange(2200f, 2700f), "one RACE-16 lap is about 2.41 km");
        }
    }
}
