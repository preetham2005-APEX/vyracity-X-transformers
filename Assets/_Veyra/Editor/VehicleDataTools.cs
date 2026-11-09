using System.IO;
using UnityEditor;
using UnityEngine;
using VeyraCity.Assists;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Creates the initial tuning data assets from VEHICLE_HANDLING_SPEC §4 (targets, not measurements).
    /// Existing assets are never overwritten unless forced, so tuning done in the editor is preserved.
    /// </summary>
    public static class VehicleDataTools
    {
        public const string DataRoot = "Assets/_Veyra/Data";
        public const string VehiclesDir = DataRoot + "/Vehicles";
        public const string SurfacesDir = DataRoot + "/Surfaces";
        public const string AssistsDir = DataRoot + "/Assists";

        public static readonly string[] VehicleIds = { "Kite_Stock_RWD", "Kite_Street_RWD", "Greybox_FWD", "Greybox_AWD" };
        public static readonly string[] SurfaceIds = { "dry_asphalt", "concrete", "wet_asphalt", "gravel", "dirt", "grass" };

        [MenuItem("Veyra/Data/Create Missing Default Tuning Assets")]
        public static void EnsureMenu() => EnsureDefaultData(false);

        public static void EnsureDefaultData(bool force)
        {
            Directory.CreateDirectory(VehiclesDir);
            Directory.CreateDirectory(SurfacesDir);
            Directory.CreateDirectory(AssistsDir);

            Surface("dry_asphalt", 1.00f, 1.0f, force);
            Surface("concrete", 0.95f, 1.0f, force);
            Surface("wet_asphalt", 0.75f, 1.1f, force);
            Surface("gravel", 0.60f, 3.0f, force);
            Surface("dirt", 0.65f, 2.5f, force);
            Surface("grass", 0.50f, 4.0f, force);

            Preset("Casual", force, p =>
            {
                p.SteeringSpeedRef = 22f; p.MinSteerFraction = 0.12f; p.CounterSteer = 0.7f;
                p.TractionControl = true; p.TcsPeakSlipFactor = 1.2f; p.Abs = true;
                p.StabilityControl = 0.6f; p.StabilitySlipLimitDeg = 50f; p.DriftAssist = 1f; p.DriftBandDeg = new Vector2(25f, 45f); p.DriftTcsPeakSlipFactor = 3f;
            });
            Preset("Advanced", force, p =>
            {
                p.SteeringSpeedRef = 34f; p.MinSteerFraction = 0.2f; p.CounterSteer = 0.2f;
                p.TractionControl = false; p.TcsPeakSlipFactor = 1.2f; p.Abs = true;
                p.StabilityControl = 0f; p.StabilitySlipLimitDeg = 60f; p.DriftAssist = 0.2f; p.DriftBandDeg = new Vector2(25f, 45f); p.DriftTcsPeakSlipFactor = 0f;
            });

            Vehicle("Kite_Stock_RWD", force, KiteStock);
            Vehicle("Kite_Street_RWD", force, KiteStreet);
            Vehicle("Greybox_FWD", force, GreyboxFwd);
            Vehicle("Greybox_AWD", force, GreyboxAwd);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Veyra] Default tuning data ensured (force=" + force + ")");
        }

        public static VehicleDefinition LoadVehicle(string id) => AssetDatabase.LoadAssetAtPath<VehicleDefinition>($"{VehiclesDir}/{id}.asset");
        public static SurfaceDefinition LoadSurface(string id) => AssetDatabase.LoadAssetAtPath<SurfaceDefinition>($"{SurfacesDir}/{id}.asset");
        public static AssistPreset LoadPreset(string name) => AssetDatabase.LoadAssetAtPath<AssistPreset>($"{AssistsDir}/{name}.asset");

        static void Surface(string id, float grip, float rolling, bool force)
        {
            string path = $"{SurfacesDir}/{id}.asset";
            if (!force && File.Exists(path)) return;
            var s = ScriptableObject.CreateInstance<SurfaceDefinition>();
            s.Id = id; s.GripMultiplier = grip; s.RollingResistanceMultiplier = rolling;
            Save(s, path);
        }

        static void Preset(string name, bool force, System.Action<AssistPreset> fill)
        {
            string path = $"{AssistsDir}/{name}.asset";
            if (!force && File.Exists(path)) return;
            var p = ScriptableObject.CreateInstance<AssistPreset>();
            p.PresetName = name;
            fill(p);
            Save(p, path);
        }

        static void Vehicle(string id, bool force, System.Action<VehicleDefinition> fill)
        {
            string path = $"{VehiclesDir}/{id}.asset";
            if (!force && File.Exists(path)) return;
            var v = ScriptableObject.CreateInstance<VehicleDefinition>();
            v.BuildId = id;
            fill(v);
            Save(v, path);
        }

        /// <summary>Creates the asset, or overwrites an existing one in place (keeps its GUID, so scene references survive).</summary>
        static void Save(Object o, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (existing != null && existing.GetType() == o.GetType())
            {
                string keepName = existing.name;
                EditorUtility.CopySerialized(o, existing);
                existing.name = keepName;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(o);
                return;
            }
            if (existing != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(o, path);
        }

        /// <summary>Batch entry: rewrites every default tuning asset from the factory values (GUIDs preserved).</summary>
        public static void RegenerateBatch()
        {
            int code = 0;
            try { EnsureDefaultData(force: true); }
            catch (System.Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        // ---------- helpers ----------
        static float SpringFor(float mass, float frontFraction, bool front, float hz)
        {
            float corner = mass * (front ? frontFraction : 1f - frontFraction) * 0.5f;
            float w = 2f * Mathf.PI * hz;
            return corner * w * w;
        }

        static float CriticalDamping(float mass, float frontFraction, bool front, float k)
        {
            float corner = mass * (front ? frontFraction : 1f - frontFraction) * 0.5f;
            return 2f * Mathf.Sqrt(k * corner);
        }

        static SuspensionSpec Susp(float mass, float ff, bool front, float hz, float rest, float travel, float arb, float rc, float bumpRatio = 0.3f, float reboundRatio = 0.6f)
        {
            float k = SpringFor(mass, ff, front, hz);
            float c = CriticalDamping(mass, ff, front, k);
            return new SuspensionSpec
            {
                RestLength = rest, Travel = travel, SpringRate = Mathf.Round(k), BumpDamping = Mathf.Round(c * bumpRatio),
                ReboundDamping = Mathf.Round(c * reboundRatio), BumpStopRate = 150000f, AntiRollRate = arb, RollCentreHeight = rc,
            };
        }

        static TyreSpec Tyre(float radius, float width, float mu, float latScale, float peakSlip, float peakAngle, float cx, float cy)
            => new TyreSpec
            {
                Radius = radius, Width = width, WheelInertia = 1.1f * (radius / 0.316f) * (radius / 0.316f), Mu = mu, LateralGripScale = latScale,
                LongPeakSlip = peakSlip, LatPeakSlipDeg = peakAngle, LongShape = cx, LatShape = cy, LoadSensitivity = 0.12f,
                NominalLoad = 3000f, RollingResistance = 0.012f,
            };

        // ---------- builds (VEHICLE_HANDLING_SPEC §4) ----------
        static void KiteStock(VehicleDefinition v)
        {
            v.DisplayName = "Arden Kite (Stock)";
            v.Layout = DriveLayout.RWD; v.Wheelbase = 2.55f;
            v.Chassis = new ChassisSpec { Mass = 1150f, FrontWeightFraction = 0.53f, CoMHeight = 0.48f, Dimensions = new Vector3(1.78f, 1.30f, 4.30f), InertiaScale = 0.85f };
            var tyre = Tyre(0.316f, 0.205f, 1.05f, 0.90f, 0.10f, 8f, 1.75f, 1.50f);
            v.Front = new AxleSpec { TrackWidth = 1.50f, Suspension = Susp(1150f, 0.53f, true, 1.55f, 0.24f, 0.15f, 16000f, 0.06f), Tyre = tyre };
            v.Rear = new AxleSpec { TrackWidth = 1.50f, Suspension = Susp(1150f, 0.53f, false, 1.70f, 0.24f, 0.15f, 9000f, 0.10f), Tyre = tyre,
                                    Differential = new DifferentialSpec { Type = DiffType.Open } };
            v.Engine = new EngineSpec
            {
                TorqueCurve = new[] { V(1000, 105), V(2000, 130), V(3000, 148), V(4000, 160), V(4800, 165), V(5500, 160), V(6200, 152), V(6800, 146), V(7400, 128) },
                IdleRpm = 900f, LimiterRpm = 7400f, Inertia = 0.16f, EngineBrakeTorque = 45f, LaunchRpm = 3200f,
            };
            v.Gearbox = new GearboxSpec { Ratios = new[] { 3.25f, 1.95f, 1.36f, 1.03f, 0.82f }, ReverseRatio = 3.30f, FinalDrive = 4.10f, Efficiency = 0.88f,
                                          ShiftTime = 0.25f, ShiftUpRpm = 7100f, ShiftDownRpm = 3000f, Automatic = true };
            v.Brakes = new BrakeSpec { MaxTorque = 4400f, FrontBias = 0.68f, HandbrakeTorque = 1800f };
            v.Steering = new SteeringSpec { MaxAngleDeg = 34f, Ackermann = 0.5f, RateDegPerSec = 360f };
            v.Aero = new AeroSpec { DragArea = 0.62f, DownforceArea = 0.05f, FrontDownforceShare = 0.45f };
        }

        static void KiteStreet(VehicleDefinition v)
        {
            v.DisplayName = "Arden Kite (Street Build)";
            v.Layout = DriveLayout.RWD; v.Wheelbase = 2.55f;
            v.Chassis = new ChassisSpec { Mass = 1080f, FrontWeightFraction = 0.52f, CoMHeight = 0.44f, Dimensions = new Vector3(1.90f, 1.25f, 4.40f), InertiaScale = 0.85f };
            v.Front = new AxleSpec { TrackWidth = 1.58f, Suspension = Susp(1080f, 0.52f, true, 2.0f, 0.20f, 0.12f, 26000f, 0.05f),
                                     Tyre = Tyre(0.3186f, 0.225f, 1.17f, 0.92f, 0.09f, 7f, 1.8f, 1.55f) };
            v.Rear = new AxleSpec { TrackWidth = 1.60f, Suspension = Susp(1080f, 0.52f, false, 2.15f, 0.20f, 0.12f, 15000f, 0.09f),
                                    Tyre = Tyre(0.3266f, 0.245f, 1.17f, 0.92f, 0.09f, 7f, 1.8f, 1.55f),
                                    Differential = new DifferentialSpec { Type = DiffType.LimitedSlip, Preload = 80f, PowerLock = 0.35f, CoastLock = 0.20f } };
            v.Engine = new EngineSpec
            {
                TorqueCurve = new[] { V(1000, 150), V(2000, 240), V(3000, 330), V(3500, 355), V(4500, 360), V(5500, 345), V(6500, 320), V(7000, 305), V(7500, 285), V(7800, 250) },
                IdleRpm = 950f, LimiterRpm = 7800f, Inertia = 0.15f, EngineBrakeTorque = 55f, LaunchRpm = 3800f,
            };
            v.Gearbox = new GearboxSpec { Ratios = new[] { 3.10f, 2.15f, 1.62f, 1.30f, 1.08f, 0.90f }, ReverseRatio = 3.2f, FinalDrive = 3.90f, Efficiency = 0.90f,
                                          ShiftTime = 0.18f, ShiftUpRpm = 7500f, ShiftDownRpm = 3600f, Automatic = true };
            v.Brakes = new BrakeSpec { MaxTorque = 5400f, FrontBias = 0.66f, HandbrakeTorque = 2200f };
            v.Steering = new SteeringSpec { MaxAngleDeg = 36f, Ackermann = 0.4f, RateDegPerSec = 400f };
            v.Aero = new AeroSpec { DragArea = 0.70f, DownforceArea = 0.45f, FrontDownforceShare = 0.42f };
        }

        static void GreyboxFwd(VehicleDefinition v)
        {
            v.DisplayName = "Greybox FWD Hatch";
            v.Layout = DriveLayout.FWD; v.Wheelbase = 2.60f;
            v.Chassis = new ChassisSpec { Mass = 1250f, FrontWeightFraction = 0.62f, CoMHeight = 0.52f, Dimensions = new Vector3(1.80f, 1.45f, 4.20f), InertiaScale = 0.85f };
            var tyre = Tyre(0.317f, 0.215f, 1.0f, 0.90f, 0.10f, 8f, 1.75f, 1.5f);
            v.Front = new AxleSpec { TrackWidth = 1.54f, Suspension = Susp(1250f, 0.62f, true, 1.6f, 0.24f, 0.15f, 14000f, 0.06f), Tyre = tyre,
                                     Differential = new DifferentialSpec { Type = DiffType.Open } };
            v.Rear = new AxleSpec { TrackWidth = 1.52f, Suspension = Susp(1250f, 0.62f, false, 1.8f, 0.24f, 0.15f, 16000f, 0.10f), Tyre = tyre };
            v.Engine = new EngineSpec
            {
                TorqueCurve = new[] { V(1000, 140), V(1800, 230), V(2500, 260), V(4000, 260), V(5000, 250), V(6000, 225), V(6500, 205), V(6800, 180) },
                IdleRpm = 850f, LimiterRpm = 6800f, Inertia = 0.18f, EngineBrakeTorque = 50f, LaunchRpm = 3000f,
            };
            v.Gearbox = new GearboxSpec { Ratios = new[] { 3.45f, 2.05f, 1.45f, 1.12f, 0.92f, 0.78f }, ReverseRatio = 3.4f, FinalDrive = 3.65f, Efficiency = 0.90f,
                                          ShiftTime = 0.2f, ShiftUpRpm = 6500f, ShiftDownRpm = 2600f, Automatic = true };
            v.Brakes = new BrakeSpec { MaxTorque = 4600f, FrontBias = 0.70f, HandbrakeTorque = 1600f };
            v.Steering = new SteeringSpec { MaxAngleDeg = 35f, Ackermann = 0.5f, RateDegPerSec = 360f };
            v.Aero = new AeroSpec { DragArea = 0.68f, DownforceArea = 0f, FrontDownforceShare = 0.5f };
        }

        static void GreyboxAwd(VehicleDefinition v)
        {
            v.DisplayName = "Greybox AWD Performance";
            v.Layout = DriveLayout.AWD; v.AwdFrontSplit = 0.40f; v.Wheelbase = 2.65f;
            v.Chassis = new ChassisSpec { Mass = 1520f, FrontWeightFraction = 0.57f, CoMHeight = 0.50f, Dimensions = new Vector3(1.88f, 1.40f, 4.55f), InertiaScale = 0.85f };
            var tyre = Tyre(0.330f, 0.245f, 1.08f, 0.92f, 0.09f, 7.5f, 1.8f, 1.55f);
            v.Front = new AxleSpec { TrackWidth = 1.60f, Suspension = Susp(1520f, 0.57f, true, 1.8f, 0.22f, 0.13f, 22000f, 0.06f), Tyre = tyre,
                                     Differential = new DifferentialSpec { Type = DiffType.Open } };
            v.Rear = new AxleSpec { TrackWidth = 1.60f, Suspension = Susp(1520f, 0.57f, false, 1.95f, 0.22f, 0.13f, 18000f, 0.09f), Tyre = tyre,
                                    Differential = new DifferentialSpec { Type = DiffType.LimitedSlip, Preload = 60f, PowerLock = 0.3f, CoastLock = 0.15f } };
            v.Engine = new EngineSpec
            {
                TorqueCurve = new[] { V(1000, 200), V(2000, 360), V(3000, 420), V(4500, 420), V(5500, 400), V(6500, 365), V(7000, 340), V(7200, 300) },
                IdleRpm = 900f, LimiterRpm = 7200f, Inertia = 0.20f, EngineBrakeTorque = 60f, LaunchRpm = 4000f,
            };
            v.Gearbox = new GearboxSpec { Ratios = new[] { 3.30f, 2.20f, 1.60f, 1.25f, 1.02f, 0.84f }, ReverseRatio = 3.2f, FinalDrive = 3.85f, Efficiency = 0.86f,
                                          ShiftTime = 0.15f, ShiftUpRpm = 6900f, ShiftDownRpm = 3200f, Automatic = true };
            v.Brakes = new BrakeSpec { MaxTorque = 6200f, FrontBias = 0.64f, HandbrakeTorque = 2000f };
            v.Steering = new SteeringSpec { MaxAngleDeg = 34f, Ackermann = 0.5f, RateDegPerSec = 380f };
            v.Aero = new AeroSpec { DragArea = 0.72f, DownforceArea = 0.25f, FrontDownforceShare = 0.45f };
        }

        static Vector2 V(float rpm, float nm) => new Vector2(rpm, nm);
    }
}
