using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// GTA V's player-car trick: a real-time reflection probe that rides with the player car and is re-rendered one
    /// cube face per frame, so the clear-coat paint and glass reflect the actual street (buildings, neon, sky) instead
    /// of the skybox. The car itself is on <see cref="CarLayer"/>, which the probe does not render (no self-reflection
    /// from inside the body). Only the player car gets one: the cost is one low-resolution face per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CarReflectionProbe : MonoBehaviour
    {
        /// <summary>Layer the player car's renderers use so the probe skips them (wheel raycasts ignore the car anyway).</summary>
        public const int CarLayer = 30;
        public int Resolution = 256;   // iteration 8: crisper street reflections in the paint (one face per frame)
        // iteration 29: 360 m box (was 70): the street the camera sees reflects the real surroundings instead of the
        // editor-baked daytime sky (wet night roads beyond 35 m read as snow)
        public float Range = 360f;
        // BUG-170 (D2): 200 m (was 300) and a new cycle at most every 0.4 s (was back to back): the probe rendered one face of
        // the city every frame forever; now it is busy about 9 frames out of every ~25 at 60 FPS
        public float FarClip = 200f;
        public float MinCycleSeconds = 0.4f;
        // BUG-170 (D1 / coordinator): a new full cube only when the view can have changed: the car moved 12 m or turned 20 deg
        // since the last cube, or the light or wetness changed by 0.05; nothing while the car stands still. One face per frame
        // (IndividualFaces) as before
        public float RefreshDistance = 12f, RefreshTurnDeg = 20f, RefreshEnvDelta = 0.05f;
        float lastCycle = -10f, lastNight = -1f, lastWet = -1f, lastYaw;
        Vector3 lastPos = new Vector3(1e9f, 0f, 0f);

        ReflectionProbe probe;
        /// <summary>Refresh cycles started (for the run reports).</summary>
        public static int Cycles { get; private set; }
        int renderId = -1;

        public void Bind(VehicleController car)
        {
            foreach (var r in car.GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer) && !(r is TrailRenderer)) r.gameObject.layer = CarLayer;
            var go = new GameObject("CarReflectionProbe");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.resolution = Resolution;
            probe.hdr = true;
            probe.size = new Vector3(Range, Range * 0.5f, Range);
            probe.boxProjection = false;
            probe.importance = 100;               // wins over any baked city probe around the car
            probe.blendDistance = 1f;
            probe.farClipPlane = FarClip;
            probe.nearClipPlane = 0.3f;
            probe.shadowDistance = 0f;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.cullingMask = ~(1 << CarLayer);
            foreach (var r in car.GetComponentsInChildren<Renderer>(true))
                r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
        }

        void LateUpdate()
        {
            long fc0 = VeyraCity.Core.FrameCost.Start();
            LateUpdateBody();
            VeyraCity.Core.FrameCost.Stop("CarProbe", fc0);
        }

        void LateUpdateBody()
        {
            if (probe == null) return;
            // Start the next time-sliced refresh once the previous one has finished and MinCycleSeconds have passed.
            if (renderId >= 0 && !probe.IsFinishedRendering(renderId)) return;
            float since = Time.unscaledTime - lastCycle;
            if (since < MinCycleSeconds) return;
            var env = VeyraCity.World.WorldEnvironment.Instance;
            float night = env != null ? env.NightFactor : 0f, wet = env != null ? env.Wetness : 0f;
            float yaw = transform.eulerAngles.y;
            bool moved = (transform.position - lastPos).sqrMagnitude > RefreshDistance * RefreshDistance
                         || Mathf.Abs(Mathf.DeltaAngle(yaw, lastYaw)) > RefreshTurnDeg;
            bool envChanged = Mathf.Abs(night - lastNight) > RefreshEnvDelta || Mathf.Abs(wet - lastWet) > RefreshEnvDelta;
            if (!moved && !envChanged) return;
            lastCycle = Time.unscaledTime;
            lastPos = transform.position;
            lastYaw = yaw;
            lastNight = night; lastWet = wet;
            renderId = probe.RenderProbe();
            Cycles++;
            VeyraCity.Core.FrameCost.Count("CarProbe.cycles");
        }
    }
}
