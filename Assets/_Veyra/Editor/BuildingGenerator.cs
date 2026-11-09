using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.Gameplay;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Generates every building, Ironport container stack and quay crane from the data supplement CSVs
    /// (Assets/_Veyra/Data). Positions, footprints, heights and ground levels come only from the data.
    /// Geometry is grouped per 250 m chunk (Chunk_&lt;Cell&gt;_&lt;i&gt;&lt;j&gt;) and material; every building and stack
    /// gets a BoxCollider (UBX). Style kits choose facade, roof and shape; Neon Mile and Downtown get neon.
    /// </summary>
    public static class BuildingGenerator
    {
        const string DataDir = "Assets/_Veyra/Data";

        sealed class MB
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<int> T = new List<int>();
        }

        sealed class Ctx
        {
            public readonly Dictionary<(string chunk, string mat), MB> Meshes = new Dictionary<(string, string), MB>();
            public readonly Dictionary<string, List<(Vector3 pos, Quaternion rot, Vector3 size, string name)>> Colliders = new Dictionary<string, List<(Vector3, Quaternion, Vector3, string)>>();
            public int Tris;
            public MB Get(string chunk, string mat)
            {
                if (!Meshes.TryGetValue((chunk, mat), out var mb)) Meshes[(chunk, mat)] = mb = new MB();
                return mb;
            }
            public void Collider(string chunk, Vector3 pos, Quaternion rot, Vector3 size, string name)
            {
                if (!Colliders.TryGetValue(chunk, out var l)) Colliders[chunk] = l = new List<(Vector3, Quaternion, Vector3, string)>();
                l.Add((pos, rot, size, name));
            }
        }

        // ------------------------------------------------------------------ primitive emitters (world space)
        /// <summary>Quad seen from outside: bl, br, tr, tl (clockwise front face in Unity).</summary>
        static void Quad(MB m, Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, Vector3 n, Vector2 uvbl, Vector2 uvtr)
        {
            int i = m.V.Count;
            m.V.Add(bl); m.V.Add(br); m.V.Add(tr); m.V.Add(tl);
            for (int k = 0; k < 4; k++) m.N.Add(n);
            m.UV.Add(uvbl); m.UV.Add(new Vector2(uvtr.x, uvbl.y)); m.UV.Add(uvtr); m.UV.Add(new Vector2(uvbl.x, uvtr.y));
            m.T.Add(i); m.T.Add(i + 3); m.T.Add(i + 2);
            m.T.Add(i); m.T.Add(i + 2); m.T.Add(i + 1);
        }

        static void Tri(MB m, Vector3 a, Vector3 b, Vector3 c, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            int i = m.V.Count;
            m.V.Add(a); m.V.Add(b); m.V.Add(c);
            m.N.Add(n); m.N.Add(n); m.N.Add(n);
            m.UV.Add(ua); m.UV.Add(ub); m.UV.Add(uc);
            m.T.Add(i); m.T.Add(i + 1); m.T.Add(i + 2);
        }

        struct Frame
        {
            public Vector3 O;
            public Quaternion Q;
            public Vector3 P(float x, float y, float z) => O + Q * new Vector3(x, y, z);
            public Vector3 D(Vector3 d) => Q * d;
        }

        /// <summary>Walls of a local rectangle [x0,x1]x[z0,z1] from y0 to y1; uScale/vScale metres per texture tile, v measured from vBase.</summary>
        static void Walls(MB m, Frame f, float x0, float x1, float z0, float z1, float y0, float y1, float uScale, float vScale, float vBase, float outset = 0f)
        {
            x0 -= outset; z0 -= outset; x1 += outset; z1 += outset;
            var c = new[] { new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1) };
            float u = 0f;
            for (int k = 0; k < 4; k++)
            {
                var a = c[k];
                var b = c[(k + 1) % 4];
                var dir = new Vector3(b.x - a.x, 0f, b.y - a.y);
                float len = dir.magnitude;
                var n = f.D(Vector3.Cross(Vector3.up, dir).normalized);
                Quad(m, f.P(a.x, y0, a.y), f.P(b.x, y0, b.y), f.P(b.x, y1, b.y), f.P(a.x, y1, a.y), n,
                     new Vector2(u / uScale, (y0 - vBase) / vScale), new Vector2((u + len) / uScale, (y1 - vBase) / vScale));
                u += len;
            }
        }

        static void Top(MB m, Frame f, float x0, float x1, float z0, float z1, float y, float uvScale)
        {
            var p0 = f.P(x0, y, z0); var p1 = f.P(x1, y, z0); var p2 = f.P(x1, y, z1); var p3 = f.P(x0, y, z1);
            int i = m.V.Count;
            m.V.Add(p0); m.V.Add(p1); m.V.Add(p2); m.V.Add(p3);
            for (int k = 0; k < 4; k++) m.N.Add(Vector3.up);
            m.UV.Add(new Vector2(p0.x, p0.z) / uvScale); m.UV.Add(new Vector2(p1.x, p1.z) / uvScale); m.UV.Add(new Vector2(p2.x, p2.z) / uvScale); m.UV.Add(new Vector2(p3.x, p3.z) / uvScale);
            m.T.Add(i); m.T.Add(i + 3); m.T.Add(i + 2);
            m.T.Add(i); m.T.Add(i + 2); m.T.Add(i + 1);
        }

        static void Box(MB walls, MB top, Frame f, float x0, float x1, float z0, float z1, float y0, float y1, float uScale, float vScale, float vBase)
        {
            Walls(walls, f, x0, x1, z0, z1, y0, y1, uScale, vScale, vBase);
            Top(top ?? walls, f, x0, x1, z0, z1, y1, 8f);
        }

        static void Cylinder(MB walls, MB top, Vector3 c, float r, float y0, float y1, int seg, float uScale, float vScale, float topRise = 0f)
        {
            float circ = 2f * Mathf.PI * r;
            for (int k = 0; k < seg; k++)
            {
                float a0 = k / (float)seg * Mathf.PI * 2f, a1 = (k + 1) / (float)seg * Mathf.PI * 2f;
                var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)); var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                var n = ((d0 + d1) * 0.5f).normalized;
                // viewed from outside, going counter-clockwise in xz puts d1 on the left
                Quad(walls, c + d1 * r + Vector3.up * y0, c + d0 * r + Vector3.up * y0, c + d0 * r + Vector3.up * y1, c + d1 * r + Vector3.up * y1, n,
                     new Vector2((k + 1) / (float)seg * circ / uScale, y0 / vScale), new Vector2(k / (float)seg * circ / uScale, y1 / vScale));
                if (top != null)
                {
                    var apex = c + Vector3.up * (y1 + topRise);
                    Tri(top, c + d0 * r + Vector3.up * y1, apex, c + d1 * r + Vector3.up * y1, Vector3.up,
                        new Vector2(d0.x, d0.z) * r / 8f, Vector2.zero, new Vector2(d1.x, d1.z) * r / 8f);
                }
            }
        }

        /// <summary>Gable roof along local X over [x0,x1]x[z0,z1], eaves at ye, ridge at yr, with overhang o.</summary>
        static void Gable(MB roof, MB wall, Frame f, float x0, float x1, float z0, float z1, float ye, float yr, float o, float uScale, float vScale, float vBase)
        {
            float zm = (z0 + z1) * 0.5f;
            float rx0 = x0 - o, rx1 = x1 + o, rz0 = z0 - o, rz1 = z1 + o;
            float drop = o * (yr - ye) / Mathf.Max(0.1f, zm - z0);
            float slopeLen = Mathf.Sqrt((zm - rz0) * (zm - rz0) + (yr - ye + drop) * (yr - ye + drop));
            var nS = f.D(new Vector3(0f, zm - z0, -(yr - ye)).normalized);
            var nN = f.D(new Vector3(0f, zm - z0, (yr - ye)).normalized);
            Quad(roof, f.P(rx0, ye - drop, rz0), f.P(rx1, ye - drop, rz0), f.P(rx1, yr, zm), f.P(rx0, yr, zm), nS, new Vector2(0f, 0f), new Vector2((rx1 - rx0) / 4f, slopeLen / 4f));
            Quad(roof, f.P(rx1, ye - drop, rz1), f.P(rx0, ye - drop, rz1), f.P(rx0, yr, zm), f.P(rx1, yr, zm), nN, new Vector2(0f, 0f), new Vector2((rx1 - rx0) / 4f, slopeLen / 4f));
            // gable ends (walls)
            var nW = f.D(Vector3.left); var nE = f.D(Vector3.right);
            float vb = (ye - vBase) / vScale, vt = (yr - vBase) / vScale;
            Tri(wall, f.P(x0, ye, z1), f.P(x0, yr, zm), f.P(x0, ye, z0), nW, new Vector2(0f, vb), new Vector2((z1 - zm) / uScale, vt), new Vector2((z1 - z0) / uScale, vb));
            Tri(wall, f.P(x1, ye, z0), f.P(x1, yr, zm), f.P(x1, ye, z1), nE, new Vector2(0f, vb), new Vector2((zm - z0) / uScale, vt), new Vector2((z1 - z0) / uScale, vb));
        }

        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        static List<Dictionary<string, string>> ReadCsv(string file)
        {
            var rows = new List<Dictionary<string, string>>();
            var lines = File.ReadAllLines(file);
            var head = lines[0].Split(',');
            for (int i = 1; i < lines.Length; i++)
            {
                var f = SplitCsv(lines[i]);
                if (f.Count < head.Length) continue;
                var d = new Dictionary<string, string>();
                for (int k = 0; k < head.Length; k++) d[head[k]] = f[k];
                rows.Add(d);
            }
            return rows;
        }

        static List<string> SplitCsv(string line)
        {
            var res = new List<string>();
            bool q = false;
            var cur = new System.Text.StringBuilder();
            foreach (char ch in line)
            {
                if (ch == '"') { q = !q; continue; }
                if (ch == ',' && !q) { res.Add(cur.ToString()); cur.Clear(); continue; }
                cur.Append(ch);
            }
            res.Add(cur.ToString());
            return res;
        }

        public static string ChunkOf(float unityX, float unityZ)
        {
            float x = unityX + 4000f, y = unityZ + 3000f;
            int col = Mathf.Clamp(Mathf.FloorToInt(x / 1000f), 0, 7);
            int rowFromSouth = Mathf.Clamp(Mathf.FloorToInt(y / 1000f), 0, 5);
            int row = 6 - rowFromSouth;
            int i = Mathf.Clamp(Mathf.FloorToInt((x - col * 1000f) / 250f), 0, 3);
            int j = Mathf.Clamp(Mathf.FloorToInt((y - rowFromSouth * 1000f) / 250f), 0, 3);
            return $"Chunk_{"ABCDEFGH"[col]}{row}_{i}{j}";
        }

        // ------------------------------------------------------------------ style kits
        static string FacadeFor(Dictionary<string, string> b)
        {
            string kit = b["style_kit"], typ = b["typology"], name = b["name"];
            if (kit.StartsWith("GlassModern"))
                switch (typ) { case "T2": case "T3": return "stone_deco"; case "T5": return "facade_light"; case "T6": return "glass_bronze"; case "T7": return "facade_brick"; default: return "glass_blue"; }
            switch (kit)
            {
                case "Landmark": return name.Contains("Spire") ? "glass_bronze" : b["shape"] == "stepped" ? "stone_deco" : "glass_blue";
                case "ResidentialMidrise": return "facade_light";
                case "CoastalStucco": return "facade_stucco";
                case "IndustrialTank": case "IndustrialRefinery": return "tank_white";
                case "BrickIndustrial": case "MarketTownBrick": return "facade_brick";
                case "ModernLowrise": case "SuburbanRetail": case "Roadside": return "facade_mid";
                case "Industrial": case "Aviation": return "metal_clad";
                case "Alpine": case "Rural": return "wood_wall";
                default: return "house_wall";
            }
        }

        static readonly HashSet<string> ShopKinds = new HashSet<string> { "office", "apartment", "midrise", "retail", "hotel", "theater", "showroom", "lowrise", "town", "marina_building", "beach_club", "parking_structure" };
        static readonly string[] Neons = { "neon_cyan", "neon_magenta", "neon_amber" };

        // ------------------------------------------------------------------ main
        public static string Build(Transform worldRoot, Dictionary<string, Material> mats, GameDatabase db, string gen)
        {
            var ctx = new Ctx();
            int buildings = 0, stacks = 0, boxes = 0;
            foreach (var b in ReadCsv(Path.Combine(DataDir, "world_building_placements.csv")))
            {
                EmitBuilding(ctx, b);
                buildings++;
            }
            foreach (var c in ReadCsv(Path.Combine(DataDir, "container_placements.csv")))
            {
                int tiers = int.Parse(c["tiers"]);
                float L = F(c["length_m"]), W = F(c["width_m"]), H = 2.59f;
                var o = new Vector3(F(c["unity_x"]), F(c["unity_y_base"]), F(c["unity_z"]));
                string mat = "container_" + c["color_hex"].TrimStart('#').ToLowerInvariant();
                if (!mats.ContainsKey(mat)) mat = "container_grey";
                string chunk = c["chunk"];
                var f = new Frame { O = o, Q = Quaternion.identity };
                var mb = ctx.Get(chunk, mat);
                for (int t = 0; t < tiers; t++)
                {
                    float y0 = t * H + 0.01f, y1 = (t + 1) * H - 0.02f;
                    Box(mb, null, f, -L * 0.5f, L * 0.5f, -W * 0.5f, W * 0.5f, y0, y1, 6f, 6f, 0f);
                    boxes++;
                }
                ctx.Collider(chunk, o + Vector3.up * (tiers * H * 0.5f), Quaternion.identity, new Vector3(L, tiers * H, W), "UBX_" + c["id"]);
                stacks++;
            }
            int cranes = 0;
            foreach (var c in ReadCsv(Path.Combine(DataDir, "crane_placements.csv")))
            {
                EmitCrane(ctx, new Vector3(F(c["unity_x"]), F(c["unity_y_base"]), F(c["unity_z"])), F(c["height_m"]), c["id"]);
                cranes++;
            }
            return Write(ctx, worldRoot, mats, db, gen) + $"; {buildings} buildings, {stacks} container stacks ({boxes} boxes), {cranes} cranes";
        }

        static void EmitBuilding(Ctx ctx, Dictionary<string, string> b)
        {
            float w = F(b["footprint_w_m"]), l = F(b["footprint_l_m"]), h = F(b["height_m"]);
            float yaw = F(b["unity_yaw_deg"]);
            float seed = F(b["seed"]);
            var o = new Vector3(F(b["unity_x"]), F(b["unity_y_base"]), F(b["unity_z"]));
            string chunk = b["chunk"], shape = b["shape"], kind = b["kind"], district = b["district_id"];
            string facade = FacadeFor(b);
            bool pitched = shape == "house";
            string roofMat = pitched ? (b["style_kit"] == "Alpine" ? "roof_metal" : "roof_tile") : (facade == "metal_clad" || facade == "tank_white" ? "roof_metal" : "roof_dark");
            // keep the ridge along the longer side (rotation keeps handedness)
            if (pitched && l > w) { yaw += 90f; (w, l) = (l, w); }
            var f = new Frame { O = o, Q = Quaternion.Euler(0f, yaw, 0f) };
            float hx = w * 0.5f, hz = l * 0.5f;
            float below = -0.6f; // sink into the ground slightly; base is the lowest footprint corner
            var wall = ctx.Get(chunk, facade);
            var roof = ctx.Get(chunk, roofMat);
            const float U = 12f, V = 14f;

            switch (shape)
            {
                case "house":
                {
                    float eaves = Mathf.Max(3f, h * 0.62f);
                    Walls(wall, f, -hx, hx, -hz, hz, below, eaves, U, V, 0f);
                    Gable(roof, wall, f, -hx, hx, -hz, hz, eaves, h, 0.45f, U, V, 0f);
                    break;
                }
                case "cylinder":
                {
                    float r = Mathf.Min(w, l) * 0.5f;
                    Cylinder(wall, roof, o, r, below, h, 20, 6f, 6f, kind == "silo" ? r * 0.6f : r * 0.12f);
                    break;
                }
                case "hangar":
                {
                    float wallH = h * 0.45f;
                    Walls(wall, f, -hx, hx, -hz, hz, below, wallH, 6f, 6f, 0f);
                    // barrel vault across Z, running along X
                    int seg = 12;
                    float rise = h - wallH;
                    for (int k = 0; k < seg; k++)
                    {
                        float a0 = k / (float)seg * Mathf.PI, a1 = (k + 1) / (float)seg * Mathf.PI;
                        Vector3 P0 = new Vector3(0f, wallH + Mathf.Sin(a0) * rise, -hz * Mathf.Cos(a0)), P1 = new Vector3(0f, wallH + Mathf.Sin(a1) * rise, -hz * Mathf.Cos(a1));
                        var n = f.D(new Vector3(0f, Mathf.Sin((a0 + a1) * 0.5f), -Mathf.Cos((a0 + a1) * 0.5f)));
                        Quad(roof, f.P(-hx, P0.y, P0.z), f.P(hx, P0.y, P0.z), f.P(hx, P1.y, P1.z), f.P(-hx, P1.y, P1.z), n,
                             new Vector2(0f, k * 2f), new Vector2(w / 6f, (k + 1) * 2f));
                        // end walls (fans)
                        Tri(wall, f.P(-hx, wallH, 0f), f.P(-hx, P1.y, P1.z), f.P(-hx, P0.y, P0.z), f.D(Vector3.left), Vector2.zero, Vector2.one, Vector2.right);
                        Tri(wall, f.P(hx, wallH, 0f), f.P(hx, P0.y, P0.z), f.P(hx, P1.y, P1.z), f.D(Vector3.right), Vector2.zero, Vector2.right, Vector2.one);
                    }
                    break;
                }
                case "mast":
                {
                    Cylinder(ctx.Get(chunk, "steel"), null, o, 0.9f, below, h, 8, 6f, 6f);
                    var lamp = new Frame { O = o + Vector3.up * h, Q = Quaternion.identity };
                    Box(ctx.Get(chunk, "marker_red"), null, lamp, -0.6f, 0.6f, -0.6f, 0.6f, 0f, 1f, 1f, 1f, 0f);
                    break;
                }
                case "stepped":
                {
                    float[] ys = { 0f, 0.55f, 0.82f, 1f };
                    float[] sc = { 1f, 0.78f, 0.56f };
                    for (int t = 0; t < 3; t++)
                    {
                        float y0 = t == 0 ? below : ys[t] * h, y1 = ys[t + 1] * h;
                        Box(wall, roof, f, -hx * sc[t], hx * sc[t], -hz * sc[t], hz * sc[t], y0, y1, U, V, 0f);
                    }
                    Crown(ctx, chunk, f, hx * 0.3f, hz * 0.3f, h, seed);
                    break;
                }
                default: // box and tower
                {
                    bool shops = h > 9f && ShopKinds.Contains(kind) && (district == "D1" || district == "D6" || district == "D2" || district == "D8" || district == "D5");
                    float podium = shops ? 4.5f : 0f;
                    if (shops) Walls(ctx.Get(chunk, "storefront"), f, -hx, hx, -hz, hz, below, podium, 12f, 4.5f, 0f);
                    Box(wall, roof, f, -hx, hx, -hz, hz, shops ? podium : below, h, U, V, 0f);
                    if (shape == "tower" || h > 120f) Crown(ctx, chunk, f, hx * 0.45f, hz * 0.45f, h, seed);
                    else if (h > 10f && w > 12f && l > 12f) RoofKit(ctx, chunk, f, hx, hz, h, seed);
                    bool neon = district == "D6" || (district == "D1" && (kind == "office" || kind == "midrise" || kind == "apartment" || kind == "theater" || kind == "hotel") && seed > 0.35f);
                    if (neon && h > 8f) Neon(ctx, chunk, f, hx, hz, h, seed, shops ? podium : 3.5f);
                    break;
                }
            }
            ctx.Collider(chunk, f.P(0f, (h + below) * 0.5f, 0f), f.Q, new Vector3(w, h - below, l), "UBX_" + b["id"]);
        }

        /// <summary>Tower crown: a setback box, a mast and an aircraft-warning light.</summary>
        static void Crown(Ctx ctx, string chunk, Frame f, float hx, float hz, float h, float seed)
        {
            var steel = ctx.Get(chunk, "steel");
            Box(steel, steel, f, -hx, hx, -hz, hz, h, h + Mathf.Max(4f, h * 0.03f), 6f, 6f, h);
            float top = h + Mathf.Max(4f, h * 0.03f);
            float mast = Mathf.Max(8f, h * 0.08f);
            Box(steel, steel, f, -0.4f, 0.4f, -0.4f, 0.4f, top, top + mast, 2f, 2f, top);
            Box(ctx.Get(chunk, "marker_red"), null, f, -0.7f, 0.7f, -0.7f, 0.7f, top + mast, top + mast + 1.2f, 1f, 1f, 0f);
        }

        /// <summary>Rooftop plant: HVAC boxes and a stair core, deterministic from the seed.</summary>
        static void RoofKit(Ctx ctx, string chunk, Frame f, float hx, float hz, float h, float seed)
        {
            var mb = ctx.Get(chunk, "roof_metal");
            int n = 1 + Mathf.FloorToInt(seed * 3f);
            for (int k = 0; k < n; k++)
            {
                float sx = Mathf.Lerp(-hx * 0.6f, hx * 0.6f, Mathf.Repeat(seed * (k + 3) * 7.13f, 1f));
                float sz = Mathf.Lerp(-hz * 0.6f, hz * 0.6f, Mathf.Repeat(seed * (k + 5) * 3.71f, 1f));
                float s = 1.5f + Mathf.Repeat(seed * (k + 1) * 11.3f, 1f) * 2f;
                Box(mb, mb, f, sx - s, sx + s, sz - s * 0.7f, sz + s * 0.7f, h, h + 1.2f + s * 0.5f, 6f, 6f, h);
            }
        }

        /// <summary>Neon: a glowing band above the shopfronts and a vertical blade sign (Neon Mile, Downtown).</summary>
        static void Neon(Ctx ctx, string chunk, Frame f, float hx, float hz, float h, float seed, float bandY)
        {
            string m1 = Neons[Mathf.FloorToInt(seed * 3f) % 3];
            string m2 = Neons[Mathf.FloorToInt(seed * 7f) % 3];
            Walls(ctx.Get(chunk, m1), f, -hx, hx, -hz, hz, bandY + 0.15f, bandY + 0.4f, 4f, 1f, 0f, 0.12f);
            if (h > 14f)
            {
                float sh = Mathf.Min(16f, h * 0.45f);
                float y0 = bandY + 1.5f;
                var blade = ctx.Get(chunk, m2);
                Box(blade, blade, f, hx + 0.2f, hx + 0.55f, -hz + 2f, -hz + 3.6f, y0, y0 + sh, 1f, 1f, y0);
            }
        }

        /// <summary>Ship-to-shore gantry crane: portal legs on the quay, boom out over the water (south), A-frame to full height.</summary>
        static void EmitCrane(Ctx ctx, Vector3 o, float h, string id)
        {
            string chunk = ChunkOf(o.x, o.z);
            var f = new Frame { O = o, Q = Quaternion.identity };
            var paint = ctx.Get(chunk, "crane_paint");
            float gx = 9f, gz = 15f, leg = 0.9f, portal = 46f;
            foreach (var (x, z) in new[] { (-gx, -gz), (gx, -gz), (-gx, gz), (gx, gz) })
            {
                Box(paint, paint, f, x - leg, x + leg, z - leg, z + leg, 0f, portal, 6f, 6f, 0f);
                ctx.Collider(chunk, f.P(x, portal * 0.5f, z), Quaternion.identity, new Vector3(leg * 2f, portal, leg * 2f), $"UBX_{id}_Leg");
            }
            Box(paint, paint, f, -gx - leg, gx + leg, -gz - leg, -gz + leg, portal, portal + 2.5f, 6f, 6f, 0f);
            Box(paint, paint, f, -gx - leg, gx + leg, gz - leg, gz + leg, portal, portal + 2.5f, 6f, 6f, 0f);
            Box(paint, paint, f, -2.2f, 2.2f, -72f, 42f, portal + 2.5f, portal + 6f, 6f, 6f, 0f);          // boom + backreach
            Box(paint, paint, f, -6f, 6f, gz - 4f, gz + 8f, portal + 6f, portal + 13f, 6f, 6f, 0f);         // machinery house
            Box(paint, paint, f, -gx * 0.5f - 0.6f, -gx * 0.5f + 0.6f, -1f, 1f, portal + 6f, h, 6f, 6f, 0f); // A-frame legs
            Box(paint, paint, f, gx * 0.5f - 0.6f, gx * 0.5f + 0.6f, -1f, 1f, portal + 6f, h, 6f, 6f, 0f);
            Box(paint, paint, f, -gx * 0.5f - 0.6f, gx * 0.5f + 0.6f, -1f, 1f, h - 1.5f, h, 6f, 6f, 0f);
            Box(ctx.Get(chunk, "steel"), null, f, -1.5f, 1.5f, -42f, -38f, portal - 6f, portal + 2.5f, 2f, 2f, 0f); // trolley + spreader
            Box(ctx.Get(chunk, "marker_red"), null, f, -0.6f, 0.6f, -0.6f, 0.6f, h, h + 1.2f, 1f, 1f, 0f);
            Box(ctx.Get(chunk, "marker_red"), null, f, -0.5f, 0.5f, -72.5f, -71.5f, portal + 6f, portal + 7f, 1f, 1f, 0f);
        }

        // ------------------------------------------------------------------ output
        static string Write(Ctx ctx, Transform worldRoot, Dictionary<string, Material> mats, GameDatabase db, string gen)
        {
            var root = new GameObject("Buildings").transform;
            root.SetParent(worldRoot, false);
            var chunkRoots = new Dictionary<string, Transform>();
            Transform ChunkRoot(string id)
            {
                if (chunkRoots.TryGetValue(id, out var t)) return t;
                t = new GameObject(id).transform;
                t.SetParent(root, false);
                t.gameObject.isStatic = true;
                chunkRoots[id] = t;
                return t;
            }
            Directory.CreateDirectory(gen + "/Meshes/Buildings");
            foreach (var old in Directory.GetFiles(gen + "/Meshes/Buildings", "*.asset")) AssetDatabase.DeleteAsset(old.Replace('\\', '/'));
            int meshes = 0, tris = 0;
            foreach (var kv in ctx.Meshes)
            {
                var (chunk, mat) = kv.Key;
                var mb = kv.Value;
                if (mb.T.Count == 0) continue;
                var mesh = new Mesh { name = $"B_{chunk}_{mat}", indexFormat = mb.V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(mb.V);
                mesh.SetNormals(mb.N);
                mesh.SetUVs(0, mb.UV);
                mesh.SetTriangles(mb.T, 0);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                AssetDatabase.CreateAsset(mesh, $"{gen}/Meshes/Buildings/{mesh.name}.asset");
                var go = new GameObject(mat + "_LOD0");
                go.transform.SetParent(ChunkRoot(chunk), false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mats.TryGetValue(mat, out var m) ? m : mats["concrete"];
                bool emissive = mat.StartsWith("neon") || mat == "marker_red";
                r.shadowCastingMode = emissive ? ShadowCastingMode.Off : ShadowCastingMode.On;
                go.isStatic = true;
                var lod = go.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(emissive ? 0.002f : 0.0015f, new Renderer[] { r }) });
                meshes++;
                tris += mb.T.Count / 3;
            }
            int cols = 0;
            foreach (var kv in ctx.Colliders)
            {
                var holder = new GameObject("Colliders").transform;
                holder.SetParent(ChunkRoot(kv.Key), false);
                holder.gameObject.AddComponent<VehicleSurface>().Surface = db.Concrete;
                holder.gameObject.isStatic = true;
                foreach (var (pos, rot, size, name) in kv.Value)
                {
                    var c = new GameObject(name);
                    c.transform.SetParent(holder, false);
                    c.transform.SetPositionAndRotation(pos, rot);
                    c.AddComponent<BoxCollider>().size = size;
                    c.isStatic = true;
                    cols++;
                }
            }
            return $"{meshes} building meshes ({tris:N0} tris) in {chunkRoots.Count} chunks, {cols} box colliders";
        }
    }
}
