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
        // D1 tunables. Renamed from the old D1 draft (StreetLightGain, MoonIntensity, LampSpriteDistance) so the stale values
        // serialized in VeyraCity.unity (1.5, 0.32) no longer override the defaults below.
        [Tooltip("D1: street-light intensity multiplier. URP spot intensity falls with distance squared: 4.5 x 100 = 450 (x height gain) gives ~4.5 on the road under a 10 m head, several times the moonlit ground (dark asphalt, albedo ~0.08)")] public float PoolGain = 100f;
        [Tooltip("D1: moonlight intensity at night (clear sky); BUG-128: 0.8 keeps rural and aerial night terrain readable above the ACES toe")] public float MoonLight = 0.8f;
        [Tooltip("D1: top of the ground haze layer (m); fog thins above it for aerial views")] public float FogLayerTop = 120f;
        [Tooltip("D1: far lamps drawn as glow sprites out to this distance (0 = off)")] public float FarLampDistance = 2400f;
        [Tooltip("D1: far traffic drawn as head/tail-light sprites on the major roads at night (0 = off)")] public float FarTrafficDistance = 3200f;

        public static WorldEnvironment Instance { get; private set; }
        /// <summary>D1: the camera the player is looking through when it is not Camera.main (review captures render with their
        /// own on-demand camera). The light pool, the lamp sprites and the district haze follow it; null = Camera.main.</summary>
        [NonSerialized] public Camera ViewCamera;
        Camera View => ViewCamera != null ? ViewCamera : Camera.main;
        /// <summary>The camera the player sees through: the review camera during captures, else Camera.main (D1, BUG-125).</summary>
        public static Camera ViewOrMain => Instance != null && Instance.ViewCamera != null ? Instance.ViewCamera : Camera.main;
        /// <summary>0 = full day, 1 = full night.</summary>
        public float NightFactor { get; private set; }
        [Tooltip("Card L5: wetness of the Downtown / Neon Mile wet-night preset (0 = off)")] [Range(0f, 1f)] public float WetNightLevel = 0.65f;
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
        bool noLightPool;
        // DIAG noon sweep (temporary): -veyraNoon <0|1|2>
        static int noonPreset = -1;
        static int NoonPreset { get { if (noonPreset < 0) { var g = Environment.GetCommandLineArgs(); int i = Array.IndexOf(g, "-veyraNoon"); noonPreset = i >= 0 && i + 1 < g.Length && int.TryParse(g[i + 1], out int v) ? v : 0; } return noonPreset; } }
        float districtHaze, districtHazeTarget, hazeTimer;
        ParticleSystem lampSprites;
        ParticleSystem.Particle[] spriteBuf;
        Vector3[] spriteLamps;
        float spriteTimer;
        DistantTrafficLights farTraffic;
        Camera lastAaCam;
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
            noLightPool = Array.IndexOf(Environment.GetCommandLineArgs(), "-veyraNoLightPool") >= 0;   // BUG-109 A/B
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
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
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
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

        float baseFogDensity = 0.00035f;

        /// <summary>BUG-059: fog thins above the ground haze layer (x0.12 at FogLayerTop + 900 m and higher).</summary>
        float FogAltitudeFactor(float y) => Mathf.Lerp(1f, 0.12f, Smooth(FogLayerTop, FogLayerTop + 900f, y));

        /// <summary>URP takes the fog parameters per camera from RenderSettings when the camera is set up, so the altitude
        /// factor is applied for every camera just before it renders (main, review, reflection-probe cameras).</summary>
        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == null) return;
            RenderSettings.fogDensity = baseFogDensity * FogAltitudeFactor(cam.transform.position.y);
        }

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
            // BUG-060: golden hour below 30 deg sun elevation (18.8 h = 17 deg: 0.58; was 0.17 with the 22 deg window).
            float golden = sunUp ? 1f - Smooth(6f, 30f, elevation) : 0f;
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
                    Sun.color = new Color(0.62f, 0.7f, 0.92f);
                    // D1: brighter moon fill so rural and aerial night views keep their shapes (was 0.18: black).
                    Sun.intensity = MoonLight * Mathf.Lerp(1f, 0.45f, Cloudiness);
                }
                Sun.shadowStrength = Mathf.Lerp(0.9f, 0.55f, NightFactor);
            }

            // D1 night: moonlit blue sky fill and a warm city-glow horizon (GTA-style readable night: shapes stay visible far
            // from street lights). BUG-128: the Trilight colours are gamma-space (the player builds the ambient SH from their
            // linear values: 0.19 gave 0.026), so the night set is 6x the old linear fill (SH up 0.16 / 0.20 / 0.40, measured
            // with a dev-build probe dump); rural and aerial night views were black.
            Color skyDay = new Color(0.48f, 0.62f, 0.82f), skyGold = new Color(0.62f, 0.48f, 0.42f), skyNight = new Color(0.46f, 0.55f, 0.80f);
            // iteration 56: night equator and ground fill raised ~15 % in gamma (~40 % linear). A vertical facade is lit
            // almost entirely by the equator term, and against windows bright enough to drive the tonemap the walls
            // between them were crushing to black - so a tower lost its silhouette and read as lit dots on nothing.
            Color eqDay = new Color(0.62f, 0.66f, 0.70f), eqGold = new Color(0.72f, 0.50f, 0.36f), eqNight = new Color(0.46f, 0.45f, 0.52f);
            Color grDay = new Color(0.30f, 0.29f, 0.27f), grNight = new Color(0.24f, 0.24f, 0.27f);
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
            // Card L3: exponential-squared density day 0.00035, golden 0.0005, night 0.0007 (+ wet / cloud haze).
            float density = Mathf.Lerp(Mathf.Lerp(0.00035f * (NoonPreset >= 1 ? 0.6f : 1f), 0.0005f, golden), 0.0007f, NightFactor) + wetHaze;   // DIAG noon
            // D1 / BUG-059: height fog. URP fog depends on camera distance only, so the density thins with camera altitude
            // above the ground haze layer (aerial views no longer a fog wall) and thickens in the hazy districts (D6 Neon Mile
            // at night, D3 Ironport always). No extra GPU cost: same fog term, different density.
            var fogCam = View;
            float camY = fogCam != null ? fogCam.transform.position.y : 0f;
            if (Application.isPlaying && fogCam != null) UpdateDistrictHaze(fogCam.transform.position);
            baseFogDensity = density * (1f + districtHaze);
            RenderSettings.fogDensity = baseFogDensity * FogAltitudeFactor(camY);
            RenderSettings.fogColor = Color.Lerp(Color.Lerp(NoonPreset >= 1 ? new Color(0.58f, 0.66f, 0.78f) : new Color(0.66f, 0.72f, 0.80f), new Color(0.78f, 0.58f, 0.45f), golden),
                                                 new Color(0.058f, 0.064f, 0.085f), NightFactor);   // D1: night haze lit by the city
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

            // Card L5: the "wet night" preset: Downtown and Neon Mile streets are wet at night even without rain (district
            // weight from PostVolumeDirector, faded at the district border and with the night factor).
            float wet = Mathf.Max(Wetness, PostVolumeDirector.WetNight * WetNightLevel);
            ApplyMaterials(NightFactor, golden, wet);
            float rain = Application.isPlaying ? RainIntensity : Mathf.Max(RainIntensity, Weather == WeatherKind.Rain ? 1f : 0f);
            VeyraGlobals.Apply(TimeOfDay, wet, rain, WindStrength, WindDirection, Sun != null ? -Sun.transform.forward : Vector3.up, NightFactor);   // iteration 21: shaders follow the sun

            if (Application.isPlaying)
            {
                UpdateLightPool(force);
                UpdateLampSprites(force);
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
                if (SkyboxMaterial.HasProperty(ExposureId)) SkyboxMaterial.SetFloat(ExposureId, Mathf.Lerp(NoonPreset >= 2 ? 1.0f : 1.15f, 0.06f, night));
                if (SkyboxMaterial.HasProperty(AtmosphereId)) SkyboxMaterial.SetFloat(AtmosphereId, Mathf.Lerp(NoonPreset >= 2 ? 0.7f : 0.85f, 1.05f, golden));   // D1: 1.3 turned the upper golden sky green
                if (SkyboxMaterial.HasProperty(SkyTintId)) SkyboxMaterial.SetColor(SkyTintId, Color.Lerp(NoonPreset >= 2 ? new Color(0.42f, 0.5f, 0.66f) : new Color(0.5f, 0.55f, 0.62f), new Color(0.25f, 0.28f, 0.45f), night));
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

        /// <summary>Card L4: real lights for the street lamps around the player's car, by quality tier (PC_Ultra 96, PC_High 64,
        /// PC_Medium 48, mobile 8). Downward spot cones (90°) give the light pools on the road; no shadows. D1 / BUG-073: the
        /// pool is centred on the car (about 7 m ahead of the chase camera), refreshed every 0.25 s with hysteresis: a lit lamp
        /// stays lit until it is more than 25 % beyond the radius of the nearest-N set, so lamps at the edge never flicker; each
        /// slot fades in and out over 0.6 s (smoothstep). Colour temperature by district (<see cref="LampColour"/>).</summary>
        static int PoolSizeForQuality()
        {
            string q = QualitySettings.names[QualitySettings.GetQualityLevel()];
            return q.Contains("Ultra") ? 96 : q.Contains("High") ? 64 : q.Contains("Medium") ? 48 : q.StartsWith("Mobile") ? 8 : 28;
        }

        /// <summary>The point the light pool and the haze follow: the car, about 7 m ahead of the chase camera on the ground plane.</summary>
        static Vector3 FocusPoint(Camera cam)
        {
            var fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            return cam.transform.position + (fwd.sqrMagnitude > 1e-4f ? fwd.normalized * 7f : Vector3.zero);
        }

        readonly float[] lampDist = new float[2048];
        float[] poolHeightGain;
        int lastCollect; Vector3 lastFocus;

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
                poolHeightGain = new float[want];
                var root = transform.Find("StreetLightPool");
                if (root == null) { root = new GameObject("StreetLightPool").transform; root.SetParent(transform, false); }
                for (int i = 0; i < pool.Length; i++)
                {
                    var go = new GameObject("StreetLight_" + i);
                    go.transform.SetParent(root, false);
                    go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    var l = go.AddComponent<Light>();
                    l.type = LightType.Spot;
                    l.spotAngle = 90f;        // BUG-109: the road below, not the facades beside the lamp
                    l.innerSpotAngle = 50f;
                    l.range = StreetLightRange;
                    l.color = StreetLightColor;
                    l.shadows = LightShadows.None;
                    l.enabled = false;
                    pool[i] = l;
                }
            }
            var cam = View;
            var props = InstancedPropRenderer.Instance;
            bool on = cam != null && props != null && NightFactor >= 0.25f && !noLightPool;
            poolTimer -= Time.deltaTime;
            if (on && (poolTimer <= 0f || force))
            {
                poolTimer = 0.25f;
                Vector3 focus = FocusPoint(cam);
                props.CollectLamps(focus, StreetLightRadius, lampBuffer, out int count);
                lastCollect = count; lastFocus = focus;
                int n = Mathf.Min(count, pool.Length);
                for (int j = 0; j < count; j++) lampDist[j] = (lampBuffer[j] - focus).sqrMagnitude;
                for (int k = 0; k < n; k++) // partial selection of the nearest lamps
                {
                    int best = k;
                    for (int j = k + 1; j < count; j++) if (lampDist[j] < lampDist[best]) best = j;
                    (lampBuffer[k], lampBuffer[best]) = (lampBuffer[best], lampBuffer[k]);
                    (lampDist[k], lampDist[best]) = (lampDist[best], lampDist[k]);
                }
                // Hysteresis radius: 25 % beyond the farthest lamp of the nearest-N set (the whole radius when fewer lamps).
                float keep = n >= pool.Length && n > 0 ? Mathf.Sqrt(lampDist[n - 1]) * 1.25f : StreetLightRadius;
                float keep2 = keep * keep;
                selected.Clear();
                for (int k = 0; k < n; k++) selected.Add(lampBuffer[k]);
                // Lit slots stay lit while their lamp is inside the hysteresis radius (selected or not).
                for (int i = 0; i < pool.Length; i++)
                {
                    if (!poolActive[i] && poolLevel[i] <= 0f) continue;
                    bool stay = (poolLamp[i] - focus).sqrMagnitude <= keep2;
                    selected.Remove(poolLamp[i]);
                    poolActive[i] = stay;
                    if (force && !stay) poolLevel[i] = 0f;
                }
                // New lamps (nearest first) go to fully faded slots.
                for (int k = 0; k < n; k++)
                {
                    var lamp = lampBuffer[k];
                    if (!selected.Contains(lamp)) continue;
                    for (int i = 0; i < pool.Length; i++)
                        if (!poolActive[i] && poolLevel[i] <= 0.001f)
                        {
                            poolLamp[i] = lamp;
                            poolActive[i] = true;
                            pool[i].transform.position = lamp + Vector3.down * 0.4f;
                            pool[i].color = LampColour(lamp);
                            // D1: same light on the ground whatever the mast height (6 m coastal to 25 m high-mast): the
                            // intensity scales with the head height squared, the range with the height
                            float h = Physics.Raycast(lamp + Vector3.down * 0.6f, Vector3.down, out var hit, 40f, 1, QueryTriggerInteraction.Ignore) ? hit.distance + 0.6f : 9f;
                            h = Mathf.Clamp(h, 4f, 30f);
                            poolHeightGain[i] = (h * h) / 100f;
                            pool[i].range = Mathf.Max(StreetLightRange, h * 1.9f);
                            break;
                        }
                }
            }
            else if (!on)
            {
                for (int i = 0; i < pool.Length; i++) poolActive[i] = false;
            }
            if (force && Debug.isDebugBuild)
            {
                int litN = 0; for (int i = 0; i < pool.Length; i++) if (poolActive[i]) litN++;
                Debug.Log($"[LightPool] forced: on {on} (cam {(cam != null ? cam.name : "null")}, props {(props != null)}, night {NightFactor:0.00}, off-flag {noLightPool}), slots {pool.Length}, active {litN}, collected {lastCollect} at {lastFocus:F0}, gain {PoolGain}");
            }
            float step = force ? 1f : Time.deltaTime / 0.6f;   // a forced update (time set, camera cut) snaps, no fade
            for (int i = 0; i < pool.Length; i++)
            {
                poolLevel[i] = Mathf.MoveTowards(poolLevel[i], poolActive[i] ? 1f : 0f, step);
                bool lit = poolLevel[i] > 0.001f;
                if (pool[i].enabled != lit) pool[i].enabled = lit;
                float s = poolLevel[i] * poolLevel[i] * (3f - 2f * poolLevel[i]);
                if (lit) pool[i].intensity = StreetLightIntensity * PoolGain * poolHeightGain[i] * NightFactor * s;
            }
        }

        // District colour temperatures (card L4): D1 Crown Downtown 4000 K LED, D2 Azure Coast 2700 K, D3 Ironport 2200 K
        // high-pressure sodium, D4 Blackridge 3000 K, D5 Westhaven 3000 K, D6 Neon Mile 4000 K LED, D7 Falcon Airfield 4500 K,
        // D8 Redwood Valley 2700 K.
        static readonly Dictionary<string, float> DistrictKelvin = new Dictionary<string, float>
        { { "D1", 4000f }, { "D2", 2700f }, { "D3", 2200f }, { "D4", 3000f }, { "D5", 3000f }, { "D6", 4000f }, { "D7", 4500f }, { "D8", 2700f } };
        readonly Dictionary<long, Color> lampColourCache = new Dictionary<long, Color>();

        /// <summary>Street-light colour at p from its district's colour temperature; cached on a 50 m grid (the district
        /// polygon test is too slow for thousands of far-lamp sprites).</summary>
        public Color LampColour(Vector3 p)
        {
            long key = ((long)Mathf.FloorToInt(p.x / 50f) << 32) ^ (uint)Mathf.FloorToInt(p.z / 50f);
            if (lampColourCache.TryGetValue(key, out var c)) return c;
            var d = WorldData.DistrictAt(p);
            string id = d != null && d.id != null && d.id.Length >= 2 ? d.id.Substring(0, 2) : "";
            c = DistrictKelvin.TryGetValue(id, out float k) ? KelvinColour(k) : StreetLightColor;
            lampColourCache[key] = c;
            return c;
        }

        /// <summary>Normalised sRGB colour (max channel 1) of a black body at the given temperature (for Light.color).</summary>
        public static Color KelvinColour(float kelvin)
        {
            var c = Mathf.CorrelatedColorTemperatureToRGB(kelvin).gamma;   // the function is linear; Light.color is sRGB
            float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            return m > 0f ? new Color(c.r / m, c.g / m, c.b / m, 1f) : Color.white;
        }

        void UpdateDistrictHaze(Vector3 p)
        {
            hazeTimer -= Time.deltaTime;
            if (hazeTimer <= 0f)
            {
                hazeTimer = 1f;
                var d = WorldData.DistrictAt(p);
                string id = d != null && d.id != null ? d.id : "";
                districtHazeTarget = id.StartsWith("D3") ? 0.6f : id.StartsWith("D6") ? 0.8f * NightFactor : 0f;
            }
            districtHaze = Mathf.MoveTowards(districtHaze, districtHazeTarget, Time.deltaTime / 6f);
        }

        /// <summary>
        /// D1: far street lamps as glow sprites (one particle system of billboards, no lights), so rural roads and aerial
        /// night views show the lamp lines instead of black. Lamps inside the light-pool radius have a lit head and a real
        /// light; beyond it each lamp gets a soft additive dot of about constant screen size. Rebuilt every 0.5 s.
        /// </summary>
        /// <summary>BUG-074: with STP (temporal upscaling and AA, URP asset) a camera must not also request SMAA; the scene's
        /// main camera still has SMAA serialized, so it is turned off on whichever camera becomes Camera.main.</summary>
        void EnsureNoSmaaUnderStp(Camera cam)
        {
            if (cam == null || cam == lastAaCam) return;
            lastAaCam = cam;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null || urp.upscalingFilter != UpscalingFilterSelection.STP) return;
            if (cam.TryGetComponent<UniversalAdditionalCameraData>(out var data) && data.antialiasing != AntialiasingMode.None)
                data.antialiasing = AntialiasingMode.None;
        }

        void UpdateLampSprites(bool force)
        {
            var cam = View;
            var props = InstancedPropRenderer.Instance;
            EnsureNoSmaaUnderStp(Camera.main);
            if (cam == null || props == null || FarLampDistance <= 0f) return;
            bool on = NightFactor > 0.2f;
            if (lampSprites == null)
            {
                if (!on) return;
                var m = RuntimeMaterials.NewParticle("LampGlow", true);
                if (m == null) return;
                m.SetTexture("_BaseMap", GlowTexture());
                m.SetColor("_BaseColor", new Color(3f, 3f, 3f, 1f));   // HDR: particle colours are 8-bit, the glow needs > 1
                var go = new GameObject("LampGlowSprites");
                go.transform.SetParent(transform, false);
                lampSprites = go.AddComponent<ParticleSystem>();
                lampSprites.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = lampSprites.main;
                main.loop = false;
                main.playOnAwake = false;
                main.maxParticles = 8192;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = 1e6f;
                main.startSpeed = 0f;
                var em = lampSprites.emission; em.enabled = false;
                var sh = lampSprites.shape; sh.enabled = false;
                var r = lampSprites.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Billboard;
                r.sharedMaterial = m;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.minParticleSize = 0f;
                r.maxParticleSize = 0.12f;   // haze halos near the camera
                spriteBuf = new ParticleSystem.Particle[8192];
                spriteLamps = new Vector3[8192];
            }
            if (farTraffic == null && on && FarTrafficDistance > 0f)
                farTraffic = DistantTrafficLights.Create(transform, RuntimeMaterials.NewParticle("FarTrafficGlow", true), GlowTexture());
            if (farTraffic != null)
            {
                long fc0 = VeyraCity.Core.FrameCost.Start();
                farTraffic.Tick(cam, NightFactor, FarTrafficDistance);
                VeyraCity.Core.FrameCost.Stop("FarTraffic", fc0);
            }
            spriteTimer -= Time.deltaTime;
            if (!force && spriteTimer > 0f) return;
            spriteTimer = 0.5f;
            if (!on) { lampSprites.Clear(); return; }
            Vector3 eye = cam.transform.position;
            props.CollectLamps(eye, FarLampDistance, spriteLamps, out int count);
            float near = StreetLightRadius * 0.6f;
            // L3 haze: in the hazy districts (D3 Ironport, D6 Neon Mile at night) the near lamps get a soft halo, the look of
            // light scattering in the haze, without a volumetric pass.
            float halo = Mathf.Clamp01(districtHaze) * NightFactor;
            int n = 0;
            for (int i = 0; i < count && n < spriteBuf.Length; i++)
            {
                float d = Vector3.Distance(spriteLamps[i], eye);
                var c = LampColour(spriteLamps[i]);
                if (d < near)
                {
                    if (halo < 0.05f || d < 6f) continue;
                    spriteBuf[n++] = new ParticleSystem.Particle
                    {
                        position = spriteLamps[i] + Vector3.down * 0.6f,
                        startSize = 7f,
                        startColor = new Color(c.r, c.g, c.b, 0.07f * halo * Mathf.Clamp01((d - 6f) / 20f)),
                        remainingLifetime = 1e6f,
                        startLifetime = 1e6f,
                        randomSeed = (uint)i,
                    };
                    continue;
                }
                float a = NightFactor * Mathf.Clamp01((d - near) / 60f) * Mathf.Clamp01((FarLampDistance - d) / 300f);
                if (a <= 0.01f) continue;
                spriteBuf[n++] = new ParticleSystem.Particle
                {
                    position = spriteLamps[i],
                    startSize = Mathf.Clamp(d * 0.006f, 1.2f, 12f),   // ~7 px at 1440p at any distance
                    startColor = new Color(c.r, c.g, c.b, a),
                    remainingLifetime = 1e6f,
                    startLifetime = 1e6f,
                    randomSeed = (uint)i,
                };
            }
            lampSprites.SetParticles(spriteBuf, n);
            if (!lampSprites.isPlaying) lampSprites.Play();
        }

        static Texture2D glowTex;
        static Texture2D GlowTexture()
        {
            if (glowTex != null) return glowTex;
            const int n = 32;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - r);
                    a = a * a * (0.4f + 0.6f * Mathf.Exp(-r * r * 18f));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(255f * a));
                }
            glowTex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "LampGlow", wrapMode = TextureWrapMode.Clamp };
            glowTex.SetPixels32(px);
            glowTex.Apply(true, true);
            return glowTex;
        }

        /// <summary>
        /// D1 rain (card 5): thin wind-slanted needles instead of thick streaks (8 mm wide, ~0.5 m long from the velocity
        /// stretch at 9 m/s terminal speed, soft-particle material so they fade into geometry), plus small splashes on the
        /// ground around the camera (one short additive burst per raycast hit, ~250 per second). Both follow the viewing camera.
        /// </summary>
        void UpdateRain()
        {
            var cam = View;
            if (cam == null) return;
            if (rain == null && Weather != WeatherKind.Rain && RainIntensity <= 0f) return; // built on first rain
            if (rain == null)
            {
                var m = RuntimeMaterials.NewParticleSoft("Rain");
                if (m == null) return; // RuntimeMaterials logged why (no library or empty slot)
                m.SetColor(BaseColorId, new Color(0.78f, 0.84f, 0.95f, 0.4f));
                var go = new GameObject("Rain");
                go.transform.SetParent(transform, false);
                rain = go.AddComponent<ParticleSystem>();
                rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = rain.main;
                main.loop = true;
                main.startLifetime = 1.4f;
                main.startSpeed = 0f;
                main.startSize3D = true;
                main.startSizeX = 0.008f;
                main.startSizeY = 0.25f;   // stretched mode: length = size + speed x velocityScale (~0.65 m)
                main.startSizeZ = 0.008f;
                main.maxParticles = 12000;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.8f, 0.86f, 0.98f, 0.1f), new Color(0.85f, 0.9f, 1f, 0.26f));
                var vel = rain.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                var shape = rain.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(44f, 1f, 44f);
                var emission = rain.emission;
                emission.rateOverTime = 0f;
                var r = rain.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.04f;
                r.lengthScale = 1f;
                r.sharedMaterial = m;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                rain.Play();

                var sm = RuntimeMaterials.NewParticle("RainSplash", true);
                if (sm != null)
                {
                    sm.SetTexture("_BaseMap", GlowTexture());
                    var sgo = new GameObject("RainSplashes");
                    sgo.transform.SetParent(transform, false);
                    splashes = sgo.AddComponent<ParticleSystem>();
                    splashes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var sMain = splashes.main;
                    sMain.loop = false;
                    sMain.playOnAwake = false;
                    sMain.startLifetime = 0.14f;
                    sMain.startSpeed = 0f;
                    sMain.maxParticles = 600;
                    sMain.simulationSpace = ParticleSystemSimulationSpace.World;
                    var sEm = splashes.emission; sEm.enabled = false;
                    var sSh = splashes.shape; sSh.enabled = false;
                    var size = splashes.sizeOverLifetime;
                    size.enabled = true;
                    size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 1f));
                    var col = splashes.colorOverLifetime;
                    col.enabled = true;
                    var g = new Gradient();
                    g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                              new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                    col.color = g;
                    var sr = splashes.GetComponent<ParticleSystemRenderer>();
                    sr.renderMode = ParticleSystemRenderMode.Billboard;
                    sr.sharedMaterial = sm;
                    sr.shadowCastingMode = ShadowCastingMode.Off;
                    sr.receiveShadows = false;
                    splashes.Play();
                }
            }
            float k = Mathf.Clamp01(RainIntensity);
            // Wind slant: the needles lean with the global wind (2-6 m/s sideways against 9 m/s fall).
            var wind = WindDirection.sqrMagnitude > 1e-4f ? new Vector3(WindDirection.x, 0f, WindDirection.z).normalized : Vector3.right;
            float windSpeed = Mathf.Lerp(1.5f, 6f, Mathf.Clamp01(WindStrength));
            var v = rain.velocityOverLifetime;
            v.x = new ParticleSystem.MinMaxCurve(wind.x * windSpeed * 0.8f, wind.x * windSpeed * 1.2f);
            v.y = new ParticleSystem.MinMaxCurve(-10f, -8.5f);
            v.z = new ParticleSystem.MinMaxCurve(wind.z * windSpeed * 0.8f, wind.z * windSpeed * 1.2f);
            // Emitter upwind and above, so the slanted needles fill the view around the camera.
            var fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            rain.transform.position = cam.transform.position + Vector3.up * 11f + fwd.normalized * 10f - wind * windSpeed * 1.1f;
            var em = rain.emission;
            em.rateOverTime = 9000f * k;
            EmitSplashes(cam, k);
        }

        ParticleSystem splashes;
        float splashBudget;
        readonly RaycastHit[] splashHit = new RaycastHit[1];

        /// <summary>~250 splashes per second at full rain within 22 m of the camera (one downward raycast each).</summary>
        void EmitSplashes(Camera cam, float k)
        {
            if (splashes == null || k <= 0.01f) return;
            splashBudget += 250f * k * Time.deltaTime;
            var origin = cam.transform.position;
            var fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            int n = Mathf.Min(12, (int)splashBudget);
            splashBudget -= n;
            for (int i = 0; i < n; i++)
            {
                var r2 = UnityEngine.Random.insideUnitCircle * 22f;
                var p = origin + fwd * 9f + new Vector3(r2.x, 30f, r2.y);
                if (Physics.RaycastNonAlloc(p, Vector3.down, splashHit, 80f, 1, QueryTriggerInteraction.Ignore) == 0) continue;
                var ep = new ParticleSystem.EmitParams
                {
                    position = splashHit[0].point + splashHit[0].normal * 0.03f,
                    startSize = UnityEngine.Random.Range(0.1f, 0.22f),
                    startColor = new Color(0.85f, 0.9f, 1f, 0.22f + 0.2f * NightFactor),
                    applyShapeToPosition = false,
                };
                splashes.Emit(ep, 1);
            }
        }
    }
}
