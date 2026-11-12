using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Visual body damage (GTA V-style dents): a hard hit pushes the body vertices around the contact point inward,
    /// with a smooth falloff, scaled by the impact's speed change. Each car gets its own mesh copy on its first dent;
    /// total deformation per vertex is capped so cars dent and crumple but never turn into spikes. Needs readable body
    /// meshes (CarImportTools imports drivable cars with Read/Write on). Physics is unaffected.
    /// </summary>
    public sealed class CarDamage : MonoBehaviour
    {
        [Tooltip("Speed change (m/s) below which a hit leaves no mark")] public float Threshold = 2.5f;
        public float MaxDepth = 0.16f, MaxTotal = 0.28f;

        sealed class Body
        {
            public MeshFilter Filter;
            public Mesh Mesh;
            public Vector3[] Base, Verts;
            public bool Copied;
        }

        VehicleController car;
        readonly List<Body> bodies = new List<Body>();
        ParticleSystem debris, dust, engineSmoke;
        Color paint = new Color(0.5f, 0.5f, 0.5f);
        static Material shardMat, dustMat;
        public float Damage01 { get; private set; }
        /// <summary>BUG-151 diagnostics: live engine-smoke particles.</summary>
        public int EngineSmokeParticles => engineSmoke != null ? engineSmoke.particleCount : 0;

        // D2 (round 3): damage by zone, so a front hit cracks the windscreen and breaks the headlights, a rear hit the
        // tail lamps. Zones: 0 front, 1 rear, 2 left, 3 right (by where the contact lands on the body).
        public enum Zone { Front, Rear, Left, Right }
        readonly float[] zones = new float[4];
        public float ZoneDamage(Zone z) => zones[(int)z];
        /// <summary>1 = lamps intact; falls to 0 as the front (or rear) zone passes 35-65 % damage.</summary>
        public float LampHealth(bool front) => 1f - Mathf.Clamp01((zones[front ? 0 : 1] - 0.35f) / 0.3f);

        struct GlassSlot { public Renderer R; public int Slot; public Zone Zone; public Color Base; public bool Cracked; }
        readonly List<GlassSlot> glass = new List<GlassSlot>();
        static Texture2D crackTex;
        MaterialPropertyBlock glassBlock;

        public void Bind(VehicleController v)
        {
            car = v;
            foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
                if (mf.name.StartsWith("Body_LOD") && mf.sharedMesh != null && mf.sharedMesh.isReadable)
                    bodies.Add(new Body { Filter = mf });
            if (bodies.Count == 0) { enabled = false; return; }
            foreach (var r in bodies[0].Filter.GetComponents<Renderer>())
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.name.Contains("Paint") && m.HasProperty("_BaseColor")) { paint = m.GetColor("_BaseColor"); break; }
            BuildDebris();
            BuildEngineSmoke(v);
            FindGlass(v);
            v.Collided += OnHit;
        }

        void OnDestroy()
        {
            if (car != null) car.Collided -= OnHit;
            foreach (var b in bodies) if (b.Copied && b.Mesh != null) Destroy(b.Mesh);
        }

        void OnHit(VehicleController v, Collision c)
        {
            float dv = c.impulse.magnitude / Mathf.Max(100f, v.Body.mass);
            if (dv < Threshold || c.contactCount == 0) return;
            Vector3 point = Vector3.zero, normal = Vector3.zero;
            for (int i = 0; i < c.contactCount; i++) { var cp = c.GetContact(i); point += cp.point; normal += cp.normal; }
            point /= c.contactCount;
            normal = normal.sqrMagnitude > 1e-6f ? normal.normalized : -v.transform.forward;
            float radius = Mathf.Clamp(0.35f + dv * 0.045f, 0.35f, 0.9f);
            float depth = Mathf.Min(MaxDepth, dv * 0.013f);
            foreach (var b in bodies) Dent(b, point, normal, radius, depth);
            Damage01 = Mathf.Clamp01(Damage01 + dv / 60f);
            var z = ZoneOf(v, point);
            zones[(int)z] = Mathf.Clamp01(zones[(int)z] + dv / 30f);
            CrackGlass();
            Burst(point, -normal, dv);
        }

        public static Zone ZoneOf(Vector3 local, Vector3 size)
        {
            // normalised position on the footprint; the end zones take the outer quarter of the length
            float zx = size.x > 0.1f ? local.x / (size.x * 0.5f) : 0f;
            float zz = size.z > 0.1f ? local.z / (size.z * 0.5f) : 0f;
            if (Mathf.Abs(zz) > 0.5f && Mathf.Abs(zz) >= Mathf.Abs(zx) * 0.5f) return zz > 0f ? Zone.Front : Zone.Rear;
            return zx < 0f ? Zone.Left : Zone.Right;
        }

        Zone ZoneOf(VehicleController v, Vector3 world)
        {
            var size = v.Definition != null ? v.Definition.Chassis.Dimensions : new Vector3(1.9f, 1.3f, 4.4f);
            return ZoneOf(v.transform.InverseTransformPoint(world), size);
        }

        /// <summary>Glass slots (windscreen, side and rear windows), each assigned to the zone it sits in. Lamp lenses
        /// are left to VehicleFX (they dim through <see cref="LampHealth"/>).</summary>
        void FindGlass(VehicleController v)
        {
            var size = v.Definition != null ? v.Definition.Chassis.Dimensions : new Vector3(1.9f, 1.3f, 4.4f);
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    string n = m.name;
                    if (n.StartsWith("M_Lamp") || n.Contains("Light") || n.Contains("Inside")) continue;
                    if (!(n.Contains("Glass") || n.Contains("Window")) || !m.HasProperty("_BaseColor")) continue;
                    var local = v.transform.InverseTransformPoint(r.bounds.center);
                    glass.Add(new GlassSlot { R = r, Slot = i, Zone = ZoneOf(local, size), Base = m.GetColor("_BaseColor") });
                }
            }
        }

        /// <summary>A glass slot cracks once its zone passes 40 % damage (a whole-car hit above 70 % cracks all of it):
        /// a procedural crack texture through the slot's own UVs, frosted a little more opaque.</summary>
        void CrackGlass()
        {
            for (int i = 0; i < glass.Count; i++)
            {
                var g = glass[i];
                if (g.Cracked || g.R == null) continue;
                if (zones[(int)g.Zone] < 0.4f && Damage01 < 0.7f) continue;
                if (crackTex == null) crackTex = CrackTexture();
                glassBlock ??= new MaterialPropertyBlock();
                g.R.GetPropertyBlock(glassBlock, g.Slot);
                glassBlock.SetTexture("_BaseMap", crackTex);
                glassBlock.SetVector("_BaseMap_ST", new Vector4(2f, 2f, 0f, 0f));
                glassBlock.SetColor("_BaseColor", new Color(g.Base.r, g.Base.g, g.Base.b, Mathf.Max(g.Base.a, 0.1f) * 2.5f));
                g.R.SetPropertyBlock(glassBlock, g.Slot);
                g.Cracked = true;
                glass[i] = g;
            }
        }

        /// <summary>A shattered-safety-glass pattern: radial cracks from an impact point plus concentric rings, as
        /// opaque white lines (alpha 1) over clear glass (alpha 0.4), tileable enough at 2x2.</summary>
        static Texture2D CrackTexture()
        {
            const int N = 256;
            var px = new Color32[N * N];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(160, 165, 170, 100);
            var rng = new System.Random(7);
            var c = new Vector2(N * 0.45f, N * 0.55f);
            void Line(Vector2 a, Vector2 b)
            {
                int steps = Mathf.CeilToInt(Vector2.Distance(a, b));
                for (int s = 0; s <= steps; s++)
                {
                    var p = Vector2.Lerp(a, b, s / (float)Mathf.Max(1, steps));
                    int x = Mathf.RoundToInt(p.x), y = Mathf.RoundToInt(p.y);
                    if (x < 0 || y < 0 || x >= N || y >= N) continue;
                    px[y * N + x] = new Color32(235, 240, 245, 255);
                }
            }
            int rays = 14;
            var ends = new Vector2[rays];
            for (int k = 0; k < rays; k++)
            {
                float ang = (k + (float)rng.NextDouble() * 0.6f) / rays * Mathf.PI * 2f;
                var p = c;
                float len = N * (0.35f + 0.3f * (float)rng.NextDouble());
                for (int seg = 0; seg < 6; seg++)
                {
                    ang += ((float)rng.NextDouble() - 0.5f) * 0.35f;
                    var q = p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (len / 6f);
                    Line(p, q);
                    p = q;
                }
                ends[k] = p;
            }
            // concentric rings joining neighbouring rays at three radii
            foreach (float t in new[] { 0.15f, 0.32f, 0.55f })
                for (int k = 0; k < rays; k++)
                {
                    var a = Vector2.Lerp(c, ends[k], t + 0.04f * (float)rng.NextDouble());
                    var b = Vector2.Lerp(c, ends[(k + 1) % rays], t + 0.04f * (float)rng.NextDouble());
                    Line(a, b);
                }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "CrackedGlass", wrapMode = TextureWrapMode.Repeat };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>
        /// Iteration 5: what flies off in a GTA V crash: paint chips in the car's colour, dark trim and plastic, a few glass
        /// glints, and a dust puff at the contact point. World-space bursts, lit (alpha) particles, bouncing off the ground.
        /// </summary>
        void BuildDebris()
        {
            if (shardMat == null)
            {
                var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false) { name = "DebrisShard", filterMode = FilterMode.Point };
                var px = new Color[64];
                for (int i = 0; i < 64; i++) px[i] = Color.white;
                tex.SetPixels(px);
                tex.Apply();
                shardMat = RuntimeMaterials.NewParticle("CrashDebris", false);
                shardMat.SetTexture("_BaseMap", tex);
                const int n = 32;
                var soft = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "DebrisDust", wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                        soft.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - d), 2f)));
                    }
                soft.Apply();
                dustMat = RuntimeMaterials.NewParticleSoft("CrashDust");   // D2: soft particles (also the engine smoke)
                dustMat.SetTexture("_BaseMap", soft);
            }
            debris = MakeSystem("CrashDebris", shardMat, 80, ParticleSystemRenderMode.Billboard);
            var main = debris.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.09f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 1.6f;
            var rot = debris.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-12f, 12f);
            var col = debris.collision;
            col.enabled = true;
            col.type = ParticleSystemCollisionType.World;
            col.quality = ParticleSystemCollisionQuality.Low;
            col.dampen = 0.5f;
            col.bounce = 0.3f;
            col.lifetimeLoss = 0.1f;
            dust = MakeSystem("CrashDust", dustMat, 24, ParticleSystemRenderMode.Billboard);
            var dm = dust.main;
            dm.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            dm.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            dm.gravityModifier = -0.05f;
            var dsz = dust.sizeOverLifetime;
            dsz.enabled = true;
            dsz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.4f));
            var dcol = dust.colorOverLifetime;
            dcol.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0.45f, 0f), new GradientAlphaKey(0f, 1f) });
            dcol.color = g;
        }

        ParticleSystem MakeSystem(string name, Material mat, int max, ParticleSystemRenderMode mode)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = mode;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        /// <summary>
        /// Iteration 9: a wrecked engine smokes (GTA V): grey wisps from the bonnet above 55 % damage, a thick dark plume
        /// near 90 %. The smoke is unlit, so it is dimmed with the light level.
        /// </summary>
        void BuildEngineSmoke(VehicleController v)
        {
            engineSmoke = MakeSystem("EngineSmoke", dustMat, 120, ParticleSystemRenderMode.Billboard);
            var d = v.Definition;
            float len = d != null ? d.Chassis.Dimensions.z : 4.4f;
            float h = d != null ? d.Chassis.Dimensions.y : 1.3f;
            // engine bay: the front third for most cars (mid-engined cars smoke from behind the cabin)
            bool mid = d != null && d.Layout.ToString().Contains("AWD") && h < 1.25f;
            engineSmoke.transform.localPosition = new Vector3(0f, h * 0.8f, (mid ? -0.3f : 0.32f) * len);
            var main = engineSmoke.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.gravityModifier = -0.08f;
            var shape = engineSmoke.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.9f, 0.1f, 0.6f);
            var size = engineSmoke.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 3.2f));
            var col = engineSmoke.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            engineSmoke.Play();
        }

        void LateUpdate()
        {
            long fc0 = VeyraCity.Core.FrameCost.Start();
            LateUpdateBody();
            VeyraCity.Core.FrameCost.Stop("CarDamage", fc0);
        }

        void LateUpdateBody()
        {
            if (engineSmoke == null) return;
            float k = Mathf.Clamp01((Damage01 - 0.55f) / 0.35f);
            var em = engineSmoke.emission;
            em.rateOverTime = k * 26f;
            if (k <= 0f) return;
            float night = WorldEnvironment.Instance != null ? WorldEnvironment.Instance.NightFactor : 0f;
            float lum = Mathf.Lerp(Mathf.Lerp(0.62f, 0.12f, k), Mathf.Lerp(0.2f, 0.06f, k), night);   // grey to black, darker at night
            var main = engineSmoke.main;
            main.startColor = new Color(lum, lum, lum * 1.02f, Mathf.Lerp(0.45f, 0.8f, k));
        }

        void Burst(Vector3 point, Vector3 outward, float dv)
        {
            if (debris == null) return;
            int n = Mathf.Clamp(Mathf.RoundToInt(dv * 2.5f), 6, 36);
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < n; i++)
            {
                float k = Random.value;
                ep.startColor = k < 0.5f ? paint * Random.Range(0.8f, 1.1f) : k < 0.85f ? new Color(0.08f, 0.08f, 0.09f) : new Color(0.75f, 0.85f, 0.95f, 0.8f);
                ep.position = point + Random.insideUnitSphere * 0.25f;
                Vector3 dir = (outward + Random.insideUnitSphere * 0.9f + Vector3.up * 0.6f).normalized;
                ep.velocity = dir * Random.Range(1.5f, 2f + dv * 0.35f) + car.Body.linearVelocity * 0.4f;
                debris.Emit(ep, 1);
            }
            var dp = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            int puffs = Mathf.Clamp(Mathf.RoundToInt(dv * 0.6f), 2, 8);
            for (int i = 0; i < puffs; i++)
            {
                dp.position = point + Random.insideUnitSphere * 0.4f;
                dp.velocity = (outward + Random.insideUnitSphere * 0.5f) * Random.Range(0.4f, 1.4f);
                dp.startColor = new Color(0.62f, 0.6f, 0.56f);
                dust.Emit(dp, 1);
            }
        }

        void Dent(Body b, Vector3 worldPoint, Vector3 worldNormal, float radius, float depth)
        {
            if (!b.Copied)
            {
                b.Mesh = Instantiate(b.Filter.sharedMesh);
                b.Mesh.name = b.Filter.sharedMesh.name + "_Dented";
                b.Mesh.MarkDynamic();
                b.Filter.sharedMesh = b.Mesh;
                b.Base = b.Mesh.vertices;
                b.Verts = (Vector3[])b.Base.Clone();
                b.Copied = true;
            }
            var t = b.Filter.transform;
            Vector3 p = t.InverseTransformPoint(worldPoint);
            Vector3 push = t.InverseTransformDirection(worldNormal).normalized * depth;   // the contact normal points into this car
            float r2 = radius * radius;
            bool changed = false;
            var verts = b.Verts;
            for (int i = 0; i < verts.Length; i++)
            {
                float d2 = (verts[i] - p).sqrMagnitude;
                if (d2 > r2) continue;
                float f = 1f - Mathf.Sqrt(d2) / radius;
                Vector3 nv = verts[i] + push * (f * f);
                Vector3 off = nv - b.Base[i];
                if (off.sqrMagnitude > MaxTotal * MaxTotal) nv = b.Base[i] + off.normalized * MaxTotal;
                verts[i] = nv;
                changed = true;
            }
            if (!changed) return;
            b.Mesh.vertices = verts;
            b.Mesh.RecalculateNormals();
            b.Mesh.RecalculateBounds();
        }
    }
}
