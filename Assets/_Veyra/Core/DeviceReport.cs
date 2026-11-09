using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace VeyraCity.Core
{
    /// <summary>One-line-per-field device and runtime report, logged at startup for every build (PC compatibility evidence).</summary>
    public static class DeviceReport
    {
        public static string Build(BuildInfo build)
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            var res = Screen.currentResolution;
            var sb = new StringBuilder();
            sb.AppendLine("[Veyra] ===== DEVICE REPORT =====");
            sb.AppendLine($"[Veyra] Build: {build}");
            sb.AppendLine($"[Veyra] GPU: {SystemInfo.graphicsDeviceName} | vendor {SystemInfo.graphicsDeviceVendor} | {SystemInfo.graphicsMemorySize} MB | API {SystemInfo.graphicsDeviceType} ({SystemInfo.graphicsDeviceVersion})");
            sb.AppendLine($"[Veyra] CPU: {SystemInfo.processorType} | {SystemInfo.processorCount} threads | RAM {SystemInfo.systemMemorySize} MB");
            sb.AppendLine($"[Veyra] OS: {SystemInfo.operatingSystem}");
            sb.AppendLine($"[Veyra] Display: window {Screen.width}x{Screen.height} ({Screen.fullScreenMode}) | monitor {res.width}x{res.height} @ {res.refreshRateRatio.value:0.##} Hz");
            sb.AppendLine($"[Veyra] Quality: {QualitySettings.names[QualitySettings.GetQualityLevel()]} | vSync {QualitySettings.vSyncCount} | targetFrameRate {Application.targetFrameRate}");
            sb.AppendLine($"[Veyra] Render pipeline: {(rp != null ? rp.name + " (" + rp.GetType().Name + ")" : "Built-in")}");
            if (IsLikelyIntegratedGpu(SystemInfo.graphicsDeviceVendor, SystemInfo.graphicsDeviceName))
                sb.AppendLine("[Veyra] WARNING: running on an integrated GPU. On a hybrid laptop, force the NVIDIA GPU in Windows Settings > Display > Graphics.");
            sb.Append("[Veyra] ===========================");
            return sb.ToString();
        }

        public static bool IsLikelyIntegratedGpu(string vendor, string deviceName)
        {
            string v = (vendor ?? "").ToLowerInvariant();
            string d = (deviceName ?? "").ToLowerInvariant();
            if (v.Contains("intel") && !d.Contains("arc")) return true;
            if (d.Contains("microsoft basic render")) return true;
            return false;
        }
    }
}
