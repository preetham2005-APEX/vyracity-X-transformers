using System;
using System.Collections.Generic;
using UnityEngine;

namespace VeyraCity.Testing
{
    /// <summary>
    /// Card S5 review cameras: the same viewpoints for every world-changing card (Appendix E). FIXED cameras sit at a Unity
    /// position (absolute Y, or Y metres above the ground/road found by a raycast) looking at a point. CHASE cameras take a point
    /// on a road profile (road_network_geometry.json, right-hand lane), put the car there facing along the road and sit
    /// <see cref="Entry.back"/> m behind and <see cref="Entry.up"/> m above it, looking <see cref="Entry.lookAhead"/> m ahead,
    /// so driving shots stay valid when roads are rebuilt. Asset: Assets/_Veyra/Data/ReviewCameras.asset (Veyra/Review menus).
    /// </summary>
    [CreateAssetMenu(menuName = "Veyra/Review Camera Set", fileName = "ReviewCameras")]
    public sealed class ReviewCameraSet : ScriptableObject
    {
        public enum Kind { Fixed, Chase }
        public enum TimeLabel { Day, Golden, Dusk, Night, Morning }

        [Serializable]
        public sealed class Entry
        {
            public string name;
            public Kind kind;
            [Tooltip("D1–D8, or World")] public string district;

            [Header("Fixed")]
            [Tooltip("Unity X, Y, Z. Y is absolute unless 'heightAboveGround' is set")] public Vector3 position;
            [Tooltip("Y is metres above the ground or road under (X, Z)")] public bool heightAboveGround;
            public Vector3 lookAt;

            [Header("Chase")]
            public string roadId;
            [Tooltip("Metres along the road profile")] public float distanceAlongRoad_m;
            [Tooltip("+1 = profile direction, -1 = against it")] public int direction = 1;
            public float back = 7.5f, up = 2.1f, lookAhead = 30f;

            [Header("View")]
            public float fov = 60f;
            public TimeLabel time;
            [Tooltip("Clock hour used for this label (sun angle)")] public float hour = 12f;
            [Tooltip("Wet roads (no rain)")] public bool wet;
            [TextArea] public string note;

            public override string ToString() => kind == Kind.Fixed
                ? $"{name} FIXED {position} → {lookAt} fov {fov} {time}"
                : $"{name} CHASE {roadId} {distanceAlongRoad_m:0} m dir {direction:+0;-0} ahead {lookAhead} fov {fov} {time}{(wet ? " wet" : "")}";
        }

        public List<Entry> Cameras = new List<Entry>();

        public Entry Find(string cameraName) => Cameras.Find(c => string.Equals(c.name, cameraName, StringComparison.OrdinalIgnoreCase));

        /// <summary>Cameras whose name contains any of the comma-separated filter terms (all cameras when the filter is empty).</summary>
        public List<Entry> Filter(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return new List<Entry>(Cameras);
            var terms = filter.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return Cameras.FindAll(c => Array.Exists(terms, t => c.name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0));
        }
    }
}
