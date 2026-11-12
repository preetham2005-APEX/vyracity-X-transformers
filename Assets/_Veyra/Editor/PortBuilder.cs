using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    /// World iteration 26 (CAM_15: the container quay was an empty paved field; registry LM-11 "Six-berth container terminal
    /// with eight ship-to-shore gantry cranes", but no ship at any berth). WorldBuildBatch step `port`. Everything is derived
    /// from crane_placements.csv (the crane line is the authority): the quay edge is found under each crane by sampling the
    /// rendered ground seaward (south) of the waterside leg until it drops to the sea; along it go
    /// <list type="bullet">
    /// <item>two crane rails (60 mm steel on a concrete runway beam) under the landside and waterside legs, the full crane line;</item>
    /// <item>a white safety line 1.5 m in from the edge and mooring bollards every 24 m on the edge;</item>
    /// <item>three moored container ships (original procedural hull: raked bow, transom stern, red antifouling below a
    /// black boot-top, deck container bays in the data's container colours, white accommodation block with bridge wings and
    /// funnel aft), each under a pair of working cranes with a 50 m gap between ships.</item>
    /// </list>
    /// Ships stand in the water on the sea level (y 0); convex mesh collider per hull (UCX, no box: RooftopDetails dresses boxes).
    /// Idempotent (World/Port rebuilt). Report Logs/C/port.txt.
    /// </summary>
    public static class PortBuilder
    {
        const string MeshDir = "Assets/_Veyra/World/Generated/Port";
        const string MatDir = "Assets/_Veyra/World/Generated/Materials";
        const float Sea = 0f;

        sealed class MB { public List<Vector3> V = new List<Vector3>(); public List<Vector2> UV = new List<Vector2>(); public List<int> T = new List<int>(); }

        static Material Mat(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{n}.mat");

        static Material LitMat(string name, Color col, float smooth, float metal)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name }; AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", col); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m); AssetDatabase.SaveAssetIfDirty(m);
            return m;
        }

        public static string Run()
        {
            var sb = new StringBuilder("Port (iteration 26)\n");
            var world = EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == "World");
            if (world == null) return "FAIL: no World root\n";
            var old = world.transform.Find("Port");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            Directory.CreateDirectory(MeshDir);

            // cranes from the data
            var cranes = new List<Vector3>();
            var lines = File.ReadAllLines("Assets/_Veyra/Data/crane_placements.csv");
            var head = lines[0].Split(',');
            int ix = System.Array.IndexOf(head, "unity_x"), iy = System.Array.IndexOf(head, "unity_y_base"), iz = System.Array.IndexOf(head, "unity_z");
            foreach (var l in lines.Skip(1))
            {
                var f = l.Split(',');
                if (f.Length <= iz) continue;
                cranes.Add(new Vector3(float.Parse(f[ix], CultureInfo.InvariantCulture), float.Parse(f[iy], CultureInfo.InvariantCulture), float.Parse(f[iz], CultureInfo.InvariantCulture)));
            }
            cranes = cranes.OrderBy(c => c.x).ToList();
            if (cranes.Count < 2) return "FAIL: fewer than two cranes in crane_placements.csv\n";

            // rendered ground (terrain-like surfaces, roads, quays): highest triangle under a point, 10 m grid
            var grid = new Dictionary<long, List<Vector3>>();
            long Key(float x, float z) => ((long)Mathf.FloorToInt(x / 10f) << 32) ^ (uint)Mathf.FloorToInt(z / 10f);
            float x0w = cranes[0].x - 200f, x1w = cranes[cranes.Count - 1].x + 200f, zc = cranes.Average(c => c.z);
            foreach (var mf in world.GetComponentsInChildren<MeshFilter>(false))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null || !mr.enabled || mf.name.EndsWith("_LOD1") || mf.name.EndsWith("_LOD2")) continue;
                var bb = mr.bounds;
                if (bb.max.x < x0w || bb.min.x > x1w || bb.max.z < zc - 150f || bb.min.z > zc + 60f) continue;
                // iteration 30: ground-like = not under Buildings / Port / Landmarks (the quay paving and seawall meshes are tall)
                bool skip = false;
                for (var tr = mf.transform; tr != null; tr = tr.parent) if (tr.name == "Buildings" || tr.name == "Port" || tr.name == "Landmarks" || tr.name == "HighwayLights") { skip = true; break; }
                if (skip) continue;
                var sm = mr.sharedMaterials;
                if (sm.Any(m => m != null && (m.name.Contains("Water") || m.name.Contains("Foam")))) continue;
                var vv = mf.sharedMesh.vertices; var tt = mf.sharedMesh.triangles; var l2w = mf.transform.localToWorldMatrix;
                for (int i = 0; i < tt.Length; i += 3)
                {
                    var a = l2w.MultiplyPoint3x4(vv[tt[i]]); var b = l2w.MultiplyPoint3x4(vv[tt[i + 1]]); var c = l2w.MultiplyPoint3x4(vv[tt[i + 2]]);
                    float ax0 = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), ax1 = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                    float az0 = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), az1 = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                    if (ax1 - ax0 > 3000f || az1 - az0 > 3000f) continue;   // iteration 35: the apron paving uses quads over 300 m
                    for (float gx = Mathf.Floor(ax0 / 10f) * 10f; gx <= ax1; gx += 10f)
                        for (float gz = Mathf.Floor(az0 / 10f) * 10f; gz <= az1; gz += 10f)
                        {
                            long k = Key(gx + 0.01f, gz + 0.01f);
                            if (!grid.TryGetValue(k, out var lst)) grid[k] = lst = new List<Vector3>();
                            lst.Add(a); lst.Add(b); lst.Add(c);
                        }
                }
            }
            var terrains = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include);
            float cap = float.MaxValue;   // ignore surfaces above this (decks, roofs) while sampling the quay
            float GroundY(float x, float z)
            {
                float best = float.NegativeInfinity;
                if (grid.TryGetValue(Key(x, z), out var lst))
                    for (int i = 0; i < lst.Count; i += 3)
                    {
                        var a = lst[i]; var b = lst[i + 1]; var c = lst[i + 2];
                        float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                        if (Mathf.Abs(d) < 1e-6f) continue;
                        float w0 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d, w1 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d, w2 = 1f - w0 - w1;
                        if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
                        float yy = w0 * a.y + w1 * b.y + w2 * c.y;
                        if (yy <= cap) best = Mathf.Max(best, yy);
                    }
                foreach (var t in terrains)
                {
                    var o = t.GetPosition(); var s = t.terrainData.size;
                    if (x >= o.x && x <= o.x + s.x && z >= o.z && z <= o.z + s.z) best = Mathf.Max(best, t.SampleHeight(new Vector3(x, 0f, z)) + o.y);
                }
                return best;
            }

            // quay edge under each crane: first point south of the crane centre where the ground drops below 1 m
            // iteration 30: per crane, walk seaward from the landside leg line; water = ground more than 1.2 m below the
            // crane's own base (the data quay level), surfaces above base + 2 m ignored
            var edges = new List<float>();
            var edgeOf = new float[cranes.Count];
            for (int ci = 0; ci < cranes.Count; ci++)
            {
                var c = cranes[ci];
                cap = c.y + 2f;
                float edge = float.NaN;
                for (float dz = -15f; dz < 90f; dz += 0.5f)
                {
                    float gy = Mathf.Max(GroundY(c.x - 6f, c.z - dz), GroundY(c.x + 6f, c.z - dz));
                    if (gy < c.y - 1.2f) { edge = c.z - dz + 0.5f; break; }
                }
                edgeOf[ci] = edge;
                if (!float.IsNaN(edge)) edges.Add(edge);
            }
            cap = float.MaxValue;
            if (edges.Count == 0) return "FAIL: no quay edge found south of the crane line\n";
            float edgeZ = Median(edges);
            float quayY = Median(cranes.Select(c => c.y).ToList());
            sb.AppendLine($"  {cranes.Count} cranes x {cranes[0].x:0}-{cranes[cranes.Count - 1].x:0} on z {zc:0.0}; quay edge z {edgeZ:0.0} (samples {string.Join(", ", edges.Select(e => e.ToString("0.0")))}), quay level {quayY:0.00}");

            var steel = Mat("M_Metal_Galvanized") ?? Mat("Steel");
            var concrete = Mat("M_Concrete_Deck") ?? Mat("Concrete");
            var white = Mat("M_Road_Marking_White") ?? Mat("Marking_White");
            var bollardM = LitMat("M_Port_BollardBlack", new Color(0.06f, 0.06f, 0.065f), 0.35f, 0.6f);
            var hullTop = LitMat("M_Ship_HullNavy", new Color(0.06f, 0.11f, 0.2f), 0.4f, 0.3f);
            var hullBoot = LitMat("M_Ship_BootTop", new Color(0.03f, 0.03f, 0.035f), 0.35f, 0.3f);
            var hullBottom = LitMat("M_Ship_Antifoul", new Color(0.36f, 0.07f, 0.06f), 0.3f, 0.1f);
            var deckM = LitMat("M_Ship_Deck", new Color(0.32f, 0.22f, 0.17f), 0.25f, 0.2f);
            var superM = LitMat("M_Ship_Superstructure", new Color(0.82f, 0.82f, 0.8f), 0.35f, 0.1f);
            var funnelM = LitMat("M_Ship_Funnel", new Color(0.62f, 0.1f, 0.07f), 0.4f, 0.2f);
            var windowM = Mat("Glass_Blue") != null ? LitMat("M_Ship_Windows", new Color(0.05f, 0.08f, 0.1f), 0.92f, 0.5f) : null;
            var boxMats = new[] { "Container_orange", "Container_blue", "Container_red", "Container_teal", "Container_grey", "Container_2e5e9a", "Container_b23a2e", "Container_c9b036", "Container_7a7f86", "Container_3c4650" }
                .Select(Mat).Where(m => m != null).ToArray();
            var parts = new Dictionary<Material, MB>();
            MB Get(Material m) { if (!parts.TryGetValue(m, out var mb)) parts[m] = mb = new MB(); return mb; }

            // crane rails: landside / waterside leg lines (crane legs at z +-15 from the crane point), full crane line + 60 m
            float rx0 = cranes[0].x - 60f, rx1 = cranes[cranes.Count - 1].x + 60f;
            foreach (float dz in new[] { -15f, 15f })
            {
                float z = zc + dz;
                for (float x = rx0; x < rx1; x += 20f)
                {
                    float xb = Mathf.Min(x + 20f, rx1);
                    float y0 = Mathf.Max(GroundY(x, z), quayY - 0.5f), y1 = Mathf.Max(GroundY(xb, z), quayY - 0.5f);
                    foreach (float s in new[] { -0.8f, 0.8f })   // two rails per leg line, 1.6 m gauge
                    {
                        Box(Get(concrete), new Vector3((x + xb) / 2f, (y0 + y1) / 2f + 0.01f, z + s), Vector3.right, Vector3.forward, xb - x, 0.06f, 0.9f);
                        Box(Get(steel), new Vector3((x + xb) / 2f, (y0 + y1) / 2f + 0.07f, z + s), Vector3.right, Vector3.forward, xb - x, 0.08f, 0.14f);
                    }
                }
            }
            // safety line 1.5 m in from the edge, bollards on the edge every 24 m
            int bollards = 0;
            for (float x = rx0; x < rx1; x += 6f)
            {
                float y = GroundY(x + 1.5f, edgeZ + 1.5f);
                if (y < Sea + 1f) continue;
                Box(Get(white), new Vector3(x + 1.5f, y + 0.015f, edgeZ + 1.5f), Vector3.right, Vector3.forward, 2.9f, 0.02f, 0.25f);
            }
            for (float x = rx0; x <= rx1; x += 24f)
            {
                float y = GroundY(x, edgeZ + 0.7f);
                if (y < Sea + 1f) continue;
                var p = new Vector3(x, y, edgeZ + 0.7f);
                Cyl(Get(bollardM), p, 0.32f, 0f, 0.55f, 10);
                Cyl(Get(bollardM), p + Vector3.up * 0.55f, 0.42f, 0f, 0.14f, 10);
                bollards++;
            }

            // iteration 33: parked straddle carriers on the apron landside of each crane (two per crane, 25 m behind the
            // landside leg line), skipped where a data container stack is within 9 m
            var yellow = LitMat("M_Port_StraddleYellow", new Color(0.82f, 0.6f, 0.08f), 0.4f, 0.2f);
            var cont = new List<Vector2>();
            var cl = File.ReadAllLines("Assets/_Veyra/Data/container_placements.csv");
            var ch = cl[0].Split(',');
            int kx = System.Array.IndexOf(ch, "unity_x"), kz = System.Array.IndexOf(ch, "unity_z");
            foreach (var l in cl.Skip(1)) { var f = l.Split(','); if (f.Length > kz) cont.Add(new Vector2(float.Parse(f[kx], CultureInfo.InvariantCulture), float.Parse(f[kz], CultureInfo.InvariantCulture))); }
            int carriers = 0;
            foreach (var c in cranes)
                foreach (float dx in new[] { -22f, 22f })
                {
                    var p = new Vector3(c.x + dx, 0f, zc + 15f + 25f);
                    if (cont.Any(q => (q - new Vector2(p.x, p.z)).sqrMagnitude < 9f * 9f)) continue;
                    p.y = GroundY(p.x, p.z);
                    if (p.y < Sea + 1f) continue;
                    // frame 15 m tall, 6 m wide (x) by 9 m long (z): 4 legs, two top beams, cab, 8 wheels
                    foreach (float lx in new[] { -3f, 3f })
                        foreach (float lz in new[] { -4.3f, 4.3f })
                        {
                            Box(Get(yellow), p + new Vector3(lx, 8f, lz), Vector3.right, Vector3.forward, 0.6f, 14f, 0.8f);
                            Cyl(Get(bollardM), p + new Vector3(lx, 0f, lz), 0.6f, 0f, 1.2f, 10);
                        }
                    foreach (float lx in new[] { -3f, 3f }) Box(Get(yellow), p + new Vector3(lx, 15.2f, 0f), Vector3.right, Vector3.forward, 0.9f, 1.4f, 9.6f);
                    Box(Get(yellow), p + new Vector3(0f, 15.2f, -4.3f), Vector3.right, Vector3.forward, 6.6f, 1.2f, 0.9f);
                    Box(Get(yellow), p + new Vector3(0f, 15.2f, 4.3f), Vector3.right, Vector3.forward, 6.6f, 1.2f, 0.9f);
                    Box(Get(superM), p + new Vector3(2.4f, 17f, 3.6f), Vector3.right, Vector3.forward, 1.8f, 2.2f, 1.8f);
                    carriers++;
                }

            // ships: one per pair of cranes from the west, 50 m gaps; length from the pair's span
            var root = new GameObject("Port") { isStatic = true }.transform;
            root.SetParent(world.transform, false);
            var rng = new System.Random(26);
            int ships = 0, boxes = 0;
            var hullCols = new List<Mesh>();
            for (int i = 0; i + 1 < cranes.Count && ships < 3; i += 3)
            {
                float cx = (cranes[i].x + cranes[i + 1].x) * 0.5f;
                float len = Mathf.Clamp(cranes[i + 1].x - cranes[i].x + 120f, 200f, 300f);
                float beam = 34f, free = 11f, draft = 9f;
                float own = float.IsNaN(edgeOf[i]) ? edgeZ : float.IsNaN(edgeOf[i + 1]) ? edgeOf[i] : Mathf.Min(edgeOf[i], edgeOf[i + 1]);
                float hz = own - 3f - beam * 0.5f;   // 3 m fender gap off this berth's own edge
                var c = new Vector3(cx, Sea, hz);
                bool bowEast = (ships & 1) == 0;
                boxes += Ship(Get, c, len, beam, free, draft, bowEast, hullTop, hullBoot, hullBottom, deckM, superM, funnelM, windowM ?? superM, boxMats, rng);
                var cm = new MB(); Box(cm, c + Vector3.up * (free - draft) * 0.5f, Vector3.right, Vector3.forward, len, free + draft, beam);
                hullCols.Add(Save($"PORT_ShipCol_{ships}", cm));
                sb.AppendLine($"  ship {ships}: centre ({cx:0}, {hz:0.0}), {len:0} x {beam:0} m, bow {(bowEast ? "east" : "west")}");
                ships++;
            }

            var keep = new HashSet<string>();
            for (int k = 0; k < hullCols.Count; k++) keep.Add($"PORT_ShipCol_{k}");
            foreach (var kv in parts)
            {
                if (kv.Value.T.Count == 0 || kv.Key == null) continue;
                string name = $"PORT_{kv.Key.name}";
                var mesh = Save(name, kv.Value);
                keep.Add(name);
                var g = new GameObject(kv.Key.name) { isStatic = true };
                g.transform.SetParent(root, false);
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = g.AddComponent<MeshRenderer>();
                r.sharedMaterial = kv.Key;
                r.shadowCastingMode = kv.Key == white ? ShadowCastingMode.Off : ShadowCastingMode.On;
                var lg = g.AddComponent<LODGroup>();
                lg.SetLODs(new[] { new LOD(kv.Key == white || kv.Key == bollardM || kv.Key == steel ? 0.01f : 0.001f, new Renderer[] { r }) });
                lg.RecalculateBounds();
            }
            for (int k = 0; k < hullCols.Count; k++)
            {
                var g = new GameObject($"UCX_Ship_{k}") { isStatic = true };
                g.transform.SetParent(root, false);
                var mc = g.AddComponent<MeshCollider>(); mc.sharedMesh = hullCols[k]; mc.convex = true;
            }
            foreach (var f in Directory.GetFiles(MeshDir, "*.asset"))
                if (!keep.Contains(Path.GetFileNameWithoutExtension(f))) AssetDatabase.DeleteAsset(f.Replace('\\', '/'));
            AssetDatabase.SaveAssets();
            sb.AppendLine($"  rails {rx1 - rx0:0} m x 2 lines, {bollards} bollards, {carriers} straddle carriers, {ships} ships with {boxes} deck containers");
            Directory.CreateDirectory("Logs/C");
            File.WriteAllText("Logs/C/port.txt", sb.ToString());
            return sb.ToString();
        }

        static float Median(List<float> v) { v.Sort(); return v[v.Count / 2]; }

        /// <summary>Container ship along X: hull, deck bays, accommodation aft, funnel. Returns the deck container count.</summary>
        static int Ship(System.Func<Material, MB> Get, Vector3 c, float len, float beam, float free, float draft, bool bowEast,
                        Material top, Material boot, Material bottom, Material deck, Material sup, Material funnel, Material win, Material[] boxMats, System.Random rng)
        {
            float dir = bowEast ? 1f : -1f;
            Vector3 Fw(float along, float y, float across) => c + new Vector3(along * dir, y, across);
            float half = len * 0.5f, hb = beam * 0.5f, bow = 28f;
            // hull sections: (along, half-width) from stern to bow tip
            var sec = new List<(float s, float w)> { (-half, hb * 0.82f), (-half + 6f, hb * 0.97f), (-half + 18f, hb), (half - bow, hb), (half - bow * 0.5f, hb * 0.82f), (half - 6f, hb * 0.35f), (half, 0.6f) };
            void Band(Material m, float y0, float y1, float inset)
            {
                for (int i = 0; i + 1 < sec.Count; i++)
                {
                    var (s0, w0) = sec[i]; var (s1, w1) = sec[i + 1];
                    w0 = Mathf.Max(0.3f, w0 - inset); w1 = Mathf.Max(0.3f, w1 - inset);
                    foreach (float side in new[] { -1f, 1f })
                        Quad(Get(m), Fw(s0, y0, side * w0), Fw(s1, y0, side * w1), Fw(s1, y1, side * w1), Fw(s0, y1, side * w0), new Vector3(0f, 0f, side) + Vector3.right * dir * (s1 > half - bow ? 0.6f : 0f));
                }
                // transom
                Quad(Get(m), Fw(-half, y0, -Mathf.Max(0.3f, sec[0].w - inset)), Fw(-half, y0, Mathf.Max(0.3f, sec[0].w - inset)), Fw(-half, y1, Mathf.Max(0.3f, sec[0].w - inset)), Fw(-half, y1, -Mathf.Max(0.3f, sec[0].w - inset)), -Vector3.right * dir);
            }
            Band(bottom, -draft, -0.4f, 1.2f);
            Band(boot, -0.4f, 1.2f, 0f);
            Band(top, 1.2f, free, 0f);
            // main deck (polygon fan from the centre line)
            for (int i = 0; i + 1 < sec.Count; i++)
            {
                var (s0, w0) = sec[i]; var (s1, w1) = sec[i + 1];
                Quad(Get(deck), Fw(s0, free, -w0), Fw(s1, free, -w1), Fw(s1, free, w1), Fw(s0, free, w0), Vector3.up);
            }
            // bulwark + forecastle
            Box(Get(top), Fw(half - bow * 0.55f, free + 1.6f, 0f), Vector3.right, Vector3.forward, bow * 0.8f, 3.2f, beam * 0.62f);
            // accommodation block aft: 6 decks, bridge with wings, funnel behind
            float ax = -half + 24f;
            Box(Get(sup), Fw(ax, free + 9f, 0f), Vector3.right, Vector3.forward, 14f, 18f, beam * 0.72f);
            for (int dk = 0; dk < 6; dk++)
                foreach (float side in new[] { -1f, 1f })
                    Box(Get(win), Fw(ax, free + 2.2f + dk * 2.8f, side * (beam * 0.36f + 0.02f)), Vector3.right, Vector3.forward, 11f, 1.0f, 0.06f);
            Box(Get(sup), Fw(ax + 2f, free + 19f, 0f), Vector3.right, Vector3.forward, 9f, 2.6f, beam + 2f);   // bridge + wings
            Box(Get(win), Fw(ax + 6.55f, free + 19.3f, 0f), Vector3.right, Vector3.forward, 0.08f, 1.2f, beam * 0.9f);
            Box(Get(funnel), Fw(ax - 11f, free + 8f, 0f), Vector3.right, Vector3.forward, 7f, 16f, 6f);
            // iteration 32: enclosed lifeboats on davits each side of the accommodation, radar mast with scanner on the bridge
            foreach (float side in new[] { -1f, 1f })
            {
                var lb = Fw(ax - 2f, free + 7.5f, side * (beam * 0.36f + 2.2f));
                Box(Get(funnel), lb, Vector3.right, Vector3.forward, 8.5f, 2.6f, 3.0f);
                Box(Get(sup), lb + Vector3.up * 1.5f, Vector3.right, Vector3.forward, 6.5f, 0.6f, 2.4f);
                foreach (float a in new[] { -3.5f, 3.5f })
                    Box(Get(boot), Fw(ax - 2f + a, free + 9.3f, side * (beam * 0.36f + 1.2f)), Vector3.right, Vector3.forward, 0.4f, 4.2f, 0.4f);
            }
            Box(Get(sup), Fw(ax + 1f, free + 23.5f, 0f), Vector3.right, Vector3.forward, 0.6f, 6.4f, 0.6f);
            Box(Get(sup), Fw(ax + 1f, free + 24.5f, 0f), Vector3.right, Vector3.forward, 0.3f, 0.3f, 7f);
            Box(Get(boot), Fw(ax + 1f, free + 26.9f, 0f), Vector3.right, Vector3.forward, 0.5f, 0.25f, 4.2f);
            Box(Get(boot), Fw(ax - 11f, free + 16.4f, 0f), Vector3.right, Vector3.forward, 7.2f, 1.2f, 6.2f);
            // deck containers: 40 ft bays from the accommodation to the forecastle, 13 rows across, 1-6 tiers (lower at the ends)
            int n = 0;
            const float L40 = 12.19f, W = 2.44f, H = 2.59f;
            float s0b = ax + 10f, s1b = half - bow - 2f;
            int rows = Mathf.FloorToInt((beam - 2f) / (W + 0.08f));
            for (float s = s0b; s + L40 <= s1b; s += L40 + 1.2f)
            {
                float tFrac = (s - s0b) / Mathf.Max(1f, s1b - s0b);
                int tierCap = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(3f, 6f, Mathf.Sin(tFrac * Mathf.PI))), 1, 6);
                for (int r = 0; r < rows; r++)
                {
                    float across = (r - (rows - 1) * 0.5f) * (W + 0.08f);
                    int tiers = Mathf.Max(1, tierCap - (rng.NextDouble() < 0.25 ? rng.Next(0, 3) : 0));
                    for (int t = 0; t < tiers; t++)
                    {
                        var m = boxMats.Length > 0 ? boxMats[rng.Next(boxMats.Length)] : top;
                        Box(Get(m), Fw(s + L40 * 0.5f, free + 0.3f + t * H + H * 0.5f, across), Vector3.right, Vector3.forward, L40, H - 0.04f, W);
                        n++;
                    }
                }
            }
            return n;
        }

        static void Quad(MB m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
        {
            int i = m.V.Count;
            m.V.Add(a); m.V.Add(b); m.V.Add(c); m.V.Add(d);
            float w = (b - a).magnitude, h = (d - a).magnitude;
            m.UV.Add(new Vector2(0, 0)); m.UV.Add(new Vector2(w / 4f, 0)); m.UV.Add(new Vector2(w / 4f, h / 4f)); m.UV.Add(new Vector2(0, h / 4f));
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-10f) n = Vector3.Cross(c - a, d - a);
            if (Vector3.Dot(n, facing) >= 0f) m.T.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            else m.T.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
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

        static Vector3 Dir(float a) => new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));

        static void Cyl(MB m, Vector3 c, float r, float y0, float y1, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                Quad(m, c + Dir(a0) * r + Vector3.up * y0, c + Dir(a1) * r + Vector3.up * y0, c + Dir(a1) * r + Vector3.up * y1, c + Dir(a0) * r + Vector3.up * y1, Dir((a0 + a1) * 0.5f));
                int k = m.V.Count;
                m.V.Add(c + Vector3.up * y1); m.V.Add(c + Dir(a0) * r + Vector3.up * y1); m.V.Add(c + Dir(a1) * r + Vector3.up * y1);
                m.UV.Add(Vector2.zero); m.UV.Add(Vector2.right); m.UV.Add(Vector2.up);
                if (Vector3.Cross(m.V[k + 1] - m.V[k], m.V[k + 2] - m.V[k]).y >= 0f) m.T.AddRange(new[] { k, k + 1, k + 2 }); else m.T.AddRange(new[] { k, k + 2, k + 1 });
            }
        }

        static Mesh Save(string name, MB mb)
        {
            string path = $"{MeshDir}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh { name = name };
            mesh.Clear();
            mesh.indexFormat = mb.V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(mb.V); mesh.SetUVs(0, mb.UV); mesh.SetTriangles(mb.T, 0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            if (created) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
