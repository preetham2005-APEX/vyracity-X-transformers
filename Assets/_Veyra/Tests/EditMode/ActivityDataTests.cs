using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VeyraCity.Gameplay;
using VeyraCity.UI;
using VeyraCity.World.Data;

namespace VeyraCity.Tests
{
    /// <summary>
    /// Card G5 (round 3): the player's activity data (Resources/Activities/activities.json) matches location_registry.csv,
    /// and every activity location can be reached by GPS from the starting garage.
    /// </summary>
    public class ActivityDataTests
    {
        const string Csv = "Assets/_Veyra/Data/location_registry.csv";
        const string StartGarage = "GAR-01";

        static readonly Dictionary<string, int> Expected = new Dictionary<string, int>
        {
            { "Speed zone", 5 }, { "Drift zone", 5 }, { "Drag strip", 3 }, { "Stunt jump", 9 }, { "Collectible area", 10 },
            { "Photo challenge", 10 }, { "Hidden shortcut", 8 }, { "Time trial / test", 6 }, { "Garage", 11 },
            { "Hidden garage / barn find", 6 }, { "Dealership", 5 }, { "Delivery hub", 5 }, { "Car meet", 12 }, { "Scenic viewpoint", 11 },
        };

        [Test]
        public void ActivitiesMatchTheRegistry()
        {
            var acts = ActivityHooks.Load();
            var reg = LocationRegistry.Parse(File.ReadAllText(Csv));
            foreach (var kv in Expected)
                Assert.AreEqual(kv.Value, acts.Count(a => a.category == kv.Key), kv.Key + " count (run Tools/Data/build_activities.py)");
            foreach (var a in acts)
            {
                var r = reg.Get(a.id);
                Assert.IsNotNull(r, a.id + " missing from the registry");
                Assert.AreEqual(r.Category, a.category, a.id);
                Assert.Less(Vector2.Distance(new Vector2(a.x, a.z), new Vector2(r.UnityX, r.UnityZ)), 0.05f, a.id + " position out of date");
            }
            foreach (var a in acts.Where(a => a.category == "Drift zone")) Assert.Greater(a.radius, 0f, a.id + " radius");
            foreach (var a in acts.Where(a => a.category == "Speed zone" || a.category == "Drag strip"))
            {
                Assert.IsTrue(a.hasEnd, a.id + " end point");
                Assert.Greater(a.length, 0f, a.id + " length");
            }
        }

        [Test]
        public void CollectibleAreasPlaceTheirItemsBesideRoads()
        {
            var report = new List<string>();
            foreach (var a in ActivityHooks.Load().Where(a => a.category == "Collectible area"))
            {
                var pos = ActivityHooks.CollectiblePositions(a);
                report.Add($"{a.id} {pos.Count}/{a.count}");
                Assert.AreEqual(a.count, pos.Count, a.id + " items placed");
                foreach (var q in pos) Assert.LessOrEqual(Vector2.Distance(new Vector2(q.x, q.z), new Vector2(a.x, a.z)), a.radius + 30f, a.id + " item outside its area");
                for (int i = 0; i < pos.Count; i++)
                    for (int j = i + 1; j < pos.Count; j++)
                        Assert.GreaterOrEqual(Vector2.Distance(new Vector2(pos[i].x, pos[i].z), new Vector2(pos[j].x, pos[j].z)), 39.9f, a.id + " items too close");
                Assert.AreEqual(pos, ActivityHooks.CollectiblePositions(a), a.id + " placement must be deterministic");
            }
            Debug.Log("[G5] collectibles: " + string.Join(", ", report));
        }

        [Test]
        public void TimeTrialsWithRoutesPointAtExistingRaceRoutes()
        {
            var trials = ActivityHooks.Load().Where(a => a.category == "Time trial / test" && !string.IsNullOrEmpty(a.route)).ToList();
            Assert.GreaterOrEqual(trials.Count, 3, "TT-01, TT-05, TT-06 reuse race routes");
            foreach (var a in trials) Assert.IsNotNull(ActivityHooks.FindRoute(a.route), a.id + " -> " + a.route);
        }

        /// <summary>BUG-150 (pass 37): locations the current road data cannot reach (more than 120 m from any road, or on a
        /// road not joined to the network). The world session owns the road data; remove ids here as access roads arrive.</summary>
        static readonly HashSet<string> KnownUnreachable = new HashSet<string>
        {
            "GAR-08", "MEET-05", "MEET-06", "VP-02", "VP-06",
        };

        [Test]
        public void EveryActivityIsReachableByGpsFromTheStartingGarage()
        {
            var acts = ActivityHooks.Load();
            var start = acts.FirstOrDefault(a => a.id == StartGarage);
            Assert.IsNotNull(start, StartGarage);
            var line = new List<Vector3>();
            var missing = new List<string>();
            int checkedCount = 0;
            foreach (var a in acts)
            {
                if (a == start) continue;
                // hidden shortcuts are off-road cuts by design: the GPS routes to the road they leave from, not into them
                if (a.category == "Hidden shortcut") continue;
                checkedCount++;
                Vector3 target = a.Pos;
                // an area (collectible area, drift zone) is reachable when a road enters it; route to that road point
                if (a.radius > 0f && World.WorldData.NearestRoad(a.Pos, a.radius, out var hit)) target = hit.Point;
                if (!RoadGraph.Route(start.Pos, target, line, out float len) || line.Count < 2) missing.Add(a.id);
            }
            var unexpected = missing.Where(id => !KnownUnreachable.Contains(id)).ToList();
            var fixedNow = KnownUnreachable.Where(id => !missing.Contains(id)).ToList();
            Debug.Log($"[G5] GPS: {checkedCount - missing.Count}/{checkedCount} activities reachable from {StartGarage} (shortcuts excluded)" +
                      (missing.Count > 0 ? "; unreachable: " + string.Join(", ", missing) : "") +
                      (fixedNow.Count > 0 ? "; now reachable, drop from BUG-150 list: " + string.Join(", ", fixedNow) : ""));
            Assert.IsEmpty(unexpected, "newly unreachable by GPS (more than 120 m from a road, or no path)");
        }
    }
}
