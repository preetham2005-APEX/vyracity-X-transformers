using System;
using System.Collections.Generic;
using UnityEngine;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.AI
{
    /// <summary>
    /// Cars parked along the kerbs of city streets (collector, service and arterial roads), the GTA V street-life cue
    /// the iteration-1 captures were missing. Slots are generated once from the road network and are deterministic
    /// (the same car, model and colour is there every time you come back); only slots near the player are
    /// instantiated, from a pool. Parked cars are kinematic until something hits them, then they become physics
    /// bodies and get shoved like in GTA.
    /// </summary>
    public sealed class ParkedCars : MonoBehaviour
    {
        public GameObject[] Prefabs = Array.Empty<GameObject>();
        public VehicleController Player;
        public bool Active = true;
        public float SpawnRadius = 140f, DespawnRadius = 175f;
        public int MaxCars = 70;
        [Range(0f, 1f)] public float Occupancy = 0.42f;
        public float Spacing = 10.5f;

        static readonly HashSet<string> ParkingClasses = new HashSet<string> { "CO", "SV", "AR" };
        static readonly Color[] Paints =
        {
            new Color(0.75f, 0.75f, 0.78f), new Color(0.08f, 0.08f, 0.09f), new Color(0.9f, 0.9f, 0.92f), new Color(0.45f, 0.06f, 0.06f),
            new Color(0.10f, 0.18f, 0.38f), new Color(0.32f, 0.34f, 0.36f), new Color(0.55f, 0.48f, 0.36f), new Color(0.12f, 0.28f, 0.22f),
        };

        struct Slot { public Vector3 Pos; public Vector3 Dir; public int Model; public Color Paint; }
        readonly List<Slot> slots = new List<Slot>();
        readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
        readonly Dictionary<int, GameObject> live = new Dictionary<int, GameObject>();
        readonly Dictionary<int, Stack<GameObject>> pools = new Dictionary<int, Stack<GameObject>>();
        const float Cell = 100f;
        bool built;
        float timer;

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        void BuildSlots()
        {
            built = true;
            if (Prefabs == null || Prefabs.Length == 0) return;
            var roads = WorldData.Roads;
            for (int r = 0; r < roads.Count; r++)
            {
                var road = roads[r];
                if (!ParkingClasses.Contains(road.Data.cls) || road.Path.Closed) continue;
                if (road.Data.width < 7f) continue;      // too narrow to park and still drive
                float len = road.Path.Length;
                int k = 0;
                for (float s = 22f; s < len - 22f; s += Spacing, k++)
                {
                    var p = road.Path.PositionAt(s);
                    var t = road.Path.TangentAt(s);
                    var right = new Vector3(t.z, 0f, -t.x).normalized;
                    int hint = 0;
                    road.Path.Project(p, ref hint, 0);
                    if (road.StructureAt(hint) != StructureKind.None) continue;   // no parking on bridges or in tunnels
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var rng = new System.Random(r * 7919 + k * 31 + side * 3);
                        if (rng.NextDouble() > Occupancy) continue;
                        var pos = p + right * side * (road.HalfWidth - 1.15f);
                        if (NearJunction(pos, right * side, t, r)) continue;
                        var dir = side > 0 ? t : -t;                        // right-hand traffic: parked with the flow
                        int idx = slots.Count;
                        slots.Add(new Slot { Pos = pos, Dir = new Vector3(dir.x, 0f, dir.z).normalized, Model = rng.Next(Prefabs.Length), Paint = Paints[rng.Next(Paints.Length)] });
                        long key = Key(Mathf.FloorToInt(pos.x / Cell), Mathf.FloorToInt(pos.z / Cell));
                        if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                        list.Add(idx);
                    }
                }
            }
            Debug.Log($"[ParkedCars] {slots.Count} kerbside slots on {roads.Count} roads");
        }

        /// <summary>Another road close to the slot (sideways or along the kerb) means a junction or a driveway: skip.</summary>
        static bool NearJunction(Vector3 pos, Vector3 outward, Vector3 along, int road)
        {
            foreach (var probe in new[] { pos + outward * 4f, pos + along * 9f, pos - along * 9f })
                if (WorldData.NearestRoad(probe, 6f, out var hit) && hit.Road != road) return true;
            return false;
        }

        void Update()
        {
            if (!Active || Player == null) { if (live.Count > 0) ClearAll(); return; }
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = 0.4f;
            if (!built) BuildSlots();
            Vector3 focus = Player.transform.position;
            // recycle far cars
            var gone = new List<int>();
            foreach (var kv in live)
                if (kv.Value == null || (kv.Value.transform.position - focus).sqrMagnitude > DespawnRadius * DespawnRadius) gone.Add(kv.Key);
            foreach (int idx in gone) Recycle(idx);
            // fill near slots
            int cx = Mathf.FloorToInt(focus.x / Cell), cz = Mathf.FloorToInt(focus.z / Cell);
            int reach = Mathf.CeilToInt(SpawnRadius / Cell);
            for (int x = cx - reach; x <= cx + reach && live.Count < MaxCars; x++)
                for (int z = cz - reach; z <= cz + reach && live.Count < MaxCars; z++)
                {
                    if (!grid.TryGetValue(Key(x, z), out var list)) continue;
                    foreach (int idx in list)
                    {
                        if (live.Count >= MaxCars) break;
                        if (live.ContainsKey(idx)) continue;
                        var sl = slots[idx];
                        if ((sl.Pos - focus).sqrMagnitude > SpawnRadius * SpawnRadius) continue;
                        if (Mathf.Abs(sl.Pos.y - focus.y) > 25f) continue;
                        live[idx] = Spawn(sl);
                    }
                }
        }

        GameObject Spawn(Slot sl)
        {
            if (!pools.TryGetValue(sl.Model, out var pool)) pools[sl.Model] = pool = new Stack<GameObject>();
            GameObject go;
            if (pool.Count > 0) go = pool.Pop();
            else
            {
                go = Instantiate(Prefabs[sl.Model], transform);
                if (go.GetComponentInChildren<Collider>() == null) go.AddComponent<BoxCollider>().size = new Vector3(1.8f, 1.4f, 4.4f);
                var rb = go.GetComponent<Rigidbody>();
                if (rb == null) rb = go.AddComponent<Rigidbody>();
                rb.mass = 1350f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                go.AddComponent<ParkedCar>();
            }
            var body = go.GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            Vector3 p = sl.Pos;
            p.y = WorldData.GroundHeight(p + Vector3.up * 3f, p.y) + 0.02f;
            go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(sl.Dir, Vector3.up));
            body.position = go.transform.position;
            body.rotation = go.transform.rotation;
            go.SetActive(true);
            Paint(go, sl.Paint);
            return go;
        }

        void Recycle(int idx)
        {
            if (!live.TryGetValue(idx, out var go)) return;
            live.Remove(idx);
            if (go == null) return;
            go.SetActive(false);
            if (!pools.TryGetValue(slots[idx].Model, out var pool)) pools[slots[idx].Model] = pool = new Stack<GameObject>();
            pool.Push(go);
        }

        void ClearAll()
        {
            foreach (int idx in new List<int>(live.Keys)) Recycle(idx);
        }

        // Created lazily: Unity forbids creating engine objects in a MonoBehaviour's static/field initialisers (BUG-091).
        static MaterialPropertyBlock block;

        /// <summary>Tints paint materials only (M_Paint_*, Traffic_Body*), per material slot.</summary>
        static void Paint(GameObject go, Color c)
        {
            block ??= new MaterialPropertyBlock();
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !(mats[i].name.StartsWith("M_Paint") || mats[i].name.StartsWith("Traffic_Body"))) continue;
                    block.Clear();
                    block.SetColor("_BaseColor", c);
                    r.SetPropertyBlock(block, i);
                }
            }
        }
    }
}
