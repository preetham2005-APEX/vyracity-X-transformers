using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.World.Data;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// BUG-057: the Mirror Lake water surface.
    /// The design package gives the lake as a 4-vertex rectangle (the lake_polygon's bounding box) at lake_level_m = 150 m,
    /// but the registry and the heightmap agree on a lower lake: LM-25 Sable Falls, "where the Sable River leaves Mirror
    /// Lake's basin", is at 143.6 m, the VP-08 jetty at 143.9 m, and 70 of the 72 lake_polygon vertices stand above 143.6 m
    /// on the terrain (median 147.7 m). At 150 m the water floods past the shoreline and covers the jetty and VP-08.
    /// The surface is therefore built from lake_polygon at the outflow's registry elevation, as a grid clipped to the
    /// polygon, and the rectangle is dropped. The data files are unchanged (lake_level_m stays 150 there).
    /// Used by <see cref="CityBuilder"/> when it builds the inland water, and by the menu below to patch the existing
    /// Water_D8 mesh without a full city rebuild. Both rebuild from the same source, so running twice gives the same mesh.
    /// </summary>
    public static class MirrorLake
    {
        /// <summary>Registry id of the lake's outflow (its sill sets the water level).</summary>
        public const string OutflowLocationId = "LM-25";
        public const string MeshAssetPath = "Assets/_Veyra/World/Generated/Meshes/Water_D8_0.asset";
        const string SourceFile = "D8.vmesh";
        const float CellSize = 8f;
        /// <summary>The package's lake rectangle sits at 150 m; no river vertex is that high.</summary>
        const float SourceRectangleMinY = 149.9f;
        /// <summary>CityBuilder lifts every inland water vertex by this much above its source height.</summary>
        public const float WaterLift = 0.05f;

        public sealed class Lake
        {
            public List<Vector2> PolygonXZ;
            public float LevelM;
        }

        /// <summary>The lake polygon (Unity x, z) and the outflow level from Assets/_Veyra/Data; null if the data is missing.</summary>
        public static Lake LoadOrNull()
        {
            try
            {
                var net = RoadNetwork.Parse(VeyraDataValidator.LoadText("road_network_geometry.json"));
                var reg = LocationRegistry.Parse(VeyraDataValidator.LoadText("location_registry.csv"));
                var outflow = reg.Get(OutflowLocationId);
                if (net?.LakePolygon == null || net.LakePolygon.Length < 3 || outflow == null) return null;
                var poly = net.LakePolygon.Select(p => new Vector2(p[0] - 4000f, p[1] - 3000f)).ToList();
                return new Lake { PolygonXZ = poly, LevelM = outflow.ElevationM };
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MirrorLake] lake data not available: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// If the primitive holds the package's lake rectangle (triangles with all three vertices above 149.9 m), removes
        /// those triangles and appends a grid at <paramref name="lake"/>'s level clipped to its polygon. Vertices must
        /// already carry <see cref="WaterLift"/>. Returns the number of grid cells added (0 = no rectangle found).
        /// </summary>
        public static int Patch(List<Vector3> verts, List<Vector2> uvs, List<int> tris, Lake lake)
        {
            if (lake == null) return 0;
            float minY = SourceRectangleMinY + WaterLift;
            var keep = new List<int>(tris.Count);
            bool hadRectangle = false;
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                if (verts[tris[i]].y > minY && verts[tris[i + 1]].y > minY && verts[tris[i + 2]].y > minY) { hadRectangle = true; continue; }
                keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]);
            }
            if (!hadRectangle) return 0;
            tris.Clear();
            tris.AddRange(keep);

            var poly = lake.PolygonXZ;
            float x0 = poly.Min(p => p.x), x1 = poly.Max(p => p.x), z0 = poly.Min(p => p.y), z1 = poly.Max(p => p.y);
            int nx = Mathf.CeilToInt((x1 - x0) / CellSize), nz = Mathf.CeilToInt((z1 - z0) / CellSize);
            float y = lake.LevelM + WaterLift;
            var index = new Dictionary<int, int>();
            int Vertex(int ix, int iz)
            {
                int key = iz * (nx + 1) + ix;
                if (index.TryGetValue(key, out int vi)) return vi;
                var p = new Vector3(x0 + ix * CellSize, y, z0 + iz * CellSize);
                vi = verts.Count;
                verts.Add(p);
                uvs.Add(new Vector2(p.x, p.z));
                index[key] = vi;
                return vi;
            }
            int cells = 0;
            for (int iz = 0; iz < nz; iz++)
                for (int ix = 0; ix < nx; ix++)
                {
                    var c = new Vector2(x0 + (ix + 0.5f) * CellSize, z0 + (iz + 0.5f) * CellSize);
                    if (!Inside(poly, c)) continue;
                    int a = Vertex(ix, iz), b = Vertex(ix, iz + 1), d = Vertex(ix + 1, iz), e = Vertex(ix + 1, iz + 1);
                    // same winding as CityBuilder's sea plane (faces up)
                    tris.Add(a); tris.Add(b); tris.Add(d);
                    tris.Add(d); tris.Add(b); tris.Add(e);
                    cells++;
                }
            return cells;
        }

        /// <summary>Grass blades reach ~0.6 m; none may stand where they would poke through the lake surface.</summary>
        public const float GrassClearance = 0.6f;

        /// <summary>True where terrain grass must not grow: inside the lake polygon and lower than level + clearance.</summary>
        public static bool IsLakeBed(Lake lake, float x, float z, float groundY) =>
            lake != null && groundY < lake.LevelM + GrassClearance && Inside(lake.PolygonXZ, new Vector2(x, z));

        /// <summary>Clears the grass detail layer on the lake bed of one terrain. Returns the number of cells cleared.</summary>
        public static int ClearGrass(Terrain t, Lake lake)
        {
            var td = t.terrainData;
            if (td == null || td.detailPrototypes.Length == 0) return 0;
            var o = t.transform.position;
            var poly = lake.PolygonXZ;
            if (o.x > poly.Max(p => p.x) || o.x + td.size.x < poly.Min(p => p.x) || o.z > poly.Max(p => p.y) || o.z + td.size.z < poly.Min(p => p.y)) return 0;
            int w = td.detailWidth, h = td.detailHeight, cleared = 0;
            var layer = td.GetDetailLayer(0, 0, w, h, 0);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (layer[y, x] == 0) continue;
                    float nx = (x + 0.5f) / w, ny = (y + 0.5f) / h;
                    float gy = td.GetInterpolatedHeight(nx, ny) + o.y;
                    if (!IsLakeBed(lake, o.x + nx * td.size.x, o.z + ny * td.size.z, gy)) continue;
                    layer[y, x] = 0;
                    cleared++;
                }
            if (cleared > 0)
            {
                td.SetDetailLayer(0, 0, 0, layer);
                EditorUtility.SetDirty(td);
            }
            return cleared;
        }

        static bool Inside(List<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        [MenuItem("Veyra/World/Rebuild Mirror Lake Surface (BUG-057)")]
        public static void Menu() => Debug.Log(Rebuild());

        /// <summary>Batch entry: <c>-executeMethod VeyraCity.EditorTools.MirrorLake.BatchRebuild</c>.</summary>
        public static void BatchRebuild()
        {
            int code = 0;
            try { Debug.Log(Rebuild()); }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        /// <summary>Rewrites the Water_D8 mesh asset from WorldSource/meshes/D8.vmesh with the lake patched in.</summary>
        public static string Rebuild()
        {
            var lake = LoadOrNull() ?? throw new InvalidOperationException("lake_polygon or the LM-25 registry row is missing");
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshAssetPath) ?? throw new FileNotFoundException(MeshAssetPath);
            string file = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "WorldSource", "meshes", SourceFile));
            if (!ReadWaterPrimitive(file, out var pos, out var idx, out int primitive))
                throw new InvalidOperationException($"no inland water primitive in {file}");
            if (mesh.name != $"Water_D8_{primitive}") throw new InvalidOperationException($"{MeshAssetPath} is {mesh.name}, source primitive is {primitive}");

            var verts = pos.Select(v => v + Vector3.up * WaterLift).ToList();
            var uvs = pos.Select(v => new Vector2(v.x, v.z)).ToList();
            var tris = idx.ToList();
            int sourceTris = tris.Count / 3;
            int cells = Patch(verts, uvs, tris, lake);
            if (cells == 0) throw new InvalidOperationException("the lake rectangle was not found in the D8 water primitive");

            mesh.Clear();
            mesh.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);

            // Grass on the submerged bed (terrain detail layers; the terrain data assets change, the scene does not).
            EnsureCityScene();
            int grassCells = 0, terrains = 0;
            foreach (var t in UnityEngine.Object.FindObjectsByType<Terrain>())
            {
                int c = ClearGrass(t, lake);
                if (c > 0) { grassCells += c; terrains++; }
            }
            AssetDatabase.SaveAssets();

            var sb = new StringBuilder();
            sb.AppendLine($"  grass cleared from {grassCells} detail cells on {terrains} terrain tiles (lake bed lower than level + {GrassClearance} m)");
            sb.AppendLine($"[MirrorLake] BUG-057: {mesh.name} rebuilt from {SourceFile} primitive {primitive}");
            sb.AppendLine($"  level {lake.LevelM:0.00} m (registry {OutflowLocationId} outflow; lake_level_m in the package is 150 m)");
            sb.AppendLine($"  source {pos.Length} vertices / {sourceTris} triangles -> {verts.Count} vertices / {tris.Count / 3} triangles; lake grid {cells} cells of {CellSize} m inside the {lake.PolygonXZ.Count}-point polygon");
            sb.Append($"  bounds {mesh.bounds.min} .. {mesh.bounds.max}");
            return sb.ToString();
        }

        /// <summary>
        /// Evidence for BUG-057: opens VeyraCity.unity and renders two views of the lake with the review camera rig (edit
        /// mode, plain "Water" material): straight down from 700 m over the lake centre, and an oblique view from 60 m above
        /// the VP-08 jetty. Writes PNGs to Screenshots/&lt;date&gt;_BUG-057/.
        /// Batch entry: <c>-executeMethod VeyraCity.EditorTools.MirrorLake.BatchCaptureCheck</c>.
        /// </summary>
        public static void BatchCaptureCheck()
        {
            int code = 0;
            try
            {
                EnsureCityScene();
                var lake = LoadOrNull() ?? throw new InvalidOperationException("lake data missing");
                float cx = lake.PolygonXZ.Average(p => p.x), cz = lake.PolygonXZ.Average(p => p.y);
                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", DateTime.Now.ToString("yyyy-MM-dd") + "_BUG-057"));
                Directory.CreateDirectory(dir);
                var cam = VeyraCity.Testing.ReviewCameraRig.CreateCamera("[MirrorLakeCheck]");
                cam.farClipPlane = 5000f;
                var views = new[]
                {
                    ("lake_topdown", new Vector3(cx, lake.LevelM + 700f, cz), new Vector3(cx, lake.LevelM, cz + 0.01f), 60f),
                    ("lake_oblique_from_VP08", new Vector3(-2950f, lake.LevelM + 60f, 1700f), new Vector3(cx, lake.LevelM, cz + 150f), 60f),
                };
                foreach (var (name, pos, look, fov) in views)
                {
                    cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation((look - pos).normalized, Vector3.forward));
                    cam.fieldOfView = fov;
                    cam.aspect = (float)VeyraCity.Testing.ReviewCameraRig.Width / VeyraCity.Testing.ReviewCameraRig.Height;
                    VeyraCity.Testing.ReviewCameraRig.WarmUp(cam);
                    var tex = VeyraCity.Testing.ReviewCameraRig.Render(cam);
                    VeyraCity.Testing.ReviewCameraRig.SavePng(tex, Path.Combine(dir, name + ".png"));
                    UnityEngine.Object.DestroyImmediate(tex);
                    Debug.Log($"[MirrorLake] captured {name} from {pos} -> {dir}");
                }
                UnityEngine.Object.DestroyImmediate(cam.gameObject);
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        const string CityScenePath = "Assets/_Veyra/Scenes/VeyraCity.unity";

        /// <summary>Opens VeyraCity.unity unless it is already active; in the Editor, asks to save modified scenes first.</summary>
        static void EnsureCityScene()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path == CityScenePath) return;
            if (!Application.isBatchMode && !UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new OperationCanceledException("cancelled: modified scenes were not saved");
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(CityScenePath);
        }

        /// <summary>Same reader as CityBuilder.BuildWater: the first "water" primitive with a vertex above 0.6 m.</summary>
        static bool ReadWaterPrimitive(string file, out Vector3[] pos, out int[] idx, out int primitive)
        {
            pos = null; idx = null; primitive = -1;
            using (var br = new BinaryReader(File.OpenRead(file)))
            {
                br.ReadChars(4); br.ReadUInt32();
                uint prims = br.ReadUInt32();
                for (int p = 0; p < prims; p++)
                {
                    new string(br.ReadChars((int)br.ReadUInt32()));
                    string mat = new string(br.ReadChars((int)br.ReadUInt32()));
                    uint vc = br.ReadUInt32(), ic = br.ReadUInt32(), flags = br.ReadUInt32();
                    var v = new Vector3[vc];
                    for (int i = 0; i < vc; i++) v[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    if ((flags & 1) != 0) br.ReadBytes((int)vc * 12);
                    if ((flags & 2) != 0) br.ReadBytes((int)vc * 8);
                    var t = new int[ic];
                    for (int i = 0; i < ic; i++) t[i] = (int)br.ReadUInt32();
                    if (mat != "water" || vc < 3 || !v.Any(q => q.y > 0.6f)) continue;
                    pos = v; idx = t; primitive = p;
                    return true;
                }
            }
            return false;
        }
    }
}
