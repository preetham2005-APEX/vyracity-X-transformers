using UnityEngine;
using VeyraCity.World;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Visual iteration 20: the terrain height over the whole city as a global shader texture (_VC_GroundHeight, 10 m per
    /// texel, bilinear) with its world rectangle (_VC_GroundHeightRect: x/z min, 1/width, 1/depth). Building UVs are
    /// absolute world height (CityBuilder: v = y / 14), while downtown stands 4-43 m above zero, so the facade shader
    /// needs the ground under each wall to put shopfronts and street-level grime at the pavement. Baked once at start
    /// from the terrain tiles; shaders fall back to y = 0 while the rectangle is unset.
    /// </summary>
    public sealed class GroundHeightField : MonoBehaviour
    {
        const int W = 768, H = 576;
        static readonly int TexId = Shader.PropertyToID("_VC_GroundHeight");
        static readonly int RectId = Shader.PropertyToID("_VC_GroundHeightRect");
        Texture2D tex;

        public static float BakeMs { get; private set; }

        void Start()
        {
            float t0 = Time.realtimeSinceStartup;
            var b = WorldData.Bounds;
            var terrains = Terrain.activeTerrains;
            if (terrains == null || terrains.Length == 0 || b.width <= 0f) return;
            var h = new float[W * H];
            Terrain last = null;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var p = new Vector3(b.xMin + (x + 0.5f) / W * b.width, 0f, b.yMin + (y + 0.5f) / H * b.height);
                    float v = 0f;
                    for (int k = -1; k < terrains.Length; k++)
                    {
                        var t = k < 0 ? last : terrains[k];
                        if (t == null || t.terrainData == null) continue;
                        var tp = t.GetPosition();
                        var sz = t.terrainData.size;
                        if (p.x < tp.x || p.z < tp.z || p.x > tp.x + sz.x || p.z > tp.z + sz.z) continue;
                        v = t.SampleHeight(p) + tp.y;
                        last = t;
                        break;
                    }
                    h[y * W + x] = v;
                }
            tex = new Texture2D(W, H, TextureFormat.RFloat, false, true) { name = "VC_GroundHeight", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixelData(h, 0);
            tex.Apply(false, true);
            Shader.SetGlobalTexture(TexId, tex);
            // texel centres line up with the samples above
            Shader.SetGlobalVector(RectId, new Vector4(b.xMin, b.yMin, 1f / b.width, 1f / b.height));
            BakeMs = (Time.realtimeSinceStartup - t0) * 1000f;
        }

        void OnDestroy()
        {
            Shader.SetGlobalVector(RectId, Vector4.zero);
            if (tex != null) Destroy(tex);
        }
    }
}
