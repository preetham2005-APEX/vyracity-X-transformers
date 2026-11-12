using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.Nitro;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Visual feedback driven by the simulation: tyre smoke and skid marks from tyre slip, brake/tail lights
    /// from brake input, headlights at night, and nitro exhaust flames that follow the actual boost scalar.
    /// </summary>
    public sealed class VehicleFX : MonoBehaviour
    {
        VehicleController car;
        NitroSystem nitro;
        readonly ParticleSystem[] smoke = new ParticleSystem[4];
        /// <summary>BUG-151 diagnostics: live tyre-smoke particles on all wheels, and the peak emission intensity (0-1)
        /// since <see cref="ResetSmokePeak"/>.</summary>
        public int SmokeParticles { get { int n = 0; foreach (var s in smoke) if (s != null) n += s.particleCount; return n; } }
        public float SmokePeak { get; private set; }
        public void ResetSmokePeak() => SmokePeak = 0f;
        readonly int[] lastMark = { -1, -1, -1, -1 };
        Renderer[] brakeRenderers, headRenderers;
        MaterialPropertyBlock mpb;
        Light[] headLights;
        Transform[] flames;
        Light flameLight;
        ParticleSystem sparks;
        TrailRenderer[] trails;
        Renderer[] lampRenderers;
        int[] lampSlots;
        LampFn[] lampFns;

        /// <summary>BUG-064 (round 3): what a lamp material slot does, so braking lights the brake lamps only.</summary>
        public enum LampFn { Head, Rear, Reverse, Combined, None }

        /// <summary>Classifies an imported lamp material by its name; a combined lights-and-glass material falls back to
        /// where its renderer sits on the car (rear third = rear lamps, front third = headlights).</summary>
        public static LampFn ClassifyLamp(string materialName, float localZ, float halfLength)
        {
            string n = materialName.ToLowerInvariant();
            if (n.Contains("housing") || n.Contains("clamp") || n.Contains("chrome") || n.Contains("metal") || n.Contains("plastic")) return LampFn.None;
            if (n.Contains("reverse")) return LampFn.Reverse;
            if (n.Contains("brake") || n.Contains("tail") || n.Contains("red_strip") || n.Contains("rear")) return LampFn.Rear;
            if (n.Contains("head") || n.Contains("hdlight") || n.Contains("beam") || n.Contains("day_light")) return LampFn.Head;
            if (halfLength > 0.1f)
            {
                if (localZ < -0.33f * halfLength) return LampFn.Rear;
                if (localZ > 0.33f * halfLength) return LampFn.Head;
            }
            return LampFn.Combined;
        }
        float backfire, prevThrottle;
        static Material smokeMat, flameMat, sparkMat, trailMat;
        MaterialPropertyBlock flameBlock;
        static Texture2D puff;

        void OnDestroy()
        {
            if (car != null) car.Scraped -= OnScraped;
        }

        /// <summary>Sparks where the body grinds along a wall (the controller reports glancing hits and scrapes).</summary>
        void OnScraped(Vector3 point, Vector3 normal, float intensity)
        {
            if (sparks == null || intensity < 0.05f) return;
            Vector3 along = Vector3.ProjectOnPlane(car.Body.linearVelocity, normal);
            int count = Mathf.CeilToInt(intensity * 26f);
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                ep.position = point + Random.insideUnitSphere * 0.15f;
                ep.velocity = along * Random.Range(0.25f, 0.7f) + normal * Random.Range(0.5f, 3f) + Vector3.up * Random.Range(1f, 4f) + Random.insideUnitSphere * 2f;
                ep.startLifetime = Random.Range(0.2f, 0.55f);
                ep.startSize = Random.Range(0.025f, 0.06f);
                sparks.Emit(ep, 1);
            }
        }

        public void Bind(VehicleController v, NitroSystem n, bool headlights)
        {
            car = v;
            nitro = n;
            mpb = new MaterialPropertyBlock();
            EnsureMaterials();
            var d = v.Definition;
            for (int i = 0; i < 4; i++)
            {
                var axle = i < 2 ? d.Front : d.Rear;
                float x = (i % 2 == 0 ? -0.5f : 0.5f) * axle.TrackWidth;
                float z = i < 2 ? d.Wheelbase * 0.5f : -d.Wheelbase * 0.5f;
                smoke[i] = MakeSmoke(new Vector3(x, 0.15f, z));
            }
            var brakes = new List<Renderer>();
            var heads = new List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                string n0 = r.name;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    if (m.name.Contains("LightTail") || n0.Contains("Brake") || n0.Contains("Tail")) { brakes.Add(r); break; }
                    if (m.name.Contains("LightHead") || n0.Contains("Lights_Head")) { heads.Add(r); break; }
                }
            }
            brakeRenderers = brakes.ToArray();
            headRenderers = heads.ToArray();
            if (headlights)
            {
                headLights = new Light[2];
                for (int i = 0; i < 2; i++)
                {
                    var go = new GameObject("HeadLight" + i);
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = new Vector3(i == 0 ? -0.62f : 0.62f, 0.68f, d.Chassis.Dimensions.z * 0.5f - 0.1f);
                    go.transform.localRotation = Quaternion.Euler(6f, i == 0 ? -3f : 3f, 0f);
                    var l = go.AddComponent<Light>();
                    l.type = LightType.Spot;
                    l.spotAngle = 62f;
                    l.innerSpotAngle = 30f;
                    l.range = 60f;
                    l.intensity = 14f;
                    l.color = new Color(1f, 0.95f, 0.86f);
                    l.shadows = i == 0 ? LightShadows.Soft : LightShadows.None;
                    headLights[i] = l;
                }
            }
            // D2 (round 3): shock-cone flames instead of a stretched capsule primitive: three crossed quads along -Z with a
            // procedural texture (white-hot core, coloured sheath, three Mach diamonds), plus embers from each exhaust
            flames = new Transform[2];
            embers = new ParticleSystem[2];
            for (int i = 0; i < 2; i++)
            {
                var f = new GameObject("NitroFlame" + i);
                f.transform.SetParent(transform, false);
                f.transform.localPosition = new Vector3(i == 0 ? -0.35f : 0.35f, 0.32f, -d.Chassis.Dimensions.z * 0.5f - 0.2f);
                f.transform.localScale = Vector3.zero;
                f.AddComponent<MeshFilter>().sharedMesh = FlameMesh();
                var r = f.AddComponent<MeshRenderer>();
                r.sharedMaterial = coneMat != null ? coneMat : flameMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                flames[i] = f.transform;
                embers[i] = MakeEmbers(f.transform.localPosition);
            }
            var fl = new GameObject("FlameLight");
            fl.transform.SetParent(transform, false);
            fl.transform.localPosition = new Vector3(0f, 0.4f, -d.Chassis.Dimensions.z * 0.5f - 0.6f);
            flameLight = fl.AddComponent<Light>();
            flameLight.type = LightType.Point;
            flameLight.range = 7f;
            flameLight.intensity = 0f;

            // Nitro light trails (Asphalt-style streaks from the exhausts).
            trails = new TrailRenderer[2];
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("NitroTrail" + i);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(i == 0 ? -0.35f : 0.35f, 0.32f, -d.Chassis.Dimensions.z * 0.5f - 0.2f);
                var tr = go.AddComponent<TrailRenderer>();
                tr.time = 0.35f;
                tr.minVertexDistance = 0.4f;
                tr.widthCurve = AnimationCurve.Linear(0f, 0.22f, 1f, 0f);
                tr.sharedMaterial = trailMat;
                tr.shadowCastingMode = ShadowCastingMode.Off;
                tr.emitting = false;
                trails[i] = tr;
            }

            // Sparks for wall scrapes.
            var sg = new GameObject("Sparks");
            sg.transform.SetParent(transform, false);
            sparks = sg.AddComponent<ParticleSystem>();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var sm = sparks.main;
            sm.loop = false;
            sm.playOnAwake = false;
            sm.simulationSpace = ParticleSystemSimulationSpace.World;
            sm.gravityModifier = 1.2f;
            sm.maxParticles = 400;
            sm.startColor = new Color(1f, 0.72f, 0.3f, 1f);
            var sem = sparks.emission;
            sem.rateOverTime = 0f;
            var scol = sparks.colorOverLifetime;
            scol.enabled = true;
            var sgrad = new Gradient();
            sgrad.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.9f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.35f, 0.05f), 1f) },
                          new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            scol.color = sgrad;
            var sr = sg.GetComponent<ParticleSystemRenderer>();
            sr.renderMode = ParticleSystemRenderMode.Stretch;
            sr.velocityScale = 0.04f;
            sr.lengthScale = 1.5f;
            sr.sharedMaterial = sparkMat;
            sr.shadowCastingMode = ShadowCastingMode.Off;
            sparks.Play();
            v.Scraped -= OnScraped;
            v.Scraped += OnScraped;

            // Imported cars: lamp materials (M_Lamp_*) glow by slot and by function (BUG-064): headlights at night,
            // brake/tail lamps at night and under braking, reverse lamps when reversing; housings and chrome stay dark.
            var lr = new List<Renderer>();
            var ls = new List<int>();
            var lf = new List<LampFn>();
            float halfLen = d.Chassis.Dimensions.z * 0.5f;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                float z = transform.InverseTransformPoint(r.bounds.center).z;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].name.StartsWith("M_Lamp"))
                    {
                        var fn = ClassifyLamp(mats[i].name, z, halfLen);
                        if (fn == LampFn.None) continue;
                        lr.Add(r); ls.Add(i); lf.Add(fn);
                    }
            }
            lampRenderers = lr.ToArray();
            lampSlots = ls.ToArray();
            lampFns = lf.ToArray();
        }

        static void EnsureMaterials()
        {
            if (smokeMat != null) return;
            puff = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 32f;
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (0.75f + 0.25f * Mathf.PerlinNoise(x * 0.15f, y * 0.15f));
                    puff.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            puff.Apply();
            // Copies of the library's particle templates (card F1); null only when the library is missing (RuntimeMaterials logs it).
            // D2: soft particles, so smoke fades where it meets the road and the car instead of cutting hard edges through it
            // (pass 38: the plain template showed a flat slab through the body; the brake close-up haze, BUG-151, is not
            // caused by the material: it appears with both)
            smokeMat = RuntimeMaterials.NewParticleSoft("TyreSmoke");
            if (smokeMat != null) smokeMat.SetColor("_BaseColor", new Color(0.85f, 0.86f, 0.88f, 0.55f));
            if (smokeMat != null) smokeMat.SetTexture("_BaseMap", puff);
            flameMat = Particle("NitroFlame", true, new Color(1f, 0.55f, 0.15f, 0.8f));
            coneMat = Particle("NitroCone", true, Color.white);
            if (coneMat != null) coneMat.SetTexture("_BaseMap", FlameTexture());
            emberMat = Particle("NitroEmber", true, new Color(1f, 0.7f, 0.3f, 1f));
            sparkMat = Particle("Sparks", true, new Color(1f, 0.8f, 0.45f, 1f));
            trailMat = Particle("NitroTrail", true, new Color(1f, 1f, 1f, 0.7f));
        }

        CarDamage damage;
        bool damageLooked;

        /// <summary>1 for intact lamps, 0 for smashed ones; in between the lamp flickers (a loose connection).</summary>
        float LampFactor(bool front)
        {
            if (damage == null) return 1f;
            float h = damage.LampHealth(front);
            if (h >= 0.999f || h <= 0.001f) return h;
            float n = Mathf.PerlinNoise(Time.time * 9f, front ? 0.3f : 5.1f);
            return n < 1f - h ? 0.05f : 1f;
        }

        static Material coneMat, emberMat;
        static Mesh flameMesh;
        static Texture2D flameTex;
        ParticleSystem[] embers;
        float prevBoost, pulse;

        /// <summary>Three quads crossed at 60 degrees along -Z (0 at the exhaust, -1 at the tip), width 1.</summary>
        static Mesh FlameMesh()
        {
            if (flameMesh != null) return flameMesh;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int k = 0; k < 3; k++)
            {
                var side = Quaternion.AngleAxis(k * 60f, Vector3.forward) * Vector3.right * 0.5f;
                int b = v.Count;
                v.Add(-side); v.Add(side); v.Add(-side + Vector3.back); v.Add(side + Vector3.back);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1));
                t.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3, b, b + 1, b + 2, b + 1, b + 3, b + 2 });
            }
            flameMesh = new Mesh { name = "NitroShockCone" };
            flameMesh.SetVertices(v); flameMesh.SetUVs(0, uv); flameMesh.SetTriangles(t, 0);
            flameMesh.RecalculateBounds();
            return flameMesh;
        }

        /// <summary>u across (0-1), v along the jet (0 at the nozzle): a white core, a sheath that narrows to the tip,
        /// three Mach diamonds (bright nodes where the core pinches) and flicker noise. Alpha carries the shape; rgb stays
        /// near white so the per-car _BaseColor tints it by boost mode.</summary>
        static Texture2D FlameTexture()
        {
            if (flameTex != null) return flameTex;
            const int W = 64, H = 256;
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                float vv = (y + 0.5f) / H;
                float radius = Mathf.Lerp(0.42f, 0.04f, Mathf.Pow(vv, 0.8f));
                float diamonds = Mathf.Pow(Mathf.Abs(Mathf.Cos(vv * Mathf.PI * 3.5f)), 6f) * (1f - vv);
                for (int x = 0; x < W; x++)
                {
                    float u = Mathf.Abs((x + 0.5f) / W - 0.5f);
                    float sheath = Mathf.Clamp01(1f - u / radius);
                    float core = Mathf.Clamp01(1f - u / (radius * 0.35f + 0.02f)) * (1f - vv * 0.7f);
                    float n = Mathf.PerlinNoise(x * 0.15f, y * 0.06f);
                    float a = Mathf.Clamp01(sheath * sheath * (0.55f + 0.45f * n) * (1f - vv * vv) + core * 0.9f + diamonds * sheath * 1.2f);
                    float white = Mathf.Clamp01(core + diamonds * 0.8f);
                    px[y * W + x] = new Color32(255, (byte)(255 * Mathf.Lerp(0.85f, 1f, white)), (byte)(255 * Mathf.Lerp(0.75f, 1f, white)), (byte)(255 * a));
                }
            }
            flameTex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "NitroShockCone", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            flameTex.SetPixels32(px);
            flameTex.Apply(true, true);
            return flameTex;
        }

        ParticleSystem MakeEmbers(Vector3 local)
        {
            var go = new GameObject("NitroEmbers");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.LookRotation(Vector3.back);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            main.gravityModifier = 0.4f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 9f;
            shape.radius = 0.04f;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.9f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.35f, 0.05f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = emberMat;
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.04f;
            r.lengthScale = 1.5f;
            r.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        static Material Particle(string name, bool additive, Color colour)
        {
            var m = RuntimeMaterials.NewParticle(name, additive);
            if (m != null) m.SetColor("_BaseColor", colour);
            return m;
        }

        ParticleSystem MakeSmoke(Vector3 local)
        {
            var go = new GameObject("TyreSmoke");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = local;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 220;
            main.gravityModifier = -0.04f;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.25f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.6f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.1f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = smokeMat;
            rend.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        void LateUpdate()
        {
            if (car == null || !car.IsInitialised) return;
            var st = car.State;
            for (int i = 0; i < 4; i++)
            {
                var w = car.GetWheel(i);
                float slip = w.Grounded ? Mathf.Max(Mathf.Abs(w.SlipAngleDeg) / 20f, Mathf.Abs(w.SlipRatio) / 0.3f) : 0f;
                float intensity = Mathf.Clamp01(slip - 0.6f) * Mathf.Clamp01((st.Speed + Mathf.Abs(w.AngularVelocity) * 0.3f) / 6f);
                bool soft = w.Surface != null && (w.Surface.name.Contains("gravel") || w.Surface.name.Contains("dirt") || w.Surface.name.Contains("grass"));
                var em = smoke[i].emission;
                em.rateOverTime = intensity * (soft ? 30f : 55f);
                SmokePeak = Mathf.Max(SmokePeak, intensity);
                var main = smoke[i].main;   // BUG-080: dust on soft ground, white smoke again back on tarmac
                main.startColor = soft ? new Color(0.55f, 0.47f, 0.36f, 1f) : Color.white;
                if (intensity > 0.15f && w.Grounded) lastMark[i] = SkidMarks.Add(w.ContactPoint, w.ContactNormal, car.Body.GetPointVelocity(w.ContactPoint), 0.24f, Mathf.Clamp01(intensity * 1.3f), lastMark[i]);
                else lastMark[i] = -1;
            }
            // lights
            float night = WorldEnvironment.Instance != null ? WorldEnvironment.Instance.NightFactor : 0f;
            float brake = Mathf.Max(st.AppliedBrake, car.Controls.Brake);
            if (brakeRenderers != null)
            {
                var col = new Color(1f, 0.06f, 0.03f) * (0.6f + night * 0.8f + brake * 5f);
                foreach (var r in brakeRenderers)
                {
                    r.GetPropertyBlock(mpb);
                    mpb.SetColor("_EmissionColor", col);
                    r.SetPropertyBlock(mpb);
                }
            }
            // D2 (round 3): crash damage breaks lamps by zone; a half-broken lamp flickers
            if (damage == null && !damageLooked) { damage = GetComponent<CarDamage>(); damageLooked = true; }
            float headOk = LampFactor(true), rearOk = LampFactor(false);
            if (headRenderers != null)
            {
                var col = new Color(1f, 0.97f, 0.9f) * (0.4f + night * 3f) * headOk;
                foreach (var r in headRenderers)
                {
                    r.GetPropertyBlock(mpb);
                    mpb.SetColor("_EmissionColor", col);
                    r.SetPropertyBlock(mpb);
                }
            }
            if (headLights != null)
                foreach (var l in headLights) { l.enabled = night > 0.3f && headOk > 0.05f; l.intensity = 14f * night * headOk; }
            if (lampRenderers != null && lampRenderers.Length > 0)
            {
                bool reversing = car.Controls.WantsReverse;
                for (int i = 0; i < lampRenderers.Length; i++)
                {
                    float k = lampFns[i] switch
                    {
                        LampFn.Head => 0.15f + night * 1.6f,
                        LampFn.Rear => 0.15f + night * 1.0f + brake * 1.8f,
                        LampFn.Reverse => reversing ? 1.6f : 0.05f,
                        _ => 0.15f + night * 1.2f,
                    };
                    mpb.Clear();
                    k *= lampFns[i] == LampFn.Head ? headOk : lampFns[i] == LampFn.Combined ? Mathf.Min(headOk, rearOk) : rearOk;
                    mpb.SetColor("_EmissionColor", Color.white * k);
                    lampRenderers[i].SetPropertyBlock(mpb, lampSlots[i]);
                }
                mpb.Clear();
            }
            // exhaust backfire: lifting off hard at high revs pops a short flame
            float throttle = car.Controls.Throttle;
            if (prevThrottle > 0.8f && throttle < 0.15f && st.Speed > 15f && car.Definition.Engine.LimiterRpm > 0f
                && st.EngineRpm > 0.7f * car.Definition.Engine.LimiterRpm && Random.value < 0.7f)
                backfire = 0.14f;
            prevThrottle = throttle;
            backfire = Mathf.Max(0f, backfire - Time.deltaTime);
            float pop = backfire > 0f ? 0.35f + 0.35f * Mathf.PerlinNoise(Time.time * 40f, 1.7f) : 0f;
            // nitro flames
            float b = nitro != null ? nitro.BoostScalar : 0f;
            Color fc = nitro != null && nitro.Mode == NitroMode.Overdrive ? new Color(1f, 0.25f, 0.85f) : nitro != null && nitro.Mode == NitroMode.CleanBoost ? new Color(0.35f, 0.75f, 1f) : new Color(1f, 0.55f, 0.15f);
            if (b < 0.01f && pop > 0f) fc = new Color(1f, 0.5f, 0.12f);
            float flick = 0.8f + 0.4f * Mathf.PerlinNoise(Time.time * 18f, 0.3f);
            float f = Mathf.Max(b, pop);
            // a light pulse and a longer jet for 0.25 s when a boost starts (the Asphalt kick)
            if (b > 0.2f && prevBoost <= 0.2f) pulse = 0.25f;
            prevBoost = b;
            pulse = Mathf.Max(0f, pulse - Time.deltaTime);
            float kick = 1f + 1.4f * (pulse / 0.25f);
            for (int i = 0; i < flames.Length; i++)
            {
                float w = 0.24f * Mathf.Sqrt(f) * (0.9f + 0.1f * flick);
                float len = (0.55f + 0.9f * f) * flick * kick * (b > 0.01f ? 1f : 0.6f);
                flames[i].localScale = f > 0.01f ? new Vector3(w, w, len) : Vector3.zero;
                if (embers != null && embers[i] != null)
                {
                    var em = embers[i].emission;
                    em.rateOverTime = b > 0.2f ? 60f * b : pop > 0f ? 40f : 0f;
                }
            }
            // BUG-081: per-renderer colour (the flame material is shared by every car)
            flameBlock ??= new MaterialPropertyBlock();
            flameBlock.SetColor("_BaseColor", new Color(fc.r, fc.g, fc.b, 0.85f));
            foreach (var fl in flames) fl.GetComponent<Renderer>().SetPropertyBlock(flameBlock);
            flameLight.color = fc;
            flameLight.intensity = f * 6f * flick * (1f + 2f * (pulse / 0.25f));
            if (trails != null)
                foreach (var tr in trails)
                {
                    tr.emitting = b > 0.2f && st.Speed > 8f;
                    tr.startColor = new Color(fc.r, fc.g, fc.b, 0.75f * b);
                    tr.endColor = new Color(fc.r, fc.g, fc.b, 0f);
                }
        }
    }

    /// <summary>Shared skid-mark mesh: a ring buffer of quads laid where tyres slide.</summary>
    public static class SkidMarks
    {
        const int MaxQuads = 3000;
        static Mesh mesh;
        static Vector3[] verts;
        static Color32[] colors;
        static Vector3[] lastLeft = new Vector3[MaxQuads], lastRight = new Vector3[MaxQuads];
        static int next;
        static bool dirty;
        static GameObject go;

        static void Ensure()
        {
            if (go != null) return;
            go = new GameObject("SkidMarks");
            mesh = new Mesh { name = "SkidMarks", indexFormat = IndexFormat.UInt32 };
            verts = new Vector3[MaxQuads * 4];
            colors = new Color32[MaxQuads * 4];
            var tris = new int[MaxQuads * 6];
            var uvs = new Vector2[MaxQuads * 4];
            for (int i = 0; i < MaxQuads; i++)
            {
                tris[i * 6] = i * 4; tris[i * 6 + 1] = i * 4 + 2; tris[i * 6 + 2] = i * 4 + 1;
                tris[i * 6 + 3] = i * 4 + 1; tris[i * 6 + 4] = i * 4 + 2; tris[i * 6 + 5] = i * 4 + 3;
                uvs[i * 4] = new Vector2(0, 0); uvs[i * 4 + 1] = new Vector2(1, 0); uvs[i * 4 + 2] = new Vector2(0, 1); uvs[i * 4 + 3] = new Vector2(1, 1);
            }
            mesh.vertices = verts;
            mesh.colors32 = colors;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 20000f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            var m = RuntimeMaterials.NewParticle("SkidMark", false); // null only without the library (RuntimeMaterials logs it)
            if (m != null)
            {
                m.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.02f, 0.8f));
                m.renderQueue = 2990;
            }
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>Adds a segment continuing from <paramref name="previous"/> (or starting a new strip). Returns the new index.</summary>
        public static int Add(Vector3 point, Vector3 normal, Vector3 velocity, float width, float opacity, int previous)
        {
            Ensure();
            if (velocity.sqrMagnitude < 0.25f) return previous;
            var p = point + normal * 0.025f;
            var dir = Vector3.ProjectOnPlane(velocity, normal).normalized;
            var side = Vector3.Cross(dir, normal).normalized * (width * 0.5f);
            Vector3 l = p + side, r = p - side;
            int i = next;
            if (previous >= 0 && (lastLeft[previous] - l).sqrMagnitude > 0.04f && (lastLeft[previous] - l).sqrMagnitude < 9f)
            {
                verts[i * 4] = lastLeft[previous];
                verts[i * 4 + 1] = lastRight[previous];
            }
            else if (previous >= 0 && (lastLeft[previous] - l).sqrMagnitude <= 0.04f) return previous;
            else
            {
                verts[i * 4] = l;
                verts[i * 4 + 1] = r;
            }
            verts[i * 4 + 2] = l;
            verts[i * 4 + 3] = r;
            byte a = (byte)(opacity * 200);
            var c = new Color32(255, 255, 255, a);
            for (int k = 0; k < 4; k++) colors[i * 4 + k] = c;
            lastLeft[i] = l;
            lastRight[i] = r;
            next = (next + 1) % MaxQuads;
            dirty = true;
            return i;
        }

        public static void Flush()
        {
            if (!dirty || mesh == null) return;
            mesh.vertices = verts;
            mesh.colors32 = colors;
            dirty = false;
        }

        public static void Clear()
        {
            if (mesh == null) return;
            for (int i = 0; i < verts.Length; i++) { verts[i] = Vector3.zero; colors[i] = default; }
            dirty = true;
            Flush();
        }
    }
}
