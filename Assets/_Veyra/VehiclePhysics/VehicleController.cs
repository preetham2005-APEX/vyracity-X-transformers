using System;
using System.Collections.Generic;
using UnityEngine;

namespace VeyraCity.VehiclePhysics
{
    /// <summary>
    /// Custom raycast vehicle (VEHICLE_HANDLING_SPEC candidate A): per-wheel raycast suspension with
    /// bump/rebound damping, bump stops and anti-roll bars; combined-slip tyres with load sensitivity;
    /// engine, clutch, gearbox and differentials; brakes, handbrake and ABS; aero drag and downforce.
    /// Everything is applied to one Rigidbody. Tyre and wheel-speed integration is sub-stepped and
    /// implicit in the longitudinal stiffness, so it stays stable at 50–120 Hz.
    /// Never references Input or UI. Tuning lives in <see cref="VehicleDefinition"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DefaultExecutionOrder(0)]
    public sealed class VehicleController : MonoBehaviour
    {
        public const string PhysicsVersion = "0.2.0";
        public const int WheelCount = 4;
        public const int FL = 0, FR = 1, RL = 2, RR = 3;
        public static readonly string[] WheelNames = { "FL", "FR", "RL", "RR" };

        /// <summary>Below this speed, slip uses this denominator so the forces stay finite (low-speed blending).</summary>
        const float LowSpeed = 3f;
        const float AirDensity = 1.225f;

        [SerializeField] VehicleDefinition definition;
        [SerializeField] Transform[] wheelVisuals = new Transform[WheelCount];
        [SerializeField] Transform[] caliperVisuals = new Transform[WheelCount];
        [SerializeField] SurfaceDefinition defaultSurface;
        [SerializeField, Range(1, 16)] int tyreSubsteps = 6;
        [SerializeField] bool manualStepping;

        Rigidbody rb;
        Powertrain powertrain;
        VehicleControls controls = VehicleControls.Neutral;
        VehicleState state;
        readonly WheelState[] wheels = new WheelState[WheelCount];
        readonly Vector3[] mountLocal = new Vector3[WheelCount];
        readonly float[] prevLength = new float[WheelCount];
        readonly float[] absScale = { 1f, 1f, 1f, 1f };
        readonly Quaternion[] wheelBaseRot = new Quaternion[WheelCount];
        readonly Quaternion[] caliperBaseRot = new Quaternion[WheelCount];
        readonly Vector3[] contactForward = new Vector3[WheelCount];
        readonly Vector3[] contactRight = new Vector3[WheelCount];
        readonly float[] vx = new float[WheelCount];
        readonly float[] vy = new float[WheelCount];
        readonly float[] brakeTorque = new float[WheelCount];
        readonly float[] driveTorque = new float[WheelCount];
        readonly float[] fxSum = new float[WheelCount];
        readonly float[] fySum = new float[WheelCount];
        readonly RaycastHit[] hitBuffer = new RaycastHit[8];
        readonly Dictionary<Collider, SurfaceDefinition> surfaceCache = new Dictionary<Collider, SurfaceDefinition>();
        float steerAngle;
        float tcsCut;
        float tcsTrim = 1f;
        Vector3 prevVelocity;
        float lateralG, longitudinalG;
        bool initialised;

        public VehicleDefinition Definition => definition;
        public Rigidbody Body => rb;
        public VehicleState State => state;
        public VehicleControls Controls => controls;
        public bool IsInitialised => initialised;
        public SurfaceDefinition DefaultSurface { get => defaultSurface; set => defaultSurface = value; }
        public bool ManualStepping { get => manualStepping; set => manualStepping = value; }
        public int TyreSubsteps { get => tyreSubsteps; set => tyreSubsteps = Mathf.Clamp(value, 1, 16); }
        public WheelState GetWheel(int i) => wheels[i];
        /// <summary>Raised after every simulation step (telemetry, effects).</summary>
        public event Action<VehicleController> Stepped;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            if (definition != null && !initialised) Initialize(definition);
        }

