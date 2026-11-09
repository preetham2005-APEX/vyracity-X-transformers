using System.Collections.Generic;
using UnityEngine;
using VeyraCity.Nitro;
using VeyraCity.Racing;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.AI
{
    /// <summary>
    /// AI race driver producing DriverIntent through the same physics path as the player (RACE_RULES_SPEC §6):
    /// pure-pursuit steering on the racing line, a speed profile v = sqrt(mu g / kappa) x skill with braking
    /// zones derived backwards from corner speeds, lateral offsets to overtake, and bounded catch-up (±5 %).
    /// Never teleports (respawns are the race session's rules) and never exceeds the car's physics.
    /// </summary>
    public sealed class AIRacerDriver : MonoBehaviour, IDriverIntentSource
    {
        public VehicleController Vehicle;
        public RaceSession Session;
        public RaceParticipant Me;
        public NitroSystem Nitro;
        [Range(0.8f, 1.05f)] public float Skill = 0.94f;
        [Tooltip("Personal variation so the field spreads out")] public float Aggression = 0.5f;
        public bool CatchUp = true;

        int hint;
        float lateralOffset;
        float lateralTarget;
        float overtakeTimer;
        float nitroHold;
        float mu = 1f;
        float wheelbase = 2.6f;
        float maxSteerRad = 0.6f;
        float personalLine;

        public float TargetSpeed { get; private set; }
        float stuckTimer, recoverTimer, recoverSteer;
        int centreHint;
        /// <summary>-veyraAIDebug: log driving state every 2 s and every collision (diagnostics).</summary>
        public static bool DebugLog = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-veyraAIDebug") >= 0;
        float debugTimer;
        string lastHit = "";
        DriverIntent lastIntent;
        float lastFollow;
        float lastS;

        void LogHit(VehicleController v, Collision c)
        {
            lastHit = c.collider != null ? c.collider.name : "?";
            if (DebugLog && Me != null && Me.IsPlayer) Debug.Log($"[AIDBG] hit {lastHit} impulse {c.impulse.magnitude:0} at {Vehicle.transform.position} speed {Vehicle.State.SpeedKmh:0}");
        }

        void FixedUpdate()
        {
            if (!DebugLog || Vehicle == null || Me == null || !Me.IsPlayer) return;
            debugTimer -= Time.fixedDeltaTime;
            if (debugTimer > 0f) return;
            debugTimer = 2f;
            var st = Vehicle.State;
            var line = Session != null ? Session.RacingLine : null;
            float lat = line != null ? line.LateralOffset(Vehicle.transform.position, lastS) : 0f;
            Debug.Log($"[AIDBG] t {Time.time:0.0} pos {Vehicle.transform.position} v {st.SpeedKmh:0} target {TargetSpeed * 3.6f:0} follow {lastFollow * 3.6f:0} thr {lastIntent.Throttle:0.00} brk {lastIntent.Brake:0.00} steer {lastIntent.Steer:0.00} gear {st.Gear} rpm {st.EngineRpm:0} grounded {st.GroundedWheels} s {lastS:0} lat {lat:0.0} sinceHit {Vehicle.TimeSinceCollision:0.0} lastHit {lastHit} up {Vehicle.transform.up.y:0.00}");
        }

        public void Init(VehicleController v, RaceSession session, RaceParticipant me, NitroSystem nitro, float skill)
        {
            Vehicle = v;
            Session = session;
            Me = me;
            Nitro = nitro;
            Skill = skill;
            var d = v.Definition;
            mu = Mathf.Min(d.Front.Tyre.Mu * d.Front.Tyre.LateralGripScale, d.Rear.Tyre.Mu * d.Rear.Tyre.LateralGripScale);
            if (mu <= 0f) mu = 1f;
            wheelbase = Mathf.Max(2f, d.Wheelbase);
            maxSteerRad = Mathf.Max(10f, d.Steering.MaxAngleDeg) * Mathf.Deg2Rad;
            personalLine = Random.Range(-0.6f, 0.6f);
            hint = 0;
            if (DebugLog) { v.Collided -= LogHit; v.Collided += LogHit; }
        }

        public DriverIntent ReadIntent()
        {
            var intent = new DriverIntent();
            if (Vehicle == null || Session == null || Session.RacingLine == null) return intent;
            float dtR = Time.fixedDeltaTime;
            if (recoverTimer > 0f)
            {
                // Back away from whatever we hit, steering the other way, then try again.
                recoverTimer -= dtR;
                intent.Brake = 1f;
                intent.Steer = recoverSteer;
                return intent;
            }
            if (Session.State == RaceSession.Phase.Racing && Vehicle.State.Speed < 1f && lastIntent.Throttle > 0.3f && Vehicle.TimeSinceCollision < 1.5f)
            {
                stuckTimer += dtR;
                if (stuckTimer > 1.2f)
                {
                    stuckTimer = 0f;
                    recoverTimer = 1.6f;
                    recoverSteer = -Mathf.Sign(lastIntent.Steer == 0f ? 1f : lastIntent.Steer);
                    intent.Brake = 1f;
                    lastIntent = intent;
                    return intent;
                }
            }
            else stuckTimer = 0f;
            var line = Session.RacingLine;
            var pos = Vehicle.transform.position;
            float s = line.Project(pos, ref hint, 25);
            if (Mathf.Abs(line.LateralOffset(pos, s)) > 40f) s = line.Project(pos, ref hint, 0);
            float v = Vehicle.State.Speed;
            float dt = Time.fixedDeltaTime;

            // --- Traffic awareness: other racers ahead.
            UpdateOvertake(pos, v, s, line, dt, out float followSpeed);

            // --- Steering: pure pursuit on the racing line plus lateral offset.
            lateralOffset = Mathf.MoveTowards(lateralOffset, lateralTarget + personalLine, dt * 2.2f);
            float look = Mathf.Clamp(5f + v * 0.5f, 7f, 38f);
            float sa = s + look;
            var target = line.PositionAt(sa);
            var tan = line.TangentAt(sa);
            var right = new Vector3(tan.z, 0f, -tan.x).normalized;
            float half = Session.HalfWidthAt(sa);
            target += right * Mathf.Clamp(lateralOffset, -(half - 1.4f), half - 1.4f);
            // The racing line already sits off-centre in corners: clamp the final aim point to the road itself.
            var centre = Session.Path;
            float sc = centre.Project(target, ref centreHint, 20);
            float latC = centre.LateralOffset(target, sc);
            float limit = Mathf.Max(0.3f, Session.HalfWidthAt(sc) - 1.25f);
            if (Mathf.Abs(latC) > limit)
            {
                var ct = centre.TangentAt(sc);
                var cr = new Vector3(ct.z, 0f, -ct.x).normalized;
                target -= cr * (latC - Mathf.Sign(latC) * limit);
            }
            var local = Vehicle.transform.InverseTransformPoint(target);
            float ld = Mathf.Max(3f, new Vector2(local.x, local.z).magnitude);
            float steerRad = Mathf.Atan2(2f * wheelbase * (local.x / ld), ld);
            // Yaw-rate damping keeps it stable at speed.
            steerRad -= Vehicle.State.YawRate * 0.02f;
            intent.Steer = Mathf.Clamp(steerRad / maxSteerRad, -1f, 1f);

            // --- Speed profile from curvature ahead with braking distance.
            float g = 9.81f;
            float catchUp = 1f;
            if (CatchUp && Session.Player != null && Me != null && Session.State == RaceSession.Phase.Racing)
            {
                float gap = Session.Player.Progress - Me.Progress; // + means player ahead
                catchUp = 1f + Mathf.Clamp(gap / 400f, -1f, 1f) * 0.05f;
            }
            float grip = mu * g * Mathf.Lerp(0.82f, 0.97f, Mathf.InverseLerp(0.85f, 1.02f, Skill));
            float brakeDecel = mu * g * 0.82f;
            float vTarget = 95f;
            float horizon = Mathf.Clamp(v * v / (2f * brakeDecel) + 30f, 40f, 260f);
            for (float d = 4f; d <= horizon; d += 6f)
            {
                float k = Mathf.Abs(line.CurvatureAt(s + d, 9f));
                float vCorner = Mathf.Sqrt(grip / Mathf.Max(k, 1e-4f));
                float allowed = Mathf.Sqrt(vCorner * vCorner + 2f * brakeDecel * Mathf.Max(0f, d - 6f));
                if (allowed < vTarget) vTarget = allowed;
            }
            vTarget *= Mathf.Clamp(Skill * catchUp, 0.8f, 1.06f);
            vTarget = Mathf.Min(vTarget, followSpeed);
            TargetSpeed = vTarget;
            lastFollow = followSpeed;
            lastS = s;

            float err = vTarget - v;
            if (err > 1.5f) { intent.Throttle = 1f; intent.Brake = 0f; }
            else if (err > -1.5f) { intent.Throttle = Mathf.Clamp01(0.45f + err * 0.3f); intent.Brake = 0f; }
            else { intent.Throttle = 0f; intent.Brake = Mathf.Clamp01(-err * 0.18f); }
            // Large steering at speed: ease off.
            if (Mathf.Abs(intent.Steer) > 0.7f && v > 15f) intent.Throttle *= 0.6f;

            // --- Nitro on straights.
            if (Nitro != null && Session.State == RaceSession.Phase.Racing)
            {
                float straight = 0f;
                for (float d = 10f; d <= 120f; d += 15f) straight = Mathf.Max(straight, Mathf.Abs(line.CurvatureAt(s + d, 12f)));
                bool want = straight < 0.004f && err > 3f && Nitro.Amount > 25f * (1.2f - Aggression * 0.4f);
                if (want) nitroHold = 1.2f;
                nitroHold -= dt;
                intent.Nitro = nitroHold > 0f;
            }
            lastIntent = intent;
            return intent;
        }

        void UpdateOvertake(Vector3 pos, float v, float s, PolylinePath line, float dt, out float followSpeed)
        {
            followSpeed = 999f;
            if (Session == null) return;
            overtakeTimer -= dt;
            RaceParticipant blocker = null;
            float look = 20f + v * 1.6f;
            float bestAhead = look;
            float blockerLocalX = 0f;
            foreach (var p in Session.Participants)
            {
                if (p == Me || p.Vehicle == null) continue;
                var local = Vehicle.transform.InverseTransformPoint(p.Vehicle.transform.position);
                if (local.z > 1f && local.z < bestAhead && Mathf.Abs(local.x) < 3.0f)
                {
                    bestAhead = local.z;
                    blocker = p;
                    blockerLocalX = local.x;
                }
            }
            if (blocker == null)
            {
                if (overtakeTimer <= 0f) lateralTarget = Mathf.MoveTowards(lateralTarget, 0f, dt * 1.5f);
                return;
            }
            float theirs = Mathf.Max(0f, Vector3.Dot(blocker.Vehicle.Body.linearVelocity, Vehicle.transform.forward));
            float closing = v - theirs;
            float half = Session.HalfWidthAt(s);
            if (overtakeTimer <= 0f && closing > -0.5f)
            {
                // pass on the side with more room, as far as the road allows
                float side = blockerLocalX > 0f ? -1f : 1f;
                lateralTarget = half < 4.5f ? side * Mathf.Max(0.6f, half - 2.4f) : side * Mathf.Clamp(half - 1.6f, 2.2f, 4.5f);
                overtakeTimer = 2.5f;
            }
            // Brake early enough to match their speed with a 4 m/s^2 decel, unless we are already alongside.
            float lateralGap = Mathf.Abs(blockerLocalX - lateralOffset);
            if (lateralGap < 2.4f && closing > 0f)
            {
                float room = Mathf.Max(0f, bestAhead - 7f);
                followSpeed = theirs + Mathf.Sqrt(2f * 4f * room);
            }
        }
    }
}
