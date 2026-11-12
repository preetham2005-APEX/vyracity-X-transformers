using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.World;

namespace VeyraCity.AI
{
    /// <summary>
    /// Rain spray behind traffic (visual iteration 7; GTA V's wet streets: every car ahead throws a mist plume). A pool of
    /// emitters follows the nearest moving traffic cars while the roads are wet; the rest stay dry, so the cost is fixed.
    /// The mist is unlit, so its brightness follows the light level (no glowing puffs at night).
    /// </summary>
    public sealed class TrafficSpray : MonoBehaviour
    {
        public TrafficSystem Traffic;
        public int Emitters = 8;
        public float Radius = 90f;

        readonly List<ParticleSystem> pool = new List<ParticleSystem>();
        readonly TrafficCar[] owner = new TrafficCar[16];
        readonly List<(float d, TrafficCar c)> near = new List<(float, TrafficCar)>();
        static Material mistMat;
        float timer;

        void Update()
        {
            long fc0 = VeyraCity.Core.FrameCost.Start();
            UpdateBody();
            VeyraCity.Core.FrameCost.Stop("TrafficSpray", fc0);
        }

        void UpdateBody()
        {
            if (Traffic == null) return;
            float wet = WorldEnvironment.Instance != null ? WorldEnvironment.Instance.Wetness : 0f;
            var cam = Camera.main;
            if (wet < 0.15f || cam == null)
            {
                for (int i = 0; i < pool.Count; i++) { SetRate(pool[i], 0f); owner[i] = null; }
                return;
            }
            Ensure();
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                timer = 0.3f;
                near.Clear();
                Vector3 eye = cam.transform.position;
                foreach (var c in Traffic.Cars)
                {
                    if (c == null || !c.isActiveAndEnabled || c.Speed < 6f) continue;
                    float d = (c.transform.position - eye).sqrMagnitude;
                    if (d < Radius * Radius) near.Add((d, c));
                }
                near.Sort((a, b) => a.d.CompareTo(b.d));
                for (int i = 0; i < pool.Count; i++) owner[i] = i < near.Count ? near[i].c : null;
            }
            float night = WorldEnvironment.Instance != null ? WorldEnvironment.Instance.NightFactor : 0f;
            float lum = Mathf.Lerp(0.78f, 0.15f, night);
            for (int i = 0; i < pool.Count; i++)
            {
                var ps = pool[i];
                var c = owner[i];
                if (c == null || !c.isActiveAndEnabled) { SetRate(ps, 0f); continue; }
                var t = c.transform;
                ps.transform.SetPositionAndRotation(t.position - c.Forward * 2.3f + Vector3.up * 0.3f, Quaternion.LookRotation(-c.Forward + Vector3.up * 0.2f));
                SetRate(ps, wet * Mathf.Clamp01((c.Speed - 5f) / 20f) * 70f);
                var main = ps.main;
                main.startSpeed = new ParticleSystem.MinMaxCurve(1f + c.Speed * 0.08f, 2.5f + c.Speed * 0.15f);
                main.startColor = new Color(lum, lum * 1.03f, lum * 1.08f, Mathf.Lerp(0.5f, 0.38f, night));
            }
        }

        static void SetRate(ParticleSystem ps, float rate)
        {
            var em = ps.emission;
            em.rateOverTime = rate;
        }

        void Ensure()
        {
            if (pool.Count > 0) return;
            if (mistMat == null)
            {
                const int n = 32;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "TrafficSprayMist" };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - dist), 2f)));
                    }
                tex.Apply();
                mistMat = RuntimeMaterials.NewParticleSoft("TrafficSpray");   // D2: soft particles
                if (mistMat != null) mistMat.SetTexture("_BaseMap", tex);
            }
            for (int i = 0; i < Mathf.Min(Emitters, owner.Length); i++)
            {
                var go = new GameObject("TrafficSpray" + i);
                go.transform.SetParent(transform, false);
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.85f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 90;
                main.gravityModifier = 0.25f;
                var em = ps.emission;
                em.rateOverTime = 0f;
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(1.6f, 0.2f, 0.3f);
                var size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2.2f));
                var col = ps.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.5f, 0.15f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.sharedMaterial = mistMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.08f;
                r.lengthScale = 1.5f;
                ps.Play();
                pool.Add(ps);
            }
        }
    }
}
