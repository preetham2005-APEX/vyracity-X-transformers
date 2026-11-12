using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VeyraCity.World
{
    /// <summary>
    /// Roadmap D1: far traffic at night as light sprites only (the technique open-world games use for distant traffic): cars
    /// that exist only as a position on the motorways, expressways, highways and arterials of the Veyra road data, drawn as
    /// one additive billboard each, white-warm when the car drives towards the camera (headlights) and red when it drives
    /// away (tail lights). They show only beyond <see cref="NearFade"/> metres, where the real traffic and the lamp pool take
    /// over, out to <see cref="WorldEnvironment.FarTrafficDistance"/>. One particle system, no lights, no physics; positions are
    /// rebuilt every third frame (the particles fly on the car velocity in between) and re-projected onto the road path for one eighth of the cars per rebuild. Created at runtime by
    /// <see cref="WorldEnvironment"/> (no scene object).
    /// </summary>
    public sealed class DistantTrafficLights : MonoBehaviour
    {
        public float NearFade = 300f;
        const int MaxCars = 8000;
        const int Slices = 8;
        const int RebuildEvery = 3;   // particles move on their own velocity in between (simulation speed 1)

        struct Car
        {
            public int Road;
            public float S, Speed, Lateral;
            public sbyte Dir;
            public Vector3 Pos, Vel;
        }

        Car[] cars;
        int carCount, slice, frame;
        float pending;
        ParticleSystem ps;
        ParticleSystem.Particle[] buf;
        static readonly Color Head = new Color(1f, 0.92f, 0.78f), Tail = new Color(1f, 0.08f, 0.05f);

        public static DistantTrafficLights Create(Transform parent, Material additive, Texture glow)
        {
            var go = new GameObject("FarTrafficLights");
            go.transform.SetParent(parent, false);
            var d = go.AddComponent<DistantTrafficLights>();
            d.Init(additive, glow);
            return d;
        }

        void Init(Material m, Texture glow)
        {
            m.SetTexture("_BaseMap", glow);
            m.SetColor("_BaseColor", new Color(3.5f, 3.5f, 3.5f, 1f));   // HDR: 8-bit particle colours, the glow needs > 1
            ps = gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = MaxCars;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 1e6f;
            main.startSpeed = 0f;
            main.simulationSpeed = 1f;   // particles carry the car velocity between rebuilds
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.minParticleSize = 0f;
            r.maxParticleSize = 0.05f;
            BuildCars();
            buf = new ParticleSystem.Particle[carCount];
        }

        void BuildCars()
        {
            cars = new Car[MaxCars];
            var rng = new System.Random(4242);
            var roads = WorldData.Roads;
            for (int r = 0; r < roads.Count && carCount < MaxCars; r++)
            {
                var road = roads[r];
                int rank = WorldData.Rank(road);
                if (rank < 4 || road.Path == null || road.Path.Length < 200f) continue;
                // spacing per direction: dense motorways, sparse arterials (night traffic)
                float spacing = rank >= 6 ? 70f : rank == 5 ? 110f : 170f;
                float speed = Mathf.Max(10f, road.Data.speedKmh / 3.6f);
                float lane = Mathf.Max(1.8f, road.HalfWidth * 0.45f);
                for (int dir = -1; dir <= 1; dir += 2)
                    for (float s = (float)rng.NextDouble() * spacing; s < road.Path.Length && carCount < MaxCars; s += spacing * (0.6f + 0.8f * (float)rng.NextDouble()))
                    {
                        var c = new Car { Road = r, S = s, Dir = (sbyte)dir, Speed = speed * (0.8f + 0.25f * (float)rng.NextDouble()), Lateral = lane * dir };
                        Project(ref c, roads);
                        cars[carCount++] = c;
                    }
            }
        }

        static void Project(ref Car c, IReadOnlyList<RoadPath> roads)
        {
            var path = roads[c.Road].Path;
            if (path.Closed) c.S = path.Wrap(c.S);
            else if (c.S < 0f || c.S > path.Length) { c.S = c.Dir > 0 ? 0f : path.Length; }   // respawn at the start of its direction
            var t = path.TangentAt(c.S);
            var right = Vector3.Cross(Vector3.up, t).normalized;
            c.Pos = path.PositionAt(c.S) + right * c.Lateral + Vector3.up * 0.75f;
            c.Vel = t * (c.Speed * c.Dir);
        }

        /// <summary>Called by WorldEnvironment every frame with the viewing camera and the night factor.</summary>
        public void Tick(Camera cam, float night, float maxDistance)
        {
            if (ps == null || carCount == 0) return;
            bool on = cam != null && night > 0.2f && maxDistance > NearFade;
            if (ps.gameObject.activeSelf != on) ps.gameObject.SetActive(on);
            if (!on) return;
            pending += Time.deltaTime;
            if (++frame % RebuildEvery != 0) return;
            float dt = pending;
            pending = 0f;
            var roads = WorldData.Roads;
            Vector3 eye = cam.transform.position;
            Vector3 fwd = cam.transform.forward;
            float max2 = maxDistance * maxDistance, near2 = NearFade * NearFade;
            int n = 0;
            for (int i = 0; i < carCount; i++)
            {
                ref var car = ref cars[i];
                car.S += car.Speed * car.Dir * dt;
                if ((i & (Slices - 1)) == slice) Project(ref car, roads);
                else car.Pos += car.Vel * dt;
                var to = car.Pos - eye;
                float d2 = to.sqrMagnitude;
                if (d2 < near2 || d2 > max2 || Vector3.Dot(to, fwd) < 0f) continue;
                float d = Mathf.Sqrt(d2);
                float a = night * Mathf.Clamp01((d - NearFade) / 120f) * Mathf.Clamp01((maxDistance - d) / 400f);
                bool towards = Vector3.Dot(car.Vel, to) < 0f;
                var c = towards ? Head : Tail;
                if (!towards) a *= 0.8f;
                buf[n++] = new ParticleSystem.Particle
                {
                    position = car.Pos,
                    velocity = car.Vel,
                    startSize = Mathf.Clamp(d * 0.0035f, 1.1f, 9f) * (towards ? 1f : 0.8f),
                    startColor = new Color(c.r, c.g, c.b, a),
                    remainingLifetime = 1e6f,
                    startLifetime = 1e6f,
                };
            }
            slice = (slice + 1) & (Slices - 1);
            ps.SetParticles(buf, n);
            if (!ps.isPlaying) ps.Play();
        }
    }
}
