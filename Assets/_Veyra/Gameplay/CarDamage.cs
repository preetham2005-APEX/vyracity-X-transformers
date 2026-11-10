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
        ParticleSystem debris, dust;
        Color paint = new Color(0.5f, 0.5f, 0.5f);
        static Material shardMat, dustMat;
        public float Damage01 { get; private set; }

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
            Burst(point, -normal, dv);
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
                dustMat = RuntimeMaterials.NewParticle("CrashDust", false);
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
