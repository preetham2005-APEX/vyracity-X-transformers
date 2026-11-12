using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VeyraCity.World.Streaming;

namespace VeyraCity.World
{
    /// <summary>
    /// Roadmap D1 / BUG-068: the world's realtime box reflection probes (Crown Downtown, Neon Mile and the bridges; built by
    /// Veyra/Lighting/Build Reflection Probes under VC_Lighting_D1). Probes render via scripting only, one at a time and one
    /// face per frame (time slicing), so a refresh never costs more than one cube face in a frame. Day/night blending: a probe
    /// re-renders when the light it saw has changed (night factor or wetness moved by more than <see cref="RefreshDelta"/>),
    /// so its cubemap follows dusk and rain in steps of 8 %; between steps URP blends neighbouring probes.
    /// D1 / BUG-112: only probes within <see cref="ActiveRadius"/> of the camera render, nearest stale first, and a new probe
    /// starts at most every <see cref="MinInterval"/> seconds; a streamed cell only marks the probes around it stale (no
    /// burst of re-renders on every cell load). Quality tiers without realtime probes (mobile) disable them: an unrendered
    /// realtime probe would reflect black.
    /// </summary>
    public sealed class ReflectionProbeDirector : MonoBehaviour
    {
        public List<ReflectionProbe> Probes = new List<ReflectionProbe>();
        [Tooltip("Re-render a probe when the night factor or wetness changed by more than this since its last render")]
        public float RefreshDelta = 0.08f;
        [Tooltip("Only probes within this distance of the camera render (the others wait until the camera comes near)")]
        public float ActiveRadius = 900f;
        [Tooltip("Seconds between the start of two probe renders (each render takes 6 frames, one face per frame)")]
        public float MinInterval = 0.3f;

        float[] seenNight, seenWet;
        int current = -1, renderId;
        float nextStart;
        bool supported;

        /// <summary>Probe renders started since load (stats / tests).</summary>
        public static int Renders { get; private set; }

        void OnEnable()
        {
            supported = QualitySettings.realtimeReflectionProbes;
            string q = QualitySettings.names[QualitySettings.GetQualityLevel()];
            int res = q.Contains("Ultra") ? 256 : 128;
            foreach (var p in Probes)
            {
                if (p == null) continue;
                p.enabled = supported;
                p.resolution = res;
            }
            seenNight = new float[Probes.Count];
            seenWet = new float[Probes.Count];
            for (int i = 0; i < seenNight.Length; i++) seenNight[i] = -1f;   // never rendered
            CellStreamer.CellLoaded += OnCellLoaded;
        }

        void OnDisable() => CellStreamer.CellLoaded -= OnCellLoaded;

        /// <summary>A streamed 1 km cell arrived (A2): probes within 1 km of its centre are marked stale (they re-render when
        /// their turn comes, near the camera first), so they reflect its buildings.</summary>
        void OnCellLoaded(string cell, Scene scene)
        {
            if (seenNight == null || string.IsNullOrEmpty(cell) || cell.Length < 2) return;
            int col = "ABCDEFGH".IndexOf(cell[0]);
            if (col < 0 || !int.TryParse(cell.Substring(1), out int row)) return;
            // Grid (CLAUDE.md): A1 = data x 0-1000, y 5000-6000; Unity = (x - 4000, z, y - 3000).
            var centre = new Vector3(col * 1000f + 500f - 4000f, 0f, 6000f - row * 1000f + 500f - 3000f);
            for (int i = 0; i < Probes.Count; i++)
            {
                if (Probes[i] == null) continue;
                var p = Probes[i].transform.position;
                if (new Vector2(p.x - centre.x, p.z - centre.z).sqrMagnitude < 1000f * 1000f) seenNight[i] = -1f;
            }
        }

        void Update()
        {
            if (!supported || !Application.isPlaying || Probes.Count == 0) return;
            if (current >= 0)
            {
                var cp = Probes[current];
                if (cp != null && !cp.IsFinishedRendering(renderId)) return;
                current = -1;
            }
            if (Time.unscaledTime < nextStart) return;
            var env = WorldEnvironment.Instance;
            float night = env != null ? env.NightFactor : 0f, wet = env != null ? env.Wetness : 0f;
            // Nearest stale probe first (the camera sees it soonest); far probes wait.
            var cam = WorldEnvironment.ViewOrMain;
            if (cam == null) return;
            Vector3 eye = cam.transform.position;
            float r2 = ActiveRadius * ActiveRadius;
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < Probes.Count; i++)
            {
                var p = Probes[i];
                if (p == null) continue;
                bool stale = seenNight[i] < 0f || Mathf.Abs(seenNight[i] - night) > RefreshDelta || Mathf.Abs(seenWet[i] - wet) > RefreshDelta;
                if (!stale) continue;
                float d = (p.transform.position - eye).sqrMagnitude;
                if (d < bestD && d < r2) { bestD = d; best = i; }
            }
            if (best < 0) return;
            seenNight[best] = night;
            seenWet[best] = wet;
            current = best;
            nextStart = Time.unscaledTime + MinInterval;
            renderId = Probes[best].RenderProbe();
            Renders++;
        }

        /// <summary>Review captures: render every probe near the camera now (all faces at once), so a shot taken right after a
        /// time or weather change shows matching reflections.</summary>
        public void RenderNearNow(Vector3 eye)
        {
            if (!supported || seenNight == null) return;
            var env = WorldEnvironment.Instance;
            float night = env != null ? env.NightFactor : 0f, wet = env != null ? env.Wetness : 0f;
            float r2 = ActiveRadius * ActiveRadius;
            for (int i = 0; i < Probes.Count; i++)
            {
                var p = Probes[i];
                if (p == null || (p.transform.position - eye).sqrMagnitude > r2) continue;
                var slicing = p.timeSlicingMode;
                p.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
                p.RenderProbe();
                p.timeSlicingMode = slicing;
                seenNight[i] = night;
                seenWet[i] = wet;
                Renders++;
            }
            current = -1;
        }
    }
}
