using UnityEngine;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Visual-only body lean on top of the simulated suspension (GTA/Asphalt-style weight): the body meshes
    /// (Body_LOD*) roll outward in corners, dive under braking and squat under power, about the centre-of-mass
    /// height. Physics, colliders and wheels are untouched (the controller positions the wheels after this runs).
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class VehicleBodyLean : MonoBehaviour
    {
        [Tooltip("Degrees of roll per g of lateral acceleration")] public float RollPerG = 2.4f;
        [Tooltip("Degrees of pitch per g of longitudinal acceleration")] public float PitchPerG = 1.8f;
        public float MaxRoll = 4f, MaxPitch = 2.6f;
        [Tooltip("Extra roll toward the outside while drifting, degrees")] public float DriftRoll = 1.5f;
        [Tooltip("Response, 1/s")] public float Response = 7f;

        VehicleController car;
        Transform[] bodies;
        Quaternion[] baseRot;
        Vector3[] basePos;
        float roll, pitch, pivotY;

        public void Bind(VehicleController v)
        {
            car = v;
            var f = v.Feel;   // BUG-087: gains from the HandlingFeel asset when the car has one
            if (f != null) { RollPerG = f.LeanRollPerG; PitchPerG = f.LeanPitchPerG; MaxRoll = f.LeanMaxRoll; MaxPitch = f.LeanMaxPitch; DriftRoll = f.LeanDriftRoll; Response = f.LeanResponse; }
            var list = new System.Collections.Generic.List<Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Body_LOD")) list.Add(t);
            bodies = list.ToArray();
            baseRot = new Quaternion[bodies.Length];
            basePos = new Vector3[bodies.Length];
            for (int i = 0; i < bodies.Length; i++) { baseRot[i] = bodies[i].localRotation; basePos[i] = bodies[i].localPosition; }
            pivotY = v.Definition != null ? v.Definition.Chassis.CoMHeight : 0.5f;
        }

        void LateUpdate()
        {
            if (car == null || bodies == null || bodies.Length == 0 || !car.IsInitialised) return;
            var st = car.State;
            bool grounded = st.GroundedWheels >= 2;
            float targetRoll = grounded ? Mathf.Clamp(st.LateralG * RollPerG, -MaxRoll, MaxRoll) : 0f;
            if (st.DriftBlend > 0f) targetRoll += Mathf.Sign(st.SlipAngleDeg) * -DriftRoll * st.DriftBlend;
            float targetPitch = grounded ? Mathf.Clamp(-st.LongitudinalG * PitchPerG, -MaxPitch, MaxPitch) : 0f;
            float k = 1f - Mathf.Exp(-Response * Time.deltaTime);
            roll = Mathf.Lerp(roll, targetRoll, k);
            pitch = Mathf.Lerp(pitch, targetPitch, k);
            var q = Quaternion.Euler(pitch, 0f, roll);
            var pivot = new Vector3(0f, pivotY, 0f);
            for (int i = 0; i < bodies.Length; i++)
            {
                var b = bodies[i];
                if (b == null) continue;
                // rotate about the pivot in the model's space: p' = pivot + q (p - pivot)
                b.localRotation = q * baseRot[i];
                b.localPosition = pivot + q * (basePos[i] - pivot);
            }
        }
    }
}
