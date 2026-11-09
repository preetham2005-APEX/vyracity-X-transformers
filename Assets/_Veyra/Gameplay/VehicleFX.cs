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
        readonly int[] lastMark = { -1, -1, -1, -1 };
        Renderer[] brakeRenderers, headRenderers;
        MaterialPropertyBlock mpb;
        Light[] headLights;
        Transform[] flames;
        Light flameLight;
        static Material smokeMat, flameMat;
        static Texture2D puff;

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
            flames = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                var f = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                DestroyImmediate(f.GetComponent<Collider>());
                f.name = "NitroFlame" + i;
                f.transform.SetParent(transform, false);
                f.transform.localPosition = new Vector3(i == 0 ? -0.35f : 0.35f, 0.32f, -d.Chassis.Dimensions.z * 0.5f - 0.25f);
                f.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                f.transform.localScale = Vector3.zero;
                var r = f.GetComponent<Renderer>();
                r.sharedMaterial = flameMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                flames[i] = f.transform;
            }
            var fl = new GameObject("FlameLight");
            fl.transform.SetParent(transform, false);
            fl.transform.localPosition = new Vector3(0f, 0.4f, -d.Chassis.Dimensions.z * 0.5f - 0.6f);
            flameLight = fl.AddComponent<Light>();
            flameLight.type = LightType.Point;
            flameLight.range = 7f;
            flameLight.intensity = 0f;
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
            smokeMat = RuntimeMaterials.NewParticle("TyreSmoke", false);
            smokeMat.SetTexture("_BaseMap", puff);
            smokeMat.SetColor("_BaseColor", new Color(0.85f, 0.86f, 0.88f, 0.55f));
            flameMat = RuntimeMaterials.NewParticle("NitroFlame", true);
            flameMat.SetColor("_BaseColor", new Color(1f, 0.55f, 0.15f, 0.8f));
        }

        static void MakeTransparent(Material m, bool additive)
        {
            m.SetFloat("_Surface", 1f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", additive ? (int)BlendMode.One : (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.renderQueue = 3000;
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
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
                if (soft)
                {
                    var main = smoke[i].main;
                    main.startColor = new Color(0.55f, 0.47f, 0.36f, 1f);
                }
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
            if (headRenderers != null)
            {
                var col = new Color(1f, 0.97f, 0.9f) * (0.4f + night * 3f);
                foreach (var r in headRenderers)
                {
                    r.GetPropertyBlock(mpb);
                    mpb.SetColor("_EmissionColor", col);
                    r.SetPropertyBlock(mpb);
                }
            }
            if (headLights != null)
                foreach (var l in headLights) { l.enabled = night > 0.3f; l.intensity = 14f * night; }
            // nitro flames
            float b = nitro != null ? nitro.BoostScalar : 0f;
            Color fc = nitro != null && nitro.Mode == NitroMode.Overdrive ? new Color(1f, 0.25f, 0.85f) : nitro != null && nitro.Mode == NitroMode.CleanBoost ? new Color(0.35f, 0.75f, 1f) : new Color(1f, 0.55f, 0.15f);
            float flick = 0.8f + 0.4f * Mathf.PerlinNoise(Time.time * 18f, 0.3f);
            for (int i = 0; i < flames.Length; i++)
            {
                flames[i].localScale = b > 0.01f ? new Vector3(0.16f * Mathf.Sqrt(b), 0.45f * b * flick, 0.16f * Mathf.Sqrt(b)) : Vector3.zero;
                flames[i].localPosition = new Vector3(flames[i].localPosition.x, flames[i].localPosition.y, -car.Definition.Chassis.Dimensions.z * 0.5f - 0.25f - 0.4f * b * flick);
            }
            flameMat.SetColor("_BaseColor", new Color(fc.r, fc.g, fc.b, 0.85f));
            flameLight.color = fc;
            flameLight.intensity = b * 6f * flick;
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
            var m = RuntimeMaterials.NewParticle("SkidMark", false);
            m.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.02f, 0.8f));
            m.renderQueue = 2990;
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
