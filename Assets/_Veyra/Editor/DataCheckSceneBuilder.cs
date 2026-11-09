using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.Core;
using VeyraCity.World.Data;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card S3 debug scene <c>Assets/_Veyra/Scenes/Test/T_DataCheck.unity</c>: a sphere at every registry location
    /// (colour by category), a LineRenderer along every road profile (colour by class, real class width), plus the
    /// coastline, river, lake, district outlines and the 1 km grid, seen by an orthographic top-down camera over the
    /// full 8000 x 6000 m world (north = +Z = up). Re-runnable: the scene is rebuilt from scratch, materials are reused.
    /// Editor-only code, so Shader.Find is allowed here (CLAUDE.md).
    /// </summary>
    public static class DataCheckSceneBuilder
    {
        public const string ScenePath = "Assets/_Veyra/Scenes/Test/T_DataCheck.unity";
        public const string MaterialDir = "Assets/_Veyra/Art/Materials/Debug";
        public const string MasterMapPath = "Assets/_Veyra/Art/Textures/Maps/master_world_map.png";
        const string CameraName = "TopDownCamera";

        [MenuItem("Veyra/Data/Build Data Check Scene")]
        public static void BuildMenu() => Debug.Log(Build());

        [MenuItem("Veyra/Data/Capture Data Check Top-Down")]
        public static void CaptureMenu() => Debug.Log(Capture(DefaultShotDir));

        /// <summary>Build, then capture on the next editor update (see the note on <see cref="Capture"/>).</summary>
        [MenuItem("Veyra/Data/Build And Capture Data Check")]
        public static void BuildAndCaptureMenu()
        {
            Debug.Log(Build());
            EditorApplication.delayCall += () => Debug.Log(Capture(DefaultShotDir));
        }

        static string DefaultShotDir => Path.Combine(ProjectRoot, "Screenshots", DateTime.Now.ToString("yyyy-MM-dd") + "_S3");

        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        static readonly Dictionary<string, Color> ClassColours = new Dictionary<string, Color>
        {
            { "MW", new Color(1f, 0.25f, 0.2f) }, { "EX", new Color(1f, 0.55f, 0.1f) }, { "HW", new Color(1f, 0.85f, 0.1f) },
            { "AR", new Color(0.95f, 0.95f, 0.95f) }, { "CO", new Color(0.6f, 0.6f, 0.65f) }, { "MT", new Color(0.85f, 0.35f, 0.95f) },
            { "RU", new Color(0.4f, 0.9f, 0.35f) }, { "GR", new Color(0.8f, 0.65f, 0.4f) }, { "SV", new Color(0.2f, 0.9f, 0.9f) },
            { "RW", new Color(0.45f, 0.55f, 0.75f) },
        };

        public static string Build()
        {
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException($"Scene '{EditorSceneManager.GetSceneAt(i).path}' has unsaved changes; save or discard them first.");

            var reg = LocationRegistry.Parse(VeyraDataValidator.LoadText("location_registry.csv"));
            var net = RoadNetwork.Parse(VeyraDataValidator.LoadText("road_network_geometry.json"));

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("DataCheck");

            // Locations: one sphere per registry row, coloured by category.
            var categories = reg.Categories.OrderBy(c => c, StringComparer.Ordinal).ToList();
            var locRoot = Child(root, "Locations");
            for (int c = 0; c < categories.Count; c++)
            {
                var cat = categories[c];
                var mat = Mat("M_DataCheck_Loc_" + Safe(cat), Color.HSVToRGB((c * 0.618034f) % 1f, 0.8f, 1f));
                var catRoot = Child(locRoot, cat);
                foreach (var l in reg.ByCategory(cat))
                {
                    var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    UnityEngine.Object.DestroyImmediate(s.GetComponent<Collider>());
                    s.name = $"{l.Id} {l.Name}";
                    s.transform.SetParent(catRoot.transform, false);
                    s.transform.position = l.UnityFromWorld;
                    s.transform.localScale = Vector3.one * (cat == "Landmark" ? 90f : 55f);
                    var r = s.GetComponent<MeshRenderer>();
                    r.sharedMaterial = mat;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
            }

            // Roads: the profile (the single height source), at the class width.
            var lineMat = LineMaterial();
            var roadRoot = Child(root, "Roads");
            int points = 0;
            foreach (var g in net.Roads.GroupBy(r => r.Cls).OrderBy(g => g.Key))
            {
                var clsRoot = Child(roadRoot, g.Key);
                var colour = ClassColours.TryGetValue(g.Key, out var cc) ? cc : Color.magenta;
                foreach (var road in g)
                {
                    var pts = new Vector3[road.SampleCount];
                    for (int i = 0; i < pts.Length; i++) pts[i] = road.ProfileUnity(i);
                    points += pts.Length;
                    Line(clsRoot, $"{road.Id} {road.Name}", pts, road.Closed, road.ClassInfo.WidthM, colour, lineMat);
                }
            }

            // Reference geography, drawn below the roads (lower Y = further from the top-down camera).
            var geo = Child(root, "Geography");
            var water = new Color(0.15f, 0.45f, 0.95f);
            Line(geo, "Coastline", Flat(net.Coastline, -40f), false, 30f, water, lineMat);
            float riverWidth = net.RiverWidthM != null && net.RiverWidthM.Length == 2 ? (net.RiverWidthM[0] + net.RiverWidthM[1]) * 0.5f : 60f;
            Line(geo, $"Sable River (centreline, {riverWidth:F0} m drawn)", Flat(net.RiverCentreline, -40f), false, riverWidth, water, lineMat);
            Line(geo, "Mirror Lake", Flat(net.LakePolygon, -40f), true, 25f, water, lineMat);
            var districts = Child(root, "Districts");
            foreach (var kv in net.Districts.OrderBy(k => k.Key))
                Line(districts, $"{kv.Key} {kv.Value.Name}", Flat(kv.Value.Polygon, -60f), true, 12f, Hex(kv.Value.Color), lineMat);
            var grid = Child(root, "Grid1km");
            var gridColour = new Color(0.32f, 0.34f, 0.38f);
            for (int x = 0; x <= 8; x++) Line(grid, $"x={x * 1000}", new[] { WorldCoordinates.ToUnity(x * 1000f, 0f, -80f), WorldCoordinates.ToUnity(x * 1000f, 6000f, -80f) }, false, x % 8 == 0 ? 14f : 5f, gridColour, lineMat);
            for (int y = 0; y <= 6; y++) Line(grid, $"y={y * 1000}", new[] { WorldCoordinates.ToUnity(0f, y * 1000f, -80f), WorldCoordinates.ToUnity(8000f, y * 1000f, -80f) }, false, y % 6 == 0 ? 14f : 5f, gridColour, lineMat);

            // Orthographic top-down camera over the whole world: up = +Z = north.
            var camGo = new GameObject(CameraName);
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 1000f, 0f), Quaternion.Euler(90f, 0f, 0f));
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = WorldCoordinates.WorldHeightM * 0.5f;
            cam.aspect = WorldCoordinates.WorldWidthM / WorldCoordinates.WorldHeightM;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 1200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.07f, 0.09f);
            camGo.tag = "MainCamera";

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save " + ScenePath);
            return $"Built {ScenePath}: {reg.Count} locations in {categories.Count} categories, {net.Roads.Count} road lines ({points:N0} profile points), " +
                   $"coastline, river, lake, {net.Districts.Count} district outlines, 1 km grid, camera '{CameraName}' (ortho {cam.orthographicSize} m, aspect {cam.aspect:F3})";
        }

        /// <summary>
        /// Renders the top-down camera to <c>T_DataCheck_topdown.png</c> and a side-by-side with the master map in <paramref name="outDir"/>.
        /// Call it in a later editor update than <see cref="Build"/>: MeshRenderers created in the same update are not drawn by
        /// <c>Camera.Render</c> yet (observed in S3: the first capture had road lines but none of the 209 spheres).
        /// </summary>
        public static string Capture(string outDir, int width = 2400, int height = 1800)
        {
            var active = EditorSceneManager.GetActiveScene();
            if (active.path != ScenePath)
            {
                if (active.isDirty) throw new InvalidOperationException($"Scene '{active.path}' has unsaved changes.");
                if (!File.Exists(ScenePath)) Build(); else EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            var cam = GameObject.Find(CameraName)?.GetComponent<Camera>() ?? throw new InvalidOperationException("No " + CameraName + " in " + ScenePath);
            Directory.CreateDirectory(outDir);

            var shot = Render(cam, width, height);
            string shotPath = Path.Combine(outDir, "T_DataCheck_topdown.png");
            File.WriteAllBytes(shotPath, shot.EncodeToPNG());

            // Side by side: master map (left 4800 x 3600 of 6320 x 3600 is the 8 x 6 km map; the rest is the legend) | capture.
            string sideBySide = null;
            var mapFile = Path.Combine(ProjectRoot, MasterMapPath);
            if (File.Exists(mapFile))
            {
                var map = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                map.LoadImage(File.ReadAllBytes(mapFile));
                float mapW = Mathf.Min(map.width, map.height * WorldCoordinates.WorldWidthM / WorldCoordinates.WorldHeightM);
                var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(map, rt, new Vector2(mapW / map.width, 1f), Vector2.zero);
                var mapScaled = Read(rt, width, height);
                RenderTexture.ReleaseTemporary(rt);
                var both = new Texture2D(width * 2 + 20, height, TextureFormat.RGB24, false);
                var fill = new Color32[both.width * both.height];
                for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(255, 255, 255, 255);
                both.SetPixels32(fill);
                both.SetPixels(0, 0, width, height, mapScaled.GetPixels());
                both.SetPixels(width + 20, 0, width, height, shot.GetPixels());
                both.Apply();
                sideBySide = Path.Combine(outDir, "T_DataCheck_vs_master_map.png");
                File.WriteAllBytes(sideBySide, both.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(map); UnityEngine.Object.DestroyImmediate(mapScaled); UnityEngine.Object.DestroyImmediate(both);
            }
            UnityEngine.Object.DestroyImmediate(shot);
            return $"Captured {shotPath} ({width}x{height})" + (sideBySide != null ? $" and {sideBySide}" : " (master map not found)");
        }

        static Texture2D Render(Camera cam, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.antiAliasing = 4;
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.aspect = (float)w / h;
            cam.Render();
            cam.targetTexture = prev;
            var tex = Read(rt, w, h);
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        static Texture2D Read(RenderTexture rt, int w, int h)
        {
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            return tex;
        }

        static GameObject Child(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        static void Line(GameObject parent, string name, Vector3[] pts, bool loop, float width, Color colour, Material mat)
        {
            var go = Child(parent, name);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = pts.Length;
            lr.SetPositions(pts);
            lr.loop = loop;
            lr.widthMultiplier = width;
            lr.startColor = lr.endColor = colour;
            lr.sharedMaterial = mat;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCornerVertices = 0;
            lr.numCapVertices = 0;
        }

        static Vector3[] Flat(float[][] xy, float elevation)
        {
            var pts = new Vector3[xy.Length];
            for (int i = 0; i < xy.Length; i++) pts[i] = WorldCoordinates.ToUnity(xy[i][0], xy[i][1], elevation);
            return pts;
        }

        static Material Mat(string name, Color colour)
        {
            Directory.CreateDirectory(MaterialDir);
            string path = $"{MaterialDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.GetColor("_BaseColor") != colour) { m.SetColor("_BaseColor", colour); EditorUtility.SetDirty(m); }
            return m;
        }

        /// <summary>Unlit material that multiplies by vertex colour (LineRenderer colours).</summary>
        static Material LineMaterial()
        {
            Directory.CreateDirectory(MaterialDir);
            string path = $"{MaterialDir}/M_DataCheck_Line.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "M_DataCheck_Line" };
                m.SetColor("_BaseColor", Color.white);
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;

        static string Safe(string s) => new string(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
    }
}
