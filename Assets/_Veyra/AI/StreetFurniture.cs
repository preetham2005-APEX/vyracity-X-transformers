using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.AI
{
    /// <summary>
    /// Street furniture (visual iteration 7; GTA V's pavements are full of it, ours had trees and lamps only): fire hydrants,
    /// litter bins, parking meters and mailboxes on the kerb line; benches and newspaper boxes at the back of the pavement;
    /// bus shelters with a lit advertising panel on arterials; street-name signs at the block ends; zebra crossings and stop
    /// lines at every at-grade junction mouth (painted road markings, a few cm above the asphalt); manhole covers in the
    /// lanes, a quarter of them venting steam (Liberty City / Night City streets; a fixed pool of emitters follows the
    /// nearest vents). All procedural geometry
    /// (no third-party models, no real brands: the ad panels are abstract colour fields). Placement is deterministic from the
    /// road data; the props of each 100 m cell are merged into one mesh (a submesh per material) and built lazily near the
    /// player, so a whole block costs a handful of SRP-batched draws. Decorative only: no colliders (like the street trees).
    /// </summary>
    public sealed class StreetFurniture : MonoBehaviour
    {
        public VehicleController Player;
        public bool Active = true;
        public float BuildRadius = 260f, DropRadius = 400f;

        enum Kind { Hydrant, Bin, Meter, Mailbox, Bench, NewsBox, Shelter, Sign, Crosswalk, StopLine, Manhole, Arrow }
        struct Prop { public Kind Kind; public Vector3 Pos; public float Yaw; public int Variant; }

        // material slots (submesh index)
        const int MetalDark = 0, Red = 1, Green = 2, Wood = 3, Glass = 4, SignGreen = 5, Yellow = 6, Blue = 7, AdPanel = 8, MetalLight = 9, Paint = 10, SlotCount = 11;

        static readonly HashSet<string> Classes = new HashSet<string> { "CO", "AR", "SV" };
        const float Cell = 100f;
        readonly Dictionary<long, List<Prop>> grid = new Dictionary<long, List<Prop>>();
        readonly Dictionary<long, GameObject> builtCells = new Dictionary<long, GameObject>();
        static Material[] mats;
        static Material adMat;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        bool placed;
        float timer;
        readonly List<Vector3> vents = new List<Vector3>();
        /// <summary>Bus shelters (position, yaw facing the road): pedestrians wait at them.</summary>
        public readonly List<(Vector3 Pos, float Yaw)> Shelters = new List<(Vector3, float)>();
        public bool Placed => placed;
        readonly List<ParticleSystem> steam = new List<ParticleSystem>();
        readonly List<(float d, Vector3 p)> nearVents = new List<(float, Vector3)>();
        static Material steamMat;
        float ventTimer;
        public int SteamEmitters = 8;
        public float SteamRadius = 140f;
        int propCount;
        static readonly RaycastHit[] hits = new RaycastHit[8];

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        // ------------------------------------------------------------------ placement

        void Place()
        {
            placed = true;
            var roads = WorldData.Roads;
            for (int r = 0; r < roads.Count; r++)
            {
                var road = roads[r];
                if (!Classes.Contains(road.Data.cls) || road.Path.Closed || road.Path.Length < 50f) continue;
                float len = road.Path.Length;
                bool arterial = road.Data.cls == "AR";
                if (road.Data.width >= 7f)
                    for (int end = 0; end < 2; end++)
                    {
                        Vector3 pe = road.Path.PositionAt(end == 0 ? 0f : len);
                        Vector3 te = road.Path.TangentAt(end == 0 ? 2f : len - 2f);
                        Vector3 d = end == 0 ? -te : te;                      // travel direction toward this end
                        d.y = 0f;
                        if (d.sqrMagnitude < 1e-4f) continue;
                        d.Normalize();
                        if (!WorldData.NearestRoad(pe + d * 9f, 9f, out var jh) || jh.Road == r || Mathf.Abs(jh.Point.y - pe.y) > 3f) continue;
                        var rightE = new Vector3(d.z, 0f, -d.x);
                        float yawE = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                        int wDm = Mathf.RoundToInt(road.Data.width * 10f);
                        float stop = TrafficLights.StopLineDistance(WorldData.Roads[jh.Road].HalfWidth);
                        if (stop > len * 0.4f) continue;   // very short block
                        float zebra = stop - 3f;            // the crossing sits between the stop line and the junction
                        Add(Kind.Crosswalk, road.Path.PositionAt(end == 0 ? zebra : len - zebra), yawE, wDm);
                        // stop line across the arriving (right-hand) lanes, where traffic waits at a red
                        Add(Kind.StopLine, road.Path.PositionAt(end == 0 ? stop : len - stop) + rightE * (road.HalfWidth * 0.5f), yawE, Mathf.RoundToInt(road.HalfWidth * 10f));
                        // painted lane arrows 12 m before the stop line, one per approach lane (round 2)
                        float arrowS = stop + 12f;
                        if (arrowS < len * 0.45f)
                        {
                            int lanes = Mathf.Max(1, road.Data.lanes);
                            for (int ln = 0; ln < lanes; ln++)
                                Add(Kind.Arrow, road.Path.PositionAt(end == 0 ? arrowS : len - arrowS) + rightE * (1.75f + 3.5f * ln), yawE, 0);
                        }
                    }
                int k = 0;
                for (float s = 16f; s < len - 16f; s += 9f, k++)
                {
                    int hint = 0;
                    var p = road.Path.PositionAt(s);
                    hint = road.Path.SegmentAt(s);   // binary search (iteration 9)
                    if (road.StructureAt(hint) != StructureKind.None) continue;
                    var t = road.Path.TangentAt(s);
                    t.y = 0f;
                    t.Normalize();
                    var right = new Vector3(t.z, 0f, -t.x);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var rng = new System.Random(r * 92821 + k * 131 + side * 7);
                        float faceRoad = Mathf.Atan2(-right.x * side, -right.z * side) * Mathf.Rad2Deg;   // props face the road
                        float along = Mathf.Atan2(t.x, t.z) * Mathf.Rad2Deg;
                        // street-name signs at both ends of the block
                        if (k == 0 || s + 9f >= len - 16f)
                        {
                            Add(Kind.Sign, p + right * side * (road.HalfWidth + 0.6f), along, 0);
                            continue;
                        }
                        var kerb = p + right * side * (road.HalfWidth + 0.55f);
                        var back = p + right * side * (road.HalfWidth + 3.7f);
                        if (NearOtherRoad(kerb, r)) continue;
                        double a = rng.NextDouble();
                        if (a < 0.05) Add(Kind.Hydrant, kerb, faceRoad, 0);
                        else if (a < 0.15) Add(Kind.Bin, kerb, faceRoad, 0);
                        else if (a < 0.24 && !arterial) Add(Kind.Meter, kerb, faceRoad, 0);
                        else if (a < 0.27) Add(Kind.Mailbox, kerb, faceRoad, 0);
                        if (arterial && side > 0 && k % 26 == 7)
                        {
                            Add(Kind.Shelter, p + right * side * (road.HalfWidth + 2.9f), faceRoad, rng.Next(4));
                            Shelters.Add((p + right * side * (road.HalfWidth + 2.9f), faceRoad));
                            continue;
                        }
                        double b = rng.NextDouble();
                        if (b < 0.08) Add(Kind.Bench, back, faceRoad, 0);
                        else if (b < 0.115) Add(Kind.NewsBox, back, faceRoad, rng.Next(2));
                        // a manhole cover in the lane every ~60 m on each side; one in four vents steam
                        if (road.Data.cls != "SV" && rng.NextDouble() < 0.14)
                        {
                            var mh = p + right * side * (road.HalfWidth * (0.25f + 0.35f * (float)rng.NextDouble()));
                            bool steam = rng.NextDouble() < 0.25;
                            Add(Kind.Manhole, mh, along, steam ? 1 : 0);
                            if (steam) vents.Add(mh);
                        }
                    }
                }
            }
            Debug.Log($"[StreetFurniture] {propCount} props placed");
        }

        void Add(Kind kind, Vector3 pos, float yaw, int variant)
        {
            long key = Key(Mathf.FloorToInt(pos.x / Cell), Mathf.FloorToInt(pos.z / Cell));
            if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<Prop>();
            list.Add(new Prop { Kind = kind, Pos = pos, Yaw = yaw, Variant = variant });
            propCount++;
        }

        static bool NearOtherRoad(Vector3 pos, int road) => WorldData.NearestRoad(pos, 3.5f, out var hit) && hit.Road != road;

        // ------------------------------------------------------------------ streaming

        void Update()
        {
            long fc0 = VeyraCity.Core.FrameCost.Start();
            UpdateBody();
            VeyraCity.Core.FrameCost.Stop("StreetFurniture", fc0);
        }

        void UpdateBody()
        {
            // iteration 10: build the slot tables right after the scene loads (behind the menu), on staggered frames,
            // instead of on the first frame of free roam
            if (!placed && Active && Time.frameCount > 7) Place();
            if (!Active || Player == null) return;
            if (adMat != null)
            {
                float night = WorldEnvironment.Instance != null ? WorldEnvironment.Instance.NightFactor : 0f;
                adMat.SetColor(EmissionId, new Color(1f, 1f, 1f) * (0.25f + 2.4f * night));
            }
            UpdateSteam();
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = 0.2f;
            if (!placed) Place();
            Vector3 f = Player.transform.position;
            // drop far cells
            List<long> drop = null;
            foreach (var kv in builtCells)
            {
                int cx = (int)(kv.Key >> 32), cz = (int)(uint)kv.Key;
                var c = new Vector3((cx + 0.5f) * Cell, f.y, (cz + 0.5f) * Cell);
                if ((c - f).sqrMagnitude > DropRadius * DropRadius) (drop ??= new List<long>()).Add(kv.Key);
            }
            if (drop != null)
                foreach (var key in drop)
                {
                    var go = builtCells[key];
                    if (go != null) { Destroy(go.GetComponent<MeshFilter>().sharedMesh); Destroy(go); }
                    builtCells.Remove(key);
                }
            // build near cells, two per tick
            int fx = Mathf.FloorToInt(f.x / Cell), fz = Mathf.FloorToInt(f.z / Cell);
            int reach = Mathf.CeilToInt(BuildRadius / Cell);
            int budget = 1;   // iteration 9: one cell per tick (cell builds showed up in long-drive hitches)
            for (int x = fx - reach; x <= fx + reach && budget > 0; x++)
                for (int z = fz - reach; z <= fz + reach && budget > 0; z++)
                {
                    long key = Key(x, z);
                    if (builtCells.ContainsKey(key)) continue;
                    var c = new Vector3((x + 0.5f) * Cell, f.y, (z + 0.5f) * Cell);
                    if ((c - f).sqrMagnitude > BuildRadius * BuildRadius) continue;
                    builtCells[key] = grid.TryGetValue(key, out var list) ? BuildCell(key, list) : null;
                    budget--;
                }
        }

        GameObject BuildCell(long key, List<Prop> props)
        {
            Ensure();
            var mb = new MeshBuilder();
            int n = 0;
            foreach (var p in props)
            {
                if (!Ground(p.Pos, out float gy)) continue;
                var m = Matrix4x4.TRS(new Vector3(p.Pos.x, gy, p.Pos.z), Quaternion.Euler(0f, p.Yaw, 0f), Vector3.one);
                Emit(mb, p, m);
                n++;
            }
            if (n == 0) return null;
            var go = new GameObject("Furniture_" + key);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("FurnitureCell");
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.Off;
            return go;
        }

        /// <summary>Steam plumes on the vents nearest the player (pooled emitters; dimmed with the light level, the mist
        /// material is unlit; warmer at night, lit by sodium street lamps).</summary>
        void UpdateSteam()
        {
            if (!placed || vents.Count == 0) return;
            if (steam.Count == 0) BuildSteam();
            float night = WorldEnvironment.Instance != null ? WorldEnvironment.Instance.NightFactor : 0f;
            ventTimer -= Time.deltaTime;
            if (ventTimer <= 0f)
            {
                ventTimer = 0.5f;
                nearVents.Clear();
                Vector3 f = Player.transform.position;
                foreach (var v in vents)
                {
                    float d = (v - f).sqrMagnitude;
                    if (d < SteamRadius * SteamRadius) nearVents.Add((d, v));
                }
                nearVents.Sort((a, b) => a.d.CompareTo(b.d));
                for (int i = 0; i < steam.Count; i++)
                {
                    var ps = steam[i];
                    if (i < nearVents.Count)
                    {
                        var v = nearVents[i].p;
                        if (Ground(v, out float gy)) v.y = gy;
                        if ((ps.transform.position - v).sqrMagnitude > 0.25f) { ps.Clear(); ps.transform.position = v + Vector3.up * 0.05f; }
                        if (!ps.isPlaying) ps.Play();
                    }
                    else if (ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
            Vector3 wind = VeyraGlobals.WindStrength > 0f && !float.IsNaN(VeyraGlobals.WindDirection.x) ? VeyraGlobals.WindDirection * (0.4f + 1.6f * VeyraGlobals.WindStrength) : new Vector3(0.3f, 0f, 0.1f);
            Color c = Color.Lerp(new Color(0.85f, 0.86f, 0.88f, 0.32f), new Color(0.42f, 0.38f, 0.34f, 0.3f), night);
            foreach (var ps in steam)
            {
                var main = ps.main;
                main.startColor = c;
                var force = ps.forceOverLifetime;
                force.x = wind.x;
                force.z = wind.z;
            }
        }

        void BuildSteam()
        {
            if (steamMat == null)
            {
                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SteamPuff" };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                        float a = Mathf.Pow(Mathf.Clamp01(1f - dist), 1.8f) * (0.65f + 0.35f * Mathf.PerlinNoise(x * 0.11f, y * 0.11f));
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                tex.Apply();
                steamMat = RuntimeMaterials.NewParticle("ManholeSteam", false);
                if (steamMat != null) steamMat.SetTexture("_BaseMap", tex);
            }
            for (int i = 0; i < SteamEmitters; i++)
            {
                var go = new GameObject("ManholeSteam" + i);
                go.transform.SetParent(transform, false);
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = true;
                main.playOnAwake = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 80;
                main.gravityModifier = -0.02f;
                var em = ps.emission;
                em.rateOverTime = 14f;
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 12f;
                shape.radius = 0.25f;
                shape.rotation = new Vector3(-90f, 0f, 0f);   // up
                var size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 3.6f));
                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
                var col = ps.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                var force = ps.forceOverLifetime;
                force.enabled = true;
                force.space = ParticleSystemSimulationSpace.World;
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.sharedMaterial = steamMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.sortingFudge = 2f;
                steam.Add(ps);
            }
        }

        static bool Ground(Vector3 pos, out float y)
        {
            y = pos.y;
            int n = Physics.RaycastNonAlloc(pos + Vector3.up * 30f, Vector3.down, hits, 60f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Collider top = null;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].rigidbody != null) continue;   // cars
                if (hits[i].distance < best) { best = hits[i].distance; top = hits[i].collider; y = hits[i].point.y; }
            }
            if (top == null) return false;
            if (top.name.StartsWith("UBX") || top.name.StartsWith("UCX")) return false;   // inside a building footprint
            return Mathf.Abs(y - pos.y) < 2.5f;
        }

        // ------------------------------------------------------------------ geometry

        static void Emit(MeshBuilder mb, Prop p, Matrix4x4 m)
        {
            switch (p.Kind)
            {
                case Kind.Hydrant:
                    mb.Cylinder(m, MetalDark, new Vector3(0f, 0.03f, 0f), 0.16f, 0.06f, 10);
                    mb.Cylinder(m, Red, new Vector3(0f, 0.33f, 0f), 0.12f, 0.54f, 10);
                    mb.Cylinder(m, Red, new Vector3(0f, 0.63f, 0f), 0.14f, 0.07f, 10);
                    mb.Cylinder(m, Red, new Vector3(0f, 0.7f, 0f), 0.06f, 0.08f, 8);
                    mb.Box(m, Red, new Vector3(0f, 0.42f, 0f), new Vector3(0.36f, 0.08f, 0.08f));
                    mb.Box(m, Red, new Vector3(0f, 0.42f, 0.12f), new Vector3(0.1f, 0.1f, 0.1f));
                    break;
                case Kind.Bin:
                    mb.Cylinder(m, Green, new Vector3(0f, 0.43f, 0f), 0.25f, 0.86f, 10);
                    mb.Cylinder(m, MetalDark, new Vector3(0f, 0.89f, 0f), 0.27f, 0.06f, 10);
                    break;
                case Kind.Meter:
                    mb.Cylinder(m, MetalDark, new Vector3(0f, 0.55f, 0f), 0.035f, 1.1f, 6);
                    mb.Box(m, MetalLight, new Vector3(0f, 1.24f, 0f), new Vector3(0.18f, 0.28f, 0.14f));
                    mb.Box(m, Glass, new Vector3(0f, 1.3f, 0.072f), new Vector3(0.12f, 0.1f, 0.01f));
                    break;
                case Kind.Mailbox:
                    mb.Box(m, Blue, new Vector3(0f, 0.62f, 0f), new Vector3(0.48f, 0.62f, 0.45f));
                    mb.Cylinder(m, Blue, new Vector3(0f, 0.93f, 0f), 0.24f, 0.45f, 10, true);
                    mb.Box(m, MetalDark, new Vector3(0f, 0.15f, 0f), new Vector3(0.4f, 0.3f, 0.36f));
                    break;
                case Kind.Bench:
                    mb.Box(m, Wood, new Vector3(0f, 0.46f, 0.02f), new Vector3(1.7f, 0.05f, 0.44f));
                    mb.Box(m, Wood, new Vector3(0f, 0.78f, -0.2f), new Vector3(1.7f, 0.32f, 0.05f));
                    mb.Box(m, MetalDark, new Vector3(-0.72f, 0.4f, 0f), new Vector3(0.06f, 0.8f, 0.46f));
                    mb.Box(m, MetalDark, new Vector3(0.72f, 0.4f, 0f), new Vector3(0.06f, 0.8f, 0.46f));
                    break;
                case Kind.NewsBox:
                    mb.Box(m, p.Variant == 0 ? Yellow : Blue, new Vector3(0f, 0.55f, 0f), new Vector3(0.46f, 0.8f, 0.4f));
                    mb.Box(m, Glass, new Vector3(0f, 0.72f, 0.205f), new Vector3(0.36f, 0.3f, 0.01f));
                    mb.Box(m, MetalDark, new Vector3(0f, 0.08f, 0f), new Vector3(0.4f, 0.16f, 0.34f));
                    break;
                case Kind.Shelter:
                {
                    mb.Box(m, MetalDark, new Vector3(0f, 2.42f, 0f), new Vector3(3.4f, 0.1f, 1.5f));
                    mb.Box(m, Glass, new Vector3(0f, 1.3f, -0.68f), new Vector3(3.1f, 2.0f, 0.03f));
                    mb.Box(m, Glass, new Vector3(-1.62f, 1.3f, -0.05f), new Vector3(0.03f, 2.0f, 1.2f));
                    mb.Box(m, AdPanel, new Vector3(1.62f, 1.3f, -0.05f), new Vector3(0.12f, 1.8f, 1.15f));
                    foreach (var x in new[] { -1.65f, 1.65f })
                        foreach (var z in new[] { -0.7f, 0.6f })
                            mb.Box(m, MetalLight, new Vector3(x, 1.2f, z), new Vector3(0.07f, 2.4f, 0.07f));
                    mb.Box(m, MetalLight, new Vector3(0f, 0.46f, -0.45f), new Vector3(2.3f, 0.05f, 0.38f));
                    mb.Box(m, MetalDark, new Vector3(0f, 0.23f, -0.45f), new Vector3(2.1f, 0.46f, 0.05f));
                    break;
                }
                case Kind.Crosswalk:
                {
                    // stripes run along the road, laid across its width (zebra)
                    float w = p.Variant * 0.1f - 1f;
                    for (float x = -w * 0.5f + 0.3f; x <= w * 0.5f - 0.3f; x += 1.1f)
                        mb.Box(m, Paint, new Vector3(x, 0.035f, 0f), new Vector3(0.55f, 0.02f, 3.2f));
                    break;
                }
                case Kind.Arrow:
                {
                    // straight-ahead arrow: shaft and a chevron head pointing along +z (the travel direction)
                    mb.Box(m, Paint, new Vector3(0f, 0.035f, -0.5f), new Vector3(0.2f, 0.02f, 2.8f));
                    var head = Matrix4x4.TRS(new Vector3(0f, 0f, 0.95f), Quaternion.identity, Vector3.one);
                    mb.Box(m * head * Matrix4x4.Rotate(Quaternion.Euler(0f, 38f, 0f)), Paint, new Vector3(0f, 0.035f, -0.45f), new Vector3(0.22f, 0.02f, 1.1f));
                    mb.Box(m * head * Matrix4x4.Rotate(Quaternion.Euler(0f, -38f, 0f)), Paint, new Vector3(0f, 0.035f, -0.45f), new Vector3(0.22f, 0.02f, 1.1f));
                    break;
                }
                case Kind.StopLine:
                    mb.Box(m, Paint, new Vector3(0f, 0.035f, 0f), new Vector3(Mathf.Max(1f, p.Variant * 0.1f - 0.4f), 0.02f, 0.45f));
                    break;
                case Kind.Manhole:
                    mb.Cylinder(m, MetalDark, new Vector3(0f, 0.012f, 0f), 0.36f, 0.024f, 14);
                    mb.Box(m, MetalLight, new Vector3(0f, 0.026f, 0f), new Vector3(0.5f, 0.004f, 0.05f));
                    mb.Box(m, MetalLight, new Vector3(0f, 0.026f, 0f), new Vector3(0.05f, 0.004f, 0.5f));
                    break;
                case Kind.Sign:
                    mb.Cylinder(m, MetalDark, new Vector3(0f, 1.5f, 0f), 0.04f, 3f, 6);
                    mb.Box(m, SignGreen, new Vector3(0f, 2.85f, 0f), new Vector3(0.03f, 0.22f, 0.95f));
                    mb.Box(m, SignGreen, new Vector3(0f, 2.6f, 0f), new Vector3(0.95f, 0.22f, 0.03f));
                    break;
            }
        }

        static void Ensure()
        {
            if (mats != null) return;
            mats = new Material[SlotCount];
            mats[MetalDark] = Lit("FurnMetalDark", new Color(0.09f, 0.1f, 0.11f), 0.45f, 0.6f);
            mats[Red] = Lit("FurnRed", new Color(0.62f, 0.07f, 0.05f), 0.55f, 0f);
            mats[Green] = Lit("FurnGreen", new Color(0.08f, 0.22f, 0.14f), 0.4f, 0.2f);
            mats[Wood] = Lit("FurnWood", new Color(0.4f, 0.26f, 0.15f), 0.25f, 0f);
            mats[SignGreen] = Lit("FurnSign", new Color(0.03f, 0.33f, 0.16f), 0.6f, 0f);
            mats[Yellow] = Lit("FurnYellow", new Color(0.85f, 0.62f, 0.08f), 0.5f, 0f);
            mats[Blue] = Lit("FurnBlue", new Color(0.08f, 0.18f, 0.5f), 0.5f, 0.1f);
            mats[MetalLight] = Lit("FurnMetalLight", new Color(0.55f, 0.57f, 0.6f), 0.6f, 0.8f);
            mats[Paint] = Lit("FurnRoadPaint", new Color(0.78f, 0.78f, 0.76f), 0.35f, 0f);
            var glass = RuntimeMaterials.NewUnlitTransparent("FurnGlass", false);
            if (glass != null) glass.SetColor("_BaseColor", new Color(0.55f, 0.68f, 0.75f, 0.28f));
            mats[Glass] = glass;
            adMat = Lit("FurnAdPanel", new Color(0.9f, 0.9f, 0.9f), 0.85f, 0f);
            if (adMat != null)
            {
                // an abstract poster: diagonal colour fields, no text or brand
                const int w = 32, h = 64;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "AdPanelTex", wrapMode = TextureWrapMode.Clamp };
                var c0 = new Color(0.95f, 0.25f, 0.55f); var c1 = new Color(0.15f, 0.55f, 0.95f); var c2 = new Color(0.98f, 0.8f, 0.3f);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float d = (x + y * 0.6f) / (w + h * 0.6f);
                        var col = d < 0.4f ? Color.Lerp(c1, c0, d / 0.4f) : d < 0.7f ? c0 : Color.Lerp(c0, c2, (d - 0.7f) / 0.3f);
                        if (y > h * 0.78f) col = Color.Lerp(col, Color.white, 0.6f);
                        tex.SetPixel(x, y, col);
                    }
                tex.Apply();
                adMat.SetTexture("_BaseMap", tex);
                adMat.SetTexture("_EmissionMap", tex);
                adMat.SetColor(EmissionId, Color.white * 0.25f);
            }
            mats[AdPanel] = adMat;
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

        /// <summary>Accumulates transformed boxes and cylinders into per-material submeshes.</summary>
        sealed class MeshBuilder
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector3> n = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int>[] tris = new List<int>[SlotCount];

            public MeshBuilder() { for (int i = 0; i < SlotCount; i++) tris[i] = new List<int>(); }

            void Quad(Matrix4x4 m, int slot, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            {
                int i0 = v.Count;
                Vector3 wn = m.MultiplyVector(normal).normalized;
                v.Add(m.MultiplyPoint3x4(a)); v.Add(m.MultiplyPoint3x4(b)); v.Add(m.MultiplyPoint3x4(c)); v.Add(m.MultiplyPoint3x4(d));
                n.Add(wn); n.Add(wn); n.Add(wn); n.Add(wn);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(1, 0));
                // outward winding: front face toward +normal
                var fn = Vector3.Cross(c - a, d - b);   // diagonals: robust for the degenerate cap quads (a == b)
                var t = tris[slot];
                if (Vector3.Dot(fn, normal) >= 0f) { t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3); }
                else { t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1); t.Add(i0); t.Add(i0 + 3); t.Add(i0 + 2); }
            }

            static readonly Vector3[] Dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

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

            public void Cylinder(Matrix4x4 m, int slot, Vector3 c, float r, float height, int sides, bool sideways = false)
            {
                // sideways: axis along x (the mailbox's rounded top), otherwise along y
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                    Vector3 p0 = Ring(a0, r), p1 = Ring(a1, r);
                    Vector3 ax = sideways ? Vector3.right * (height * 0.5f) : Vector3.up * (height * 0.5f);
                    if (sideways) { p0 = new Vector3(0f, p0.x, p0.z); p1 = new Vector3(0f, p1.x, p1.z); }
                    Vector3 mid = ((p0 + p1) * 0.5f).normalized;
                    Quad(m, slot, c + p0 - ax, c + p0 + ax, c + p1 + ax, c + p1 - ax, mid);
                    // caps
                    Quad(m, slot, c + ax, c + ax, c + p1 + ax, c + p0 + ax, ax.normalized);
                    Quad(m, slot, c - ax, c - ax, c + p0 - ax, c + p1 - ax, -ax.normalized);
                }
            }

            static Vector3 Ring(float a, float r) => new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                if (v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(v);
                mesh.SetNormals(n);
                mesh.SetUVs(0, uv);
                mesh.subMeshCount = SlotCount;
                for (int i = 0; i < SlotCount; i++) mesh.SetTriangles(tris[i], i);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
