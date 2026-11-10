using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Road spray: on a wet road, each rear wheel throws a mist plume that grows with speed (the rooster tails behind
    /// cars in GTA V's and Cyberpunk's rain). Particles are lit alpha mist, coloured by the scene, so they pick up
    /// tail lamps and neon at night. Dry roads emit nothing.
    /// </summary>
    public sealed class CarSprayFX : MonoBehaviour
    {
        VehicleController car;
        readonly ParticleSystem[] spray = new ParticleSystem[2];
        static Material mistMat;

        public void Bind(VehicleController v)
        {
            car = v;
            if (mistMat == null)
            {
                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SprayMist" };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                        float a = Mathf.Pow(Mathf.Clamp01(1f - dist), 2f) * (0.7f + 0.3f * Mathf.PerlinNoise(x * 0.2f, y * 0.2f));
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                tex.Apply();
                mistMat = RuntimeMaterials.NewParticle("RoadSpray", false);
                mistMat.SetTexture("_BaseMap", tex);
                mistMat.SetColor("_BaseColor", new Color(0.78f, 0.82f, 0.88f, 0.5f));
            }
            var d = v.Definition;
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("Spray" + i);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3((i == 0 ? -0.5f : 0.5f) * d.Rear.TrackWidth, 0.25f, -d.Wheelbase * 0.5f - 0.35f);
                go.transform.localRotation = Quaternion.Euler(-12f, 180f, 0f);    // backwards and slightly up
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 160;
                main.gravityModifier = 0.25f;
                var em = ps.emission;
                em.rateOverTime = 0f;
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 18f;
                shape.radius = 0.15f;
                var size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2.2f));
                var col = ps.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.5f, 0.12f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.sharedMaterial = mistMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                ps.Play();
                spray[i] = ps;
            }
        }

        void LateUpdate()
        {
            if (car == null) return;
            float wet = WorldEnvironment.Instance != null ? WorldEnvironment.Instance.Wetness : 0f;
            float v = car.State.Speed;
            float k = wet * Mathf.Clamp01((v - 6f) / 30f);
            for (int i = 0; i < 2; i++)
            {
                if (spray[i] == null) continue;
                var w = car.GetWheel(2 + i);
                var em = spray[i].emission;
                em.rateOverTime = w.Grounded ? k * 90f : 0f;
                var main = spray[i].main;
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f + v * 0.08f, 3f + v * 0.16f);
            }
        }
    }
}