        public void SetVisuals(Transform[] wheelsVisual, Transform[] calipers)
        {
            for (int i = 0; i < WheelCount; i++)
            {
                wheelVisuals[i] = wheelsVisual != null && i < wheelsVisual.Length ? wheelsVisual[i] : null;
                caliperVisuals[i] = calipers != null && i < calipers.Length ? calipers[i] : null;
            }
        }

        public void Initialize(VehicleDefinition def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            definition = def;
            if (rb == null) rb = GetComponent<Rigidbody>();
            var c = def.Chassis;
            rb.mass = Mathf.Max(50f, c.Mass);
            rb.linearDamping = 0f;
            rb.angularDamping = 0.05f;
            rb.maxAngularVelocity = 30f;

            float halfBase = def.Wheelbase * 0.5f;
            var hubs = new Vector3[WheelCount];
            for (int i = 0; i < WheelCount; i++)
            {
                var axle = Axle(i);
                bool left = i % 2 == 0;
                if (wheelVisuals[i] != null)
                {
                    hubs[i] = transform.InverseTransformPoint(wheelVisuals[i].position);
                    wheelBaseRot[i] = Quaternion.Inverse(transform.rotation) * wheelVisuals[i].rotation;
                }
                else
                {
                    hubs[i] = new Vector3((left ? -0.5f : 0.5f) * axle.TrackWidth, axle.Tyre.Radius, i < 2 ? halfBase : -halfBase);
                    wheelBaseRot[i] = Quaternion.identity;
                }
                if (caliperVisuals[i] != null)
                    caliperBaseRot[i] = Quaternion.Inverse(transform.rotation) * caliperVisuals[i].rotation;
            }

            float zFront = (hubs[FL].z + hubs[FR].z) * 0.5f;
            float zRear = (hubs[RL].z + hubs[RR].z) * 0.5f;
            rb.centerOfMass = new Vector3(0f, c.CoMHeight, Mathf.Lerp(zRear, zFront, Mathf.Clamp01(c.FrontWeightFraction)));
            Vector3 d = c.Dimensions;
            float scale = c.InertiaScale > 0f ? c.InertiaScale : 1f;
            rb.inertiaTensor = scale * rb.mass / 12f * new Vector3(d.y * d.y + d.z * d.z, d.x * d.x + d.z * d.z, d.x * d.x + d.y * d.y);
            rb.inertiaTensorRotation = Quaternion.identity;

            float g = Physics.gravity.magnitude;
            for (int i = 0; i < WheelCount; i++)
            {
                var sus = Axle(i).Suspension;
                float share = i < 2 ? c.FrontWeightFraction : 1f - c.FrontWeightFraction;
                float staticLoad = rb.mass * g * share * 0.5f;
                float staticLength = Mathf.Clamp(sus.RestLength - staticLoad / Mathf.Max(1f, sus.SpringRate),
                                                 sus.RestLength - sus.Travel, sus.RestLength);
                mountLocal[i] = hubs[i] + Vector3.up * staticLength;
                prevLength[i] = staticLength;
                wheels[i] = default;
                wheels[i].SuspensionLength = staticLength;
                absScale[i] = 1f;
            }

            powertrain = new Powertrain(def.Engine, def.Gearbox);
            steerAngle = 0f;
            tcsCut = 0f;
            controls = VehicleControls.Neutral;
            prevVelocity = rb.linearVelocity;
            state = default;
            state.Gear = powertrain.Gear;
            initialised = true;
        }

        public void SetControls(in VehicleControls c) => controls = c;

        AxleSpec Axle(int wheel) => wheel < 2 ? definition.Front : definition.Rear;

        bool IsDriven(int wheel)
        {
            switch (definition.Layout)
            {
                case DriveLayout.FWD: return wheel < 2;
                case DriveLayout.RWD: return wheel >= 2;
                default: return true;
            }
        }

