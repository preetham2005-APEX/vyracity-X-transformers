using UnityEngine;

namespace VeyraCity.World
{
    /// <summary>
    /// The one place that drives every time-of-day and weather dependent material (cards F2/F3): global shader properties
    /// read by the Veyra/* shaders. WorldEnvironment calls <see cref="Apply"/> every frame (Edit and Play mode); a property is
    /// only sent to the GPU when its value changed. No shader keyword is toggled (card F1).
    /// </summary>
    public static class VeyraGlobals
    {
        public static readonly int TimeOfDayId = Shader.PropertyToID("_VC_TimeOfDay");
        public static readonly int NightFactorId = Shader.PropertyToID("_VC_NightFactor");
        public static readonly int WetnessId = Shader.PropertyToID("_VC_Wetness");
        public static readonly int RainIntensityId = Shader.PropertyToID("_VC_RainIntensity");
        public static readonly int WindStrengthId = Shader.PropertyToID("_VC_WindStrength");
        public static readonly int WindDirectionId = Shader.PropertyToID("_VC_WindDirection");
        public static readonly int SunDirectionId = Shader.PropertyToID("_VC_SunDirection");

        public static float TimeOfDay { get; private set; } = -1f;
        public static float NightFactor { get; private set; } = -1f;
        public static float Wetness { get; private set; } = -1f;
        public static float RainIntensity { get; private set; } = -1f;
        public static float WindStrength { get; private set; } = -1f;
        public static Vector3 WindDirection { get; private set; } = new Vector3(float.NaN, 0f, 0f);
        public static Vector3 SunDirection { get; private set; } = new Vector3(float.NaN, 0f, 0f);
        /// <summary>Number of SetGlobal calls made (diagnostics: shows the "only when changed" rule working).</summary>
        public static int Writes { get; private set; }

        /// <summary>
        /// Card F3's night curve: 0 by day, 1 at night, smoothstep 18:00 → 20:00 in the evening and 05:30 → 07:00 in the morning.
        /// </summary>
        public static float NightFactorAt(float hours)
        {
            float h = Mathf.Repeat(hours, 24f);
            float evening = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(18f, 20f, h));
            float morning = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(5.5f, 7f, h));
            return h >= 12f ? evening : morning;
        }

        public static void Apply(float timeOfDay, float wetness, float rainIntensity, float windStrength, Vector3 windDirection, Vector3 sunDirection)
        {
            float t = Mathf.Repeat(timeOfDay, 24f);
            SetFloat(TimeOfDayId, t, TimeOfDay, v => TimeOfDay = v, 0.001f);
            SetFloat(NightFactorId, NightFactorAt(t), NightFactor, v => NightFactor = v, 0.0005f);
            SetFloat(WetnessId, Mathf.Clamp01(wetness), Wetness, v => Wetness = v, 0.0005f);
            SetFloat(RainIntensityId, Mathf.Clamp01(rainIntensity), RainIntensity, v => RainIntensity = v, 0.0005f);
            SetFloat(WindStrengthId, Mathf.Clamp01(windStrength), WindStrength, v => WindStrength = v, 0.0005f);
            var wd = windDirection.sqrMagnitude > 1e-6f ? windDirection.normalized : Vector3.right;
            if (!(Vector3.Distance(wd, WindDirection) < 1e-4f)) { WindDirection = wd; Shader.SetGlobalVector(WindDirectionId, wd); Writes++; }
            var sd = sunDirection.sqrMagnitude > 1e-6f ? sunDirection.normalized : Vector3.up;
            if (!(Vector3.Distance(sd, SunDirection) < 1e-4f)) { SunDirection = sd; Shader.SetGlobalVector(SunDirectionId, sd); Writes++; }
        }

        static void SetFloat(int id, float value, float current, System.Action<float> store, float epsilon)
        {
            if (Mathf.Abs(value - current) <= epsilon) return;
            store(value);
            Shader.SetGlobalFloat(id, value);
            Writes++;
        }

        /// <summary>Forgets the cached values so the next <see cref="Apply"/> writes every property (after a domain reload or for tests).</summary>
        public static void Invalidate()
        {
            TimeOfDay = NightFactor = Wetness = RainIntensity = WindStrength = -1f;
            WindDirection = SunDirection = new Vector3(float.NaN, 0f, 0f);
        }
    }
}
