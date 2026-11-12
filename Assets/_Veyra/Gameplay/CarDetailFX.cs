using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.Nitro;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Round 2 car detail (Forza / NFS / Asphalt):
    ///  * brake discs that heat up: each wheel accumulates heat from braking at speed and cools in the airflow; the rotor
    ///    behind the spokes glows dark red, orange, then yellow-white (front axle hotter, as the brake bias is forward);
    ///  * light streaks from the tail lamps: in the boost colour under nitro (Asphalt's light-speed look) and red under hard
    ///    braking at speed.
    /// Works for any car with Wheel_* visuals; the streaks need the pipeline's Lamp_RL / Lamp_RR anchors.
    /// </summary>
    public sealed class CarDetailFX : MonoBehaviour
    {
        VehicleController car;
        NitroSystem nitro;
        readonly Renderer[] discs = new Renderer[4];
        readonly float[] heat = new float[4];
        readonly TrailRenderer[] streaks = new TrailRenderer[2];
        MaterialPropertyBlock block;
        static Material discMat, streakMat;
        static Mesh discMesh;

        public float Heat01(int wheel) => heat[wheel];

        public void Bind(VehicleController v, NitroSystem n)
        {
            car = v;
            nitro = n;
            block = new MaterialPropertyBlock();
            Ensure();
            var all = GetComponentsInChildren<Transform>(true);
            string[] names = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
            for (int i = 0; i < 4; i++)
            {
                Transform w = null;
                foreach (var t in all) if (t.name == names[i]) { w = t; break; }
                if (w == null) continue;
                var tyre = i < 2 ? v.Definition.Front.Tyre : v.Definition.Rear.Tyre;
                var go = new GameObject("BrakeDiscGlow");
                go.transform.SetParent(w, false);
                // the disc sits inboard of the spokes: towards the car's centre line along the wheel's spin axis
                Vector3 inward = w.InverseTransformDirection(-transform.right * Mathf.Sign(transform.InverseTransformPoint(w.position).x));
                go.transform.localPosition = inward.normalized * (tyre.Width * 0.02f);   // iteration 24: at the wheel's centre plane, behind the spokes (deeper sat inside the rim barrel)
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, inward.normalized);
                float r = tyre.Radius * 0.6f;
                go.transform.localScale = new Vector3(r, r, r) / Mathf.Max(1e-4f, w.lossyScale.x);
                go.AddComponent<MeshFilter>().sharedMesh = discMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = discMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.enabled = false;
                discs[i] = mr;
            }
            string[] lamps = { "Lamp_RL", "Lamp_RR" };
            for (int j = 0; j < 2; j++)
            {
                Transform a = null;
                foreach (var t in all) if (t.name == lamps[j]) { a = t; break; }
                if (a == null) continue;
                var go = new GameObject("LampStreak");
                go.transform.SetParent(a, false);
                go.transform.localPosition = new Vector3(0f, 0f, -0.06f);
                var tr = go.AddComponent<TrailRenderer>();
                // iteration 19: short and thin (0.55 s / 11 cm drew 30 m red ribbons along the road at 200 km/h)
                tr.time = 0.14f;
                tr.minVertexDistance = 0.3f;
                tr.widthCurve = AnimationCurve.EaseInOut(0f, 0.06f, 1f, 0.0f);
                tr.sharedMaterial = streakMat;
                tr.shadowCastingMode = ShadowCastingMode.Off;
                tr.receiveShadows = false;
                tr.emitting = false;
                streaks[j] = tr;
            }
        }

        static void Ensure()
        {
            if (discMat != null) return;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "BrakeDiscGlow" };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                    // the friction ring glows (0.55-0.95 of the radius), the hat and the edge stay dark
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.76f) / 0.22f);
                    float a = ring * ring * (0.85f + 0.15f * Mathf.PerlinNoise(x * 0.3f, y * 0.3f));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            discMat = RuntimeMaterials.NewParticle("BrakeDiscGlow", true);
            if (discMat != null) discMat.SetTexture("_BaseMap", tex);
            streakMat = RuntimeMaterials.NewParticle("LampStreak", true);
            discMesh = new Mesh { name = "BrakeDiscQuad" };
            discMesh.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(-1f, 1f, 0f), new Vector3(1f, 1f, 0f) };
            discMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            discMesh.triangles = new[] { 0, 2, 1, 1, 2, 3, 0, 1, 2, 1, 3, 2 };   // double-sided, own winding per side
            discMesh.RecalculateBounds();
        }

        void LateUpdate()
        {
            if (car == null || !car.IsInitialised) return;
            float dt = Time.deltaTime;
            var st = car.State;
            float v = st.Speed;
            float brake = Mathf.Clamp01(Mathf.Max(st.AppliedBrake, car.Controls.Brake));
            float bias = car.Definition.Brakes.FrontBias;
            for (int i = 0; i < 4; i++)
            {
                float share = i < 2 ? bias : 1f - bias;
                // heat from the power dissipated (brake x speed), cooled faster at speed (airflow)
                heat[i] += brake * share * 2f * (v / 45f) * dt * 1.1f;
                heat[i] -= (0.08f + 0.004f * v) * heat[i] * dt + 0.015f * dt;
                heat[i] = Mathf.Clamp01(heat[i]);
                var d = discs[i];
                if (d == null) continue;
                float glow = Mathf.InverseLerp(0.28f, 1f, heat[i]);
                d.enabled = glow > 0.01f;
                if (!d.enabled) continue;
                Color c = glow < 0.5f ? Color.Lerp(new Color(0.45f, 0.02f, 0f), new Color(1f, 0.28f, 0.02f), glow * 2f)
                                      : Color.Lerp(new Color(1f, 0.28f, 0.02f), new Color(1f, 0.85f, 0.45f), (glow - 0.5f) * 2f);
                block.SetColor("_BaseColor", c * (0.6f + 1.8f * glow));
                d.SetPropertyBlock(block);
            }
            float boost = nitro != null ? nitro.BoostScalar : 0f;
            bool hardBrake = brake > 0.55f && v > 22f;
            Color sc = boost > 0.2f
                ? (nitro.Mode == NitroMode.Overdrive ? new Color(1f, 0.25f, 0.85f) : nitro.Mode == NitroMode.CleanBoost ? new Color(0.35f, 1f, 0.6f) : new Color(0.3f, 0.7f, 1f))
                : new Color(1f, 0.08f, 0.04f);
            float night = WorldEnvironment.Instance != null ? WorldEnvironment.Instance.NightFactor : 0f;
            // light trails read at night and under nitro; a daytime brake trail is a faint hint only
            float k = boost > 0.2f ? 0.4f + 0.4f * night : 0.12f + 0.5f * night;
            bool brakeTrail = hardBrake && night > 0.15f;
            foreach (var tr in streaks)
            {
                if (tr == null) continue;
                tr.emitting = (boost > 0.2f || brakeTrail) && v > 15f;
                // D2 (round 3): the streak follows the car's path with a length set in metres, not seconds: about 3 m at
                // 60 km/h growing to 9 m at 300 km/h, so it neither hangs behind at low speed nor vanishes at top speed
                float lengthM = Mathf.Lerp(3f, 9f, Mathf.InverseLerp(16f, 83f, v)) * (boost > 0.2f ? 1.3f : 1f);
                tr.time = Mathf.Clamp(lengthM / Mathf.Max(5f, v), 0.05f, 0.3f);
                tr.startColor = new Color(sc.r, sc.g, sc.b, k);
                tr.endColor = new Color(sc.r, sc.g, sc.b, 0f);
            }
        }
    }
}
