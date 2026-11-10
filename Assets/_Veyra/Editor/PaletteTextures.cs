using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card F2: procedural, tileable PBR texture sets for the material palette (no downloads; CC0 sources can replace them
    /// later, see Docs/ASSET_SOURCES.md). Each set is an albedo (sRGB), a normal map (from a height field) and a mask
    /// (linear RGBA: R metallic, G occlusion, B unused, A smoothness = 1 - roughness, URP "Metallic Alpha" packing).
    /// Samplers get (u, v) in [0, 1) over one texture tile and must be thread-safe (rows are generated in parallel).
    /// Albedo values are chosen in linear space and calibrated to card F2's ranges (asphalt 0.05–0.12, concrete 0.25–0.40,
    /// brick 0.15–0.30, never below 0.02 or above 0.9).
    /// </summary>
    public static class PaletteTextures
    {
        public const string Dir = "Assets/_Veyra/Art/Textures/Palette";

        public struct Sample
        {
            public Color Albedo;   // linear
            public float Height;   // 0–1
            public float Smooth;   // 0–1
            public float AO;       // 0–1
            public float Metal;    // 0–1
            public float Alpha;    // albedo alpha (detail maps)
        }

        public sealed class Set
        {
            public Texture2D Albedo, Normal, Mask;
        }

        // ------------------------------------------------------------------ noise (tileable, pure)

        public static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        static int Wrap(int i, int p) => ((i % p) + p) % p;

        /// <summary>Value noise with period <paramref name="p"/> cells over u, v in [0, 1).</summary>
        public static float Noise(float u, float v, int p, int seed)
        {
            float x = u * p, y = v * p;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int X0 = Wrap(x0, p), Y0 = Wrap(y0, p), X1 = Wrap(x0 + 1, p), Y1 = Wrap(y0 + 1, p);
            float a = Hash(X0, Y0, seed), b = Hash(X1, Y0, seed), c = Hash(X0, Y1, seed), d = Hash(X1, Y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>Value noise with separate periods along u and v (streaks, broom lines, wood grain).</summary>
        public static float NoiseXY(float u, float v, int px, int py, int seed)
        {
            float x = u * px, y = v * py;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int X0 = Wrap(x0, px), Y0 = Wrap(y0, py), X1 = Wrap(x0 + 1, px), Y1 = Wrap(y0 + 1, py);
            float a = Hash(X0, Y0, seed), b = Hash(X1, Y0, seed), c = Hash(X0, Y1, seed), d = Hash(X1, Y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        public static float Fbm(float u, float v, int p, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += Noise(u, v, p, seed + o * 101) * amp;
                norm += amp;
                amp *= 0.5f;
                p *= 2;
            }
            return sum / norm;
        }

        /// <summary>Tileable Voronoi with <paramref name="p"/> x <paramref name="p"/> jittered cells: F1, F2 (in cell units) and the nearest cell's random id.</summary>
        public static void Voronoi(float u, float v, int p, int seed, float jitter, out float f1, out float f2, out float id)
        {
            float x = u * p, y = v * p;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            f1 = 9f; f2 = 9f; id = 0f;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int gx = cx + i, gy = cy + j;
                    int wx = Wrap(gx, p), wy = Wrap(gy, p);
                    float px = gx + 0.5f + (Hash(wx, wy, seed) - 0.5f) * jitter;
                    float py = gy + 0.5f + (Hash(wx, wy, seed + 7) - 0.5f) * jitter;
                    float dx = px - x, dy = py - y;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d < f1) { f2 = f1; f1 = d; id = Hash(wx, wy, seed + 13); }
                    else if (d < f2) f2 = d;
                }
        }

        public static Color Lin(float r, float g, float b) => new Color(r, g, b, 1f);
        public static Color Grey(float l) => new Color(l, l, l, 1f);
        public static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        // ------------------------------------------------------------------ set writer

        /// <summary>Generates and imports one texture set: <name>_A (sRGB), <name>_N (normal map), <name>_M (linear mask).</summary>
        public static Set Make(string name, int size, float normalStrength, Func<float, float, Sample> fn, bool albedoAlpha = false)
        {
            Directory.CreateDirectory(Dir);
            int n = size * size;
            var alb = new Color32[n];
            var mask = new Color32[n];
            var height = new float[n];
            Parallel.For(0, size, y =>
            {
                float v = (y + 0.5f) / size;
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size;
                    var s = fn(u, v);
                    int i = y * size + x;
                    var a = s.Albedo;
                    alb[i] = new Color32(ToSrgb8(a.r), ToSrgb8(a.g), ToSrgb8(a.b), albedoAlpha ? (byte)Mathf.RoundToInt(Mathf.Clamp01(s.Alpha) * 255f) : (byte)255);
                    mask[i] = new Color32(To8(s.Metal), To8(s.AO), 0, To8(s.Smooth));
                    height[i] = s.Height;
                }
            });
            var nrm = new Color32[n];
            float k = normalStrength * size / 512f;
            Parallel.For(0, size, y =>
            {
                int yd = (y + size - 1) % size, yu = (y + 1) % size;
                for (int x = 0; x < size; x++)
                {
                    int xl = (x + size - 1) % size, xr = (x + 1) % size;
                    float dx = (height[y * size + xr] - height[y * size + xl]) * k;
                    float dy = (height[yu * size + x] - height[yd * size + x]) * k;
                    var nv = new Vector3(-dx, -dy, 1f).normalized;
                    nrm[y * size + x] = new Color32(To8(nv.x * 0.5f + 0.5f), To8(nv.y * 0.5f + 0.5f), To8(nv.z * 0.5f + 0.5f), 255);
                }
            });
            return new Set
            {
                Albedo = Write($"{name}_A", size, alb, Kind.Albedo),
                Normal = Write($"{name}_N", size, nrm, Kind.Normal),
                Mask = Write($"{name}_M", size, mask, Kind.Mask),
            };
        }

        /// <summary>A single sRGB colour texture (interior atlas, LED screen pattern, overlays).</summary>
        public static Texture2D MakeColor(string name, int size, Func<float, float, Color> fn, bool withAlpha = false)
        {
            Directory.CreateDirectory(Dir);
            var px = new Color32[size * size];
            Parallel.For(0, size, y =>
            {
                float v = (y + 0.5f) / size;
                for (int x = 0; x < size; x++)
                {
                    var c = fn((x + 0.5f) / size, v);
                    px[y * size + x] = new Color32(ToSrgb8(c.r), ToSrgb8(c.g), ToSrgb8(c.b), withAlpha ? To8(c.a) : (byte)255);
                }
            });
            return Write(name, size, px, Kind.Albedo);
        }

        static byte To8(float f) => (byte)Mathf.RoundToInt(Mathf.Clamp01(f) * 255f);
        static byte ToSrgb8(float lin) => To8(Mathf.LinearToGammaSpace(Mathf.Clamp01(lin)));

        enum Kind { Albedo, Normal, Mask }

        static Texture2D Write(string name, int size, Color32[] px, Kind kind)
        {
            string path = $"{Dir}/{name}.png";
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, kind != Kind.Albedo);
            tex.SetPixels32(px);
            tex.Apply(false);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            bool changed = false;
            void Set<T>(T current, T wanted, Action<T> apply) { if (!Equals(current, wanted)) { apply(wanted); changed = true; } }
            Set(imp.textureType, kind == Kind.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default, t => imp.textureType = t);
            Set(imp.sRGBTexture, kind == Kind.Albedo, b => imp.sRGBTexture = b);
            Set(imp.mipmapEnabled, true, b => imp.mipmapEnabled = b);
            Set(imp.wrapMode, TextureWrapMode.Repeat, w => imp.wrapMode = w);
            Set(imp.anisoLevel, 8, a => imp.anisoLevel = a);
            Set(imp.maxTextureSize, 2048, m => imp.maxTextureSize = m);
            Set(imp.alphaSource, TextureImporterAlphaSource.FromInput, a => imp.alphaSource = a);
            var pc = imp.GetPlatformTextureSettings("Standalone");
            if (!pc.overridden || pc.format != TextureImporterFormat.BC7 || pc.maxTextureSize != 2048)
            {
                pc.overridden = true;
                pc.format = TextureImporterFormat.BC7;
                pc.maxTextureSize = 2048;
                imp.SetPlatformTextureSettings(pc);
                changed = true;
            }
            if (changed) imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
