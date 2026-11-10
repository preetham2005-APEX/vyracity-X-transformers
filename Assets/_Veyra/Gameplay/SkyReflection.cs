using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.World;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Visual iterations 25-28: the scene's environment reflection was the skybox reflection generated in the editor, a
    /// daytime sky that never changed, so wet roads and pavements outside the car's own probe mirrored a white noon sky at
    /// night (rain night read as snow). Iteration 25 set a sky-only probe as the custom default reflection and iteration 27
    /// regenerated the default with DynamicGI.UpdateEnvironment; neither reached URP's Forward+ reflection path. This
    /// version uses the path the car's probe already proves: one realtime probe whose box covers the whole city (low
    /// importance, so the car's probe still wins near the car), rendering only the sky (128 px, all faces in one frame)
    /// whenever the hour moves by 0.05 h or the cloud cover by 5 %, at most once a second.
    /// </summary>
    public sealed class SkyReflection : MonoBehaviour
    {
        ReflectionProbe probe;
        float lastHour = -99f, lastCloud = -1f, timer;
        int framesSince = 99;
        bool assigned;
        public static int Renders { get; private set; }

        void Start()
        {
            var b = WorldData.Bounds;
            var go = new GameObject("SkyReflectionProbe");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(b.center.x, 200f, b.center.y);
            probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.resolution = 128;
            probe.hdr = true;
            probe.cullingMask = 1 << 31;   // an unused layer: the sky only (iteration 29: a mask of 0 never rendered under URP)
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.size = new Vector3(b.width + 4000f, 3000f, b.height + 4000f);   // the whole city and its approaches
            probe.boxProjection = false;
            probe.importance = 0;                                               // the car's probe (100) wins near the car
            probe.blendDistance = 0f;
        }

        void OnDestroy()
        {
            if (assigned && probe != null && RenderSettings.customReflectionTexture == probe.realtimeTexture)
            {
                RenderSettings.customReflectionTexture = null;
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            }
        }

        void Update()
        {
            var env = WorldEnvironment.Instance;
            if (probe == null || env == null) return;
            timer -= Time.unscaledDeltaTime;
            // iteration 31: also the scene's default reflection (what URP falls back to outside every probe box, e.g. the
            // river far from the car); iteration 25 tried this with a probe that never rendered (culling mask 0)
            if (++framesSince == 3 && probe.realtimeTexture != null && !assigned)
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = probe.realtimeTexture;
                assigned = true;
            }
            float hour = env.TimeOfDay, cloud = env.Cloudiness;
            float dh = Mathf.Abs(Mathf.DeltaAngle(hour * 15f, lastHour * 15f)) / 15f;
            if (timer > 0f || (dh <= 0.05f && Mathf.Abs(cloud - lastCloud) <= 0.05f)) return;
            lastHour = hour;
            lastCloud = cloud;
            timer = 1f;
            probe.RenderProbe();
            framesSince = 0;
            Renders++;
        }
    }
}