        void FixedUpdate()
        {
            if (!manualStepping && initialised) Simulate(Time.fixedDeltaTime);
        }

        /// <summary>One physics step. Called from FixedUpdate, or by a test harness in script-simulation mode.</summary>
        public void Simulate(float dt)
        {
            if (!initialised || dt <= 0f) return;
            var def = definition;
            Transform t = transform;
            Vector3 up = t.up, fwd = t.forward;
            float g = Physics.gravity.magnitude;
            int n = Mathf.Max(1, tyreSubsteps);
            float h = dt / n;

            // ---- Steering (physical rack rate limit + Ackermann) ----
            float maxSteer = Mathf.Max(1f, def.Steering.MaxAngleDeg);
            float targetSteer = Mathf.Clamp(controls.Steer, -1f, 1f) * maxSteer;
            steerAngle = Mathf.MoveTowards(steerAngle, targetSteer, Mathf.Max(30f, def.Steering.RateDegPerSec) * dt);
            Ackermann(steerAngle, out float steerL, out float steerR);
            wheels[FL].SteerAngleDeg = steerL;
            wheels[FR].SteerAngleDeg = steerR;
            wheels[RL].SteerAngleDeg = 0f;
            wheels[RR].SteerAngleDeg = 0f;

            // ---- Suspension ----
            int grounded = 0;
            for (int i = 0; i < WheelCount; i++)
            {
                ref WheelState w = ref wheels[i];
                var sus = Axle(i).Suspension;
                float r = Axle(i).Tyre.Radius;
                Vector3 mount = t.TransformPoint(mountLocal[i]);
                float length;
                if (CastWheel(mount, -up, sus.RestLength + r, out RaycastHit hit))
                {
                    length = Mathf.Max(0f, hit.distance - r);
                    w.Grounded = true;
                    w.ContactPoint = hit.point;
                    w.ContactNormal = hit.normal;
                    w.Surface = ResolveSurface(hit.collider);
                    grounded++;
                }
                else
                {
                    length = sus.RestLength;
                    w.Grounded = false;
                    w.Surface = null;
                }

                float compression = sus.RestLength - length;
                float compressionVelocity = (prevLength[i] - length) / dt;
                prevLength[i] = length;
                w.SuspensionLength = length;
                w.Compression = compression;

                if (w.Grounded)
                {
                    float spring = sus.SpringRate * compression;
                    float damper = compressionVelocity > 0f ? sus.BumpDamping * compressionVelocity : sus.ReboundDamping * compressionVelocity;
                    float minLength = sus.RestLength - sus.Travel;
                    float bumpStop = length < minLength ? sus.BumpStopRate * (minLength - length) : 0f;
                    w.Load = spring + damper + bumpStop;
                }
                else w.Load = 0f;
            }

            // Anti-roll bars.
            for (int a = 0; a < 2; a++)
            {
                int l = a * 2, rgt = l + 1;
                float arb = (a == 0 ? def.Front : def.Rear).Suspension.AntiRollRate;
                float f = arb * (wheels[l].Compression - wheels[rgt].Compression);
                if (wheels[l].Grounded) wheels[l].Load += f;
                if (wheels[rgt].Grounded) wheels[rgt].Load -= f;
            }

            float maxLoad = rb.mass * g * 3f;
            for (int i = 0; i < WheelCount; i++)
            {
                ref WheelState w = ref wheels[i];
                w.Load = w.Grounded ? Mathf.Clamp(w.Load, 0f, maxLoad) : 0f;
                if (w.Load > 0f)
                {
                    Vector3 hubWorld = t.TransformPoint(mountLocal[i]) - up * w.SuspensionLength;
                    rb.AddForceAtPosition(up * w.Load, hubWorld);
                }
            }

            // ---- Contact frames and patch velocities (body velocity at the start of the step) ----
            for (int i = 0; i < WheelCount; i++)
            {
                ref WheelState w = ref wheels[i];
                if (!w.Grounded) { vx[i] = vy[i] = 0f; continue; }
                Vector3 wheelFwd = Quaternion.AngleAxis(w.SteerAngleDeg, up) * fwd;
                Vector3 nrm = w.ContactNormal;
                Vector3 cf = wheelFwd - nrm * Vector3.Dot(wheelFwd, nrm);
                cf = cf.sqrMagnitude > 1e-8f ? cf.normalized : fwd;
                Vector3 cr = Vector3.Cross(nrm, cf);
                contactForward[i] = cf;
                contactRight[i] = cr;
                Vector3 pv = rb.GetPointVelocity(w.ContactPoint);
                vx[i] = Vector3.Dot(pv, cf);
                vy[i] = Vector3.Dot(pv, cr);
            }

            // ---- Powertrain ----
            int drivenCount = 0;
            float drivenOmega = 0f, maxDrivenSlip = 0f;
            for (int i = 0; i < WheelCount; i++)
            {
                if (!IsDriven(i)) continue;
                drivenCount++;
                drivenOmega += wheels[i].AngularVelocity;
                if (wheels[i].Grounded) maxDrivenSlip = Mathf.Max(maxDrivenSlip, Mathf.Abs(wheels[i].SlipRatio));
            }
            drivenOmega /= Mathf.Max(1, drivenCount);

            float throttle = Mathf.Clamp01(controls.Throttle);
            float torqueScale = controls.TorqueScale > 0f ? controls.TorqueScale : 1f;
            bool declutch = controls.Brake > 0.3f && throttle < 0.05f;
            float axleTorqueTotal = powertrain.Step(dt, throttle, torqueScale, drivenOmega, controls.WantsReverse,
                                                    def.Gearbox.Automatic ? 0 : controls.GearRequest, out float reflectedInertia, declutch);

            // Traction control: feed-forward limit of drive torque to what the driven tyres can transmit
            // (peak friction plus the torque needed to spin up the wheels and engine), trimmed by measured slip.
            if (controls.TcsPeakSlipFactor > 0f && axleTorqueTotal > 0f && grounded > 0)
            {
                var drivenTyre = def.Layout == DriveLayout.FWD ? def.Front.Tyre : def.Rear.Tyre;
                float target = Mathf.Max(0.01f, drivenTyre.LongPeakSlip) * controls.TcsPeakSlipFactor;
                tcsTrim = maxDrivenSlip > target ? Mathf.Max(0.25f, tcsTrim - 4f * dt) : Mathf.Min(1.25f, tcsTrim + 1.2f * dt);
                float cap = TractionCapacity(reflectedInertia, drivenCount, Mathf.Max(0f, longitudinalG) * g) * tcsTrim;
                if (axleTorqueTotal > cap) { tcsCut = 1f - cap / axleTorqueTotal; axleTorqueTotal = cap; }
                else tcsCut = 0f;
            }
            else { tcsCut = 0f; tcsTrim = 1f; }
            throttle *= 1f - tcsCut;
            float frontAxleTorque, rearAxleTorque;
            switch (def.Layout)
            {
                case DriveLayout.FWD: frontAxleTorque = axleTorqueTotal; rearAxleTorque = 0f; break;
                case DriveLayout.RWD: frontAxleTorque = 0f; rearAxleTorque = axleTorqueTotal; break;
                default:
                    frontAxleTorque = axleTorqueTotal * def.AwdFrontSplit;
                    rearAxleTorque = axleTorqueTotal * (1f - def.AwdFrontSplit);
                    break;
            }
            float reflectedPerWheel = reflectedInertia / Mathf.Max(1, drivenCount);

            // ---- Brakes (ABS caps the service brake just below the tyre's peak torque) ----
            float brake = Mathf.Clamp01(controls.Brake);
            float handbrake = Mathf.Clamp01(controls.Handbrake) * def.Brakes.HandbrakeTorque;
            bool absActive = false;
            for (int i = 0; i < WheelCount; i++)
            {
                bool front = i < 2;
                float share = front ? def.Brakes.FrontBias : 1f - def.Brakes.FrontBias;
                float service = def.Brakes.MaxTorque * share * 0.5f * brake;
                ref WheelState w = ref wheels[i];
                if (controls.AbsEnabled && service > 0f && w.Grounded)
                {
                    var tyre = Axle(i).Tyre;
                    float peakSlip = Mathf.Max(0.01f, tyre.LongPeakSlip);
                    absScale[i] = w.SlipRatio < -peakSlip
                        ? Mathf.Max(0.15f, absScale[i] - 14f * dt)
                        : Mathf.Min(1f, absScale[i] + 6f * dt);
                    float grip = SurfaceGrip(w.Surface);
                    // Target the tyre's peak force, plus the torque that decelerating the wheel itself absorbs.
                    float cap = (0.995f * TyreModel.LoadedMu(tyre, w.Load, grip) * w.Load * tyre.Radius
                                 + tyre.WheelInertia * Mathf.Abs(longitudinalG * g) / tyre.Radius) * absScale[i];
                    if (service > cap) { service = cap; absActive = true; }
                }
                else absScale[i] = 1f;
                brakeTorque[i] = service + (front ? 0f : handbrake);
                w.BrakeTorque = brakeTorque[i];
            }

            // ---- Tyres and wheel spin (sub-stepped) ----
            for (int i = 0; i < WheelCount; i++) { fxSum[i] = 0f; fySum[i] = 0f; }
            for (int s = 0; s < n; s++)
            {
                for (int a = 0; a < 2; a++)
                {
                    int l = a * 2, rgt = l + 1;
                    float axleT = a == 0 ? frontAxleTorque : rearAxleTorque;
                    if (!IsDriven(l)) { driveTorque[l] = driveTorque[rgt] = 0f; continue; }
                    var diff = (a == 0 ? def.Front : def.Rear).Differential;
                    float lockTorque = diff.Type == DiffType.Locked ? 1e6f
                        : diff.Type == DiffType.LimitedSlip ? diff.Preload + (axleT >= 0f ? diff.PowerLock : diff.CoastLock) * Mathf.Abs(axleT)
                        : 0f;
                    float delta = 0f;
                    if (lockTorque > 0f)
                    {
                        float inertia = Axle(l).Tyre.WheelInertia + reflectedPerWheel;
                        float equalise = (wheels[l].AngularVelocity - wheels[rgt].AngularVelocity) * inertia / (2f * h);
                        delta = Mathf.Clamp(0.5f * equalise, -lockTorque, lockTorque);
                    }
                    driveTorque[l] = axleT * 0.5f - delta;
                    driveTorque[rgt] = axleT * 0.5f + delta;
                }

                for (int i = 0; i < WheelCount; i++)
                {
                    ref WheelState w = ref wheels[i];
                    var tyre = Axle(i).Tyre;
                    float r = tyre.Radius;
                    float inertia = tyre.WheelInertia + (IsDriven(i) ? reflectedPerWheel : 0f);
                    float omega = w.AngularVelocity;
                    float driveT = driveTorque[i];

                    if (w.Grounded && w.Load > 0f)
                    {
                        float grip = SurfaceGrip(w.Surface);
                        float vDen = Mathf.Max(Mathf.Abs(vx[i]), LowSpeed);
                        float alpha = Mathf.Atan2(vy[i], vDen);
                        float kappa = (omega * r - vx[i]) / vDen;
                        TyreForces f = TyreModel.Evaluate(tyre, kappa, alpha, w.Load, grip);
                        float stiffness = TyreModel.LongitudinalStiffness(tyre, w.Load, grip);
                        float denom = 1f + h * stiffness * r * r / (inertia * vDen);
                        float newOmega = omega + h * (driveT - f.Fx * r) / (inertia * denom);
                        newOmega = ApplyBrake(newOmega, h * brakeTorque[i] / (inertia * denom));

                        kappa = (newOmega * r - vx[i]) / vDen;
                        f = TyreModel.Evaluate(tyre, kappa, alpha, w.Load, grip);
                        fxSum[i] += f.Fx;
                        fySum[i] += f.Fy;
                        w.AngularVelocity = newOmega;
                        w.SlipRatio = kappa;
                        w.SlipAngleDeg = alpha * Mathf.Rad2Deg;
                        w.CombinedSlip = f.CombinedSlip;
                    }
                    else
                    {
                        float newOmega = omega + h * driveT / inertia;
                        newOmega = ApplyBrake(newOmega, h * brakeTorque[i] / inertia);
                        newOmega *= 1f - 0.2f * h;
                        w.AngularVelocity = newOmega;
                        w.SlipRatio = 0f;
                        w.SlipAngleDeg = 0f;
                        w.CombinedSlip = 0f;
                    }
                    w.DriveTorque = driveT;
                }
            }

            // ---- Apply tyre forces ----
            for (int i = 0; i < WheelCount; i++)
            {
                ref WheelState w = ref wheels[i];
                w.SpinAngleDeg = Mathf.Repeat(w.SpinAngleDeg + w.AngularVelocity * dt * Mathf.Rad2Deg, 360f);
                if (!w.Grounded) { w.Fx = w.Fy = 0f; continue; }
                var axle = Axle(i);
                float rrMul = w.Surface != null ? w.Surface.RollingResistanceMultiplier : 1f;
                float rolling = -axle.Tyre.RollingResistance * rrMul * w.Load * Mathf.Clamp(vx[i] / 0.5f, -1f, 1f);
                w.Fx = fxSum[i] / n;
                w.Fy = fySum[i] / n;
                Vector3 force = contactForward[i] * (w.Fx + rolling) + contactRight[i] * w.Fy;
                Vector3 point = w.ContactPoint + up * axle.Suspension.RollCentreHeight;
                rb.AddForceAtPosition(force, point);
            }

            // ---- Aero and extra thrust ----
            Vector3 v = rb.linearVelocity;
            float speed = v.magnitude;
            if (speed > 0.1f)
            {
                rb.AddForce(-0.5f * AirDensity * def.Aero.DragArea * speed * v);
                float vf = Vector3.Dot(v, fwd);
                float downforce = 0.5f * AirDensity * def.Aero.DownforceArea * vf * vf;
                if (Mathf.Abs(downforce) > 0.01f)
                {
                    Vector3 frontAxle = t.TransformPoint((mountLocal[FL] + mountLocal[FR]) * 0.5f);
                    Vector3 rearAxle = t.TransformPoint((mountLocal[RL] + mountLocal[RR]) * 0.5f);
                    float share = Mathf.Clamp01(def.Aero.FrontDownforceShare);
                    rb.AddForceAtPosition(-up * downforce * share, frontAxle);
                    rb.AddForceAtPosition(-up * downforce * (1f - share), rearAxle);
                }
            }
            if (controls.ExtraThrust > 0f && grounded > 0) rb.AddForce(fwd * controls.ExtraThrust);

            // ---- State ----
            Vector3 accel = (v - prevVelocity) / dt;
            prevVelocity = v;
            Vector3 localV = t.InverseTransformDirection(v);
            longitudinalG = Mathf.Lerp(longitudinalG, Vector3.Dot(accel, fwd) / g, 0.3f);
            lateralG = Mathf.Lerp(lateralG, Vector3.Dot(accel, t.right) / g, 0.3f);
            state.Speed = speed;
            state.ForwardSpeed = localV.z;
            state.LateralSpeed = localV.x;
            state.YawRate = Vector3.Dot(rb.angularVelocity, up);
            state.SlipAngleDeg = speed > 2f ? Mathf.Atan2(localV.x, Mathf.Abs(localV.z)) * Mathf.Rad2Deg : 0f;
            Vector3 frontLocalV = t.InverseTransformDirection(rb.GetPointVelocity(t.TransformPoint((mountLocal[FL] + mountLocal[FR]) * 0.5f)));
            state.FrontSlipAngleDeg = speed > 2f ? Mathf.Atan2(frontLocalV.x, Mathf.Abs(frontLocalV.z)) * Mathf.Rad2Deg : 0f;
            state.MaxSteerDeg = def.Steering.MaxAngleDeg;
            state.Gear = powertrain.Gear;
            state.EngineRpm = powertrain.EngineRpm;
            state.EngineTorque = powertrain.EngineTorque;
            state.Shifting = powertrain.Shifting;
            state.AtLimiter = powertrain.AtLimiter;
            state.LongitudinalG = longitudinalG;
            state.LateralG = lateralG;
            state.GroundedWheels = grounded;
            state.AppliedThrottle = throttle;
            state.AppliedBrake = brake;
            state.AppliedHandbrake = Mathf.Clamp01(controls.Handbrake);
            state.SteerAngleDeg = steerAngle;
            state.TcsCut = tcsCut;
            state.AbsActive = absActive;
            state.SimTime += dt;

            Stepped?.Invoke(this);
        }

