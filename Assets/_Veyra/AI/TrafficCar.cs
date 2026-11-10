using System.Collections.Generic;
using UnityEngine;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.AI
{
    /// <summary>One traffic car: lane following on a road polyline until hit, then free physics.</summary>
    public sealed class TrafficCar : MonoBehaviour
    {
        RoadPath road;
        float s;
        int dir;
        float offset;
        float cruise;
        float speed;
        Rigidbody rb;
        float crashedTime;
        MaterialPropertyBlock mpb;
        Renderer[] paintRenderers;
        int[] paintSlots;
        int roadIndex = -1;
        // junction turn (iteration 7): a Bezier from the end of this road onto a connected one, instead of vanishing
        bool turning;
        Vector3 tP0, tP1, tP2, tP3;
        float tU, tLen, turnSpeed;
        RoadPath nextRoad;
        int nextRoadIndex, nextDir;
        float nextS;
        static readonly List<WorldData.RoadHit> roadHits = new List<WorldData.RoadHit>();

        public bool Crashed { get; private set; }
        public bool Expired { get; private set; }
        public bool NearArmed;
        public int Id;
        public float TimeSinceContact { get; private set; } = 99f;
        public Vector3 Forward { get; private set; }
        public float Speed => Crashed ? 0f : speed;
        /// <summary>Where this car was last placed (cached: the car-following loop reads it 64x per car per step; iteration 11).</summary>
        public Vector3 Pos { get; private set; }
        /// <summary>Slowing down (signal, queue, turn): drives the brake lamps (iteration 7).</summary>
        public bool Braking { get; private set; }

        public void Begin(RoadPath r, float startS, int direction, float laneOffset, float cruiseSpeed, Color paint)
        {
            rb = GetComponent<Rigidbody>();
            road = r;
            roadIndex = IndexOf(r);
            turning = false;
            s = startS;
            dir = direction;
            offset = laneOffset;
            cruise = cruiseSpeed;
            speed = cruiseSpeed;
            Crashed = false;
            Expired = false;
            NearArmed = false;
            TimeSinceContact = 99f;
            rb.isKinematic = true;
            Place(out var pos, out var rot);
            rb.position = pos;
            rb.rotation = rot;
            transform.SetPositionAndRotation(pos, rot);
            Pos = pos;
            if (paintRenderers == null)
            {
                // Tint only paint materials (M_Paint_*, Traffic_Body*), per material slot: glass, chrome, lights,
                // tyres and textured liveries (taxi, police) keep their own colours.
                var list = new List<Renderer>();
                var slots = new List<int>();
                foreach (var rend in GetComponentsInChildren<Renderer>())
                {
                    var mats = rend.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] != null && (mats[i].name.StartsWith("M_Paint") || mats[i].name.StartsWith("Traffic_Body"))) { list.Add(rend); slots.Add(i); }
                }
                paintRenderers = list.ToArray();
                paintSlots = slots.ToArray();
                mpb = new MaterialPropertyBlock();
            }
            mpb.SetColor("_BaseColor", paint);
            for (int i = 0; i < paintRenderers.Length; i++) paintRenderers[i].SetPropertyBlock(mpb, paintSlots[i]);
        }

        void Place(out Vector3 pos, out Quaternion rot)
        {
            var path = road.Path;
            var p = path.PositionAt(s);
            var t = path.TangentAt(s) * dir;
            var right = new Vector3(t.z, 0f, -t.x).normalized;
            pos = p + right * offset + Vector3.up * 0.02f;
            Forward = new Vector3(t.x, t.y, t.z).normalized;
            rot = Quaternion.LookRotation(Forward, Vector3.up);
        }

        public void Step(float dt, List<TrafficCar> all, VehicleController player)
        {
            TimeSinceContact += dt;
            if (Crashed)
            {
                crashedTime += dt;
                if (crashedTime > 10f) Expired = true;
                return;
            }
            // Car following: slow for anything ahead in our lane.
            float gap = 60f;
            var me = Crashed ? transform.position : Pos;
            foreach (var o in all)
            {
                if (o == this) continue;
                var d = (o.Crashed ? o.transform.position : o.Pos) - me;
                float along = Vector3.Dot(d, Forward);
                if (along <= 0f || along > gap) continue;
                float lateral = Vector3.Dot(d, new Vector3(Forward.z, 0f, -Forward.x));
                if (Mathf.Abs(lateral) < 2f) gap = along;
            }
            if (player != null)
            {
                var d = player.transform.position - me;
                float along = Vector3.Dot(d, Forward);
                float lateral = Vector3.Dot(d, new Vector3(Forward.z, 0f, -Forward.x));
                if (along > 0f && along < gap && Mathf.Abs(lateral) < 2.2f) gap = along;
            }
            // Stop at a red signal (or amber, if there is room) at the end we are driving toward: queues at the lights.
            var lights = TrafficLights.Instance;
            if (!turning && lights != null && roadIndex >= 0 && !road.Path.Closed)
            {
                int end = dir > 0 ? 1 : 0;
                int state = lights.StateFor(roadIndex, end, out float stopAt);
                float toLine = (dir > 0 ? road.Path.Length - s : s) - stopAt;   // painted stop line, outside the crossing road
                if ((state == 0 || (state == 1 && toLine > 14f)) && toLine > -1f && toLine < 70f) gap = Mathf.Min(gap, toLine + 8f);
            }
            float desired = gap < 60f ? Mathf.Min(cruise, Mathf.Max(0f, (gap - 8f) * 0.6f)) : cruise;
            if (turning) desired = Mathf.Min(desired, turnSpeed);
            Braking = desired < speed - 0.4f || speed < 0.5f;
            speed = Mathf.MoveTowards(speed, desired, (desired < speed ? 7f : 2.5f) * dt);
            if (turning) { StepTurn(dt); return; }
            s += speed * dt * dir;
            var path = road.Path;
            if (!path.Closed && (s < 1f || s > path.Length - 1f))
            {
                s = Mathf.Clamp(s, 1f, path.Length - 1f);
                if (!BeginTurn()) { Expired = true; return; }
                StepTurn(dt);
                return;
            }
            s = path.Wrap(s);
            Place(out var pos, out var rot);
            rb.MovePosition(pos);
            Pos = pos;
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, rot, 0.5f));
        }

        static int IndexOf(RoadPath r)
        {
            var roads = WorldData.Roads;
            for (int i = 0; i < roads.Count; i++) if (roads[i] == r) return i;
            return -1;
        }

        /// <summary>Picks a connected road at the end of this one (no U-turns) and sets up the curve onto its lane.</summary>
        bool BeginTurn()
        {
            var path = road.Path;
            Vector3 d0 = path.TangentAt(dir > 0 ? path.Length - 2f : 2f) * dir;
            d0.y = 0f;
            if (d0.sqrMagnitude < 1e-4f) return false;
            d0.Normalize();
            Vector3 endP = path.PositionAt(dir > 0 ? path.Length : 0f);
            WorldData.RoadsAt(endP + d0 * 6f, -7f, 3f, roadHits);
            int pick = -1, pickDir = 0, n = 0;
            float pickS = 0f;
            Vector3 pickD = d0;
            foreach (var h in roadHits)
            {
                if (h.Road == roadIndex) continue;
                var other = WorldData.Roads[h.Road];
                float len = other.Path.Length;
                for (int dd = -1; dd <= 1; dd += 2)
                {
                    float sIn = h.S + dd * 12f;
                    if (!other.Path.Closed && (sIn < 15f || sIn > len - 15f)) continue;   // room to drive on
                    sIn = other.Path.Wrap(sIn);
                    Vector3 d1 = other.Path.TangentAt(sIn) * dd;
                    d1.y = 0f;
                    if (d1.sqrMagnitude < 1e-4f) continue;
                    d1.Normalize();
                    if (Vector3.Dot(d0, d1) < -0.3f) continue;   // no U-turns
                    n++;
                    if (Random.Range(0, n) == 0) { pick = h.Road; pickDir = dd; pickS = sIn; pickD = d1; }
                }
            }
            if (pick < 0) return false;
            nextRoad = WorldData.Roads[pick];
            nextRoadIndex = pick;
            nextDir = pickDir;
            nextS = pickS;
            var right1 = new Vector3(pickD.z, 0f, -pickD.x);
            tP0 = transform.position;
            tP3 = nextRoad.Path.PositionAt(nextS) + right1 * offset + Vector3.up * 0.02f;
            float L = Vector3.Distance(tP0, tP3);
            tP1 = tP0 + d0 * (L * 0.45f);
            tP2 = tP3 - pickD * (L * 0.45f);
            tLen = Mathf.Max(1f, L * 1.15f);
            tU = 0f;
            float sharp = 1f - Mathf.Clamp01(Vector3.Dot(d0, pickD));   // 0 straight on, 1 right angle
            turnSpeed = Mathf.Lerp(cruise, 7f, sharp);
            turning = true;
            return true;
        }

        void StepTurn(float dt)
        {
            tU += speed * dt / tLen;
            if (tU >= 1f)
            {
                road = nextRoad;
                roadIndex = nextRoadIndex;
                dir = nextDir;
                s = nextS;
                turning = false;
                Place(out var pos, out var rot);
                rb.MovePosition(pos);
                Pos = pos;
                rb.MoveRotation(rot);
                return;
            }
            float u = tU, w = 1f - u;
            Vector3 p = w * w * w * tP0 + 3f * w * w * u * tP1 + 3f * w * u * u * tP2 + u * u * u * tP3;
            Vector3 tan = 3f * w * w * (tP1 - tP0) + 6f * w * u * (tP2 - tP1) + 3f * u * u * (tP3 - tP2);
            if (tan.sqrMagnitude > 1e-4f) Forward = tan.normalized;
            rb.MovePosition(p);
            Pos = p;
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(Forward, Vector3.up), 0.5f));
        }

        void OnCollisionEnter(Collision c)
        {
            TimeSinceContact = 0f;
            if (Crashed) return;
            if (c.impulse.magnitude < 900f && c.rigidbody == null) return;
            Crashed = true;
            crashedTime = 0f;
            var v = Forward * speed;
            rb.isKinematic = false;
            rb.linearVelocity = v;
        }

        void OnCollisionStay(Collision c) => TimeSinceContact = 0f;
    }
}
