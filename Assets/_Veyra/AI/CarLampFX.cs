using System;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.World;

namespace VeyraCity.AI
{
    /// <summary>
    /// Lamp glow for any car with Lamp_FL/FR/RL/RR anchors (written by the Blender car pipeline at the real lamp
    /// positions): camera-facing additive glows on the head and tail lamps that fade in at dusk, tail lamps that flare
    /// under braking, and red light streaks behind the tail lamps at speed after dark: the GTA V freeway-at-night and
    /// Cyberpunk light-trail look. Used by the player, AI racers and traffic (speed and brake come from delegates).
    /// </summary>
    public sealed class CarLampFX : MonoBehaviour
    {
        static readonly string[] Names = { "Lamp_FL", "Lamp_FR", "Lamp_RL", "Lamp_RR" };
        static Material glowMat, trailMat, beamMat;
        static Mesh quad;

        readonly Transform[] glows = new Transform[4];
        readonly Renderer[] glowRenderers = new Renderer[4];
        readonly TrailRenderer[] trails = new TrailRenderer[2];
        readonly Renderer[] beams = new Renderer[2];
        static Mesh cone;
        MaterialPropertyBlock block;
        Func<float> speed, brake;
        bool any;
        Light headLight, tailLight;

        public bool HasLamps => any;
        /// <summary>Real lights may be switched off by a budget (traffic gives them to its nearest few cars only).</summary>
        public bool RealLightsOn = true;
        public bool HasRealLights => headLight != null;

        /// <summary>
        /// Real lights for the cars that matter on screen (player and AI racers): one spot that throws the headlight pool
        /// on the road ahead at night (GTA V's night driving) and a small red light that spills the tail and brake lamps
        /// onto the road behind (visible from the chase camera, and in wet-road reflections). Traffic keeps glows only.
        /// </summary>
        public void AddRealLights()
        {
            if (headLight != null) return;
            Vector3 front = Vector3.zero, rear = Vector3.zero;
            int nf = 0, nr = 0;
            for (int i = 0; i < 4; i++)
            {
                if (glows[i] == null) continue;
                var lp = transform.InverseTransformPoint(glows[i].parent.position);
                if (i < 2) { front += lp; nf++; } else { rear += lp; nr++; }
            }
            if (nf == 0 || nr == 0) return;
            front /= nf;
            rear /= nr;
            // The spot sits above the lamps (a lamp-height source at grazing angles barely lights the road beyond a few
            // metres) and aims a little down, so the pool reaches 8–30 m ahead.
            var hg = new GameObject("HeadlightSpot");
            hg.transform.SetParent(transform, false);
            hg.transform.localPosition = front + new Vector3(0f, 0.55f, 0.1f);
            hg.transform.localRotation = Quaternion.Euler(7f, 0f, 0f);
            headLight = hg.AddComponent<Light>();
            headLight.type = LightType.Spot;
            headLight.spotAngle = 78f;
            headLight.innerSpotAngle = 34f;
            headLight.range = 55f;
            headLight.color = new Color(1f, 0.92f, 0.8f);
            headLight.shadows = LightShadows.None;
            headLight.intensity = 0f;
            headLight.enabled = false;
            var tg = new GameObject("TailLight");
            tg.transform.SetParent(transform, false);
            // A wide spot facing backwards and down: lights the road behind, never the car's own bumper (iteration 4 used a
            // point light, which washed the whole rear of the car red).
            tg.transform.localPosition = rear + new Vector3(0f, 0.1f, -0.12f);
            tg.transform.localRotation = Quaternion.Euler(28f, 180f, 0f);
            tailLight = tg.AddComponent<Light>();
            tailLight.type = LightType.Spot;
            tailLight.spotAngle = 150f;
            tailLight.innerSpotAngle = 70f;
            tailLight.range = 8f;
            tailLight.color = new Color(1f, 0.1f, 0.05f);
            tailLight.shadows = LightShadows.None;
            tailLight.intensity = 0f;
            tailLight.enabled = false;
        }

