using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using VeyraCity.Core;
using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Master prompt §1.4/E (road wear and grime): WorldBuildBatch step `roaddecals`, after Lighting's DBuffer Decal Renderer
    /// Feature (396ea4b5). URP DecalProjectors with one material (URP "Shader Graphs/Decal": Base_Map + Normal_Map; the atlas'
    /// MAOS mask needs a custom decal graph, not used yet) on the original procedural atlas (Tools/Blender/vc_decals.py,
    /// 4 x 4 tiles selected by uvScale / uvBias). DERIVED, NOT DATA: placed by a seeded deterministic rule along the road
    /// network centre lines (class, lanes, station), never by hand:
    /// <list type="bullet">
    /// <item>urban AR / CO / SV: a round or square cover in alternate lanes about every 55 m, a storm drain grate at the
    /// kerb about every 60 m (each side), broken gutter grime strips along both kerbs, oil stains in the lanes;</item>
    /// <item>all roads: oil stains / drip trails in the lanes, cracks and sealed patches now and then; rural and highways:
    /// skid marks now and then.</item>
    /// </list>
    /// None on runways, gravel roads or structures (deck and parapet geometry). Each projector stands on the rendered
    /// asphalt (highest M_Asphalt triangle within 2 m of the road) and is skipped where there is none. Projectors draw to
    /// 120 m (the feature culls at 150 m). Grouped in 250 m chunks under World/RoadDecals so Distribute To Cells streams
    /// them. Idempotent (root rebuilt; texture import and material updated in place). Report Logs/Decals/road_decals.txt.
    /// </summary>
    public static class RoadDecalBuilder
    {
        const string TexDir = "Assets/_Veyra/Art/Textures/Decals";
        const string MatPath = "Assets/_Veyra/World/Generated/Materials/M_Decal_Road.mat";
        const string SrcDir = "ArtSource/Blender/Decals";
        static readonly HashSet<string> Urban = new HashSet<string> { "AR", "CO", "SV" };

        public static string Run()
        {
            WorldData.EnsureLoaded();
            var sb = new StringBuilder("Road decals\n");
            var world = EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == "World");
            if (world == null) return "FAIL: no World root\n";
            var mat = Material();
            if (mat == null) return "FAIL: decal material (URP Shader Graphs/Decal or the atlas textures) not available\n";
            var old = world.transform.Find("RoadDecals");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("RoadDecals").transform;
            root.SetParent(world.transform, false);

            // rendered asphalt sampler (10 m grid of M_Asphalt triangles)
            var grid = new Dictionary<long, List<Vector3>>();
            long Key(float x, float z) => ((long)Mathf.FloorToInt(x / 10f) << 32) ^ (uint)Mathf.FloorToInt(z / 10f);
            foreach (var mf in world.GetComponentsInChildren<MeshFilter>(false))
            {
                if (mf.sharedMesh == null || mf.name.EndsWith("_LOD1") || mf.name.EndsWith("_LOD2")) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || !mr.enabled) continue;
                var sm = mr.sharedMaterials; var mesh = mf.sharedMesh; Vector3[] vv = null; var l2w = mf.transform.localToWorldMatrix;
                for (int si = 0; si < mesh.subMeshCount && si < sm.Length; si++)
                {
                    if (sm[si] == null || !sm[si].name.StartsWith("M_Asphalt")) continue;
                    if (vv == null) vv = mesh.vertices;
                    var tt = mesh.GetTriangles(si);
                    for (int i = 0; i < tt.Length; i += 3)
                    {
                        var a = l2w.MultiplyPoint3x4(vv[tt[i]]); var b = l2w.MultiplyPoint3x4(vv[tt[i + 1]]); var c = l2w.MultiplyPoint3x4(vv[tt[i + 2]]);
                        float x0 = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), x1 = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                        float z0 = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), z1 = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                        if (x1 - x0 > 200f || z1 - z0 > 200f) continue;
                        for (float gx = Mathf.Floor(x0 / 10f) * 10f; gx <= x1; gx += 10f)
                            for (float gz = Mathf.Floor(z0 / 10f) * 10f; gz <= z1; gz += 10f)
                            {
                                long k = Key(gx + 0.01f, gz + 0.01f);
                                if (!grid.TryGetValue(k, out var lst)) grid[k] = lst = new List<Vector3>();
                                lst.Add(a); lst.Add(b); lst.Add(c);
                            }
                    }
                }
            }
            float AsphaltY(Vector3 at)
            {
                float best = float.NegativeInfinity;
                if (grid.TryGetValue(Key(at.x, at.z), out var lst))
                    for (int i = 0; i < lst.Count; i += 3)
                    {
                        var a = lst[i]; var b = lst[i + 1]; var c = lst[i + 2];
                        float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                        if (Mathf.Abs(d) < 1e-6f) continue;
                        float w0 = ((b.z - c.z) * (at.x - c.x) + (c.x - b.x) * (at.z - c.z)) / d, w1 = ((c.z - a.z) * (at.x - c.x) + (a.x - c.x) * (at.z - c.z)) / d, w2 = 1f - w0 - w1;
                        if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
                        float y = w0 * a.y + w1 * b.y + w2 * c.y;
                        if (Mathf.Abs(y - at.y) < 2f && y > best) best = y;
                    }
                return best;
            }

            var chunkRoots = new Dictionary<string, Transform>();
            Transform Chunk(Vector3 p)
            {
                var w = WorldCoordinates.FromUnity(p);
                string c = WorldCoordinates.ChunkOf(Mathf.Clamp(w.x, 0f, 7999f), Mathf.Clamp(w.y, 0f, 5999f));
                if (!chunkRoots.TryGetValue(c, out var t)) { t = new GameObject(c).transform; t.SetParent(root, false); chunkRoots[c] = t; }
                return t;
            }
            var counts = new int[16]; int skipped = 0;
            void Place(int tile, Vector3 at, Vector3 along, float width, float length, float yawJitter, System.Random rng)
            {
                float y = AsphaltY(at);
                if (float.IsNegativeInfinity(y)) { skipped++; return; }
                at.y = y;
                var dir = Quaternion.Euler(0f, yawJitter, 0f) * along;
                var go = new GameObject($"Decal_{tile:D2}");
                go.transform.SetParent(Chunk(at), false);
                go.transform.position = at;
                go.transform.rotation = Quaternion.LookRotation(Vector3.down, dir);   // project straight down, texture up = along
                var dp = go.AddComponent<DecalProjector>();
                dp.material = mat;
                dp.size = new Vector3(width, length, 1.2f);
                dp.pivot = Vector3.zero;
                dp.uvScale = new Vector2(0.25f, 0.25f);
                dp.uvBias = new Vector2((tile % 4) * 0.25f, (3 - tile / 4) * 0.25f);
                dp.drawDistance = 120f;
                dp.fadeScale = 0.85f;
                dp.fadeFactor = 1f;
                counts[tile]++;
            }

            for (int r = 0; r < WorldData.Roads.Count; r++)
            {
                var road = WorldData.Roads[r];
                if (road.Data.cls == "RW" || road.Data.cls == "GR") continue;
                bool urban = Urban.Contains(road.Data.cls);
                var path = road.Path; float len = path.Length, hw = road.HalfWidth;
                int lanes = Mathf.Max(1, road.Data.lanes);
                float laneW = road.Data.width / lanes;
                var rng = new System.Random(road.Data.id.GetHashCode());
                for (float s = 6f; s < len - 6f; s += 4f)
                {
                    if (road.StructureAt(path.SegmentAt(s)) != StructureKind.None) continue;
                    var c = path.PositionAt(s); var t = path.TangentAt(s); t.y = 0f; if (t.sqrMagnitude < 1e-6f) continue; t.Normalize();
                    var right = new Vector3(t.z, 0f, -t.x);
                    Vector3 LaneCentre(int lane) => c + right * (-hw + laneW * (lane + 0.5f));
                    int step = Mathf.RoundToInt(s / 4f);
                    if (urban)
                    {
                        if (step % 14 == 0) Place(rng.NextDouble() < 0.6 ? 5 : 6, LaneCentre((step / 14) % lanes), t, 1.1f, 1.1f, (float)rng.NextDouble() * 360f, rng);          // cover
                        if (step % 15 == 7)   // grate each side about every 60 m
                            foreach (float sd in new[] { -1f, 1f })
                                Place(7, c + right * sd * (hw - 0.45f), t, 0.9f, 0.6f, 0f, rng);                                                                   // drain grate at the kerb
                        if (step % 6 == 0 && rng.NextDouble() < 0.6)   // grime strips, broken (every 24 m, 60 %)
                            foreach (float sd in new[] { -1f, 1f })
                                Place(15, c + right * sd * (hw - 0.55f), t * sd, 1.1f, 12f, 0f, rng);                                                              // gutter grime strip
                    }
                    double roll = rng.NextDouble();
                    int ln = rng.Next(lanes);
                    var lc = LaneCentre(ln) + right * (float)(rng.NextDouble() - 0.5) * laneW * 0.3f;
                    if (roll < (urban ? 0.09 : 0.03)) Place(rng.NextDouble() < 0.7 ? rng.Next(2) : 2, lc, t, 1.4f + (float)rng.NextDouble(), 1.6f + (float)rng.NextDouble() * 1.5f, (float)rng.NextDouble() * 30f - 15f, rng);   // oil
                    else if (roll < (urban ? 0.12 : 0.06)) Place(8 + rng.Next(2), lc, t, 3f, 3f, (float)rng.NextDouble() * 360f, rng);                                   // cracks
                    else if (roll < (urban ? 0.135 : 0.07)) Place(10 + rng.Next(2), lc, t, laneW * 0.8f, 2.5f + (float)rng.NextDouble() * 3f, 0f, rng);                  // sealed patch
                    else if (!urban && roll < 0.075) Place(rng.Next(2) == 0 ? 3 : 4, lc, t, 2.2f, 9f, (float)rng.NextDouble() * 10f - 5f, rng);                           // skid marks
                }
            }
            sb.AppendLine($"  {counts.Sum():N0} decal projectors in {chunkRoots.Count} chunks (skipped off the rendered asphalt: {skipped:N0}); per tile: {string.Join(" ", counts.Select((n, i) => $"{i}:{n}"))}");
            sb.AppendLine("  DERIVED, NOT DATA: seeded rule along the road centre lines (see the class summary)");
            Directory.CreateDirectory("Logs/Decals");
            File.WriteAllText("Logs/Decals/road_decals.txt", sb.ToString());
            return sb.ToString();
        }

        /// <summary>Copies the atlas from ArtSource, sets the importers, and creates or updates the decal material.</summary>
        static Material Material()
        {
            Directory.CreateDirectory(TexDir);
            foreach (var n in new[] { "T_Decal_Road_BaseMap", "T_Decal_Road_Normal" })
            {
                string src = $"{SrcDir}/{n}.png", dst = $"{TexDir}/{n}.png";
                if (!File.Exists(src)) return null;
                if (!File.Exists(dst) || File.GetLastWriteTimeUtc(src) > File.GetLastWriteTimeUtc(dst)) { File.Copy(src, dst, true); AssetDatabase.ImportAsset(dst); }
                var ti = (TextureImporter)AssetImporter.GetAtPath(dst);
                bool normal = n.EndsWith("Normal");
                bool dirty = false;
                if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; dirty = true; }
                if (!normal && !ti.alphaIsTransparency) { ti.alphaIsTransparency = true; dirty = true; }
                if (ti.wrapMode != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; dirty = true; }
                if (ti.maxTextureSize != 2048) { ti.maxTextureSize = 2048; dirty = true; }
                if (dirty) ti.SaveAndReimport();
            }
            var shader = Shader.Find("Shader Graphs/Decal");   // Editor code
            if (shader == null) return null;
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (m == null) { m = new Material(shader) { name = "M_Decal_Road" }; AssetDatabase.CreateAsset(m, MatPath); }
            m.shader = shader;
            m.SetTexture("Base_Map", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/T_Decal_Road_BaseMap.png"));
            m.SetTexture("Normal_Map", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/T_Decal_Road_Normal.png"));
            m.SetFloat("Normal_Blend", 0.6f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            return m;
        }
    }
}
