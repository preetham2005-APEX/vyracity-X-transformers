using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VeyraCity.World;
using VeyraCity.World.Data;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Phase B (W2): "Veyra/World/Apply Grass Masks". Removes instanced grass where a real city has none, on the terrain detail
    /// layers that <see cref="GrassBuilder"/> planted (authoring scene):
    /// <list type="bullet">
    /// <item>every building footprint from world_building_placements.csv (rotated rectangle + 2 m);</item>
    /// <item>D1 Crown Downtown and D6 Neon Mile: grass only on park lawn (Lawn splat ≥ 0.45), never on plazas or paving;</item>
    /// <item>D3 Ironport: grass only on lawn ≥ 0.6 (the port is hard standing);</item>
    /// <item>D7 Falcon Airfield: none within 90 m of a runway centre line (aprons, taxiways, runway strips).</item>
    /// </list>
    /// District polygons, footprints and runways come from the Veyra data only. Only ever lowers density, so running it twice
    /// gives the same result. Report: Logs/B1/grass_masks.txt.
    /// </summary>
    public static class GrassMasks
    {
        const int LawnLayer = 7, PavingLayer = 4;

        [MenuItem("Veyra/World/Apply Grass Masks")]
        static void Menu()
        {
            if (EditorSceneManager.GetActiveScene().path != CellDistributor.AuthoringScene)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(CellDistributor.AuthoringScene);
            }
            Debug.Log(Apply());
        }

        public static string Apply()
        {
            WorldData.EnsureLoaded();
            var sb = new StringBuilder("Grass masks\n");
            var buildings = PlacementReader.Buildings(VeyraDataValidator.LoadText("world_building_placements.csv"), "world_building_placements.csv");
            var runways = WorldData.Roads.Where(r => r.Data.cls == "RW").ToList();
            var districts = WorldData.Districts.districts;
            float[] Poly(string id) => districts.FirstOrDefault(d => d.id == id)?.polygon;
            var d1 = Poly("D1"); var d6 = Poly("D6"); var d3 = Poly("D3"); var d7 = Poly("D7");
            long before = 0, after = 0;
            int[] removedBy = new int[5];
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
            {
                var td = t.terrainData;
                if (td == null || td.detailPrototypes.Length == 0) continue;
                int res = td.detailWidth;
                var layer = td.GetDetailLayer(0, 0, res, res, 0);
                var am = td.GetAlphamaps(0, 0, td.alphamapWidth, td.alphamapHeight);
                var o = t.transform.position;
                var size = td.size;
                float cell = size.x / res;
                // building footprints rasterised once per tile
                var blocked = new bool[res, res];
                foreach (var b in buildings)
                {
                    float hx = b.FootprintWM * 0.5f + 2f, hz = b.FootprintLM * 0.5f + 2f;
                    float r = Mathf.Sqrt(hx * hx + hz * hz);
                    if (b.UnityX + r < o.x || b.UnityX - r > o.x + size.x || b.UnityZ + r < o.z || b.UnityZ - r > o.z + size.z) continue;
                    var rot = Quaternion.Euler(0f, b.UnityYawDeg, 0f);
                    var inv = Quaternion.Inverse(rot);
                    int x0 = Mathf.Max(0, (int)((b.UnityX - r - o.x) / cell)), x1 = Mathf.Min(res - 1, (int)((b.UnityX + r - o.x) / cell));
                    int z0 = Mathf.Max(0, (int)((b.UnityZ - r - o.z) / cell)), z1 = Mathf.Min(res - 1, (int)((b.UnityZ + r - o.z) / cell));
                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            var p = inv * new Vector3(o.x + (x + 0.5f) * cell - b.UnityX, 0f, o.z + (z + 0.5f) * cell - b.UnityZ);
                            if (Mathf.Abs(p.x) <= hx && Mathf.Abs(p.z) <= hz) blocked[z, x] = true;
                        }
                }
                for (int z = 0; z < res; z++)
                    for (int x = 0; x < res; x++)
                    {
                        int n = layer[z, x];
                        if (n == 0) continue;
                        before += n;
                        float px = o.x + (x + 0.5f) * cell, pz = o.z + (z + 0.5f) * cell;
                        int ax = Mathf.Min(td.alphamapWidth - 1, (int)((x + 0.5f) / res * td.alphamapWidth));
                        int ay = Mathf.Min(td.alphamapHeight - 1, (int)((z + 0.5f) / res * td.alphamapHeight));
                        float lawn = am[ay, ax, LawnLayer], paving = am[ay, ax, PavingLayer];
                        int why = -1;
                        if (blocked[z, x]) why = 0;
                        else if ((In(d1, px, pz) || In(d6, px, pz)) && (lawn < 0.45f || paving > 0.3f)) why = 1;
                        else if (In(d3, px, pz) && lawn < 0.6f) why = 2;
                        else if (In(d7, px, pz) && NearRunway(runways, px, pz, 90f)) why = 3;
                        else if (paving > 0.5f) why = 4;
                        if (why >= 0) { layer[z, x] = 0; removedBy[why] += n; }
                        else after += n;
                    }
                td.SetDetailLayer(0, 0, 0, layer);
                EditorUtility.SetDirty(td);
            }
            AssetDatabase.SaveAssets();
            sb.AppendLine($"grass instances {before:N0} -> {after:N0}");
            sb.AppendLine($"removed: building footprints {removedBy[0]:N0}, D1/D6 plazas {removedBy[1]:N0}, D3 port {removedBy[2]:N0}, D7 airfield {removedBy[3]:N0}, paving {removedBy[4]:N0}");
            Directory.CreateDirectory("Logs/B1");
            File.WriteAllText("Logs/B1/grass_masks.txt", sb.ToString());
            return sb.ToString();
        }

        static bool In(float[] poly, float ux, float uz)
        {
            return poly != null && WorldData.PointInPolygon(poly, ux, uz);   // district polygons are in Unity x, z (WorldData.DistrictAt)
        }

        static bool NearRunway(List<RoadPath> runways, float x, float z, float dist)
        {
            foreach (var r in runways)
            {
                var p = new Vector3(x, 0f, z);
                int hint = 0;
                float s = r.Path.Project(p, ref hint, 1 << 20);
                var q = r.Path.PositionAt(s);
                if (new Vector2(q.x - x, q.z - z).sqrMagnitude < (dist + r.HalfWidth) * (dist + r.HalfWidth)) return true;
            }
            return false;
        }
    }
}
