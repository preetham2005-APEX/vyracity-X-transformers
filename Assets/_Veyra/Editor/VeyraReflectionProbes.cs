using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Roadmap D1 / BUG-068: realtime box reflection probes for glass, wet roads and car paint. Positions come only from the
    /// Veyra data: a 400 m grid inside the D1 Crown Downtown and D6 Neon Mile polygons, and one probe per 400 m of every bridge
    /// run (road structure = Bridge) longer than 120 m. Heights: the terrain + 14 m in districts, the road profile
    /// + 6 m on bridges. Everything lives under VC_Lighting_D1/ReflectionProbes, which is rebuilt on every run (idempotent).
    /// </summary>
    public static class VeyraReflectionProbes
    {
        const string RootName = "VC_Lighting_D1";
        const float Cell = 400f;

        [MenuItem("Veyra/Lighting/Build Reflection Probes")]
        static void Menu() => Debug.Log(Build());

        public static string Build()
        {
            var log = new StringBuilder("D1 reflection probes\n");
            var scene = EditorSceneManager.GetActiveScene();
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName) ?? new GameObject(RootName);
            var old = root.transform.Find("ReflectionProbes");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var holder = new GameObject("ReflectionProbes").transform;
            holder.SetParent(root.transform, false);
            var director = root.GetComponent<ReflectionProbeDirector>();
            if (director == null) director = root.AddComponent<ReflectionProbeDirector>();   // GetComponent returns a fake null: no ??
            director.Probes.Clear();

            WorldData.EnsureLoaded();
            foreach (var id in new[] { "D1", "D6" })
            {
                var d = WorldData.Districts?.districts?.FirstOrDefault(x => x.id != null && x.id.StartsWith(id));
                if (d == null || d.polygon == null) { log.AppendLine($"district {id} not found"); continue; }
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                for (int i = 0; i + 1 < d.polygon.Length; i += 2)
                {
                    minX = Mathf.Min(minX, d.polygon[i]); maxX = Mathf.Max(maxX, d.polygon[i]);
                    minZ = Mathf.Min(minZ, d.polygon[i + 1]); maxZ = Mathf.Max(maxZ, d.polygon[i + 1]);
                }
                int n = 0;
                for (float x = minX + Cell * 0.5f; x < maxX; x += Cell)
                    for (float z = minZ + Cell * 0.5f; z < maxZ; z += Cell)
                    {
                        if (!WorldData.PointInPolygon(d.polygon, x, z)) continue;
                        float y = TerrainHeight(x, z);
                        director.Probes.Add(Probe(holder, $"RP_{id}_{n++}", new Vector3(x, y + 14f, z), new Vector3(Cell + 40f, 160f, Cell + 40f), 1));
                    }
                log.AppendLine($"{id} {d.name}: {n} probes");
            }

            int bridges = 0;
            foreach (var r in WorldData.Roads)
            {
                var pts = r.Path.Points;
                int i = 0;
                while (i < pts.Length - 1)
                {
                    if (r.StructureAt(i) != StructureKind.Bridge) { i++; continue; }
                    int j = i;
                    while (j < pts.Length - 1 && r.StructureAt(j) == StructureKind.Bridge) j++;
                    float len = r.Path.S[j] - r.Path.S[i];
                    if (len > 120f)
                    {
                        int count = Mathf.Max(1, Mathf.RoundToInt(len / Cell));
                        for (int k = 0; k < count; k++)
                        {
                            float s = r.Path.S[i] + len * (k + 0.5f) / count;
                            var p = r.Path.PositionAt(s);
                            float seg = len / count + 40f;
                            director.Probes.Add(Probe(holder, $"RP_Bridge_{r.Data.id}_{bridges++}", p + Vector3.up * 6f, new Vector3(seg, 80f, seg), 2));
                        }
                    }
                    i = j + 1;
                }
            }
            log.AppendLine($"bridges: {bridges} probes");
            log.AppendLine($"total {director.Probes.Count} probes under {RootName}/ReflectionProbes");
            EditorUtility.SetDirty(director);
            EditorSceneManager.MarkSceneDirty(scene);
            return log.ToString();
        }

        /// <summary>Terrain height (the Veyra heightmap tiles) at x, z; 0 outside every tile. Not a raycast: from above that
        /// would hit roofs in Downtown.</summary>
        static float TerrainHeight(float x, float z)
        {
            var p = new Vector3(x, 0f, z);
            foreach (var t in Terrain.activeTerrains)
            {
                var o = t.transform.position;
                var size = t.terrainData.size;
                if (x >= o.x && z >= o.z && x <= o.x + size.x && z <= o.z + size.z) return t.SampleHeight(p) + o.y;
            }
            return 0f;
        }

        static ReflectionProbe Probe(Transform parent, string name, Vector3 pos, Vector3 size, int importance)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var p = go.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Realtime;
            p.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            p.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            p.boxProjection = true;
            p.size = size;
            p.blendDistance = 20f;
            p.importance = importance;
            p.resolution = 128;
            p.hdr = true;
            p.nearClipPlane = 1f;
            p.farClipPlane = 600f;   // D1: 600 m is enough for box-projected city reflections and halves the per-face cost
            p.shadowDistance = 0f;
            p.clearFlags = ReflectionProbeClearFlags.Skybox;
            return p;
        }
    }
}
