using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VeyraCity.World
{
    /// <summary>
    /// BUG-170: reflection-probe renders (Camera.RenderToCubemap, CameraType.Reflection) drew the terrain grass, so every
    /// probe face rebuilt the detail patches around the probe's own viewpoint (Terrain.Details.Render ->
    /// DetailMesh.MeshGenerationJob 75-120 ms, the whole 100-130 ms "TickRealtimeProbes" spikes in the profiled R_SMOKE).
    /// While a reflection camera renders, every active terrain's trees and foliage are switched off and restored right
    /// after; reflections lose only the grass blades. Installed once at start-up, no scene object.
    /// </summary>
    public static class ReflectionGrassGuard
    {
        static readonly List<Terrain> off = new List<Terrain>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            RenderPipelineManager.beginCameraRendering -= Begin;
            RenderPipelineManager.endCameraRendering -= End;
            RenderPipelineManager.beginCameraRendering += Begin;
            RenderPipelineManager.endCameraRendering += End;
        }

        static void Begin(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == null || cam.cameraType != CameraType.Reflection) return;
            foreach (var t in Terrain.activeTerrains)
                if (t != null && t.drawTreesAndFoliage) { t.drawTreesAndFoliage = false; off.Add(t); }
        }

        static void End(ScriptableRenderContext ctx, Camera cam)
        {
            if (off.Count == 0) return;
            foreach (var t in off) if (t != null) t.drawTreesAndFoliage = true;
            off.Clear();
        }
    }
}
