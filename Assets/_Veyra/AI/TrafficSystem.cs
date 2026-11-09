using System;
using System.Collections.Generic;
using UnityEngine;
using VeyraCity.Nitro;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.AI
{
    /// <summary>
    /// Ambient traffic around the player on the real road network. Cars drive in lane (right-hand traffic)
    /// on kinematic bodies, follow the car ahead, and become free physics objects when hit. Near misses
    /// (passing within ~3 m at speed without contact) are reported to the player's nitro.
    /// </summary>
    public sealed class TrafficSystem : MonoBehaviour
    {
        public int MaxCars = 36;
        [Range(0f, 2f)] public float Density = 1f;
        public float SpawnMin = 140f, SpawnMax = 360f, DespawnDistance = 460f;
        public GameObject[] CarPrefabs = Array.Empty<GameObject>();
        public VehicleController PlayerCar;
        public NitroSystem PlayerNitro;
        public bool Active = true;
        public event Action NearMiss;

        static readonly HashSet<string> TrafficClasses = new HashSet<string> { "MW", "EX", "HW", "AR", "CO", "SV", "RU", "MT" };
        readonly List<TrafficCar> cars = new List<TrafficCar>();
        readonly Stack<TrafficCar> pool = new Stack<TrafficCar>();
        float spawnTimer;
        static int nextId;
        static readonly Color[] Paints =
        {
            new Color(0.75f, 0.75f, 0.78f), new Color(0.08f, 0.08f, 0.09f), new Color(0.9f, 0.9f, 0.92f), new Color(0.45f, 0.06f, 0.06f),
            new Color(0.10f, 0.18f, 0.38f), new Color(0.32f, 0.34f, 0.36f), new Color(0.55f, 0.48f, 0.36f), new Color(0.12f, 0.28f, 0.22f),
        };

        public int Count => cars.Count;

        public void Clear()
        {
            foreach (var c in cars) Recycle(c, false);
            cars.Clear();
        }

        void Update()
        {
            if (!Active || PlayerCar == null)
            {
                if (cars.Count > 0) Clear();
                return;
            }
            Vector3 focus = PlayerCar.transform.position;
            for (int i = cars.Count - 1; i >= 0; i--)
            {
                var c = cars[i];
                bool far = (c.transform.position - focus).sqrMagnitude > DespawnDistance * DespawnDistance;
                if (far || c.Expired)
                {
                    Recycle(c, true);
                    cars.RemoveAt(i);
                }
            }
            spawnTimer -= Time.deltaTime;
            int target = Mathf.RoundToInt(MaxCars * Density);
            if (spawnTimer <= 0f && cars.Count < target)
            {
                spawnTimer = 0.12f;
                TrySpawn(focus);
            }
        }

        void FixedUpdate()
        {
            if (!Active) return;
            float dt = Time.fixedDeltaTime;
            foreach (var c in cars) c.Step(dt, cars, PlayerCar);
            DetectNearMisses();
        }

        void DetectNearMisses()
        {
            if (PlayerCar == null || PlayerNitro == null) return;
            var pt = PlayerCar.transform;
            float pv = PlayerCar.State.Speed;
            if (pv < 60f / 3.6f) return;
            foreach (var c in cars)
            {
                if (c.Crashed) continue;
                var local = pt.InverseTransformPoint(c.transform.position);
                float side = Mathf.Abs(local.x);
                if (local.z > -8f && local.z < 8f && side < 3.4f && side > 1.2f && c.TimeSinceContact > 1f)
                {
                    if (!c.NearArmed) c.NearArmed = true;
                }
                if (c.NearArmed && local.z < -6f)
                {
                    c.NearArmed = false;
                    if (c.TimeSinceContact > 1f)
                    {
                        PlayerNitro.ReportNearMiss(c.Id);
                        NearMiss?.Invoke();
                    }
                }
                if (local.z > 12f || side > 6f) c.NearArmed = false;
            }
        }

        void TrySpawn(Vector3 focus)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                float ang = UnityEngine.Random.value * Mathf.PI * 2f;
                float r = UnityEngine.Random.Range(SpawnMin, SpawnMax);
                var probe = focus + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                if (!WorldData.NearestRoad(probe, 60f, out var hit)) continue;
                var road = hit.RoadPath;
                if (!TrafficClasses.Contains(road.Data.cls)) continue;
                if (Mathf.Abs(hit.Point.y - focus.y) > 60f) continue;
                if (road.StructureAt(hit.Segment) == StructureKind.Tunnel) continue;
                var cam = Camera.main;
                if (cam != null)
                {
                    var vp = cam.WorldToViewportPoint(hit.Point);
                    if (vp.z > 0f && vp.x > -0.1f && vp.x < 1.1f && vp.y > -0.1f && vp.y < 1.1f && vp.z < 220f) continue;
                }
                bool crowded = false;
                foreach (var c in cars)
                    if ((c.transform.position - hit.Point).sqrMagnitude < 30f * 30f) { crowded = true; break; }
                if (crowded) continue;

                int dir = UnityEngine.Random.value < 0.5f ? 1 : -1;
                int lanes = Mathf.Max(1, road.Data.lanes);
                int lane = UnityEngine.Random.Range(0, lanes);
                float offset;
                if (road.Divided)
                {
                    float cw = road.Data.width * 0.5f - 1.2f;
                    float lw = cw / lanes;
                    offset = 1.2f + (lane + 0.5f) * lw;
                }
                else
                {
                    float lw = Mathf.Min(3.4f, road.Data.width / (2f * lanes));
                    offset = (lane + 0.5f) * lw;
                }
                var car = pool.Count > 0 ? pool.Pop() : CreateCar();
                car.gameObject.SetActive(true);
                float speed = road.Data.speedKmh / 3.6f * UnityEngine.Random.Range(0.85f, 1.05f);
                car.Begin(road, hit.S, dir, offset, speed, Paints[UnityEngine.Random.Range(0, Paints.Length)]);
                cars.Add(car);
                return;
            }
        }

        TrafficCar CreateCar()
        {
            GameObject go;
            if (CarPrefabs != null && CarPrefabs.Length > 0)
            {
                go = Instantiate(CarPrefabs[UnityEngine.Random.Range(0, CarPrefabs.Length)], transform);
                if (go.GetComponentInChildren<Collider>() == null) go.AddComponent<BoxCollider>().size = new Vector3(1.8f, 1.4f, 4.4f);
            }
            else go = ProceduralCar.Build(transform, UnityEngine.Random.Range(0, 3));
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.mass = 1350f;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var tc = go.GetComponent<TrafficCar>();
            if (tc == null) tc = go.AddComponent<TrafficCar>();
            tc.Id = ++nextId;
            return tc;
        }

        void Recycle(TrafficCar c, bool pooled)
        {
            if (c == null) return;
            c.gameObject.SetActive(false);
            if (pooled) pool.Push(c); else Destroy(c.gameObject);
        }
    }
}
