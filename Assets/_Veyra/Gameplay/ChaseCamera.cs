using UnityEngine;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Chase / close-chase / bonnet camera. Follows the interpolated body with no world-space lag,
    /// leans toward the direction of travel so drift angle is readable, widens FOV with speed.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(Camera))]
    public sealed class ChaseCamera : MonoBehaviour
    {
        public enum Mode { Chase, CloseChase, Bonnet }

        [SerializeField] VehicleController target;
        public Mode CurrentMode = Mode.Chase;
        public bool LookBack;
        public float BaseFov = 60f;
        [Tooltip("Extra FOV at the reference speed (comfort setting scales it)")] public float SpeedFov = 14f;
        public float FovSpeedRef = 70f;
        [Range(0f, 1f)] public float SpeedEffectsIntensity = 1f;

        Camera cam;
        Vector3 smoothedDir = Vector3.forward;
        Vector3 smoothedOffset;
        bool snapped;

        public VehicleController Target => target;

        void Awake() => cam = GetComponent<Camera>();

        public void SetTarget(VehicleController v)
        {
            target = v;
            snapped = false;
        }

        public void Cycle()
        {
            CurrentMode = (Mode)(((int)CurrentMode + 1) % 3);
            snapped = false;
        }

        void LateUpdate()
        {
            if (target == null || target.Body == null) return;
            float dt = Mathf.Max(1e-4f, Time.unscaledDeltaTime);
            Transform t = target.transform;
            Rigidbody rb = target.Body;
            Vector3 pos = t.position;
            Vector3 v = rb.linearVelocity;
            float speed = v.magnitude;

            if (CurrentMode == Mode.Bonnet)
            {
                transform.SetPositionAndRotation(t.TransformPoint(new Vector3(0f, 1.12f, 0.35f)),
                    LookBack ? t.rotation * Quaternion.Euler(0f, 180f, 0f) : t.rotation);
                ApplyFov(speed);
                return;
            }

            Vector3 fwdFlat = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            if (fwdFlat.sqrMagnitude < 1e-4f) fwdFlat = smoothedDir;
            fwdFlat.Normalize();
            Vector3 velFlat = Vector3.ProjectOnPlane(v, Vector3.up);
            Vector3 dir = fwdFlat;
            if (velFlat.magnitude > 3f && Vector3.Dot(velFlat, fwdFlat) > 0f)
                dir = Vector3.Slerp(fwdFlat, velFlat.normalized, 0.45f * Mathf.Clamp01((speed - 3f) / 15f));
            if (LookBack) dir = -dir;

            float distance = CurrentMode == Mode.Chase ? 6.2f : 4.6f;
            float height = CurrentMode == Mode.Chase ? 1.95f : 1.45f;
            distance += Mathf.Clamp01(speed / 80f) * 1.2f * SpeedEffectsIntensity;

            if (!snapped)
            {
                smoothedDir = dir;
                smoothedOffset = -dir * distance + Vector3.up * height;
                snapped = true;
            }
            smoothedDir = Vector3.Slerp(smoothedDir, dir, 1f - Mathf.Exp(-5f * dt)).normalized;
            Vector3 desiredOffset = -smoothedDir * distance + Vector3.up * height;
            smoothedOffset = Vector3.Lerp(smoothedOffset, desiredOffset, 1f - Mathf.Exp(-10f * dt));

            Vector3 camPos = pos + smoothedOffset;
            camPos.y = Mathf.Max(camPos.y, pos.y + 0.6f);
            Vector3 lookAt = pos + Vector3.up * 0.95f + smoothedDir * 2.2f;
            transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(lookAt - camPos, Vector3.up));
            ApplyFov(speed);
        }

        void ApplyFov(float speed)
        {
            if (cam == null) return;
            cam.fieldOfView = BaseFov + SpeedFov * SpeedEffectsIntensity * Mathf.Clamp01(speed / FovSpeedRef);
        }
    }
}
