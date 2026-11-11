using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.Core;
using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// BUG-111 (Phase B, cards W4/W5): road render meshes from the drive surface (BUG-108), so what is drawn is exactly what the
    /// wheels drive on. "Veyra/World/Build Road Render Meshes".
    /// <list type="bullet">
    /// <item>Carriageway: the drive-surface grid of each road (World/DriveSurface/DS_&lt;road&gt;_&lt;chunk&gt;, 2 m rows) 4 mm
    /// below the collider, with a 1 % crown and a 0.45 m gutter pan (−2.5 cm) along each edge. UV0 in metres (u across,
    /// v along the road), UV1 = (0–1 across, lane count) for the Veyra/Road wear lanes, vertex colours R wheel-track wear
    /// (more on junction approaches), G edge grime, B puddle mask (gutters and low noise), A patch/age (seeded per 40 m).</item>
    /// <item>Urban roads (AR, CO, SV) off structures: bevelled granite kerb (15 cm, 30 cm top) and a sidewalk (3 m AR, 2.5 m CO/SV)
    /// with 2 % crossfall, then a 1 m skirt down into the ground. Rural roads: 0.8 m shoulder and an embankment skirt.
    /// Structures (bridges, elevated, tunnels): parapet kerb only (the blockout deck stays). Kerbs and sidewalks open where
    /// they would stand in another carriageway (junction mouths).</item>
    /// <item>Markings (2 cm above the surface, no z-fighting): white edge lines, yellow centre lines between opposing traffic
    /// (double solid on undivided roads, solid along the median on divided roads), white dashed lane lines (3 m / 6 m urban,
    /// 3 m / 9 m fast roads); none inside junctions (+6 m) or on gravel.</item>
    /// <item>Junctions: where a road lies inside a stronger road's carriageway its surface drops 6 mm, so the stronger road draws
    /// on top without z-fighting.</item>
    /// <item>Output: one renderer per 250 m chunk under World/Roads/Chunk_&lt;Cell&gt;_&lt;ij&gt; with a LODGroup (LOD0 full,
    /// LOD1 every 2nd row and 3 columns, LOD2 every 4th row, no markings), meshes in World/Generated/Roads. The blockout road
    /// renderers (CityMeshes asphalt, asphalt_worn, marking_*, kerb, sidewalk and the C2 ease ribbons) are disabled, never
    /// deleted (their colliders are untouched). Idempotent: World/Roads is rebuilt and meshes are overwritten in place.</item>
    /// </list>
    /// </summary>
    public static class RoadRenderBuilder
    {
        const string RootName = "Roads";
        public const string MeshDir = "Assets/_Veyra/World/Generated/Roads";
        const string MatDir = "Assets/_Veyra/World/Generated/Materials";
        const float Step = 2f, ColumnSpacing = 2.5f, Sink = 0.004f, Crown = 0.01f, GutterW = 0.45f, GutterDepth = 0.025f;
        const float JunctionDrop = 0.006f, MarkLift = 0.02f, MedianHalf = 1.5f;
        static readonly HashSet<string> Urban = new HashSet<string> { "AR", "CO", "SV" };
        static readonly string[] BlockoutGroups = { "asphalt", "asphalt_worn", "marking_white", "marking_yellow", "kerb", "sidewalk" };

        enum M { Asphalt, AsphaltFast, Gravel, Kerb, Sidewalk, Shoulder, White, Yellow, Metal, Chevron, Count }
        static readonly string[] MatFiles = { "M_Asphalt_Worn", "M_Asphalt_New", "M_Gravel", "M_Kerb_Granite", "M_Sidewalk_Concrete", "M_Gravel", "M_Road_Marking_White", "M_Road_Marking_Yellow", "M_Metal_Galvanized", "M_Sign_Chevron" };
        /// <summary>Guardrail collision per chunk (vertical strips, non-convex MeshCollider, layer Default).</summary>
        sealed class Col { public readonly List<Vector3> V = new List<Vector3>(); public readonly List<int> T = new List<int>(); }
        static Dictionary<string, Col> railCols;
        static Terrain[] terrains;
        static int railRows, chevrons;

        sealed class Part
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>(), UV1 = new List<Vector2>();
            public readonly List<Color32> C = new List<Color32>();
            public readonly List<int>[] T = new List<int>[(int)M.Count];
            public Part() { for (int i = 0; i < T.Length; i++) T[i] = new List<int>(); }
            public int Add(Vector3 v, Vector2 uv, Vector2 uv1, Color32 c) { V.Add(v); UV.Add(uv); UV1.Add(uv1); C.Add(c); return V.Count - 1; }
            public void Quad(M m, int a, int b, int c, int d) { var t = T[(int)m]; t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d); }
        }

        [MenuItem("Veyra/World/Build Road Render Meshes (BUG-111)")]
        static void Menu()
        {
            if (EditorSceneManager.GetActiveScene().path != CellDistributor.AuthoringScene)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(CellDistributor.AuthoringScene);
            }
            Debug.Log(Build());
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        /// <summary>Batch: -executeMethod VeyraCity.EditorTools.RoadRenderBuilder.BatchBuild (saves the authoring scene).</summary>
        public static void BatchBuild()
        {
            int code = 0;
            try
            {
                var scene = EditorSceneManager.OpenScene(CellDistributor.AuthoringScene);
                string log = Build();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Directory.CreateDirectory("Logs/B1");
                File.WriteAllText("Logs/B1/road_render.txt", log);
                Debug.Log(log);
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static Material[] mats;
        static readonly List<WorldData.RoadHit> hits = new List<WorldData.RoadHit>();
        static int[] rankPos;

        public static string Build()
        {
            var log = new StringBuilder($"BUG-111 road render meshes {DateTime.Now:yyyy-MM-dd HH:mm}\n");
            WorldData.EnsureLoaded();
            GameObject world = EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == "World");
            if (world == null) throw new InvalidOperationException("no World root");
            var ds = world.transform.Find("DriveSurface");
            if (ds == null) throw new InvalidOperationException("no World/DriveSurface (run Veyra/Physics/Build Drive Surface first)");
            EnsureChevronMaterial();
            mats = MatFiles.Select(f => AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{f}.mat")).ToArray();
            railCols = new Dictionary<string, Col>();
            terrains = UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include);
            railRows = 0; chevrons = 0;
            for (int i = 0; i < mats.Length; i++) if (mats[i] == null) throw new InvalidOperationException($"material {MatFiles[i]} missing in {MatDir}");
            Directory.CreateDirectory(MeshDir);

            var roads = WorldData.Roads;
            var order = Enumerable.Range(0, roads.Count).OrderByDescending(r => WorldData.Rank(roads[r])).ThenByDescending(r => roads[r].Data.width).ThenBy(r => r).ToArray();
            rankPos = new int[roads.Count];
            for (int i = 0; i < order.Length; i++) rankPos[order[i]] = i;

            // drive-surface grids per road
            var grids = new Dictionary<string, SortedDictionary<int, Mesh>>();
            foreach (var mc in ds.GetComponentsInChildren<MeshCollider>(true))
            {
                var n = mc.name; // DS_<road>_<chunk:D3>
                int us = n.LastIndexOf('_');
                if (!n.StartsWith("DS_") || us < 4 || !int.TryParse(n.Substring(us + 1), out int chunk) || mc.sharedMesh == null) continue;
                string id = n.Substring(3, us - 3);
                if (!grids.TryGetValue(id, out var d)) grids[id] = d = new SortedDictionary<int, Mesh>();
                d[chunk] = mc.sharedMesh;
            }

            var parts = new Dictionary<string, Part[]>();   // chunk name -> LOD0..2
            int roadsDone = 0; long rows = 0; int junctionRows = 0, openKerbRows = 0;
            for (int r = 0; r < roads.Count; r++)
            {
                var road = roads[r];
                if (road.Data.cls == "RW" || !grids.TryGetValue(road.Data.id, out var chunks)) continue;
                int cols = Mathf.Max(2, Mathf.CeilToInt(road.Data.width / ColumnSpacing)) + 1;
                // reassemble the full grid: chunk c holds rows c*50 .. c*50+50
                var grid = new List<Vector3[]>();
                foreach (var kv in chunks)
                {
                    var v = kv.Value.vertices;
                    int nRows = v.Length / cols;
                    for (int k = grid.Count == 0 ? 0 : 1; k < nRows; k++)
                    {
                        var row = new Vector3[cols];
                        Array.Copy(v, k * cols, row, 0, cols);
                        grid.Add(row);
                    }
                }
                if (grid.Count < 2) continue;
                roadsDone++;
                rows += grid.Count;
                BuildRoad(r, road, grid, cols, parts, ref junctionRows, ref openKerbRows);
            }

            // scene objects
            var old = world.transform.Find(RootName);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var root = new GameObject(RootName) { isStatic = true }.transform;
            root.SetParent(world.transform, false);
            var keep = new HashSet<string>();
            long tris0 = 0, tris1 = 0, tris2 = 0; int maxTris = 0;
            foreach (var kv in parts.OrderBy(k => k.Key))
            {
                var go = new GameObject(kv.Key) { isStatic = true };
                go.transform.SetParent(root, false);
                var lods = new List<LOD>();
                float[] h = { 0.30f, 0.08f, 0.008f };
                for (int l = 0; l < 3; l++)
                {
                    var p = kv.Value[l];
                    if (p.T.All(t => t.Count == 0)) continue;   // a chunk corner with no rows at this LOD: no empty renderer
                    string name = $"RD_{kv.Key}_LOD{l}";
                    var mesh = ToMesh(name, p, out var used);
                    keep.Add(name);
                    var lg = new GameObject($"{kv.Key}_LOD{l}") { isStatic = true };
                    lg.transform.SetParent(go.transform, false);
                    lg.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = lg.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = used;
                    mr.shadowCastingMode = ShadowCastingMode.Off;   // flat ground: no visible shadow, saves the caster pass
                    mr.receiveShadows = true;
                    int t = 0;
                    for (int s = 0; s < mesh.subMeshCount; s++) t += (int)mesh.GetIndexCount(s) / 3;
                    if (l == 0) { tris0 += t; maxTris = Mathf.Max(maxTris, t); } else if (l == 1) tris1 += t; else tris2 += t;
                    lods.Add(new LOD(h[l], new Renderer[] { mr }));
                }
                var group = go.AddComponent<LODGroup>();
                group.SetLODs(lods.ToArray());
                group.fadeMode = LODFadeMode.CrossFade;
                group.animateCrossFading = true;
                group.RecalculateBounds();
                if (railCols.TryGetValue(kv.Key, out var col) && col.V.Count > 0)
                {
                    string cname = $"COL_guardrail_{kv.Key}";
                    var cm = SaveCollision(cname, col);
                    keep.Add(cname);
                    var cg = new GameObject("COL_guardrail") { isStatic = true };
                    cg.transform.SetParent(go.transform, false);
                    cg.AddComponent<MeshCollider>().sharedMesh = cm;
                }
            }
            foreach (var f in Directory.GetFiles(MeshDir, "*.asset"))
                if (!keep.Contains(Path.GetFileNameWithoutExtension(f))) AssetDatabase.DeleteAsset(f.Replace('\\', '/'));

            int hidden = HideBlockout(world.transform);
            AssetDatabase.SaveAssets();
            log.AppendLine($"roads {roadsDone} (runways skipped), {rows:N0} rows of 2 m, {parts.Count} chunks under World/{RootName}");
            log.AppendLine($"triangles LOD0 {tris0:N0} (max {maxTris:N0} per chunk, budget 300k), LOD1 {tris1:N0} ({100.0 * tris1 / Math.Max(1, tris0):0}%), LOD2 {tris2:N0} ({100.0 * tris2 / Math.Max(1, tris0):0}%)");
            log.AppendLine($"junction rows (markings off, surface under the stronger road) {junctionRows:N0}; kerb/sidewalk rows opened at junction mouths {openKerbRows:N0}");
            log.AppendLine($"blockout road renderers disabled: {hidden}");
            log.AppendLine($"D4 guardrails: {railRows * Step:N0} m of rail (with collision), {chevrons} chevron signs on curves under 80 m radius");
            return log.ToString();
        }

        // ------------------------------------------------------------------ per road

        static void BuildRoad(int r, RoadPath road, List<Vector3[]> grid, int cols, Dictionary<string, Part[]> parts, ref int junctionRows, ref int openKerbRows)
        {
            var d = road.Data;
            float hw = road.HalfWidth, width = d.width;
            bool urban = Urban.Contains(d.cls);
            bool gravel = d.cls == "GR";
            bool divided = road.Divided;
            bool fast = d.cls == "MW" || d.cls == "EX" || d.cls == "HW";
            M asphalt = gravel ? M.Gravel : fast ? M.AsphaltFast : M.Asphalt;
            int lanesPerDir = Mathf.Max(1, d.lanes);
            int lanesTotal = lanesPerDir * 2;
            float sidewalk = d.cls == "AR" ? 3f : 2.5f;
            float dashOn = 3f, dashCycle = fast ? 12f : 9f;
            int n = grid.Count;
            var path = road.Path;

            // per row: frame, structure, junction state, kerb openings
            var centre = new Vector3[n]; var right = new Vector3[n]; var s = new float[n];
            var structure = new bool[n]; var inJunction = new bool[n]; var nearJunction = new float[n];
            var openL = new bool[n]; var openR = new bool[n];
            for (int k = 0; k < n; k++)
            {
                s[k] = Mathf.Min(k * Step, path.Length);
                var row = grid[k];
                centre[k] = (row[0] + row[cols - 1]) * 0.5f;
                var rr = row[cols - 1] - row[0]; rr.y = 0f;
                right[k] = rr.sqrMagnitude > 1e-6f ? rr.normalized : Vector3.right;
                structure[k] = road.StructureAt(path.SegmentAt(s[k])) != StructureKind.None;
                WorldData.RoadsAt(centre[k], -(hw + 1f), 3f, hits);
                foreach (var h in hits)
                {
                    if (h.Road == r) continue;
                    if (h.Distance < WorldData.Roads[h.Road].HalfWidth + 0.5f) inJunction[k] = true;
                }
                for (int side = -1; side <= 1; side += 2)
                {
                    var kerbPt = centre[k] + right[k] * side * (hw + 1.2f);
                    kerbPt.y = centre[k].y;
                    WorldData.RoadsAt(kerbPt, -0.5f, 3f, hits);
                    bool open = hits.Any(h => h.Road != r);
                    if (side < 0) openL[k] = open; else openR[k] = open;
                }
            }
            // markings stay off 6 m around a junction; wear rises over 40 m of approach
            var markOff = new bool[n];
            for (int k = 0; k < n; k++)
            {
                if (!inJunction[k]) continue;
                junctionRows++;
                for (int q = Mathf.Max(0, k - 3); q <= Mathf.Min(n - 1, k + 3); q++) markOff[q] = true;
            }
            float lastJ = -1e9f;
            for (int k = 0; k < n; k++) { if (inJunction[k]) lastJ = s[k]; nearJunction[k] = s[k] - lastJ; }
            lastJ = 1e9f;
            for (int k = n - 1; k >= 0; k--) { if (inJunction[k]) lastJ = s[k]; nearJunction[k] = Mathf.Min(nearJunction[k], lastJ - s[k]); }
            // a kerb opening is widened by one row each way (fillet room)
            bool[] Widen(bool[] a) { var b = (bool[])a.Clone(); for (int k = 0; k < n; k++) if (a[k]) { if (k > 0) b[k - 1] = true; if (k + 1 < n) b[k + 1] = true; } return b; }
            openL = Widen(openL); openR = Widen(openR);
            float[] Ramp(bool[] open)
            {
                var f = new float[n];
                for (int k = 0; k < n; k++)
                {
                    int d = 99;
                    for (int q = Mathf.Max(0, k - 3); q <= Mathf.Min(n - 1, k + 3); q++) if (open[q]) d = Mathf.Min(d, Mathf.Abs(q - k));
                    f[k] = Mathf.Clamp01((d - 1) / 2f);   // next to an opening 0 (flush), 2 rows on 0.5, 3+ rows full height
                }
                return f;
            }
            var rampL = Ramp(openL); var rampR = Ramp(openR);
            for (int k = 0; k < n; k++) if (openL[k] || openR[k]) openKerbRows++;

            // carriageway height with junction drop
            Vector3 Surface(int k, int j)
            {
                var p = grid[k][j];
                float lat = -hw + width * j / (cols - 1);
                float crown = -Crown * Mathf.Abs(lat);
                float gut = Mathf.Abs(lat) > hw - GutterW && urban && !structure[k] ? -GutterDepth * Mathf.InverseLerp(hw - GutterW, hw, Mathf.Abs(lat)) : 0f;
                if (divided && Mathf.Abs(lat) < MedianHalf) crown = -Crown * MedianHalf;
                p.y += -Sink + crown * 0.5f + gut;   // half the crown: the collider is flat, the wheels sit within 1-2 cm
                if (inJunction[k] && StrongerAt(r, p)) p.y -= JunctionDrop;
                return p;
            }

            for (int lod = 0; lod < 3; lod++)
            {
                int stride = lod == 0 ? 1 : lod == 1 ? 2 : 4;
                int[] colIdx = lod == 0 ? Enumerable.Range(0, cols).ToArray() : lod == 1 ? new[] { 0, cols / 2, cols - 1 } : new[] { 0, cols - 1 };
                var rowsIdx = new List<int>();
                for (int k = 0; k < n; k += stride) rowsIdx.Add(k);
                if (rowsIdx[rowsIdx.Count - 1] != n - 1) rowsIdx.Add(n - 1);

                for (int ri = 0; ri + 1 < rowsIdx.Count; ri++)
                {
                    int k0 = rowsIdx[ri], k1 = rowsIdx[ri + 1];
                    var part = PartFor(parts, centre[k0], lod);
                    // carriageway strip
                    var a = new int[colIdx.Length]; var b = new int[colIdx.Length];
                    for (int c = 0; c < colIdx.Length; c++)
                    {
                        int j = colIdx[c];
                        float lat = -hw + width * j / (cols - 1);
                        a[c] = part.Add(Surface(k0, j), new Vector2(lat + hw, s[k0]), new Vector2((lat + hw) / width, lanesTotal), Wear(r, k0, lat, hw, s[k0], nearJunction[k0], urban));
                        b[c] = part.Add(Surface(k1, j), new Vector2(lat + hw, s[k1]), new Vector2((lat + hw) / width, lanesTotal), Wear(r, k1, lat, hw, s[k1], nearJunction[k1], urban));
                    }
                    for (int c = 0; c + 1 < colIdx.Length; c++) part.Quad(asphalt, a[c], a[c + 1], b[c], b[c + 1]);

                    // edges
                    for (int side = -1; side <= 1; side += 2)
                    {
                        int je = side < 0 ? 0 : cols - 1;
                        var e0 = Surface(k0, je); var e1 = Surface(k1, je);
                        var r0 = right[k0] * side; var r1 = right[k1] * side;
                        bool open = side < 0 ? (openL[k0] || openL[k1]) : (openR[k0] || openR[k1]);
                        if (structure[k0] || structure[k1])
                        {
                            if (!open)
                            {
                                Profile(part, M.Kerb, e0, e1, r0, r1, s[k0], s[k1], new[] { (0f, 0f), (0.02f, 0.18f), (0.35f, 0.2f) }, lod, 1f, 1f, true);
                                // iteration 3: bridge railing on the parapet kerb (posts every 2.5 m, rails at 0.6 and 1.05 m)
                                if (lod < 2)
                                {
                                    var b0 = e0 + r0 * 0.25f + Vector3.up * 0.2f; var b1 = e1 + r1 * 0.25f + Vector3.up * 0.2f;
                                    foreach (float ry in lod == 0 ? new[] { 0.6f, 1.05f } : new[] { 1.05f })
                                        Profile(part, M.Metal, b0 + Vector3.up * ry, b1 + Vector3.up * ry, r0, r1, s[k0], s[k1], new[] { (0f, 0f), (0f, 0.06f), (0.05f, 0.06f) }, lod);
                                    if (lod == 0)
                                        for (float ps = Mathf.Ceil(s[k0] / 2.5f) * 2.5f; ps < s[k1]; ps += 2.5f)
                                        {
                                            float fp = (ps - s[k0]) / Mathf.Max(0.01f, s[k1] - s[k0]);
                                            Post(part, M.Metal, Vector3.Lerp(b0, b1, fp), 0.06f, 1.1f, r0);
                                        }
                                }
                            }
                        }
                        else if (urban)
                        {
                            if (open) continue;
                            // iteration 2: dropped kerbs — kerb and pavement ramp to road level over the 2 rows before a junction
                            // mouth (iteration 1 review CAM_16: pavements ended in floating slabs)
                            float[] rp = side < 0 ? rampL : rampR;
                            float f0 = rp[k0], f1 = rp[k1];
                            Profile(part, M.Kerb, e0, e1, r0, r1, s[k0], s[k1], lod == 0
                                ? new[] { (0f, 0f), (0.02f, 0.11f), (0.06f, 0.15f), (0.30f, 0.15f) }
                                : new[] { (0f, 0f), (0.03f, 0.15f), (0.30f, 0.15f) }, lod, f0, f1, true);   // kerb: along-road UVs (iteration 3: barcode streaks)
                            var o0 = e0 + r0 * 0.30f + Vector3.up * 0.15f * f0; var o1 = e1 + r1 * 0.30f + Vector3.up * 0.15f * f1;
                            Profile(part, M.Sidewalk, o0, o1, r0, r1, s[k0], s[k1], new[] { (0f, 0f), (sidewalk, sidewalk * 0.02f) }, lod, f0, f1);
                            var w0 = o0 + r0 * sidewalk + Vector3.up * sidewalk * 0.02f * f0; var w1 = o1 + r1 * sidewalk + Vector3.up * sidewalk * 0.02f * f1;
                            // skirt into the ground: plain granite, along-road UVs (iteration 1: sidewalk texture squeezed into stripes)
                            Profile(part, M.Kerb, w0, w1, r0, r1, s[k0], s[k1], new[] { (0f, 0f), (0.05f, -1.2f) }, lod, 1f, 1f, true);
                        }
                        else
                        {
                            if (open) continue;
                            Profile(part, gravel ? M.Gravel : M.Shoulder, e0, e1, r0, r1, s[k0], s[k1], new[] { (0f, 0f), (0.8f, -0.05f), (2.6f, -1.4f) }, lod);
                        }
                    }

                    if (lod < 2) Roadside(part, road, k0, k1, s, grid, cols, structure, openL, openR, lod, Surface);
                    if (lod == 2 || gravel) continue;
                    // markings
                    if (markOff[k0] || markOff[k1]) continue;
                    // iteration 3: zebra crossing on urban approaches, in the 2 m row just before the junction markings stop
                    bool zebra = urban && lod == 0 && ((k1 + 1 < n && markOff[k1 + 1]) || (k0 - 1 >= 0 && markOff[k0 - 1]));
                    void Line(M m, float lat, float w)
                    {
                        float l0 = lat - w * 0.5f, l1 = lat + w * 0.5f;
                        var p00 = Lat(grid, k0, cols, hw, width, l0) + Vector3.up * MarkLift; var p01 = Lat(grid, k0, cols, hw, width, l1) + Vector3.up * MarkLift;
                        var p10 = Lat(grid, k1, cols, hw, width, l0) + Vector3.up * MarkLift; var p11 = Lat(grid, k1, cols, hw, width, l1) + Vector3.up * MarkLift;
                        var white = new Color32(255, 255, 255, 255);
                        int i0 = part.Add(p00, new Vector2(0f, s[k0]), Vector2.zero, white), i1 = part.Add(p01, new Vector2(1f, s[k0]), Vector2.zero, white);
                        int i2 = part.Add(p10, new Vector2(0f, s[k1]), Vector2.zero, white), i3 = part.Add(p11, new Vector2(1f, s[k1]), Vector2.zero, white);
                        part.Quad(m, i0, i1, i2, i3);
                    }
                    if (zebra)
                    {
                        for (float zl = -hw + 0.6f; zl < hw - 0.5f; zl += 1.0f) Line(M.White, zl + 0.25f, 0.5f);
                        continue;
                    }
                    bool dash = (s[k0] % dashCycle) < dashOn;
                    float edgeInset = fast ? 0.5f : 0.35f;
                    Line(M.White, -hw + edgeInset, 0.15f);
                    Line(M.White, hw - edgeInset, 0.15f);
                    float inner = divided ? MedianHalf + 0.25f : 0f;
                    if (divided) { Line(M.Yellow, -inner, 0.15f); Line(M.Yellow, inner, 0.15f); }
                    else if (d.cls != "MT" || lod == 0) { Line(M.Yellow, -0.12f, 0.11f); Line(M.Yellow, 0.12f, 0.11f); }
                    if (lanesPerDir > 1 && lod == 0 && dash)
                    {
                        float laneW = (hw - inner - edgeInset) / lanesPerDir;
                        for (int q = 1; q < lanesPerDir; q++) { Line(M.White, inner + laneW * q, 0.12f); Line(M.White, -(inner + laneW * q), 0.12f); }
                    }
                }
            }
        }

        // ------------------------------------------------------------------ D4 roadside: guardrails and chevrons

        static bool InD4(Vector3 p) => WorldData.DistrictAt(p)?.id == "D4";

        static float TerrainY(Vector3 p)
        {
            foreach (var t in terrains)
            {
                var o = t.GetPosition(); var sz = t.terrainData.size;
                if (p.x >= o.x && p.x <= o.x + sz.x && p.z >= o.z && p.z <= o.z + sz.z) return t.SampleHeight(p) + o.y;
            }
            return float.NaN;
        }

        /// <summary>
        /// D4 mountain roads (not urban classes): a W-beam guardrail on a side where the ground falls 1.5 m or more within 6 m of
        /// the edge, or on the outside of a curve tighter than 150 m radius; chevron boards every 16 m on the outside of curves
        /// under 80 m radius. Never at junction mouths or on structures (they have parapets).
        /// </summary>
        static void Roadside(Part part, RoadPath road, int k0, int k1, float[] s, List<Vector3[]> grid, int ncols,
                             bool[] structure, bool[] openL, bool[] openR, int lod, Func<int, int, Vector3> surface)
        {
            if (Urban.Contains(road.Data.cls) || structure[k0] || structure[k1]) return;
            var c0 = (grid[k0][0] + grid[k0][ncols - 1]) * 0.5f;
            if (!InD4(c0)) return;
            float kappa = road.Path.CurvatureAt(s[k0], 12f);
            int outside = kappa > 0f ? 1 : -1;
            var w = new Color32(255, 255, 255, 255);
            for (int side = -1; side <= 1; side += 2)
            {
                if (side < 0 ? (openL[k0] || openL[k1]) : (openR[k0] || openR[k1])) continue;
                int je = side < 0 ? 0 : ncols - 1;
                var e0 = surface(k0, je); var e1 = surface(k1, je);
                var rr = grid[k0][ncols - 1] - grid[k0][0]; rr.y = 0f; rr = rr.normalized * side;
                var rr1 = grid[k1][ncols - 1] - grid[k1][0]; rr1.y = 0f; rr1 = rr1.normalized * side;
                float ground = TerrainY(e0 + rr * 6f);
                float drop = float.IsNaN(ground) ? 0f : e0.y - ground;
                bool curve = side == outside && Mathf.Abs(kappa) > 1f / 150f;
                if (!(drop >= 1.5f || curve)) continue;
                if (lod == 0) railRows += k1 - k0;
                var b0 = e0 + rr * 0.75f; var b1 = e1 + rr1 * 0.75f;
                // W-beam: strips with a 5 cm outward ridge, both faces
                (float y, float o)[] prof = lod == 0
                    ? new[] { (0.45f, 0f), (0.55f, 0f), (0.6f, 0.05f), (0.65f, 0f), (0.75f, 0f) }
                    : new[] { (0.45f, 0f), (0.75f, 0f) };
                for (int q = 0; q + 1 < prof.Length; q++)
                {
                    int a0 = part.Add(b0 + rr * prof[q].o + Vector3.up * prof[q].y, new Vector2(s[k0], prof[q].y), Vector2.zero, w);
                    int a1 = part.Add(b0 + rr * prof[q + 1].o + Vector3.up * prof[q + 1].y, new Vector2(s[k0], prof[q + 1].y), Vector2.zero, w);
                    int a2 = part.Add(b1 + rr1 * prof[q].o + Vector3.up * prof[q].y, new Vector2(s[k1], prof[q].y), Vector2.zero, w);
                    int a3 = part.Add(b1 + rr1 * prof[q + 1].o + Vector3.up * prof[q + 1].y, new Vector2(s[k1], prof[q + 1].y), Vector2.zero, w);
                    part.Quad(M.Metal, a0, a2, a1, a3);
                    part.Quad(M.Metal, a0, a1, a2, a3);
                }
                if (lod != 0) continue;
                // posts every 4 m behind the rail
                for (float ps = Mathf.Ceil(s[k0] / 4f) * 4f; ps < s[k1]; ps += 4f)
                {
                    float f = (ps - s[k0]) / Mathf.Max(0.01f, s[k1] - s[k0]);
                    Post(part, M.Metal, Vector3.Lerp(b0, b1, f) + rr * 0.12f + Vector3.down * 0.4f, 0.1f, 1.15f, rr);
                }
                // collision strip 0-0.9 m
                var chunk = ChunkName(c0);
                if (!railCols.TryGetValue(chunk, out var col)) railCols[chunk] = col = new Col();
                int i = col.V.Count;
                col.V.Add(b0); col.V.Add(b0 + Vector3.up * 0.9f); col.V.Add(b1); col.V.Add(b1 + Vector3.up * 0.9f);
                col.T.AddRange(new[] { i, i + 1, i + 2, i + 2, i + 1, i + 3 });
                // chevrons on tight outside curves
                if (side != outside || Mathf.Abs(kappa) <= 1f / 80f) continue;
                for (float cs = Mathf.Ceil(s[k0] / 16f) * 16f; cs < s[k1]; cs += 16f)
                {
                    float f = (cs - s[k0]) / Mathf.Max(0.01f, s[k1] - s[k0]);
                    var pb = Vector3.Lerp(b0, b1, f) + rr * 0.5f;
                    Post(part, M.Metal, pb + Vector3.down * 0.3f, 0.07f, 2.0f, rr);
                    var along = b1 - b0; along.y = 0f; along.Normalize();
                    var board = pb + Vector3.up * 1.35f - rr * 0.05f;
                    float uA = side > 0 ? 0f : 1f, uB = 1f - uA;
                    int z0 = part.Add(board - along * 0.3f - Vector3.up * 0.375f, new Vector2(uA, 0f), Vector2.zero, w);
                    int z1 = part.Add(board + along * 0.3f - Vector3.up * 0.375f, new Vector2(uB, 0f), Vector2.zero, w);
                    int z2 = part.Add(board - along * 0.3f + Vector3.up * 0.375f, new Vector2(uA, 1f), Vector2.zero, w);
                    int z3 = part.Add(board + along * 0.3f + Vector3.up * 0.375f, new Vector2(uB, 1f), Vector2.zero, w);
                    part.Quad(M.Chevron, z0, z1, z2, z3);
                    part.Quad(M.Chevron, z1, z0, z3, z2);
                    chevrons++;
                }
            }
        }

        /// <summary>A square post (four sides) from base p, width w, height h.</summary>
        static void Post(Part part, M m, Vector3 p, float w, float h, Vector3 right)
        {
            var fwd = Vector3.Cross(right, Vector3.up).normalized * (w * 0.5f);
            var rt = right.normalized * (w * 0.5f);
            Vector3[] c = { -rt - fwd, rt - fwd, rt + fwd, -rt + fwd };
            var col = new Color32(255, 255, 255, 255);
            for (int i = 0; i < 4; i++)
            {
                var a = p + c[i]; var b = p + c[(i + 1) % 4];
                int i0 = part.Add(a, new Vector2(0f, 0f), Vector2.zero, col), i1 = part.Add(b, new Vector2(w, 0f), Vector2.zero, col);
                int i2 = part.Add(a + Vector3.up * h, new Vector2(0f, h), Vector2.zero, col), i3 = part.Add(b + Vector3.up * h, new Vector2(w, h), Vector2.zero, col);
                part.Quad(m, i0, i1, i2, i3);
                part.Quad(m, i1, i0, i3, i2);
            }
        }

        static string ChunkName(Vector3 p)
        {
            var w = WorldCoordinates.FromUnity(p);
            return WorldCoordinates.ChunkOf(Mathf.Clamp(w.x, 0f, 7999f), Mathf.Clamp(w.y, 0f, 5999f));
        }

        static Mesh SaveCollision(string name, Col c)
        {
            string path = $"{MeshDir}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh { name = name };
            mesh.Clear();
            mesh.indexFormat = c.V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(c.V);
            mesh.SetTriangles(c.T, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (created) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>M_Sign_Chevron: an original yellow-on-black chevron board (procedural 128 px texture), URP Lit. Editor only.</summary>
        static void EnsureChevronMaterial()
        {
            const string texPath = "Assets/_Veyra/Art/Textures/Roadside/T_Sign_Chevron.png";
            string matPath = $"{MatDir}/M_Sign_Chevron.mat";
            if (!File.Exists(texPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(texPath));
                var tex = new Texture2D(128, 128, TextureFormat.RGBA32, false);
                var yellow = new Color(0.95f, 0.72f, 0.05f); var black = new Color(0.03f, 0.03f, 0.03f);
                for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 128; x++)
                    {
                        float u = x / 127f, v = y / 127f;
                        bool border = x < 6 || y < 6 || x > 121 || y > 121;
                        float a = Mathf.Repeat(u + Mathf.Abs(v - 0.5f) * 0.9f, 0.5f);
                        tex.SetPixel(x, y, border ? black : (a > 0.12f && a < 0.32f) ? yellow : black);
                    }
                File.WriteAllBytes(texPath, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(texPath);
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_Sign_Chevron" };   // Editor code: allowed
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            mat.SetFloat("_Smoothness", 0.35f);
            EditorUtility.SetDirty(mat);
        }

        /// <summary>A strip outward from an edge (e0 at row 0, e1 at row 1) through (offset, rise) points.</summary>
        /// <summary>sc0/sc1 scale the rises at either row (dropped kerbs); alongU puts the road direction on u (vertical skirts).</summary>
        static void Profile(Part part, M m, Vector3 e0, Vector3 e1, Vector3 r0, Vector3 r1, float s0, float s1, (float off, float rise)[] pts, int lod,
                            float sc0 = 1f, float sc1 = 1f, bool alongU = false)
        {
            var c = new Color32(255, 255, 255, 255);
            int pa = -1, pb = -1;
            float u = 0f;
            for (int i = 0; i < pts.Length; i++)
            {
                if (i > 0) u += new Vector2(pts[i].off - pts[i - 1].off, pts[i].rise - pts[i - 1].rise).magnitude;
                var uv0 = alongU ? new Vector2(s0, u) : new Vector2(u, s0);
                var uv1 = alongU ? new Vector2(s1, u) : new Vector2(u, s1);
                int a = part.Add(e0 + r0 * pts[i].off + Vector3.up * pts[i].rise * sc0, uv0, Vector2.zero, c);
                int b = part.Add(e1 + r1 * pts[i].off + Vector3.up * pts[i].rise * sc1, uv1, Vector2.zero, c);
                if (i > 0)
                {
                    // face outward-up: winding so the normal points away from the road / up
                    bool leftSide = Vector3.Cross(e1 - e0, r0).y > 0f;
                    if (leftSide) part.Quad(m, pa, a, pb, b); else part.Quad(m, a, pa, b, pb);
                }
                pa = a; pb = b;
            }
        }

        /// <summary>Point at a lateral offset on row k of the drive-surface grid (linear between columns), 4 mm sunk.</summary>
        static Vector3 Lat(List<Vector3[]> grid, int k, int cols, float hw, float width, float lat)
        {
            float f = Mathf.Clamp01((lat + hw) / width) * (cols - 1);
            int j = Mathf.Min(cols - 2, Mathf.FloorToInt(f));
            var p = Vector3.Lerp(grid[k][j], grid[k][j + 1], f - j);
            p.y += -Sink - Crown * 0.5f * Mathf.Abs(lat);
            return p;
        }

        static bool StrongerAt(int r, Vector3 p)
        {
            WorldData.RoadsAt(p, 0f, 2.5f, hits);
            foreach (var h in hits) if (h.Road != r && rankPos[h.Road] < rankPos[r]) return true;
            return false;
        }

        static float Noise(float x, float y) => Mathf.PerlinNoise(x * 0.07f + 13.1f, y * 0.07f + 7.7f);

        static Color32 Wear(int r, int k, float lat, float hw, float s, float distJunction, bool urban)
        {
            float approach = Mathf.Clamp01(1f - distJunction / 40f);
            float wear = 0.45f + 0.55f * approach;
            float edge = Mathf.Clamp01((Mathf.Abs(lat) - (hw - 1.2f)) / 1.2f) * (urban ? 1f : 0.6f);
            float n = Noise(s + r * 97.3f, lat * 3f);
            float puddle = Mathf.Clamp01(edge * 0.8f + Mathf.Max(0f, n - 0.62f) * 2.5f);
            uint h = (uint)(r * 73856093) ^ (uint)(Mathf.FloorToInt(s / 40f) * 19349663);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            float patch = (h % 1000) / 1000f;
            return new Color32((byte)(wear * 255f), (byte)(edge * 255f), (byte)(puddle * 255f), (byte)(patch * 255f));
        }

        static Part PartFor(Dictionary<string, Part[]> parts, Vector3 p, int lod)
        {
            var w = WorldCoordinates.FromUnity(p);
            string chunk = WorldCoordinates.ChunkOf(Mathf.Clamp(w.x, 0f, 7999f), Mathf.Clamp(w.y, 0f, 5999f));
            if (!parts.TryGetValue(chunk, out var arr)) parts[chunk] = arr = new[] { new Part(), new Part(), new Part() };
            return arr[lod];
        }

        static Mesh ToMesh(string name, Part p, out Material[] used)
        {
            string path = $"{MeshDir}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created) mesh = new Mesh { name = name };
            mesh.Clear();
            mesh.indexFormat = p.V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(p.V);
            mesh.SetUVs(0, p.UV);
            mesh.SetUVs(1, p.UV1);
            mesh.SetColors(p.C);
            var subs = new List<int>();
            for (int m = 0; m < p.T.Length; m++) if (p.T[m].Count > 0) subs.Add(m);
            mesh.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) mesh.SetTriangles(p.T[subs[i]], i, false);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            used = subs.Select(m => mats[m]).ToArray();
            if (created) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>Disables (never deletes) the blockout road renderers that the generated meshes replace.</summary>
        static int HideBlockout(Transform world)
        {
            int n = 0;
            var city = world.Find("CityMeshes");
            if (city != null)
                foreach (var mr in city.GetComponentsInChildren<MeshRenderer>(true))
                {
                    // iteration 5: also by mesh name (M_<group>_<cx>_<cz>): some blockout objects have empty names (probe at CAM_16)
                    var mf0 = mr.GetComponent<MeshFilter>();
                    string mesh = mf0 != null && mf0.sharedMesh != null ? mf0.sharedMesh.name : "";
                    bool byMesh = BlockoutGroups.Any(g => mesh.StartsWith("M_" + g + "_"));
                    if ((BlockoutGroups.Contains(mr.name) || byMesh) && mr.enabled) { mr.enabled = false; n++; }
                }
            // iteration 4: blockout "concrete" pavements beside urban roads (review CAM_16: wide slabs with striped edges
            // next to the generated kerb + pavement): render copies without the triangles that lie within the road's
            // half-width + 4.5 m and within 1.5 m of its height, except on structures. Colliders are untouched.
            if (city != null)
            {
                var clipDir = MeshDir + "/BlockoutClip";
                System.IO.Directory.CreateDirectory(clipDir);
                foreach (var mr in city.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mr.name != "concrete" || !mr.enabled) continue;
                    var mf = mr.GetComponent<MeshFilter>();
                    var src = mf != null ? mf.sharedMesh : null;
                    if (src == null) continue;
                    if (src.name.StartsWith("CLIP_")) continue;   // already clipped (re-run)
                    var v = src.vertices; var tri = src.triangles; var keepT = new List<int>();
                    var l2w = mr.transform.localToWorldMatrix;
                    int removed = 0;
                    for (int i = 0; i < tri.Length; i += 3)
                    {
                        var c = l2w.MultiplyPoint3x4((v[tri[i]] + v[tri[i + 1]] + v[tri[i + 2]]) / 3f);
                        bool drop = false;
                        WorldData.RoadsAt(c, -4.5f, 1.5f, hits);
                        foreach (var h in hits)
                        {
                            var rd = WorldData.Roads[h.Road];
                            if (!Urban.Contains(rd.Data.cls) || rd.StructureAt(h.Segment) != StructureKind.None) continue;
                            drop = true; break;
                        }
                        if (drop) { removed++; continue; }
                        keepT.Add(tri[i]); keepT.Add(tri[i + 1]); keepT.Add(tri[i + 2]);
                    }
                    if (removed == 0) continue;
                    var clip = UnityEngine.Object.Instantiate(src);
                    clip.name = "CLIP_" + src.name;
                    clip.subMeshCount = 1;
                    clip.SetTriangles(keepT, 0);
                    clip.RecalculateBounds();
                    string path = $"{clipDir}/{clip.name}.asset";
                    if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
                    AssetDatabase.CreateAsset(clip, path);
                    mf.sharedMesh = clip;
                    n++;
                }
            }
            // C2 ease ribbons and patches draw the eased profile that the drive surface now carries
            foreach (var mr in world.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = mr.GetComponent<MeshFilter>();
                var m = mf != null ? mf.sharedMesh : null;
                if (m == null || !mr.enabled) continue;
                if ((m.name.StartsWith("Ease_") || m.name.StartsWith("Patch_")) && !m.name.EndsWith("_col")) { mr.enabled = false; n++; }
            }
            return n;
        }
    }
}
