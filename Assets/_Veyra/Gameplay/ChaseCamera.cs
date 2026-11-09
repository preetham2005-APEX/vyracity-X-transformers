using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VeyraCity.Nitro;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Chase / close-chase / bonnet camera with racing "juice" (GTA framing + Asphalt sense of speed):
    ///  * follows the interpolated body, leans toward the direction of travel so the drift angle reads;
    ///  * FOV widens with speed and kicks on nitro; the camera drops a little lower and closer in drifts;
    ///  * trauma shake (shake = trauma²) from collisions, wall scrapes, kerb hits and hard landings, plus a light
    ///    high-speed rumble;
    ///  * speed-scaled camera motion blur (its own global Volume) and wind streaks past the lens above ~140 km/h.
    /// Every effect scales with <see cref="SpeedEffectsIntensity"/> (comfort setting).
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(Camera))]
    public sealed class ChaseCamera : MonoBehaviour
    {
        public enum Mode { Chase, CloseChase, Bonnet }

        [SerializeField] VehicleController target;
        public Mode CurrentMode = Mode.Chase;
        public bool LookBack;
        public float BaseFov = 60f;
        [Tooltip("Extra FOV at the reference speed (comfort setting scales it)")] public float SpeedFov = 14f;
        public float FovSpeedRef = 70f;
        [Tooltip("Extra FOV at full nitro")] public float NitroFov = 8f;
        [Range(0f, 1f)] public float SpeedEffectsIntensity = 1f;
        [Tooltip("Camera shake strength at full trauma: metres and degrees")] public float ShakeMetres = 0.22f, ShakeDegrees = 2.2f;
        [Tooltip("Motion blur at 250 km/h")] [Range(0f, 1f)] public float MaxMotionBlur = 0.45f;

        Camera cam;
        Vector3 smoothedDir = Vector3.forward;
        Vector3 smoothedOffset;
        bool snapped;
        NitroSystem nitro;
        float trauma, nitroKick, driftK, fov;
        readonly float[] lastCompression = new float[4];
        float lastAirTime;
        Volume blurVolume;
        MotionBlur blur;
        ParticleSystem streaks;

        public VehicleController Target => target;
        /// <summary>Adds camera trauma 0..1 (shake grows with its square and decays over about a second).</summary>
        public void AddTrauma(float amount) => trauma = Mathf.Clamp01(trauma + amount);

        void Awake()
        {
            cam = GetComponent<Camera>();
            fov = BaseFov;
        }

        void OnDestroy()
        {
            Unhook();
            if (blurVolume != null) Destroy(blurVolume.gameObject);
        }

        public void SetTarget(VehicleController v)
        {
            Unhook();
            target = v;
            snapped = false;
            trauma = 0f;
            nitro = v != null ? v.GetComponent<NitroSystem>() : null;
            if (v != null)
            {
                v.Collided += OnCollided;
                v.Scraped += OnScraped;
                for (int i = 0; i < 4; i++) lastCompression[i] = 0f;
            }
        }

        void Unhook()
        {
            if (target == null) return;
            target.Collided -= OnCollided;
            target.Scraped -= OnScraped;
        }

        void OnCollided(VehicleController v, Collision c)
        {
            float hit = c.relativeVelocity.magnitude;
            if (hit > 3f) AddTrauma(Mathf.Clamp01((hit - 3f) / 25f) * 0.8f);
        }

        void OnScraped(Vector3 point, Vector3 normal, float intensity) => AddTrauma(intensity * 0.05f);

        public void Cycle()
        {
            CurrentMode = (Mode)(((int)CurrentMode + 1) % 3);
            snapped = false;
        }

        void LateUpdate()
        {
            if (target == null || target.Body == null) return;
            float dt = Mathf.Max(1e-4f, Time.unscaledDeltaTime);
            Transform t = target.transform;
            Rigidbody rb = target.Body;
            Vector3 pos = t.position;
            Vector3 v = rb.linearVelocity;
            float speed = v.magnitude;
            var st = target.State;
            float boost = nitro != null ? nitro.BoostScalar : 0f;
            nitroKick = Mathf.Lerp(nitroKick, boost, 1f - Mathf.Exp(-6f * dt));
            driftK = Mathf.Lerp(driftK, st.DriftBlend, 1f - Mathf.Exp(-4f * dt));
            GatherTrauma(st, speed, dt);

            if (CurrentMode == Mode.Bonnet)
            {
                transform.SetPositionAndRotation(t.TransformPoint(new Vector3(0f, 1.12f, 0.35f)),
                    LookBack ? t.rotation * Quaternion.Euler(0f, 180f, 0f) : t.rotation);
                ApplyShake(dt, 0.4f);
                ApplyFov(speed, dt);
                UpdateSpeedEffects(speed, boost);
                return;
            }

            Vector3 fwdFlat = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            if (fwdFlat.sqrMagnitude < 1e-4f) fwdFlat = smoothedDir;
            fwdFlat.Normalize();
            Vector3 velFlat = Vector3.ProjectOnPlane(v, Vector3.up);
            Vector3 dir = fwdFlat;
            if (velFlat.magnitude > 3f && Vector3.Dot(velFlat, fwdFlat) > 0f)
            {
                // Frame the direction of travel (more so in a drift) so the slide angle is visible.
                float follow = Mathf.Lerp(0.45f, 0.7f, driftK) * Mathf.Clamp01((speed - 3f) / 15f);
                dir = Vector3.Slerp(fwdFlat, velFlat.normalized, follow);
            }
            if (LookBack) dir = -dir;

            float distance = CurrentMode == Mode.Chase ? 6.2f : 4.6f;
            float height = CurrentMode == Mode.Chase ? 1.95f : 1.45f;
            distance += Mathf.Clamp01(speed / 80f) * 1.2f * SpeedEffectsIntensity;
            distance += nitroKick * 0.6f * SpeedEffectsIntensity;     // pulled back by the boost
            distance -= driftK * 0.5f;
            height -= driftK * 0.25f;

            if (!snapped)
            {
                smoothedDir = dir;
                smoothedOffset = -dir * distance + Vector3.up * height;
                snapped = true;
            }
            smoothedDir = Vector3.Slerp(smoothedDir, dir, 1f - Mathf.Exp(-5f * dt)).normalized;
            Vector3 desiredOffset = -smoothedDir * distance + Vector3.up * height;
            smoothedOffset = Vector3.Lerp(smoothedOffset, desiredOffset, 1f - Mathf.Exp(-10f * dt));

            Vector3 camPos = pos + smoothedOffset;
            camPos.y = Mathf.Max(camPos.y, pos.y + 0.6f);
            Vector3 lookAt = pos + Vector3.up * 0.95f + smoothedDir * 2.2f;
            transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(lookAt - camPos, Vector3.up));
            ApplyShake(dt, 1f);
            ApplyFov(speed, dt);
            UpdateSpeedEffects(speed, boost);
        }

        void GatherTrauma(in VehicleState st, float speed, float dt)
        {
            // kerbs and potholes: sudden suspension compression
            for (int i = 0; i < 4; i++)
            {
                var w = target.GetWheel(i);
                float dc = w.Compression - lastCompression[i];
                lastCompression[i] = w.Compression;
                if (w.Grounded && dc > 0.035f) AddTrauma(Mathf.Clamp01((dc - 0.035f) * 6f) * 0.35f);
            }
            // hard landings
            if (lastAirTime > 0.35f && st.AirTime <= 0f) AddTrauma(Mathf.Clamp01(lastAirTime * 0.5f));
            lastAirTime = st.AirTime;
            trauma = Mathf.Max(0f, trauma - 1.4f * dt);
        }

        void ApplyShake(float dt, float scale)
        {
            float kmh = target.State.Speed * 3.6f;
            float rumble = Mathf.Clamp01((kmh - 150f) / 150f) * 0.18f + nitroKick * 0.12f;
            float s = (trauma * trauma + rumble * rumble) * SpeedEffectsIntensity * scale;
            if (s <= 1e-4f) return;
            float time = Time.unscaledTime * 22f;
            Vector3 offset = new Vector3(Mathf.PerlinNoise(time, 0.1f) - 0.5f, Mathf.PerlinNoise(0.7f, time) - 0.5f, 0f) * (2f * ShakeMetres * s);
            Vector3 angles = new Vector3(Mathf.PerlinNoise(time, 3.3f) - 0.5f, Mathf.PerlinNoise(5.1f, time) - 0.5f, Mathf.PerlinNoise(time, 8.9f) - 0.5f) * (2f * ShakeDegrees * s);
            transform.position += transform.rotation * offset;
            transform.rotation *= Quaternion.Euler(angles);
        }

        void ApplyFov(float speed, float dt)
        {
            if (cam == null) return;
            float goal = BaseFov + SpeedFov * SpeedEffectsIntensity * Mathf.Clamp01(speed / FovSpeedRef)
                         + NitroFov * SpeedEffectsIntensity * nitroKick - 2f * driftK;
            fov = Mathf.Lerp(fov, goal, 1f - Mathf.Exp(-6f * dt));
            cam.fieldOfView = fov;
        }

        void UpdateSpeedEffects(float speed, float boost)
        {
            EnsureSpeedEffects();
            float kmh = speed * 3.6f;
            if (blur != null)
                blur.intensity.value = Mathf.Clamp01((kmh - 90f) / 160f) * MaxMotionBlur * SpeedEffectsIntensity + 0.15f * boost * SpeedEffectsIntensity;
            if (streaks != null)
            {
                var em = streaks.emission;
                float k = Mathf.Clamp01((kmh - 140f) / 140f) + boost * 0.8f;
                em.rateOverTime = k * 160f * SpeedEffectsIntensity;
                var main = streaks.main;
                main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.9f + 20f, speed * 1.2f + 30f);
            }
        }

        void EnsureSpeedEffects()
        {
            if (blurVolume == null)
            {
                var go = new GameObject("SpeedBlurVolume");
                blurVolume = go.AddComponent<Volume>();
                blurVolume.isGlobal = true;
                blurVolume.priority = 50f;
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                blur = profile.Add<MotionBlur>(true);
                blur.mode.Override(MotionBlurMode.CameraOnly);
                blur.quality.Override(MotionBlurQuality.Medium);
                blur.clamp.Override(0.06f);
                blur.intensity.Override(0f);
                blurVolume.sharedProfile = profile;
            }
            if (streaks == null)
            {
                var go = new GameObject("SpeedStreaks");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 0f, 22f);
                go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // particles fly toward the lens
                streaks = go.AddComponent<ParticleSystem>();
                streaks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = streaks.main;
                main.loop = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
                main.startColor = new Color(1f, 1f, 1f, 0.35f);
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.maxParticles = 300;
                var em = streaks.emission;
                em.rateOverTime = 0f;
                var shape = streaks.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;   // emitted along the axis, toward the lens
                shape.angle = 0f;
                shape.radius = 7f;
                shape.radiusThickness = 0.45f;            // a ring: keep the centre of the view clear
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.06f;
                r.lengthScale = 2f;
                r.sharedMaterial = VeyraCity.World.RuntimeMaterials.NewParticle("SpeedStreak", true);
                r.shadowCastingMode = ShadowCastingMode.Off;
                streaks.Play();
            }
        }
    }
}
