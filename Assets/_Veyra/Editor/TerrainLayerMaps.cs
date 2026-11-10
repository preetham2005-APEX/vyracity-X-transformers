using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Phase B (W2): "Veyra/World/Terrain Layer PBR Maps". Gives the 8 terrain layers (World/Generated/Terrain/L_*.terrainlayer)
    /// a normal map and a mask map derived from their own albedo, so the ground has relief and per-pixel roughness instead of
    /// flat colour: height = blurred luminance; normal = Sobel of the height (strength per layer: rock strongest, lawn
    /// weakest); mask map R metallic 0, G ambient occlusion from cavities, B height (for height blending), A smoothness
    /// variation (remapped around the layer's own smoothness). Textures: Assets/_Veyra/Art/Terrain/T_&lt;layer&gt;_N.png
    /// (Normal Map, linear) and _M.png (linear), power of two, BC7 / BC5. Enables _NORMALMAP and _MASKMAP on the terrain
    /// material(s). Re-runnable: textures are overwritten in place (same GUIDs).
    /// </summary>
    public static class TerrainLayerMaps
    {
        const string LayerDir = "Assets/_Veyra/World/Generated/Terrain";
        const string OutDir = "Assets/_Veyra/Art/Terrain";

        static float Strength(string n) => n.Contains("Rock") ? 3.2f : n.Contains("Forest") || n.Contains("Dirt") ? 2.2f :
                                           n.Contains("Farm") ? 1.8f : n.Contains("Paving") ? 1.4f : n.Contains("Sand") ? 1.0f : 1.2f;

        [MenuItem("Veyra/World/Terrain Layer PBR Maps")]
        public static void Menu() => Debug.Log(Run());

        public static string Run()
        {
            var sb = new StringBuilder("Terrain layer PBR maps\n");
            Directory.CreateDirectory(OutDir);
            foreach (var path in Directory.GetFiles(LayerDir, "*.terrainlayer").Select(p => p.Replace('\\', '/')))
            {
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (layer == null || layer.diffuseTexture == null) continue;
                string src = AssetDatabase.GetAssetPath(layer.diffuseTexture);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                if (!File.Exists(src) || !tex.LoadImage(File.ReadAllBytes(src))) { sb.AppendLine($"  {layer.name}: albedo not readable ({src})"); continue; }
                int w = tex.width, h = tex.height;
                var px = tex.GetPixels();
                var height = new float[w * h];
                for (int i = 0; i < px.Length; i++) height[i] = px[i].r * 0.3f + px[i].g * 0.59f + px[i].b * 0.11f;
                height = Blur(height, w, h, 2);
                float min = height.Min(), max = height.Max(), range = Mathf.Max(1e-4f, max - min);
                for (int i = 0; i < height.Length; i++) height[i] = (height[i] - min) / range;
                var blurred = Blur(height, w, h, 6);
                float k = Strength(layer.name);
                var normal = new Color[w * h];
                var mask = new Color[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float H(int dx, int dy) => height[((y + dy + h) % h) * w + (x + dx + w) % w];
                        float gx = (H(1, -1) + 2f * H(1, 0) + H(1, 1)) - (H(-1, -1) + 2f * H(-1, 0) + H(-1, 1));
                        float gy = (H(-1, 1) + 2f * H(0, 1) + H(1, 1)) - (H(-1, -1) + 2f * H(0, -1) + H(1, -1));
                        var n = new Vector3(-gx * k, -gy * k, 1f).normalized;
                        normal[y * w + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                        int i = y * w + x;
                        float cavity = Mathf.Clamp01(1f - (blurred[i] - height[i]) * 3f);
                        float smooth = Mathf.Clamp01(0.5f + (height[i] - 0.5f) * 0.6f);
                        mask[i] = new Color(0f, Mathf.Lerp(0.55f, 1f, cavity), height[i], smooth);
                    }
                string nPath = $"{OutDir}/T_{layer.name}_N.png", mPath = $"{OutDir}/T_{layer.name}_M.png";
                Write(nPath, normal, w, h);
                Write(mPath, mask, w, h);
                Object.DestroyImmediate(tex);
                Import(nPath, true);
                Import(mPath, false);
                layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(nPath);
                layer.maskMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(mPath);
                layer.normalScale = 1f;
                float s0 = Mathf.Max(0f, layer.smoothness - 0.08f), s1 = Mathf.Min(1f, layer.smoothness + 0.08f);
                layer.maskMapRemapMin = new Vector4(0f, 0f, 0f, s0);
                layer.maskMapRemapMax = new Vector4(0f, 1f, 1f, s1);
                EditorUtility.SetDirty(layer);
                sb.AppendLine($"  {layer.name}: {w}x{h}, normal strength {k:0.0}, smoothness {s0:0.00}-{s1:0.00}");
            }
            int mats = 0;
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
            {
                var m = t.materialTemplate;
                if (m == null || !EditorUtility.IsPersistent(m)) continue;
                m.EnableKeyword("_NORMALMAP");
                m.EnableKeyword("_MASKMAP");
                EditorUtility.SetDirty(m);
                mats++;
            }
            AssetDatabase.SaveAssets();
            sb.AppendLine($"terrain materials with _NORMALMAP/_MASKMAP: {mats} terrain(s) checked");
            return sb.ToString();
        }

        static float[] Blur(float[] src, int w, int h, int r)
        {
            var tmp = new float[src.Length];
            var dst = new float[src.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float s = 0f;
                    for (int d = -r; d <= r; d++) s += src[y * w + (x + d + w) % w];
                    tmp[y * w + x] = s / (2 * r + 1);
                }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float s = 0f;
                    for (int d = -r; d <= r; d++) s += tmp[((y + d + h) % h) * w + x];
                    dst[y * w + x] = s / (2 * r + 1);
                }
            return dst;
        }

        static void Write(string path, Color[] px, int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true, true);
            t.SetPixels(px);
            t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
        }

        static void Import(string path, bool normal)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = false;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }
    }
}
