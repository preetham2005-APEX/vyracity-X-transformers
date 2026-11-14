using System.Collections.Generic;
using UnityEngine;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;
using VeyraCity.World.Data;

namespace VeyraCity.Testing
{
    /// <summary>
    /// Card V0 autopilot: follows the centreline of the right-hand half of a road (lane path built from the road profile in
    /// road_network_geometry.json) at a target speed. Produces DriverIntent like the player and the AI (pure-pursuit steering
    /// with yaw-rate damping, PI speed control with curvature limits), so it exercises the same assist and physics path.
    /// </summary>
    public sealed class RouteAutopilot : IDriverIntentSource
    {
        readonly VehicleController car;
        readonly float wheelbase, maxSteerRad, mu;
        int hint;
        float integral;

        public PolylinePath Path { get; private set; }
        public float EndU { get; private set; }
        public float TargetSpeedMps { get; private set; }
        /// <summary>Arc length of the car along the lane path, m.</summary>
        public float U { get; private set; }
        /// <summary>Signed distance from the lane centre, m (+ = right).</summary>
        public float LateralError { get; private set; }
        public bool Active { get; set; }
        /// <summary>Phase E follow-up (D2): brake for vehicles in the lane ahead, so V1 routes can run with traffic.</summary>
        public bool AvoidTraffic { get; set; } = true;
        /// <summary>Distance along the lane to the nearest vehicle ahead (m), or -1 when the lane is clear.</summary>
        public float ObstacleDistance { get; private set; } = -1f;
        /// <summary>Fixed steps in which the traffic check lowered the target speed (for the run report).</summary>
        public int ObstacleSlowdowns { get; private set; }
        readonly Collider[] probeHits = new Collider[16];

        public RouteAutopilot(VehicleController car)
        {
            this.car = car;
            var d = car.Definition;
            wheelbase = Mathf.Max(2f, d.Wheelbase);
            maxSteerRad = Mathf.Max(10f, d.Steering.MaxAngleDeg) * Mathf.Deg2Rad;
            mu = Mathf.Min(d.Front.Tyre.Mu * d.Front.Tyre.LateralGripScale, d.Rear.Tyre.Mu * d.Rear.Tyre.LateralGripScale);
            if (mu <= 0f) mu = 1f;
        }

        public void Begin(PolylinePath lanePath, float endU, float targetSpeedMps)
        {
            Path = lanePath;
            EndU = endU;
            TargetSpeedMps = targetSpeedMps;
            hint = 0;
            integral = 0f;
            U = 0f;
            Active = true;
        }

        public DriverIntent ReadIntent()
        {
            var intent = new DriverIntent();
            if (!Active || Path == null || car == null) { intent.Brake = car != null && car.State.Speed > 1f ? 1f : 0f; return intent; }
            float dt = Time.fixedDeltaTime;
            var pos = car.transform.position;
            U = Path.Project(pos, ref hint, 30);
            LateralError = Path.LateralOffset(pos, U);
            float v = car.State.Speed;

            // Steering: pure pursuit on the lane path, damped by yaw rate (same law as AIRacerDriver).
            float look = Mathf.Clamp(6f + 0.55f * v, 8f, 40f);
            var local = car.transform.InverseTransformPoint(Path.PositionAt(U + look));
            float ld = Mathf.Max(3f, new Vector2(local.x, local.z).magnitude);
            float steerRad = Mathf.Atan2(2f * wheelbase * (local.x / ld), ld) - car.State.YawRate * 0.02f;
            intent.Steer = Mathf.Clamp(steerRad / maxSteerRad, -1f, 1f);

            // Speed: target, limited by curvature ahead (75 % of the friction limit) with 4 m/s² of braking room.
            float vt = TargetSpeedMps;
            for (float d = 0f; d <= 80f; d += 10f)
            {
                float k = Mathf.Abs(Path.CurvatureAt(U + d, 8f));
                if (k < 1e-4f) continue;
                float vc = Mathf.Sqrt(0.75f * mu * 9.81f / k);
                vt = Mathf.Min(vt, Mathf.Sqrt(vc * vc + 2f * 4f * d));
            }
            if (AvoidTraffic)
            {
                ObstacleDistance = VehicleAhead(Mathf.Clamp(12f + v * 2.2f, 20f, 90f), out float otherSpeed);
                if (ObstacleDistance >= 0f)
                {
                    // stop 9 m short of it at 5 m/s², plus the speed it is moving away with
                    float room = Mathf.Max(0f, ObstacleDistance - 9f);
                    float vObs = Mathf.Sqrt(2f * 5f * room) + Mathf.Max(0f, otherSpeed);
                    if (vObs < vt) { vt = vObs; ObstacleSlowdowns++; }
                }
            }
            float e = vt - v;
            integral = Mathf.Clamp(integral + e * dt, -6f, 6f);
            float u = 0.35f * e + 0.08f * integral;
            if (u >= 0f) intent.Throttle = Mathf.Clamp01(u);
            else if (e < -1f && v > 1f) intent.Brake = Mathf.Clamp01(-u * (ObstacleDistance >= 0f && ObstacleDistance < 25f ? 1.2f : 0.6f)); // never brake at a standstill (that engages reverse)
            if (ObstacleDistance >= 0f && ObstacleDistance < 10f && intent.Throttle > 0f && v < 2f) intent.Throttle = 0f;   // wait behind a stopped car
            return intent;
        }

