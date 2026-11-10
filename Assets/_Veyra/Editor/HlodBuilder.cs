using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VeyraCity.World.Streaming;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card W7 / P1 HLOD: one far proxy per 1 km cell, shown by <see cref="CellStreamer"/> while that cell is not loaded.
    /// <list type="bullet">
    /// <item>Ground: a 33 × 33 height grid sampled from the cell's terrain (2,048 triangles), textured with a top-down capture of
    /// the cell as authored (terrain, roads, roofs, water) packed into one 2048² atlas (256 px per cell, ≈ 3.9 m per pixel) →
    /// one material (HLOD_Terrain) for every proxy.</item>
    /// <item>Buildings: each building's lowest LOD merged per material (so the facade shader still draws lit windows on the night
    /// skyline); if a cell's buildings exceed <see cref="BuildingBudget"/> triangles, or a mesh is not readable, the building
    /// becomes a box with facade-tile UVs (u along the wall / 12 m, v = height / 14 m, as CityBuilder lays them).</item>
    /// </list>
    /// Shadows off. Meshes: World/Generated/HLOD/HLOD_&lt;cell&gt;.asset (overwritten in place). Budget ≤ 30k triangles per cell.
    /// </summary>
    public static class HlodBuilder
    {
        public const string RootName = "HLOD";
        const string Dir = "Assets/_Veyra/World/Generated/HLOD";
        const string AtlasPath = Dir + "/HLOD_GroundAtlas.png";
        const string MatPath = "Assets/_Veyra/World/Generated/Materials/HLOD_Ground.mat";
        const int Grid = 33, Tile = 256, AtlasTiles = 8;
        public const int BuildingBudget = 27000;

        public static string Build(List<Transform>[] perCell, Transform root)
        {
            var log = new StringBuilder();
            Directory.CreateDirectory(Dir);
            Material mat = null;
            if (!CellDistributor.StayInMain.Contains("Terrain"))
            {
                var atlas = CaptureAtlas(perCell, log);
                File.WriteAllBytes(AtlasPath, atlas.EncodeToPNG());
                Object.DestroyImmediate(atlas);
                AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);
                if (AssetImporter.GetAtPath(AtlasPath) is TextureImporter ti)
                {
                    ti.sRGBTexture = true;
                    ti.mipmapEnabled = true;
                    ti.wrapMode = TextureWrapMode.Clamp;
                    ti.maxTextureSize = 2048;
                    ti.textureCompression = TextureImporterCompression.CompressedHQ;   // BC7 on PC
                    ti.SaveAndReimport();
                }
                mat = GroundMaterial(AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath));
            }

            int totalTris = 0, maxTris = 0, boxed = 0;
            for (int i = 0; i < 48; i++)
            {
                var mesh = new Mesh { name = "HLOD_" + CellStreamer.CellName(i), indexFormat = IndexFormat.UInt32 };
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var subs = new List<(Material m, List<int> tris)>();
                // the terrain stays resident (CellDistributor.StayInMain): proxies carry the ground only when it streams
                if (!CellDistributor.StayInMain.Contains("Terrain"))
                {
                    Ground(i, perCell[i], verts, uvs, out var groundTris);
                    subs.Add((mat, groundTris));
                }
                boxed += Buildings(perCell[i], verts, uvs, subs);
                Roads(i, verts, uvs, subs);
                mesh.SetVertices(verts);
                mesh.SetUVs(0, uvs);
                mesh.subMeshCount = subs.Count;
                for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s].tris, s, false);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                mesh = SaveMesh(mesh);
                var go = new GameObject("HLOD_" + CellStreamer.CellName(i));
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = subs.Select(x => x.m).ToArray();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                int tris = subs.Sum(x => x.tris.Count) / 3;
                var p = go.AddComponent<CellProxy>();
                p.Index = i; p.Cell = CellStreamer.CellName(i); p.Triangles = tris;
                totalTris += tris; maxTris = Mathf.Max(maxTris, tris);
            }
            log.AppendLine($"HLOD: 48 proxies, {totalTris:N0} triangles (max {maxTris:N0} per cell, budget 30,000), {boxed} buildings as boxes; atlas {AtlasPath}");
            return log.ToString();
        }

        // ------------------------------------------------------------------ ground

        static void Ground(int cell, List<Transform> content, List<Vector3> v, List<Vector2> uv, out List<int> t)
        {
            t = new List<int>();
            var rect = CellStreamer.CellRect(cell);
            var terrains = content.SelectMany(x => x.GetComponentsInChildren<Terrain>(true)).Where(x => x.terrainData != null).ToList();
            if (terrains.Count == 0) terrains = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include).ToList();
            int col = cell % AtlasTiles, row = cell / AtlasTiles;
            int b = v.Count;
            for (int z = 0; z < Grid; z++)
                for (int x = 0; x < Grid; x++)
                {
                    float fx = x / (Grid - 1f), fz = z / (Grid - 1f);
                    var p = new Vector3(rect.xMin + fx * rect.width, 0f, rect.yMin + fz * rect.height);
                    p.y = Height(terrains, p) - 0.6f;   // just under the real ground, so a loaded neighbour wins at the seam
                    v.Add(p);
                    uv.Add(new Vector2((col + Mathf.Lerp(0.5f, Tile - 0.5f, fx) / Tile) / AtlasTiles,
                                       ((AtlasTiles - 1 - row) + Mathf.Lerp(0.5f, Tile - 0.5f, fz) / Tile) / AtlasTiles));
                }
            for (int z = 0; z < Grid - 1; z++)
                for (int x = 0; x < Grid - 1; x++)
                {
                    int a = b + z * Grid + x, c = a + Grid;
                    t.Add(a); t.Add(c); t.Add(a + 1);
                    t.Add(a + 1); t.Add(c); t.Add(c + 1);
                }
        }

        static float Height(List<Terrain> terrains, Vector3 p)
        {
            Terrain best = null;
            float bestD = float.MaxValue;
            foreach (var ter in terrains)
            {
                var tp = ter.GetPosition();
                var s = ter.terrainData.size;
                float dx = Mathf.Max(tp.x - p.x, 0f, p.x - (tp.x + s.x)), dz = Mathf.Max(tp.z - p.z, 0f, p.z - (tp.z + s.z));
                float d = dx + dz;
                if (d < bestD) { bestD = d; best = ter; }
            }
            if (best == null) return 0f;
            var bp = best.GetPosition();
            var bs = best.terrainData.size;
            var q = new Vector3(Mathf.Clamp(p.x, bp.x, bp.x + bs.x), 0f, Mathf.Clamp(p.z, bp.z, bp.z + bs.z));
            return best.SampleHeight(q) + bp.y;
        }

        static Texture2D CaptureAtlas(List<Transform>[] perCell, StringBuilder log)
        {
            var atlas = new Texture2D(Tile * AtlasTiles, Tile * AtlasTiles, TextureFormat.RGBA32, false);
            var fill = new Color32[atlas.width * atlas.height];
            for (int k = 0; k < fill.Length; k++) fill[k] = new Color32(70, 82, 60, 255);
            atlas.SetPixels32(fill);
            var go = new GameObject("[HLOD capture]") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 500f;
            cam.aspect = 1f;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 4000f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.27f, 0.32f, 0.24f);
            cam.enabled = false;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            var rt = new RenderTexture(Tile * 2, Tile * 2, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var read = new Texture2D(Tile * 2, Tile * 2, TextureFormat.RGBA32, false);
            int ok = 0;
            try
            {
                for (int i = 0; i < 48; i++)
                {
                    var r = CellStreamer.CellRect(i);
                    go.transform.position = new Vector3(r.center.x, 2500f, r.center.y);
                    go.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);   // image top = north
                    cam.targetTexture = rt;
                    cam.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    read.Apply();
                    RenderTexture.active = prev;
                    // 2 × 2 box filter down to the tile, and take out some of the baked-in light (the proxy is lit again)
                    var src = read.GetPixels32();
                    int col = i % AtlasTiles, row = i / AtlasTiles;
                    int ox = col * Tile, oy = (AtlasTiles - 1 - row) * Tile;
                    var dst = new Color32[Tile * Tile];
                    for (int y = 0; y < Tile; y++)
                        for (int x = 0; x < Tile; x++)
                        {
                            int s0 = (y * 2) * rt.width + x * 2;
                            Color c = ((Color)src[s0] + src[s0 + 1] + src[s0 + rt.width] + src[s0 + rt.width + 1]) * 0.25f;
                            c *= 0.85f;
                            c.a = 1f;
                            dst[y * Tile + x] = c;
                        }
                    atlas.SetPixels32(ox, oy, Tile, Tile, dst);
                    ok++;
                }
            }
            finally
            {
                RenderSettings.fog = fog;
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(read);
                Object.DestroyImmediate(go);
            }
            atlas.Apply();
            log.AppendLine($"HLOD ground atlas: {ok}/48 cells captured top-down");
            return atlas;
        }

        static Material GroundMaterial(Texture2D atlas)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "HLOD_Ground" };   // Editor code: Shader.Find allowed
                AssetDatabase.CreateAsset(mat, MatPath);
            }
            mat.SetTexture("_BaseMap", atlas);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.12f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_SpecularHighlights", 0f);
            mat.SetFloat("_EnvironmentReflections", 0f);
            mat.enableInstancing = false;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ roads

        static Material roadMat;

        /// <summary>
        /// Far road ribbons from the road data (centre line + width, 20 m steps, 0.3 m above the profile so the terrain does not
        /// cover them), one material (M_Asphalt_New). Keeps the network readable from aerial views beyond the loaded window.
        /// </summary>
        static void Roads(int cell, List<Vector3> v, List<Vector2> uv, List<(Material m, List<int> tris)> subs)
        {
            if (roadMat == null) roadMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Veyra/World/Generated/Materials/M_Asphalt_New.mat");
            if (roadMat == null) return;
            VeyraCity.World.WorldData.EnsureLoaded();
            var rect = CellStreamer.CellRect(cell);
            var list = Sub(subs, roadMat);
            foreach (var r in VeyraCity.World.WorldData.Roads)
            {
                if (r.Data.cls == "RW") continue;
                var path = r.Path;
                float hw = r.HalfWidth;
                for (float s = 0f; s < path.Length; s += 20f)
                {
                    float s1 = Mathf.Min(path.Length, s + 20f);
                    var a = path.PositionAt(s); var b = path.PositionAt(s1);
                    var mid = (a + b) * 0.5f;
                    if (!rect.Contains(new Vector2(mid.x, mid.z))) continue;
                    var ta = path.TangentAt(s); ta.y = 0f; ta.Normalize();
                    var tb = path.TangentAt(s1); tb.y = 0f; tb.Normalize();
                    var ra = new Vector3(ta.z, 0f, -ta.x) * hw; var rb = new Vector3(tb.z, 0f, -tb.x) * hw;
                    int i0 = v.Count;
                    v.Add(a - ra + Vector3.up * 0.3f); v.Add(a + ra + Vector3.up * 0.3f); v.Add(b - rb + Vector3.up * 0.3f); v.Add(b + rb + Vector3.up * 0.3f);
                    uv.Add(new Vector2(0f, s)); uv.Add(new Vector2(r.Data.width, s)); uv.Add(new Vector2(0f, s1)); uv.Add(new Vector2(r.Data.width, s1));
                    list.Add(i0); list.Add(i0 + 2); list.Add(i0 + 1);
                    list.Add(i0 + 1); list.Add(i0 + 2); list.Add(i0 + 3);
                }
            }
        }

        // ------------------------------------------------------------------ buildings

        static int Buildings(List<Transform> content, List<Vector3> v, List<Vector2> uv, List<(Material m, List<int> tris)> subs)
        {
            var renderers = new List<MeshRenderer>();
            foreach (var t in content)
            {
                if (!IsUnder(t, "Buildings")) continue;
                var skip = new HashSet<Renderer>();
                foreach (var lg in t.GetComponentsInChildren<LODGroup>(true))
                {
                    var lods = lg.GetLODs();
                    for (int k = 0; k < lods.Length - 1; k++) foreach (var r in lods[k].renderers) if (r != null) skip.Add(r);
                }
                foreach (var mr in t.GetComponentsInChildren<MeshRenderer>(true))
                    if (!skip.Contains(mr) && mr.enabled && mr.gameObject.activeInHierarchy) renderers.Add(mr);
            }
            long tris = 0;
            foreach (var mr in renderers)
            {
                var m = mr.GetComponent<MeshFilter>()?.sharedMesh;
                if (m != null) tris += Tris(m);
            }
            bool boxes = tris > BuildingBudget;
            int boxed = 0;
            foreach (var mr in renderers)
            {
                var mf = mr.GetComponent<MeshFilter>();
                var m = mf != null ? mf.sharedMesh : null;
                if (m == null) continue;
                var mats = mr.sharedMaterials;
                if (!boxes && m.isReadable)
                {
                    var mv = m.vertices;
                    var muv = m.uv;
                    int b = v.Count;
                    var l2w = mr.transform.localToWorldMatrix;
                    for (int k = 0; k < mv.Length; k++) { v.Add(l2w.MultiplyPoint3x4(mv[k])); uv.Add(muv.Length == mv.Length ? muv[k] : Vector2.zero); }
                    for (int s = 0; s < m.subMeshCount; s++)
                    {
                        var list = Sub(subs, mats.Length > 0 ? mats[Mathf.Min(s, mats.Length - 1)] : null);
                        if (list == null) continue;
                        foreach (int idx in m.GetTriangles(s)) list.Add(b + idx);
                    }
                }
                else
                {
                    var list = Sub(subs, mats.Length > 0 ? mats[0] : null);
                    if (list == null) continue;
                    Box(mr.bounds, v, uv, list);
                    boxed++;
                }
            }
            return boxed;
        }

        static long Tris(Mesh m)
        {
            long n = 0;
            for (int s = 0; s < m.subMeshCount; s++) n += m.GetIndexCount(s) / 3;
            return n;
        }

        static bool IsUnder(Transform t, string group)
        {
            for (var x = t; x != null; x = x.parent) if (x.name == group) return true;
            return false;
        }

        static List<int> Sub(List<(Material m, List<int> tris)> subs, Material m)
        {
            if (m == null) return null;
            foreach (var s in subs) if (s.m == m) return s.tris;
            var l = new List<int>();
            subs.Add((m, l));
            return l;
        }

        static void Box(Bounds b, List<Vector3> v, List<Vector2> uv, List<int> t)
        {
            var mn = b.min; var mx = b.max;
            // four walls + roof, facade-tile UVs (12 m bays, 14 m per v)
            void Quad(Vector3 a, Vector3 c, float ua, float uc)
            {
                int i = v.Count;
                v.Add(new Vector3(a.x, mn.y, a.z)); uv.Add(new Vector2(ua / 12f, mn.y / 14f));
                v.Add(new Vector3(c.x, mn.y, c.z)); uv.Add(new Vector2(uc / 12f, mn.y / 14f));
                v.Add(new Vector3(c.x, mx.y, c.z)); uv.Add(new Vector2(uc / 12f, mx.y / 14f));
                v.Add(new Vector3(a.x, mx.y, a.z)); uv.Add(new Vector2(ua / 12f, mx.y / 14f));
                t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2);
            }
            Quad(new Vector3(mn.x, 0, mn.z), new Vector3(mx.x, 0, mn.z), mn.x, mx.x);   // south, outward -Z
            Quad(new Vector3(mx.x, 0, mn.z), new Vector3(mx.x, 0, mx.z), mn.z, mx.z);   // east
            Quad(new Vector3(mx.x, 0, mx.z), new Vector3(mn.x, 0, mx.z), mx.x, mn.x);   // north
            Quad(new Vector3(mn.x, 0, mx.z), new Vector3(mn.x, 0, mn.z), mx.z, mn.z);   // west
            int r = v.Count;
            v.Add(new Vector3(mn.x, mx.y, mn.z)); v.Add(new Vector3(mx.x, mx.y, mn.z)); v.Add(new Vector3(mx.x, mx.y, mx.z)); v.Add(new Vector3(mn.x, mx.y, mx.z));
            for (int k = 0; k < 4; k++) uv.Add(new Vector2(0.02f, 0.02f));
            t.Add(r); t.Add(r + 2); t.Add(r + 1); t.Add(r); t.Add(r + 3); t.Add(r + 2);
        }

        static Mesh SaveMesh(Mesh mesh)
        {
            string path = $"{Dir}/{mesh.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, existing);   // keep the GUID
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }
    }
}
