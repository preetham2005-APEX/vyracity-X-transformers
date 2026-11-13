using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.AI
{
    /// <summary>
    /// Sidewalk life (GTA V's busiest street-level cue; our pavements were empty): walkers on the sidewalks of collector,
    /// arterial and service roads around the player. Procedural low-poly figures (no third-party models): torso, hips, head,
    /// hair, arms and legs with a walk cycle, varied heights, builds and clothing colours. They stroll along the pavement,
    /// turn at the end of the block, and dive clear of a fast car heading at them (Need for Speed style: never run over).
    /// Pooled; anchors are precomputed every 12 m of pavement in a 100 m grid. Materials: a small palette of
    /// <see cref="RuntimeMaterials.NewLit"/> copies shared by every walker (SRP-batched, no property blocks).
    /// </summary>
    public sealed class Pedestrians : MonoBehaviour
    {
        public VehicleController Player;
        public bool Active = true;
        public int MaxPeds = 85;
        /// <summary>Zebra crossings begun since start (capture telemetry).</summary>
        public static int CrossingsStarted;   // iteration 8: in-run A/B put 70 walkers at ~0.5 ms
        public float SpawnMin = 30f, SpawnMax = 150f, DespawnRadius = 185f;

        static readonly HashSet<string> Classes = new HashSet<string> { "CO", "AR", "SV" };
        static readonly Color[] Shirts =
        {
            new Color(0.75f, 0.12f, 0.12f), new Color(0.12f, 0.22f, 0.5f), new Color(0.9f, 0.9f, 0.88f), new Color(0.1f, 0.1f, 0.11f),
            new Color(0.95f, 0.75f, 0.2f), new Color(0.2f, 0.45f, 0.3f), new Color(0.55f, 0.35f, 0.6f), new Color(0.85f, 0.45f, 0.55f),
            new Color(0.45f, 0.5f, 0.55f), new Color(0.3f, 0.6f, 0.75f),
        };
        static readonly Color[] Trousers =
        {
            new Color(0.12f, 0.15f, 0.25f), new Color(0.08f, 0.08f, 0.08f), new Color(0.4f, 0.36f, 0.3f), new Color(0.3f, 0.32f, 0.35f),
            new Color(0.55f, 0.5f, 0.42f),
        };
        static readonly Color[] Skins = { new Color(0.95f, 0.78f, 0.65f), new Color(0.82f, 0.62f, 0.47f), new Color(0.6f, 0.42f, 0.3f), new Color(0.38f, 0.26f, 0.19f) };
        static readonly Color[] Hairs = { new Color(0.08f, 0.06f, 0.05f), new Color(0.3f, 0.2f, 0.12f), new Color(0.75f, 0.6f, 0.35f), new Color(0.55f, 0.55f, 0.55f) };

        struct Anchor { public int Road; public float S; public float Lateral; }

        sealed class Ped
        {
            public GameObject Go;
            public Transform Root, Hips, Torso, Head, LegL, LegR, ArmL, ArmR;
            public Renderer Shirt, Trousers, Skin, Hair, ArmLR, ArmRR, LegLR, LegRR, ShoeL, ShoeR;
            public Transform Umbrella;
            public bool WantsUmbrella;
            public bool Idle;            // waiting at a bus shelter
            public Vector3 IdlePos;
            public float IdleYaw;
            public int Road;
            public float S, Lateral, Dir, Speed, Phase, Height, Ground, GroundTimer;
            public float DodgeTime, PendingDt;
            public bool Crossing;          // on a zebra crossing while the road's traffic waits at red
            public float CrossT, CrossCooldown;
            public int Slot;
            public Vector3 DodgeVel, DodgeOffset;
            // world iteration 16: strolling round a plaza fountain (ring between the benches and the planters)
            public bool Stroll;
            public Vector3 StrollC;
            public float StrollR, StrollTh, StrollDir;
        }

        readonly List<Vector3> plazas = new List<Vector3>();

        readonly List<Anchor> anchors = new List<Anchor>();
        readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
        readonly List<Ped> live = new List<Ped>();
        readonly Stack<Ped> pool = new Stack<Ped>();
        readonly List<int> candidates = new List<int>();
        static Mesh box, sphere, canopy, torsoMesh, limbMesh, hipsMesh, shoeMesh;
        static Material[] shirtMats, trouserMats, skinMats, hairMats, umbrellaMats;
        static Material shoeMat;
        const float Cell = 100f;
        bool built;
        IEnumerator buildJob;
        /// <summary>Anchor positions tested between yields (BUG-170): each one costs a road query and a raycast.</summary>
        const int YieldEvery = 4;
        int frame;
        float createCooldown;
        float spawnTimer;
        static float Wet;
        static readonly RaycastHit[] hits = new RaycastHit[8];

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        /// <summary>
        /// Builds the pavement anchors a few positions at a time (BUG-170). The whole-city build cost 88 ms on one
        /// frame; the work is identical, only spread by <see cref="FrameBudget.Pump"/>. Anchors are re-sampled near
        /// the player every tick, so a partly built list simply spawns fewer people for the first seconds.
        /// </summary>
        IEnumerator BuildSteps()
        {
            var roads = WorldData.Roads;
            int sinceYield = 0;
            for (int r = 0; r < roads.Count; r++)
            {
                var road = roads[r];
                if (!Classes.Contains(road.Data.cls) || road.Path.Closed || road.Path.Length < 40f) continue;
                for (float s = 15f; s < road.Path.Length - 15f; s += 12f)
                {
                    if (++sinceYield >= YieldEvery) { sinceYield = 0; yield return null; }
                    int hint = 0;
                    var p = road.Path.PositionAt(s);
                    hint = road.Path.SegmentAt(s);   // binary search (iteration 9)
                    if (road.StructureAt(hint) != StructureKind.None) continue;   // no pavements on bridges or in tunnels
                    var t = road.Path.TangentAt(s);
                    var right = new Vector3(t.z, 0f, -t.x).normalized;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float lat = side * (road.HalfWidth + 1.3f);
                        var q = p + right * lat;
                        if (WorldData.NearestRoad(q, 3.5f, out var hit) && hit.Road != r) continue;   // junction mouth
                        anchors.Add(new Anchor { Road = r, S = s, Lateral = lat });
                        long key = Key(Mathf.FloorToInt(q.x / Cell), Mathf.FloorToInt(q.z / Cell));
                        if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                        list.Add(anchors.Count - 1);
                    }
                }
            }
            // world iteration 16: plaza centres (registry landmarks named Plaza; PlazaBuilder dresses them)
            foreach (var l in WorldData.Locations)
                if (l != null && l.category == "Landmark" && l.name != null && l.name.Contains("Plaza")) plazas.Add(l.Position);
            built = true;
            Debug.Log($"[Pedestrians] {anchors.Count} pavement anchors, {plazas.Count} plazas");
        }

        void Update()
        {
            long fc0 = VeyraCity.Core.FrameCost.Start();
            UpdateBody();
            VeyraCity.Core.FrameCost.Stop("Pedestrians", fc0);
        }

        void UpdateBody()
        {
            // iteration 10: build the slot tables right after the scene loads (behind the menu), on staggered frames,
            // instead of on the first frame of free roam
            // BUG-170: pump the build within a frame budget instead of doing it all on one frame.
            if (!built && Active && Time.frameCount > 9)
            {
                if (buildJob == null) buildJob = BuildSteps();
                if (!VeyraCity.Core.FrameBudget.Pump(buildJob)) buildJob = null;
            }
            if (!Active || Player == null)
            {
                if (live.Count > 0) { foreach (var p in live) Recycle(p); live.Clear(); }
                return;
            }
            Vector3 focus = Player.transform.position;
            // world iteration 16: fixed review shots park the car 400 m below the map and photo/free cameras leave it behind:
            // people live around the streaming focus a review forced, or around the camera, when either is far from the car
            var forced = VeyraCity.World.Streaming.CellStreamer.ForcedFocus;
            if (forced.HasValue && (forced.Value - focus).sqrMagnitude > 200f * 200f) focus = forced.Value;
            else
            {
                var mainCam = Camera.main;
                if (mainCam != null && (mainCam.transform.position - focus).sqrMagnitude > 200f * 200f) focus = mainCam.transform.position;
            }
            float dt = Time.deltaTime;
            Wet = WorldEnvironment.Instance != null ? Mathf.Max(WorldEnvironment.Instance.Wetness, WorldEnvironment.Instance.RainIntensity) : 0f;
            frame++;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var p = live[i];
                float d2 = (p.Root.position - focus).sqrMagnitude;
                if (d2 > DespawnRadius * DespawnRadius) { Recycle(p); live.RemoveAt(i); continue; }
                // iteration 8: walkers beyond 80 m update every third frame (same motion, a third of the cost)
                p.PendingDt += dt;
                if (d2 > 80f * 80f && (frame + p.Slot) % 3 != 0) continue;
                Step(p, p.PendingDt);
                p.PendingDt = 0f;
            }
            spawnTimer -= dt;
            createCooldown -= dt;
            // world iteration 4: after a teleport (map jump, respawn, review camera) fill the new street at once from the pool,
            // from 8 m out, instead of one walker per 0.08 s from 30 m (reviews never showed people)
            bool jumped = hasLastFocus && (focus - lastFocus).sqrMagnitude > 150f * 150f;
            lastFocus = focus; hasLastFocus = true;
            if (jumped && anchors.Count > 0)
            {
                float min = SpawnMin;
                SpawnMin = 8f;
                // the 0.25 s creation limit would leave a fresh pool at one or two figures: a teleport may hitch once
                for (int k = 0; k < MaxPeds && live.Count < MaxPeds; k++) { createCooldown = 0f; TrySpawn(focus); }
                SpawnMin = min;
                int strolling = 0; foreach (var o in live) if (o.Stroll) strolling++;
                Debug.Log($"[Pedestrians] refill at {focus:F0}: {live.Count} live, {strolling} on a plaza");
            }
            if (spawnTimer <= 0f && live.Count < MaxPeds && anchors.Count > 0)
            {
                spawnTimer = 0.08f;
                TrySpawn(focus);
            }
        }

        Vector3 lastFocus;
        bool hasLastFocus;

        /// <summary>
        /// World iteration 16 (CAM_05: the furnished Crown Plaza had nobody on it): up to 16 people per plaza within reach,
        /// three in four strolling the ring between the benches (11 m) and the planters (18 m), the rest standing at the
        /// fountain rim looking in.
        /// </summary>
        bool TrySpawnPlaza(Vector3 focus)
        {
            foreach (var c in plazas)
            {
                float d = Vector3.Distance(new Vector3(c.x, focus.y, c.z), focus);
                if (d > SpawnMax + 20f) continue;
                int here = 0;
                foreach (var o in live) if ((o.Stroll || o.Idle) && (new Vector3(o.Root.position.x - c.x, 0f, o.Root.position.z - c.z)).sqrMagnitude < 30f * 30f) here++;
                if (here >= 16) continue;
                float th = Random.value * Mathf.PI * 2f;
                bool stroll = Random.value < 0.75f;
                float r = stroll ? Random.Range(13f, 16.5f) : Random.Range(7.6f, 8.2f);
                var pos = new Vector3(c.x + Mathf.Cos(th) * r, c.y, c.z + Mathf.Sin(th) * r);
                if (!Ground(pos, out float gy)) continue;
                foreach (var o in live) if ((o.Root.position - pos).sqrMagnitude < 1.5f) return false;
                var p = pool.Count > 0 ? pool.Pop() : Create();
                Dress(p);
                p.Road = -1;
                p.Phase = Random.value * 10f;
                p.DodgeTime = 0f; p.DodgeOffset = Vector3.zero; p.PendingDt = 0f;
                p.Slot = Random.Range(0, 3);
                p.Ground = gy; p.GroundTimer = Random.value;
                p.Crossing = false;
                if (stroll)
                {
                    p.Idle = false; p.Stroll = true;
                    p.StrollC = c; p.StrollR = r; p.StrollTh = th; p.StrollDir = Random.value < 0.5f ? 1f : -1f;
                    p.Speed = Random.Range(0.85f, 1.3f);
                }
                else
                {
                    p.Idle = true; p.Stroll = false;
                    p.IdlePos = new Vector3(pos.x, gy, pos.z);
                    p.IdleYaw = Mathf.Atan2(c.x - pos.x, c.z - pos.z) * Mathf.Rad2Deg + Random.Range(-30f, 30f);
                    p.Speed = 0f;
                }
                p.Go.SetActive(true);
                live.Add(p);
                Step(p, 0f);
                return true;
            }
            return false;
        }

        /// <summary>World iteration 16: one stroller step round the plaza ring (walk cycle as on the pavement).</summary>
        void StepStroll(Ped p, float dt)
        {
            p.StrollTh += p.StrollDir * p.Speed / Mathf.Max(1f, p.StrollR) * dt;
            var pos = p.StrollC + new Vector3(Mathf.Cos(p.StrollTh), 0f, Mathf.Sin(p.StrollTh)) * p.StrollR;
            p.GroundTimer -= dt;
            if (p.GroundTimer <= 0f) { p.GroundTimer = 1.2f; if (Ground(pos, out float gy)) p.Ground = gy; }
            pos.y = p.Ground;
            var face = new Vector3(-Mathf.Sin(p.StrollTh), 0f, Mathf.Cos(p.StrollTh)) * p.StrollDir;
            if (dt > 0f) p.Root.rotation = Quaternion.Slerp(p.Root.rotation, Quaternion.LookRotation(face), 1f - Mathf.Exp(-dt * 8f));
            else p.Root.rotation = Quaternion.LookRotation(face);
            p.Root.position = pos;
            p.Phase += dt * p.Speed * 5.2f;
            float sw = Mathf.Sin(p.Phase);
            p.LegL.localRotation = Quaternion.Euler(26f * sw, 0f, 0f);
            p.LegR.localRotation = Quaternion.Euler(-26f * sw, 0f, 0f);
            p.ArmL.localRotation = Quaternion.Euler(-22f * sw, 0f, -4f);
            bool umbrella = p.WantsUmbrella && Wet > 0.3f;
            if (p.Umbrella.gameObject.activeSelf != umbrella) p.Umbrella.gameObject.SetActive(umbrella);
            p.ArmR.localRotation = umbrella ? Quaternion.Euler(-70f, 0f, 8f) : Quaternion.Euler(22f * sw, 0f, 4f);
            if (umbrella) p.Umbrella.localRotation = Quaternion.Euler(45f, 0f, -8f);
            p.Hips.localPosition = new Vector3(0f, p.Height * 0.53f + Mathf.Abs(Mathf.Cos(p.Phase)) * 0.035f, 0f);
            p.Torso.localRotation = Quaternion.Euler(2f, 0f, 0f);
        }

        StreetFurniture furniture;

        /// <summary>Iteration 9: one in six spawns is somebody waiting at a nearby bus shelter (GTA's bus stops).</summary>
        bool TrySpawnWaiting(Vector3 focus)
        {
            if (furniture == null) furniture = FindAnyObjectByType<StreetFurniture>();
            if (furniture == null || !furniture.Placed || furniture.Shelters.Count == 0) return false;
            var sh = furniture.Shelters[Random.Range(0, furniture.Shelters.Count)];
            float d = Vector3.Distance(sh.Pos, focus);
            if (d < SpawnMin || d > SpawnMax) return false;
            var face = Quaternion.Euler(0f, sh.Yaw, 0f);
            var pos = sh.Pos + face * new Vector3(Random.Range(-1.2f, 1.2f), 0f, Random.Range(0.1f, 0.6f));
            int waiting = 0;
            foreach (var o in live)
            {
                if (!o.Idle) continue;
                float od = (o.IdlePos - pos).sqrMagnitude;
                if (od < 0.7f * 0.7f) return false;
                if (od < 3f * 3f) waiting++;
            }
            if (waiting >= 3 || !Ground(pos, out float gy)) return false;
            var p = pool.Count > 0 ? pool.Pop() : Create();
            Dress(p);
            p.Idle = true;
            p.IdlePos = new Vector3(pos.x, gy, pos.z);
            p.IdleYaw = sh.Yaw + Random.Range(-25f, 25f);
            p.Road = -1;
            p.Phase = Random.value * 10f;
            p.Speed = 0f;
            p.DodgeTime = 0f;
            p.DodgeOffset = Vector3.zero;
            p.PendingDt = 0f;
            p.Slot = Random.Range(0, 3);
            p.Go.SetActive(true);
            live.Add(p);
            Step(p, 0f);
            return true;
        }

        void TrySpawn(Vector3 focus)
        {
            // building a new figure (13 objects) is the costly part: one per 0.25 s, pooled walkers any time (iteration 9)
            if (pool.Count == 0)
            {
                if (createCooldown > 0f) return;
                createCooldown = 0.25f;
            }
            if (Random.value < 0.17f && TrySpawnWaiting(focus)) return;
            if (plazas.Count > 0 && TrySpawnPlaza(focus)) return;
            candidates.Clear();
            int cx = Mathf.FloorToInt(focus.x / Cell), cz = Mathf.FloorToInt(focus.z / Cell);
            int reach = Mathf.CeilToInt(SpawnMax / Cell);
            for (int x = cx - reach; x <= cx + reach; x++)
                for (int z = cz - reach; z <= cz + reach; z++)
                    if (grid.TryGetValue(Key(x, z), out var list)) candidates.AddRange(list);
            if (candidates.Count == 0) return;
            var cam = Camera.main;
            for (int tries = 0; tries < 6; tries++)
            {
                var a = anchors[candidates[Random.Range(0, candidates.Count)]];
                var road = WorldData.Roads[a.Road];
                var pos = Lateral(road, a.S, a.Lateral);
                float d = Vector3.Distance(pos, focus);
                if (d < SpawnMin || d > SpawnMax) continue;
                if (cam != null && d < 70f)
                {
                    var vp = cam.WorldToViewportPoint(pos);
                    if (vp.z > 0f && vp.x > -0.05f && vp.x < 1.05f && vp.y > -0.05f && vp.y < 1.05f) continue;   // no popping in close in view
                }
                if (!Ground(pos, out float gy)) continue;
                // world iteration 17 (CAM_15: a downtown-sized crowd on the container-port quay): pavement density per district
                if (Random.value > DistrictDensity(pos)) continue;
                bool crowded = false;
                foreach (var o in live) if ((o.Root.position - pos).sqrMagnitude < 4f) { crowded = true; break; }
                if (crowded) continue;
                var p = pool.Count > 0 ? pool.Pop() : Create();
                Dress(p);
                p.Idle = false;
                p.Road = a.Road;
                p.S = a.S + Random.Range(-5f, 5f);
                p.Lateral = a.Lateral + Mathf.Sign(a.Lateral) * Random.Range(-0.5f, 2.2f);
                p.Dir = Random.value < 0.5f ? 1f : -1f;
                // iteration 15: a quarter of the walkers start a few metres short of a signalled crossing, heading to it
                // (GTA's corners are where people gather; mid-block walkers rarely reach one in a short scene)
                if (Random.value < 0.25f && TrafficLights.Instance != null)
                {
                    int end = Random.value < 0.5f ? 0 : 1;
                    if (TrafficLights.Instance.StateFor(a.Road, end, out float stop) >= 0)
                    {
                        float len = road.Path.Length;
                        float zebraS = end == 0 ? stop - 3f : len - (stop - 3f);
                        float back = Random.Range(2f, 9f);
                        p.S = end == 0 ? zebraS + back : zebraS - back;
                        p.Dir = end == 0 ? -1f : 1f;
                    }
                }
                p.Speed = Random.Range(1.05f, 1.6f);
                p.Phase = Random.value * 10f;
                p.Ground = gy;
                p.GroundTimer = Random.value;
                p.DodgeTime = 0f;
                p.DodgeOffset = Vector3.zero;
                p.PendingDt = 0f;
                p.Slot = Random.Range(0, 3);
                p.Crossing = false;
                p.CrossCooldown = Random.Range(0f, 8f);
                p.Go.SetActive(true);
                live.Add(p);
                Step(p, 0f);
                return;
            }
        }

        /// <summary>World iteration 17: share of pavement spawns kept per district (downtown and Neon Mile busiest).</summary>
        static float DistrictDensity(Vector3 p)
        {
            var d = WorldData.DistrictAt(p);
            switch (d != null ? d.id : "")
            {
                case "D1": case "D6": return 1f;
                case "D2": return 0.6f;
                case "D5": return 0.45f;
                case "D8": return 0.3f;
                case "D3": return 0.12f;
                case "D7": return 0.1f;
                case "D4": return 0.06f;
                default: return 0.3f;
            }
        }

        static Vector3 Lateral(RoadPath road, float s, float lat)
        {
            var p = road.Path.PositionAt(s);
            var t = road.Path.TangentAt(s);
            return p + new Vector3(t.z, 0f, -t.x).normalized * lat;
        }

        void Step(Ped p, float dt)
        {
            if (p.Stroll) { StepStroll(p, dt); return; }
            if (p.Idle) { StepIdle(p, dt); return; }
            var road = WorldData.Roads[p.Road];
            float len = road.Path.Length;
            p.CrossCooldown -= dt;
            if (p.Crossing)
            {
                // across the zebra at constant s, from this kerb to the other; then walk on along the far pavement
                p.CrossT += dt * p.Speed / Mathf.Max(2f, 2f * Mathf.Abs(p.Lateral));
                if (p.CrossT >= 1f) { p.Crossing = false; p.Lateral = -p.Lateral; p.CrossCooldown = 20f; p.GroundTimer = 0f; }
            }
            else if (p.CrossCooldown <= 0f && TrafficLights.Instance != null)
            {
                // iteration 14: cross at the zebra while the cars on this road are held at red (GTA's junctions)
                for (int end = 0; end < 2; end++)
                {
                    int state = TrafficLights.Instance.StateFor(p.Road, end, out float stop);
                    if (state < 0) continue;
                    float zebraS = end == 0 ? stop - 3f : len - (stop - 3f);
                    if (Mathf.Abs(p.S - zebraS) > 0.8f) continue;
                    p.CrossCooldown = 6f;
                    if (state == 0 && Random.value < 0.6f) { p.Crossing = true; p.CrossT = 0f; p.S = zebraS; p.GroundTimer = 0f; CrossingsStarted++; }
                    break;
                }
            }
            if (p.DodgeTime <= 0f && !p.Crossing)
            {
                p.S += p.Dir * p.Speed * dt;
                // turn back 6 m before the road end (the zebra crossings sit 6-15 m from it)
                if (p.S < 6f || p.S > len - 6f) { p.Dir = -p.Dir; p.S = Mathf.Clamp(p.S, 6f, len - 6f); }
            }
            var pos = Lateral(road, p.S, p.Crossing ? Mathf.Lerp(p.Lateral, -p.Lateral, p.CrossT) : p.Lateral);
            var t = road.Path.TangentAt(p.S);
            p.GroundTimer -= dt;
            if (p.GroundTimer <= 0f) { p.GroundTimer = p.Crossing ? 0.25f : 1.2f; if (Ground(pos, out float gy)) p.Ground = gy; }
            pos.y = p.Ground;

            // dive clear of a fast car heading this way
            var car = Player;
            if (car != null && p.DodgeTime <= 0f)
            {
                Vector3 rel = pos + p.DodgeOffset - car.transform.position;
                rel.y = 0f;
                Vector3 vel = car.Body.linearVelocity;
                vel.y = 0f;
                float vmag = vel.magnitude;
                if (vmag > 4f)
                {
                    Vector3 vd = vel / vmag;
                    float along = Vector3.Dot(rel, vd);
                    float perp = (rel - vd * along).magnitude;
                    if (along > 0f && along < vmag * 1.6f + 4f && perp < 3.5f)
                    {
                        Vector3 side = Vector3.Cross(Vector3.up, vd);           // right of the car's path
                        float sgn = Mathf.Sign(Vector3.Dot(rel, side) + 0.001f);
                        p.DodgeVel = side * (sgn * 6f);
                        p.DodgeTime = 0.9f;
                    }
                }
            }
            if (p.DodgeTime > 0f)
            {
                p.DodgeTime -= dt;
                p.DodgeOffset += p.DodgeVel * dt;
                p.DodgeVel *= Mathf.Exp(-dt * 3f);
            }
            else p.DodgeOffset = Vector3.Lerp(p.DodgeOffset, Vector3.zero, dt * 0.5f);
            pos += new Vector3(p.DodgeOffset.x, 0f, p.DodgeOffset.z);

            Vector3 crossDir = new Vector3(t.z, 0f, -t.x) * -Mathf.Sign(p.Lateral);   // toward the far kerb
            Vector3 face = p.DodgeTime > 0f && p.DodgeVel.sqrMagnitude > 0.5f ? p.DodgeVel : p.Crossing ? crossDir : t * p.Dir;
            face.y = 0f;
            if (face.sqrMagnitude > 1e-4f) p.Root.rotation = Quaternion.Slerp(p.Root.rotation, Quaternion.LookRotation(face), dt > 0f ? 1f - Mathf.Exp(-dt * 8f) : 1f);
            p.Root.position = pos;

            // walk cycle (or a startled run while dodging)
            bool run = p.DodgeTime > 0f;
            p.Phase += dt * (run ? 11f : p.Speed * 5.2f);
            float sw = Mathf.Sin(p.Phase);
            float legA = (run ? 45f : 26f) * sw;
            float armA = (run ? 60f : 22f) * sw;
            p.LegL.localRotation = Quaternion.Euler(legA, 0f, 0f);
            p.LegR.localRotation = Quaternion.Euler(-legA, 0f, 0f);
            p.ArmL.localRotation = Quaternion.Euler(-armA, 0f, run ? -25f : -4f);
            bool umbrella = p.WantsUmbrella && Wet > 0.3f && !run;
            if (p.Umbrella.gameObject.activeSelf != umbrella) p.Umbrella.gameObject.SetActive(umbrella);
            // umbrella up: arm forward at chest height, the shaft leaning back so the canopy sits over the head
            p.ArmR.localRotation = umbrella ? Quaternion.Euler(-70f, 0f, 8f) : Quaternion.Euler(armA, 0f, run ? 25f : 4f);
            if (umbrella) p.Umbrella.localRotation = Quaternion.Euler(45f, 0f, -8f);
            p.Hips.localPosition = new Vector3(0f, p.Height * 0.53f + Mathf.Abs(Mathf.Cos(p.Phase)) * 0.035f, 0f);
            p.Torso.localRotation = Quaternion.Euler(run ? 14f : 2f, 0f, 0f);
        }

        /// <summary>Standing at a shelter: weight shifts, a glance around, a step aside from a car on the pavement.</summary>
        void StepIdle(Ped p, float dt)
        {
            var car = Player;
            if (car != null && p.DodgeTime <= 0f)
            {
                Vector3 rel = p.IdlePos + p.DodgeOffset - car.transform.position;
                rel.y = 0f;
                Vector3 vel = car.Body.linearVelocity;
                vel.y = 0f;
                float vmag = vel.magnitude;
                if (vmag > 4f)
                {
                    Vector3 vd = vel / vmag;
                    float along = Vector3.Dot(rel, vd);
                    if (along > 0f && along < vmag * 1.6f + 4f && (rel - vd * along).magnitude < 3.5f)
                    {
                        Vector3 side = Vector3.Cross(Vector3.up, vd);
                        p.DodgeVel = side * (Mathf.Sign(Vector3.Dot(rel, side) + 0.001f) * 6f);
                        p.DodgeTime = 0.9f;
                    }
                }
            }
            if (p.DodgeTime > 0f) { p.DodgeTime -= dt; p.DodgeOffset += p.DodgeVel * dt; p.DodgeVel *= Mathf.Exp(-dt * 3f); }
            p.Phase += dt;
            float sway = Mathf.Sin(p.Phase * 0.9f);
            p.Root.position = p.IdlePos + new Vector3(p.DodgeOffset.x, 0f, p.DodgeOffset.z);
            float look = Mathf.Sin(p.Phase * 0.23f) * 35f;   // glancing up the road for the bus
            p.Root.rotation = Quaternion.Euler(0f, p.IdleYaw + look * 0.3f, 0f);
            p.Head.localRotation = Quaternion.Euler(0f, look, 0f);
            p.LegL.localRotation = Quaternion.Euler(0f, 0f, -2f - sway);
            p.LegR.localRotation = Quaternion.Euler(0f, 0f, 2f - sway);
            p.ArmL.localRotation = Quaternion.Euler(4f, 0f, -5f);
            bool umbrella = p.WantsUmbrella && Wet > 0.3f;
            if (p.Umbrella.gameObject.activeSelf != umbrella) p.Umbrella.gameObject.SetActive(umbrella);
            p.ArmR.localRotation = umbrella ? Quaternion.Euler(-70f, 0f, 8f) : Quaternion.Euler(4f, 0f, 5f);
            if (umbrella) p.Umbrella.localRotation = Quaternion.Euler(45f, 0f, -8f);
            p.Hips.localPosition = new Vector3(sway * 0.015f, p.Height * 0.53f, 0f);
            p.Torso.localRotation = Quaternion.Euler(1f, 0f, sway * 1.5f);
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
            if (top.name.StartsWith("UBX") || top.name.StartsWith("UCX")) return false;   // inside a building
            return Mathf.Abs(y - pos.y) < 2.5f;
        }

        // ------------------------------------------------------------------ figure

        Ped Create()
        {
            Ensure();
            var p = new Ped();
            p.Go = new GameObject("Pedestrian");
            p.Go.transform.SetParent(transform, false);
            p.Root = p.Go.transform;
            p.Hips = Node("Hips", p.Root);
            p.Torso = Node("Torso", p.Hips);
            p.Trousers = Part("HipsMesh", p.Hips, hipsMesh, Vector3.zero, Vector3.one);
            p.Shirt = Part("TorsoMesh", p.Torso, torsoMesh, Vector3.zero, Vector3.one, true);
            p.Head = Node("Head", p.Torso);
            p.Skin = Part("HeadMesh", p.Head, sphere, Vector3.zero, Vector3.one);
            p.Hair = Part("HairMesh", p.Head, sphere, Vector3.zero, Vector3.one);
            p.ArmL = Node("ArmL", p.Torso);
            p.ArmR = Node("ArmR", p.Torso);
            p.ArmLR = Part("ArmLMesh", p.ArmL, limbMesh, Vector3.zero, Vector3.one);
            p.ArmRR = Part("ArmRMesh", p.ArmR, limbMesh, Vector3.zero, Vector3.one);
            p.LegL = Node("LegL", p.Hips);
            p.LegR = Node("LegR", p.Hips);
            p.LegLR = Part("LegLMesh", p.LegL, limbMesh, Vector3.zero, Vector3.one);
            p.LegRR = Part("LegRMesh", p.LegR, limbMesh, Vector3.zero, Vector3.one);
            p.ShoeL = Part("ShoeL", p.LegL, shoeMesh, Vector3.zero, Vector3.one);
            p.ShoeR = Part("ShoeR", p.LegR, shoeMesh, Vector3.zero, Vector3.one);
            p.ShoeL.sharedMaterial = p.ShoeR.sharedMaterial = shoeMat;
            // rain: an umbrella held in the right hand (GTA's walkers open them when it rains)
            p.Umbrella = Node("Umbrella", p.ArmR);
            var shaft = Part("UmbrellaShaft", p.Umbrella, box, new Vector3(0f, 0.45f, 0f), new Vector3(0.02f, 0.9f, 0.02f));
            shaft.sharedMaterial = shoeMat;
            Part("UmbrellaCanopy", p.Umbrella, canopy, new Vector3(0f, 0.9f, 0f), new Vector3(1.05f, 0.32f, 1.05f));
            return p;
        }

        /// <summary>New proportions and clothes for a pooled walker.</summary>
        static void Dress(Ped p)
        {
            p.Stroll = false;
            float h = Random.Range(1.55f, 1.92f);
            float build = Random.Range(0.85f, 1.2f);
            p.Height = h;
            float legLen = h * 0.47f, torsoLen = h * 0.3f, headD = h * 0.13f;
            float shoulders = 0.4f * build, hipW = 0.32f * build;
            p.Hips.localPosition = new Vector3(0f, h * 0.53f, 0f);
            Shape(p.Trousers.transform, new Vector3(0f, -0.02f, 0f), new Vector3(hipW, 0.16f, 0.2f * build));
            p.Torso.localPosition = new Vector3(0f, 0.05f, 0f);
            Shape(p.Shirt.transform, new Vector3(0f, torsoLen * 0.5f, 0f), new Vector3(shoulders, torsoLen, 0.22f * build));
            p.Head.localPosition = new Vector3(0f, torsoLen + headD * 0.62f, 0.01f);
            p.Head.localRotation = Quaternion.identity;   // a pooled walker may have been glancing about at a bus stop
            Shape(p.Skin.transform, Vector3.zero, new Vector3(headD * 0.85f, headD, headD * 0.92f));
            Shape(p.Hair.transform, new Vector3(0f, headD * 0.14f, -headD * 0.04f), new Vector3(headD * 0.9f, headD * 0.8f, headD * 0.98f));
            float armLen = h * 0.36f;
            p.ArmL.localPosition = new Vector3(-shoulders * 0.5f - 0.04f, torsoLen - 0.04f, 0f);
            p.ArmR.localPosition = new Vector3(shoulders * 0.5f + 0.04f, torsoLen - 0.04f, 0f);
            Shape(p.ArmLR.transform, new Vector3(0f, -armLen * 0.5f, 0f), new Vector3(0.085f * build, armLen, 0.09f * build));
            Shape(p.ArmRR.transform, new Vector3(0f, -armLen * 0.5f, 0f), new Vector3(0.085f * build, armLen, 0.09f * build));
            p.LegL.localPosition = new Vector3(-hipW * 0.27f, -0.06f, 0f);
            p.LegR.localPosition = new Vector3(hipW * 0.27f, -0.06f, 0f);
            Shape(p.LegLR.transform, new Vector3(0f, -legLen * 0.5f, 0f), new Vector3(0.13f * build, legLen, 0.14f * build));
            Shape(p.LegRR.transform, new Vector3(0f, -legLen * 0.5f, 0f), new Vector3(0.13f * build, legLen, 0.14f * build));
            Shape(p.ShoeL.transform, new Vector3(0f, -legLen + 0.03f, 0.04f), new Vector3(0.12f * build, 0.07f, 0.25f));
            Shape(p.ShoeR.transform, new Vector3(0f, -legLen + 0.03f, 0.04f), new Vector3(0.12f * build, 0.07f, 0.25f));
            p.Umbrella.localPosition = new Vector3(0f, -armLen * 0.95f, 0.05f);
            p.WantsUmbrella = Random.value < 0.55f;
            var canopyR = p.Umbrella.Find("UmbrellaCanopy")?.GetComponent<Renderer>();
            if (canopyR != null) canopyR.sharedMaterial = umbrellaMats[Random.Range(0, umbrellaMats.Length)];
            var shirt = shirtMats[Random.Range(0, shirtMats.Length)];
            var trousers = trouserMats[Random.Range(0, trouserMats.Length)];
            var skin = skinMats[Random.Range(0, skinMats.Length)];
            var hair = hairMats[Random.Range(0, hairMats.Length)];
            bool shortSleeves = Random.value < 0.4f;
            p.Shirt.sharedMaterial = shirt;
            p.Trousers.sharedMaterial = trousers;
            p.LegLR.sharedMaterial = p.LegRR.sharedMaterial = trousers;
            p.ArmLR.sharedMaterial = p.ArmRR.sharedMaterial = shortSleeves ? skin : shirt;
            p.Skin.sharedMaterial = skin;
            p.Hair.sharedMaterial = hair;
        }

        static void Shape(Transform t, Vector3 pos, Vector3 size)
        {
            t.localPosition = pos;
            t.localScale = size;
        }

        static Transform Node(string name, Transform parent)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        static Renderer Part(string name, Transform parent, Mesh mesh, Vector3 pos, Vector3 size, bool shadows = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.Off;
            return r;
        }

        void Recycle(Ped p)
        {
            if (p?.Go == null) return;
            p.Go.SetActive(false);
            pool.Push(p);
        }

        static void Ensure()
        {
            if (box != null) return;
            box = BuildBox();
            sphere = BuildSphere(14, 10);
            // world iteration 1: shaped body parts instead of boxes (unit size; Dress scales them like the boxes before)
            torsoMesh = BuildRounded("PedTorso", 14, 8, 1.0f, 0.8f, 3f, 6f);
            limbMesh = BuildRounded("PedLimb", 10, 8, 1.0f, 0.72f, 2f, 3f);
            hipsMesh = BuildRounded("PedHips", 12, 6, 0.92f, 1.0f, 3f, 5f);
            shoeMesh = BuildRounded("PedShoe", 10, 5, 1.0f, 1.0f, 3.5f, 4f);
            canopy = BuildCanopy(10);
            shoeMat = Palette("PedShoe", new[] { new Color(0.05f, 0.05f, 0.05f) }, 0.3f)[0];
            umbrellaMats = Palette("PedUmbrella", new[] { new Color(0.04f, 0.04f, 0.05f), new Color(0.5f, 0.06f, 0.08f), new Color(0.08f, 0.15f, 0.4f), new Color(0.15f, 0.15f, 0.16f) }, 0.6f);
            shirtMats = Palette("PedShirt", Shirts, 0.25f);
            trouserMats = Palette("PedTrousers", Trousers, 0.2f);
            skinMats = Palette("PedSkin", Skins, 0.35f);
            hairMats = Palette("PedHair", Hairs, 0.3f);
        }

        static Material[] Palette(string name, Color[] colours, float smooth)
        {
            var mats = new Material[colours.Length];
            for (int i = 0; i < colours.Length; i++)
            {
                var m = RuntimeMaterials.NewLit(name + i);
                if (m == null) { m = mats[0]; mats[i] = m; continue; }
                m.SetColor("_BaseColor", colours[i]);
                m.SetFloat("_Smoothness", smooth);
                m.SetFloat("_Metallic", 0f);
                mats[i] = m;
            }
            return mats;
        }

        /// <summary>
        /// A unit rounded body part centred on the origin (y −0.5 … 0.5): superellipse cross-section (exponent
        /// <paramref name="n"/>: 2 round, 3–4 rounded box), width tapering from <paramref name="bottom"/> to <paramref name="top"/>,
        /// ends rounded with exponent <paramref name="p"/> (higher = flatter). Smooth normals.
        /// </summary>
        static Mesh BuildRounded(string name, int seg, int rings, float top, float bottom, float n, float p)
        {
            var v = new List<Vector3>();
            var tri = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float t = r / (float)rings;
                float y = t - 0.5f;
                float end = Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(Mathf.Abs(2f * t - 1f), p)), 1f / p);
                float w = Mathf.Lerp(bottom, top, t) * end * 0.5f;
                for (int s = 0; s <= seg; s++)
                {
                    float a = 2f * Mathf.PI * s / seg;
                    float c = Mathf.Cos(a), sn = Mathf.Sin(a);
                    float x = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / n), z = Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), 2f / n);
                    v.Add(new Vector3(x * w, y, z * w));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * (seg + 1) + s, b = a + seg + 1;
                    tri.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                }
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetTriangles(tri, 0);
            Outward(m);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Unit cube centred on the origin, flat normals.</summary>
        static Mesh BuildBox()
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var tri = new List<int>();
            Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var d in dirs)
            {
                Vector3 u = Mathf.Abs(d.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 w = Vector3.Cross(d, u);
                int b = v.Count;
                v.Add((d - u - w) * 0.5f); v.Add((d + u - w) * 0.5f); v.Add((d + u + w) * 0.5f); v.Add((d - u + w) * 0.5f);
                for (int k = 0; k < 4; k++) n.Add(d);
                tri.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            var m = new Mesh { name = "PedBox" };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetTriangles(tri, 0);
            Outward(m);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Flips any triangle whose face normal points toward the centre (both meshes are convex around the origin).</summary>
        static void Outward(Mesh m)
        {
            var t = m.triangles;
            var vs = m.vertices;
            for (int i = 0; i < t.Length; i += 3)
            {
                var c = Vector3.Cross(vs[t[i + 1]] - vs[t[i]], vs[t[i + 2]] - vs[t[i]]);
                if (Vector3.Dot(c, vs[t[i]] + vs[t[i + 1]] + vs[t[i + 2]]) < 0f) { int sw = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = sw; }
            }
            m.triangles = t;
        }

        /// <summary>
        /// A shallow cone (umbrella canopy), apex up, unit radius and height. Top and underside have their own vertices:
        /// iteration 7 shared them between both windings, the recalculated normals cancelled to zero, the lighting
        /// produced NaN pixels and bloom spread them into screen-filling white discs.
        /// </summary>
        static Mesh BuildCanopy(int seg)
        {
            var v = new List<Vector3>();
            var nrm = new List<Vector3>();
            var tri = new List<int>();
            for (int side = 0; side < 2; side++)
            {
                for (int i = 0; i < seg; i++)
                {
                    float a0 = 2f * Mathf.PI * i / seg, a1 = 2f * Mathf.PI * (i + 1) / seg;
                    Vector3 apex = new Vector3(0f, 0.5f, 0f);
                    Vector3 r0 = new Vector3(Mathf.Cos(a0) * 0.5f, -0.5f, Mathf.Sin(a0) * 0.5f);
                    Vector3 r1 = new Vector3(Mathf.Cos(a1) * 0.5f, -0.5f, Mathf.Sin(a1) * 0.5f);
                    Vector3 n = Vector3.Cross(r1 - apex, r0 - apex).normalized;   // facet normal
                    if (Vector3.Dot(n, (apex + r0 + r1) / 3f - new Vector3(0f, -0.5f, 0f)) < 0f) n = -n;   // outward/up for the top
                    if (side == 1) n = -n;
                    int b = v.Count;
                    v.Add(apex); v.Add(r0); v.Add(r1);
                    nrm.Add(n); nrm.Add(n); nrm.Add(n);
                    var c = Vector3.Cross(r0 - apex, r1 - apex);
                    bool keep = Vector3.Dot(c, n) >= 0f;
                    if (keep) tri.AddRange(new[] { b, b + 1, b + 2 });
                    else tri.AddRange(new[] { b, b + 2, b + 1 });
                }
            }
            var m = new Mesh { name = "PedCanopy" };
            m.SetVertices(v);
            m.SetNormals(nrm);
            m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh BuildSphere(int seg, int rings)
        {
            var v = new List<Vector3>();
            var tri = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float lat = Mathf.PI * (r / (float)rings - 0.5f);
                for (int s = 0; s <= seg; s++)
                {
                    float lon = 2f * Mathf.PI * s / seg;
                    v.Add(new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon)) * 0.5f);
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * (seg + 1) + s, b = a + seg + 1;
                    tri.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                }
            var m = new Mesh { name = "PedSphere" };
            m.SetVertices(v);
            m.SetTriangles(tri, 0);
            Outward(m);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
