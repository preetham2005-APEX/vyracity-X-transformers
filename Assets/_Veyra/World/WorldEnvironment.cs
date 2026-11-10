using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VeyraCity.World
{
    /// <summary>
    /// Time of day, weather and night lighting for the whole city:
    /// sun/moon, ambient, fog and sky; emissive windows, neon and lamp heads; a pool of real point lights
    /// placed on the street lamps nearest the camera; rain particles and wet-road material response.
    /// </summary>
    [ExecuteAlways]
    public sealed class WorldEnvironment : MonoBehaviour
    {
        public enum WeatherKind { Clear, Overcast, Rain }

        [Serializable]
        public struct EmissiveEntry
        {
            public Material Material;
            public Color BaseEmission;
            [Tooltip("0 = always on (signs), 1 = only at night (windows, lamps)")] public float NightOnly;
        }

        [Serializable]
        public struct WetEntry
        {
            public Material Material;
            public float DrySmoothness;
            public float WetSmoothness;
            public Color DryColor;
        }

        [Tooltip("Assets/_Veyra/World/Generated/Materials/VeyraMaterialLibrary.asset (rain material; activated for the scene)")]
        [SerializeField] VeyraMaterialLibrary materials;
        public Light Sun;
        public Material SkyboxMaterial;
        public Volume PostVolume;
        [Range(0f, 24f)] public float TimeOfDay = 21.5f;
        public bool AnimateTime;
        [Tooltip("Real minutes per 24 h game day")] public float DayLengthMinutes = 24f;
        public WeatherKind Weather = WeatherKind.Clear;
        [Tooltip("Free roam: change weather every few minutes (clear, overcast, rain)")] public bool AutoWeather;
        float weatherTimer = 240f;
        /// <summary>0 = clear sky, 1 = full cloud (overcast or rain); ramps over ~25 s.</summary>
        public float Cloudiness { get; private set; }
        public List<EmissiveEntry> Emissives = new List<EmissiveEntry>();
        public List<WetEntry> WetSurfaces = new List<WetEntry>();
        [Tooltip("Water materials whose normal map scrolls")] public List<Material> ScrollMaterials = new List<Material>();
        [Tooltip("Global wind for the Veyra shaders (_VC_WindStrength 0–1, _VC_WindDirection); vegetation and rain lean with it")]
        public float WindStrength = 0.3f;
        public Vector3 WindDirection = new Vector3(-1f, 0f, 0.25f);
        public int StreetLightCount = 28;
        public float StreetLightRadius = 260f;
        public float StreetLightRange = 26f;
        public float StreetLightIntensity = 4.5f;
        public Color StreetLightColor = new Color(1f, 0.78f, 0.52f);

        public static WorldEnvironment Instance { get; private set; }
        /// <summary>0 = full day, 1 = full night.</summary>
        public float NightFactor { get; private set; }
        /// <summary>0..1 golden-hour factor (sun up and below ~22° elevation); drives the L2 Golden volume weight.</summary>
        public float GoldenFactor { get; private set; }
        /// <summary>0 = dry, 1 = soaked; ramps over ~20 s when the weather changes.</summary>
        public float Wetness { get; private set; }
        /// <summary>0 = no rain, 1 = full rain; ramps over ~8 s (drives the rain ripples of the Veyra road shader).</summary>
        public float RainIntensity { get; private set; }
        public event Action<WeatherKind> WeatherChanged;

        Light[] pool;
        Vector3[] poolLamp;
        float[] poolLevel;
        bool[] poolActive;
        readonly System.Collections.Generic.HashSet<Vector3> selected = new System.Collections.Generic.HashSet<Vector3>();
        Vector3[] lampBuffer = new Vector3[2048];
        float poolTimer;
        ParticleSystem rain;
        ColorAdjustments colorAdj;
        Bloom bloom;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int SmoothId = Shader.PropertyToID("_Smoothness");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
        static readonly int ExposureId = Shader.PropertyToID("_Exposure");
        static readonly int AtmosphereId = Shader.PropertyToID("_AtmosphereThickness");
        static readonly int SkyTintId = Shader.PropertyToID("_SkyTint");

        public VeyraMaterialLibrary Materials { get => materials; set => materials = value; }

        void OnEnable()
        {
            Instance = this;
            if (materials != null || Application.isPlaying) VeyraMaterialLibrary.Activate(materials, this);
            if (PostVolume != null && PostVolume.sharedProfile != null)
            {
                PostVolume.sharedProfile.TryGet(out colorAdj);
                PostVolume.sharedProfile.TryGet(out bloom);
            }
            Wetness = Weather == WeatherKind.Rain ? 1f : 0f;
            Cloudiness = Weather == WeatherKind.Clear ? 0f : 1f;
            RainIntensity = Weather == WeatherKind.Rain ? 1f : 0f;
            VeyraGlobals.Invalidate();
            Apply(true);
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            // Edit mode (scene closed, recompiled or built in batch mode): leave the shared .mat assets in their reference state
            // so a later asset save never writes the preview (BUG-039; the editor save guard covers saves while the scene is open).
            if (!Application.isPlaying) WriteReferenceMaterialState();
        }

        public void SetWeather(WeatherKind w)
        {
            if (w == Weather) return;
            Weather = w;
            WeatherChanged?.Invoke(w);
        }

        public void SetTime(float hours)
        {
            TimeOfDay = Mathf.Repeat(hours, 24f);
            Apply(true);
        }

        /// <summary>Sets the rain intensity directly (0–1, drives the rain ripples); in Play mode it then ramps towards the weather's target.</summary>
        public void SetRainIntensity(float intensity)
        {
            RainIntensity = Mathf.Clamp01(intensity);
            Apply(true);
        }

        /// <summary>Sets road wetness directly (0 dry … 1 wet) without rain, e.g. for review cameras (card S5). In Play mode the
        /// weather then drifts it back towards its own target (dry for Clear) at the normal slow rate.</summary>
        public void SetWetness(float wetness)
        {
            Wetness = Mathf.Clamp01(wetness);
            Apply(true);
        }

        void Update()
        {
            if (Application.isPlaying)
            {
                if (AnimateTime) TimeOfDay = Mathf.Repeat(TimeOfDay + Time.deltaTime * 24f / (DayLengthMinutes * 60f), 24f);
                float target = Weather == WeatherKind.Rain ? 1f : 0f;
                Wetness = Mathf.MoveTowards(Wetness, target, Time.deltaTime / (target > Wetness ? 20f : 90f));
                Cloudiness = Mathf.MoveTowards(Cloudiness, Weather == WeatherKind.Clear ? 0f : 1f, Time.deltaTime / 25f);
                RainIntensity = Mathf.MoveTowards(RainIntensity, Weather == WeatherKind.Rain ? 1f : 0f, Time.deltaTime / 8f);
                if (AutoWeather)
                {
                    weatherTimer -= Time.deltaTime;
                    if (weatherTimer <= 0f)
                    {
                        float r = UnityEngine.Random.value;
                        SetWeather(r < 0.55f ? WeatherKind.Clear : r < 0.8f ? WeatherKind.Overcast : WeatherKind.Rain);
                        weatherTimer = UnityEngine.Random.Range(180f, 360f);
                    }
                }
            }
            Apply(false);
            float tt = Application.isPlaying ? Time.time : 0f;
            foreach (var m in ScrollMaterials)
            {
                if (m == null) continue;
                m.SetTextureOffset(BumpMapId, new Vector2(tt * 0.011f, tt * 0.007f));
#if UNITY_EDITOR
                if (!Application.isPlaying) UnityEditor.EditorUtility.ClearDirty(m); // BUG-039: preview only
#endif
            }
        }

        /// <summary>
        /// Writes the reference state into the shared materials this component animates (noon, clear, dry: daytime emission,
        /// dry roads, day sky, water scroll at 0). The .mat assets are saved in this state, so previewing night or rain in the
        /// Editor no longer changes them on disk (BUG-039); the editor save guard calls this before every asset save and then
        /// re-applies the current time and weather. Scene-level settings (RenderSettings, light, post volume) are not touched.
        /// </summary>
        public void WriteReferenceMaterialState()
        {
            ApplyMaterials(0f, 0f, 0f);
            foreach (var m in ScrollMaterials)
                if (m != null) m.SetTextureOffset(BumpMapId, Vector2.zero);
        }

        /// <summary>Re-applies the current time of day and weather after <see cref="WriteReferenceMaterialState"/>.</summary>
        public void ReapplyCurrentState() => Apply(true);

        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        void Apply(bool force)
        {
            float t = TimeOfDay;
            // Sun elevation: rises 6:00, peaks 13:00 at 62 deg, sets 20:00.
            float dayPhase = Mathf.InverseLerp(6f, 20f, t);
            bool sunUp = t > 6f && t < 20f;
            // Continuous elevation: above the horizon 6:00-20:00, below it overnight.
            float nightPhase = Mathf.Repeat(t - 20f, 24f) / 10f;
            float elevation = sunUp ? Mathf.Sin(dayPhase * Mathf.PI) * 62f : -25f * Mathf.Sin(Mathf.Clamp01(nightPhase) * Mathf.PI);
            float azimuth = Mathf.Lerp(80f, 280f, dayPhase);
            NightFactor = Mathf.Clamp01(1f - Smooth(-6f, 10f, elevation));
            float golden = sunUp ? 1f - Smooth(4f, 22f, elevation) : 0f;
            GoldenFactor = golden;

            if (Sun != null)
            {
                if (NightFactor < 0.98f && sunUp)
                {
                    Sun.transform.rotation = Quaternion.Euler(Mathf.Max(2f, elevation), azimuth, 0f);
                    Sun.color = Color.Lerp(new Color(1f, 0.97f, 0.92f), new Color(1f, 0.62f, 0.32f), golden);
                    Sun.intensity = Mathf.Lerp(1.9f, 1.15f, golden) * (1f - NightFactor) * Mathf.Lerp(1f, 0.38f, Cloudiness);
                }
                else
                {
                    // Moonlight: cool, dim, high.
                    Sun.transform.rotation = Quaternion.Euler(48f, 210f, 0f);
                    Sun.color = new Color(0.55f, 0.65f, 0.95f);
                    Sun.intensity = 0.18f;
                }
                Sun.shadowStrength = Mathf.Lerp(0.9f, 0.55f, NightFactor);
            }

            Color skyDay = new Color(0.48f, 0.62f, 0.82f), skyGold = new Color(0.62f, 0.48f, 0.42f), skyNight = new Color(0.05f, 0.07f, 0.13f);
            Color eqDay = new Color(0.62f, 0.66f, 0.70f), eqGold = new Color(0.72f, 0.50f, 0.36f), eqNight = new Color(0.07f, 0.07f, 0.11f);
            Color grDay = new Color(0.30f, 0.29f, 0.27f), grNight = new Color(0.03f, 0.03f, 0.04f);
            Color sky = Color.Lerp(Color.Lerp(skyDay, skyGold, golden), skyNight, NightFactor);
            sky = Color.Lerp(sky, new Color(0.42f, 0.45f, 0.5f) * Mathf.Lerp(1f, 0.15f, NightFactor), Cloudiness * 0.7f);
            Color eq = Color.Lerp(Color.Lerp(eqDay, eqGold, golden), eqNight, NightFactor);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = eq;
            RenderSettings.ambientGroundColor = Color.Lerp(grDay, grNight, NightFactor);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            float wetHaze = Wetness * 0.0006f + Cloudiness * 0.00035f;
            RenderSettings.fogDensity = Mathf.Lerp(0.00042f, 0.00085f, NightFactor) + wetHaze;
            RenderSettings.fogColor = Color.Lerp(Color.Lerp(new Color(0.66f, 0.72f, 0.80f), new Color(0.78f, 0.58f, 0.45f), golden),
                                                 new Color(0.035f, 0.045f, 0.075f), NightFactor);
            if (SkyboxMaterial != null) RenderSettings.skybox = SkyboxMaterial;
            // Card L2: with a PostVolumeDirector the Day / Golden / Night volumes carry these values (weights only, no profile
            // writes, so no asset changes in Play mode); the legacy writes stay for scenes without one.
            bool legacyPost = PostVolumeDirector.Active == null;
            if (legacyPost && colorAdj != null) colorAdj.postExposure.value = Mathf.Lerp(0.05f, 0.3f, NightFactor);
            if (legacyPost && bloom != null)
            {
                // Sunlit light paving reaches ~0.9 in HDR, so the low threshold is for night only (neon, lamps, windows).
                bloom.intensity.value = Mathf.Lerp(0.55f, 1.2f, NightFactor);
                bloom.threshold.value = Mathf.Lerp(1.25f, 0.9f, NightFactor);
            }

            ApplyMaterials(NightFactor, golden, Wetness);
            float rain = Application.isPlaying ? RainIntensity : Mathf.Max(RainIntensity, Weather == WeatherKind.Rain ? 1f : 0f);
            VeyraGlobals.Apply(TimeOfDay, Wetness, rain, WindStrength, WindDirection, Sun != null ? -Sun.transform.forward : Vector3.up);

            if (Application.isPlaying)
            {
                UpdateLightPool(force);
                UpdateRain();
            }
        }

        /// <summary>
        /// Material response to light and weather: sky, emissives (windows, neon, lamp heads) and wet roads. Only property values
        /// change; no shader keyword is toggled at runtime (card F1): emissive materials keep _EMISSION on in their assets and
        /// night brightness is the _EmissionColor value.
        /// </summary>
        void ApplyMaterials(float night, float golden, float wetness)
        {
            if (SkyboxMaterial != null)
            {
                if (SkyboxMaterial.HasProperty(ExposureId)) SkyboxMaterial.SetFloat(ExposureId, Mathf.Lerp(1.15f, 0.06f, night));
                if (SkyboxMaterial.HasProperty(AtmosphereId)) SkyboxMaterial.SetFloat(AtmosphereId, Mathf.Lerp(0.85f, 1.6f, golden));
                if (SkyboxMaterial.HasProperty(SkyTintId)) SkyboxMaterial.SetColor(SkyTintId, Color.Lerp(new Color(0.5f, 0.55f, 0.62f), new Color(0.25f, 0.28f, 0.45f), night));
            }
            foreach (var e in Emissives)
            {
                if (e.Material == null) continue;
                float k = Mathf.Lerp(1f, night, e.NightOnly);
                e.Material.SetColor(EmissionId, e.BaseEmission * k);
            }
            // Wet roads: darker and much glossier.
            foreach (var w in WetSurfaces)
            {
                if (w.Material == null) continue;
                w.Material.SetFloat(SmoothId, Mathf.Lerp(w.DrySmoothness, w.WetSmoothness, wetness));
                w.Material.SetColor(BaseColorId, Color.Lerp(w.DryColor, w.DryColor * 0.55f, wetness));
            }
#if UNITY_EDITOR
            // BUG-039: in Edit mode these writes are a preview only. Clearing the dirty flag keeps every save path (Editor saves,
            // batch builds, quitting) from writing the preview into the .mat files; the assets on disk never change.
            if (!Application.isPlaying)
            {
                if (SkyboxMaterial != null) UnityEditor.EditorUtility.ClearDirty(SkyboxMaterial);
                foreach (var e in Emissives) if (e.Material != null) UnityEditor.EditorUtility.ClearDirty(e.Material);
                foreach (var w in WetSurfaces) if (w.Material != null) UnityEditor.EditorUtility.ClearDirty(w.Material);
                foreach (var m in ScrollMaterials) if (m != null) UnityEditor.EditorUtility.ClearDirty(m);
            }
#endif
        }

        /// <summary>Card L4: real lights for the nearest street lamps, by quality tier (PC_Ultra 96, PC_High 48, PC_Medium 24,
        /// mobile 8). Downward spot cones (110°) give the light pools on the road; no shadows. Reassigned every 0.25 s to the lamps
        /// nearest a point slightly ahead of the camera; a lamp keeps its slot while it stays selected, and every slot fades in or
        /// out over 0.5 s, so lights never pop while driving.</summary>
        static int PoolSizeForQuality()
        {
            string q = QualitySettings.names[QualitySettings.GetQualityLevel()];
            return q.Contains("Ultra") ? 96 : q.Contains("High") ? 48 : q.Contains("Medium") ? 24 : q.StartsWith("Mobile") ? 8 : 28;
        }

        void UpdateLightPool(bool force)
        {
            int want = StreetLightCount > 0 ? PoolSizeForQuality() : 0;
            if (pool != null && pool.Length != want)
            {
                foreach (var l in pool) if (l != null) Destroy(l.gameObject);
                pool = null;
            }
            if (pool == null)
            {
                pool = new Light[want];
                poolLamp = new Vector3[want];
                poolLevel = new float[want];
                poolActive = new bool[want];
                var root = transform.Find("StreetLightPool");
                if (root == null) { root = new GameObject("StreetLightPool").transform; root.SetParent(transform, false); }
                for (int i = 0; i < pool.Length; i++)
                {
                    var go = new GameObject("StreetLight_" + i);
                    go.transform.SetParent(root, false);
                    go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    var l = go.AddComponent<Light>();
                    l.type = LightType.Spot;
                    l.spotAngle = 110f;
                    l.innerSpotAngle = 60f;
                    l.range = StreetLightRange;
                    l.color = StreetLightColor;
                    l.shadows = LightShadows.None;
                    l.enabled = false;
                    pool[i] = l;
                }
            }
            var cam = Camera.main;
            var props = InstancedPropRenderer.Instance;
            bool on = cam != null && props != null && NightFactor >= 0.25f;
            poolTimer -= Time.deltaTime;
            if (on && (poolTimer <= 0f || force))
            {
                poolTimer = 0.25f;
                Vector3 eye = cam.transform.position;
                Vector3 ahead = eye + cam.transform.forward * StreetLightRadius * 0.35f;
                props.CollectLamps(ahead, StreetLightRadius, lampBuffer, out int count);
                int n = Mathf.Min(count, pool.Length);
                for (int k = 0; k < n; k++) // partial selection of the nearest lamps
                {
                    int best = k;
                    float bd = (lampBuffer[k] - ahead).sqrMagnitude;
                    for (int j = k + 1; j < count; j++)
                    {
                        float d = (lampBuffer[j] - ahead).sqrMagnitude;
                        if (d < bd) { bd = d; best = j; }
                    }
                    (lampBuffer[k], lampBuffer[best]) = (lampBuffer[best], lampBuffer[k]);
                }
                selected.Clear();
                for (int k = 0; k < n; k++) selected.Add(lampBuffer[k]);
                // Keep slots whose lamp is still selected; free the others (they fade out).
                for (int i = 0; i < pool.Length; i++)
                {
                    poolActive[i] = poolLevel[i] > 0f && selected.Remove(poolLamp[i]) ? true : false;
                }
                // New lamps go to fully faded slots.
                foreach (var lamp in selected)
                    for (int i = 0; i < pool.Length; i++)
                        if (!poolActive[i] && poolLevel[i] <= 0.001f)
                        {
                            poolLamp[i] = lamp;
                            poolActive[i] = true;
                            pool[i].transform.position = lamp + Vector3.down * 0.4f;
                            break;
                        }
            }
            else if (!on)
            {
                for (int i = 0; i < pool.Length; i++) poolActive[i] = false;
            }
            float step = Time.deltaTime / 0.5f;
            for (int i = 0; i < pool.Length; i++)
            {
                poolLevel[i] = Mathf.MoveTowards(poolLevel[i], poolActive[i] ? 1f : 0f, step);
                bool lit = poolLevel[i] > 0.001f;
                if (pool[i].enabled != lit) pool[i].enabled = lit;
                if (lit) pool[i].intensity = StreetLightIntensity * 2.2f * NightFactor * poolLevel[i];
            }
        }

        void UpdateRain()
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (rain == null && Weather != WeatherKind.Rain) return; // built on first rain
            if (rain == null)
            {
                var m = RuntimeMaterials.NewParticle("Rain", false);
                if (m == null) return; // RuntimeMaterials logged why (no library or empty slot)
                m.SetColor(BaseColorId, new Color(0.8f, 0.85f, 1f, 0.3f));
                var go = new GameObject("Rain");
                rain = go.AddComponent<ParticleSystem>();
                var main = rain.main;
                main.loop = true;
                main.startLifetime = 1.1f;
                main.startSpeed = 0f;
                main.startSize3D = true;
                main.startSizeX = 0.02f;
                main.startSizeY = 0.9f;
                main.startSizeZ = 0.02f;
                main.maxParticles = 6000;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startColor = new Color(0.75f, 0.82f, 0.95f, 0.35f);
                var vel = rain.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                vel.y = new ParticleSystem.MinMaxCurve(-24f);
                vel.x = new ParticleSystem.MinMaxCurve(-1.5f);
                vel.z = new ParticleSystem.MinMaxCurve(0f);
                var shape = rain.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(60f, 1f, 60f);
                var emission = rain.emission;
                emission.rateOverTime = 0f;
                var r = rain.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.04f;
                r.lengthScale = 1f;
                r.sharedMaterial = m;
                rain.Play();
            }
            rain.transform.position = cam.transform.position + Vector3.up * 14f + cam.transform.forward * 12f;
            var em = rain.emission;
            em.rateOverTime = Weather == WeatherKind.Rain ? 5500f : 0f;
        }
    }
}
