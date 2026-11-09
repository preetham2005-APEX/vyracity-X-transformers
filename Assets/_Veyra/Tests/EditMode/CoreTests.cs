using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using VeyraCity.Core;
using VeyraCity.EditorTools;

namespace VeyraCity.Tests
{
    public class LaunchOptionsTests
    {
        [Test]
        public void Defaults_WhenNoArgs()
        {
            var o = LaunchOptions.Parse(new string[0]);
            Assert.IsFalse(o.IsAutomatedRun);
            Assert.IsNull(o.ScreenshotPath);
            Assert.IsNull(o.StatsPath);
            Assert.IsFalse(o.ShowOverlay);
        }

        [Test]
        public void ParsesAllFlags_CaseInsensitive()
        {
            var o = LaunchOptions.Parse(new[] { "VeyraCity.exe", "-veyraAutoQuit", "20", "-VEYRASCREENSHOT", @"E:\a b\shot.png",
                "-veyraScreenshotAt", "7.5", "-veyraStats", @"E:\s.json", "-veyraWarmup", "2", "-veyraOverlay" });
            Assert.AreEqual(20f, o.AutoQuitSeconds);
            Assert.AreEqual(@"E:\a b\shot.png", o.ScreenshotPath);
            Assert.AreEqual(7.5f, o.ScreenshotAtSeconds);
            Assert.AreEqual(@"E:\s.json", o.StatsPath);
            Assert.AreEqual(2f, o.WarmupSeconds);
            Assert.IsTrue(o.ShowOverlay);
            Assert.IsTrue(o.IsAutomatedRun);
        }

        [Test]
        public void IgnoresMalformedOrMissingValues()
        {
            var o = LaunchOptions.Parse(new[] { "-veyraAutoQuit", "abc", "-veyraScreenshot", "-veyraOverlay", "-veyraStats" });
            Assert.IsFalse(o.IsAutomatedRun);
            Assert.IsNull(o.ScreenshotPath, "a following flag must not be taken as a path");
            Assert.IsTrue(o.ShowOverlay);
            Assert.IsNull(o.StatsPath);
        }
    }

    public class FrameStatsTests
    {
        [Test]
        public void Summary_KnownSequence()
        {
            var s = new FrameStats();
            for (int i = 0; i < 98; i++) s.Add(10f);
            s.Add(60f);   // one hitch
            s.Add(100f);  // another hitch
            var r = s.Summarize();
            Assert.AreEqual(100, r.frames);
            Assert.AreEqual(11.4f, r.avgMs, 1e-3f);        // (98*10 + 60 + 100) / 100
            Assert.AreEqual(10f, r.p50Ms);
            Assert.AreEqual(10f, r.p95Ms);
            Assert.AreEqual(60f, r.p99Ms);                 // nearest rank: 99th of 100 sorted values
            Assert.AreEqual(100f, r.maxMs);
            Assert.AreEqual(2, r.hitchesOver50Ms);
            Assert.AreEqual(1000f / 11.4f, r.avgFps, 1e-2f);
        }

        [Test]
        public void IgnoresInvalidSamples_AndEmptyIsZero()
        {
            var s = new FrameStats();
            s.Add(0f); s.Add(-5f); s.Add(float.NaN); s.Add(float.PositiveInfinity);
            Assert.AreEqual(0, s.Count);
            Assert.AreEqual(0f, s.Summarize().avgFps);
        }

        [Test]
        public void Percentile_Bounds()
        {
            var list = new List<float> { 1f, 2f, 3f, 4f };
            Assert.AreEqual(1f, FrameStats.Percentile(list, 0.0));
            Assert.AreEqual(4f, FrameStats.Percentile(list, 1.0));
            Assert.AreEqual(2f, FrameStats.Percentile(list, 0.5));   // nearest rank: ceil(0.5*4) = 2nd value
            Assert.AreEqual(0f, FrameStats.Percentile(new List<float>(), 0.5));
        }
    }

    public class WorldCoordinatesTests
    {
        // Values copied from VEYRA_CITY_WORLD_DESIGN/06_DESIGN_DOCUMENTATION/location_registry.csv
        [TestCase(4170f, 2330f, 9.8f, 170f, -670f, "E4")]   // LM-01 Veyra Spire
        [TestCase(4030f, 2530f, 11.7f, 30f, -470f, "E4")]   // LM-02 Crown Plaza
        [TestCase(3700f, 2100f, 8.3f, -300f, -900f, "D4")]  // RACE-16 Midnight Crown Circuit start
        public void ToUnity_MatchesRegistry(float x, float y, float elev, float ux, float uz, string cell)
        {
            var u = WorldCoordinates.ToUnity(x, y, elev);
            Assert.AreEqual(ux, u.x, 1e-3f);
            Assert.AreEqual(elev, u.y, 1e-3f);
            Assert.AreEqual(uz, u.z, 1e-3f);
            Assert.AreEqual(cell, WorldCoordinates.GridCell(x, y));
            Assert.AreEqual(new Vector2(x, y), WorldCoordinates.FromUnity(u));
        }

        [Test]
        public void GridCell_CornersAndOutside()
        {
            Assert.AreEqual("A6", WorldCoordinates.GridCell(0f, 0f));        // south-west
            Assert.AreEqual("H1", WorldCoordinates.GridCell(7999f, 5999f));  // north-east
            Assert.IsNull(WorldCoordinates.GridCell(8000f, 10f));
            Assert.IsNull(WorldCoordinates.GridCell(-1f, 10f));
        }
    }

    public class BuildInfoTests
    {
        [Test]
        public void JsonRoundTrip()
        {
            var a = new BuildInfo { version = "0.1.0", gitCommit = "abcdef12", unityVersion = "6000.6.8f1", scriptingBackend = "IL2CPP", development = true };
            var b = BuildInfo.FromJson(a.ToJson());
            Assert.AreEqual(a.version, b.version);
            Assert.AreEqual(a.gitCommit, b.gitCommit);
            Assert.AreEqual(a.scriptingBackend, b.scriptingBackend);
            Assert.IsTrue(b.development);
        }

        [Test]
        public void InvalidJson_ReturnsNull()
        {
            Assert.IsNull(BuildInfo.FromJson(""));
            Assert.IsNull(BuildInfo.FromJson("not json"));
        }
    }

    public class BuildSetupTests
    {
        [Test]
        public void FoundationScene_IsInBuildSettings()
        {
            Assert.IsTrue(File.Exists(SceneTools.FoundationScenePath), "Run SceneTools.EnsureFoundationSceneBatch first");
            CollectionAssert.Contains(BuildScript.GetScenes(), SceneTools.FoundationScenePath);
        }

        [Test]
        public void IntegratedGpuDetection()
        {
            Assert.IsTrue(DeviceReport.IsLikelyIntegratedGpu("Intel", "Intel(R) UHD Graphics"));
            Assert.IsFalse(DeviceReport.IsLikelyIntegratedGpu("NVIDIA", "NVIDIA GeForce RTX 4070 Laptop GPU"));
            Assert.IsFalse(DeviceReport.IsLikelyIntegratedGpu("Intel", "Intel(R) Arc(TM) A770"));
        }
    }
}
