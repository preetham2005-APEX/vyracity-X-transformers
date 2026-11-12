using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// World iteration 28 (CAM_18 Mirror Lake, CAM_09: open meadows read as dark bare soil with scattered grass "stickers": the
    /// splat put the forest-floor layer (needles and twigs, 18 % albedo, 35 % grass density) under treeless ground).
    /// WorldBuildBatch step `meadow` (run `regrass` after it): on every terrain, where the forest-floor weight is above 0.2, no
    /// tree of the placement data stands within 18 m (InstancedPropRenderer tree prototypes) and the slope is under 28°,
    /// 85 % of the forest-floor weight moves to the grass layer. Under and near trees nothing changes. Idempotent (a second
    /// run finds little forest floor left to move). Iteration 29 (CAM_14 headland: grass texture smeared down the cliff): slopes
    /// over 32° blend to the rock layer (full at 44°). Report Logs/C/meadow.txt.
    /// </summary>
    public static class MeadowPass
    {
        const float TreeClear = 18f, Move = 0.85f;

        public static string Run()
        {
            var ipr = Object.FindAnyObjectByType<InstancedPropRenderer>();
            if (ipr == null) return "meadow: no InstancedPropRenderer\n";
            var trees = new HashSet<long>();   // 10 m cells holding a tree
            long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
            int nTrees = 0;
            foreach (var cell in ipr.Cells)
            {
                if (cell.Placements == null || cell.Prototype < 0 || cell.Prototype >= ipr.Prototypes.Length) continue;
                string n = ipr.Prototypes[cell.Prototype].Name.ToLowerInvariant();
                if (!(n.Contains("tree") || n.Contains("pine") || n.Contains("palm"))) continue;
                foreach (var p in cell.Placements) { trees.Add(Key(Mathf.FloorToInt(p.x / 10f), Mathf.FloorToInt(p.z / 10f))); nTrees++; }
            }
            bool NearTree(float x, float z)
            {
                int cx = Mathf.FloorToInt(x / 10f), cz = Mathf.FloorToInt(z / 10f), r = Mathf.CeilToInt(TreeClear / 10f);
                for (int dx = -r; dx <= r; dx++)
                    for (int dz = -r; dz <= r; dz++)
                        if (trees.Contains(Key(cx + dx, cz + dz))) return true;
                return false;
            }
            int terrains = 0; long moved = 0, texels = 0, rocked = 0;
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
            {
                var td = t.terrainData;
                var layers = td.terrainLayers;
                int GrassLayer = System.Array.FindIndex(layers, l => l != null && l.name == "L_Grass");
                int ForestLayer = System.Array.FindIndex(layers, l => l != null && l.name == "L_ForestFloor");
                int RockLayer = System.Array.FindIndex(layers, l => l != null && l.name == "L_Rock");
                if (GrassLayer < 0 || ForestLayer < 0) continue;
                int w = td.alphamapWidth, h = td.alphamapHeight;
                var am = td.GetAlphamaps(0, 0, w, h);
                var o = t.transform.position; var size = td.size;
                bool any = false;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float f = am[y, x, ForestLayer];
                        texels++;
                        // cliffs: above 32° the vertical-projected grass/soil smears down the face; blend to rock up to 44°
                        if (RockLayer >= 0)
                        {
                            float st = td.GetSteepness((x + 0.5f) / w, (y + 0.5f) / h);
                            float k = Mathf.Clamp01((st - 32f) / 12f);
                            if (k > 0f && am[y, x, RockLayer] < k)
                            {
                                float rest = 1f - am[y, x, RockLayer], take = k - am[y, x, RockLayer];
                                for (int l = 0; l < td.alphamapLayers; l++)
                                    if (l != RockLayer && rest > 1e-4f) am[y, x, l] *= 1f - take / rest;
                                am[y, x, RockLayer] = k;
                                rocked++; any = true;
                                continue;
                            }
                        }
                        if (f < 0.2f) continue;
                        float nx = (x + 0.5f) / w, ny = (y + 0.5f) / h;
                        if (td.GetSteepness(nx, ny) > 28f) continue;
                        if (NearTree(o.x + nx * size.x, o.z + ny * size.z)) continue;
                        float m = f * Move;
                        am[y, x, ForestLayer] = f - m;
                        am[y, x, GrassLayer] += m;
                        moved++; any = true;
                    }
                if (any) { td.SetAlphamaps(0, 0, am); EditorUtility.SetDirty(td); terrains++; }
            }
            AssetDatabase.SaveAssets();
            string rep = $"Meadow pass: {nTrees:N0} data trees; forest floor turned to grass on {moved:N0} of {texels:N0} splat texels in {terrains} terrains (no tree within {TreeClear} m, slope < 28°); {rocked:N0} steep texels (> 32°) blended to rock\n";
            Directory.CreateDirectory("Logs/C");
            File.WriteAllText("Logs/C/meadow.txt", rep);
            return rep;
        }
    }
}