        public void Bind(Func<float> speedMps, Func<float> brake01, bool trailsOn)
        {
            speed = speedMps;
            brake = brake01;
            Ensure();
            block = new MaterialPropertyBlock();
            var all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < 4; i++)
            {
                Transform anchor = null;
                foreach (var t in all) if (t.name == Names[i]) { anchor = t; break; }
                if (anchor == null) continue;
                any = true;
                var go = new GameObject("Glow_" + Names[i]);
                go.transform.SetParent(anchor, false);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = glowMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                glows[i] = go.transform;
                glowRenderers[i] = r;
                if (trailsOn && i < 2)
                {
                    // Faint light cone ahead of each headlight at night (headlight beams in the night haze).
                    var bg = new GameObject("Beam");
                    bg.transform.SetParent(anchor, false);
                    bg.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);
                    bg.AddComponent<MeshFilter>().sharedMesh = cone;
                    var br = bg.AddComponent<MeshRenderer>();
                    br.sharedMaterial = beamMat;
                    br.shadowCastingMode = ShadowCastingMode.Off;
                    br.receiveShadows = false;
                    beams[i] = br;
                }
                if (trailsOn && i >= 2)
                {
                    var tg = new GameObject("TailTrail");
                    tg.transform.SetParent(anchor, false);
                    tg.transform.localPosition = new Vector3(0f, 0f, -0.05f);
                    var tr = tg.AddComponent<TrailRenderer>();
                    tr.time = 0.18f;
                    tr.minVertexDistance = 0.6f;
                    tr.widthCurve = AnimationCurve.Linear(0f, 0.09f, 1f, 0.01f);
                    tr.sharedMaterial = trailMat;
                    tr.shadowCastingMode = ShadowCastingMode.Off;
                    tr.emitting = false;
                    trails[i - 2] = tr;
                }
            }
            if (!any) enabled = false;
        }

        static void Ensure()
        {
            if (glowMat != null) return;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "LampGlowTex" };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                    float core = Mathf.Clamp01(1f - d * 3.5f);
                    float halo = Mathf.Pow(Mathf.Clamp01(1f - d), 2.5f);
                    float a = Mathf.Clamp01(core + halo * 0.55f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            glowMat = RuntimeMaterials.NewParticle("LampGlow", true);
            glowMat.SetTexture("_BaseMap", tex);
            trailMat = RuntimeMaterials.NewParticle("LampTrail", true);
            // beam: fades from the lamp to 9 m
            var grad = new Texture2D(4, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "LampBeamTex" };
            for (int y = 0; y < 64; y++)
            {
                float v = y / 63f;
                float a = Mathf.Pow(1f - v, 1.6f) * Mathf.Clamp01(v * 8f);
                for (int x = 0; x < 4; x++) grad.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            grad.Apply();
            beamMat = RuntimeMaterials.NewParticle("LampBeam", true);
            beamMat.SetTexture("_BaseMap", grad);
            cone = BuildCone(9f, 2.1f, 14);
            quad = new Mesh { name = "LampGlowQuad" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            quad.RecalculateBounds();
        }

        /// <summary>Open cone along +Z, apex at the origin, double-sided; uv.y = distance along the beam (0..1).</summary>
        static Mesh BuildCone(float length, float radius, int sides)
        {
            var v = new System.Collections.Generic.List<Vector3>();
            var uv = new System.Collections.Generic.List<Vector2>();
            var tris = new System.Collections.Generic.List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                int b = v.Count;
                v.Add(Vector3.zero); uv.Add(new Vector2(0.5f, 0f));
                v.Add(new Vector3(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius * 0.55f, length)); uv.Add(new Vector2(0.5f, 1f));
                v.Add(new Vector3(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius * 0.55f, length)); uv.Add(new Vector2(0.5f, 1f));
                tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 1 });
            }
            var m = new Mesh { name = "LampBeamCone" };
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            float night = WorldEnvironment.Instance != null ? WorldEnvironment.Instance.NightFactor : 0f;
            float b = brake != null ? Mathf.Clamp01(brake()) : 0f;
            float v = speed != null ? speed() : 0f;
            Vector3 camPos = cam.transform.position;
            for (int i = 0; i < 4; i++)
            {
                if (glows[i] == null) continue;
                bool head = i < 2;
                // Glows are visible from the lamp's own side only (headlights from the front, tail lamps from behind).
                Vector3 toCam = (camPos - glows[i].position).normalized;
                float facing = Vector3.Dot(toCam, head ? transform.forward : -transform.forward);
                float vis = Mathf.Clamp01(facing * 1.4f + 0.25f);
                // Headlights glow after dusk only; tail lamps glow at night and flare under braking day or night.
                // iteration 4: stronger at chase distance (iteration 2 captures showed them too faint)
                float k = head ? night * 1.4f : (night * 1.0f + b * (0.8f + 0.8f * night));
                k *= vis;
                glowRenderers[i].enabled = k > 0.01f;
                if (!glowRenderers[i].enabled) continue;
                float size = head ? 0.75f : 0.6f + b * 0.2f;
                glows[i].localScale = Vector3.one * size;
                glows[i].rotation = Quaternion.LookRotation(-toCam, cam.transform.up);
                Color c = head ? new Color(1f, 0.93f, 0.8f) : new Color(1f, 0.08f, 0.04f);
                block.SetColor("_BaseColor", c * k);
                glowRenderers[i].SetPropertyBlock(block);
            }
            for (int j = 0; j < 2; j++)
            {
                if (beams[j] == null) continue;
                beams[j].enabled = night > 0.3f;
                if (!beams[j].enabled) continue;
                block.SetColor("_BaseColor", new Color(1f, 0.93f, 0.8f) * (0.09f * night));
                beams[j].SetPropertyBlock(block);
            }
            if (headLight != null)
            {
                headLight.enabled = RealLightsOn && night > 0.05f;
                headLight.intensity = 90f * night;
                float tk = 0.9f * night + b * (1.2f + 2.4f * night);
                tailLight.enabled = RealLightsOn && tk > 0.02f;
                tailLight.intensity = tk;
            }
            for (int j = 0; j < 2; j++)
            {
                if (trails[j] == null) continue;
                bool on = night > 0.4f && v > 22f;
                trails[j].emitting = on;
                var col = new Color(1f, 0.06f, 0.03f, 0.55f * night);
                trails[j].startColor = col;
                trails[j].endColor = new Color(col.r, col.g, col.b, 0f);
            }
        }
    }
}