        /// <summary>Total drive torque (at the wheels) the driven tyres can transmit at their peak, through the differentials.</summary>
        float TractionCapacity(float reflectedInertia, int drivenCount, float acceleration)
        {
            float total = float.MaxValue;
            for (int a = 0; a < 2; a++)
            {
                int l = a * 2, rgt = l + 1;
                if (!IsDriven(l)) continue;
                float share = definition.Layout == DriveLayout.AWD ? (a == 0 ? definition.AwdFrontSplit : 1f - definition.AwdFrontSplit) : 1f;
                if (share <= 0.001f) continue;
                float capL = WheelTractionCapacity(l, reflectedInertia / Mathf.Max(1, drivenCount), acceleration);
                float capR = WheelTractionCapacity(rgt, reflectedInertia / Mathf.Max(1, drivenCount), acceleration);
                var diff = (a == 0 ? definition.Front : definition.Rear).Differential;
                float axleCap = diff.Type == DiffType.Open ? 2f * Mathf.Min(capL, capR) : capL + capR;
                total = Mathf.Min(total, axleCap / share);
            }
            return total == float.MaxValue ? 0f : total;
        }

        float WheelTractionCapacity(int i, float reflectedInertia, float acceleration)
        {
            ref WheelState w = ref wheels[i];
            if (!w.Grounded || w.Load <= 0f) return 0f;
            var tyre = Axle(i).Tyre;
            float mu = TyreModel.LoadedMu(tyre, w.Load, SurfaceGrip(w.Surface));
            return mu * w.Load * tyre.Radius + (tyre.WheelInertia + reflectedInertia) * acceleration / tyre.Radius;
        }

