using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.Nitro;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Asphalt-style nitro moments: a perfect-timing boost (Clean Boost) fires a blue ring burst and a camera kick;
    /// Overdrive fires a bigger magenta shockwave ring, a heavier kick and a dust ring along the road. Standard nitro
    /// stays with the exhaust flames and trails in VehicleFX.
    /// </summary>
    public sealed class NitroBurstFX : MonoBehaviour
    {
        NitroSystem nitro;
        ChaseCamera cam;
        ParticleSystem ring;
        static Material ringMat;

        public void Bind(VehicleController car, NitroSystem n, ChaseCamera camera)
        {
            nitro = n;
            cam = camera;
            if (ringMat == null)
            {
                const int s = 64;
                var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "NitroRingTex" };
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s * 0.5f, s * 0.5f)) / (s * 0.5f);
                        float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.8f) / 0.2f);       // a soft ring
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                    }
                tex.Apply();
                ringMat = VeyraCity.World.RuntimeMaterials.NewParticle("NitroRing", true);
                ringMat.SetTexture("_BaseMap", tex);
            }
            var go = new GameObject("NitroBurst");
            go.transform.SetParent(car.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            ring = go.AddComponent<ParticleSystem>();
            ring.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ring.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = 0.45f;
            main.startSpeed = 0f;
            main.startSize = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 4;
            var em = ring.emission;
            em.rateOverTime = 0f;
            var shape = ring.shape;
            shape.enabled = false;
            var size = ring.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 14f));
            var col = ring.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = ringMat;
            r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;   // lies flat around the car
            r.shadowCastingMode = ShadowCastingMode.Off;
            nitro.ModeStarted += OnMode;
        }

        void OnDestroy()
        {
            if (nitro != null) nitro.ModeStarted -= OnMode;
        }

        void OnMode(NitroMode m)
        {
            if (m != NitroMode.CleanBoost && m != NitroMode.Overdrive) return;
            bool od = m == NitroMode.Overdrive;
            var ep = new ParticleSystem.EmitParams
            {
                startColor = od ? new Color(1f, 0.25f, 0.85f) : new Color(0.35f, 0.75f, 1f),
                startSize = od ? 1.6f : 1.1f,
                startLifetime = od ? 0.6f : 0.4f,
            };
            ring.Emit(ep, 1);
            if (cam != null) cam.AddTrauma(od ? 0.55f : 0.3f);
        }
    }
}
