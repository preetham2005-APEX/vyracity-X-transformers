using UnityEngine;
using VeyraCity.World;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Visual iteration 22 (GTA V / Forza aerial perspective): Unity's fog has one colour, so a city looks equally grey
    /// whichever way you face. Each frame, after WorldEnvironment has written the fog colour for the time and weather,
    /// this tints it by the view: looking towards the sun the haze takes the sun's colour (forward Mie scattering,
    /// strongest at golden hour, damped by cloud), looking away it turns cooler and bluer; at night a sodium-orange glow
    /// of light pollution sits in the city's haze. Runs before SkyDome's LateUpdate so the sky's horizon haze matches.
    /// Fog density and the profiles are untouched.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class AtmosphereScatter : MonoBehaviour
    {
        public static bool Enabled = true;

        void LateUpdate()
        {
            if (!Enabled) return;
            var env = WorldEnvironment.Instance;
            var cam = Camera.main;
            if (env == null || cam == null || env.Sun == null) return;
            Color fog = RenderSettings.fogColor;   // written by WorldEnvironment.Update this frame
            float night = env.NightFactor, day = 1f - night, golden = env.GoldenFactor;
            float clear = 1f - 0.7f * Mathf.Clamp01(env.Cloudiness);
            Vector3 view = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            Vector3 sun = Vector3.ProjectOnPlane(-env.Sun.transform.forward, Vector3.up);
            float mu = view.sqrMagnitude > 1e-4f && sun.sqrMagnitude > 1e-4f ? Vector3.Dot(view.normalized, sun.normalized) * 0.5f + 0.5f : 0.5f;
            // towards the sun: its colour scattered into the haze
            fog += env.Sun.color * ((0.16f + 0.42f * golden) * day * clear * Mathf.Pow(mu, 3f));
            // away from it: a cooler, bluer haze (sky light)
            fog = Color.Lerp(fog, new Color(fog.r * 0.88f, fog.g * 0.95f, fog.b * 1.06f), (1f - mu) * day * clear * 0.7f);
            // night: sodium light pollution over the city
            fog += new Color(0.07f, 0.04f, 0.018f) * night;
            fog.a = 1f;
            RenderSettings.fogColor = fog;
        }
    }
}
