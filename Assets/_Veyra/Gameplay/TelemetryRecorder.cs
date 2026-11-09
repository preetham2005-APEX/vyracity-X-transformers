using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Records one telemetry row per physics step to CSV (VEHICLE_HANDLING_SPEC §6).
    /// The header records the physics version, vehicle build, tuning hash, assist preset,
    /// fixed Hz and render Hz.
    /// </summary>
    public sealed class TelemetryRecorder : IDisposable
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly VehicleController vehicle;
        readonly StringBuilder sb = new StringBuilder(1 << 20);
        public bool Recording { get; private set; }
        public int Rows { get; private set; }
        public string AssistPreset = "none";
        public string Label = "manual";
        public float RenderHz;

        public TelemetryRecorder(VehicleController vehicle) => this.vehicle = vehicle;

        public void Start(float fixedHz)
        {
            if (Recording) return;
            sb.Clear();
            Rows = 0;
            var def = vehicle.Definition;
            sb.Append("# veyra_telemetry v1").AppendLine();
            sb.Append($"# physics_version={VehicleController.PhysicsVersion}; vehicle={def.BuildId}; tuning_version={def.TuningVersion}; tuning_hash={def.TuningHash()}; ")
              .Append($"assist_preset={AssistPreset}; fixed_hz={fixedHz.ToString("F0", Inv)}; render_hz={RenderHz.ToString("F0", Inv)}; ")
              .Append($"unity={Application.unityVersion}; label={Label}; utc={DateTime.UtcNow:O}").AppendLine();
            sb.Append("t,speed_kmh,fwd_ms,lat_ms,gear,rpm,engine_nm,throttle,brake,handbrake,steer_deg,slip_deg,yaw_dps,lat_g,long_g,tcs,abs,pos_x,pos_y,pos_z");
            foreach (var w in VehicleController.WheelNames)
                sb.Append($",{w}_load,{w}_comp,{w}_kappa,{w}_alpha,{w}_fx,{w}_fy,{w}_omega,{w}_surface");
            sb.AppendLine();
            vehicle.Stepped += OnStepped;
            Recording = true;
        }

        public void Stop()
        {
            if (!Recording) return;
            vehicle.Stepped -= OnStepped;
            Recording = false;
        }

        void OnStepped(VehicleController v)
        {
            var s = v.State;
            Vector3 p = v.Body.position;
            sb.Append(F(s.SimTime, "F3")).Append(',').Append(F(s.SpeedKmh)).Append(',').Append(F(s.ForwardSpeed)).Append(',').Append(F(s.LateralSpeed)).Append(',')
              .Append(s.Gear).Append(',').Append(F(s.EngineRpm, "F0")).Append(',').Append(F(s.EngineTorque, "F1")).Append(',')
              .Append(F(s.AppliedThrottle)).Append(',').Append(F(s.AppliedBrake)).Append(',').Append(F(s.AppliedHandbrake)).Append(',')
              .Append(F(s.SteerAngleDeg)).Append(',').Append(F(s.SlipAngleDeg)).Append(',').Append(F(s.YawRate * Mathf.Rad2Deg)).Append(',')
              .Append(F(s.LateralG, "F3")).Append(',').Append(F(s.LongitudinalG, "F3")).Append(',').Append(F(s.TcsCut)).Append(',').Append(s.AbsActive ? 1 : 0).Append(',')
              .Append(F(p.x)).Append(',').Append(F(p.y)).Append(',').Append(F(p.z));
            for (int i = 0; i < VehicleController.WheelCount; i++)
            {
                var w = v.GetWheel(i);
                sb.Append(',').Append(F(w.Load, "F0")).Append(',').Append(F(w.Compression, "F4")).Append(',').Append(F(w.SlipRatio, "F3"))
                  .Append(',').Append(F(w.SlipAngleDeg)).Append(',').Append(F(w.Fx, "F0")).Append(',').Append(F(w.Fy, "F0"))
                  .Append(',').Append(F(w.AngularVelocity)).Append(',').Append(w.Grounded ? (w.Surface != null ? w.Surface.Id : "default") : "air");
            }
            sb.AppendLine();
            Rows++;
        }

        static string F(float x, string fmt = "F2") => x.ToString(fmt, Inv);

        public string Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, sb.ToString());
            return Path.GetFullPath(path);
        }

        /// <summary>Default folder: the project's Logs/telemetry in the editor, next to the executable in a player.</summary>
        public static string DefaultFolder()
        {
            string root = Application.isEditor ? Path.Combine(Application.dataPath, "..", "Logs") : Path.Combine(Application.dataPath, "..");
            return Path.GetFullPath(Path.Combine(root, "Telemetry"));
        }

        public void Dispose() => Stop();
    }
}
