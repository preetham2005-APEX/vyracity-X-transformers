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
        public int Resolution = 128;
        public float Range = 70f;
        public float FarClip = 300f;

        ReflectionProbe probe;
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
            if (probe == null) return;
            // Start the next time-sliced refresh as soon as the previous one has finished (about every 9 frames).
            if (renderId < 0 || probe.IsFinishedRendering(renderId)) renderId = probe.RenderProbe();
        }
    }
}
