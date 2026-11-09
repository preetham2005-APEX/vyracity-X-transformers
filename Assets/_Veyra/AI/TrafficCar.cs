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

        public bool Crashed { get; private set; }
        public bool Expired { get; private set; }
        public bool NearArmed;
        public int Id;
        public float TimeSinceContact { get; private set; } = 99f;
        public Vector3 Forward { get; private set; }
        public float Speed => Crashed ? 0f : speed;

        public void Begin(RoadPath r, float startS, int direction, float laneOffset, float cruiseSpeed, Color paint)
        {
            rb = GetComponent<Rigidbody>();
            road = r;
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
            if (paintRenderers == null)
            {
                var list = new List<Renderer>();
                foreach (var rend in GetComponentsInChildren<Renderer>()) if (rend.name.Contains("Body")) list.Add(rend);
                paintRenderers = list.ToArray();
                mpb = new MaterialPropertyBlock();
            }
            mpb.SetColor("_BaseColor", paint);
            foreach (var rend in paintRenderers) rend.SetPropertyBlock(mpb);
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
            var me = transform.position;
            foreach (var o in all)
            {
                if (o == this) continue;
                var d = o.transform.position - me;
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
            float desired = gap < 60f ? Mathf.Min(cruise, Mathf.Max(0f, (gap - 8f) * 0.6f)) : cruise;
            speed = Mathf.MoveTowards(speed, desired, (desired < speed ? 7f : 2.5f) * dt);
            s += speed * dt * dir;
            var path = road.Path;
            if (!path.Closed && (s < 1f || s > path.Length - 1f)) { Expired = true; return; }
            s = path.Wrap(s);
            Place(out var pos, out var rot);
            rb.MovePosition(pos);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, rot, 0.5f));
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
