using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card C2 (BUG-001): puts the eased profile (<see cref="RoadProfileEaser"/>) into the built city.
    /// <para>Why not move the blockout vertices: the blockout ramps each 3–8.5 m abutment step over ~12 m (70 % grade) with
    /// coarse triangles, so moving vertices leaves spikes and dips (tried: max step 1.9–2.7 m afterwards). Instead each eased
    /// zone gets a new carriageway ribbon exactly on the eased profile (asphalt render, non-convex MeshCollider with the road's
    /// surface, 0.6 m skirt), built by <see cref="BridgeDeckFiller.Build"/> under World/RoadFixes as Ease_&lt;road&gt;_&lt;s&gt;.
    /// The eased approach is always at or above the old surface, so the old ramp ends up underneath it.</para>
    /// <list type="bullet">
    /// <item>Instanced props in the road corridor (lamps, signs) move by the profile delta.</item>
    /// <item>Terrain embankments (card step 5): where an approach was raised, the terrain under the corridor (half width + 4 m)
    /// is lifted to 0.3 m below the road and blended out at 1:2, burying the old ramp. Each touched TerrainData is backed up first
    /// to Assets/_Veyra/World/Generated/TerrainBackup~ (ignored by Unity); terrain is only ever raised, so a re-run changes
    /// nothing.</item>
    /// </list>
    /// Idempotent: ribbons are rebuilt by name; props move only when they are not already at the eased height
    /// (<see cref="Marker"/> lists the props already moved).
    /// </summary>
    public static class RoadProfileApply
    {
        const string Backup = "Assets/_Veyra/World/Generated/TerrainBackup~";
        /// <summary>Records that the props were moved (CityBuilder deletes it when it rebuilds the city).</summary>
        public const string Marker = "Assets/_Veyra/World/Generated/eased_props.txt";

        [MenuItem("Veyra/Physics/Apply Eased Road Profile")]
        static void Menu() => Debug.Log(Apply(true));

        public static string Apply() => Apply(true);

        /// <summary>Eases the open city scene (CityBuilder calls it with <paramref name="save"/> false right after building).</summary>
        public static string Apply(bool save)
        {
            var log = new StringBuilder("C2 apply eased road profile\n");
            RoadProfileEaser.Reset();
            WorldData.EnsureLoaded();
            var scene = EditorSceneManager.GetActiveScene();
            GameObject world = null;
            foreach (var r in scene.GetRootGameObjects()) if (r.name == "World") world = r;
            if (world == null) return "no World root in the open scene";
            var fixes = world.transform.Find("RoadFixes");
            if (fixes == null) { fixes = new GameObject("RoadFixes").transform; fixes.SetParent(world.transform, false); fixes.gameObject.isStatic = true; }
            for (int i = fixes.childCount - 1; i >= 0; i--) if (fixes.GetChild(i).name.StartsWith("Ease_") || fixes.GetChild(i).name.StartsWith("Patch_")) Object.DestroyImmediate(fixes.GetChild(i).gameObject);

            var lib = AssetDatabase.LoadAssetAtPath<VeyraMaterialLibrary>(VeyraMaterialFactory.LibraryPath);
            var asphalt = lib != null ? lib.Get(VeyraMat.RoadAsphaltNew) : null;
            var concrete = lib != null ? lib.Get(VeyraMat.RoadConcreteDeck) : null;
            var dry = AssetDatabase.LoadAssetAtPath<SurfaceDefinition>("Assets/_Veyra/Data/Surfaces/dry_asphalt.asset");
            var gravel = AssetDatabase.LoadAssetAtPath<SurfaceDefinition>("Assets/_Veyra/Data/Surfaces/gravel.asset");
            var concreteSurface = AssetDatabase.LoadAssetAtPath<SurfaceDefinition>("Assets/_Veyra/Data/Surfaces/concrete.asset");
            int ribbons = 0;
            float total = 0f;
            var keep = new HashSet<string>();
            for (int r = 0; r < WorldData.Roads.Count; r++)
            {
                var e = RoadProfileEaser.For(r);
                if (e.EasedJoints == 0) continue;
                var road = WorldData.Roads[r];
                foreach (var z in e.Zones)
                {
                    float a = Mathf.Max(0f, z.x), b = Mathf.Min(road.Path.Length, z.y);
                    if (b - a < 4f) continue;
                    string name = $"Ease_{road.Data.id}_{Mathf.RoundToInt(a)}";
                    // 2 cm above the old surface where they meet, so the ends never z-fight and the wheels see the ribbon first.
                    BridgeDeckFiller.Build(fixes, name, r, a, b, asphalt, concrete, road.Data.cls == "GR" ? gravel : dry, concreteSurface, false, 0.6f, 0.032f, EndMatch(r, a, b, 0.032f, null));
                    keep.Add(name);
                    ribbons++;
                    total += b - a;
                    log.AppendLine($"{name}: s {a:F0}..{b:F0} ({b - a:F0} m), max raise {MaxDelta(r, a, b):F2} m");
                }
            }
            AssetDatabase.SaveAssets();
            log.AppendLine($"eased ribbons: {ribbons}, total {total:F0} m");
            log.Append(PatchArtefacts(fixes, asphalt, concrete, dry, gravel, concreteSurface, keep));
            foreach (var f in Directory.GetFiles("Assets/_Veyra/World/Generated/Fixes", "*.asset"))
            {
                if (Path.GetFileName(f).StartsWith("Deck_")) continue;
                string n = Path.GetFileNameWithoutExtension(f);
                string baseName = n.EndsWith("_col") || n.EndsWith("_par") ? n.Substring(0, n.Length - 4) : n;
                if (!keep.Contains(baseName)) AssetDatabase.DeleteAsset(f.Replace(Path.DirectorySeparatorChar, '/'));
            }
            AssetDatabase.SaveAssets();

            // Props in the road corridor move with the road (once).
            int props = 0;
            var ipr = Object.FindAnyObjectByType<InstancedPropRenderer>();
            if (ipr != null && !File.Exists(Marker))
            {
                foreach (var cell in ipr.Cells)
                    for (int i = 0; i < cell.Placements.Length; i++)
                    {
                        var p = cell.Placements[i];
                        var v = new Vector3(p.x, p.y, p.z);
                        if (!WorldData.NearestRoad(v, 30f, out var hit) || hit.Distance > hit.RoadPath.HalfWidth + 8f || Mathf.Abs(v.y - hit.Point.y) > 3f) continue;
                        if (!RoadProfileEaser.InZone(hit.Road, hit.S)) continue;
                        float d = RoadProfileEaser.Delta(hit.Road, hit.S);
                        if (Mathf.Abs(d) < 0.01f) continue;
                        p.y += d;
                        cell.Placements[i] = p;
                        props++;
                    }
                EditorUtility.SetDirty(ipr);
                File.WriteAllText(Marker, $"props moved by C2: {props}\n");
            }
            log.AppendLine(File.Exists(Marker) && props == 0 ? "instanced props: already moved (marker present)" : $"instanced props moved with the road: {props}");
            log.Append(Embankments());
            log.Append(CarveCorridors());
            EditorSceneManager.MarkSceneDirty(scene);
            if (save) EditorSceneManager.SaveScene(scene);
            return log.ToString();
        }

        /// <summary>
        /// Local blockout artefacts (portal lips, junction dips and bumps) that are not profile steps: the centre-line collider is
        /// measured every 1 m; where it steps by more than 0.25 m while the eased profile changes by less than 0.1 m, a ribbon
        /// covers ±15 m on the profile (filling dips) and rises over any bump up to 1.5 m with 8 m smooth ramps each side.
        /// </summary>
        static string PatchArtefacts(Transform fixes, Material asphalt, Material concrete, SurfaceDefinition dry, SurfaceDefinition gravel, SurfaceDefinition concreteSurface, HashSet<string> keep)
        {
            Physics.SyncTransforms();
            var sb = new StringBuilder();
            int patches = 0, skippedHigh = 0;
            for (int r = 0; r < WorldData.Roads.Count; r++)
            {
                var road = WorldData.Roads[r];
                if (road.Data.cls == "RW") continue;
                int n = Mathf.FloorToInt(road.Path.Length);
                var y = new float[n + 1];
                var ok = new bool[n + 1];
                var eased = new float[n + 1];
                for (int i = 0; i <= n; i++)
                {
                    var c = road.Path.PositionAt(i);
                    eased[i] = c.y + RoadProfileEaser.Delta(r, i);
                    c.y = eased[i];
                    ok[i] = RoadProfileValidator.ColliderHeight(c, out y[i]);
                }
                var windows = new List<Vector2>();
                for (int i = 1; i <= n; i++)
                    if (ok[i] && ok[i - 1] && Mathf.Abs(y[i] - y[i - 1]) > 0.25f && Mathf.Abs(eased[i] - eased[i - 1]) < 0.1f)
                    {
                        var w = new Vector2(Mathf.Max(0f, i - 15f), Mathf.Min(n, i + 15f));
                        if (windows.Count > 0 && w.x <= windows[windows.Count - 1].y) windows[windows.Count - 1] = new Vector2(windows[windows.Count - 1].x, w.y);
                        else windows.Add(w);
                    }
                foreach (var w in windows)
                {
                    float bump = 0f;
                    for (int i = (int)w.x; i <= (int)w.y; i++) if (ok[i]) bump = Mathf.Max(bump, y[i] - eased[i] - 0.012f);
                    if (bump > 1.5f) { skippedHigh++; continue; } // not a lip: a structure over the road (left to C3 / the validator)
                    float a = w.x - 8f, b = w.y + 8f;
                    a = Mathf.Max(0f, a); b = Mathf.Min(road.Path.Length, b);
                    float rise = Mathf.Max(0f, bump);
                    System.Func<float, float> extra = s =>
                    {
                        float tIn = Mathf.Clamp01((s - a) / 8f), tOut = Mathf.Clamp01((b - s) / 8f);
                        float t = Mathf.Min(tIn, tOut);
                        return rise * t * t * (3f - 2f * t);
                    };
                    string name = $"Patch_{road.Data.id}_{Mathf.RoundToInt(a)}";
                    BridgeDeckFiller.Build(fixes, name, r, a, b, asphalt, concrete, road.Data.cls == "GR" ? gravel : dry, concreteSurface, false, 0.6f, 0.032f, EndMatch(r, a, b, 0.032f, extra));
                    keep.Add(name);
                    patches++;
                    sb.AppendLine($"{name}: s {a:F0}..{b:F0}, hump {rise:F2} m");
                }
            }
            sb.AppendLine($"artefact patches: {patches} (skipped {skippedHigh} with a rise above 1.5 m)");
            return sb.ToString();
        }

        /// <summary>
        /// BUG-108: a ribbon that ends at the eased profile (+3 cm) can meet an old surface a little higher or lower (blockout
        /// crown, deck kerb): a lip. The old drivable surface is measured at both ends (left / centre / right; only hits within
        /// ±0.3 m of the ribbon count, a bigger difference is another structure) and the ribbon end moves up or down by at most
        /// 0.25 m to meet it, over a 12 m smoothstep. Returns the combined height offset on top of <paramref name="inner"/>.
        /// </summary>
        static System.Func<float, float> EndMatch(int r, float a, float b, float raise, System.Func<float, float> inner)
        {
            var road = WorldData.Roads[r];
            float Ribbon(float s) => road.Path.PositionAt(s).y + RoadProfileEaser.Delta(r, s) + raise + (inner != null ? inner(s) : 0f);
            float Rise(float s)
            {
                var c = road.Path.PositionAt(s);
                var d = road.Path.PositionAt(Mathf.Min(road.Path.Length, s + 1f)) - road.Path.PositionAt(Mathf.Max(0f, s - 1f));
                var right = new Vector3(d.z, 0f, -d.x).normalized;
                float rib = Ribbon(s), top = float.MinValue;
                foreach (float o in new[] { -road.HalfWidth * 0.6f, 0f, road.HalfWidth * 0.6f })
                {
                    var p = c + right * o;
                    foreach (var h in Physics.RaycastAll(new Vector3(p.x, rib + 0.3f, p.z), Vector3.down, 0.6f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (h.collider is TerrainCollider || h.collider.GetComponentInParent<VehicleSurface>() == null) continue;
                        top = Mathf.Max(top, h.point.y);
                    }
                }
                return top == float.MinValue ? 0f : Mathf.Clamp(top - rib, -0.25f, 0.25f);
            }
            float riseA = Rise(a + 0.5f), riseB = Rise(b - 0.5f);
            return s =>
            {
                float tA = 1f - Mathf.Clamp01((s - a) / 12f), tB = 1f - Mathf.Clamp01((b - s) / 12f);
                return riseA * tA * tA * (3f - 2f * tA) + riseB * tB * tB * (3f - 2f * tB) + (inner != null ? inner(s) : 0f);
            };
        }

        static float MaxDelta(int r, float a, float b)
        {
            float m = 0f;
            for (float s = a; s <= b; s += 2f) m = Mathf.Max(m, Mathf.Abs(RoadProfileEaser.Delta(r, s)));
            return m;
        }

        /// <summary>Raises terrain under raised approaches (never lowers it): road − 0.3 m inside the corridor, 1:2 outside.</summary>
        static string Embankments()
        {
            var terrains = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude);
            var touched = new HashSet<TerrainData>();
            var heights = new Dictionary<TerrainData, float[,]>();
            // Per texel, the target of the NEAREST profile sample (a max over all samples within reach would lift the terrain
            // above a climbing road by up to grade x corridor radius).
            var best = new Dictionary<(TerrainData, int, int), Vector2>(); // (distance, target height)
            int texels = 0;
            for (int r = 0; r < WorldData.Roads.Count; r++)
            {
                var e = RoadProfileEaser.For(r);
                if (e.EasedJoints == 0) continue;
                var road = WorldData.Roads[r];
                var pts = road.Path.Points;
                float inner = road.HalfWidth + 4f;
                for (int i = 0; i < pts.Length; i++)
                {
                    float d = e.Delta[i];
                    if (d < 0.3f) continue;
                    float roadY = pts[i].y + d;
                    float outer = inner + 2f * (d + 1f);
                    foreach (var t in terrains)
                    {
                        var td = t.terrainData;
                        var tp = t.transform.position;
                        var size = td.size;
                        if (pts[i].x < tp.x - outer || pts[i].x > tp.x + size.x + outer || pts[i].z < tp.z - outer || pts[i].z > tp.z + size.z + outer) continue;
                        int res = td.heightmapResolution;
                        if (!heights.TryGetValue(td, out var h))
                        {
                            BackupTerrain(td);
                            h = td.GetHeights(0, 0, res, res);
                            heights[td] = h;
                            touched.Add(td);
                        }
                        float cell = size.x / (res - 1);
                        int x0 = Mathf.Max(0, Mathf.FloorToInt((pts[i].x - outer - tp.x) / cell)), x1 = Mathf.Min(res - 1, Mathf.CeilToInt((pts[i].x + outer - tp.x) / cell));
                        int z0 = Mathf.Max(0, Mathf.FloorToInt((pts[i].z - outer - tp.z) / cell)), z1 = Mathf.Min(res - 1, Mathf.CeilToInt((pts[i].z + outer - tp.z) / cell));
                        for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            float wx = tp.x + x * cell, wz = tp.z + z * cell;
                            float dist = Mathf.Sqrt((wx - pts[i].x) * (wx - pts[i].x) + (wz - pts[i].z) * (wz - pts[i].z));
                            if (dist > outer) continue;
                            float target = roadY - 0.3f - Mathf.Max(0f, dist - inner) * 0.5f;
                            // Never bury another road (a street beside or under the approach, the runway next to a taxiway).
                            if (WorldData.NearestRoad(new Vector3(wx, tp.y + h[z, x] * size.y, wz), 30f, out var other) && other.Road != r && other.Distance < other.RoadPath.HalfWidth + 2f) continue;
                            var key = (td, z, x);
                            if (!best.TryGetValue(key, out var bt) || dist < bt.x) best[key] = new Vector2(dist, target);
                        }
                    }
                }
            }
            foreach (var kv in best)
            {
                var (td, z, x) = kv.Key;
                var h = heights[td];
                float cur = h[z, x] * td.size.y;
                float target = kv.Value.y - TerrainY(terrains, td);
                if (target <= cur + 0.02f) continue; // 16-bit heights: 560 m / 65535 = 8.5 mm steps
                h[z, x] = Mathf.Clamp01(target / td.size.y);
                texels++;
            }
            foreach (var kv in heights)
            {
                kv.Key.SetHeights(0, 0, kv.Value);
                EditorUtility.SetDirty(kv.Key);
            }
            AssetDatabase.SaveAssets();
            return $"terrain embankments: {texels:N0} height samples raised on {touched.Count} terrain tile(s) (backups in {Backup})\n";
        }

        /// <summary>
        /// Card C2 step 5 (and BUG-049, walls at tunnel portals): on every road, terrain inside the carriageway corridor (half width
        /// + 1 m) that intrudes into the driving envelope (between road − 0.3 m and road + 6 m) is lowered to road − 0.3 m. Terrain far
        /// above (the mountain over a tunnel) and below the road is untouched; each sample follows its nearest road. Idempotent.
        /// </summary>
        static string CarveCorridors()
        {
            var terrains = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude);
            var heights = new Dictionary<TerrainData, float[,]>();
            var target = new Dictionary<(TerrainData, int, int), Vector2>(); // (distance, height) of the nearest road sample
            for (int r = 0; r < WorldData.Roads.Count; r++)
            {
                var road = WorldData.Roads[r];
                if (road.Data.cls == "RW") continue;
                float inner = road.HalfWidth + 1f;
                var pts = road.Path.Points;
                for (int i = 0; i < pts.Length; i++)
                {
                    float roadY = pts[i].y + RoadProfileEaser.For(r).Delta[i];
                    foreach (var t in terrains)
                    {
                        var td = t.terrainData; var tp = t.transform.position; var size = td.size;
                        if (pts[i].x < tp.x - inner || pts[i].x > tp.x + size.x + inner || pts[i].z < tp.z - inner || pts[i].z > tp.z + size.z + inner) continue;
                        int res = td.heightmapResolution;
                        if (!heights.TryGetValue(td, out var h)) { BackupTerrain(td); h = td.GetHeights(0, 0, res, res); heights[td] = h; }
                        float cell = size.x / (res - 1);
                        int x0 = Mathf.Max(0, Mathf.FloorToInt((pts[i].x - inner - tp.x) / cell)), x1 = Mathf.Min(res - 1, Mathf.CeilToInt((pts[i].x + inner - tp.x) / cell));
                        int z0 = Mathf.Max(0, Mathf.FloorToInt((pts[i].z - inner - tp.z) / cell)), z1 = Mathf.Min(res - 1, Mathf.CeilToInt((pts[i].z + inner - tp.z) / cell));
                        for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            float wx = tp.x + x * cell, wz = tp.z + z * cell;
                            float dist = Mathf.Sqrt((wx - pts[i].x) * (wx - pts[i].x) + (wz - pts[i].z) * (wz - pts[i].z));
                            if (dist > inner) continue;
                            var key = (td, z, x);
                            if (!target.TryGetValue(key, out var bt) || dist < bt.x) target[key] = new Vector2(dist, roadY);
                        }
                    }
                }
            }
            int carved = 0;
            var changed = new HashSet<TerrainData>();
            foreach (var kv in target)
            {
                var (td, z, x) = kv.Key;
                var h = heights[td];
                float ty = TerrainY(terrains, td);
                float cur = ty + h[z, x] * td.size.y;
                float roadY = kv.Value.y;
                if (cur <= roadY - 0.3f + 0.02f || cur > roadY + 6f) continue;
                h[z, x] = Mathf.Clamp01((roadY - 0.3f - ty) / td.size.y);
                changed.Add(td);
                carved++;
            }
            foreach (var td in changed) { td.SetHeights(0, 0, heights[td]); EditorUtility.SetDirty(td); }
            AssetDatabase.SaveAssets();
            return $"corridor carve: {carved:N0} terrain samples lowered to road - 0.3 m on {changed.Count} tile(s)" + "\n";
        }

        static float TerrainY(Terrain[] terrains, TerrainData td)
        {
            foreach (var t in terrains) if (t.terrainData == td) return t.transform.position.y;
            return 0f;
        }

        static void BackupTerrain(TerrainData td)
        {
            string src = AssetDatabase.GetAssetPath(td);
            if (string.IsNullOrEmpty(src)) return;
            Directory.CreateDirectory(Backup);
            string dst = Path.Combine(Backup, Path.GetFileName(src));
            if (!File.Exists(dst)) File.Copy(src, dst);
        }
    }
}
