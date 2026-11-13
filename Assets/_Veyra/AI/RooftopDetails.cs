using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.AI
{
    /// <summary>
    /// Rooftop clutter for the skyline (round 2; GTA V's and Cyberpunk's roofs are busy, ours were bare boxes): stair and lift
    /// housings, AC units with fans, New York style wooden water tanks on steel legs (mid-rise), and antenna masts with
    /// blinking red aircraft beacons (tall buildings). Buildings come from their UBX collision boxes (centre, yaw, size);
    /// props stay in the middle third of the roof so they sit on top of every building shape (stepped towers included), and
    /// only buildings 12 m and taller get them (pitched-roof houses are lower). Merged per 250 m cell, built one cell per
    /// frame within 1.6 km of the player, no colliders, no shadows (small roof props, distant skyline).
    /// </summary>
    public sealed class RooftopDetails : MonoBehaviour
    {
        public VehicleController Player;
        public bool Active = true;
        public float BuildRadius = 1600f, DropRadius = 2000f;

        struct Roof { public Vector3 Top; public Quaternion Rot; public float HalfW, HalfL, Height; public int Seed; }
        const float Cell = 250f;
        const int Concrete = 0, Metal = 1, MetalDark = 2, Wood = 3, Beacon = 4, RoofGravel = 5, RoofWhite = 6, RoofGreen = 7, Solar = 8, Slots = 9;
        readonly Dictionary<long, List<Roof>> grid = new Dictionary<long, List<Roof>>();
        readonly Dictionary<long, GameObject> built = new Dictionary<long, GameObject>();
        static Material[] mats;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        bool scanned;
        /// <summary>A cell arrived while the scan was running, so the snapshot missed it (BUG-170).</summary>
        bool rescan;
        IEnumerator scanJob;
        /// <summary>Colliders tested between yields (BUG-170): each one costs a name test and a transform.</summary>
        const int YieldEvery = 256;
        float timer;
        int roofCount;

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        readonly ProcMeshBuilder sharedBuilder = new ProcMeshBuilder(Slots);

        /// <summary>Deterministic per-roof random numbers without allocation (xorshift32).</summary>
        struct FastRng
        {
            uint s;
            public FastRng(int seed) { s = (uint)seed * 2654435761u + 1u; if (s == 0) s = 1; }
            public double NextDouble() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / 16777216.0; }
        }

        // round 3: streamed cells (world-abc's CellStreamer) bring their buildings later; at boot all 48 cells are
        // loaded before main starts, so the first scan sees the whole city and later loads only add that cell's roofs
        void OnEnable() => VeyraCity.World.Streaming.CellStreamer.CellLoaded += OnCellLoaded;
        void OnDisable() => VeyraCity.World.Streaming.CellStreamer.CellLoaded -= OnCellLoaded;

        // BUG-170: reused across cell loads. A cell streaming back in used to allocate a HashSet plus one array per
        // root object and one per GetComponentsInChildren call; that garbage landed in the streaming frames.
        readonly HashSet<long> seenKeys = new HashSet<long>();
        readonly List<GameObject> rootBuf = new List<GameObject>();
        readonly List<BoxCollider> boxBuf = new List<BoxCollider>();

        void OnCellLoaded(string cell, UnityEngine.SceneManagement.Scene scene)
        {
            // BUG-170: the scan snapshots the collider list, so a cell arriving mid-scan is in neither the snapshot
            // nor this path. Flag it and the scan runs again; AddRoof dedupes, so the second pass only picks up what
            // the first could not see. Doing the work here instead would undo the budget - at boot all 48 cells land
            // at once, which is the whole-city scan again, on the loading frames.
            if (!scanned) { rescan = true; return; }
            seenKeys.Clear();
            rootBuf.Clear();
            scene.GetRootGameObjects(rootBuf);
            for (int g = 0; g < rootBuf.Count; g++)
            {
                rootBuf[g].GetComponentsInChildren(true, boxBuf);
                for (int i = 0; i < boxBuf.Count; i++) AddRoof(boxBuf[i], seenKeys);
            }
            // cells already dressed whose roof list changed are rebuilt on the next ticks
            foreach (var key in seenKeys)
                if (built.TryGetValue(key, out var g)) { if (g != null) { Destroy(g.GetComponent<MeshFilter>().sharedMesh); Destroy(g); } built.Remove(key); }
        }

        // BUG-170: all 48 cells load at boot and far ones stream out and back in; a reloaded cell's roofs are already in the
        // grid, so they are recognised by name and position (0.5 m) and neither added twice nor rebuilt
        readonly HashSet<long> knownRoofs = new HashSet<long>();

        static long RoofId(string name, Vector3 c)
        {
            unchecked
            {
                long h = name.GetHashCode();
                h = h * 486187739 + Mathf.RoundToInt(c.x * 2f);
                h = h * 486187739 + Mathf.RoundToInt(c.y * 2f);
                h = h * 486187739 + Mathf.RoundToInt(c.z * 2f);
                return h;
            }
        }

        void AddRoof(BoxCollider box, HashSet<long> touched)
        {
                var n = box.name;
                if (!n.StartsWith("UBX_") || n.EndsWith("_Leg")) return;
                var t = box.transform;
                Vector3 size = Vector3.Scale(box.size, t.lossyScale);
                if (size.y < 12f || size.x < 9f || size.z < 9f) return;
                Vector3 c = t.TransformPoint(box.center);
                if (!knownRoofs.Add(RoofId(n, c))) return;
                var roof = new Roof
                {
                    Top = c + Vector3.up * (size.y * 0.5f),
                    Rot = Quaternion.Euler(0f, t.eulerAngles.y, 0f),
                    HalfW = size.x * 0.5f,
                    HalfL = size.z * 0.5f,
                    Height = size.y,
                    Seed = n.GetHashCode() & 0x7fffffff,
                };
                long key = Key(Mathf.FloorToInt(c.x / Cell), Mathf.FloorToInt(c.z / Cell));
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<Roof>();
                list.Add(roof);
                roofCount++;
                touched?.Add(key);
        }

        /// <summary>
        /// Builds the roof table a few hundred colliders at a time (BUG-170). The whole-city scan cost 108 ms on one
        /// frame; the work is identical, only spread by <see cref="FrameBudget.Pump"/>. No cell may be dressed until
        /// this finishes: a cell built mid-scan would cache a mesh missing the roofs found after it, and an empty cell
        /// caches as null, so it would never be revisited.
        /// </summary>
        IEnumerator ScanSteps()
        {
            int sinceYield = 0, passes = 0, colliders = 0;
            do
            {
                rescan = false;
                passes++;
                // FindObjectsSortMode.None: the InstanceID sort over a whole city of colliders is pure cost here, and
                // the roof set does not depend on the order (dedupe is by name and position)
                var boxes = FindObjectsByType<BoxCollider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                colliders = boxes.Length;
                for (int i = 0; i < boxes.Length; i++)
                {
                    if (++sinceYield >= YieldEvery) { sinceYield = 0; yield return null; }
                    AddRoof(boxes[i], null);
                }
            }
            while (rescan);   // a cell loaded while we were scanning: sweep again for what the snapshot missed
            scanned = true;
            Debug.Log($"[Rooftops] {roofCount} roofs dressed (from {colliders} colliders, {passes} scan pass(es))");
        }

        void Update()
        {
            long fc0 = VeyraCity.Core.FrameCost.Start();
            UpdateBody();
            VeyraCity.Core.FrameCost.Stop("Rooftops", fc0);
        }

        void UpdateBody()
        {
            // behind the menu, after the other slot builders
            // BUG-170: pump the scan within a frame budget instead of doing it all on one frame.
            if (!scanned && Active && Time.frameCount > 11)
            {
                if (scanJob == null) scanJob = ScanSteps();
                if (!VeyraCity.Core.FrameBudget.Pump(scanJob)) scanJob = null;
            }
            if (!Active || Player == null) return;
            if (mats != null && mats[Beacon] != null)
            {
                float night = WorldEnvironment.Instance != null ? WorldEnvironment.Instance.NightFactor : 0f;
                float blink = Mathf.Repeat(Time.time * 0.75f, 1f) < 0.35f ? 1f : 0.15f;   // aviation red, ~45 flashes a minute
                mats[Beacon].SetColor(EmissionId, new Color(1f, 0.06f, 0.04f) * (0.4f + 6f * night) * blink);
            }
            // BUG-170: cell meshes are built once and cached, and a cell with no roofs caches as null, so nothing may
            // be dressed until the scan is complete — a cell built mid-scan would silently lose every roof found after
            // it, with no error and no second chance. The beacon blink above still runs.
            if (!scanned) return;
            // a cell under construction gets 8 roofs a frame (iteration 22: with parapets a whole downtown cell in one
            // frame cost up to 28 ms)
            if (pendingRoofs != null)
            {
                int end = Mathf.Min(pendingRoofs.Count, pendingIndex + 8);
                for (; pendingIndex < end; pendingIndex++) Dress(pendingBuilder, pendingRoofs[pendingIndex]);
                if (pendingIndex >= pendingRoofs.Count)
                {
                    built[pendingKey] = Finish(pendingKey, pendingBuilder);
                    pendingRoofs = null;
                    pendingBuilder = null;
                }
                return;
            }
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = 0.1f;
            Vector3 f = Player.transform.position;
            List<long> drop = null;
            foreach (var kv in built)
            {
                int cx = (int)(kv.Key >> 32), cz = (int)(uint)kv.Key;
                var c = new Vector3((cx + 0.5f) * Cell, f.y, (cz + 0.5f) * Cell);
                if ((c - f).sqrMagnitude > DropRadius * DropRadius) (drop ??= new List<long>()).Add(kv.Key);
            }
            if (drop != null)
                foreach (var key in drop)
                {
                    var go = built[key];
                    if (go != null) { Destroy(go.GetComponent<MeshFilter>().sharedMesh); Destroy(go); }
                    built.Remove(key);
                }
            int fx = Mathf.FloorToInt(f.x / Cell), fz = Mathf.FloorToInt(f.z / Cell);
            int reach = Mathf.CeilToInt(BuildRadius / Cell);
            // nearest unbuilt cell first, one per tick
            long best = 0;
            float bestD = float.MaxValue;
            for (int x = fx - reach; x <= fx + reach; x++)
                for (int z = fz - reach; z <= fz + reach; z++)
                {
                    long key = Key(x, z);
                    if (built.ContainsKey(key)) continue;
                    var c = new Vector3((x + 0.5f) * Cell, f.y, (z + 0.5f) * Cell);
                    float d = (c - f).sqrMagnitude;
                    if (d > BuildRadius * BuildRadius || d >= bestD) continue;
                    best = key;
                    bestD = d;
                }
            if (bestD < float.MaxValue)
            {
                if (grid.TryGetValue(best, out var roofs))
                {
                    Ensure();
                    pendingKey = best;
                    pendingRoofs = roofs;
                    pendingIndex = 0;
                    pendingBuilder = sharedBuilder;   // round 3 P1: one builder reused (a fresh one per cell regrew its lists: ~16 KB/frame)
                    pendingBuilder.Clear();
                }
                else built[best] = null;
            }
        }

        long pendingKey;
        List<Roof> pendingRoofs;
        int pendingIndex;
        ProcMeshBuilder pendingBuilder;

        GameObject Finish(long key, ProcMeshBuilder mb)
        {
            var go = new GameObject("Rooftops_" + key);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("RooftopCell");
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.Off;
            return go;
        }

        static readonly float[] Sides = { -1f, 1f }, Legs = { -1.1f, 1.1f };   // no per-roof array garbage

        static void Dress(ProcMeshBuilder mb, Roof r)
        {
            var rng = new FastRng(r.Seed);   // round 3 P1: no System.Random (and its int[56]) per roof
            var m = Matrix4x4.TRS(r.Top, r.Rot, Vector3.one);
            float ax = r.HalfW * 0.33f, az = r.HalfL * 0.33f;   // the safe middle third
            Vector3 P(double u, double v) => new Vector3((float)(u * 2 - 1) * ax, 0f, (float)(v * 2 - 1) * az);
            // roof finish (iteration 26: every roof was the same dark membrane from the skyline and on the map's aerial):
            // pale gravel, white reflective membrane, a green roof, or the dark membrane left showing; solar rows on some
            double fin = rng.NextDouble();
            int finish = fin < 0.32 ? RoofGravel : fin < 0.55 ? RoofWhite : fin < 0.66 ? RoofGreen : -1;
            if (finish >= 0)
                mb.Box(m, finish, new Vector3(0f, 0.03f, 0f), new Vector3(r.HalfW * 2f - 0.9f, 0.06f, r.HalfL * 2f - 0.9f));
            if (finish != RoofGreen && r.HalfW * r.HalfL > 120f && rng.NextDouble() < 0.3)
            {
                // rows of tilted solar panels in the half away from the stair housing
                int rows = Mathf.Clamp(Mathf.FloorToInt(r.HalfL * 0.5f / 2.2f), 1, 5);
                for (int k = 0; k < rows; k++)
                {
                    var c = new Vector3(0f, 0.55f, -r.HalfL * 0.45f + k * 2.2f);
                    var rot = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(-22f, 0f, 0f), Vector3.one);
                    mb.Box(m * Matrix4x4.Translate(c) * rot, Solar, Vector3.zero, new Vector3(r.HalfW * 0.9f, 0.05f, 1.6f));
                    mb.Box(m, MetalDark, c + new Vector3(0f, -0.3f, 0.4f), new Vector3(r.HalfW * 0.9f, 0.08f, 0.08f));
                }
            }
            // parapet round the roof edge with a darker coping (iteration 21: the flat box tops read as unfinished from the
            // skyline views; GTA V's roofs all have one)
            float ph = 0.9f + (float)rng.NextDouble() * 0.6f, pt = 0.35f;
            foreach (float sx in Sides)
            {
                mb.Box(m, Concrete, new Vector3(sx * (r.HalfW - pt * 0.5f), ph * 0.5f, 0f), new Vector3(pt, ph, r.HalfL * 2f));
                mb.Box(m, MetalDark, new Vector3(sx * (r.HalfW - pt * 0.5f), ph + 0.04f, 0f), new Vector3(pt + 0.08f, 0.08f, r.HalfL * 2f + 0.08f));
            }
            foreach (float sz in Sides)
            {
                mb.Box(m, Concrete, new Vector3(0f, ph * 0.5f, sz * (r.HalfL - pt * 0.5f)), new Vector3(r.HalfW * 2f - pt * 2f, ph, pt));
                mb.Box(m, MetalDark, new Vector3(0f, ph + 0.04f, sz * (r.HalfL - pt * 0.5f)), new Vector3(r.HalfW * 2f, 0.08f, pt + 0.08f));
            }
            // stair / lift housing
            if (r.HalfW * r.HalfL > 60f)
            {
                var p = P(rng.NextDouble(), rng.NextDouble());
                mb.Box(m, Concrete, p + new Vector3(0f, 1.5f, 0f), new Vector3(3.2f, 3f, 3.6f));
                mb.Box(m, MetalDark, p + new Vector3(0f, 1.1f, 1.81f), new Vector3(1.1f, 2.1f, 0.05f));   // door
            }
            // AC units with fan tops
            int ac = Mathf.Clamp(Mathf.RoundToInt(r.HalfW * r.HalfL / 90f), 1, 6);
            for (int i = 0; i < ac; i++)
            {
                var p = P(rng.NextDouble(), rng.NextDouble());
                bool big = rng.NextDouble() < 0.4;
                var sz = big ? new Vector3(2.4f, 1.3f, 1.6f) : new Vector3(1.3f, 0.9f, 1.0f);
                mb.Box(m, Metal, p + new Vector3(0f, sz.y * 0.5f, 0f), sz);
                mb.Cylinder(m, MetalDark, p + new Vector3(0f, sz.y + 0.03f, 0f), Mathf.Min(sz.x, sz.z) * 0.35f, 0.06f, 10);
                if (big) mb.Cylinder(m, MetalDark, p + new Vector3(sz.x * 0.25f, sz.y + 0.03f, 0f), sz.z * 0.3f, 0.06f, 10);
            }
            // water tank (mid-rise, the New York roofline)
            if (r.Height > 22f && r.Height < 95f && rng.NextDouble() < 0.38)
            {
                var p = P(0.3 + 0.4 * rng.NextDouble(), 0.3 + 0.4 * rng.NextDouble());
                float legs = 2.2f;
                foreach (var lx in Legs)
                    foreach (var lz in Legs)
                        mb.Box(m, MetalDark, p + new Vector3(lx, legs * 0.5f, lz), new Vector3(0.18f, legs, 0.18f));
                mb.Box(m, MetalDark, p + new Vector3(0f, legs, 0f), new Vector3(3.2f, 0.15f, 3.2f));
                mb.Cylinder(m, Wood, p + new Vector3(0f, legs + 1.45f, 0f), 1.55f, 2.8f, 12);
                mb.Cylinder(m, MetalDark, p + new Vector3(0f, legs + 0.5f, 0f), 1.58f, 0.08f, 12);   // hoops
                mb.Cylinder(m, MetalDark, p + new Vector3(0f, legs + 2.3f, 0f), 1.58f, 0.08f, 12);
                mb.Cone(m, MetalDark, p + new Vector3(0f, legs + 2.85f, 0f), 1.7f, 0.9f, 12);
            }
            // antenna masts with red beacons (tall buildings)
            if (r.Height > 55f)
            {
                int masts = 1 + (rng.NextDouble() < 0.5 ? 1 : 0) + (r.Height > 120f ? 1 : 0);
                for (int i = 0; i < masts; i++)
                {
                    var p = P(rng.NextDouble(), rng.NextDouble());
                    float h = 5f + (float)rng.NextDouble() * (r.Height > 120f ? 14f : 7f);
                    mb.Cylinder(m, Metal, p + new Vector3(0f, h * 0.5f, 0f), 0.11f, h, 6);
                    mb.Box(m, Metal, p + new Vector3(0f, h * 0.55f, 0f), new Vector3(0.9f, 0.06f, 0.06f));   // cross bar
                    mb.Box(m, Beacon, p + new Vector3(0f, h + 0.18f, 0f), new Vector3(0.36f, 0.36f, 0.36f));
                }
            }
        }

        static void Ensure()
        {
            if (mats != null) return;
            mats = new Material[Slots];
            mats[Concrete] = Lit("RoofConcrete", new Color(0.46f, 0.46f, 0.44f), 0.2f, 0f);
            mats[Metal] = Lit("RoofMetal", new Color(0.6f, 0.62f, 0.64f), 0.5f, 0.6f);
            mats[MetalDark] = Lit("RoofMetalDark", new Color(0.1f, 0.1f, 0.11f), 0.35f, 0.5f);
            mats[Wood] = Lit("RoofTankWood", new Color(0.3f, 0.2f, 0.13f), 0.15f, 0f);
            mats[Beacon] = Lit("RoofBeacon", new Color(0.4f, 0.03f, 0.02f), 0.4f, 0f);
            mats[RoofGravel] = Lit("RoofGravel", new Color(0.52f, 0.5f, 0.46f), 0.08f, 0f);
            mats[RoofWhite] = Lit("RoofWhiteMembrane", new Color(0.78f, 0.79f, 0.8f), 0.35f, 0f);
            mats[RoofGreen] = Lit("RoofGreen", new Color(0.2f, 0.3f, 0.14f), 0.05f, 0f);
            mats[Solar] = Lit("RoofSolar", new Color(0.04f, 0.07f, 0.14f), 0.85f, 0.3f);
        }

        static Material Lit(string name, Color c, float smooth, float metal)
        {
            var m = RuntimeMaterials.NewLit(name);
            if (m == null) return null;
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            return m;
        }
    }

    /// <summary>Accumulates transformed boxes, cylinders and cones into per-material submeshes (flat normals, outward faces).</summary>
    public sealed class ProcMeshBuilder
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector3> n = new List<Vector3>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int>[] tris;
        static readonly Vector3[] Dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        public ProcMeshBuilder(int slots)
        {
            tris = new List<int>[slots];
            for (int i = 0; i < slots; i++) tris[i] = new List<int>();
        }

        void Quad(Matrix4x4 m, int slot, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            int i0 = v.Count;
            Vector3 wn = m.MultiplyVector(normal).normalized;
            v.Add(m.MultiplyPoint3x4(a)); v.Add(m.MultiplyPoint3x4(b)); v.Add(m.MultiplyPoint3x4(c)); v.Add(m.MultiplyPoint3x4(d));
            n.Add(wn); n.Add(wn); n.Add(wn); n.Add(wn);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(1, 0));
            var fn = Vector3.Cross(c - a, d - b);
            var t = tris[slot];
            if (Vector3.Dot(fn, normal) >= 0f) { t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3); }
            else { t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1); t.Add(i0); t.Add(i0 + 3); t.Add(i0 + 2); }
        }

        public void Box(Matrix4x4 m, int slot, Vector3 c, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            foreach (var d in Dirs)
            {
                Vector3 u = Mathf.Abs(d.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 w = Vector3.Cross(d, u);
                Vector3 fc = c + Vector3.Scale(d, h);
                Vector3 du = Vector3.Scale(u, h), dw = Vector3.Scale(w, h);
                Quad(m, slot, fc - du - dw, fc + du - dw, fc + du + dw, fc - du + dw, d);
            }
        }

        public void Cylinder(Matrix4x4 m, int slot, Vector3 c, float r, float height, int sides)
        {
            Vector3 ax = Vector3.up * (height * 0.5f);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * r), p1 = new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * r);
                Quad(m, slot, c + p0 - ax, c + p0 + ax, c + p1 + ax, c + p1 - ax, ((p0 + p1) * 0.5f).normalized);
                Quad(m, slot, c + ax, c + ax, c + p1 + ax, c + p0 + ax, Vector3.up);
                Quad(m, slot, c - ax, c - ax, c + p0 - ax, c + p1 - ax, Vector3.down);
            }
        }

        /// <summary>A cone standing on c (base radius r, apex height h above it).</summary>
        public void Cone(Matrix4x4 m, int slot, Vector3 c, float r, float h, int sides)
        {
            Vector3 apex = c + Vector3.up * h;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 p0 = c + new Vector3(Mathf.Cos(a0) * r, 0f, Mathf.Sin(a0) * r), p1 = c + new Vector3(Mathf.Cos(a1) * r, 0f, Mathf.Sin(a1) * r);
                Vector3 mid = ((p0 + p1) * 0.5f - c).normalized;
                Vector3 nrm = (mid * h + Vector3.up * r).normalized;
                Quad(m, slot, p0, apex, apex, p1, nrm);
            }
        }

        /// <summary>Empties the builder for reuse (keeps the lists' capacity).</summary>
        public void Clear()
        {
            v.Clear(); n.Clear(); uv.Clear();
            foreach (var t in tris) t.Clear();
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = tris.Length;
            for (int i = 0; i < tris.Length; i++) mesh.SetTriangles(tris[i], i);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
