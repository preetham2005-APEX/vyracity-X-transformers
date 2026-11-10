using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VeyraCity.Assists;
using VeyraCity.EditorTools;
using VeyraCity.Gameplay;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.Tests
{
    /// <summary>
    /// Card B3: imported B2 models (world chunks and kitbash pieces) carry correct colliders, surfaces, materials and LODs,
    /// and a car drives across the chunk borders of the placed test chunks without falling through.
    /// </summary>
    public class ChunkImportTests
    {
        static readonly string[] Roots = { "Assets/_Veyra/Art/World/Chunks", "Assets/_Veyra/Art/Models/Kitbash" };

        static IEnumerable<(string path, GameObject root)> Models()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Model", Roots.Where(AssetDatabase.IsValidFolder).ToArray()))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go != null) yield return (p, go);
            }
        }

        [Test]
        public void ImportedModels_CollisionNeverRenders_DrivableHasSurface_MaterialsFromLibrary()
        {
            var lib = AssetDatabase.LoadAssetAtPath<VeyraMaterialLibrary>(VeyraMaterialFactory.LibraryPath);
            Assert.IsNotNull(lib, "material library");
            var libMats = new HashSet<Material>(lib.Slots.Where(s => s != null && s.Material != null).Select(s => s.Material));
            lib.TryGet(VeyraMat.DebugMissing, out var orange);
            int models = 0, col = 0, ucx = 0, ubx = 0, slots = 0, missing = 0, lodGroups = 0;
            var errors = new List<string>();
            foreach (var (path, root) in Models())
            {
                models++;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name;
                    var r = t.GetComponent<Renderer>();
                    if (n.StartsWith("COL_") || n.StartsWith("UCX_") || n.StartsWith("UBX_"))
                    {
                        if (r != null) errors.Add($"{path}: collision node {n} renders");
                        if (n.StartsWith("COL_"))
                        {
                            col++;
                            var mc = t.GetComponent<MeshCollider>();
                            if (mc == null || mc.convex || mc.sharedMesh == null) errors.Add($"{path}: {n} is not a non-convex MeshCollider");
                            var vs = t.GetComponent<VehicleSurface>();
                            if (vs == null || vs.Surface == null) errors.Add($"{path}: {n} has no SurfaceDefinition");
                        }
                        else if (n.StartsWith("UCX_")) { ucx++; var mc = t.GetComponent<MeshCollider>(); if (mc == null || !mc.convex) errors.Add($"{path}: {n} is not convex"); }
                        else { ubx++; if (t.GetComponent<BoxCollider>() == null) errors.Add($"{path}: {n} has no BoxCollider"); }
                        continue;
                    }
                    if (r == null) continue;
                    foreach (var m in r.sharedMaterials)
                    {
                        slots++;
                        if (m == null) errors.Add($"{path}: {n} has an empty material slot");
                        else if (m == orange) missing++;
                        else if (!libMats.Contains(m)) errors.Add($"{path}: {n} uses '{m.name}', not a library material");
                    }
                }
                foreach (var g in root.GetComponentsInChildren<LODGroup>(true))
                {
                    lodGroups++;
                    var lods = g.GetLODs();
                    if (lods.Length >= 3 && !Mathf.Approximately(lods[0].screenRelativeTransitionHeight, 0.35f))
                        errors.Add($"{path}: LOD0 transition {lods[0].screenRelativeTransitionHeight}, expected 0.35");
                }
            }
            string summary = $"{models} models, {lodGroups} LODGroups, colliders COL {col} / UCX {ucx} / UBX {ubx}, {slots} material slots, {missing} orange (unmapped)";
            Debug.Log("[B3] " + summary);
            if (models == 0) Assert.Ignore("no B2 models imported yet");
            Assert.IsEmpty(errors, string.Join("\n", errors.Take(30)));
            Assert.Less(missing, Mathf.Max(1, slots / 20), "more than 5 % of material slots fell back to M_Debug_Missing: " + summary);
        }

        [Test]
        public void TestChunks_CarCrossesChunkBorders_WithoutFallingThrough()
        {
            if (!File.Exists(ChunkPlacement.TestScene)) Assert.Ignore("run Veyra/World/Place Test Chunks first");
            var scene = EditorSceneManager.OpenScene(ChunkPlacement.TestScene, OpenSceneMode.Additive);
            var def = AssetDatabase.LoadAssetAtPath<VehicleDefinition>("Assets/_Veyra/Data/Vehicles/Kite_Stock_RWD.asset");
            var preset = AssetDatabase.LoadAssetAtPath<AssistPreset>("Assets/_Veyra/Data/Assists/Casual.asset");
            var surface = AssetDatabase.LoadAssetAtPath<SurfaceDefinition>("Assets/_Veyra/Data/Surfaces/dry_asphalt.asset");
            if (def == null || preset == null || surface == null) Assert.Ignore("vehicle data assets missing");
            var report = new StringBuilder();
            try
            {
                // B2 synthetic map: east-west road on z = -1500 (row border of D5), north-south road on x = -750 (column border).
                var runs = new[]
                {
                    (name: "EW x -900 -> -600 (crosses column border x -750)", from: new Vector3(-900f, 0f, -1503f), to: new Vector3(-600f, 0f, -1503f)),
                    (name: "NS z -1680 -> -1320 (crosses row border z -1500)", from: new Vector3(-747f, 0f, -1680f), to: new Vector3(-747f, 0f, -1320f)),
                };
                foreach (var run in runs)
                {
                    using var h = VehicleTestHarness.Create(def, preset, surface, 100f);
                    Physics.SyncTransforms();
                    Assert.IsTrue(RoadHit(run.from, out var start), $"{run.name}: no COL_Road under the start point");
                    var dir = (run.to - run.from).normalized;
                    h.Vehicle.ResetTo(start.point + Vector3.up * 0.6f, Quaternion.LookRotation(dir, Vector3.up));
                    h.Settle(1.5f);
                    bool spawnedOnRoad = RoadHit(h.Vehicle.transform.position, out _);
                    int falls = 0, offRoad = 0, steps = 0;
                    float maxDrop = 0f, travelled = 0f;
                    Vector3 last = h.Vehicle.transform.position;
                    float length = Vector3.Distance(run.from, run.to);
                    h.Script.Policy = v =>
                    {
                        var p = v.transform.position;
                        float along = Vector3.Dot(p - run.from, dir);
                        var target = run.from + dir * (along + 15f);
                        var to = target - p; to.y = 0f;
                        float ang = Vector3.SignedAngle(Vector3.ProjectOnPlane(v.transform.forward, Vector3.up), to, Vector3.up);
                        return new DriverIntent { Steer = Mathf.Clamp(ang / 25f, -1f, 1f), Throttle = v.State.Speed < 16.7f ? 0.7f : 0f };
                    };
                    while (Vector3.Dot(h.Vehicle.transform.position - run.from, dir) < length - 20f && h.Time < 40f)
                    {
                        h.Step();
                        steps++;
                        var p = h.Vehicle.transform.position;
                        travelled += Vector3.Distance(p, last);
                        last = p;
                        if (RoadHit(p, out var hit))
                        {
                            float drop = hit.point.y - p.y;
                            maxDrop = Mathf.Max(maxDrop, drop);
                            if (drop > 0.5f) falls++;
                        }
                        else offRoad++;
                    }
                    report.AppendLine($"{run.name}: spawned_on_road={spawnedOnRoad}, travelled {travelled:F0} m in {h.Time:F1} s, falls_below_surface={falls}, " +
                                      $"steps without COL_Road below={offRoad}/{steps}, max drop below surface {maxDrop:F2} m, final speed {h.Vehicle.State.Speed * 3.6f:F0} km/h");
                    Assert.IsTrue(spawnedOnRoad, run.name + ": not spawned on the road");
                    Assert.AreEqual(0, falls, run.name + ": fell below the road surface");
                    Assert.AreEqual(0, offRoad, run.name + ": left the COL_Road collider");
                    Assert.Greater(travelled, length - 60f, run.name + ": did not complete the run");
                }
            }
            finally
            {
                Debug.Log("[B3] chunk drive\n" + report);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        static bool RoadHit(Vector3 p, out RaycastHit best)
        {
            best = default;
            var hits = Physics.RaycastAll(new Vector3(p.x, p.y + 60f, p.z), Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore);
            float d = float.MaxValue;
            bool found = false;
            foreach (var hit in hits)
            {
                if (!hit.collider.name.StartsWith("COL_Road")) continue;
                if (hit.distance < d) { d = hit.distance; best = hit; found = true; }
            }
            return found;
        }
    }
}
