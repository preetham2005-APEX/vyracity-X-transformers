using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VeyraCity.World
{
    /// <summary>
    /// Card L2: drives the weights of the post-processing volumes every frame (no profile values are written at runtime, so
    /// no asset changes in Play mode). The Day profile sits on WorldEnvironment's base volume (priority 0); Golden (priority 1)
    /// and Night (priority 2) are global volumes whose weights come from <see cref="WorldEnvironment.GoldenFactor"/> and
    /// <see cref="WorldEnvironment.NightFactor"/>, so URP blends day → golden → night. District looks (Neon Mile at night,
    /// Ironport and Blackridge by day) are global volumes weighted by the camera's position in the district polygon, faded
    /// over <see cref="Area.Blend"/> metres at the border. Tunnels use ordinary local box volumes (built by the editor tool).
    /// Built by Veyra/Rendering/Build Post Volumes (VeyraPostVolumes).
    /// </summary>
    [ExecuteAlways]
    public sealed class PostVolumeDirector : MonoBehaviour
    {
        public enum TimeGate { Always, NightOnly, DayOnly }

        [Serializable]
        public sealed class Area
        {
            public string Id;
            public Volume Volume;
            /// <summary>District polygon in Unity XZ (data x − 4000, y − 3000).</summary>
            public Vector2[] Polygon;
            public TimeGate Gate;
            public float Blend = 60f;
        }

        public static PostVolumeDirector Active { get; private set; }

        public WorldEnvironment Environment;
        public Volume Golden, Night;
        public Area[] Areas = Array.Empty<Area>();

        void OnEnable() => Active = this;
        void OnDisable() { if (Active == this) Active = null; }

        void LateUpdate()
        {
            var env = Environment != null ? Environment : WorldEnvironment.Instance;
            float night = env != null ? env.NightFactor : 0f;
            float golden = env != null ? env.GoldenFactor : 0f;
            if (Golden != null) Golden.weight = golden;
            if (Night != null) Night.weight = night;
            var cam = Camera.main;
            if (cam == null || Areas == null) return;
            var p = new Vector2(cam.transform.position.x, cam.transform.position.z);
            foreach (var a in Areas)
            {
                if (a?.Volume == null) continue;
                float time = a.Gate == TimeGate.NightOnly ? night : a.Gate == TimeGate.DayOnly ? 1f - night : 1f;
                a.Volume.weight = time > 0.001f ? time * Inside(a.Polygon, p, a.Blend) : 0f;
            }
        }

        /// <summary>1 deep inside the polygon, fading to 0 at the border over <paramref name="blend"/> metres (inside only).</summary>
        public static float Inside(Vector2[] poly, Vector2 p, float blend)
        {
            if (poly == null || poly.Length < 3) return 0f;
            bool inside = false;
            float best = float.MaxValue;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[i], b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
                var ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            if (!inside) return 0f;
            return blend <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(best / blend));
        }
    }
}
