using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.Core;
using VeyraCity.Testing;
using VeyraCity.World;
using VeyraCity.World.Data;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// World iteration 23 (CAM_14 "Lighthouse Golden": the headland had no lighthouse; registry LM-06 "White stone lighthouse
    /// on the headland guarding the Lantern Strait"). WorldBuildBatch step `landmarks`: original procedural landmark models on
    /// the registry points (position from location_registry.csv, height from the terrain, never moved).
    /// Lighthouse: granite plinth, 16-sided white stone tower tapering 3.2 → 2.1 m over 22 m with a red band, corbelled
    /// gallery with railing, glazed lantern room (Lamp_Head lamp, lit at night by WorldEnvironment), red domed cap with a
    /// vent ball; a keeper's cottage (white render, tile roof) on the landward side. Capsule / convex mesh colliders (no
    /// BoxCollider: RooftopDetails would dress it). Idempotent (World/Landmarks rebuilt). Report: Logs/C/landmarks.txt.
    /// </summary>
    public static class LandmarkBuilder
    {
        const string MeshDir = "Assets/_Veyra/World/Generated/Landmarks";
        const string MatDir = "Assets/_Veyra/World/Generated/Materials";

        sealed class MB { public List<Vector3> V = new List<Vector3>(); public List<Vector2> UV = new List<Vector2>(); public List<int> T = new List<int>(); public List<Vector3> N = new List<Vector3>(); }

        /// <summary>Landmarks whose generic data shell BuildingGenerator leaves undrawn (its collider stays).</summary>
        public static bool Replaces(string landmarkId) => landmarkId == "LM-06";

        static Material Mat(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");

        /// <summary>Plain URP Lit library material (created once, updated in place; Shader.Find is Editor-only here).</summary>
        static Material LitMat(string name, Color col, float smooth, float metal, bool transparent)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name }; AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", col);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            m.SetFloat("_Surface", transparent ? 1f : 0f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", transparent ? 0f : 1f);
            m.SetFloat("_SrcBlend", transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            m.SetFloat("_DstBlend", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            if (transparent) m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); else m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = transparent ? (int)RenderQueue.Transparent : -1;
            m.SetOverrideTag("RenderType", transparent ? "Transparent" : "Opaque");
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            return m;
        }

        public static string Run()
        {
            WorldData.EnsureLoaded();
            var sb = new StringBuilder("Landmarks (iteration 23)\n");
            var world = EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == "World");
            if (world == null) return "FAIL: no World root\n";
            var old = world.transform.Find("Landmarks");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("Landmarks") { isStatic = true }.transform;
            root.SetParent(world.transform, false);
            Directory.CreateDirectory(MeshDir);
            var terrains = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include);
            float Ground(Vector3 p, float fallback)
            {
                foreach (var t in terrains)
                {
                    var o = t.GetPosition(); var s = t.terrainData.size;
                    if (p.x >= o.x && p.x <= o.x + s.x && p.z >= o.z && p.z <= o.z + s.z) return t.SampleHeight(p) + o.y;
                }
                return fallback;
            }
            var reg = LocationRegistry.Parse(VeyraDataValidator.LoadText("location_registry.csv"));
            var keep = new HashSet<string>();
            foreach (var loc in reg.All.Where(l => l.Category == "Landmark" && l.Name.Contains("Lighthouse")))
            {
                var parts = new Dictionary<Material, MB>();
                MB Get(Material m) { if (!parts.TryGetValue(m, out var mb)) parts[m] = mb = new MB(); return mb; }
                // iteration 24: own plain materials (Tank_White / M_Metal_Painted_* sample texture and palette atlases, so
                // metre UVs turned the tower orange with a blue band)
                var white = LitMat("M_LM_WhiteRender", new Color(0.86f, 0.85f, 0.81f), 0.22f, 0f, false);
                var red = LitMat("M_LM_SignalRed", new Color(0.55f, 0.07f, 0.05f), 0.45f, 0.2f, false);
                var dark = LitMat("M_LM_DarkMetal", new Color(0.09f, 0.1f, 0.11f), 0.4f, 0.8f, false);
                var glass = LitMat("M_LM_Glass", new Color(0.55f, 0.62f, 0.66f, 0.28f), 0.95f, 0f, true);
                var tile = LitMat("M_LM_SlateRoof", new Color(0.16f, 0.17f, 0.19f), 0.3f, 0f, false);
                var granite = Mat("M_Kerb_Granite"); var lamp = Mat("Lamp_Head");
                if (white == null || red == null || granite == null || glass == null || lamp == null || dark == null || tile == null) { sb.AppendLine("  FAIL: library material missing"); continue; }
                var c = loc.Unity;
                // the lowest terrain under the plinth, so nothing floats on the sloping headland
                float gy = float.MaxValue;
                for (int i = 0; i < 8; i++) gy = Mathf.Min(gy, Ground(c + Dir(i * Mathf.PI / 4f) * 4f, c.y));
                gy = Mathf.Min(gy, Ground(c, c.y));
                var b = new Vector3(c.x, gy, c.z);
                const int N = 16;
                Ring(Get(granite), b, 4.4f, 4.4f, -1.5f, 1.2f, 8, true);                   // plinth
                Cap(Get(granite), b + Vector3.up * 1.2f, 4.4f, 8);
                Frustum(Get(white), b + Vector3.up * 1.2f, 3.2f, 2.45f, 15.5f, N);          // tower, lower white
                Frustum(Get(red), b + Vector3.up * 16.7f, 2.45f, 2.3f, 2.8f, N);            // red band
                Frustum(Get(white), b + Vector3.up * 19.5f, 2.3f, 2.1f, 2.5f, N);           // upper white
                Frustum(Get(white), b + Vector3.up * 22.0f, 2.1f, 2.9f, 0.6f, N);           // corbel
                Ring(Get(dark), b, 2.95f, 0f, 22.6f, 22.8f, N, false);                      // gallery deck
                for (int i = 0; i < 24; i++)                                                // railing posts
                {
                    var d = Dir(i * Mathf.PI * 2f / 24f);
                    Beam(Get(dark), b + d * 2.85f + Vector3.up * 22.8f, b + d * 2.85f + Vector3.up * 23.85f, 0.05f);
                }
                for (int i = 0; i < 24; i++)                                                // top rail
                {
                    var d0 = Dir(i * Mathf.PI * 2f / 24f); var d1 = Dir((i + 1) * Mathf.PI * 2f / 24f);
                    Beam(Get(dark), b + d0 * 2.85f + Vector3.up * 23.85f, b + d1 * 2.85f + Vector3.up * 23.85f, 0.06f);
                }
                Frustum(Get(white), b + Vector3.up * 22.8f, 1.75f, 1.75f, 0.9f, N);         // lantern base wall
                Frustum(Get(glass), b + Vector3.up * 23.7f, 1.6f, 1.6f, 2.4f, 12);          // glazing
                for (int i = 0; i < 12; i++)                                                // astragals
                {
                    var d = Dir((i + 0.5f) * Mathf.PI * 2f / 12f);
                    Beam(Get(dark), b + d * 1.63f + Vector3.up * 23.7f, b + d * 1.63f + Vector3.up * 26.1f, 0.07f);
                }
                Frustum(Get(lamp), b + Vector3.up * 24.3f, 0.55f, 0.55f, 1.0f, 10);         // the light
                Cap(Get(lamp), b + Vector3.up * 25.3f, 0.55f, 10);
                Frustum(Get(red), b + Vector3.up * 26.1f, 1.95f, 1.2f, 0.45f, N);           // dome
                Frustum(Get(red), b + Vector3.up * 26.55f, 1.2f, 0.25f, 0.9f, N);
                Frustum(Get(dark), b + Vector3.up * 27.45f, 0.12f, 0.12f, 0.5f, 6);         // vent stem
                Frustum(Get(dark), b + Vector3.up * 27.95f, 0.32f, 0.0f, 0.45f, 8);         // vent ball (cone pair)
                Frustum(Get(dark), b + Vector3.up * 27.95f, 0.32f, 0.0f, -0.3f, 8);

                // keeper's cottage on the landward side: towards the nearest road, 13 m from the tower
                var toRoad = Vector3.right;
                if (WorldData.NearestRoad(c, 400f, out var hit)) { toRoad = hit.Point - c; toRoad.y = 0f; if (toRoad.sqrMagnitude > 1f) toRoad.Normalize(); else toRoad = Vector3.right; }
                var hc = c + toRoad * 13f;
                float hy = float.MaxValue;
                foreach (var o in new[] { new Vector2(-4.5f, -3.5f), new Vector2(4.5f, -3.5f), new Vector2(4.5f, 3.5f), new Vector2(-4.5f, 3.5f), Vector2.zero })
                    hy = Mathf.Min(hy, Ground(hc + toRoad * o.y + Vector3.Cross(Vector3.up, toRoad) * o.x, c.y));
                var hb = new Vector3(hc.x, hy, hc.z);
                var ax = Vector3.Cross(Vector3.up, toRoad).normalized; var az = toRoad;
                Box(Get(granite), hb + Vector3.up * -0.4f, ax, az, 9.4f, 1.6f, 7.4f);       // footing (0.4 m showing)
                Box(Get(white), hb + Vector3.up * 2.0f, ax, az, 9f, 3.2f, 7f);              // walls
                Gable(Get(tile), Get(white), hb + Vector3.up * 3.6f, ax, az, 9.6f, 7.6f, 2.6f);
                Box(Get(dark), hb + Vector3.up * 1.25f + az * 3.52f, ax, az, 1.1f, 2.1f, 0.06f);   // door
                foreach (float x in new[] { -2.8f, 2.8f })
                    Box(Get(glass), hb + Vector3.up * 2.2f + az * 3.52f + ax * x, ax, az, 1.2f, 1.3f, 0.06f);
                Box(Get(white), hb + Vector3.up * 6.1f + ax * 2.6f, ax, az, 0.7f, 2.0f, 0.7f);   // chimney

                var go = new GameObject($"{loc.Id}_{loc.Name.Replace(' ', '_')}") { isStatic = true };
                go.transform.SetParent(root, false);
                foreach (var kv in parts)
                {
                    if (kv.Value.T.Count == 0) continue;
                    string name = $"LMK_{loc.Id}_{kv.Key.name}";
                    var mesh = Save(name, kv.Value);
                    keep.Add(name);
                    var r = new GameObject(kv.Key.name) { isStatic = true };
                    r.transform.SetParent(go.transform, false);
                    r.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = r.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = kv.Key;
                    mr.shadowCastingMode = kv.Key == lamp || kv.Key == glass ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    var lg = r.AddComponent<LODGroup>();
                    lg.SetLODs(new[] { new LOD(kv.Key == white || kv.Key == red ? 0.002f : 0.006f, new Renderer[] { mr }) });
                    lg.RecalculateBounds();
                }
                // the tower collides through the data building's capsule (BLD with this landmark_id, r = footprint / 2)
                var house = new GameObject("UCX_Cottage") { isStatic = true };
                house.transform.SetParent(go.transform, false);
                var hm = new MB(); Box(hm, hb + Vector3.up * 2.4f, ax, az, 9f, 6f, 7f);
                var hmesh = Save($"LMK_{loc.Id}_COL", hm); keep.Add($"LMK_{loc.Id}_COL");
                var mc = house.AddComponent<MeshCollider>(); mc.sharedMesh = hmesh; mc.convex = true;
                sb.AppendLine($"  {loc.Id} {loc.Name} at ({c.x:0}, {gy:0.0}, {c.z:0}) registry y {c.y:0.0}: tower 28 m, cottage towards the road at ({hc.x:0}, {hy:0.0}, {hc.z:0})");
            }
            // ---------------------------------------------------------------- iteration 26: shared helpers
            var buildings = PlacementReader.Buildings(VeyraDataValidator.LoadText("world_building_placements.csv"), "world_building_placements.csv");
            bool Free(Vector3 p, float clearance)
            {
                foreach (var bd in buildings)
                {
                    float dx = p.x - bd.UnityX, dz = p.z - bd.UnityZ;
                    if (dx * dx + dz * dz > 150f * 150f) continue;
                    var local = Quaternion.Euler(0f, -bd.UnityYawDeg, 0f) * new Vector3(dx, 0f, dz);
                    if (Mathf.Abs(local.x) < bd.FootprintWM * 0.5f + clearance && Mathf.Abs(local.z) < bd.FootprintLM * 0.5f + clearance) return false;
                }
                if (WorldData.NearestRoad(p, 40f, out var rh) && rh.Distance < WorldData.Roads[rh.Road].HalfWidth + clearance + 2f) return false;
                return true;
            }
            // direction to the nearest sea (ground below 0.3 m) among 32 bearings, and the distance to it
            (Vector3 dir, float dist) SeaDir(Vector3 c, float max)
            {
                var best = (Vector3.zero, float.MaxValue);
                for (int i = 0; i < 32; i++)
                {
                    var d = Dir(i * Mathf.PI * 2f / 32f);
                    for (float r = 0f; r < max; r += 2f)
                        if (Ground(c + d * r, 99f) < 0.3f) { if (r < best.Item2) best = (d, r); break; }
                }
                return best;
            }
            var mWood = Mat("M_Wood_Weathered") ?? Mat("Wood");
            var mWhite = LitMat("M_LM_WhiteRender", new Color(0.86f, 0.85f, 0.81f), 0.22f, 0f, false);
            var mDark = LitMat("M_LM_DarkMetal", new Color(0.09f, 0.1f, 0.11f), 0.4f, 0.8f, false);
            var mRed = LitMat("M_LM_SignalRed", new Color(0.55f, 0.07f, 0.05f), 0.45f, 0.2f, false);
            var mGlass = LitMat("M_LM_Glass", new Color(0.55f, 0.62f, 0.66f, 0.28f), 0.95f, 0f, true);
            var mAlu = LitMat("M_LM_Aluminium", new Color(0.72f, 0.74f, 0.76f), 0.6f, 0.9f, false);
            var mTeal = LitMat("M_LM_AwningTeal", new Color(0.05f, 0.42f, 0.44f), 0.3f, 0f, false);
            var mYellow = LitMat("M_LM_AwningYellow", new Color(0.85f, 0.62f, 0.1f), 0.3f, 0f, false);
            var mSteel = LitMat("M_LM_RefinerySteel", new Color(0.55f, 0.56f, 0.55f), 0.45f, 0.85f, false);
            var mFlame = FlameMat();
            var mFoam = Mat("M_Shore_Foam"); var mWater = Mat("M_Water");
            void Emit(string id, string label, Dictionary<Material, MB> pp, List<(Vector3 c, Vector3 ax, Vector3 size)> cols, float lodSize = 0.004f)
            {
                var go = new GameObject($"{id}_{label}") { isStatic = true };
                go.transform.SetParent(root, false);
                foreach (var kv in pp)
                {
                    if (kv.Value.T.Count == 0 || kv.Key == null) continue;
                    string name = $"LMK_{id}_{kv.Key.name}";
                    var mesh = Save(name, kv.Value);
                    keep.Add(name);
                    var r = new GameObject(kv.Key.name) { isStatic = true };
                    r.transform.SetParent(go.transform, false);
                    r.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = r.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = kv.Key;
                    bool noShadow = kv.Key == mGlass || kv.Key == mFlame || kv.Key == mFoam || kv.Key == mWater;
                    mr.shadowCastingMode = noShadow ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    var lg = r.AddComponent<LODGroup>();
                    lg.SetLODs(new[] { new LOD(lodSize, new Renderer[] { mr }) });
                    lg.RecalculateBounds();
                }
                for (int k = 0; k < cols.Count; k++)
                {
                    var cm = new MB(); Box(cm, cols[k].c, cols[k].ax, Vector3.Cross(cols[k].ax, Vector3.up) * -1f, cols[k].size.x, cols[k].size.y, cols[k].size.z);
                    string cn = $"LMK_{id}_COL{k}";
                    var cmesh = Save(cn, cm); keep.Add(cn);
                    var g = new GameObject($"UCX_{id}_{k}") { isStatic = true };
                    g.transform.SetParent(go.transform, false);
                    var mc = g.AddComponent<MeshCollider>(); mc.sharedMesh = cmesh; mc.convex = true;
                }
            }

            // ---------------------------------------------------------------- LM-08 Azure Pier
            foreach (var loc in reg.All.Where(l => l.Category == "Landmark" && l.Name.Contains("Pier")))
            {
                var pp = new Dictionary<Material, MB>(); MB G(Material m) { if (!pp.TryGetValue(m, out var mb)) pp[m] = mb = new MB(); return mb; }
                var cols = new List<(Vector3, Vector3, Vector3)>();
                var c = loc.Unity;
                var (dir, shore) = SeaDir(c, 400f);
                if (dir == Vector3.zero) { sb.AppendLine($"  {loc.Id}: no sea within 400 m, skipped"); continue; }
                var side = Vector3.Cross(Vector3.up, dir).normalized;
                float deckY = Mathf.Max(c.y, 4.4f);
                var start = c + dir * Mathf.Max(0f, shore - 12f);
                const float L = 220f, Wd = 9f;
                // deck, joists, piles every 6 m in pairs, railings
                for (float s = 0f; s < L; s += 6f)
                {
                    var p = start + dir * s;
                    Box(G(mWood), new Vector3(p.x, deckY - 0.2f, p.z) + dir * 3f, dir, side, 6.02f, 0.4f, Wd);
                    foreach (float o in new[] { -Wd * 0.5f + 0.4f, Wd * 0.5f - 0.4f })
                    {
                        var q = p + side * o;
                        float gy = Mathf.Min(Ground(q, 0f), -1f);
                        Frustum(G(mWood), new Vector3(q.x, gy - 1f, q.z), 0.32f, 0.3f, deckY - 0.4f - gy + 1f, 8);
                        Box(G(mWood), new Vector3(q.x, deckY + 0.55f, q.z) + dir * 3f, dir, side, 6f, 0.1f, 0.12f);   // handrail
                        Box(G(mWood), new Vector3(q.x, deckY + 0.5f, q.z), dir, side, 0.12f, 1.0f, 0.12f);           // post
                    }
                    Beam(G(mWood), new Vector3(p.x, deckY - 0.5f, p.z) - side * (Wd * 0.5f - 0.4f), new Vector3(p.x, deckY - 0.5f, p.z) + side * (Wd * 0.5f - 0.4f), 0.25f);
                }
                cols.Add((new Vector3(start.x, deckY - 0.2f, start.z) + dir * L * 0.5f, dir, new Vector3(L, 0.4f, Wd)));
                // pier head platform and Ferris wheel (30 m) at 75 %
                var wc = start + dir * (L * 0.75f);
                Box(G(mWood), new Vector3(wc.x, deckY - 0.2f, wc.z), dir, side, 26f, 0.4f, 22f);
                cols.Add((new Vector3(wc.x, deckY - 0.2f, wc.z), dir, new Vector3(26f, 0.4f, 22f)));
                float R = 15f; var hub = new Vector3(wc.x, deckY + R + 2.5f, wc.z) + side * 4f;
                foreach (float o in new[] { -1.4f, 1.4f })   // A-frame legs (the wheel turns in the dir/up plane)
                {
                    var hb = hub + side * o;
                    Beam(G(mWhite), new Vector3(wc.x, deckY, wc.z) + side * 4f + side * o * 1.6f - dir * 7f, hb, 0.5f);
                    Beam(G(mWhite), new Vector3(wc.x, deckY, wc.z) + side * 4f + side * o * 1.6f + dir * 7f, hb, 0.5f);
                    for (int k = 0; k < 32; k++)
                    {
                        float a0 = k * Mathf.PI * 2f / 32f, a1 = (k + 1) * Mathf.PI * 2f / 32f;
                        Beam(G(mWhite), hb + dir * Mathf.Cos(a0) * R + Vector3.up * Mathf.Sin(a0) * R, hb + dir * Mathf.Cos(a1) * R + Vector3.up * Mathf.Sin(a1) * R, 0.35f);
                        if ((k & 1) == 0) Beam(G(mWhite), hb, hb + dir * Mathf.Cos(a0) * R + Vector3.up * Mathf.Sin(a0) * R, 0.12f);
                    }
                }
                Beam(G(mDark), hub - side * 1.8f, hub + side * 1.8f, 0.7f);   // axle
                for (int k = 0; k < 16; k++)   // gondolas hang under the rim points
                {
                    float a = k * Mathf.PI * 2f / 16f;
                    var rp = hub + dir * Mathf.Cos(a) * R + Vector3.up * Mathf.Sin(a) * R;
                    Beam(G(mWhite), rp - side * 1.4f, rp + side * 1.4f, 0.12f);
                    Box(G((k & 1) == 0 ? mTeal : mYellow), rp - Vector3.up * 1.3f, dir, side, 1.8f, 1.6f, 2.2f);
                    Box(G(mWhite), rp - Vector3.up * 0.35f, dir, side, 2.0f, 0.12f, 2.4f);
                }
                // food stalls along the deck (alternate sides, awnings in two colours)
                int stalls = 0;
                for (float s = 30f; s < L * 0.62f; s += 22f, stalls++)
                {
                    var p = start + dir * s + side * ((stalls & 1) == 0 ? -2.6f : 2.6f);
                    var b0 = new Vector3(p.x, deckY, p.z);
                    Box(G(mWhite), b0 + Vector3.up * 1.3f, dir, side, 3.2f, 2.6f, 2.6f);
                    Box(G(mGlass), b0 + Vector3.up * 1.6f + side * ((stalls & 1) == 0 ? 1.31f : -1.31f), dir, side, 2.6f, 1.0f, 0.04f);
                    Box(G((stalls & 1) == 0 ? mTeal : mYellow), b0 + Vector3.up * 2.75f + side * ((stalls & 1) == 0 ? 1.6f : -1.6f), dir, side, 3.6f, 0.12f, 1.4f);
                }
                Emit(loc.Id, loc.Name.Replace(' ', '_'), pp, cols);
                sb.AppendLine($"  {loc.Id} {loc.Name}: from ({start.x:0}, {deckY:0.0}, {start.z:0}) {L:0} m towards the sea (bearing {Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg:0}°), Ferris wheel 30 m, {stalls} stalls");
            }

            // ---------------------------------------------------------------- LM-10 Azure Marina
            foreach (var loc in reg.All.Where(l => l.Category == "Landmark" && l.Name.Contains("Marina")))
            {
                var pp = new Dictionary<Material, MB>(); MB G(Material m) { if (!pp.TryGetValue(m, out var mb)) pp[m] = mb = new MB(); return mb; }
                var cols = new List<(Vector3, Vector3, Vector3)>();
                var c = loc.Unity;
                var (dir, shore) = SeaDir(c, 400f);
                if (dir == Vector3.zero) { sb.AppendLine($"  {loc.Id}: no sea within 400 m, skipped"); continue; }
                var side = Vector3.Cross(Vector3.up, dir).normalized;
                var start = c + dir * shore;
                const float L = 150f, fy = 0.55f;
                var rng = new System.Random(10);
                Box(G(mWood), new Vector3(start.x, fy, start.z) + dir * L * 0.5f, dir, side, L, 0.35f, 3f);
                cols.Add((new Vector3(start.x, fy, start.z) + dir * L * 0.5f, dir, new Vector3(L, 0.35f, 3f)));
                Box(G(mWood), new Vector3(start.x, fy + 0.6f, start.z) - dir * 4f, dir, side, 9f, 0.25f, 2.4f);   // gangway up to the quay
                int yachts = 0, berths = 0;
                for (float s = 18f; s < L - 6f; s += 13f)
                    foreach (float sd in new[] { -1f, 1f })
                    {
                        var fp = start + dir * s + side * sd * 7f;
                        Box(G(mWood), new Vector3(fp.x, fy, fp.z), side, dir, 11f, 0.3f, 1.4f);
                        var pile = start + dir * s + side * sd * 12.8f;
                        Frustum(G(mDark), new Vector3(pile.x, -3f, pile.z), 0.18f, 0.18f, 5.6f, 6);
                        berths++;
                        if (rng.NextDouble() < 0.3) continue;
                        // yacht bow out, beside the finger (hull with raked bow, cabin, mast)
                        float yl = 10f + (float)rng.NextDouble() * 5f, yb = yl * 0.3f;
                        var yc = start + dir * (s + 6.3f) + side * sd * (6.5f + yl * 0.5f - 1f);
                        var fwd = side * sd;
                        Yacht(G(mWhite), G(mWood), G(mGlass), G(mAlu), G(mDark), new Vector3(yc.x, 0f, yc.z), fwd, yl, yb, rng.NextDouble() < 0.6);
                        yachts++;
                    }
                Emit(loc.Id, loc.Name.Replace(' ', '_'), pp, cols);
                sb.AppendLine($"  {loc.Id} {loc.Name}: main pontoon {L:0} m from ({start.x:0}, {start.z:0}) bearing {Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg:0}°, {berths} berths, {yachts} yachts");
            }

            // ---------------------------------------------------------------- LM-12 Ironport Refinery
            foreach (var loc in reg.All.Where(l => l.Category == "Landmark" && l.Name.Contains("Refinery")))
            {
                var pp = new Dictionary<Material, MB>(); MB G(Material m) { if (!pp.TryGetValue(m, out var mb)) pp[m] = mb = new MB(); return mb; }
                var cols = new List<(Vector3, Vector3, Vector3)>();
                var c = loc.Unity;
                var spots = new List<Vector3>();
                for (float r = 0f; r < 160f && spots.Count < 7; r += 12f)
                    for (int i = 0; i < 16 && spots.Count < 7; i++)
                    {
                        var p = c + Dir(i * Mathf.PI / 8f + r * 0.05f) * r;
                        if (!Free(p, 7f) || spots.Any(q => (q - p).sqrMagnitude < 26f * 26f)) continue;
                        float gy = Ground(p, c.y);
                        if (gy < 0.5f) continue;
                        spots.Add(new Vector3(p.x, gy, p.z));
                    }
                int cols2 = 0, flares = 0;
                for (int k = 0; k < spots.Count; k++)
                {
                    var b = spots[k];
                    if (k < 3)
                    {
                        // distillation column: 3.2 m shell, 42-52 m, ring platforms every 9 m, ladder cage
                        float h = 42f + k * 5f;
                        Frustum(G(mSteel), b + Vector3.down, 3.2f, 3.2f, h + 1f, 16);
                        Cap(G(mSteel), b + Vector3.up * h, 3.2f, 16);
                        for (float y = 9f; y < h; y += 9f) Ring(G(mDark), b, 4.4f, 3.2f, y - 0.15f, y, 16, false);
                        Box(G(mDark), b + Vector3.right * 3.6f + Vector3.up * h * 0.5f, Vector3.right, Vector3.forward, 0.8f, h, 0.8f);
                        cols.Add((b + Vector3.up * h * 0.5f, Vector3.right, new Vector3(6.4f, h, 6.4f)));
                        cols2++;
                    }
                    else if (k < 6)
                    {
                        // flare stack: 1.4 m lattice-braced stack to 64 m, flame on top (burns day and night)
                        float h = 58f + (k - 3) * 6f;
                        Frustum(G(mSteel), b + Vector3.down, 1.4f, 0.9f, h + 1f, 10);
                        for (float y = 0f; y < h - 6f; y += 6f)
                            foreach (var d in new[] { Vector3.right, Vector3.forward, Vector3.left, Vector3.back })
                                Beam(G(mDark), b + d * 4f + Vector3.up * y, b + d * 1.2f + Vector3.up * (y + 6f), 0.18f);
                        Box(G(mRed), b + Vector3.up * (h - 2f), Vector3.right, Vector3.forward, 2.2f, 1.2f, 2.2f);
                        Frustum(G(mFlame), b + Vector3.up * h, 1.1f, 0.05f, 7f, 8);
                        Frustum(G(mFlame), b + Vector3.up * h + Vector3.right * 0.6f, 0.7f, 0.05f, 4.5f, 8);
                        cols.Add((b + Vector3.up * 3f, Vector3.right, new Vector3(3f, 6f, 3f)));
                        flares++;
                    }
                }
                // pipe racks between consecutive columns / stacks: portal frames every 8 m, four pipes at 6 m
                for (int k = 0; k + 1 < spots.Count && k < 5; k++)
                {
                    var a = spots[k]; var b = spots[k + 1];
                    var d = b - a; d.y = 0f; float len = d.magnitude; if (len < 1f) continue; d /= len;
                    var sd = Vector3.Cross(Vector3.up, d);
                    for (float s = 6f; s < len - 6f; s += 8f)
                    {
                        var p = a + d * s; p.y = Ground(p, a.y);
                        foreach (float o in new[] { -1.6f, 1.6f }) Box(G(mSteel), p + sd * o + Vector3.up * 3.2f, d, sd, 0.3f, 6.4f, 0.3f);
                        Box(G(mSteel), p + Vector3.up * 6.3f, d, sd, 0.3f, 0.3f, 3.6f);
                    }
                    for (int q = 0; q < 4; q++)
                    {
                        float o = -1.2f + q * 0.8f, y = (a.y + b.y) * 0.5f + 6.8f;
                        Beam(G(q == 1 ? mYellow : mSteel), new Vector3(a.x, y, a.z) + d * 5f + sd * o, new Vector3(b.x, y, b.z) - d * 5f + sd * o, 0.45f);
                    }
                }
                Emit(loc.Id, loc.Name.Replace(' ', '_'), pp, cols, 0.002f);
                sb.AppendLine($"  {loc.Id} {loc.Name}: {cols2} distillation columns, {flares} flare stacks, pipe racks between them ({spots.Count} free spots within 160 m)");
            }

            // ---------------------------------------------------------------- LM-25 Sable Falls
            foreach (var loc in reg.All.Where(l => l.Category == "Landmark" && l.Name.Contains("Falls")))
            {
                var pp = new Dictionary<Material, MB>(); MB G(Material m) { if (!pp.TryGetValue(m, out var mb)) pp[m] = mb = new MB(); return mb; }
                var c = loc.Unity;
                // steepest descent through the point (terrain 20 m either side)
                Vector3 down = Vector3.zero; float drop = 0f;
                for (int i = 0; i < 32; i++)
                {
                    var d = Dir(i * Mathf.PI * 2f / 32f);
                    float dd = Ground(c - d * 20f, c.y) - Ground(c + d * 20f, c.y);
                    if (dd > drop) { drop = dd; down = d; }
                }
                if (drop < 4f || mFoam == null) { sb.AppendLine($"  {loc.Id} {loc.Name}: terrain drop {drop:0.0} m over 40 m, no falls built"); continue; }
                var sideF = Vector3.Cross(Vector3.up, down).normalized;
                // lip = last point along the descent still within 1 m of the upper level; base = where the slope flattens
                float top = Ground(c - down * 20f, c.y), lipS = -20f;
                for (float s = -20f; s < 30f; s += 0.5f) if (Ground(c + down * s, c.y) > top - 1f) lipS = s;
                var lip = c + down * lipS; lip.y = Ground(lip, c.y);
                float baseS = lipS; for (float s = lipS; s < lipS + 40f; s += 0.5f) { baseS = s; if (Ground(c + down * (s + 1f), c.y) > Ground(c + down * s, c.y) - 0.15f) break; }
                var bottom = c + down * baseS; bottom.y = Ground(bottom, c.y);
                float fallH = lip.y - bottom.y, Wf = 14f;
                // the falling sheet: 8 rows down a slight parabola, each row a quad across the width, offset 0.4 m off the rock
                for (int k = 0; k < 8; k++)
                {
                    float t0 = k / 8f, t1 = (k + 1) / 8f;
                    Vector3 P(float t, float o) => lip + down * (0.4f + Mathf.Lerp(0f, baseS - lipS, t * t) * 0.6f + t * 1.2f) + Vector3.up * (-fallH * t + 0.3f) + sideF * o;
                    for (int j = 0; j < 4; j++)
                    {
                        float o0 = -Wf * 0.5f + j * Wf / 4f, o1 = o0 + Wf / 4f;
                        Quad(G(mFoam), P(t0, o0), P(t0, o1), P(t1, o1), P(t1, o0), down);
                    }
                }
                // plunge pool foam and water, spray mist sheet
                Cap(G(mWater), bottom + Vector3.up * 0.25f + down * 6f, 11f, 16);
                Cap(G(mFoam), bottom + Vector3.up * 0.32f + down * 3f, 7f, 16);
                Emit(loc.Id, loc.Name.Replace(' ', '_'), pp, new List<(Vector3, Vector3, Vector3)>());
                sb.AppendLine($"  {loc.Id} {loc.Name}: lip ({lip.x:0}, {lip.y:0.0}, {lip.z:0}) falls {fallH:0.0} m (registry: 12 m) towards bearing {Mathf.Atan2(down.x, down.z) * Mathf.Rad2Deg:0}°, width {Wf:0} m");
            }

            foreach (var f in Directory.GetFiles(MeshDir, "*.asset"))
                if (!keep.Contains(Path.GetFileNameWithoutExtension(f))) AssetDatabase.DeleteAsset(f.Replace('\\', '/'));
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs/C");
            File.WriteAllText("Logs/C/landmarks.txt", sb.ToString());
            return sb.ToString();
        }

        static Vector3 Dir(float a) => new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));

        /// <summary>Flare flame: unlit-looking hot orange (URP Lit with HDR emission, burns day and night; static keyword).</summary>
        static Material FlameMat()
        {
            var m = LitMat("M_LM_FlareFlame", new Color(1f, 0.55f, 0.12f), 0f, 0f, false);
            m.SetColor("_EmissionColor", new Color(1f, 0.42f, 0.08f) * 6f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
            return m;
        }

        /// <summary>A moored sailing/motor yacht on the water line at c, bow towards fwd: raked hull, teak deck, cabin, mast.</summary>
        static void Yacht(MB hull, MB deck, MB glass, MB alu, MB dark, Vector3 c, Vector3 fwd, float len, float beam, bool sail)
        {
            fwd.y = 0f; fwd.Normalize();
            var sd = Vector3.Cross(Vector3.up, fwd);
            float hl = len * 0.5f, hb = beam * 0.5f, fb = 1.3f;
            Vector3 P(float a, float y, float o) => c + fwd * a + Vector3.up * y + sd * o;
            // hull: stern transom, parallel body, bow taper to a stem; bottom at -0.6, sheer at fb (higher at the bow)
            var sec = new (float a, float w, float sh)[] { (-hl, hb * 0.85f, fb), (-hl * 0.3f, hb, fb), (hl * 0.35f, hb * 0.92f, fb + 0.15f), (hl * 0.8f, hb * 0.5f, fb + 0.3f), (hl, 0.05f, fb + 0.4f) };
            for (int i = 0; i + 1 < sec.Length; i++)
                foreach (float s in new[] { -1f, 1f })
                {
                    var (a0, w0, h0) = sec[i]; var (a1, w1, h1) = sec[i + 1];
                    Quad(hull, P(a0, -0.6f, s * w0 * 0.7f), P(a1, -0.6f, s * w1 * 0.7f), P(a1, h1, s * w1), P(a0, h0, s * w0), sd * s);
                    Quad(deck, P(a0, h0, 0f), P(a1, h1, 0f), P(a1, h1, s * w1), P(a0, h0, s * w0), Vector3.up);
                }
            Quad(hull, P(-hl, -0.6f, -hb * 0.6f), P(-hl, -0.6f, hb * 0.6f), P(-hl, fb, hb * 0.85f), P(-hl, fb, -hb * 0.85f), -fwd);
            // cabin with windows
            Box(hull, P(-hl * 0.1f, fb + 0.55f, 0f), fwd, sd, len * 0.38f, 1.1f, beam * 0.62f);
            Box(glass, P(-hl * 0.1f, fb + 0.65f, 0f), fwd, sd, len * 0.3f, 0.35f, beam * 0.63f);
            if (sail)
            {
                Frustum(alu, P(hl * 0.12f, fb, 0f), 0.09f, 0.06f, len * 1.25f, 6);   // mast
                Beam(alu, P(hl * 0.12f, fb + 1.6f, 0f), P(-hl * 0.75f, fb + 1.6f, 0f), 0.1f);   // boom
                Box(dark, P(-hl * 0.3f, fb + 1.75f, 0f), fwd, sd, len * 0.82f, 0.3f, 0.32f);   // furled sail cover
            }
            else
                Box(hull, P(-hl * 0.15f, fb + 1.45f, 0f), fwd, sd, len * 0.26f, 0.7f, beam * 0.5f);   // flybridge
        }

        static void Quad(MB m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
        {
            int i = m.V.Count;
            m.V.Add(a); m.V.Add(b); m.V.Add(c); m.V.Add(d);
            float w = (b - a).magnitude, h = (d - a).magnitude;
            m.UV.Add(new Vector2(0, 0)); m.UV.Add(new Vector2(w, 0)); m.UV.Add(new Vector2(w, h)); m.UV.Add(new Vector2(0, h));
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-10f) n = Vector3.Cross(c - a, d - a);
            if (Vector3.Dot(n, facing) >= 0f) m.T.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            else { m.T.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 }); n = -n; }
            n.Normalize();
            for (int k = 0; k < 4; k++) m.N.Add(n);
        }

        /// <summary>Open frustum wall from radius r0 at c to r1 at c + h (h may be negative).</summary>
        static void Frustum(MB m, Vector3 c, float r0, float r1, float h, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                var mid = Dir((a0 + a1) * 0.5f);
                var facing = mid + Vector3.up * ((r0 - r1) / Mathf.Max(0.01f, Mathf.Abs(h))) * Mathf.Sign(h);
                Quad(m, c + Dir(a0) * r0, c + Dir(a1) * r0, c + Dir(a1) * r1 + Vector3.up * h, c + Dir(a0) * r1 + Vector3.up * h, facing);
                // smooth round wall: radial normals tilted by the taper
                float tilt = (r0 - r1) / Mathf.Max(0.01f, Mathf.Abs(h)) * Mathf.Sign(h);
                int k = m.N.Count - 4;
                m.N[k] = (Dir(a0) + Vector3.up * tilt).normalized; m.N[k + 1] = (Dir(a1) + Vector3.up * tilt).normalized;
                m.N[k + 2] = m.N[k + 1]; m.N[k + 3] = m.N[k];
            }
        }

        static void Cap(MB m, Vector3 c, float r, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                int k = m.V.Count;
                m.V.Add(c); m.V.Add(c + Dir(a0) * r); m.V.Add(c + Dir(a1) * r);
                m.UV.Add(Vector2.zero); m.UV.Add(new Vector2(r, 0)); m.UV.Add(new Vector2(0, r));
                if (Vector3.Cross(m.V[k + 1] - c, m.V[k + 2] - c).y >= 0f) m.T.AddRange(new[] { k, k + 1, k + 2 }); else m.T.AddRange(new[] { k, k + 2, k + 1 });
                m.N.Add(Vector3.up); m.N.Add(Vector3.up); m.N.Add(Vector3.up);
            }
        }

        /// <summary>Flat annulus (deck) or straight wall ring from r0 to r1 between y0 and y1 (wall when r0 == r1).</summary>
        static void Ring(MB m, Vector3 c, float r0, float r1, float y0, float y1, int n, bool wall)
        {
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                var mid = Dir((a0 + a1) * 0.5f);
                if (wall)
                    Quad(m, c + Dir(a0) * r0 + Vector3.up * y0, c + Dir(a1) * r0 + Vector3.up * y0, c + Dir(a1) * r0 + Vector3.up * y1, c + Dir(a0) * r0 + Vector3.up * y1, mid);
                else
                {
                    Quad(m, c + Dir(a0) * r0 + Vector3.up * y1, c + Dir(a1) * r0 + Vector3.up * y1, c + Dir(a1) * r1 + Vector3.up * y1, c + Dir(a0) * r1 + Vector3.up * y1, Vector3.up);
                    Quad(m, c + Dir(a0) * r0 + Vector3.up * y0, c + Dir(a1) * r0 + Vector3.up * y0, c + Dir(a1) * r1 + Vector3.up * y0, c + Dir(a0) * r1 + Vector3.up * y0, Vector3.down);
                    Quad(m, c + Dir(a0) * r0 + Vector3.up * y0, c + Dir(a1) * r0 + Vector3.up * y0, c + Dir(a1) * r0 + Vector3.up * y1, c + Dir(a0) * r0 + Vector3.up * y1, mid);
                }
            }
        }

        static void Beam(MB m, Vector3 a, Vector3 b, float w)
        {
            var dir = (b - a).normalized;
            var refUp = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.9f ? Vector3.forward : Vector3.up;
            var side = Vector3.Cross(refUp, dir).normalized * w * 0.5f;
            var up = Vector3.Cross(dir, side).normalized * w * 0.5f;
            Vector3[] r0 = { a - side - up, a + side - up, a + side + up, a - side + up };
            Vector3[] r1 = { b - side - up, b + side - up, b + side + up, b - side + up };
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Quad(m, r0[i], r0[j], r1[j], r1[i], (r0[i] + r0[j]) * 0.5f - a);
            }
        }

        static void Box(MB m, Vector3 c, Vector3 ax, Vector3 az, float sx, float sy, float sz)
        {
            ax = ax.normalized * sx * 0.5f; az = az.normalized * sz * 0.5f; var ay = Vector3.up * sy * 0.5f;
            Vector3 P(int x, int y, int z) => c + ax * x + ay * y + az * z;
            Quad(m, P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), -az);
            Quad(m, P(1, -1, 1), P(-1, -1, 1), P(-1, 1, 1), P(1, 1, 1), az);
            Quad(m, P(-1, -1, 1), P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), -ax);
            Quad(m, P(1, -1, -1), P(1, -1, 1), P(1, 1, 1), P(1, 1, -1), ax);
            Quad(m, P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), Vector3.up);
            Quad(m, P(-1, -1, 1), P(1, -1, 1), P(1, -1, -1), P(-1, -1, -1), Vector3.down);
        }

        /// <summary>Pitched roof (ridge along ax) over a w × d plan at eave height base.y, ridge height h; gable ends in wall.</summary>
        static void Gable(MB roof, MB wall, Vector3 c, Vector3 ax, Vector3 az, float w, float d, float h)
        {
            ax = ax.normalized; az = az.normalized;
            Vector3 e0 = c - ax * w / 2 - az * d / 2, e1 = c + ax * w / 2 - az * d / 2, e2 = c + ax * w / 2 + az * d / 2, e3 = c - ax * w / 2 + az * d / 2;
            Vector3 r0 = c - ax * w / 2 + Vector3.up * h, r1 = c + ax * w / 2 + Vector3.up * h;
            Quad(roof, e0, e1, r1, r0, -az + Vector3.up);
            Quad(roof, e2, e3, r0, r1, az + Vector3.up);
            Quad(roof, e1, e0, r0, r1, -az - Vector3.up);   // soffit
            Quad(roof, e3, e2, r1, r0, az - Vector3.up);
            float wi = w / 2 - 0.3f, di = d / 2 - 0.3f;
            foreach (float s in new[] { -1f, 1f })
            {
                int k = wall.V.Count;
                wall.V.Add(c + ax * s * wi - az * di); wall.V.Add(c + ax * s * wi + az * di); wall.V.Add(c + ax * s * wi + Vector3.up * (h * di / (d / 2)));
                wall.UV.Add(Vector2.zero); wall.UV.Add(new Vector2(d, 0)); wall.UV.Add(new Vector2(d / 2, h));
                var n = Vector3.Cross(wall.V[k + 1] - wall.V[k], wall.V[k + 2] - wall.V[k]);
                if (Vector3.Dot(n, ax * s) >= 0f) wall.T.AddRange(new[] { k, k + 1, k + 2 }); else wall.T.AddRange(new[] { k, k + 2, k + 1 });
                wall.N.Add(ax * s); wall.N.Add(ax * s); wall.N.Add(ax * s);
            }
        }

        static Mesh Save(string name, MB mb)
        {
            string path = $"{MeshDir}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh { name = name };
            mesh.Clear();
            mesh.SetVertices(mb.V); mesh.SetUVs(0, mb.UV); mesh.SetTriangles(mb.T, 0);
            if (mb.N.Count == mb.V.Count) mesh.SetNormals(mb.N); else mesh.RecalculateNormals();
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            if (created) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