        static float ApplyBrake(float omega, float maxDelta)
        {
            if (maxDelta <= 0f) return omega;
            if (Mathf.Abs(omega) <= maxDelta) return 0f;
            return omega - Mathf.Sign(omega) * maxDelta;
        }

        float SurfaceGrip(SurfaceDefinition s)
        {
            if (s != null) return s.GripMultiplier;
            return defaultSurface != null ? defaultSurface.GripMultiplier : 1f;
        }

        void Ackermann(float steerDeg, out float left, out float right)
        {
            left = right = steerDeg;
            float ack = Mathf.Clamp01(definition.Steering.Ackermann);
            if (Mathf.Abs(steerDeg) < 0.01f || ack <= 0f) return;
            float wb = Mathf.Max(0.5f, definition.Wheelbase);
            float track = definition.Front.TrackWidth;
            float radius = wb / Mathf.Tan(Mathf.Abs(steerDeg) * Mathf.Deg2Rad);
            float inner = Mathf.Atan(wb / Mathf.Max(0.1f, radius - track * 0.5f)) * Mathf.Rad2Deg;
            float outer = Mathf.Atan(wb / (radius + track * 0.5f)) * Mathf.Rad2Deg;
            float sign = Mathf.Sign(steerDeg);
            float innerDeg = Mathf.Lerp(Mathf.Abs(steerDeg), inner, ack) * sign;
            float outerDeg = Mathf.Lerp(Mathf.Abs(steerDeg), outer, ack) * sign;
            // Steering right (+): the right wheel is the inner wheel.
            if (steerDeg > 0f) { right = innerDeg; left = outerDeg; }
            else { left = innerDeg; right = outerDeg; }
        }

