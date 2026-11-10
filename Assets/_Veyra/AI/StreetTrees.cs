using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.AI
{
    /// <summary>
    /// Street trees along city sidewalks (GTA's tree-lined avenues; our kerbs were bare). Uses the broadleaf tree
    /// prototypes the world builder already made for <see cref="InstancedPropRenderer"/>, places them every ~14 m on
    /// both sides of collector and arterial roads (skipping junctions, bridges and building footprints), and draws
    /// the ones near the player with GPU instancing. No colliders, like the other instanced trees (BUG-043).
    /// </summary>
    public sealed class StreetTrees : MonoBehaviour
    {
        public VehicleController Player;
        public float Spacing = 14f, DrawRadius = 260f;
        [Tooltip("Trees nearer than this cast shadows and use the full mesh; farther ones use the low LOD without shadows")]
        public float NearRadius = 85f;
        public bool Active = true;

        static readonly HashSet<string> TreeClasses = new HashSet<string> { "CO", "AR" };
        readonly Dictionary<long, List<Matrix4x4>> grid = new Dictionary<long, List<Matrix4x4>>();
        readonly List<Matrix4x4> visible = new List<Matrix4x4>();
        readonly List<Matrix4x4> far = new List<Matrix4x4>();
        readonly Matrix4x4[] batch = new Matrix4x4[1023];
        InstancedPropRenderer.Prototype proto;
        const float Cell = 100f;
        bool built;
        float timer;
        static readonly RaycastHit[] hits = new RaycastHit[8];

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        void Build()
        {
            built = true;
            var ipr = InstancedPropRenderer.Instance;
            if (ipr == null) return;
            foreach (var p in ipr.Prototypes)
            {
                if (p == null || p.Mesh == null || p.IsLamp) continue;
                string n = p.Name != null ? p.Name.ToLowerInvariant() : "";
                if (!n.Contains("tree") || n.Contains("pine") || n.Contains("conifer") || n.Contains("palm")) continue;
                proto = p;
                break;
            }
            if (proto == null) { Debug.Log("[StreetTrees] no broadleaf tree prototype found"); return; }
            var roads = WorldData.Roads;
            int count = 0;
            for (int r = 0; r < roads.Count; r++)
            {
                var road = roads[r];
                if (!TreeClasses.Contains(road.Data.cls) || road.Path.Closed) continue;
                float len = road.Path.Length;
                int k = 0;
                for (float s = 18f; s < len - 18f; s += Spacing, k++)
                {
                    var p = road.Path.PositionAt(s);
                    var t = road.Path.TangentAt(s);
                    var right = new Vector3(t.z, 0f, -t.x).normalized;
                    int hint = 0;
                    road.Path.Project(p, ref hint, 0);
                    if (road.StructureAt(hint) != StructureKind.None) continue;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var rng = new System.Random(r * 4099 + k * 17 + side);
                        if (rng.NextDouble() < 0.15) continue;   // a few gaps (driveways, shop fronts)
                        var pos = p + right * side * (road.HalfWidth + 2.4f);
                        if (NearOtherRoad(pos, t, r)) continue;
                        if (!Ground(pos, out var ground)) continue;
                        float scale = 0.8f + (float)rng.NextDouble() * 0.45f;
                        var m = Matrix4x4.TRS(ground, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), Vector3.one * scale);
                        long key = Key(Mathf.FloorToInt(ground.x / Cell), Mathf.FloorToInt(ground.z / Cell));
                        if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<Matrix4x4>();
                        list.Add(m);
                        count++;
                    }
                }
            }
            Debug.Log($"[StreetTrees] {count} street trees ({proto.Name})");
        }

        static bool NearOtherRoad(Vector3 pos, Vector3 along, int road)
        {
            foreach (var probe in new[] { pos, pos + along * 8f, pos - along * 8f })
                if (WorldData.NearestRoad(probe, 5f, out var hit) && hit.Road != road) return true;
            return false;
        }

        /// <summary>Ground under the point; false inside a building footprint (UBX collider) or over water.</summary>
        static bool Ground(Vector3 pos, out Vector3 ground)
        {
            ground = pos;
            int n = Physics.RaycastNonAlloc(pos + Vector3.up * 80f, Vector3.down, hits, 200f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Collider top = null;
            for (int i = 0; i < n; i++)
                if (hits[i].distance < best) { best = hits[i].distance; top = hits[i].collider; ground = hits[i].point; }
            if (top == null) return false;
            if (top.name.StartsWith("UBX") || top.name.StartsWith("UCX")) return false;
            return ground.y > 0.5f;
        }

        void Update()
        {
            if (!Active || Player == null) return;
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                timer = 0.5f;
                if (!built) Build();
                visible.Clear();
                far.Clear();
                if (proto != null)
                {
                    Vector3 f = Player.transform.position;
                    int cx = Mathf.FloorToInt(f.x / Cell), cz = Mathf.FloorToInt(f.z / Cell);
                    int reach = Mathf.CeilToInt(DrawRadius / Cell);
                    float r2 = DrawRadius * DrawRadius, n2 = NearRadius * NearRadius;
                    for (int x = cx - reach; x <= cx + reach; x++)
                        for (int z = cz - reach; z <= cz + reach; z++)
                            if (grid.TryGetValue(Key(x, z), out var list))
                                foreach (var m in list)
                                {
                                    Vector3 p = m.GetColumn(3);
                                    float d2 = (p - f).sqrMagnitude;
                                    if (d2 < n2) visible.Add(m);
                                    else if (d2 < r2) far.Add(m);
                                }
                }
            }
            if (proto == null) return;
            var bounds = new Bounds(Player.transform.position, Vector3.one * (DrawRadius * 2.2f));
            // iteration 4: shadows and the full mesh only near the car (iteration 3 drew ~1,500 full trees into every
            // shadow cascade: p99 25 → 50 ms)
            Draw(proto.Mesh, proto.Materials, visible, ShadowCastingMode.On, bounds);
            bool low = proto.LowMesh != null && proto.LowMaterials != null && proto.LowMaterials.Length > 0;
            Draw(low ? proto.LowMesh : proto.Mesh, low ? proto.LowMaterials : proto.Materials, far, ShadowCastingMode.Off, bounds);
        }

        void Draw(Mesh mesh, Material[] mats, List<Matrix4x4> list, ShadowCastingMode shadows, Bounds bounds)
        {
            if (mesh == null || mats == null || list.Count == 0) return;
            for (int sub = 0; sub < mesh.subMeshCount && sub < mats.Length; sub++)
            {
                var mat = mats[sub];
                if (mat == null) continue;
                var rp = new RenderParams(mat) { shadowCastingMode = shadows, receiveShadows = true, worldBounds = bounds };
                for (int start = 0; start < list.Count; start += batch.Length)
                {
                    int count = Mathf.Min(batch.Length, list.Count - start);
                    list.CopyTo(start, batch, 0, count);
                    Graphics.RenderMeshInstanced(rp, mesh, sub, batch, count);
                }
            }
        }
    }
}
