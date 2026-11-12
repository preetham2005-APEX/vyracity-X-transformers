using System.IO;
using UnityEditor;
using UnityEngine;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// D2 (round 3): metallic flake for car paint. Writes a procedural flake normal map (Voronoi cells, each tilted a
    /// random 0-22 deg) and sets it as the detail normal of every M_Paint_* material (URP Complex Lit with clear coat),
    /// tiled finely so each flake catches the light on its own under the smooth clear coat. Edit-time only: the
    /// _DETAIL_MULX2 keyword is set on the material assets here, never at runtime (card F1). The materials are git-tracked
    /// (revert with git checkout). Batch: -executeMethod VeyraCity.EditorTools.PaintFlake.Apply
    /// </summary>
    public static class PaintFlake
    {
        const string TexPath = "Assets/_Veyra/Art/Vehicles/Shared/T_PaintFlake_N.png";
        const float Tiling = 40f, Strength = 0.07f;   // pass 37: 24 / 0.18 read as grain on the close-ups

        [MenuItem("Veyra/Vehicles/Apply Paint Flake")]
        public static void Apply()
        {
            var tex = EnsureTexture();
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material M_Paint", new[] { "Assets/_Veyra/Art/Vehicles" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || !Path.GetFileName(path).StartsWith("M_Paint") || !m.HasProperty("_DetailNormalMap")) continue;
                m.SetTexture("_DetailNormalMap", tex);
                m.SetFloat("_DetailNormalMapScale", Strength);
                m.SetTextureScale("_DetailAlbedoMap", new Vector2(Tiling, Tiling));
                if (m.HasProperty("_DetailAlbedoMapScale")) m.SetFloat("_DetailAlbedoMapScale", 1f);
                m.EnableKeyword("_DETAIL_MULX2");
                EditorUtility.SetDirty(m);
                n++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[PaintFlake] flake detail normal on {n} paint materials ({TexPath}, tiling {Tiling}, strength {Strength})");
        }

        static Texture2D EnsureTexture()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
            if (existing != null) return existing;
            Directory.CreateDirectory(Path.GetDirectoryName(TexPath));
            const int N = 256, Cells = 2600;
            var rng = new System.Random(1234);
            var pts = new Vector2[Cells];
            var nrm = new Vector3[Cells];
            for (int i = 0; i < Cells; i++)
            {
                pts[i] = new Vector2((float)rng.NextDouble() * N, (float)rng.NextDouble() * N);
                float tilt = (float)rng.NextDouble() * 22f * Mathf.Deg2Rad, az = (float)rng.NextDouble() * Mathf.PI * 2f;
                nrm[i] = new Vector3(Mathf.Sin(tilt) * Mathf.Cos(az), Mathf.Sin(tilt) * Mathf.Sin(az), Mathf.Cos(tilt));
            }
            // a coarse grid of cell lists keeps the nearest-point search cheap; distances wrap so the map tiles
            const int G = 16;
            var grid = new System.Collections.Generic.List<int>[G * G];
            for (int i = 0; i < grid.Length; i++) grid[i] = new System.Collections.Generic.List<int>();
            for (int i = 0; i < Cells; i++) grid[(int)(pts[i].y / N * G) % G * G + (int)(pts[i].x / N * G) % G].Add(i);
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int gx = x * G / N, gy = y * G / N, best = 0;
                    float bd = float.MaxValue;
                    for (int oy = -1; oy <= 1; oy++)
                        for (int ox = -1; ox <= 1; ox++)
                            foreach (int i in grid[(gy + oy + G) % G * G + (gx + ox + G) % G])
                            {
                                float dx = Mathf.Abs(pts[i].x - x), dy = Mathf.Abs(pts[i].y - y);
                                dx = Mathf.Min(dx, N - dx); dy = Mathf.Min(dy, N - dy);
                                float d = dx * dx + dy * dy;
                                if (d < bd) { bd = d; best = i; }
                            }
                    var v = nrm[best];
                    px[y * N + x] = new Color32((byte)((v.x * 0.5f + 0.5f) * 255), (byte)((v.y * 0.5f + 0.5f) * 255), (byte)((v.z * 0.5f + 0.5f) * 255), 255);
                }
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false, true);
            t.SetPixels32(px);
            t.Apply();
            File.WriteAllBytes(TexPath, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(TexPath);
            var imp = (TextureImporter)AssetImporter.GetAtPath(TexPath);
            imp.textureType = TextureImporterType.NormalMap;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
        }
    }
}