        bool CastWheel(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit best)
        {
            best = default;
            int count = Physics.RaycastNonAlloc(origin, direction, hitBuffer, maxDistance, ~0, QueryTriggerInteraction.Ignore);
            float bestDistance = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = hitBuffer[i];
                if (hit.rigidbody == rb) continue;
                if (hit.distance < bestDistance) { bestDistance = hit.distance; best = hit; found = true; }
            }
            return found;
        }

        SurfaceDefinition ResolveSurface(Collider c)
        {
            if (c == null) return defaultSurface;
            if (!surfaceCache.TryGetValue(c, out var surface))
            {
                var tag = c.GetComponentInParent<VehicleSurface>();
                surface = tag != null && tag.Surface != null ? tag.Surface : defaultSurface;
                surfaceCache[c] = surface;
            }
            return surface;
        }

        void LateUpdate()
        {
            if (!initialised) return;
            Transform t = transform;
            for (int i = 0; i < WheelCount; i++)
            {
                Vector3 hubLocal = mountLocal[i] - Vector3.up * wheels[i].SuspensionLength;
                Vector3 hubWorld = t.TransformPoint(hubLocal);
                Quaternion steer = Quaternion.AngleAxis(wheels[i].SteerAngleDeg, Vector3.up);
                if (wheelVisuals[i] != null)
                {
                    wheelVisuals[i].SetPositionAndRotation(hubWorld,
                        t.rotation * steer * Quaternion.AngleAxis(wheels[i].SpinAngleDeg, Vector3.right) * wheelBaseRot[i]);
                }
                if (caliperVisuals[i] != null)
                    caliperVisuals[i].SetPositionAndRotation(hubWorld, t.rotation * steer * caliperBaseRot[i]);
            }
        }

        /// <summary>Teleports the car and clears all motion (respawn, tests).</summary>
        public void ResetTo(Vector3 position, Quaternion rotation)
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
            rb.position = position;
            rb.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            prevVelocity = Vector3.zero;
            steerAngle = 0f;
            tcsCut = 0f;
            lateralG = longitudinalG = 0f;
            for (int i = 0; i < WheelCount; i++)
            {
                wheels[i].AngularVelocity = 0f;
                wheels[i].SlipRatio = 0f;
                prevLength[i] = wheels[i].SuspensionLength;
            }
            powertrain?.ForceGear(1);
        }

        /// <summary>Sets a forward speed with matching wheel spin (test set-ups, e.g. 100–0 braking).</summary>
        public void ForceForwardSpeed(float metresPerSecond)
        {
            rb.linearVelocity = transform.forward * metresPerSecond;
            prevVelocity = rb.linearVelocity;
            for (int i = 0; i < WheelCount; i++)
                wheels[i].AngularVelocity = metresPerSecond / Mathf.Max(0.05f, Axle(i).Tyre.Radius);
        }

        public void ForceGear(int gear) => powertrain?.ForceGear(gear);
    }
}
