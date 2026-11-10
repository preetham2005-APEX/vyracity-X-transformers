using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Instanced grass on the terrain: crossed-card blades with an alpha-clipped procedural texture, density from the
    /// grass and lawn splat weights, kept off roads (rasterised road corridors), water and steep rock.
    /// </summary>
    public static class GrassBuilder
    {
        const int DetailRes = 256;

        public static void AddGrass(Transform worldRoot, string gen)
        {
            var proto = GrassPrefab(gen);
            WorldData.EnsureLoaded();
            var lake = MirrorLake.LoadOrNull(); // BUG-057: no grass on the submerged Mirror Lake bed
            int planted = 0;
            foreach (var t in worldRoot.GetComponentsInChildren<Terrain>())
            {
                var td = t.terrainData;
                td.detailPrototypes = new[]
                {
                    new DetailPrototype
                    {
                        prototype = proto, usePrototypeMesh = true, useInstancing = true, renderMode = DetailRenderMode.VertexLit,
                        minWidth = 0.7f, maxWidth = 1.3f, minHeight = 0.6f, maxHeight = 1.25f, noiseSpread = 0.3f,
                        healthyColor = Color.white, dryColor = new Color(0.86f, 0.82f, 0.62f),
                    }
                };
                td.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
                td.SetDetailResolution(DetailRes, 32);
                var am = td.GetAlphamaps(0, 0, td.alphamapWidth, td.alphamapHeight);
                var road = RoadMask(t.transform.position, td.size.x);
                var layer = new int[DetailRes, DetailRes];
                for (int y = 0; y < DetailRes; y++)
                    for (int x = 0; x < DetailRes; x++)
                    {
                        if (road[y, x]) continue;
                        float nx = (x + 0.5f) / DetailRes, ny = (y + 0.5f) / DetailRes;
                        float hgt = td.GetInterpolatedHeight(nx, ny) + t.transform.position.y;
                        if (hgt < 0.6f || td.GetSteepness(nx, ny) > 32f) continue;
                        if (MirrorLake.IsLakeBed(lake, t.transform.position.x + nx * td.size.x, t.transform.position.z + ny * td.size.z, hgt)) continue;
                        int ax = Mathf.Min(td.alphamapWidth - 1, (int)(nx * td.alphamapWidth)), ay = Mathf.Min(td.alphamapHeight - 1, (int)(ny * td.alphamapHeight));
                        float g = am[ay, ax, 0] + am[ay, ax, 7] * 0.9f + am[ay, ax, 1] * 0.35f + am[ay, ax, 6] * 0.3f;
                        int n = Mathf.RoundToInt(Mathf.Clamp01(g) * 9f);
                        layer[y, x] = n;
                        planted += n;
                    }
                td.SetDetailLayer(0, 0, 0, layer);
                t.detailObjectDistance = 120f;
                t.detailObjectDensity = 1f;
                t.drawTreesAndFoliage = true;
                EditorUtility.SetDirty(td);
            }
            Debug.Log($"[GrassBuilder] planted {planted:N0} grass instances");
        }

        /// <summary>Cells within a road's half-width + 2.5 m of its centreline.</summary>
        static bool[,] RoadMask(Vector3 origin, float size)
        {
            var mask = new bool[DetailRes, DetailRes];
            float cell = size / DetailRes;
            foreach (var r in WorldData.Roads)
            {
                float half = r.HalfWidth + 2.5f;
                var p = r.Path.Points;
                int segs = r.Path.Closed ? p.Length : p.Length - 1;
                for (int i = 0; i < segs; i++)
                {
                    var a = p[i]; var b = p[(i + 1) % p.Length];
                    float minx = Mathf.Min(a.x, b.x) - half - origin.x, maxx = Mathf.Max(a.x, b.x) + half - origin.x;
                    float minz = Mathf.Min(a.z, b.z) - half - origin.z, maxz = Mathf.Max(a.z, b.z) + half - origin.z;
                    if (maxx < 0 || maxz < 0 || minx > size || minz > size) continue;
                    int x0 = Mathf.Max(0, (int)(minx / cell)), x1 = Mathf.Min(DetailRes - 1, (int)(maxx / cell));
                    int z0 = Mathf.Max(0, (int)(minz / cell)), z1 = Mathf.Min(DetailRes - 1, (int)(maxz / cell));
                    var ab = new Vector2(b.x - a.x, b.z - a.z);
                    float L2 = Mathf.Max(1e-6f, ab.sqrMagnitude);
                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            var q = new Vector2(origin.x + (x + 0.5f) * cell - a.x, origin.z + (z + 0.5f) * cell - a.z);
                            float t = Mathf.Clamp01(Vector2.Dot(q, ab) / L2);
                            if ((q - ab * t).sqrMagnitude < half * half) mask[z, x] = true;
                        }
                }
            }
            return mask;
        }

        static GameObject GrassPrefab(string gen)
        {
            string dir = gen + "/Grass";
            Directory.CreateDirectory(dir);
            // texture: tapered blades with alpha
            string texPath = dir + "/GrassBlades.png";
            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, true);
            var px = new Color[256 * 256];
            var rng = new System.Random(5);
            for (int b = 0; b < 26; b++)
            {
                float cx = (float)rng.NextDouble() * 256f, hgt = 140f + (float)rng.NextDouble() * 110f, w = 4f + (float)rng.NextDouble() * 5f;
                float lean = ((float)rng.NextDouble() - 0.5f) * 40f;
                var col = Color.Lerp(new Color(0.22f, 0.36f, 0.12f), new Color(0.42f, 0.5f, 0.2f), (float)rng.NextDouble());
                for (int y = 0; y < hgt && y < 256; y++)
                {
                    float t = y / hgt;
                    float half = w * (1f - t);
                    float x0 = cx + lean * t * t;
                    for (int x = Mathf.FloorToInt(x0 - half); x <= Mathf.CeilToInt(x0 + half); x++)
                    {
                        int xx = ((x % 256) + 256) % 256;
                        px[y * 256 + xx] = new Color(col.r * (0.6f + 0.4f * t), col.g * (0.6f + 0.4f * t), col.b, 1f);
                    }
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceSynchronousImport);
            var imp = (TextureImporter)AssetImporter.GetAtPath(texPath);
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
            var grassTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

            string matPath = dir + "/M_Grass.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.SetTexture("_BaseMap", grassTex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.4f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Cull", (float)CullMode.Off);
            mat.SetFloat("_Smoothness", 0.15f);
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);

            // mesh: three crossed cards 1 m wide, 0.55 m tall
            string meshPath = dir + "/GrassCards.asset";
            AssetDatabase.DeleteAsset(meshPath);
            var mesh = new Mesh { name = "GrassCards" };
            var v = new System.Collections.Generic.List<Vector3>();
            var n = new System.Collections.Generic.List<Vector3>();
            var uv = new System.Collections.Generic.List<Vector2>();
            var tris = new System.Collections.Generic.List<int>();
            for (int k = 0; k < 3; k++)
            {
                float ang = k * 60f * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 0.5f;
                int i = v.Count;
                v.Add(-d); v.Add(d); v.Add(d + Vector3.up * 0.55f); v.Add(-d + Vector3.up * 0.55f);
                for (int q = 0; q < 4; q++) n.Add(Vector3.up); // up-facing normals light the cards like the ground
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                tris.AddRange(new[] { i, i + 3, i + 2, i, i + 2, i + 1 });
            }
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, meshPath);

            string prefabPath = dir + "/GrassCards.prefab";
            var go = new GameObject("GrassCards");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }
    }
}
