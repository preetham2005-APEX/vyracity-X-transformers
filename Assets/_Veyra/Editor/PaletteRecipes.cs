using UnityEngine;
using static VeyraCity.EditorTools.PaletteTextures;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card F2: the procedural recipes behind the palette textures. Each recipe samples one texture tile (u, v in [0, 1))
    /// of a stated real size, with albedo in LINEAR space calibrated to the card's ranges. Pure functions (run in parallel).
    /// </summary>
    public static class PaletteRecipes
    {
        static float S(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        // ------------------------------------------------------------------ F2a: roads (tile sizes in metres in brackets)

        /// <summary>New dense asphalt [4 m]: dark binder, small grey aggregate. Mean albedo ≈ 0.07.</summary>
        public static Sample AsphaltNew(float u, float v) => Asphalt(u, v, 0.042f, 0.36f, 0.10f, 0.17f, 0f, 211);

        /// <summary>Worn asphalt [4 m]: lighter, more exposed aggregate, fine cracks. Mean albedo ≈ 0.10.</summary>
        public static Sample AsphaltWorn(float u, float v) => Asphalt(u, v, 0.058f, 0.46f, 0.12f, 0.20f, 1f, 223);

        static Sample Asphalt(float u, float v, float binder, float stoneRadius, float stoneLo, float stoneHi, float cracks, int seed)
        {
            Voronoi(u, v, 320, seed, 0.9f, out float f1, out _, out float id);
            float stone = S(stoneRadius, stoneRadius - 0.1f, f1);
            float fine = Fbm(u, v, 64, 3, seed + 1);
            float macro = Fbm(u, v, 4, 4, seed + 2);
            float b = binder * (0.9f + 0.2f * fine) * (0.9f + 0.2f * macro);
            float st = Mathf.Lerp(stoneLo, stoneHi, id) * (0.92f + 0.16f * macro);
            float lum = Mathf.Lerp(b, st, stone);
            float h = Mathf.Lerp(0.3f + 0.12f * fine, 0.55f + 0.45f * (1f - f1 / Mathf.Max(stoneRadius, 0.01f)), stone);
            float smooth = Mathf.Lerp(0.34f, 0.18f + 0.1f * id, stone) * (0.9f + 0.2f * macro);
            if (cracks > 0f)
            {
                // Domain-warped cell edges so cracks meander instead of forming straight seams; only where the mask allows.
                float wu = u + (Fbm(u, v, 8, 3, seed + 5) - 0.5f) * 0.08f, wv = v + (Fbm(u, v, 8, 3, seed + 6) - 0.5f) * 0.08f;
                Voronoi(Mathf.Repeat(wu, 1f), Mathf.Repeat(wv, 1f), 4, seed + 3, 1f, out float c1, out float c2, out _);
                float crack = S(0.012f, 0.0f, c2 - c1) * S(0.55f, 0.65f, Fbm(u, v, 4, 3, seed + 4)) * cracks;
                lum = Mathf.Lerp(lum, 0.025f, crack);
                h = Mathf.Lerp(h, 0f, crack);
                smooth = Mathf.Lerp(smooth, 0.1f, crack);
            }
            var c = new Color(lum * 0.98f, lum, lum * 1.02f, 1f);
            return new Sample { Albedo = c, Height = h, Smooth = smooth, AO = 0.55f + 0.45f * h, Metal = 0f, Alpha = 1f };
        }

        /// <summary>Patch overlay for M_Asphalt_Patched [4 m]: rectangular repairs of newer, darker asphalt with seams; alpha = coverage.</summary>
        public static Sample AsphaltPatch(float u, float v)
        {
            const int p = 3;
            int cx = Mathf.FloorToInt(u * p), cy = Mathf.FloorToInt(v * p);
            float fx = u * p - cx, fy = v * p - cy;
            float r = Hash(cx, cy, 401);
            float x0 = 0.08f + 0.3f * Hash(cx, cy, 402), x1 = x0 + 0.35f + 0.25f * Hash(cx, cy, 403);
            float y0 = 0.08f + 0.3f * Hash(cx, cy, 404), y1 = y0 + 0.3f + 0.3f * Hash(cx, cy, 405);
            bool inside = r < 0.55f && fx > x0 && fx < x1 && fy > y0 && fy < y1;
            if (!inside) return new Sample { Albedo = Grey(0.05f), Height = 0.5f, Smooth = 0.3f, AO = 1f, Alpha = 0f };
            float edge = Mathf.Min(Mathf.Min(fx - x0, x1 - fx), Mathf.Min(fy - y0, y1 - fy));
            float seam = S(0.012f, 0.0f, edge);
            var s = AsphaltNew(u * 1.37f % 1f, v * 1.37f % 1f);
            float lum = Mathf.Lerp(Lum(s.Albedo) * 0.85f, 0.03f, seam);
            return new Sample { Albedo = Grey(lum), Height = s.Height, Smooth = s.Smooth, AO = s.AO, Alpha = 1f };
        }

        /// <summary>Concrete deck for viaducts, bridges and the runway [4 m]: broom finish, pores, stains, a joint at the tile edge. Mean ≈ 0.30.</summary>
        public static Sample ConcreteDeck(float u, float v)
        {
            float macro = Fbm(u, v, 4, 4, 501);
            float broom = NoiseXY(u, v, 8, 512, 502);
            Voronoi(u, v, 380, 503, 1f, out float p1, out _, out float pid);
            float pore = S(0.16f, 0.06f, p1) * (pid > 0.55f ? 1f : 0f);
            float stain = S(0.62f, 0.82f, Fbm(u, v, 8, 4, 504)) * 0.55f;
            float lum = 0.30f * (0.92f + 0.16f * macro) * (0.95f + 0.1f * broom) * (1f - 0.3f * pore) * (1f - 0.18f * stain);
            float joint = Mathf.Max(S(0.004f, 0.0f, Mathf.Min(u, 1f - u)), S(0.004f, 0.0f, Mathf.Min(v, 1f - v)));
            lum *= 1f - 0.45f * joint;
            float h = 0.5f + 0.08f * broom - 0.3f * pore - 0.5f * joint;
            return new Sample { Albedo = new Color(lum, lum * 0.99f, lum * 0.96f, 1f), Height = h, Smooth = 0.3f * (1f - 0.3f * stain) + 0.05f * stain, AO = 1f - 0.45f * joint - 0.3f * pore, Alpha = 1f };
        }

        /// <summary>Granite kerb [1 m]: honed, speckled. Mean ≈ 0.24.</summary>
        public static Sample KerbGranite(float u, float v)
        {
            Voronoi(u, v, 180, 601, 1f, out float f1, out _, out float id);
            float grain = Fbm(u, v, 32, 3, 602);
            float lum = id < 0.18f ? 0.05f : id > 0.86f ? 0.55f : Mathf.Lerp(0.18f, 0.3f, id);
            lum *= 0.92f + 0.16f * grain;
            float wear = S(0.5f, 0.8f, Fbm(u, v, 4, 3, 603));
            return new Sample { Albedo = new Color(lum, lum, lum * 1.03f, 1f), Height = 0.5f + 0.1f * grain - 0.05f * f1, Smooth = Mathf.Lerp(0.45f, 0.3f, wear), AO = 0.95f, Alpha = 1f };
        }

        /// <summary>Concrete sidewalk [3.2 m = 2 x 2 slabs of 1.6 m]: per-slab tone, joints, pores, gum spots. Mean ≈ 0.27.</summary>
        public static Sample SidewalkConcrete(float u, float v)
        {
            int sx = Mathf.FloorToInt(u * 2f), sy = Mathf.FloorToInt(v * 2f);
            float fx = u * 2f - sx, fy = v * 2f - sy;
            float slab = 0.27f + (Hash(sx, sy, 701) - 0.5f) * 0.05f;
            float macro = Fbm(u, v, 4, 4, 702);
            Voronoi(u, v, 300, 703, 1f, out float p1, out _, out float pid);
            float pore = S(0.14f, 0.05f, p1) * (pid > 0.6f ? 1f : 0f);
            Voronoi(u, v, 24, 704, 1f, out float g1, out _, out float gid);
            float gum = S(0.06f, 0.03f, g1) * (gid > 0.93f ? 1f : 0f);
            float joint = Mathf.Max(S(0.008f, 0.0f, Mathf.Min(fx, 1f - fx)), S(0.008f, 0.0f, Mathf.Min(fy, 1f - fy)));
            float lum = slab * (0.93f + 0.14f * macro) * (1f - 0.3f * pore) * (1f - 0.55f * joint);
            lum = Mathf.Lerp(lum, 0.07f, gum);
            float h = 0.55f + 0.05f * macro - 0.25f * pore - 0.55f * joint;
            return new Sample { Albedo = new Color(lum, lum * 0.99f, lum * 0.97f, 1f), Height = h, Smooth = 0.28f + 0.1f * gum, AO = 1f - 0.5f * joint - 0.25f * pore, Alpha = 1f };
        }

        /// <summary>Cobblestones [2 m]: rounded setts with sand joints. Mean ≈ 0.17.</summary>
        public static Sample Cobblestone(float u, float v)
        {
            Voronoi(u, v, 16, 801, 0.55f, out float f1, out float f2, out float id);
            float gap = f2 - f1;
            float stone = S(0.06f, 0.14f, gap);
            float dome = S(0.0f, 0.35f, gap);
            float grain = Fbm(u, v, 64, 3, 802);
            var stoneCol = Color.Lerp(new Color(0.15f, 0.145f, 0.14f), new Color(0.22f, 0.2f, 0.17f), id) * (0.9f + 0.2f * grain);
            var sand = new Color(0.16f, 0.14f, 0.11f) * (0.8f + 0.4f * grain);
            var c = Color.Lerp(sand, stoneCol, stone);
            c.a = 1f;
            return new Sample { Albedo = c, Height = 0.15f + 0.85f * dome, Smooth = Mathf.Lerp(0.1f, 0.42f * (0.8f + 0.4f * id), stone), AO = 0.5f + 0.5f * dome, Alpha = 1f };
        }

        /// <summary>Loose gravel [2 m]. Mean ≈ 0.20.</summary>
        public static Sample Gravel(float u, float v)
        {
            Voronoi(u, v, 60, 901, 1f, out float f1, out float f2, out float id);
            float peb = S(0.03f, 0.12f, f2 - f1);
            float dome = S(0.0f, 0.3f, f2 - f1);
            var pc = Color.Lerp(new Color(0.24f, 0.22f, 0.18f), new Color(0.2f, 0.2f, 0.21f), id) * (0.85f + 0.3f * Hash((int)(id * 1000f), 3, 902));
            var c = Color.Lerp(new Color(0.07f, 0.065f, 0.06f), pc, peb);
            c.a = 1f;
            return new Sample { Albedo = c, Height = 0.2f + 0.8f * dome, Smooth = 0.15f, AO = 0.45f + 0.55f * dome, Alpha = 1f };
        }

        /// <summary>Compacted dirt [4 m] with sparse small stones. Mean ≈ 0.12.</summary>
        public static Sample Dirt(float u, float v)
        {
            float macro = Fbm(u, v, 4, 5, 1001);
            float fine = Fbm(u, v, 64, 3, 1002);
            Voronoi(u, v, 140, 1003, 1f, out float f1, out _, out float id);
            float stone = S(0.2f, 0.1f, f1) * (id > 0.8f ? 1f : 0f);
            var c = new Color(0.17f, 0.125f, 0.085f) * (0.75f + 0.5f * macro) * (0.9f + 0.2f * fine);
            c = Color.Lerp(c, new Color(0.2f, 0.19f, 0.17f), stone);
            c.a = 1f;
            return new Sample { Albedo = c, Height = 0.4f + 0.3f * macro + 0.1f * fine + 0.3f * stone, Smooth = 0.12f, AO = 0.7f + 0.3f * macro, Alpha = 1f };
        }

        // ------------------------------------------------------------------ F2b: facades and roofs
        // Wall textures cover ~3 m x 3 m of wall; the facade materials repeat them with _WallTiling over a 12 m x 14 m UV tile.

        /// <summary>Precast concrete panels [3 m]: 1.5 m panel joints, form-tie holes, rain streaks. Mean ≈ 0.32 (light ≈ 0.38).</summary>
        public static Sample FacadeConcrete(float u, float v) => Concrete(u, v, 0.32f, 1201);
        public static Sample FacadeConcreteLight(float u, float v) => Concrete(u, v, 0.38f, 1211);

        static Sample Concrete(float u, float v, float tone, int seed)
        {
            float px = u * 2f, py = v * 2f;
            float fx = px - Mathf.Floor(px), fy = py - Mathf.Floor(py);
            float panel = (Hash(Mathf.FloorToInt(px), Mathf.FloorToInt(py), seed) - 0.5f) * 0.05f;
            float joint = Mathf.Max(S(0.006f, 0.0f, Mathf.Min(fx, 1f - fx)), S(0.006f, 0.0f, Mathf.Min(fy, 1f - fy)));
            float tie = 0f;
            foreach (var cx in new[] { 0.25f, 0.75f })
                foreach (var cy in new[] { 0.25f, 0.75f })
                    tie = Mathf.Max(tie, S(0.012f, 0.006f, Mathf.Sqrt((fx - cx) * (fx - cx) + (fy - cy) * (fy - cy))));
            float streak = S(0.55f, 0.9f, NoiseXY(u, v, 24, 2, seed + 1)) * (0.4f + 0.6f * S(0.2f, 1f, 1f - fy)) * 0.6f;
            float grain = Fbm(u, v, 32, 4, seed + 2);
            float lum = (tone + panel) * (0.92f + 0.16f * grain) * (1f - 0.12f * streak) * (1f - 0.45f * joint) * (1f - 0.5f * tie);
            return new Sample { Albedo = new Color(lum, lum * 0.99f, lum * 0.965f, 1f), Height = 0.5f + 0.08f * grain - 0.5f * joint - 0.3f * tie, Smooth = 0.22f + 0.06f * streak, AO = 1f - 0.4f * joint - 0.3f * tie, Alpha = 1f };
        }

        /// <summary>Limestone ashlar with a carved band [3 m]: 0.5 m courses, staggered 1 m blocks. Mean ≈ 0.40.</summary>
        public static Sample FacadeLimestone(float u, float v)
        {
            float cy = v * 6f;
            int row = Mathf.FloorToInt(cy);
            float fy = cy - row;
            float cx = u * 3f + (row % 2) * 0.5f;
            int col = Mathf.FloorToInt(cx);
            float fx = cx - col;
            float mortar = Mathf.Max(S(0.012f, 0.0f, Mathf.Min(fx, 1f - fx) / 3f), S(0.012f, 0.0f, Mathf.Min(fy, 1f - fy) / 6f));
            float block = 0.40f + (Hash(col & 1023, row, 1301) - 0.5f) * 0.06f;
            float grain = Fbm(u, v, 48, 4, 1302);
            float band = row == 5 ? 0.85f + 0.15f * S(0.3f, 0.7f, Mathf.Abs(Mathf.Sin(u * Mathf.PI * 24f))) : 1f;
            float lum = block * (0.93f + 0.14f * grain) * band * (1f - 0.35f * mortar);
            var c = new Color(lum * 1.04f, lum, lum * 0.9f, 1f);
            float h = 0.6f + 0.1f * grain - 0.5f * mortar + (row == 5 ? 0.15f * S(0.3f, 0.7f, Mathf.Abs(Mathf.Sin(u * Mathf.PI * 24f))) : 0f);
            return new Sample { Albedo = c, Height = h, Smooth = 0.25f, AO = 1f - 0.4f * mortar, Alpha = 1f };
        }

        /// <summary>Brick in stretcher bond [2.925 m x 3 m: 13 bricks x 40 courses], red or dark. Mean ≈ 0.18 / 0.16.</summary>
        public static Sample FacadeBrickRed(float u, float v) => Brick(u, v, new Color(0.34f, 0.115f, 0.068f), new Color(0.27f, 0.25f, 0.22f), 1401);
        public static Sample FacadeBrickDark(float u, float v) => Brick(u, v, new Color(0.24f, 0.15f, 0.12f), new Color(0.22f, 0.21f, 0.2f), 1411);

        static Sample Brick(float u, float v, Color brick, Color mortarCol, int seed)
        {
            float cy = v * 40f;
            int row = Mathf.FloorToInt(cy);
            float fy = cy - row;
            float cx = u * 13f + (row % 2) * 0.5f;
            int col = Mathf.FloorToInt(cx);
            float fx = cx - col;
            float mortar = Mathf.Max(S(0.05f, 0.03f, Mathf.Min(fx, 1f - fx)), S(0.15f, 0.1f, Mathf.Min(fy, 1f - fy)));
            float id = Hash(((col % 13) + 13) % 13, row, seed);
            float tone = 0.82f + 0.36f * id;
            float grain = Fbm(u, v, 64, 3, seed + 1);
            var b = brick * tone * (0.9f + 0.2f * grain);
            if (Hash(((col % 13) + 13) % 13, row, seed + 2) > 0.93f) b *= 0.7f; // occasional burnt header
            var c = Color.Lerp(b, mortarCol * (0.9f + 0.2f * grain), mortar);
            c.a = 1f;
            return new Sample { Albedo = c, Height = 0.6f + 0.15f * grain - 0.45f * mortar, Smooth = Mathf.Lerp(0.22f, 0.12f, mortar), AO = 1f - 0.35f * mortar, Alpha = 1f };
        }

        /// <summary>White stucco render (Azure Coast) [3 m]: fine trowel texture, faint streaks. Mean ≈ 0.62.</summary>
        public static Sample FacadeStucco(float u, float v) => Render(u, v, new Color(0.66f, 0.64f, 0.58f), 1501);

        /// <summary>Beige house render (suburbs) [3 m]. Mean ≈ 0.45.</summary>
        public static Sample FacadeHouse(float u, float v) => Render(u, v, new Color(0.5f, 0.45f, 0.37f), 1511);

        static Sample Render(float u, float v, Color tone, int seed)
        {
            float trowel = Fbm(u, v, 96, 3, seed);
            float macro = Fbm(u, v, 6, 3, seed + 1);
            float streak = S(0.6f, 0.9f, NoiseXY(u, v, 20, 2, seed + 2)) * 0.4f;
            var c = tone * (0.94f + 0.1f * trowel) * (0.95f + 0.1f * macro) * (1f - 0.1f * streak);
            c.a = 1f;
            return new Sample { Albedo = c, Height = 0.5f + 0.2f * trowel, Smooth = 0.2f, AO = 1f, Alpha = 1f };
        }

        /// <summary>Standing-seam metal cladding [3 m]: 0.5 m panels, galvanised grey. Mean ≈ 0.50, metallic.</summary>
        public static Sample FacadeMetalPanel(float u, float v)
        {
            float px = u * 6f;
            float fx = px - Mathf.Floor(px);
            float seam = S(0.03f, 0.0f, Mathf.Min(fx, 1f - fx));
            float ripple = Mathf.Sin(fx * Mathf.PI) * 0.15f;
            float grain = Fbm(u, v, 32, 3, 1601);
            float lum = 0.5f * (0.92f + 0.16f * grain) * (1f - 0.25f * seam);
            return new Sample { Albedo = new Color(lum, lum * 1.01f, lum * 1.03f, 1f), Height = 0.4f + ripple + 0.4f * seam, Smooth = 0.55f - 0.1f * grain, AO = 1f - 0.3f * seam, Metal = 0.85f, Alpha = 1f };
        }

        /// <summary>Horizontal timber siding [3 m]: 0.15 m boards, weathered. Mean ≈ 0.17.</summary>
        public static Sample FacadeWood(float u, float v)
        {
            float by = v * 20f;
            int board = Mathf.FloorToInt(by);
            float fy = by - board;
            float shadow = S(0.12f, 0.0f, fy);
            float grain = NoiseXY(u, v, 6, 160, 1701 + board);
            var c = new Color(0.25f, 0.17f, 0.11f) * (0.85f + 0.3f * Hash(board, 0, 1702)) * (0.85f + 0.3f * grain) * (1f - 0.4f * shadow);
            c.a = 1f;
            return new Sample { Albedo = c, Height = 0.4f + 0.5f * fy - 0.3f * shadow + 0.05f * grain, Smooth = 0.18f, AO = 1f - 0.4f * shadow, Alpha = 1f };
        }

        /// <summary>Dark spandrel panel behind curtain-wall glass [3 m]: dark glass/metal. Mean ≈ 0.08.</summary>
        public static Sample Spandrel(float u, float v)
        {
            float grain = Fbm(u, v, 16, 3, 1801);
            float lum = 0.08f * (0.9f + 0.2f * grain);
            return new Sample { Albedo = new Color(lum, lum * 1.03f, lum * 1.08f, 1f), Height = 0.5f, Smooth = 0.75f, AO = 1f, Metal = 0.3f, Alpha = 1f };
        }

        /// <summary>Gravel roof ballast [2 m]. Mean ≈ 0.2.</summary>
        public static Sample RoofGravel(float u, float v)
        {
            Voronoi(u, v, 90, 1901, 1f, out float f1, out float f2, out float id);
            float peb = S(0.02f, 0.1f, f2 - f1);
            var pc = Color.Lerp(new Color(0.24f, 0.23f, 0.22f), new Color(0.2f, 0.19f, 0.17f), id);
            var c = Color.Lerp(new Color(0.07f, 0.07f, 0.07f), pc, peb);
            c.a = 1f;
            return new Sample { Albedo = c, Height = 0.2f + 0.8f * S(0f, 0.3f, f2 - f1), Smooth = 0.12f, AO = 0.5f + 0.5f * peb, Alpha = 1f };
        }

        /// <summary>Dark roofing membrane [4 m]: 1 m welded seams, ponding stains. Mean ≈ 0.08.</summary>
        public static Sample RoofMembrane(float u, float v)
        {
            float sx = u * 4f;
            float fx = sx - Mathf.Floor(sx);
            float seam = S(0.012f, 0.0f, Mathf.Abs(fx - 0.03f));
            float stain = S(0.6f, 0.85f, Fbm(u, v, 4, 4, 2001)) * 0.5f;
            float grain = Fbm(u, v, 64, 2, 2002);
            float lum = 0.085f * (0.92f + 0.16f * grain) * (1f + 0.35f * stain);
            return new Sample { Albedo = new Color(lum, lum, lum * 1.02f, 1f), Height = 0.5f + 0.3f * seam, Smooth = 0.35f - 0.15f * stain, AO = 1f, Alpha = 1f };
        }

        /// <summary>
        /// Interior atlas (4 x 4 rooms, each 512 px of a 2048 px texture), the back wall of each fake room: wall colour, floor
        /// strip, ceiling light, and furniture silhouettes (desks with monitors, sofas, shelves, beds, plants, shop racks).
        /// The facade shader samples the floor tone near the cell's bottom-left and the wall tone near its left edge.
        /// </summary>
        public static Color InteriorAtlas(float u, float v)
        {
            int cx = Mathf.Min(3, Mathf.FloorToInt(u * 4f)), cy = Mathf.Min(3, Mathf.FloorToInt(v * 4f));
            float x = u * 4f - cx, y = v * 4f - cy;
            int room = cy * 4 + cx;
            int kind = room % 4;                        // 0 office, 1 living room, 2 bedroom, 3 shop
            Color[] walls = { Lin(0.55f, 0.56f, 0.56f), Lin(0.6f, 0.52f, 0.4f), Lin(0.45f, 0.5f, 0.58f), Lin(0.62f, 0.6f, 0.55f), Lin(0.48f, 0.42f, 0.36f), Lin(0.5f, 0.55f, 0.5f) };
            Color[] floors = { Lin(0.22f, 0.2f, 0.18f), Lin(0.3f, 0.2f, 0.12f), Lin(0.2f, 0.22f, 0.26f), Lin(0.35f, 0.33f, 0.3f) };
            var wall = walls[room % walls.Length] * (0.95f + 0.1f * Fbm(u, v, 64, 2, 2101 + room));
            var floorCol = floors[(room / 2) % floors.Length];
            float floorH = 0.12f;
            if (y < floorH) return floorCol * (0.8f + 0.4f * y / floorH);
            if (y > 0.93f) return Lin(0.8f, 0.79f, 0.76f);                                   // ceiling edge
            Color c = wall;
            float h = Hash(room, 7, 2102);
            // skirting
            if (y < floorH + 0.025f) c = wall * 0.6f;
            switch (kind)
            {
                case 0: // office: two desks with monitors, a cabinet
                    for (int d = 0; d < 2; d++)
                    {
                        float dx = 0.12f + d * 0.45f;
                        if (x > dx && x < dx + 0.33f && y > floorH + 0.18f && y < floorH + 0.21f) c = Lin(0.32f, 0.28f, 0.22f);
                        if (x > dx + 0.08f && x < dx + 0.22f && y > floorH + 0.21f && y < floorH + 0.33f) c = Lin(0.03f, 0.05f, 0.08f) + Lin(0.1f, 0.2f, 0.3f) * h;
                        if (x > dx + 0.02f && x < dx + 0.04f && y > floorH && y < floorH + 0.18f) c = Lin(0.1f, 0.1f, 0.1f);
                    }
                    if (x > 0.84f && x < 0.97f && y > floorH && y < 0.6f) c = Lin(0.4f, 0.41f, 0.42f);
                    break;
                case 1: // living room: sofa, picture, lamp
                    if (x > 0.15f && x < 0.75f && y > floorH && y < floorH + 0.17f) c = Lin(0.3f + 0.3f * h, 0.18f, 0.12f + 0.2f * (1f - h));
                    if (x > 0.15f && x < 0.75f && y > floorH + 0.17f && y < floorH + 0.24f) c = Lin(0.25f + 0.25f * h, 0.15f, 0.1f + 0.2f * (1f - h));
                    if (x > 0.3f && x < 0.58f && y > 0.5f && y < 0.72f) c = Lin(0.15f + 0.4f * h, 0.3f, 0.45f - 0.3f * h);
                    if (x > 0.82f && x < 0.84f && y > floorH && y < 0.55f) c = Lin(0.1f, 0.1f, 0.1f);
                    if (x > 0.78f && x < 0.88f && y > 0.55f && y < 0.63f) c = Lin(0.9f, 0.8f, 0.6f);
                    break;
                case 2: // bedroom: bed, wardrobe, plant
                    if (x > 0.1f && x < 0.6f && y > floorH && y < floorH + 0.13f) c = Lin(0.75f, 0.72f, 0.68f);
                    if (x > 0.1f && x < 0.18f && y > floorH && y < floorH + 0.3f) c = Lin(0.3f, 0.22f, 0.15f);
                    if (x > 0.7f && x < 0.95f && y > floorH && y < 0.8f) c = Lin(0.35f, 0.27f, 0.2f);
                    if (x > 0.62f && x < 0.68f && y > floorH && y < floorH + 0.22f) c = Lin(0.1f, 0.25f + 0.1f * Fbm(u, v, 128, 2, 2103), 0.08f);
                    break;
                default: // shop: shelves with goods
                    for (int s = 0; s < 4; s++)
                    {
                        float sy = floorH + 0.12f + s * 0.17f;
                        if (y > sy && y < sy + 0.015f && x > 0.05f && x < 0.95f) c = Lin(0.5f, 0.5f, 0.5f);
                        if (y > sy + 0.015f && y < sy + 0.11f && x > 0.05f && x < 0.95f)
                        {
                            float item = Hash(Mathf.FloorToInt(x * 24f), s + room * 7, 2104);
                            c = Color.HSVToRGB(item, 0.55f, 0.35f + 0.3f * item).linear;
                        }
                    }
                    break;
            }
            float vignette = 1f - 0.25f * Mathf.Pow(Mathf.Abs(x - 0.5f) * 2f, 2f);
            var outc = c * vignette;
            outc.a = 1f;
            return outc;
        }

        // ------------------------------------------------------------------ F2c: props, vegetation, vehicle, neon

        /// <summary>Painted steel [2 m], white base tinted by the material colour: paint with small chips to dark metal and rust. Mean ≈ 0.62 x tint.</summary>
        public static Sample PaintedMetal(float u, float v)
        {
            float chips = S(0.74f, 0.8f, Fbm(u, v, 32, 4, 2201));
            float rust = S(0.7f, 0.85f, Fbm(u, v, 8, 4, 2202)) * chips;
            float grain = Fbm(u, v, 128, 2, 2203);
            var paint = Grey(0.7f * (0.95f + 0.1f * grain));
            var c = Color.Lerp(paint, Lin(0.08f, 0.08f, 0.085f), chips);
            c = Color.Lerp(c, Lin(0.18f, 0.07f, 0.03f), rust);
            c.a = 1f;
            return new Sample { Albedo = c, Height = 0.6f - 0.4f * chips, Smooth = Mathf.Lerp(0.45f, 0.3f, chips), AO = 1f, Metal = 0.85f * chips * (1f - rust), Alpha = 1f };
        }

        /// <summary>Hot-dip galvanised steel [1 m]: spangle crystals. Mean ≈ 0.58, metallic.</summary>
        public static Sample Galvanized(float u, float v)
        {
            Voronoi(u, v, 40, 2301, 1f, out float f1, out float f2, out float id);
            float edge = S(0.03f, 0.0f, f2 - f1);
            float lum = 0.58f * (0.9f + 0.2f * id) * (1f - 0.1f * edge);
            return new Sample { Albedo = new Color(lum, lum * 1.01f, lum * 1.02f, 1f), Height = 0.5f + 0.05f * id, Smooth = 0.5f + 0.15f * id, AO = 1f, Metal = 0.92f, Alpha = 1f };
        }

        /// <summary>Dark moulded plastic [1 m]. Mean ≈ 0.04.</summary>
        public static Sample PlasticDark(float u, float v)
        {
            float grain = Fbm(u, v, 256, 2, 2401);
            float lum = 0.04f * (0.9f + 0.2f * grain);
            return new Sample { Albedo = Grey(lum), Height = 0.5f + 0.05f * grain, Smooth = 0.55f, AO = 1f, Alpha = 1f };
        }

        /// <summary>Rubber [1 m]. Mean ≈ 0.026.</summary>
        public static Sample Rubber(float u, float v)
        {
            float grain = Fbm(u, v, 128, 3, 2501);
            float lum = 0.026f * (0.9f + 0.2f * grain);
            return new Sample { Albedo = Grey(lum), Height = 0.5f + 0.1f * grain, Smooth = 0.22f, AO = 1f, Alpha = 1f };
        }

        /// <summary>Weathered timber planks [2 m]: silvered grain, gaps. Mean ≈ 0.16.</summary>
        public static Sample WoodWeathered(float u, float v)
        {
            float px = u * 8f;
            int plank = Mathf.FloorToInt(px);
            float fx = px - plank;
            float gap = S(0.04f, 0.0f, Mathf.Min(fx, 1f - fx));
            float grain = NoiseXY(u, v, 128, 6, 2601 + plank);
            float silver = 0.5f + 0.5f * Hash(plank, 0, 2602);
            var c = Color.Lerp(Lin(0.17f, 0.12f, 0.08f), Lin(0.2f, 0.19f, 0.17f), silver) * (0.8f + 0.4f * grain);
            c = Color.Lerp(c, Lin(0.02f, 0.02f, 0.02f), gap);
            c.a = 1f;
            return new Sample { Albedo = c, Height = 0.5f + 0.2f * grain - 0.5f * gap, Smooth = 0.14f, AO = 1f - 0.5f * gap, Alpha = 1f };
        }

        /// <summary>Leaf cluster card [1 m], alpha-clipped (alpha = leaf coverage). Mean ≈ 0.08.</summary>
        public static Sample Leaves(float u, float v)
        {
            Voronoi(u, v, 14, 2701, 1f, out float f1, out float f2, out float id);
            // An elongated leaf per cell: distance stretched along a random direction.
            float a = id * Mathf.PI * 2f;
            float lu = u * 14f, lv = v * 14f;
            float cx = Mathf.Floor(lu) + 0.5f, cy = Mathf.Floor(lv) + 0.5f;
            float dx = lu - cx, dy = lv - cy;
            float rx = dx * Mathf.Cos(a) + dy * Mathf.Sin(a), ry = -dx * Mathf.Sin(a) + dy * Mathf.Cos(a);
            float leaf = S(0.42f, 0.36f, Mathf.Sqrt(rx * rx * 1f + ry * ry * 4f));
            float cover = Mathf.Max(leaf, S(0.3f, 0.2f, f1));
            var c = Color.Lerp(Lin(0.04f, 0.09f, 0.025f), Lin(0.09f, 0.16f, 0.04f), id) * (0.85f + 0.3f * Fbm(u, v, 64, 2, 2702));
            c.a = 1f;
            return new Sample { Albedo = c, Height = 0.5f + 0.3f * cover, Smooth = 0.32f, AO = 0.7f + 0.3f * cover, Alpha = cover };
        }

        /// <summary>Tree bark [2 m]: vertical furrows. Mean ≈ 0.10.</summary>
        public static Sample Bark(float u, float v)
        {
            float furrow = NoiseXY(u, v, 24, 3, 2801);
            float detail = Fbm(u, v, 64, 3, 2802);
            float h = S(0.3f, 0.75f, furrow) * 0.8f + 0.2f * detail;
            var c = Lin(0.12f, 0.095f, 0.075f) * (0.6f + 0.6f * h);
            c.a = 1f;
            return new Sample { Albedo = c, Height = h, Smooth = 0.12f, AO = 0.5f + 0.5f * h, Alpha = 1f };
        }

        /// <summary>Car paint metallic flakes [0.25 m]: white base (the material colour is the paint), a high-frequency flake normal.</summary>
        public static Sample CarPaintFlakes(float u, float v)
        {
            Voronoi(u, v, 256, 2901, 1f, out float f1, out _, out float id);
            return new Sample { Albedo = Grey(0.8f), Height = 0.5f + 0.5f * id, Smooth = 0.6f, AO = 1f, Metal = 0.75f, Alpha = 1f };
        }

        /// <summary>LED screen content (rgb): a 128 x 64 grid of LED dots showing colour bands and fictional words; scrolled by the Neon shader.</summary>
        public static Color LedScreen(float u, float v)
        {
            const int W = 128, H = 64;
            int lx = Mathf.Min(W - 1, Mathf.FloorToInt(u * W)), ly = Mathf.Min(H - 1, Mathf.FloorToInt(v * H));
            float fx = u * W - lx, fy = v * H - ly;
            float dot = S(0.48f, 0.3f, Mathf.Sqrt((fx - 0.5f) * (fx - 0.5f) + (fy - 0.5f) * (fy - 0.5f)));
            float band = Mathf.Repeat(lx / (float)W + ly / (float)H * 0.3f, 1f);
            var bg = Color.HSVToRGB(band, 0.8f, 0.35f).linear;
            bool text = LedText(lx, ly);
            var c = text ? Lin(1f, 1f, 1f) : bg;
            var o = c * dot;
            o.a = 1f;
            return o;
        }

        static readonly string[] LedWords = { "VEYRA 24H", "NEON MILE", "OPEN LATE" };

        static bool LedText(int x, int y)
        {
            // 3 rows of 5 x 7 glyphs, scaled 1:1 onto the LED grid (each glyph 6 LEDs wide incl. spacing).
            int row = (63 - y) / 20;
            if (row > 2) return false;
            int gy = (63 - y) - row * 20 - 6;
            if (gy < 0 || gy >= 7) return false;
            string word = LedWords[row];
            int startX = 4 + row * 8;
            int gx = x - startX;
            if (gx < 0) return false;
            int ci = gx / 6, cx = gx % 6;
            if (ci >= word.Length || cx >= 5) return false;
            return VeyraCity.Testing.PixelFont.Pixel(word[ci], cx, gy);
        }

        /// <summary>Backlit sign face (rgb): a two-colour gradient panel with a border and a fictional word.</summary>
        public static Color SignBacklit(float u, float v)
        {
            var top = Lin(1f, 0.55f, 0.12f);
            var bottom = Lin(0.95f, 0.18f, 0.35f);
            var c = Color.Lerp(bottom, top, v);
            float border = Mathf.Max(S(0.04f, 0.02f, Mathf.Min(u, 1f - u)), S(0.08f, 0.05f, Mathf.Min(v, 1f - v)));
            c = Color.Lerp(c, Lin(1f, 1f, 1f), border);
            int lx = Mathf.FloorToInt(u * 64f), ly = Mathf.FloorToInt((1f - v) * 32f);
            const string word = "NOODLES";
            int gx = lx - 11, gy = ly - 12;
            if (gx >= 0 && gy >= 0 && gy < 7 && gx / 6 < word.Length && gx % 6 < 5 && VeyraCity.Testing.PixelFont.Pixel(word[gx / 6], gx % 6, gy))
                c = Lin(1f, 1f, 0.95f);
            c.a = 1f;
            return c;
        }

        /// <summary>Road paint [2 m], white (tinted yellow by the material colour): raised, slightly glossy, worn spots. Mean ≈ 0.62.</summary>
        public static Sample MarkingPaint(float u, float v)
        {
            float chips = S(0.70f, 0.78f, Fbm(u, v, 48, 3, 1101));
            float abrasion = S(0.55f, 0.85f, Fbm(u, v, 256, 2, 1103)) * 0.45f;
            float wear = Mathf.Clamp01(chips * 0.85f + abrasion * S(0.4f, 0.7f, Fbm(u, v, 8, 3, 1104)));
            float fine = Fbm(u, v, 128, 2, 1102);
            float paint = 0.72f * (0.94f + 0.06f * fine);
            float lum = Mathf.Lerp(paint, 0.08f, wear);
            return new Sample { Albedo = Grey(lum), Height = Mathf.Lerp(0.7f, 0.3f, wear), Smooth = Mathf.Lerp(0.45f, 0.3f, wear), AO = 1f, Alpha = 1f };
        }
    }
}
