using System;
using System.Collections.Generic;
using UnityEngine;

namespace VeyraCity.Testing
{
    /// <summary>
    /// Card V0 test route: road segments driven by the autopilot. Distances (s) are metres along the road's profile in
    /// road_network_geometry.json; sStart_m &gt; sEnd_m drives against the profile direction. Each segment starts with a
    /// teleport onto the drivable surface at its target speed.
    /// </summary>
    [CreateAssetMenu(menuName = "Veyra/Test Route", fileName = "R_NEW")]
    public sealed class VeyraTestRoute : ScriptableObject
    {
        [Serializable]
        public struct Segment
        {
            public string roadId;
            public float sStart_m;
            public float sEnd_m;
            public float targetSpeed_kmh;
            [Tooltip("What this segment covers (bridge names etc.)")] public string note;

            public float LengthM => Mathf.Abs(sEnd_m - sStart_m);
            public override string ToString() => $"{roadId} {sStart_m:0}→{sEnd_m:0} m @ {targetSpeed_kmh:0} km/h";
        }

        [TextArea] public string Description;
        [Tooltip("Hour used when the command line has no -timeOfDay")] public float DefaultTimeOfDay = 12f;
        public List<Segment> Segments = new List<Segment>();

        public float TotalLengthM
        {
            get { float t = 0f; foreach (var s in Segments) t += s.LengthM; return t; }
        }

        /// <summary>Driving time at target speed, excluding teleports.</summary>
        public float NominalSeconds
        {
            get { float t = 0f; foreach (var s in Segments) t += s.LengthM / Mathf.Max(1f, s.targetSpeed_kmh / 3.6f); return t; }
        }
    }
}
