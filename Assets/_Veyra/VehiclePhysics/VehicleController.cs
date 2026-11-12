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
        [Tooltip("Driving style layer (arcade drift, stability, air control, wall glancing). Null = pure simulation.")]
        [SerializeField] HandlingFeel feel;

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
        // Handling-feel layer state
        bool drifting;
        int driftDir;
        float driftBlend, driftExitTimer, driftSpeed, airTime, flipTimer;
        Vector3 tyreForceSum;

        public VehicleDefinition Definition => definition;
        public Rigidbody Body => rb;
        public VehicleState State => state;
        public VehicleControls Controls => controls;
        public bool IsInitialised => initialised;
        public SurfaceDefinition DefaultSurface { get => defaultSurface; set => defaultSurface = value; }
        public HandlingFeel Feel
        {
            get => feel;
            set
            {
                feel = value;
                // BUG-087: the feel asset may arrive after Initialize; keep the rigidbody limits in step with it
                if (rb != null && feel != null) { rb.angularDamping = feel.AngularDamping; rb.maxAngularVelocity = feel.MaxAngularVelocity; }
            }
        }
        /// <summary>Raised when the body glances off or hits static world geometry: contact point, wall normal, 0..1 intensity.</summary>
        public event Action<Vector3, Vector3, float> Scraped;
        /// <summary>Raised when a flipped car was set back on its wheels.</summary>
        public event Action<VehicleController> Recovered;
        public bool ManualStepping { get => manualStepping; set => manualStepping = value; }
        public int TyreSubsteps { get => tyreSubsteps; set => tyreSubsteps = Mathf.Clamp(value, 1, 16); }
        public WheelState GetWheel(int i) => wheels[i];
        /// <summary>Raised after every simulation step (telemetry, effects).</summary>
        public event Action<VehicleController> Stepped;
        /// <summary>Raised for every collision the body takes (walls, cars, props). Used by nitro, scoring and audio.</summary>
        public event Action<VehicleController, Collision> Collided;
        /// <summary>Seconds since the last collision of any strength.</summary>
        public float TimeSinceCollision { get; private set; } = 999f;

        void OnCollisionEnter(Collision c)
        {
            TimeSinceCollision = 0f;
            Collided?.Invoke(this, c);
            GlanceOffWall(c, true);
        }

        void OnCollisionStay(Collision c)
        {
            if (c.impulse.sqrMagnitude > 4f) TimeSinceCollision = 0f;
            GlanceOffWall(c, false);
        }

        /// <summary>
        /// Arcade wall response: on a shallow hit against static geometry the car keeps its speed along the wall
        /// (minus a small loss) and bounces off a little, instead of the solver's dead stop. Steep hits stay physical.
        /// </summary>
        /// <summary>
        /// Pure wall response (unit-tested): for a horizontal velocity into a wall with horizontal normal
        /// <paramref name="n"/>, returns false for steep hits (left to the solver); otherwise the new velocity keeps
        /// the component along the wall minus a loss (first contact: share scaled by the angle; grinding: ~20 %/s)
        /// and bounces off with the restitution.
        /// </summary>
        public static bool WallGlance(Vector3 vFlat, Vector3 n, HandlingFeel feel, bool firstContact, float dt, out Vector3 vNew)
        {
            vNew = vFlat;
            if (feel == null || feel.WallGlanceMaxDeg <= 0f) return false;
            float vn = Vector3.Dot(vFlat, n);
            float speed = vFlat.magnitude;
            if (vn >= 0f || speed < 1e-3f) return false;
            float angle = Mathf.Asin(Mathf.Clamp01(-vn / speed)) * Mathf.Rad2Deg;
            if (angle > feel.WallGlanceMaxDeg) return false;
            Vector3 vt = vFlat - n * vn;
            float loss = firstContact ? feel.WallSpeedLoss * Mathf.Clamp01(angle / Mathf.Max(1f, feel.WallGlanceMaxDeg)) : 0.2f * dt;
            vNew = vt * (1f - loss) - n * vn * feel.WallRestitution;
            return true;
        }

        void GlanceOffWall(Collision c, bool firstContact)
        {
            if (feel == null || feel.WallGlanceMaxDeg <= 0f || c.rigidbody != null || rb == null) return;
            Vector3 n = Vector3.zero, p = Vector3.zero;
            int k = 0;
            for (int i = 0; i < c.contactCount; i++)
            {
                var cp = c.GetContact(i);
                if (Mathf.Abs(cp.normal.y) > 0.5f) continue;   // ground, ramps, kerb tops: not a wall
                n += cp.normal;
                p += cp.point;
                k++;
            }
            if (k == 0) return;
            n = Vector3.ProjectOnPlane(n, Vector3.up);
            if (n.sqrMagnitude < 1e-6f) return;
            n.Normalize();
            p /= k;
            Vector3 vPre = prevVelocity;                       // velocity before this step's contact solve
            Vector3 vFlat = Vector3.ProjectOnPlane(vPre, Vector3.up);
            float vn = Vector3.Dot(vFlat, n);
            float speed = vFlat.magnitude;
            if (vn > -0.5f || speed < 2f) return;              // not driving into the wall
            float angle = Mathf.Asin(Mathf.Clamp01(-vn / speed)) * Mathf.Rad2Deg;
            float intensity = Mathf.Clamp01(speed / 30f) * Mathf.Lerp(0.35f, 1f, angle / 90f);
            Scraped?.Invoke(p, n, intensity);
            if (!WallGlance(vFlat, n, feel, firstContact, Time.fixedDeltaTime, out Vector3 v)) return;   // a real crash: solver
            Vector3 vt = vFlat - n * vn;
            v.y = rb.linearVelocity.y;
            rb.linearVelocity = v;
            prevVelocity = v;
            // Turn the car parallel to the wall (instead of the solver's spin back across the road).
            Vector3 up = transform.up;
            Vector3 w = rb.angularVelocity;
            float yaw = Vector3.Dot(w, up);
            float yawNew = yaw * Mathf.Pow(feel.WallYawKeepPer10ms, Time.fixedDeltaTime * 100f);   // BUG-082: same damping at 50–120 Hz
            if (firstContact && vt.sqrMagnitude > 1f)
            {
                float toTangent = Vector3.SignedAngle(Vector3.ProjectOnPlane(transform.forward, up), vt, up) * Mathf.Deg2Rad;
                if (Mathf.Abs(toTangent) < feel.WallAlignMaxRad) yawNew = Mathf.Clamp(toTangent * feel.WallAlignGain, -feel.WallAlignMaxRate, feel.WallAlignMaxRate);
            }
            rb.angularVelocity = w + up * (yawNew - yaw);
        }

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
            rb.angularDamping = feel != null ? feel.AngularDamping : 0.05f;   // BUG-087: from the HandlingFeel asset
            rb.maxAngularVelocity = feel != null ? feel.MaxAngularVelocity : 30f;

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
            bool drivenGrounded = false;
            for (int i = 0; i < WheelCount; i++) if (IsDriven(i) && wheels[i].Grounded) { drivenGrounded = true; break; }
            // Wheelspin by rim speed against road speed (the slip ratio reads 0 near standstill, iteration 10 launch trace):
            // the automatic must not upshift on wheels spinning far faster than the car moves.
            float drivenRadius = def.Layout == DriveLayout.FWD ? def.Front.Tyre.Radius
                               : def.Layout == DriveLayout.RWD ? def.Rear.Tyre.Radius : 0.5f * (def.Front.Tyre.Radius + def.Rear.Tyre.Radius);
            float rimSpeed = Mathf.Abs(drivenOmega) * drivenRadius;
            float roadSpeed = Mathf.Abs(Vector3.Dot(rb.linearVelocity, fwd));
            bool spinning = rimSpeed > roadSpeed * 1.3f + 1.5f;
            // launch guards apply below 15 m/s only (iteration 12: at 160+ km/h under Overdrive the rear slip passes 0.3
            // legitimately; blocking upshifts and full cuts there threw the nitro run into the barrier)
            bool launchSpeed = roadSpeed < 15f;
            float drivenSlip = !drivenGrounded ? -1f : !launchSpeed ? 0f : spinning ? Mathf.Max(maxDrivenSlip, 1f) : maxDrivenSlip;
            // Launch / landing wheelspin with traction control on: the automatic slips its clutch so the engine's reflected
            // inertia stops holding the wheels in a spin (iteration 11 trace: slip 7 -> 2 took 1.5 s at zero throttle)
            if (spinning && drivenGrounded && roadSpeed < 12f && !drifting && controls.TcsPeakSlipFactor > 0f) declutch = true;
            // Airborne driven wheels get no drive (arcade landing): they keep their road speed through a spawn drop or a jump
            // instead of spinning up to the limiter and landing in a wheelspin.
            if (!drivenGrounded) declutch = true;
            float axleTorqueTotal = powertrain.Step(dt, throttle, torqueScale, drivenOmega, controls.WantsReverse,
                                                    def.Gearbox.Automatic ? 0 : controls.GearRequest, out float reflectedInertia, declutch,
                                                    drivenSlip);

            // Traction control: feed-forward limit of drive torque to what the driven tyres can transmit
            // (peak friction plus the torque needed to spin up the wheels and engine), trimmed by measured slip.
            if (controls.TcsPeakSlipFactor > 0f && axleTorqueTotal > 0f && grounded > 0)
            {
                var drivenTyre = def.Layout == DriveLayout.FWD ? def.Front.Tyre : def.Rear.Tyre;
                float target = Mathf.Max(0.01f, drivenTyre.LongPeakSlip) * controls.TcsPeakSlipFactor;
                // gross wheelspin (> 4x the target, e.g. landing at full throttle with the wheels spun up): allow a full cut so
                // the tyres can grip again; ordinary slip keeps the 25 % floor (iteration 9 launch trace: slip 6.9 held for 2.5 s)
                bool gross = launchSpeed && (maxDrivenSlip > target * 4f || spinning);
                tcsTrim = maxDrivenSlip > target || (spinning && launchSpeed) ? Mathf.Max(gross ? 0f : 0.25f, tcsTrim - (gross ? 10f : 4f) * dt)
                                                              : Mathf.Min(1.25f, tcsTrim + (tcsTrim < 0.25f ? 4f : 1.2f) * dt);
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
            // In an arcade drift the handbrake only starts the slide; it must not lock the rear and kill the speed.
            float handbrake = Mathf.Clamp01(controls.Handbrake) * def.Brakes.HandbrakeTorque * (1f - 0.85f * driftBlend);
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
            // While an arcade drift is held, tyre side forces are scaled down: the feel layer carves the path instead.
            float lateralScale = feel != null ? 1f - driftBlend * (1f - feel.DriftTyreLateral) : 1f;
            tyreForceSum = Vector3.zero;
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
                Vector3 force = contactForward[i] * (w.Fx + rolling) + contactRight[i] * (w.Fy * lateralScale);
                Vector3 point = w.ContactPoint + up * axle.Suspension.RollCentreHeight;
                rb.AddForceAtPosition(force, point);
                tyreForceSum += force;
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
            if (controls.ExtraThrust > 0f && grounded > 0 && driftBlend < 0.5f) rb.AddForce(fwd * controls.ExtraThrust);

            // ---- Handling feel (arcade layer) ----
            ApplyFeel(dt, grounded, v);

            // ---- State ----
            Vector3 accel = (v - prevVelocity) / dt;
            prevVelocity = v;
            Vector3 localV = t.InverseTransformDirection(v);
            float gFilter = 1f - Mathf.Exp(-dt / 0.028f);   // BUG-082: 28 ms filter, independent of the physics rate
            longitudinalG = Mathf.Lerp(longitudinalG, Vector3.Dot(accel, fwd) / g, gFilter);
            lateralG = Mathf.Lerp(lateralG, Vector3.Dot(accel, t.right) / g, gFilter);
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
            state.ArcadeDrift = drifting;
            state.DriftBlend = driftBlend;
            state.AirTime = airTime;
            state.SimTime += dt;

            Stepped?.Invoke(this);
        }

        /// <summary>D2 (round 3): the feel layer's side forces act between the centre of mass and the ground under it
        /// (<see cref="HandlingFeel.SideForceContactShare"/>), so they roll the body and shift load to the outer tyres.</summary>
        Vector3 SideForcePoint(HandlingFeel f)
        {
            Vector3 com = rb.worldCenterOfMass;
            float h = definition != null ? Mathf.Max(0.1f, definition.Chassis.CoMHeight) : 0.5f;
            return com - transform.up * (h * Mathf.Clamp01(f.SideForceContactShare));
        }

        /// <summary>
        /// The <see cref="HandlingFeel"/> layer. Side forces act part-way down to the contact patches and all torques
        /// about the body's up axis or, in the air, pitch/roll toward level. Grip mode and drift mode blend by driftBlend.
        /// </summary>
        void ApplyFeel(float dt, int grounded, Vector3 v)
        {
            var f = feel;
            if (f == null) { drifting = false; driftBlend = 0f; airTime = 0f; return; }
            Transform t = transform;
            Vector3 up = t.up, fwd = t.forward, right = t.right;
            float g = Physics.gravity.magnitude;
            float mass = rb.mass;
            float speed = v.magnitude;
            float fwdSpeed = Vector3.Dot(v, fwd);
            float steerIn = Mathf.Clamp(controls.SteerIntent, -1f, 1f);
            bool onGround = grounded >= 2;
            airTime = grounded == 0 ? airTime + dt : 0f;

            // ---- drift state machine ----
            if (f.DriftEnabled && !drifting && onGround && !controls.WantsReverse && fwdSpeed * 3.6f > f.DriftMinSpeedKmh
                && Mathf.Abs(steerIn) >= f.DriftEntrySteer && (controls.DriftRequest || controls.Handbrake > 0.5f))
            {
                drifting = true;
                driftDir = steerIn > 0f ? 1 : -1;
                driftExitTimer = 0f;
                driftSpeed = Vector3.ProjectOnPlane(v, up).magnitude;
            }
            if (drifting)
            {
                float inward = steerIn * driftDir;   // 1 = full steering into the corner, -1 = full counter-steer
                bool ending = inward < f.DriftExitSteer || speed * 3.6f < f.DriftMinSpeedKmh * f.DriftExitSpeedShare
                              || controls.Brake > f.DriftExitBrake || controls.WantsReverse || airTime > f.DriftExitAirSeconds;
                driftExitTimer = ending ? driftExitTimer + dt : 0f;
                if (driftExitTimer >= f.DriftExitDelay || inward < -f.DriftCancelCounterSteer) drifting = false;
            }
            driftBlend = Mathf.MoveTowards(driftBlend, drifting ? 1f : 0f, dt / Mathf.Max(0.01f, drifting ? f.DriftBlendIn : f.DriftBlendOut));

            if (onGround)
            {
                // ---- downforce: planted at speed ----
                if (f.DownforceG > 0f)
                {
                    float x = speed * 3.6f / Mathf.Max(30f, f.DownforceRefKmh);
                    rb.AddForce(-up * (mass * g * f.DownforceG * Mathf.Min(x * x, 1.5f)));
                }

                // ---- grip mode: slide damping and yaw tracking (GTA-like) ----
                float wGrip = (1f - driftBlend) * (1f - f.HandbrakeGripCut * Mathf.Clamp01(controls.Handbrake));
                if (wGrip > 0.001f && speed > 1f)
                {
                    float vLat = Vector3.Dot(v, right);
                    if (f.SlideDamping > 0f)
                    {
                        float a = Mathf.Clamp(-vLat * f.SlideDamping, -f.SlideDampingMaxG * g, f.SlideDampingMaxG * g);
                        rb.AddForceAtPosition(right * (a * mass * wGrip), SideForcePoint(f));
                    }
                    if (f.YawTracking > 0f && Mathf.Abs(fwdSpeed) > 1f)
                    {
                        float rRef = fwdSpeed * Mathf.Tan(steerAngle * Mathf.Deg2Rad) / Mathf.Max(0.5f, definition.Wheelbase);
                        // per-car character: the cap follows the tyres' grip, the turn-in the car's mass (iteration 5)
                        float tyreMu = 0.5f * (definition.Front.Tyre.Mu + definition.Rear.Tyre.Mu);
                        float gripK = f.ReferenceMu > 0.01f ? Mathf.Clamp(tyreMu / f.ReferenceMu, 0.6f, 1.4f) : 1f;
                        float agility = Mathf.Clamp(Mathf.Sqrt(f.ReferenceMass / Mathf.Max(300f, mass)), f.AgilityRange.x, f.AgilityRange.y);
                        float rMax = f.YawRateLimitG * gripK * g / Mathf.Max(3f, Mathf.Abs(fwdSpeed));
                        rRef = Mathf.Clamp(rRef, -rMax, rMax);
                        float r = Vector3.Dot(rb.angularVelocity, up);
                        float alpha = Mathf.Clamp((rRef - r) * f.YawTracking * agility, -f.YawTrackingMaxAccel * agility, f.YawTrackingMaxAccel * agility);
                        rb.AddTorque(up * (alpha * wGrip), ForceMode.Acceleration);
                    }
                }

                // ---- drift mode: carve the path, hold the angle, keep the speed (Asphalt-like) ----
                if (driftBlend > 0.001f)
                {
                    Vector3 vFlat = Vector3.ProjectOnPlane(v, up);
                    float vmag = vFlat.magnitude;
                    if (vmag > 2f)
                    {
                        Vector3 vDir = vFlat / vmag;
                        Vector3 latDir = Vector3.Cross(up, vDir);      // right of the direction of travel
                        float inward = Mathf.Clamp01(steerIn * driftDir);
                        float omega = driftDir * Mathf.Lerp(f.DriftTurnRate.x, f.DriftTurnRate.y, inward);
                        float omegaMax = f.DriftMaxLatG * g / Mathf.Max(3f, vmag);
                        omega = Mathf.Clamp(omega, -omegaMax, omegaMax);
                        // Total side force = exactly the centripetal force of the commanded turn (tops up or cancels the tyres).
                        float latNeeded = mass * vmag * omega - Vector3.Dot(tyreForceSum, latDir);
                        // Speed: decays slowly on throttle, faster off it; nitro can push it up.
                        float loss = controls.Throttle > 0.3f ? f.DriftSpeedLoss : f.DriftSpeedLoss * 4f;
                        driftSpeed = Mathf.Max(0f, driftSpeed - driftSpeed * loss * dt + controls.ExtraThrust / mass * dt);
                        float alongNeeded = mass * (driftSpeed - vmag) * 3f - Vector3.Dot(tyreForceSum, vDir);
                        rb.AddForceAtPosition((latDir * latNeeded + vDir * alongNeeded) * driftBlend, SideForcePoint(f));
                        // Heading: drift angle β (velocity relative to heading, + = right) toward the target set by the steering.
                        float beta = Mathf.Atan2(Vector3.Dot(vFlat, right), Mathf.Max(0.1f, Vector3.Dot(vFlat, fwd)));
                        float target = -driftDir * Mathf.Lerp(f.DriftAngleDeg.x, f.DriftAngleDeg.y, inward) * Mathf.Deg2Rad;
                        float rDes = omega - f.DriftAngleGain * (target - beta);
                        float r = Vector3.Dot(rb.angularVelocity, up);
                        float alpha = Mathf.Clamp((rDes - r) * f.DriftYawGain, -15f, 15f);
                        rb.AddTorque(up * (alpha * driftBlend), ForceMode.Acceleration);
                    }
                }
            }
            else if (grounded == 0 && airTime > 0.12f)
            {
                // ---- air: level out for the landing, let the player turn the nose ----
                Vector3 axis = Vector3.Cross(up, Vector3.up);
                Vector3 w = rb.angularVelocity;
                Vector3 wTilt = Vector3.ProjectOnPlane(w, Vector3.up);
                Vector3 acc = axis * f.AirLevelGain - wTilt * f.AirDamping + Vector3.up * (steerIn * f.AirYawControl);
                rb.AddTorque(acc, ForceMode.Acceleration);
            }

            // ---- flipped and stopped: set back on the wheels ----
            if (f.FlipRecoverSeconds > 0f && Vector3.Dot(up, Vector3.up) < 0.3f && speed < 3f)
            {
                flipTimer += dt;
                if (flipTimer >= f.FlipRecoverSeconds)
                {
                    flipTimer = 0f;
                    Vector3 heading = Vector3.ProjectOnPlane(fwd, Vector3.up);
                    if (heading.sqrMagnitude < 1e-4f) heading = Vector3.ProjectOnPlane(-up, Vector3.up);
                    if (heading.sqrMagnitude < 1e-4f) heading = Vector3.forward;
                    ResetTo(rb.position + Vector3.up * 1.2f, Quaternion.LookRotation(heading.normalized, Vector3.up));
                    Recovered?.Invoke(this);
                }
            }
            else flipTimer = 0f;
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
            TimeSinceCollision += Time.deltaTime;
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
            drifting = false;
            driftBlend = driftExitTimer = airTime = flipTimer = 0f;
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
