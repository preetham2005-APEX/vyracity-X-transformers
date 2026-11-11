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

        /// <summary>
        /// Iteration 3: 1024 px tileable albedo per layer with its own character (the 256 px single-noise tints blurred up close,
        /// review CAM_14): grass with blade speckle and clover patches, forest floor with needles and twigs, rock with strata
        /// and cracks, sand with ripples, paving with slab joints, dirt with pebbles, farmland with furrows, lawn finer and
        /// greener. Colours stay the layer's calibrated base colour (Appendix B albedo ranges).
        /// </summary>
        static Texture2D Albedo(string layer, Color baseCol)
        {
            const int S = 1024;
            int seed = layer.GetHashCode() & 0xffff;
            var px = new Color[S * S];
            var rng = new System.Random(seed);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (float)S, v = y / (float)S;
                    float big = ProcTextures.Fbm(u, v, 4, 3, seed);
                    float mid = ProcTextures.Fbm(u, v, 16, 3, seed + 7);
                    float fine = ProcTextures.Fbm(u, v, 128, 2, seed + 13);
                    float g = 1f;
                    var c = baseCol;
                    if (layer.Contains("Grass") || layer.Contains("Lawn"))
                    {
                        g = 0.78f + 0.3f * big + 0.25f * (fine - 0.5f) * 2f;
                        float dry = Mathf.Clamp01((mid - 0.58f) * 4f) * (layer.Contains("Lawn") ? 0.25f : 0.6f);
                        c = Color.Lerp(baseCol, new Color(baseCol.r * 1.55f, baseCol.g * 1.25f, baseCol.b * 0.8f), dry);
                    }
                    else if (layer.Contains("Forest"))
                    {
                        g = 0.7f + 0.35f * mid + 0.3f * (fine - 0.5f);
                        c = Color.Lerp(baseCol, new Color(0.24f, 0.18f, 0.1f), Mathf.Clamp01((big - 0.5f) * 2f));
                    }
                    else if (layer.Contains("Rock"))
                    {
                        // iteration 3: softer strata and fewer cracks (iteration 2 review CAM_14: cliffs read like contour lines)
                        float strata = 0.5f + 0.5f * Mathf.Sin((v + 0.35f * big + 0.08f * mid) * Mathf.PI * 2f * 5f);
                        g = 0.74f + 0.08f * strata + 0.28f * (fine - 0.5f) + 0.22f * (mid - 0.5f) + 0.15f * big;
                        float crack = Mathf.Abs(ProcTextures.Fbm(u, v, 12, 2, seed + 31) - 0.5f);
                        if (crack < 0.004f) g *= 0.75f;
                    }
                    else if (layer.Contains("Sand"))
                    {
                        float ripple = 0.5f + 0.5f * Mathf.Sin((u * 40f + v * 9f + big * 6f) * Mathf.PI * 2f / 4f);
                        g = 0.9f + 0.06f * ripple + 0.12f * (fine - 0.5f) + 0.1f * (big - 0.5f);
                    }
                    else if (layer.Contains("Paving"))
                    {
                        float jx = Mathf.Repeat(u * 8f, 1f), jy = Mathf.Repeat(v * 8f + (Mathf.Floor(u * 8f) % 2) * 0.5f, 1f);
                        bool joint = jx < 0.015f || jy < 0.015f;
                        g = (joint ? 0.62f : 0.95f) + 0.12f * (mid - 0.5f) + 0.1f * (fine - 0.5f);
                    }
                    else if (layer.Contains("Dirt"))
                    {
                        g = 0.78f + 0.25f * mid + 0.3f * (fine - 0.5f);
                    }
                    else if (layer.Contains("Farm"))
                    {
                        float furrow = 0.5f + 0.5f * Mathf.Sin(u * Mathf.PI * 2f * 32f);
                        g = 0.75f + 0.18f * furrow + 0.2f * (mid - 0.5f) + 0.15f * (fine - 0.5f);
                    }
                    px[y * S + x] = new Color(Mathf.Clamp01(c.r * g), Mathf.Clamp01(c.g * g), Mathf.Clamp01(c.b * g), 1f);
                }
            // scattered detail: pebbles (dirt, sand, rock), twigs and needles (forest), clover (lawn/grass)
            int count = layer.Contains("Dirt") || layer.Contains("Rock") ? 2600 : layer.Contains("Forest") ? 3200 : layer.Contains("Grass") || layer.Contains("Lawn") ? 1800 : layer.Contains("Sand") ? 900 : 0;
            for (int i = 0; i < count; i++)
            {
                int cx = rng.Next(S), cy = rng.Next(S), r = 1 + rng.Next(layer.Contains("Rock") ? 4 : 3);
                float tone = 0.65f + (float)rng.NextDouble() * 0.6f;
                bool stroke = layer.Contains("Forest");
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = stroke ? -r * 3 : -r; dx <= (stroke ? r * 3 : r); dx++)
                    {
                        if (!stroke && dx * dx + dy * dy > r * r) continue;
                        int xx = (cx + dx + S) % S, yy = (cy + dy + S) % S;
                        var o = px[yy * S + xx];
                        px[yy * S + xx] = new Color(o.r * tone, o.g * tone, o.b * tone, 1f);
                    }
            }
            string path = $"{OutDir}/T_{layer}_A.png";
            var t = new Texture2D(S, S, TextureFormat.RGBA32, true);
            t.SetPixels(px); t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Repeat;
                ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.anisoLevel = 8; ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static readonly System.Collections.Generic.Dictionary<string, Color> BaseColours = new System.Collections.Generic.Dictionary<string, Color>
        {
            ["L_Grass"] = new Color(0.27f, 0.33f, 0.16f), ["L_ForestFloor"] = new Color(0.18f, 0.16f, 0.11f), ["L_Rock"] = new Color(0.4f, 0.39f, 0.37f),
            ["L_Sand"] = new Color(0.72f, 0.65f, 0.5f), ["L_Paving"] = new Color(0.42f, 0.42f, 0.41f), ["L_Dirt"] = new Color(0.36f, 0.29f, 0.21f),
            ["L_Farmland"] = new Color(0.38f, 0.33f, 0.2f), ["L_Lawn"] = new Color(0.24f, 0.36f, 0.15f),
        };

        public static string Run()
        {
            var sb = new StringBuilder("Terrain layer PBR maps\n");
            Directory.CreateDirectory(OutDir);
            foreach (var path in Directory.GetFiles(LayerDir, "*.terrainlayer").Select(p => p.Replace(Path.DirectorySeparatorChar, '/')))
            {
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (layer == null || !BaseColours.TryGetValue(layer.name, out var bc)) continue;
                layer.diffuseTexture = Albedo(layer.name, bc);   // the normal/mask maps below derive from this albedo
                EditorUtility.SetDirty(layer);
            }
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