        /// <summary>
        /// Nearest other vehicle (a collider with a rigidbody that is not this car) on the lane path within
        /// <paramref name="range"/> m ahead: overlap spheres every 5 m along the lane, so curves are followed and cars in the
        /// other lane or parked at the kerb are not hit. Walls and props (no rigidbody) are ignored. Returns -1 when clear.
        /// </summary>
        float VehicleAhead(float range, out float otherSpeed)
        {
            otherSpeed = 0f;
            Vector3 fwd = car.transform.forward;
            for (float d = 5f; d <= range; d += 5f)
            {
                Vector3 c = Path.PositionAt(U + d) + Vector3.up * 0.9f;
                int n = Physics.OverlapSphereNonAlloc(c, 1.3f, probeHits, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var rb = probeHits[i].attachedRigidbody;
                    if (rb == null || rb == car.Body) continue;
                    otherSpeed = Vector3.Dot(rb.isKinematic ? Vector3.zero : rb.linearVelocity, fwd);
                    return d;
                }
            }
            return -1f;
        }

        /// <summary>Lane offset to the right of travel: the centre of the right-hand half of the class width.</summary>
        public static float LaneOffset(RoadRecord road) => road.ClassInfo.WidthM * 0.25f;

        /// <summary>
        /// The lane path for one segment: road profile points (Unity space) from sStart towards sEnd, shifted right of travel by
        /// <see cref="LaneOffset"/>, with <paramref name="extensionM"/> extra metres past the end for look-ahead.
        /// <paramref name="endU"/> is the arc length on the lane path where the segment ends.
        /// </summary>
        public static PolylinePath BuildLanePath(RoadRecord road, float sStart, float sEnd, float extensionM, out float endU)
        {
            var full = RoadPath(road);
            float dir = sEnd >= sStart ? 1f : -1f;
            float length = Mathf.Abs(sEnd - sStart);
            float offset = LaneOffset(road);
            const float step = 2f;
            var pts = new List<Vector3>();
            int endIndex = -1;
            for (float d = 0f; ; d += step)
            {
                bool last = false;
                if (d >= length + extensionM) { d = length + extensionM; last = true; }
                float s = sStart + dir * d;
                if (!full.Closed && (s < 0f || s > full.Length)) break;
                var c = full.PositionAt(s);
                var t = full.TangentAt(s) * dir;
                var right = new Vector3(t.z, 0f, -t.x).normalized;
                pts.Add(c + right * offset);
                if (endIndex < 0 && d >= length - 1e-3f) endIndex = pts.Count - 1;
                if (last) break;
            }
            if (pts.Count < 2) { pts.Add(pts.Count == 1 ? pts[0] + Vector3.forward : Vector3.forward); }
            var lane = new PolylinePath(pts.ToArray(), false);
            endU = endIndex >= 0 ? lane.S[endIndex] : lane.Length;
            return lane;
        }

        static readonly Dictionary<RoadRecord, PolylinePath> cache = new Dictionary<RoadRecord, PolylinePath>();

        /// <summary>The road's profile as a Unity-space polyline (cached).</summary>
        public static PolylinePath RoadPath(RoadRecord road)
        {
            if (cache.TryGetValue(road, out var p)) return p;
            var pts = new Vector3[road.SampleCount];
            for (int i = 0; i < pts.Length; i++) pts[i] = road.ProfileUnity(i);
            p = new PolylinePath(pts, road.Closed);
            cache[road] = p;
            return p;
        }
    }
}
