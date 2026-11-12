using System;
using UnityEngine;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Nitro
{
    /// <summary>
    /// Drift chains: points accumulate while the car slides (angle x speed x multiplier). Consecutive drifts
    /// within the grace time extend the chain and raise the multiplier; the chain banks when the grace
    /// time runs out. Wall contact fails the chain with no payout. Circling in a small area (more than 300 deg
    /// of heading change inside a 25 m radius) earns nothing, per NITRO_BALANCE_SPEC anti-farm rules.
    /// </summary>
    public sealed class DriftScorer : MonoBehaviour
    {
        public const float MinAngleDeg = 12f;
        public const float MinSpeedKmh = 30f;
        public float GraceSeconds = 1.3f;
        public float MaxMultiplier = 5f;
        [Tooltip("BUG-087: drift-scoring thresholds (the constants above when empty)")] public NitroTuning Tuning;
        float MinAngle => Tuning != null ? Tuning.DriftMinAngleDeg : MinAngleDeg;
        float MinSpeed => Tuning != null ? Tuning.DriftMinSpeedKmh : MinSpeedKmh;

        VehicleController vehicle;

        public bool Drifting { get; private set; }
        public bool ChainActive { get; private set; }
        public float ChainPoints { get; private set; }
        public float Multiplier { get; private set; } = 1f;
        public float CurrentAngle { get; private set; }
        public float TotalBanked { get; private set; }
        public float BestChain { get; private set; }
        public float LongestDriftSeconds { get; private set; }
        public bool Circling { get; private set; }
        /// <summary>Optional: velocity . route tangent (races); drifting backwards along the route scores nothing.</summary>
        public Func<float> RouteProgress;

        public event Action<float> ChainBanked;
        public event Action<float> ChainFailed;

        float grace;
        float sustained;
        float driftTime;
        Vector3 chainOrigin;
        float headingAccum;
        float lastYaw;

        public void Bind(VehicleController v)
        {
            if (vehicle != null) vehicle.Collided -= OnCollided;
            vehicle = v;
            if (vehicle != null) vehicle.Collided += OnCollided;
            ResetChain();
        }

        void OnDestroy()
        {
            if (vehicle != null) vehicle.Collided -= OnCollided;
        }

        public void ResetAll()
        {
            ResetChain();
            TotalBanked = 0f;
            BestChain = 0f;
            LongestDriftSeconds = 0f;
        }

        void ResetChain()
        {
            ChainActive = false;
            Drifting = false;
            ChainPoints = 0f;
            Multiplier = 1f;
            sustained = 0f;
            driftTime = 0f;
            headingAccum = 0f;
            Circling = false;
        }

        void OnCollided(VehicleController v, Collision c)
        {
            if (!ChainActive) return;
            // Hard wall/car contact ends the chain without payout. Light scrapes are forgiven.
            if (c.impulse.magnitude / Mathf.Max(1f, v.Body.mass) < 0.6f) return;
            float lost = ChainPoints;
            ResetChain();
            ChainFailed?.Invoke(lost);
        }

        void FixedUpdate()
        {
            if (vehicle == null || !vehicle.IsInitialised) return;
            float dt = Time.fixedDeltaTime;
            var st = vehicle.State;
            float angle = Mathf.Abs(st.SlipAngleDeg);
            CurrentAngle = angle;
            bool progressOk = RouteProgress == null || RouteProgress() > 0.3f;
            bool sliding = angle > MinAngle && angle < 120f && st.SpeedKmh > MinSpeed && st.GroundedWheels >= 2 && progressOk;

            float yaw = vehicle.transform.eulerAngles.y;
            if (sliding)
            {
                if (!ChainActive)
                {
                    ChainActive = true;
                    ChainPoints = 0f;
                    Multiplier = 1f;
                    chainOrigin = vehicle.transform.position;
                    headingAccum = 0f;
                    lastYaw = yaw;
                }
                Drifting = true;
                grace = GraceSeconds;
                sustained += dt;
                driftTime += dt;
                LongestDriftSeconds = Mathf.Max(LongestDriftSeconds, driftTime);
                headingAccum += Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw));
                lastYaw = yaw;
                Circling = headingAccum > 300f && (vehicle.transform.position - chainOrigin).magnitude < 25f;
                if (sustained > 2f && Multiplier < MaxMultiplier)
                {
                    Multiplier = Mathf.Min(MaxMultiplier, Multiplier + 1f);
                    sustained = 0f;
                }
                if (!Circling)
                {
                    float angleFactor = Mathf.Clamp(angle, MinAngle, Tuning != null ? Tuning.DriftMaxScoredAngleDeg : 60f);
                    ChainPoints += angleFactor * st.Speed * dt * 1.6f * Multiplier;
                }
            }
            else
            {
                Drifting = false;
                driftTime = 0f;
                if (ChainActive)
                {
                    lastYaw = yaw;
                    grace -= dt;
                    if (grace <= 0f)
                    {
                        float banked = Circling ? 0f : ChainPoints;
                        TotalBanked += banked;
                        BestChain = Mathf.Max(BestChain, banked);
                        ResetChain();
                        if (banked > 0f) ChainBanked?.Invoke(banked);
                    }
                }
            }
        }
    }
}
