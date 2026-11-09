using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Procedural, tileable PBR textures for the city (no third-party textures): asphalt, paving, facades
    /// with window grids and lit-window emission maps, roofs, terrain layers and water normals.
    /// Saved as PNG assets so they are versioned and importable.
    /// </summary>
    public static class ProcTextures
    {
        public const string Dir = "Assets/_Veyra/World/Generated/Textures";

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>Tileable value noise with period p.</summary>
        static float Noise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            int X0 = ((x0 % period) + period) % period, Y0 = ((y0 % period) + period) % period;
            int X1 = (X0 + 1) % period, Y1 = (Y0 + 1) % period;
            float a = Hash(X0, Y0, seed), b = Hash(X1, Y0, seed), c = Hash(X0, Y1, seed), d = Hash(X1, Y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        public static float Fbm(float u, float v, int baseFreq, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int f = baseFreq;
            for (int o = 0; o < octaves; o++)
            {
                sum += Noise(u * f, v * f, f, seed + o * 31) * amp;
                norm += amp;
                amp *= 0.5f;
                f *= 2;
            }
            return sum / norm;
        }

        static Texture2D Save(string name, int size, Func<int, int, Color> fn, bool normal = false, bool linear = false)
        {
            Directory.CreateDirectory(Dir);
            string path = $"{Dir}/{name}.png";
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, linear);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) px[y * size + x] = fn(x, y);
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = !normal && !linear;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.anisoLevel = 8;
            imp.mipmapEnabled = true;
            imp.maxTextureSize = 2048;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Normal map from a height function (tileable).</summary>
        static Texture2D NormalFrom(string name, int size, Func<float, float, float> height, float strength)
        {
            return Save(name, size, (x, y) =>
            {
                float u = x / (float)size, v = y / (float)size, e = 1f / size;
                float hL = height(u - e, v), hR = height(u + e, v), hD = height(u, v - e), hU = height(u, v + e);
                var n = new Vector3((hL - hR) * strength, (hD - hU) * strength, 1f).normalized;
                return new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }, true);
        }

        public static (Texture2D albedo, Texture2D normal) Asphalt(string name, Color tint, int seed, float wear)
        {
            Func<float, float, float> h = (u, v) => Fbm(u, v, 64, 3, seed) * 0.6f + (Hash((int)(u * 512), (int)(v * 512), seed + 7) > 0.93f ? 0.4f : 0f);
            var a = Save(name + "_A", 512, (x, y) =>
            {
                float u = x / 512f, v = y / 512f;
                float n = Fbm(u, v, 8, 4, seed) * 0.5f + Fbm(u, v, 64, 2, seed + 3) * 0.5f;
                float speck = Hash(x, y, seed + 11) > 0.95f ? 0.18f : 0f;
                float patches = Mathf.SmoothStep(0.45f, 0.75f, Fbm(u, v, 3, 3, seed + 5)) * wear;
                float g = Mathf.Lerp(0.75f, 1.15f, n) + speck - patches * 0.25f;
                return new Color(tint.r * g, tint.g * g, tint.b * g, 1f);
            });
            var nm = NormalFrom(name + "_N", 512, h, 3f);
            return (a, nm);
        }

        public static (Texture2D albedo, Texture2D normal) Paving(string name, Color tint, int seed, int tiles)
        {
            Func<float, float, float> grout = (u, v) =>
            {
                float gx = Mathf.Abs(Mathf.Repeat(u * tiles, 1f) - 0.5f), gy = Mathf.Abs(Mathf.Repeat(v * tiles, 1f) - 0.5f);
                return Mathf.Max(gx, gy) > 0.47f ? 0f : 1f;
            };
            var a = Save(name + "_A", 512, (x, y) =>
            {
                float u = x / 512f, v = y / 512f;
                float slab = Hash(Mathf.FloorToInt(u * tiles), Mathf.FloorToInt(v * tiles), seed);
                float n = Fbm(u, v, 32, 3, seed);
                float g = (0.82f + slab * 0.16f + n * 0.12f) * Mathf.Lerp(0.6f, 1f, grout(u, v));
                return new Color(tint.r * g, tint.g * g, tint.b * g, 1f);
            });
            var nm = NormalFrom(name + "_N", 512, (u, v) => grout(u, v) * 0.6f + Fbm(u, v, 64, 2, seed) * 0.2f, 2.5f);
            return (a, nm);
        }

        public static Texture2D Plain(string name, Color tint, int seed, int freq, float contrast)
        {
            return Save(name, 256, (x, y) =>
            {
                float u = x / 256f, v = y / 256f;
                float g = Mathf.Lerp(1f - contrast, 1f + contrast, Fbm(u, v, freq, 4, seed));
                return new Color(tint.r * g, tint.g * g, tint.b * g, 1f);
            });
        }

        /// <summary>
        /// Facade covering 4 x 4 window modules (12 m x 14 m). Returns albedo and an emission map with
        /// randomly lit windows (warm and cool) for night.
        /// </summary>
        public static (Texture2D albedo, Texture2D emission) Facade(string name, Color wall, Color glass, float windowW, float windowH, int seed, bool curtain, float litFraction)
        {
            const int S = 512, M = 4;
            float mod = S / (float)M;
            Color[] warm = { new Color(1f, 0.78f, 0.48f), new Color(1f, 0.86f, 0.62f), new Color(0.72f, 0.86f, 1f), new Color(1f, 0.93f, 0.8f) };
            bool Window(int x, int y, out int wx, out int wy, out float fx, out float fy)
            {
                wx = Mathf.FloorToInt(x / mod); wy = Mathf.FloorToInt(y / mod);
                fx = (x - wx * mod) / mod; fy = (y - wy * mod) / mod;
                float hw = windowW * 0.5f;
                return Mathf.Abs(fx - 0.5f) < hw && fy > 0.12f && fy < 0.12f + windowH;
            }
            var a = Save(name + "_A", S, (x, y) =>
            {
                float u = x / (float)S, v = y / (float)S;
                float n = Fbm(u, v, 16, 3, seed);
                if (curtain)
                {
                    float mullX = Mathf.Abs(Mathf.Repeat(u * M * 2f, 1f) - 0.5f), mullY = Mathf.Abs(Mathf.Repeat(v * M, 1f) - 0.5f);
                    bool mull = mullX > 0.46f || mullY > 0.47f;
                    if (mull) return wall * (0.9f + n * 0.2f);
                    float refl = Mathf.Lerp(0.85f, 1.2f, Fbm(u, v, 4, 2, seed + 9));
                    return glass * refl;
                }
                if (Window(x, y, out _, out _, out float fx, out float fy))
                {
                    float frame = Mathf.Min(Mathf.Abs(fx - 0.5f - windowW * 0.5f), Mathf.Abs(fx - 0.5f + windowW * 0.5f));
                    if (frame < 0.02f) return wall * 0.55f;
                    return glass * Mathf.Lerp(0.8f, 1.15f, n);
                }
                float sill = Mathf.Repeat(v * M, 1f);
                float band = sill > 0.06f && sill < 0.1f ? 0.8f : 1f;
                return wall * Mathf.Lerp(0.85f, 1.12f, n) * band;
            });
            var e = Save(name + "_E", S, (x, y) =>
            {
                float u = x / (float)S, v = y / (float)S;
                if (curtain)
                {
                    int cx = Mathf.FloorToInt(u * M * 2f), cy = Mathf.FloorToInt(v * M);
                    float mullX = Mathf.Abs(Mathf.Repeat(u * M * 2f, 1f) - 0.5f), mullY = Mathf.Abs(Mathf.Repeat(v * M, 1f) - 0.5f);
                    if (mullX > 0.46f || mullY > 0.47f) return Color.black;
                    float r = Hash(cx, cy, seed + 21);
                    if (r > litFraction) return Color.black;
                    return warm[Mathf.FloorToInt(Hash(cx, cy, seed + 22) * 4f) % 4] * (0.6f + 0.4f * Hash(cx, cy, seed + 23));
                }
                if (Window(x, y, out int wx, out int wy, out float fx, out float fy))
                {
                    float r = Hash(wx, wy, seed + 21);
                    if (r > litFraction) return Color.black;
                    float blind = Hash(wx, wy, seed + 24) > 0.7f ? Mathf.SmoothStep(0.3f, 0.6f, (fy - 0.12f) / windowH) : 1f;
                    return warm[Mathf.FloorToInt(Hash(wx, wy, seed + 22) * 4f) % 4] * (0.55f + 0.45f * Hash(wx, wy, seed + 23)) * blind;
                }
                return Color.black;
            });
            return (a, e);
        }

        public static Texture2D WaterNormal(string name)
        {
            return NormalFrom(name, 512, (u, v) => Fbm(u, v, 8, 4, 77) * 0.7f + Fbm(u + 0.3f, v, 32, 2, 79) * 0.3f, 4f);
        }

        public static Texture2D Corrugated(string name, Color tint, int seed)
        {
            return Save(name, 256, (x, y) =>
            {
                float u = x / 256f, v = y / 256f;
                float rib = 0.75f + 0.25f * Mathf.Sin(u * Mathf.PI * 2f * 24f);
                float n = Fbm(u, v, 16, 3, seed);
                float rust = Mathf.SmoothStep(0.62f, 0.8f, Fbm(u, v, 4, 4, seed + 3));
                var c = tint * rib * Mathf.Lerp(0.85f, 1.1f, n);
                return Color.Lerp(c, new Color(0.35f, 0.18f, 0.08f), rust * 0.5f);
            });
        }

        /// <summary>
        /// URP Lit metallic/smoothness map (Smoothness Source = Metallic Alpha): R = metallic, A = relative smoothness
        /// (0.7–1.0, multiplied by the material's _Smoothness so wetness can raise it to mirror-like).
        /// </summary>
        public static Texture2D MetalGloss(string name, int seed, float metallic, float variation, int freq)
        {
            return Save(name, 512, (x, y) =>
            {
                float u = x / 512f, v = y / 512f;
                float n = Fbm(u, v, freq, 4, seed);
                float a = Mathf.Clamp01(1f - variation + variation * n);
                return new Color(metallic, 0f, 0f, a);
            }, false, true);
        }

        public static (Texture2D albedo, Texture2D normal) Granite(string name, Color tint, int seed)
        {
            Func<float, float, float> h = (u, v) => Fbm(u, v, 32, 3, seed) * 0.5f + (Hash((int)(u * 512), (int)(v * 512), seed + 3) > 0.9f ? 0.3f : 0f);
            var a = Save(name + "_A", 512, (x, y) =>
            {
                float u = x / 512f, v = y / 512f;
                float speck = Hash(x, y, seed + 1);
                float g = 0.82f + Fbm(u, v, 16, 3, seed) * 0.2f;
                var c = tint * g;
                if (speck > 0.92f) c = Color.Lerp(c, Color.white * 0.9f, 0.5f);
                else if (speck < 0.08f) c *= 0.55f;
                c.a = 1f;
                return c;
            });
            return (a, NormalFrom(name + "_N", 512, h, 2f));
        }

        /// <summary>Ground-floor shopfronts: 4 glazed bays per texture (12 m) and one storey (4.5 m), lit at night, sign band on top.</summary>
        public static (Texture2D albedo, Texture2D emission) Storefront(string name, int seed)
        {
            const int S = 512;
            Color[] signs = { new Color(0f, 1f, 1f), new Color(1f, 0f, 0.5f), new Color(1f, 0.67f, 0f), new Color(0.6f, 0.4f, 1f) };
            var a = Save(name + "_A", S, (x, y) =>
            {
                float u = x / (float)S * 4f, v = y / (float)S;
                int bay = Mathf.FloorToInt(u);
                float fx = u - bay;
                bool frame = fx < 0.06f || fx > 0.94f || v < 0.05f;
                if (v > 0.82f) return new Color(0.06f, 0.06f, 0.07f); // sign band
                if (frame) return new Color(0.12f, 0.12f, 0.13f);
                if (v < 0.18f) return new Color(0.18f, 0.18f, 0.19f); // stall riser
                return new Color(0.08f, 0.1f, 0.12f) * (0.8f + 0.4f * Fbm(u * 0.25f, v, 4, 2, seed));
            });
            var e = Save(name + "_E", S, (x, y) =>
            {
                float u = x / (float)S * 4f, v = y / (float)S;
                int bay = Mathf.FloorToInt(u);
                float fx = u - bay;
                if (fx < 0.06f || fx > 0.94f || v < 0.18f) return Color.black;
                if (v > 0.84f && v < 0.95f && fx > 0.15f && fx < 0.85f)
                    return signs[Mathf.FloorToInt(Hash(bay, 7, seed) * 4f) % 4] * 1.4f;
                if (v > 0.82f) return Color.black;
                float lit = Hash(bay, 3, seed);
                return lit < 0.75f ? new Color(1f, 0.82f, 0.6f) * (0.7f + 0.3f * (1f - v)) : Color.black;
            });
            return (a, e);
        }
    }
}
