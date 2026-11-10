using System;
using System.Collections.Generic;
using UnityEngine;
using VeyraCity.Assists;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Nitro
{
    public enum NitroMode { Off, Standard, CleanBoost, Overdrive }

    public enum ChargeSource { Drift, NearMiss, Airtime, CleanSection, Passive, Pickup }

    /// <summary>
    /// Bounded, meter-driven boost (NITRO_BALANCE_SPEC). Boost is extra engine torque through the drivetrain
    /// plus a longitudinal thrust that fades to zero at the mode's extended top-speed cap, so it can never
    /// run away. UI names are original: Standard, Clean Boost, Overdrive.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class NitroSystem : MonoBehaviour
    {
        public const float MeterMax = 100f;
        public const float RaceStartAmount = 33f;
        // Mode table (spec section 2)
        static readonly float[] TorqueMul = { 1f, 1.35f, 1.55f, 1.90f };
        static readonly float[] SpeedCap = { 0f, 0.05f, 0.07f, 0.10f };
        static readonly float[] ThrustAccel = { 0f, 2.4f, 3.1f, 4.4f }; // m/s^2 at standstill (x mass)
        public const float StandardDrain = 20f;
        public const float CleanWindowOpen = 0.60f, CleanWindowClose = 0.85f;
        public const float OverdriveTapWindow = 0.30f, OverdriveDuration = 2f, OverdriveCooldown = 1f;

        [Tooltip("Passive regeneration per second (1 in races, 0 in free roam)")] public float PassiveRegen;
        [Tooltip("Upgrade: multiplies all charge gains")] public float ChargeMultiplier = 1f;
        [Tooltip("Upgrade: multiplies boost torque gain")] public float PowerMultiplier = 1f;
        public bool InfiniteForTesting;

        VehicleController vehicle;
        VehicleDriver driver;
        float vmax = 60f;

        public float Amount { get; private set; }
        public NitroMode Mode { get; private set; }
        /// <summary>0..1 actual boost strength after ramps; VFX/audio/FOV follow this.</summary>
        public float BoostScalar { get; private set; }
        public bool CleanWindowOpenNow => Mode == NitroMode.Standard && sinceActivation >= CleanWindowOpen && sinceActivation <= CleanWindowClose;
        public float SinceActivation => sinceActivation;
        public float OverdriveCooldownLeft => cooldown;
        public float ExtraThrust { get; private set; }
        public float TotalUsed { get; private set; }
        public int CleanBoostHits { get; private set; }
        public event Action<NitroMode> ModeStarted;
        public event Action<ChargeSource, float> Charged;

        bool prevPressed;
        float pressTime = -10f, lastTapTime = -10f;
        bool tapStartedFull;
        float sinceActivation = 99f;
        float overdriveLeft;
        float cooldown;
        float activationDelay;
        bool cleanUsedThisActivation;
        // charge bookkeeping
        float driftChainCharge;
        float airTime, airChargeThisJump;
        float cleanTimer;
        readonly Dictionary<int, float> nearMissCooldown = new Dictionary<int, float>();
        readonly Queue<float> nearMissTimes = new Queue<float>();
        DriftScorer drift;

        public void Bind(VehicleController v, VehicleDriver d, DriftScorer scorer)
        {
            if (driver != null) driver.PostProcess -= Filter;
            if (vehicle != null) vehicle.Collided -= OnCollided;
            if (drift != null) { drift.ChainBanked -= OnChainEnd; drift.ChainFailed -= OnChainEnd; }
            vehicle = v;
            driver = d;
            drift = scorer;
            if (driver != null) driver.PostProcess += Filter;
            if (vehicle != null) vehicle.Collided += OnCollided;
            if (drift != null) { drift.ChainBanked += OnChainEnd; drift.ChainFailed += OnChainEnd; }
            if (v == null) return;
            vmax = EstimateTopSpeed(v.Definition);
            ResetState(Amount);
        }

        void OnDestroy() => Bind(null, null, null);

        public void ResetState(float amount)
        {
            Amount = Mathf.Clamp(amount, 0f, MeterMax);
            Mode = NitroMode.Off;
            BoostScalar = 0f;
            overdriveLeft = 0f;
            cooldown = 0f;
            sinceActivation = 99f;
            driftChainCharge = 0f;
            airTime = 0f;
            cleanTimer = 0f;
            TotalUsed = 0f;
            CleanBoostHits = 0;
        }

        /// <summary>Top speed estimate from peak power and drag (used for the bounded thrust cap).</summary>
        public static float EstimateTopSpeed(VehicleDefinition def)
        {
            if (def == null) return 60f;
            float peakW = 0f;
            if (def.Engine.TorqueCurve != null)
                foreach (var k in def.Engine.TorqueCurve) peakW = Mathf.Max(peakW, k.y * k.x * Mathf.PI / 30f);
            peakW *= def.Gearbox.Efficiency > 0f ? def.Gearbox.Efficiency : 0.85f;
            float cda = Mathf.Max(0.2f, def.Aero.DragArea);
            return Mathf.Clamp(Mathf.Pow(2f * peakW / (1.225f * cda), 1f / 3f) * 0.93f, 30f, 120f);
        }

        public void Add(float units, ChargeSource source)
        {
            if (units <= 0f) return;
            float before = Amount;
            Amount = Mathf.Min(MeterMax, Amount + units * ChargeMultiplier);
            if (Amount > before) Charged?.Invoke(source, Amount - before);
        }

        /// <summary>A near miss with another vehicle (identified by instance id). Spec: +5, 3 s per vehicle, max 3 per 10 s, > 60 km/h.</summary>
        public void ReportNearMiss(int otherId)
        {
            if (vehicle == null || vehicle.State.SpeedKmh < 60f) return;
            float now = Time.time;
            if (nearMissCooldown.TryGetValue(otherId, out float until) && now < until) return;
            while (nearMissTimes.Count > 0 && now - nearMissTimes.Peek() > 10f) nearMissTimes.Dequeue();
            if (nearMissTimes.Count >= 3) return;
            nearMissTimes.Enqueue(now);
            nearMissCooldown[otherId] = now + 3f;
            Add(5f, ChargeSource.NearMiss);
        }

        void OnCollided(VehicleController v, Collision c)
        {
            cleanTimer = 0f;
        }

        void OnChainEnd(float points) => driftChainCharge = 0f;

        void FixedUpdate()
        {
            if (vehicle == null || !vehicle.IsInitialised) return;
            float dt = Time.fixedDeltaTime;
            var st = vehicle.State;
            if (InfiniteForTesting) Amount = MeterMax;

            // Passive regen (races only).
            if (PassiveRegen > 0f && Mode == NitroMode.Off) Add(PassiveRegen * dt, ChargeSource.Passive);

            // Drift charge: 6/s x angle factor x speed factor, <= 40 per chain.
            if (drift != null && drift.Drifting && !drift.Circling)
            {
                float angleF = Mathf.InverseLerp(15f, 35f, drift.CurrentAngle);
                float speedF = st.SpeedKmh > 40f ? 1f : 0f;
                float gain = 6f * angleF * speedF * dt;
                gain = Mathf.Min(gain, 40f - driftChainCharge);
                if (gain > 0f)
                {
                    driftChainCharge += gain;
                    Add(gain, ChargeSource.Drift);
                }
            }

            // Airtime: +4 per 0.5 s above 0.4 s, <= 12 per jump, paid on landing on wheels.
            if (st.GroundedWheels == 0 && st.Speed > 5f) airTime += dt;
            else
            {
                if (airTime > 0.4f && st.GroundedWheels >= 3)
                {
                    float units = Mathf.Min(12f, Mathf.Floor((airTime - 0.4f) / 0.5f + 1f) * 4f);
                    Add(units, ChargeSource.Airtime);
                }
                airTime = 0f;
            }

            // Clean section: +3 per 10 s above 80 km/h without collision.
            if (st.SpeedKmh > 80f)
            {
                cleanTimer += dt;
                if (cleanTimer >= 10f)
                {
                    cleanTimer -= 10f;
                    Add(3f, ChargeSource.CleanSection);
                }
            }
        }

        void Filter(ref VehicleControls c, in DriverIntent intent, float dt)
        {
            bool pressed = intent.Nitro;
            bool edge = pressed && !prevPressed;
            prevPressed = pressed;
            float now = Time.time;
            if (cooldown > 0f) cooldown -= dt;
            sinceActivation += dt;

            if (edge)
            {
                bool doubleTap = now - lastTapTime <= OverdriveTapWindow;
                if (doubleTap && tapStartedFull && cooldown <= 0f && Mode != NitroMode.Overdrive)
                {
                    StartMode(NitroMode.Overdrive);
                    overdriveLeft = OverdriveDuration;
                }
                else if (Mode == NitroMode.Standard && !cleanUsedThisActivation && sinceActivation >= CleanWindowOpen && sinceActivation <= CleanWindowClose)
                {
                    cleanUsedThisActivation = true;
                    CleanBoostHits++;
                    StartMode(NitroMode.CleanBoost);
                }
                else if (Mode == NitroMode.Off && Amount > 1f && cooldown <= 0f)
                {
                    activationDelay = 0.05f;
                    cleanUsedThisActivation = false;
                    StartMode(NitroMode.Standard);
                }
                tapStartedFull = Amount >= MeterMax - 0.01f || (doubleTap && tapStartedFull);
                lastTapTime = now;
                pressTime = now;
            }

            // Mode upkeep and consumption.
            switch (Mode)
            {
                case NitroMode.Standard:
                case NitroMode.CleanBoost:
                    // Clean Boost window needs the button released and pressed again, so a released button
                    // inside the window keeps Standard alive until the window closes.
                    bool keepAlive = pressed || (Mode == NitroMode.Standard && !cleanUsedThisActivation && sinceActivation < CleanWindowClose);
                    float drain = StandardDrain * (Mode == NitroMode.CleanBoost ? 0.85f : 1f);
                    if (pressed) Consume(drain * dt);
                    if (!keepAlive || Amount <= 0f) Mode = NitroMode.Off;
                    break;
                case NitroMode.Overdrive:
                    Consume(MeterMax / OverdriveDuration * dt);
                    overdriveLeft -= dt;
                    if (overdriveLeft <= 0f || Amount <= 0f)
                    {
                        Mode = NitroMode.Off;
                        cooldown = OverdriveCooldown;
                    }
                    break;
            }

            // Ramps: delay 0.05 s, in 0.15 s, out 0.20 s.
            bool active = Mode != NitroMode.Off && (pressed || Mode == NitroMode.Overdrive);
            if (activationDelay > 0f) { activationDelay -= dt; active = false; }
            BoostScalar = active ? Mathf.MoveTowards(BoostScalar, 1f, dt / 0.15f) : Mathf.MoveTowards(BoostScalar, 0f, dt / 0.20f);
            if (BoostScalar <= 0f) { ExtraThrust = 0f; return; }
            // BUG-044: braking or reversing always wins over nitro (no forced throttle, no thrust).
            if (c.WantsReverse || c.Brake > 0.2f) { ExtraThrust = 0f; return; }

            int m = (int)(Mode == NitroMode.Off ? lastMode : Mode);
            float mul = Mathf.Min(1f + (TorqueMul[m] - 1f) * PowerMultiplier, 2.5f);   // BUG-084: cap the boost torque
            c.Throttle = Mathf.Max(c.Throttle, BoostScalar);
            c.TorqueScale = (c.TorqueScale > 0f ? c.TorqueScale : 1f) * Mathf.Lerp(1f, mul, BoostScalar);
            float v = Mathf.Max(0f, vehicle.State.ForwardSpeed);
            float capV = vmax * (1f + SpeedCap[m]);
            float thrust = ThrustAccel[m] * vehicle.Body.mass * Mathf.Max(0f, 1f - v / capV) * BoostScalar;
            ExtraThrust = thrust;
            c.ExtraThrust += thrust;
        }

        NitroMode lastMode = NitroMode.Standard;

        void StartMode(NitroMode m)
        {
            Mode = m;
            lastMode = m;
            if (m == NitroMode.Standard) sinceActivation = 0f;
            ModeStarted?.Invoke(m);
        }

        void Consume(float units)
        {
            float used = Mathf.Min(Amount, units);
            Amount -= used;
            TotalUsed += used;
        }
    }
}
