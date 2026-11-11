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
    /// Card A6 (neon, LED and signage kit; Cyberpunk flavour used with restraint): "Veyra/World/Build Neon Signs".
    /// Original signage only — generic shop words and invented names (no real brands, logos or game text).
    /// <list type="bullet">
    /// <item>Text atlas Art/Textures/Neon/T_NeonSigns.png: 48 sign words drawn with the project pixel font as soft-glow tubes
    /// (white on black; the Veyra/Neon shader multiplies it by the sign colour, dims it by day via _VC_NightFactor).</item>
    /// <item>Storefront signs over the ground floor of D6 Neon Mile buildings and of D1 Crown Downtown buildings that front
    /// a collector/arterial road, on the facade facing the nearest road (data positions, footprints, yaw), with a dark
    /// backplate; vertical blade signs on the street corner of buildings taller than 14 m (D6 every one, D1 one in three);
    /// rooftop LED billboards on D6 buildings 20–80 m tall.</item>
    /// <item>Five neon colours (cyan, magenta, amber, signal red, white; Appendix B palette materials M_Neon_*), seeded per
    /// building. Merged per 250 m chunk and material under World/Signs (streamed with the cells).</item>
    /// </list>
    /// Idempotent. Report: Logs/C/neon_signs.txt.
    /// </summary>
    public static class NeonSignBuilder
    {
        const string AtlasPath = "Assets/_Veyra/Art/Textures/Neon/T_NeonSigns.png";
        const string MatDir = "Assets/_Veyra/World/Generated/Materials";
        const string MeshDir = "Assets/_Veyra/World/Generated/Signs";
        const int AtlasW = 1024, Rows = 48, RowH = 21;   // 48 rows of 21 px (7 px glyphs × 3)
        static readonly string[] Words =
        {
            "RAMEN", "KARAOKE", "HOTEL AURORA", "NOODLES", "OPEN 24H", "ARCADE", "PHARMACY", "DUMPLINGS", "TATTOO", "LIQUOR",
            "PAWN", "SUSHI BAR", "CAFE", "CLUB MERIDIAN", "BAR", "MOTEL AZURE", "CINEMA", "DINER", "BOWLING", "LAUNDRY",
            "PIZZA", "BOOKS", "RECORDS", "NIGHT MARKET", "TEA HOUSE", "BURGERS", "GARAGE", "HOSTEL", "SALON", "JAZZ",
            "DANCE", "POOL HALL", "TACOS", "NAIL SPA", "GAMES", "RADIO", "PHONES", "REPAIR", "VEYRA NOODLE CO", "LUMEN LOUNGE",
            "CROWN CINEMA", "IRON KOI", "NEON MILE", "BLUE HERON", "MOON GARDEN", "STARLIGHT", "LOTUS", "CHROME",
        };
        static readonly string[] Colours = { "M_Neon_Cyan", "M_Neon_Magenta", "M_Neon_Amber", "M_Neon_SignalRed", "M_Neon_White" };

        sealed class MB { public List<Vector3> V = new List<Vector3>(); public List<Vector2> UV = new List<Vector2>(); public List<int> T = new List<int>(); }

        [MenuItem("Veyra/World/Build Neon Signs")]
        public static void Menu() => Debug.Log(Build());

        public static string Build()
        {
            WorldData.EnsureLoaded();
            var world = EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == "World");
            if (world == null) return "FAIL: no World root";
            var widths = MakeAtlas();
            var mats = Materials();
            var back = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_Plastic_Dark.mat");
            var led = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/M_LED_Screen.mat");
            var old = world.transform.Find("Signs");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("Signs") { isStatic = true }.transform;
            root.SetParent(world.transform, false);
            Directory.CreateDirectory(MeshDir);

            var records = PlacementReader.Buildings(VeyraDataValidator.LoadText("world_building_placements.csv"), "world_building_placements.csv");
            var chunks = new Dictionary<string, Dictionary<Material, MB>>();
            MB Get(string chunk, Material m)
            {
                if (!chunks.TryGetValue(chunk, out var d)) chunks[chunk] = d = new Dictionary<Material, MB>();
                if (!d.TryGetValue(m, out var mb)) d[m] = mb = new MB();
                return mb;
            }
            int store = 0, blade = 0, roof = 0, pools = 0;
            // iteration 12: coloured light pools on the wet ground under the signs (night only, additive)
            var glows = GlowMaterials(mats);
            // iteration 13: pools sit on the RENDERED pavement/asphalt (the collision-only drive surface lies up to 0.6 m
            // lower under the sidewalks): world-space triangles of the enabled sidewalk/asphalt/kerb/paving renderers in a
            // 10 m grid; the highest triangle under a point wins
            var grid = new Dictionary<long, List<Vector3>>();
            long Key(float x, float z) => ((long)Mathf.FloorToInt(x / 10f) << 32) ^ (uint)Mathf.FloorToInt(z / 10f);
            int surfMeshes = 0;
            foreach (var mf in world.GetComponentsInChildren<MeshFilter>(false))
            {
                if (mf.sharedMesh == null || mf.name.EndsWith("_LOD1") || mf.name.EndsWith("_LOD2")) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled || mr.bounds.size.y > 60f) continue;
                var sm = mr.sharedMaterials;
                var mesh = mf.sharedMesh;
                var vv = mesh.vertices; var l2w = mf.transform.localToWorldMatrix;
                bool any = false;
                for (int si = 0; si < mesh.subMeshCount && si < sm.Length; si++)
                {
                    var m = sm[si];
                    if (m == null || !(m.name.StartsWith("M_Sidewalk") || m.name.StartsWith("M_Asphalt") || m.name.StartsWith("M_Kerb") || m.name.Contains("Paving") || m.name.Contains("Plaza"))) continue;
                    any = true;
                    var tt = mesh.GetTriangles(si);
                    for (int i = 0; i < tt.Length; i += 3)
                    {
                        var a = l2w.MultiplyPoint3x4(vv[tt[i]]); var b2 = l2w.MultiplyPoint3x4(vv[tt[i + 1]]); var c2 = l2w.MultiplyPoint3x4(vv[tt[i + 2]]);
                        float x0 = Mathf.Min(a.x, Mathf.Min(b2.x, c2.x)), x1 = Mathf.Max(a.x, Mathf.Max(b2.x, c2.x));
                        float z0 = Mathf.Min(a.z, Mathf.Min(b2.z, c2.z)), z1 = Mathf.Max(a.z, Mathf.Max(b2.z, c2.z));
                        if (x1 - x0 > 200f || z1 - z0 > 200f) continue;
                        for (float gx = Mathf.Floor(x0 / 10f) * 10f; gx <= x1; gx += 10f)
                            for (float gz = Mathf.Floor(z0 / 10f) * 10f; gz <= z1; gz += 10f)
                            {
                                long k = Key(gx + 0.01f, gz + 0.01f);
                                if (!grid.TryGetValue(k, out var lst)) grid[k] = lst = new List<Vector3>();
                                lst.Add(a); lst.Add(b2); lst.Add(c2);
                            }
                    }
                }
                if (any) surfMeshes++;
            }
            Debug.Log($"[neon] surface sampling: {surfMeshes} meshes, {grid.Count} grid cells");
            float SurfaceY(Vector3 at, float fallbackY)
            {
                float best = float.NegativeInfinity;
                if (grid.TryGetValue(Key(at.x, at.z), out var lst))
                    for (int i = 0; i < lst.Count; i += 3)
                    {
                        var a = lst[i]; var b2 = lst[i + 1]; var c2 = lst[i + 2];
                        float d = (b2.z - c2.z) * (a.x - c2.x) + (c2.x - b2.x) * (a.z - c2.z);
                        if (Mathf.Abs(d) < 1e-6f) continue;
                        float w0 = ((b2.z - c2.z) * (at.x - c2.x) + (c2.x - b2.x) * (at.z - c2.z)) / d;
                        float w1 = ((c2.z - a.z) * (at.x - c2.x) + (a.x - c2.x) * (at.z - c2.z)) / d;
                        float w2 = 1f - w0 - w1;
                        if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
                        float y = w0 * a.y + w1 * b2.y + w2 * c2.y;
                        if (y > best && y < at.y + 25f) best = y;
                    }
                return float.IsNegativeInfinity(best) ? WorldData.GroundHeight(at, fallbackY) : best;
            }
            void Pool(Vector3 at, Vector3 fwd, Material signMat, float w, float l, float fallbackY)
            {
                int gi = System.Array.IndexOf(mats, signMat);
                if (gi < 0 || gi >= glows.Length || glows[gi] == null) return;
                fwd.y = 0f; fwd.Normalize();
                // highest rendered surface over the pool (facade foot, middle, outer edge)
                float gy = Mathf.Max(SurfaceY(at + fwd * 0.5f, fallbackY), Mathf.Max(SurfaceY(at + fwd * l * 0.5f, fallbackY), SurfaceY(at + fwd * (l - 0.3f), fallbackY)));
                var mb = Get(ChunkOf(at), glows[gi]);
                var o = new Vector3(at.x, gy + 0.03f, at.z);
                var rr = Vector3.Cross(Vector3.up, fwd).normalized * w * 0.5f;
                int vi = mb.V.Count;
                mb.V.Add(o - rr); mb.V.Add(o + rr); mb.V.Add(o + rr + fwd * l); mb.V.Add(o - rr + fwd * l);
                mb.UV.Add(new Vector2(0f, 1f)); mb.UV.Add(new Vector2(1f, 1f)); mb.UV.Add(new Vector2(1f, 0f)); mb.UV.Add(new Vector2(0f, 0f));
                Tri(mb, vi, Vector3.up);
                pools++;
            }
            foreach (var b in records)
            {
                bool d6 = b.DistrictId == "D6", d1 = b.DistrictId == "D1";
                if (!d6 && !d1) continue;
                if (b.StyleKit == "Landmark" || b.HeightM < 6f) continue;
                var c = new Vector3(b.UnityX, b.UnityYBase, b.UnityZ);
                if (!WorldData.NearestRoad(c, Mathf.Max(b.FootprintWM, b.FootprintLM) * 0.5f + 25f, out var hit)) continue;
                string cls = WorldData.Roads[hit.Road].Data.cls;
                if (cls != "CO" && cls != "AR" && cls != "SV") continue;
                var rng = new System.Random(b.Id.GetHashCode());
                if (d1 && rng.NextDouble() < 0.45) continue;   // restraint downtown
                // facade facing the road: one of the four local axes
                var q = Quaternion.Euler(0f, b.UnityYawDeg, 0f);
                var toRoad = hit.Point - c; toRoad.y = 0f;
                Vector3 n = Vector3.forward; float half = b.FootprintLM * 0.5f, along = b.FootprintWM;
                float best = -2f;
                foreach (var (dir, hd, wid) in new[] { (Vector3.forward, b.FootprintLM * 0.5f, b.FootprintWM), (Vector3.back, b.FootprintLM * 0.5f, b.FootprintWM), (Vector3.right, b.FootprintWM * 0.5f, b.FootprintLM), (Vector3.left, b.FootprintWM * 0.5f, b.FootprintLM) })
                {
                    float d = Vector3.Dot((q * dir).normalized, toRoad.normalized);
                    if (d > best) { best = d; n = q * dir; half = hd; along = wid; }
                }
                // a reader facing the sign looks along -n: their right is Cross(n, up) (iteration 1: "JAZZ" read mirrored)
                var right = Vector3.Cross(n, Vector3.up).normalized;
                var face = c + n * (half + 0.12f);
                string chunk = ChunkOf(face);
                int word = rng.Next(Words.Length);
                var mat = mats[rng.Next(mats.Length)];
                // storefront sign: 0.9 m tall, as wide as the word allows (max 70 % of the facade), 3.9 m up
                float w = Mathf.Min(along * 0.7f, widths[word] * 0.9f / RowH * 1.15f);
                if (w > 1.5f)
                {
                    var o = face + Vector3.up * 3.9f;
                    Panel(Get(chunk, back), o - n * 0.06f, right, n, w + 0.3f, 1.2f, Vector2.zero, Vector2.one);
                    Panel(Get(chunk, mat), o, right, n, w, 0.9f, RowUV(word, widths[word], false), RowUV(word, widths[word], true));
                    Pool(face + n * 0.2f, n, mat, w + 3f, 5.5f, b.UnityYBase);
                    store++;
                }
                // blade sign on the corner (vertical text)
                if (b.HeightM > 14f && (d6 || rng.NextDouble() < 0.33))
                {
                    int w2 = rng.Next(Words.Length);
                    float len = Mathf.Clamp(widths[w2] * 0.75f / RowH, 3f, Mathf.Min(10f, b.HeightM - 6f));
                    var corner = face + right * (along * 0.5f - 0.6f) + n * 0.55f;
                    var m2 = mats[(System.Array.IndexOf(mats, mat) + 1 + rng.Next(mats.Length - 1)) % mats.Length];
                    // a vertical panel perpendicular to the facade, text running top to bottom on both sides
                    var bo = corner + Vector3.up * (5.2f + len * 0.5f);
                    Box(Get(chunk, back), bo, n, Vector3.up, right, 1.0f, len + 0.3f, 0.18f);
                    var uv0 = RowUV(w2, widths[w2], false); var uv1 = RowUV(w2, widths[w2], true);
                    VPanel(Get(chunk, m2), bo + right * 0.1f, n, right, 0.75f, len, uv0, uv1);
                    VPanel(Get(chunk, m2), bo - right * 0.1f, -n, -right, 0.75f, len, uv0, uv1);
                    Pool(corner + n * 0.2f, n, m2, 4f, 6f, b.UnityYBase);
                    blade++;
                }
                // rooftop LED billboard
                if (d6 && b.HeightM >= 20f && b.HeightM <= 80f && led != null && rng.NextDouble() < 0.6)
                {
                    float bw = Mathf.Min(along * 0.6f, 14f), bh = bw * 0.45f;
                    var bo = face - n * 2.5f + Vector3.up * (b.HeightM + 1.2f + bh * 0.5f);
                    Box(Get(chunk, back), bo - n * 0.2f, right, Vector3.up, n, bw + 0.4f, bh + 0.4f, 0.3f);
                    Panel(Get(chunk, led), bo - Vector3.up * bh * 0.5f, right, n, bw, bh, Vector2.zero, Vector2.one);
                    // legs down to the roof
                    for (int s = -1; s <= 1; s += 2)
                        Box(Get(chunk, back), bo + right * s * bw * 0.4f - Vector3.up * (bh * 0.5f + 0.6f) - n * 0.3f, right, Vector3.up, n, 0.2f, 1.2f, 0.2f);
                    roof++;
                }
            }
            // iteration 4: overhead cables across Neon Mile streets (sagging, 7-12 m up) with small hanging neon banners
            int cables = 0, banners = 0;
            var d6poly = WorldData.Districts.districts.FirstOrDefault(d => d.id == "D6")?.polygon;
            if (d6poly != null && back != null)
                for (int r = 0; r < WorldData.Roads.Count; r++)
                {
                    var road = WorldData.Roads[r];
                    if (road.Data.cls != "CO" && road.Data.cls != "AR" && road.Data.cls != "SV") continue;
                    var rr = new System.Random(r * 7919);
                    for (float s = 15f; s < road.Path.Length - 15f; s += 18f + (float)rr.NextDouble() * 14f)
                    {
                        var p = road.Path.PositionAt(s);
                        if (!WorldData.PointInPolygon(d6poly, p.x, p.z)) continue;
                        var t = road.Path.TangentAt(s); t.y = 0f; t.Normalize();
                        var side = new Vector3(t.z, 0f, -t.x);
                        float half = road.HalfWidth + 3.4f;   // over the pavement, onto the facades
                        int n = 1 + rr.Next(3);
                        for (int k = 0; k < n; k++)
                        {
                            float y0 = 7f + (float)rr.NextDouble() * 5f, y1 = y0 + ((float)rr.NextDouble() - 0.5f) * 2f;
                            var a = p - side * half + t * (k * 0.8f) + Vector3.up * y0;
                            var b = p + side * half + t * (k * 0.8f + ((float)rr.NextDouble() - 0.5f) * 3f) + Vector3.up * y1;
                            float sag = 0.6f + (float)rr.NextDouble() * 0.9f;
                            Vector3 prev = a;
                            for (int q = 1; q <= 8; q++)
                            {
                                float f = q / 8f;
                                var c = Vector3.Lerp(a, b, f) - Vector3.up * sag * 4f * f * (1f - f);
                                Box(Get(ChunkOf(c), back), (prev + c) * 0.5f, (c - prev), Vector3.up, Vector3.Cross(c - prev, Vector3.up), (c - prev).magnitude, 0.035f, 0.035f);
                                prev = c;
                            }
                            cables++;
                            if (rr.NextDouble() < 0.35)
                            {
                                int w2 = rr.Next(Words.Length);
                                var mid = Vector3.Lerp(a, b, 0.5f) - Vector3.up * (sag + 0.55f);
                                float bw = Mathf.Clamp(widths[w2] * 0.5f / RowH, 1.5f, 4f);
                                var m2 = mats[rr.Next(mats.Length)];
                                Box(Get(ChunkOf(mid), back), mid + Vector3.up * 0.25f, side, Vector3.up, t, bw + 0.2f, 0.7f, 0.08f);
                                Panel(Get(ChunkOf(mid), m2), mid + t * 0.05f, Vector3.Cross(t, Vector3.up).normalized, t, bw, 0.5f, RowUV(w2, widths[w2], false), RowUV(w2, widths[w2], true));
                                Panel(Get(ChunkOf(mid), m2), mid - t * 0.05f, Vector3.Cross(-t, Vector3.up).normalized, -t, bw, 0.5f, RowUV(w2, widths[w2], false), RowUV(w2, widths[w2], true));
                                Pool(new Vector3(mid.x, p.y, mid.z) + t * 2.5f, t, m2, bw + 2.5f, 5f, p.y);
                                banners++;
                            }
                        }
                    }
                }
            var keep = new HashSet<string>();
            int renderers = 0;
            foreach (var kv in chunks)
            {
                var go = new GameObject(kv.Key) { isStatic = true };
                go.transform.SetParent(root, false);
                foreach (var mm in kv.Value)
                {
                    string name = $"SG_{kv.Key}_{mm.Key.name}";
                    var mesh = Save(name, mm.Value);
                    keep.Add(name);
                    var r = new GameObject(mm.Key.name) { isStatic = true };
                    r.transform.SetParent(go.transform, false);
                    r.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = r.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mm.Key;
                    mr.shadowCastingMode = mm.Key == back ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    var lg = r.AddComponent<LODGroup>();
                    lg.SetLODs(new[] { new LOD(0.004f, new Renderer[] { mr }) });
                    renderers++;
                }
            }
            foreach (var f in Directory.GetFiles(MeshDir, "*.asset"))
                if (!keep.Contains(Path.GetFileNameWithoutExtension(f))) AssetDatabase.DeleteAsset(f.Replace('\\', '/'));
            AssetDatabase.SaveAssets();
            string r2 = $"Neon signs: {store} storefront, {blade} blade, {roof} rooftop LED, {cables} overhead cables with {banners} hanging banners (D6), {pools} night light pools in {chunks.Count} chunks ({renderers} renderers); {Words.Length} original sign words\n";
            Directory.CreateDirectory("Logs/C");
            File.WriteAllText("Logs/C/neon_signs.txt", r2);
            return r2;
        }

        static string ChunkOf(Vector3 p)
        {
            var w = WorldCoordinates.FromUnity(p);
            return WorldCoordinates.ChunkOf(Mathf.Clamp(w.x, 0f, 7999f), Mathf.Clamp(w.y, 0f, 5999f));
        }

        static Vector2 RowUV(int row, int widthPx, bool max)
        {
            float v0 = 1f - (row + 1) * RowH / (float)(Rows * RowH);
            float v1 = 1f - row * RowH / (float)(Rows * RowH);
            return max ? new Vector2(widthPx / (float)AtlasW, v1) : new Vector2(0f, v0);
        }

        /// <summary>A quad facing <paramref name="n"/>: bottom-centre o, width along r, height up.</summary>
        static void Panel(MB m, Vector3 o, Vector3 r, Vector3 n, float w, float h, Vector2 uv0, Vector2 uv1)
        {
            int i = m.V.Count;
            m.V.Add(o - r * w * 0.5f); m.V.Add(o + r * w * 0.5f); m.V.Add(o + r * w * 0.5f + Vector3.up * h); m.V.Add(o - r * w * 0.5f + Vector3.up * h);
            m.UV.Add(new Vector2(uv0.x, uv0.y)); m.UV.Add(new Vector2(uv1.x, uv0.y)); m.UV.Add(new Vector2(uv1.x, uv1.y)); m.UV.Add(new Vector2(uv0.x, uv1.y));
            Tri(m, i, n);
        }

        /// <summary>A vertical text panel (text runs down): centre o, facing n, width w across (along r), length len.</summary>
        static void VPanel(MB m, Vector3 o, Vector3 n, Vector3 r, float w, float len, Vector2 uv0, Vector2 uv1)
        {
            int i = m.V.Count;
            var side = Vector3.Cross(n, Vector3.up).normalized;   // horizontal, in the panel plane
            m.V.Add(o + side * w * 0.5f + Vector3.up * len * 0.5f); m.V.Add(o + side * w * 0.5f - Vector3.up * len * 0.5f);
            m.V.Add(o - side * w * 0.5f - Vector3.up * len * 0.5f); m.V.Add(o - side * w * 0.5f + Vector3.up * len * 0.5f);
            // text start at the top: u runs down, v across
            m.UV.Add(new Vector2(uv0.x, uv0.y)); m.UV.Add(new Vector2(uv1.x, uv0.y)); m.UV.Add(new Vector2(uv1.x, uv1.y)); m.UV.Add(new Vector2(uv0.x, uv1.y));
            Tri(m, i, n);
        }

        static void Tri(MB m, int i, Vector3 facing)
        {
            var nrm = Vector3.Cross(m.V[i + 1] - m.V[i], m.V[i + 2] - m.V[i]);
            if (Vector3.Dot(nrm, facing) >= 0f) { m.T.Add(i); m.T.Add(i + 1); m.T.Add(i + 2); m.T.Add(i); m.T.Add(i + 2); m.T.Add(i + 3); }
            else { m.T.Add(i); m.T.Add(i + 2); m.T.Add(i + 1); m.T.Add(i); m.T.Add(i + 3); m.T.Add(i + 2); }
        }

        /// <summary>A box: centre c, axes a (x), u (y), f (z), sizes sx, sy, sz.</summary>
        static void Box(MB m, Vector3 c, Vector3 a, Vector3 u, Vector3 f, float sx, float sy, float sz)
        {
            a = a.normalized * sx * 0.5f; u = u.normalized * sy * 0.5f; f = f.normalized * sz * 0.5f;
            void Face(Vector3 n, Vector3 s1, Vector3 s2)
            {
                int i = m.V.Count;
                var o = c + n;
                m.V.Add(o - s1 - s2); m.V.Add(o + s1 - s2); m.V.Add(o + s1 + s2); m.V.Add(o - s1 + s2);
                for (int k = 0; k < 4; k++) m.UV.Add(new Vector2(k == 1 || k == 2 ? 1 : 0, k >= 2 ? 1 : 0));
                Tri(m, i, n);
            }
            Face(f, a, u); Face(-f, a, u); Face(a, f, u); Face(-a, f, u); Face(u, a, f); Face(-u, a, f);
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

        /// <summary>The sign-word atlas (white soft-glow tubes on black); returns each row's text width in pixels.</summary>
        static int[] MakeAtlas()
        {
            int H = Rows * RowH;
            var px = new Color[AtlasW * H];
            var widths = new int[Words.Length];
            for (int r = 0; r < Words.Length; r++)
            {
                string t = Words[r];
                int scale = 3, wpx = PixelFont.Measure(t, scale);
                if (wpx > AtlasW - 8) { scale = 2; wpx = PixelFont.Measure(t, scale); }
                widths[r] = wpx + 8;
                int y0 = H - (r + 1) * RowH;   // texture rows bottom-up; row r occupies [y0, y0 + RowH)
                var mask = new Color32[AtlasW * RowH];
                PixelFont.Draw(mask, AtlasW, RowH, t, 4, (RowH - 7 * scale) / 2, scale, new Color32(255, 255, 255, 255));
                // tube: bright core + soft halo (3 px)
                for (int y = 0; y < RowH; y++)
                    for (int x = 0; x < AtlasW; x++)
                    {
                        float core = mask[y * AtlasW + x].r / 255f, halo = 0f;
                        if (core < 1f)
                            for (int dy = -3; dy <= 3; dy++)
                                for (int dx = -3; dx <= 3; dx++)
                                {
                                    int xx = x + dx, yy = y + dy;
                                    if (xx < 0 || yy < 0 || xx >= AtlasW || yy >= RowH || mask[yy * AtlasW + xx].r == 0) continue;
                                    halo = Mathf.Max(halo, 0.45f * (1f - Mathf.Sqrt(dx * dx + dy * dy) / 4.3f));
                                }
                        float v = Mathf.Max(core, halo);
                        px[(y0 + y) * AtlasW + x] = new Color(v, v, v, 1f);
                    }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(AtlasPath));
            var tex = new Texture2D(AtlasW, H, TextureFormat.RGBA32, false);
            tex.SetPixels(px); tex.Apply();
            File.WriteAllBytes(AtlasPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(AtlasPath) is TextureImporter ti)
            {
                ti.npotScale = TextureImporterNPOTScale.None; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp;
                ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.SaveAndReimport();
            }
            return widths;
        }

        /// <summary>GLSL-style smoothstep(e0, e1, x) (Mathf.SmoothStep interpolates between its first two arguments instead).</summary>
        static float Edge(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }

        /// <summary>
        /// Iteration 12: one light-pool material per sign colour. Shader Veyra/World/GlowPool (additive unlit: a soft radial
        /// blob, brighter towards the facade, times an HDR colour). Registered
        /// with the scene's WorldEnvironment as night-only emissives: black (invisible) by day.
        /// </summary>
        static Material[] GlowMaterials(Material[] signMats)
        {
            const string texPath = "Assets/_Veyra/Art/Textures/Neon/T_Neon_Pool.png";
            Directory.CreateDirectory(Path.GetDirectoryName(texPath));
            if (!File.Exists(texPath))
            {
                const int S = 128;
                var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
                var px = new Color32[S * S];
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        // u across, v from the facade (v = 1) out to the street (v = 0)
                        float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S;
                        // iteration 13: centre 15 % off the facade, soft at both ends (a hard edge showed at the facade line)
                        float d = Mathf.Sqrt(u * u + (0.85f - v) * (0.85f - v) * 1.3f);
                        float a = Mathf.Clamp01(1f - d);
                        a = a * a * (3f - 2f * a);
                        a *= Edge(0f, 0.2f, 1f - Mathf.Abs(u)) * Edge(0f, 0.35f, v) * Edge(1f, 0.9f, v);
                        byte c = (byte)(a * 255f);
                        px[y * S + x] = new Color32(c, c, c, 255);
                    }
                tex.SetPixels32(px);
                File.WriteAllBytes(texPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(texPath);
                var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
                ti.wrapMode = TextureWrapMode.Clamp; ti.sRGBTexture = false;
                ti.SaveAndReimport();
            }
            var blob = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            var env = Object.FindAnyObjectByType<WorldEnvironment>(FindObjectsInactive.Include);
            var outMats = new Material[signMats.Length];
            var entries = new List<WorldEnvironment.EmissiveEntry>();
            // unregister the pool materials while they are edited and saved: the BUG-039 save guard writes the reference
            // state into every registered emissive and clears its dirty flag, so a registered material is never saved
            if (env != null) env.Emissives.RemoveAll(e => e.Material != null && e.Material.name.StartsWith("M_NeonPool_"));
            for (int i = 0; i < signMats.Length; i++)
            {
                var sm = signMats[i];
                string path = $"{MatDir}/{sm.name.Replace("M_NeonSign_", "M_NeonPool_")}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                var poolShader = Shader.Find("Veyra/World/GlowPool");   // Editor code (iteration 13: URP Lit transparent emission rendered black)
                if (poolShader == null) poolShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Veyra/Art/Shaders/VC_GlowPool.shader");
                if (i == 0) Debug.Log($"[neon] pool shader {(poolShader != null ? poolShader.name + " supported " + poolShader.isSupported : "MISSING")}");
                if (m == null) { m = new Material(poolShader) { name = Path.GetFileNameWithoutExtension(path) }; AssetDatabase.CreateAsset(m, path); }
                else if (m.shader != poolShader) { m.shader = poolShader; m.shaderKeywords = new string[0]; }
                // the sign shader (VC_Neon) glows with _NeonColor x _NeonIntensity; the pool takes half of that
                var col = sm.HasProperty("_NeonColor") ? sm.GetColor("_NeonColor") : sm.HasProperty("_EmissionColor") ? sm.GetColor("_EmissionColor") : Color.white;
                float inten = sm.HasProperty("_NeonIntensity") ? sm.GetFloat("_NeonIntensity") : 2f;
                float mx = Mathf.Max(0.001f, Mathf.Max(col.r, Mathf.Max(col.g, col.b)));
                var emis = new Color(col.r / mx, col.g / mx, col.b / mx) * Mathf.Clamp(inten * 0.3f, 0.8f, 3f);   // iteration 13: 0.5 read as paint
                m.SetTexture("_EmissionMap", blob);
                m.SetColor("_EmissionColor", emis);
                m.renderQueue = -1;
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
                // write now: WorldEnvironment's edit-mode preview clears the dirty flag of every registered emissive
                // material (BUG-039), so a later SaveAssets would drop these edits
                AssetDatabase.SaveAssetIfDirty(m);
                outMats[i] = m;
                entries.Add(new WorldEnvironment.EmissiveEntry { Material = m, BaseEmission = emis, NightOnly = 1f });
            }
            AssetDatabase.SaveAssets();
            if (env != null) { env.Emissives.AddRange(entries); EditorUtility.SetDirty(env); }
            return outMats;
        }

        /// <summary>Sign variants of the palette neon materials: same shader and colour, the text atlas as the pattern.</summary>
        static Material[] Materials()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            var list = new List<Material>();
            foreach (var c in Colours)
            {
                var src = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{c}.mat");
                if (src == null) continue;
                string path = $"{MatDir}/{c.Replace("M_Neon_", "M_NeonSign_")}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(src) { name = Path.GetFileNameWithoutExtension(path) }; AssetDatabase.CreateAsset(m, path); }
                else m.CopyPropertiesFromMaterial(src);
                m.SetTexture("_EmissionMap", atlas);
                if (m.HasProperty("_TubeFalloff")) m.SetFloat("_TubeFalloff", 0.3f);
                if (m.HasProperty("_FlickerChance")) m.SetFloat("_FlickerChance", 0.08f);
                EditorUtility.SetDirty(m);
                list.Add(m);
            }
            return list.ToArray();
        }
    }
}
