using System.Text;
using UnityEngine;
using VeyraCity.Assists;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.UI
{
    /// <summary>
    /// Developer telemetry overlay (VEHICLE_HANDLING_SPEC §6) plus a minimal speed/gear readout.
    /// IMGUI on purpose: zero setup and always available in development. The player HUD (UI Toolkit)
    /// comes in Phase 3.
    /// </summary>
    public sealed class TelemetryOverlay : MonoBehaviour
    {
        public bool Visible = true;
        public string StatusLine = "";

        VehicleController vehicle;
        VehicleDriver driver;
        GUIStyle box, mono, big;
        readonly StringBuilder sb = new StringBuilder(2048);

        public void Bind(VehicleController v, VehicleDriver d)
        {
            vehicle = v;
            driver = d;
        }

        void EnsureStyles()
        {
            if (box != null) return;
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.62f));
            tex.Apply();
            box = new GUIStyle(GUI.skin.box) { normal = { background = tex } };
            mono = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true, normal = { textColor = Color.white } };
            mono.font = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Courier New" }, 13);
            big = new GUIStyle(GUI.skin.label) { fontSize = 46, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight, normal = { textColor = Color.white } };
        }

        void OnGUI()
        {
            if (vehicle == null || !vehicle.IsInitialised) return;
            EnsureStyles();
            var s = vehicle.State;

            // Speed and gear, bottom right (always on).
            string gear = s.Shifting ? "-" : s.Gear < 0 ? "R" : s.Gear == 0 ? "N" : s.Gear.ToString();
            float rpmFrac = Mathf.Clamp01(s.EngineRpm / Mathf.Max(1f, vehicle.Definition.Engine.LimiterRpm));
            var r = new Rect(Screen.width - 330, Screen.height - 120, 310, 100);
            GUI.Box(r, GUIContent.none, box);
            GUI.Label(new Rect(r.x, r.y - 8, r.width - 16, 70), $"{Mathf.RoundToInt(Mathf.Abs(s.ForwardSpeed) * 3.6f)} <size=20>km/h</size>  {gear}", RichBig());
            var bar = new Rect(r.x + 12, r.y + 72, r.width - 24, 14);
            GUI.Box(bar, GUIContent.none);
            GUI.color = s.AtLimiter ? Color.red : Color.Lerp(Color.white, new Color(1f, 0.55f, 0.1f), rpmFrac);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * rpmFrac, bar.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (!Visible) return;
            sb.Clear();
            var def = vehicle.Definition;
            sb.AppendLine($"<b>{def.DisplayName}</b>  [{def.BuildId} v{def.TuningVersion}]  physics {VehicleController.PhysicsVersion}");
            sb.AppendLine($"assists: {(driver != null && driver.Preset != null ? driver.Preset.PresetName : "none")}   {StatusLine}");
            sb.AppendLine($"speed {s.SpeedKmh,6:F1} km/h  gear {gear}  rpm {s.EngineRpm,5:F0}  Te {s.EngineTorque,5:F0} Nm");
            sb.AppendLine($"thr {s.AppliedThrottle:F2} brk {s.AppliedBrake:F2} hb {s.AppliedHandbrake:F2}  steer {s.SteerAngleDeg,5:F1}°");
            sb.AppendLine($"slip β {s.SlipAngleDeg,6:F1}°  yaw {s.YawRate * Mathf.Rad2Deg,6:F1}°/s");
            sb.AppendLine($"g lat {s.LateralG,5:F2}  long {s.LongitudinalG,5:F2}   TCS {s.TcsCut:F2}  ABS {(s.AbsActive ? "ON" : "--")}");
            sb.AppendLine("whl  load kN  comp   κ      α°     Fx     Fy   surface");
            for (int i = 0; i < VehicleController.WheelCount; i++)
            {
                var w = vehicle.GetWheel(i);
                string surf = w.Grounded ? (w.Surface != null ? w.Surface.Id : "default") : "air";
                sb.AppendLine($"{VehicleController.WheelNames[i]}   {w.Load / 1000f,6:F2} {w.Compression,5:F3} {w.SlipRatio,6:F2} {w.SlipAngleDeg,6:F1} {w.Fx,6:F0} {w.Fy,6:F0}  {surf}");
            }
            sb.Append("<color=#9fd>F3</color> overlay  <color=#9fd>F5</color> car  <color=#9fd>F6</color> assists  <color=#9fd>F7</color> record  <color=#9fd>V</color> camera  <color=#9fd>R</color> reset");
            var area = new Rect(Screen.width - 560, 10, 550, 250);
            GUI.Box(area, GUIContent.none, box);
            GUI.Label(new Rect(area.x + 10, area.y + 6, area.width - 20, area.height - 12), sb.ToString(), mono);
        }

        GUIStyle RichBig()
        {
            big.richText = true;
            return big;
        }
    }
}
