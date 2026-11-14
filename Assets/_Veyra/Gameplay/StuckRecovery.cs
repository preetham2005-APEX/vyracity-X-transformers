using System;
using UnityEngine;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Realism pass §1.1 (D2): a stuck car can always recover. "Stuck" = throttle (or reverse) held, under 1.5 m/s, and less
    /// than 1 m of progress for <see cref="StuckSeconds"/>. The first response is a nudge: a velocity change up and toward the
    /// nearest road (the way out of a ditch or off a kerb lip); a second stuck spell within <see cref="NudgeWindow"/> s respawns
    /// the car on the nearest road lane, facing along it. Flips stay with HandlingFeel.FlipRecoverSeconds. Values are tuning
    /// fields here because the component is per car; the HUD shows what happened through <see cref="Recovered"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StuckRecovery : MonoBehaviour
    {
        [Tooltip("Seconds of throttle without progress before the car counts as stuck")] public float StuckSeconds = 2.5f;
        [Tooltip("Below this speed (m/s) the car may be stuck")] public float StuckSpeed = 1.5f;
        [Tooltip("Less progress than this (m) over the stuck time counts as none")] public float ProgressMetres = 1f;
        [Tooltip("Nudge: velocity change up and toward the road, m/s")] public float NudgeUp = 2.6f, NudgeToRoad = 3.2f;
        [Tooltip("A second stuck spell within this many seconds of a nudge respawns on the road")] public float NudgeWindow = 8f;
        [Tooltip("How far to look for a road, m")] public float RoadSearch = 120f;

        /// <summary>Raised with "nudge" or "respawn" (HUD toast, telemetry, tests).</summary>
        public event Action<StuckRecovery, string> Recovered;
        public int Nudges { get; private set; }
        public int Respawns { get; private set; }

        VehicleController car;
        float timer, lastNudge = -100f;
        Vector3 anchor;

        public void Bind(VehicleController v) { car = v; anchor = v.transform.position; }

        void FixedUpdate()
        {
            if (car == null || !car.IsInitialised) return;
            var c = car.Controls;
            bool pushing = c.Throttle > 0.4f || (c.WantsReverse && c.Brake > 0.4f);
            float v = car.State.Speed;
            Vector3 p = car.transform.position;
            if (!pushing || v > StuckSpeed)
            {
                timer = 0f;
                anchor = p;
                return;
            }
            timer += Time.fixedDeltaTime;
            if ((p - anchor).sqrMagnitude > ProgressMetres * ProgressMetres) { timer = 0f; anchor = p; return; }
            if (timer < StuckSeconds) return;
            timer = 0f;
            anchor = p;
            if (Time.time - lastNudge < NudgeWindow) Respawn(p);
            else Nudge(p);
        }

        void Nudge(Vector3 p)
        {
            lastNudge = Time.time;
            Vector3 toRoad = Vector3.zero;
            if (WorldData.NearestRoad(p, RoadSearch, out var hit))
            {
                toRoad = hit.Point - p;
                toRoad.y = 0f;
                if (toRoad.sqrMagnitude > 0.01f) toRoad.Normalize();
            }
            if (toRoad == Vector3.zero) toRoad = Vector3.ProjectOnPlane(car.transform.forward * (car.Controls.WantsReverse ? -1f : 1f), Vector3.up).normalized;
            var rb = car.Body;
            rb.AddForce(Vector3.up * NudgeUp + toRoad * NudgeToRoad, ForceMode.VelocityChange);
            Nudges++;
            Recovered?.Invoke(this, "nudge");
        }

        /// <summary>The nearest point index (searching both ways from <paramref name="from"/>) that is not a bridge, tunnel or
        /// elevated section, or -1 when the whole road is a structure.</summary>
        static int AtGradeSegment(RoadPath road, int from)
        {
            int n = road.Path.Points.Length;
            for (int k = 0; k < n; k++)
            {
                int a = from - k, b = from + k;
                if (a >= 0 && a < n && road.StructureAt(a) == StructureKind.None) return a;
                if (b >= 0 && b < n && road.StructureAt(b) == StructureKind.None) return b;
            }
            return -1;
        }

        void Respawn(Vector3 p)
        {
            lastNudge = -100f;
            if (!WorldData.NearestRoad(p, RoadSearch * 4f, out var hit)) return;
            // coordinator check 2: lane and direction from the road data only, and never onto a structure (bridge, tunnel,
            // elevated): walk along the road from the nearest point to the closest at-grade point
            var road = WorldData.Roads[hit.Road];
            int seg = AtGradeSegment(road, hit.Segment);
            if (seg < 0) return;
            var pts = road.Path.Points;
            int nxt = Mathf.Min(seg + 1, pts.Length - 1), prv = Mathf.Max(0, nxt - 1);
            var tangent = pts[nxt] - pts[prv];
            tangent.y = 0f;
            if (tangent.sqrMagnitude < 1e-4f) tangent = Vector3.forward;
            tangent.Normalize();
            hit.Point = pts[seg];
            // keep the car's rough heading along the road and put it in the right-hand lane of that direction
            if (Vector3.Dot(tangent, car.transform.forward) < 0f) tangent = -tangent;
            var right = new Vector3(tangent.z, 0f, -tangent.x);
            float width = WorldData.Roads[hit.Road].Data.width;
            Vector3 spot = hit.Point + right * (width * 0.25f);
            spot.y = WorldData.GroundHeight(spot + Vector3.up * 5f, hit.Point.y) + 0.6f;
            car.ResetTo(spot, Quaternion.LookRotation(tangent, Vector3.up));
            Respawns++;
            Recovered?.Invoke(this, "respawn");
        }
    }
}
