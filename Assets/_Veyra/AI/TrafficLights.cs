using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.AI
{
    /// <summary>
    /// Traffic signals at city junctions, generated at runtime from the road network (no scene edits): wherever a
    /// collector, arterial or service road ends on another road, a mast-arm signal stands on the right-hand kerb facing
    /// the arriving traffic, with a second head on the pole. Red / amber / green lamps cycle (alternating between the
    /// approaches of a junction) and glow at night, a strong GTA street cue. Only signals near the player exist (pooled).
    /// Purely visual for now: traffic AI does not stop for them yet (card G2).
    /// </summary>
    public sealed class TrafficLights : MonoBehaviour
    {
        public VehicleController Player;
        public bool Active = true;
        public float SpawnRadius = 230f, DespawnRadius = 270f;
        public int MaxSignals = 48;
        public float Cycle = 26f, Green = 11f, Amber = 3f;

        static readonly HashSet<string> SignalClasses = new HashSet<string> { "CO", "AR", "SV" };
        struct Site { public Vector3 Pos; public Vector3 Facing; public float Arm; public float Offset; }
        readonly List<Site> sites = new List<Site>();
        readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
        readonly Dictionary<int, Signal> live = new Dictionary<int, Signal>();
        readonly Stack<Signal> pool = new Stack<Signal>();
        const float Cell = 120f;
        bool built;
        float timer;
        MaterialPropertyBlock block;

        static Mesh poleMesh, quad;
        static Material metalMat, lampMat;

        sealed class Signal
        {
            public GameObject Root;
            public Transform Arm, Head1, Head2;
            public Renderer[] Lamps;   // head1 R,A,G then head2 R,A,G
            public int Site;
        }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        void BuildSites()
        {
            built = true;
            var roads = WorldData.Roads;
            for (int r = 0; r < roads.Count; r++)
            {
                var road = roads[r];
                if (!SignalClasses.Contains(road.Data.cls) || road.Path.Closed) continue;
                float len = road.Path.Length;
                for (int end = 0; end < 2; end++)
                {
                    float s = end == 0 ? 0f : len;
                    Vector3 p = road.Path.PositionAt(s);
                    Vector3 t = road.Path.TangentAt(end == 0 ? 2f : len - 2f);
                    Vector3 d = (end == 0 ? -t : t);                    // travel direction toward this end
                    d.y = 0f;
                    if (d.sqrMagnitude < 1e-4f) continue;
                    d.Normalize();
                    // a different road just past the end = an at-grade junction
                    if (!WorldData.NearestRoad(p + d * 9f, 9f, out var hit) || hit.Road == r) continue;
                    if (Mathf.Abs(hit.Point.y - p.y) > 3f) continue;  // grade-separated: no signal
                    var right = new Vector3(d.z, 0f, -d.x);
                    var pos = p - d * 7f + right * (road.HalfWidth + 0.9f);
                    int idx = sites.Count;
                    // approaches of the same junction alternate their phase by half a cycle
                    long jkey = Key(Mathf.RoundToInt(p.x / 25f), Mathf.RoundToInt(p.z / 25f));
                    float offset = (Mathf.Abs(Vector3.Dot(d, Vector3.right)) > 0.7f ? 0f : Cycle * 0.5f) + (jkey & 7) * 0.37f;
                    sites.Add(new Site { Pos = pos, Facing = -d, Arm = Mathf.Clamp(road.HalfWidth - 0.6f, 2.5f, 6.5f), Offset = offset });
                    long key = Key(Mathf.FloorToInt(pos.x / Cell), Mathf.FloorToInt(pos.z / Cell));
                    if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                    list.Add(idx);
                }
            }
            Debug.Log($"[TrafficLights] {sites.Count} signals at junctions");
        }

        static void EnsureAssets()
        {
            if (poleMesh != null) return;
            metalMat = RuntimeMaterials.NewLit("SignalMetal");
            metalMat.SetColor("_BaseColor", new Color(0.11f, 0.12f, 0.12f));
            metalMat.SetFloat("_Metallic", 0.6f);
            metalMat.SetFloat("_Smoothness", 0.45f);
            metalMat.SetColor("_EmissionColor", Color.black);
            // unit parts combined into one mesh: pole (5.8 m), base plate
            var parts = new List<CombineInstance>();
            var cyl = PrimitiveMesh(PrimitiveType.Cylinder);
            var cube = PrimitiveMesh(PrimitiveType.Cube);
            parts.Add(new CombineInstance { mesh = cyl, transform = Matrix4x4.TRS(new Vector3(0f, 2.9f, 0f), Quaternion.identity, new Vector3(0.18f, 2.9f, 0.18f)) });
            parts.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(new Vector3(0f, 0.05f, 0f), Quaternion.identity, new Vector3(0.4f, 0.1f, 0.4f)) });
            poleMesh = new Mesh { name = "SignalPole" };
            poleMesh.CombineMeshes(parts.ToArray(), true, true);
            lampMat = RuntimeMaterials.NewParticle("SignalLamp", true);
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SignalLampTex" };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                    float a = Mathf.Clamp01(1f - d * 1.6f) + Mathf.Pow(Mathf.Clamp01(1f - d), 3f) * 0.6f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
                }
            tex.Apply();
            lampMat.SetTexture("_BaseMap", tex);
            quad = new Mesh { name = "SignalLampQuad" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            quad.triangles = new[] { 0, 1, 2, 2, 1, 3, 0, 2, 1, 2, 3, 1 };    // double-sided
            quad.RecalculateBounds();
        }

        static Mesh PrimitiveMesh(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            var m = go.GetComponent<MeshFilter>().sharedMesh;
            Destroy(go);
            return m;
        }

        Signal Create()
        {
            EnsureAssets();
            var s = new Signal { Root = new GameObject("TrafficSignal") };
            s.Root.transform.SetParent(transform, false);
            AddMesh(s.Root.transform, poleMesh, Vector3.zero, Vector3.one);
            s.Root.AddComponent<CapsuleCollider>().radius = 0.14f;
            var cc = s.Root.GetComponent<CapsuleCollider>();
            cc.height = 5.8f;
            cc.center = new Vector3(0f, 2.9f, 0f);
            // mast arm over the road (local +X), head at its tip, second head on the pole
            s.Arm = new GameObject("Arm").transform;
            s.Arm.SetParent(s.Root.transform, false);
            AddMesh(s.Arm, PrimitiveMesh(PrimitiveType.Cube), new Vector3(0.5f, 0f, 0f), new Vector3(1f, 0.12f, 0.12f));
            s.Head1 = Head(s.Root.transform, "Head1");
            s.Head2 = Head(s.Root.transform, "Head2");
            var lamps = new List<Renderer>();
            foreach (var h in new[] { s.Head1, s.Head2 })
                for (int k = 0; k < 3; k++)
                {
                    var lg = new GameObject("Lamp" + k);
                    lg.transform.SetParent(h, false);
                    lg.transform.localPosition = new Vector3(0f, 0.32f - k * 0.32f, 0.19f);
                    lg.transform.localScale = Vector3.one * 0.5f;
                    lg.AddComponent<MeshFilter>().sharedMesh = quad;
                    var lr = lg.AddComponent<MeshRenderer>();
                    lr.sharedMaterial = lampMat;
                    lr.shadowCastingMode = ShadowCastingMode.Off;
                    lr.receiveShadows = false;
                    lamps.Add(lr);
                }
            s.Lamps = lamps.ToArray();
            return s;
        }

        static Transform Head(Transform parent, string name)
        {
            var h = new GameObject(name).transform;
            h.SetParent(parent, false);
            AddMesh(h, PrimitiveMesh(PrimitiveType.Cube), Vector3.zero, new Vector3(0.38f, 1.05f, 0.32f));
            return h;
        }

        static void AddMesh(Transform parent, Mesh mesh, Vector3 pos, Vector3 scale)
        {
            var g = new GameObject("Mesh");
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = metalMat;
            r.shadowCastingMode = ShadowCastingMode.On;
        }

        void Place(Signal s, Site site)
        {
            float gy = WorldData.GroundHeight(site.Pos + Vector3.up * 4f, site.Pos.y);
            s.Root.transform.SetPositionAndRotation(new Vector3(site.Pos.x, gy, site.Pos.z), Quaternion.LookRotation(site.Facing, Vector3.up));
            s.Arm.localPosition = new Vector3(0f, 5.55f, 0f);
            s.Arm.localScale = new Vector3(site.Arm, 1f, 1f);
            s.Head1.localPosition = new Vector3(site.Arm - 0.4f, 4.95f, 0f);
            s.Head2.localPosition = new Vector3(0f, 3.0f, 0.2f);
            s.Root.SetActive(true);
        }

        void Update()
        {
            if (!Active || Player == null)
            {
                foreach (var kv in live) { kv.Value.Root.SetActive(false); pool.Push(kv.Value); }
                live.Clear();
                return;
            }
            Vector3 focus = Player.transform.position;
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                timer = 0.5f;
                if (!built) BuildSites();
                var gone = new List<int>();
                foreach (var kv in live)
                    if ((sites[kv.Key].Pos - focus).sqrMagnitude > DespawnRadius * DespawnRadius) gone.Add(kv.Key);
                foreach (int k in gone) { live[k].Root.SetActive(false); pool.Push(live[k]); live.Remove(k); }
                int cx = Mathf.FloorToInt(focus.x / Cell), cz = Mathf.FloorToInt(focus.z / Cell);
                int reach = Mathf.CeilToInt(SpawnRadius / Cell);
                for (int x = cx - reach; x <= cx + reach; x++)
                    for (int z = cz - reach; z <= cz + reach; z++)
                    {
                        if (!grid.TryGetValue(Key(x, z), out var list)) continue;
                        foreach (int idx in list)
                        {
                            if (live.Count >= MaxSignals || live.ContainsKey(idx)) continue;
                            if ((sites[idx].Pos - focus).sqrMagnitude > SpawnRadius * SpawnRadius) continue;
                            var sig = pool.Count > 0 ? pool.Pop() : Create();
                            sig.Site = idx;
                            Place(sig, sites[idx]);
                            live[idx] = sig;
                        }
                    }
            }
            // lamp states
            block ??= new MaterialPropertyBlock();
            float night = WorldEnvironment.Instance != null ? WorldEnvironment.Instance.NightFactor : 0f;
            float boost = 1.6f + night * 2.4f;
            Color red = new Color(1f, 0.08f, 0.05f), amber = new Color(1f, 0.55f, 0.05f), green = new Color(0.15f, 1f, 0.45f);
            foreach (var kv in live)
            {
                float t = Mathf.Repeat(Time.time + sites[kv.Key].Offset, Cycle);
                int state = t < Green ? 2 : t < Green + Amber ? 1 : 0;   // 0 red, 1 amber, 2 green
                var lamps = kv.Value.Lamps;
                for (int i = 0; i < lamps.Length; i++)
                {
                    int k = i % 3;   // 0 red (top), 1 amber, 2 green
                    bool on = k == (state == 0 ? 0 : state == 1 ? 1 : 2);
                    Color c = k == 0 ? red : k == 1 ? amber : green;
                    block.SetColor("_BaseColor", on ? c * boost : c * 0.04f);
                    lamps[i].SetPropertyBlock(block);
                }
            }
        }
    }
}
