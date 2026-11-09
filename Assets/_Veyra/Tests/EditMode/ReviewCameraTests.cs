using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VeyraCity.EditorTools;
using VeyraCity.Testing;
using VeyraCity.World.Data;

namespace VeyraCity.Tests
{
    /// <summary>Card S5: review cameras, placement maths, pixel font and contact sheet.</summary>
    public class ReviewCameraTests
    {
        [Test]
        public void PixelFont_DrawsEveryGlyph()
        {
            const string all = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-.:()/,+ ?abc~";
            var px = new Color32[400 * 20];
            Assert.DoesNotThrow(() => PixelFont.Draw(px, 400, 20, all, 0, 0, 1, new Color32(255, 255, 255, 255)));
            Assert.Greater(px.Count(p => p.r == 255), 200);
            Assert.AreEqual((all.Length * 6 - 1) * 2, PixelFont.Measure(all, 2));
        }

        [Test]
        public void AppendixE_Has18CamerasAsSpecified()
        {
            var cams = ReviewCameraTools.AppendixE();
            Assert.AreEqual(18, cams.Count);
            Assert.AreEqual(18, cams.Select(c => c.name).Distinct().Count());
            Assert.AreEqual(6, cams.Count(c => c.kind == ReviewCameraSet.Kind.Chase));
            Assert.AreEqual(new[] { "A1", "A2", "R1", "A18", "H2", "A7" }, cams.Where(c => c.kind == ReviewCameraSet.Kind.Chase).Select(c => c.roadId).ToArray());
            var c3 = cams[2]; var c4 = cams[3];
            Assert.AreEqual(c3.position, c4.position); Assert.AreEqual(c3.lookAt, c4.lookAt);
            Assert.AreEqual(ReviewCameraSet.TimeLabel.Night, c4.time);
            Assert.IsTrue(cams.Single(c => c.name.StartsWith("CAM_07")).wet && cams.Single(c => c.name.StartsWith("CAM_13")).wet);
            Assert.AreEqual(7.5f, cams.Single(c => c.name.StartsWith("CAM_18")).hour);
            Assert.IsTrue(cams.Single(c => c.name.StartsWith("CAM_05")).heightAboveGround);
            Assert.IsFalse(cams.Single(c => c.name.StartsWith("CAM_01")).heightAboveGround);
        }

        [Test]
        public void CreateAll_ResolvesCompassDirectionsFromTheRoads()
        {
            ReviewCameraTools.CreateAll();
            var set = AssetDatabase.LoadAssetAtPath<ReviewCameraSet>(ReviewCameraTools.AssetPath);
            Assert.IsNotNull(set);
            var cfg = AssetDatabase.LoadAssetAtPath<VeyraAutoTestConfig>(TestRouteTools.ConfigPath);
            Assert.AreEqual(set, cfg.ReviewCameras, "the runtime config must reference the camera set");
            var net = RoadNetwork.Parse(VeyraDataValidator.LoadText("road_network_geometry.json"));
            Vector3 Travel(ReviewCameraSet.Entry e) => RouteAutopilot.RoadPath(net.Road(e.roadId)).TangentAt(e.distanceAlongRoad_m) * e.direction;
            Assert.Greater(Travel(set.Find("CAM_06_DT_Drive_Crown")).z, 0.5f, "CAM_06 drives northbound (+Z)");
            Assert.Greater(Travel(set.Find("CAM_07_DT_Drive_Meridian_Night")).x, 0.5f, "CAM_07 drives eastbound (+X)");
            Assert.Less(Travel(set.Find("CAM_12_H2_Highline_Golden")).x, 0f, "CAM_12 drives westbound (-X)");
            Assert.IsTrue(set.Cameras.Where(c => c.kind == ReviewCameraSet.Kind.Chase).All(c => c.direction == 1 || c.direction == -1));
        }

        [Test]
        public void FixedPose_LooksAtItsTarget()
        {
            var e = new ReviewCameraSet.Entry { kind = ReviewCameraSet.Kind.Fixed, position = new Vector3(-200f, 3800f, -6200f), lookAt = new Vector3(0f, 0f, 300f), fov = 55f };
            var p = ReviewCameraRig.Pose(e, null);
            Assert.IsTrue(p.ok);
            Assert.AreEqual(e.position, p.camPos);
            Assert.Greater(Vector3.Dot(p.camRot * Vector3.forward, (e.lookAt - e.position).normalized), 0.9999f);
            Assert.AreEqual(55f, p.fov);
            Assert.IsFalse(p.hasCar);
        }

        [Test]
        public void ContactSheet_Is6By3For18Shots()
        {
            var shots = new List<(string, Texture2D)>();
            for (int i = 0; i < 18; i++) shots.Add(($"CAM_{i + 1:00}", new Texture2D(64, 36, TextureFormat.RGB24, false)));
            var sheet = ReviewCameraRig.ContactSheet(shots, "TEST");
            int gap = 6;
            Assert.AreEqual(6 * ReviewCameraRig.ThumbWidth + 7 * gap, sheet.width);
            Assert.AreEqual(46 + 3 * (ReviewCameraRig.ThumbHeight + ReviewCameraRig.LabelBar) + 4 * gap, sheet.height);
            foreach (var s in shots) Object.DestroyImmediate(s.Item2);
            Object.DestroyImmediate(sheet);
        }
    }
}
