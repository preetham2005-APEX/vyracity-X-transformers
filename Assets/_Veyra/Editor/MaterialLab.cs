using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.Testing;
using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card F2 F: Assets/_Veyra/Scenes/Test/T_MaterialLab.unity — sample spheres, 4 x 4 m ground tiles, facade walls, a 20 m road
    /// strip with markings, kerbs and sidewalks, street lamps and a neon sign, lit by a WorldEnvironment (sun presets until L4).
    /// The scene is rebuilt from scratch on every run (idempotent); sections appear once their palette materials exist.
    /// Capture: Day 12:00, Golden 18:48, Night 23:00 and Night wet (rain) from fixed lab cameras at 2560 x 1440, with a
    /// reflection probe re-rendered for every lighting state, plus a labelled contact sheet.
    /// </summary>
    public static class MaterialLab
    {
        public const string ScenePath = "Assets/_Veyra/Scenes/Test/T_MaterialLab.unity";
        const string SkyPath = "Assets/_Veyra/World/Generated/Materials/Sky.mat";
        const string PostProfilePath = "Assets/_Veyra/World/Generated/PostProfile.asset";

        // ------------------------------------------------------------------ meshes (UVs in metres unless stated)

        static Mesh Build(string name, List<Vector3> v, List<Vector2> uv, List<int> t)
        {
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        static void AddQuad(List<Vector3> v, List<Vector2> uv, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
            t.Add(i); t.Add(i + 2); t.Add(i + 1);
            t.Add(i); t.Add(i + 3); t.Add(i + 2);
        }

        /// <summary>Horizontal rectangle [x0,x1] x [z0,z1] at height y, planar XZ UVs in metres (like the city's roads).</summary>
        static Mesh Ground(float x0, float x1, float z0, float z1, float y, float metresPerUv = 1f)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            float k = 1f / metresPerUv;
            AddQuad(v, uv, t, new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1),
                    new Vector2(x0, z0) * k, new Vector2(x1, z0) * k, new Vector2(x1, z1) * k, new Vector2(x0, z1) * k);
            return Build("Ground", v, uv, t);
        }

        /// <summary>Axis-aligned box with per-face planar UVs in metres.</summary>
        static Mesh BoxMesh(Vector3 min, Vector3 max)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            float x0 = min.x, y0 = min.y, z0 = min.z, x1 = max.x, y1 = max.y, z1 = max.z;
            AddQuad(v, uv, t, new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1)); // top
            // Each face is listed counter-clockwise as seen from outside (bottom-left, bottom-right, top-right, top-left).
            AddQuad(v, uv, t, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1)); // -z
            AddQuad(v, uv, t, new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector2(-x1, y0), new Vector2(-x0, y0), new Vector2(-x0, y1), new Vector2(-x1, y1)); // +z
            AddQuad(v, uv, t, new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector2(-z1, y0), new Vector2(-z0, y0), new Vector2(-z0, y1), new Vector2(-z1, y1)); // -x
            AddQuad(v, uv, t, new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), new Vector2(z0, y0), new Vector2(z1, y0), new Vector2(z1, y1), new Vector2(z0, y1)); // +x
            return Build("Box", v, uv, t);
        }

        /// <summary>Vertical wall facing -Z from x0 to x1, height h; UV in facade tiles (12 m x 14 m, like BuildingGenerator).</summary>
        static Mesh FacadeWall(float x0, float x1, float h, float z)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            const float U = 12f, V = 14f;
            AddQuad(v, uv, t, new Vector3(x0, 0, z), new Vector3(x1, 0, z), new Vector3(x1, h, z), new Vector3(x0, h, z),
                    new Vector2(0, 0), new Vector2((x1 - x0) / U, 0), new Vector2((x1 - x0) / U, h / V), new Vector2(0, h / V));
            return Build("FacadeWall", v, uv, t);
        }

        /// <summary>UV sphere with UVs in metres of surface (u around, v pole to pole).</summary>
        static Mesh SphereMesh(float r, int lon = 48, int lat = 24)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int j = 0; j <= lat; j++)
            {
                float th = Mathf.PI * j / lat;
                for (int i = 0; i <= lon; i++)
                {
                    float ph = 2f * Mathf.PI * i / lon;
                    v.Add(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)) * r);
                    uv.Add(new Vector2(ph * r, (Mathf.PI - th) * r));
                }
            }
            for (int j = 0; j < lat; j++)
                for (int i = 0; i < lon; i++)
                {
                    int a = j * (lon + 1) + i, b = a + lon + 1;
                    t.Add(a); t.Add(a + 1); t.Add(b);
                    t.Add(a + 1); t.Add(b + 1); t.Add(b);
                }
            return Build("Sphere", v, uv, t);
        }

        /// <summary>Tube along +X of length L and radius r (UV u = metres along, v = 0–1 around).</summary>
        static Mesh Tube(float length, float r, int seg = 12)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int k = 0; k <= seg; k++)
            {
                float a = 2f * Mathf.PI * k / seg;
                var o = new Vector3(0f, Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                v.Add(o); uv.Add(new Vector2(0f, k / (float)seg));
                v.Add(o + Vector3.right * length); uv.Add(new Vector2(length, k / (float)seg));
            }
            for (int k = 0; k < seg; k++)
            {
                int i = k * 2;
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
                t.Add(i + 1); t.Add(i + 3); t.Add(i + 2);
            }
            return Build("Tube", v, uv, t);
        }

        static GameObject Put(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, Quaternion? rot = null, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(pos, rot ?? Quaternion.identity);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        static Material Lib(VeyraMaterialLibrary lib, VeyraMat key) => lib.TryGet(key, out var m) ? m : null;

        /// <summary>A lab-only emissive material asset (Scenes/Test/LabMaterials), created once.</summary>
        static Material LabMaterial(string name, Color emission)
        {
            const string dir = "Assets/_Veyra/Scenes/Test/LabMaterials";
            Directory.CreateDirectory(dir);
            string path = $"{dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", Color.white);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ scene

        [MenuItem("Veyra/Materials/Palette/Build Material Lab Scene")]
        static void BuildMenu() => Debug.Log("[MaterialLab] " + BuildScene());

        public static string BuildScene()
        {
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) return $"ABORT: {EditorSceneManager.GetSceneAt(i).path} has unsaved changes";
            var lib = AssetDatabase.LoadAssetAtPath<VeyraMaterialLibrary>(VeyraMaterialFactory.LibraryPath);
            if (lib == null) return "no library";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var report = new StringBuilder();
            var root = new GameObject("MaterialLab").transform;

            // Sun, sky, post, environment
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            var volGo = new GameObject("PostProcess");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostProfilePath);
            var envGo = new GameObject("WorldEnvironment");
            var env = envGo.AddComponent<WorldEnvironment>();
            env.Sun = sun;
            env.SkyboxMaterial = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
            env.PostVolume = vol;
            env.Materials = lib;
            env.TimeOfDay = 12f;
            env.StreetLightCount = 0;

            // Road strip: 20 m along X, two 3.5 m lanes, kerbs and 2.5 m sidewalks.
            var road = new GameObject("RoadStrip").transform;
            road.SetParent(root, false);
            var asphalt = Lib(lib, VeyraMat.RoadAsphaltNew);
            var kerb = Lib(lib, VeyraMat.SidewalkKerbGranite);
            var walk = Lib(lib, VeyraMat.SidewalkConcrete);
            var white = Lib(lib, VeyraMat.RoadMarkingWhite);
            var yellow = Lib(lib, VeyraMat.RoadMarkingYellow);
            var deck = Lib(lib, VeyraMat.RoadConcreteDeck);
            Put(root, "Ground", Ground(-95f, 95f, -30f, 40f, -0.02f), deck, Vector3.zero);
            if (asphalt != null)
            {
                Put(road, "Asphalt", Ground(-10f, 10f, -3.5f, 3.5f, 0f), asphalt, Vector3.zero);
                if (yellow != null)
                {
                    Put(road, "Centre_Yellow_L", Ground(-10f, 10f, 0.06f, 0.18f, 0.004f), yellow, Vector3.zero, null, false);
                    Put(road, "Centre_Yellow_R", Ground(-10f, 10f, -0.18f, -0.06f, 0.004f), yellow, Vector3.zero, null, false);
                }
                if (white != null)
                {
                    Put(road, "Edge_White_N", Ground(-10f, 10f, 3.2f, 3.35f, 0.004f), white, Vector3.zero, null, false);
                    for (float x = -10f; x < 10f; x += 6f)
                        Put(road, "Dash_White_S", Ground(x, x + 3f, -3.35f, -3.2f, 0.004f), white, Vector3.zero, null, false);
                }
                if (kerb != null)
                {
                    Put(road, "Kerb_N", BoxMesh(new Vector3(-10f, 0f, 3.5f), new Vector3(10f, 0.15f, 3.8f)), kerb, Vector3.zero);
                    Put(road, "Kerb_S", BoxMesh(new Vector3(-10f, 0f, -3.8f), new Vector3(10f, 0.15f, -3.5f)), kerb, Vector3.zero);
                }
                if (walk != null)
                {
                    Put(road, "Sidewalk_N", BoxMesh(new Vector3(-10f, 0f, 3.8f), new Vector3(10f, 0.15f, 6.3f)), walk, Vector3.zero);
                    Put(road, "Sidewalk_S", BoxMesh(new Vector3(-10f, 0f, -6.3f), new Vector3(10f, 0.15f, -3.8f)), walk, Vector3.zero);
                }
                report.AppendLine("road strip: 20 m, markings, kerbs, sidewalks");
            }

            // 4 x 4 m ground tiles of every road / sidewalk material.
            var tiles = new GameObject("GroundTiles").transform;
            tiles.SetParent(root, false);
            var groundKeys = Enum.GetValues(typeof(VeyraMat)).Cast<VeyraMat>()
                .Where(k => lib.TryGet(k, out var gm) && gm.shader.name == "Veyra/Road").ToList();
            for (int i = 0; i < groundKeys.Count; i++)
            {
                float x = -24f + i * 4.4f;
                // Roof materials are tiled for building tops, whose UVs are in 8 m units.
                float uvm = VeyraMaterialLibrary.CategoryOf(groundKeys[i]) == VeyraMatCategory.Building ? 8f : 1f;
                Put(tiles, "Tile_" + groundKeys[i], Ground(x, x + 4f, -14f, -10f, 0.005f, uvm), Lib(lib, groundKeys[i]), Vector3.zero);
            }
            report.AppendLine($"ground tiles: {groundKeys.Count}");

            // Spheres of every palette material (all categories except Effects/Debug), on the north sidewalk side.
            var spheres = new GameObject("Spheres").transform;
            spheres.SetParent(root, false);
            var sphereKeys = Enum.GetValues(typeof(VeyraMat)).Cast<VeyraMat>()
                .Where(k => k != VeyraMat.None && VeyraMaterialLibrary.CategoryOf(k) != VeyraMatCategory.Effects && VeyraMaterialLibrary.CategoryOf(k) != VeyraMatCategory.Debug)
                .Where(k => lib.TryGet(k, out _)).ToList();
            var sphereMesh = SphereMesh(0.7f);
            for (int i = 0; i < sphereKeys.Count; i++)
            {
                int row = i / 12, col = i % 12;
                Put(spheres, "Sphere_" + sphereKeys[i], sphereMesh, Lib(lib, sphereKeys[i]), new Vector3(-13.2f + col * 2.4f, 0.85f, 9f + row * 2.4f));
            }
            report.AppendLine($"spheres: {sphereKeys.Count}");

            // Facade walls (F2b): 12 m x 14 m samples (one UV tile) behind the spheres.
            var walls = new GameObject("FacadeWalls").transform;
            walls.SetParent(root, false);
            var wallKeys = Enum.GetValues(typeof(VeyraMat)).Cast<VeyraMat>()
                .Where(k => lib.TryGet(k, out var wm) && wm.shader.name == "Veyra/Facade").ToList();
            for (int i = 0; i < wallKeys.Count; i++)
            {
                float x = -75f + i * 12.5f;
                Put(walls, "Wall_" + wallKeys[i], FacadeWall(x, x + 12f, 14f, 18f), Lib(lib, wallKeys[i]), Vector3.zero);
            }
            report.AppendLine($"facade walls: {wallKeys.Count}");

            // Neon sign (F2c) above the south sidewalk.
            var neonKeys = Enum.GetValues(typeof(VeyraMat)).Cast<VeyraMat>()
                .Where(k => VeyraMaterialLibrary.CategoryOf(k) == VeyraMatCategory.Neon).Where(k => lib.TryGet(k, out _)).ToList();
            if (neonKeys.Count > 0)
            {
                var sign = new GameObject("NeonSign").transform;
                sign.SetParent(root, false);
                var tube = Tube(4f, 0.035f);
                for (int i = 0; i < neonKeys.Count; i++)
                {
                    var k = neonKeys[i];
                    var m = Lib(lib, k);
                    if (k.ToString() == "LedScreen" || k.ToString() == "SignBacklit")
                        Put(sign, k.ToString(), FacadeWall(0f, 3f, 1.6f, 0f), m, new Vector3(-9f + i * 3.4f, 2.6f, -7.2f), null, false);
                    else
                        Put(sign, k.ToString(), tube, m, new Vector3(-9f, 3.0f + i * 0.35f, -7.0f), null, false);
                }
                report.AppendLine($"neon sign: {neonKeys.Count} materials");
            }

            // Street lamps (warm 4000 K point lights) for the night shots, and a magenta accent light.
            var lamps = new GameObject("Lamps").transform;
            lamps.SetParent(root, false);
            var lampHead = LabMaterial("Lab_LampHead", new Color(1f, 0.82f, 0.62f) * 14f);
            var pole = Lib(lib, VeyraMat.RoadConcreteDeck);
            foreach (var x in new[] { -6f, 5f })
            {
                var l = new GameObject("StreetLamp").AddComponent<Light>();
                l.transform.SetParent(lamps, false);
                l.transform.localPosition = new Vector3(x, 5.8f, 4.6f);
                l.type = LightType.Point;
                l.range = 24f;
                l.intensity = 40f;
                l.color = new Color(1f, 0.82f, 0.62f);
                l.shadows = LightShadows.None;
                Put(lamps, "LampHead", BoxMesh(new Vector3(-0.35f, 0f, -0.18f), new Vector3(0.35f, 0.12f, 0.18f)), lampHead, new Vector3(x, 6.0f, 4.6f), null, false);
                Put(lamps, "LampPole", BoxMesh(new Vector3(-0.08f, 0f, -0.08f), new Vector3(0.08f, 6.1f, 0.08f)), pole, new Vector3(x, 0.15f, 5.1f));
            }
            var accent = new GameObject("NeonAccent").AddComponent<Light>();
            accent.transform.SetParent(lamps, false);
            accent.transform.localPosition = new Vector3(-7f, 3.2f, -6.6f);
            accent.type = LightType.Point;
            accent.range = 12f;
            accent.intensity = 3f;
            accent.color = new Color(1f, 0.1f, 0.55f);

            // Reflection probe over the road (re-rendered per lighting state by the capture).
            var probeGo = new GameObject("ReflectionProbe");
            probeGo.transform.SetParent(root, false);
            probeGo.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom;
            probe.size = new Vector3(60f, 20f, 40f);
            probe.boxProjection = true;
            probe.importance = 1;

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            return $"{ScenePath} rebuilt\n{report}";
        }

        // ------------------------------------------------------------------ capture

        public sealed class Shot { public string Name; public Vector3 Pos, LookAt; public float Fov = 55f; }
        public sealed class LightState { public string Name; public float Hour; public float Wetness; public bool Rain; }

        public static readonly Shot[] Shots =
        {
            new Shot { Name = "Road", Pos = new Vector3(-12f, 1.6f, -1.6f), LookAt = new Vector3(6f, 0.2f, 1.2f), Fov = 58f },
            new Shot { Name = "Tiles", Pos = new Vector3(-6f, 6.5f, -22f), LookAt = new Vector3(-6f, 0f, -12f), Fov = 62f },
            new Shot { Name = "Spheres", Pos = new Vector3(-1.2f, 2.0f, -1.5f), LookAt = new Vector3(-1.2f, 1.0f, 9.5f), Fov = 55f },
            new Shot { Name = "FacadesL", Pos = new Vector3(-50f, 7f, -14f), LookAt = new Vector3(-50f, 7f, 18f), Fov = 52f },
            new Shot { Name = "FacadesC", Pos = new Vector3(0f, 7f, -14f), LookAt = new Vector3(0f, 7f, 18f), Fov = 52f },
            new Shot { Name = "FacadesR", Pos = new Vector3(50f, 7f, -14f), LookAt = new Vector3(50f, 7f, 18f), Fov = 52f },
            new Shot { Name = "FacadesAngle", Pos = new Vector3(-82f, 3f, 6f), LookAt = new Vector3(-58f, 7f, 18f), Fov = 60f },
            new Shot { Name = "Neon", Pos = new Vector3(-5f, 2.2f, 2.5f), LookAt = new Vector3(-6f, 2.4f, -7f), Fov = 60f },
        };

        public static readonly LightState[] States =
        {
            new LightState { Name = "Day", Hour = 12f },
            new LightState { Name = "Golden", Hour = 18.8f },
            new LightState { Name = "Night", Hour = 23f },
            new LightState { Name = "NightWet", Hour = 23f, Wetness = 1f, Rain = true },
        };

        enum Phase { NewState, WaitState, Place, WaitShot }

        static State run;
        sealed class State
        {
            public List<(LightState st, Shot shot)> todo;
            public int index, wait;
            public Phase phase;
            public Camera cam, probeCam;
            public WorldEnvironment env;
            public ReflectionProbe probe;
            public RenderTexture probeRT;
            public string dir, card;
            public string lastState;
            public readonly List<(string, Texture2D)> shots = new List<(string, Texture2D)>();
            public readonly StringBuilder log = new StringBuilder();
        }

        public static bool IsRunning => run != null;
        public static string LastReport { get; private set; } = "";

        [MenuItem("Veyra/Materials/Palette/Capture Material Lab")]
        static void CaptureMenu() => Debug.Log("[MaterialLab] " + StartCapture("F2"));

        /// <summary>Captures every shot whose name is in <paramref name="shotNames"/> (null = all) in every light state.</summary>
        public static string StartCapture(string card, string[] shotNames = null)
        {
            if (run != null) return "busy";
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
            var s = new State
            {
                env = UnityEngine.Object.FindAnyObjectByType<WorldEnvironment>(),
                probe = UnityEngine.Object.FindAnyObjectByType<ReflectionProbe>(),
                card = card,
                dir = ReviewCameraRig.OutputDir(Path.GetDirectoryName(Application.dataPath), card, null),
            };
            s.todo = States.SelectMany(st => Shots.Where(sh => shotNames == null || shotNames.Contains(sh.Name)).Select(sh => (st, sh))).ToList();
            s.cam = ReviewCameraRig.CreateCamera("[LabCamera]");
            s.cam.gameObject.hideFlags = HideFlags.HideAndDontSave;
            Directory.CreateDirectory(s.dir);
            s.log.AppendLine($"Material lab capture — {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {card} — {ScenePath} — Unity {Application.unityVersion}");
            run = s;
            EditorApplication.update += Tick;
            return $"started: {s.todo.Count} shots -> {s.dir}";
        }

        static void RefreshProbe(State s)
        {
            if (s.probe == null) return;
            if (s.probeRT == null)
            {
                s.probeRT = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGBHalf) { dimension = TextureDimension.Cube, useMipMap = true, autoGenerateMips = true, hideFlags = HideFlags.HideAndDontSave };
                s.probeCam = new GameObject("[ProbeCamera]") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();
                s.probeCam.enabled = false;
                s.probeCam.farClipPlane = 2000f;
                s.probeCam.clearFlags = CameraClearFlags.Skybox;
            }
            s.probeCam.transform.position = s.probe.transform.position;
            s.probeCam.RenderToCubemap(s.probeRT);
            s.probe.customBakedTexture = s.probeRT;
        }

        static void Tick()
        {
            var s = run;
            if (s == null) { EditorApplication.update -= Tick; return; }
            try
            {
                if (s.index >= s.todo.Count) { Finish(); return; }
                var (st, shot) = s.todo[s.index];
                switch (s.phase)
                {
                    case Phase.NewState:
                        if (s.lastState == st.Name) { s.phase = Phase.Place; return; }
                        s.env.SetWeather(st.Rain ? WorldEnvironment.WeatherKind.Rain : WorldEnvironment.WeatherKind.Clear);
                        s.env.SetTime(st.Hour);
                        s.env.SetWetness(st.Wetness);
                        s.wait = 4;
                        s.phase = Phase.WaitState;
                        return;
                    case Phase.WaitState:
                        if (--s.wait > 0) return;
                        RefreshProbe(s);
                        s.lastState = st.Name;
                        s.phase = Phase.Place;
                        return;
                    case Phase.Place:
                        var pose = new ReviewPose { ok = true, camPos = shot.Pos, camRot = Quaternion.LookRotation(shot.LookAt - shot.Pos), fov = shot.Fov };
                        ReviewCameraRig.Apply(s.cam, pose);
                        s.wait = 3;
                        s.phase = Phase.WaitShot;
                        return;
                    case Phase.WaitShot:
                        if (--s.wait > 0) return;
                        var tex = ReviewCameraRig.Render(s.cam);
                        string name = $"{shot.Name}_{st.Name}";
                        ReviewCameraRig.SavePng(tex, Path.Combine(s.dir, name + ".png"));
                        s.shots.Add((name, tex));
                        s.log.AppendLine($"OK    {name}: cam {shot.Pos} -> {shot.LookAt}, {st.Hour:00.00} h, wetness {st.Wetness}, rain {st.Rain}");
                        s.index++;
                        s.phase = Phase.NewState;
                        return;
                }
            }
            catch (Exception ex)
            {
                s.log.AppendLine("ERROR " + ex);
                Debug.LogException(ex);
                Finish();
            }
        }

        static void Finish()
        {
            var s = run;
            EditorApplication.update -= Tick;
            run = null;
            if (s == null) return;
            try
            {
                var sheet = ReviewCameraRig.ContactSheet(s.shots, $"{s.card} material lab");
                ReviewCameraRig.SavePng(sheet, Path.Combine(s.dir, "contact_sheet.png"));
                UnityEngine.Object.DestroyImmediate(sheet);
                s.log.AppendLine($"contact sheet: {Path.Combine(s.dir, "contact_sheet.png")} ({s.shots.Count} shots)");
            }
            finally
            {
                foreach (var (_, t) in s.shots) if (t != null) UnityEngine.Object.DestroyImmediate(t);
                if (s.cam != null) UnityEngine.Object.DestroyImmediate(s.cam.gameObject);
                if (s.probeCam != null) UnityEngine.Object.DestroyImmediate(s.probeCam.gameObject);
                if (s.probe != null) s.probe.customBakedTexture = null;
                if (s.probeRT != null) UnityEngine.Object.DestroyImmediate(s.probeRT);
                if (s.env != null) { s.env.SetWeather(WorldEnvironment.WeatherKind.Clear); s.env.SetTime(12f); s.env.SetWetness(0f); }
                var scene = EditorSceneManager.GetActiveScene();
                if (scene.isDirty) EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single); // discard capture state
                LastReport = s.log.ToString();
                File.WriteAllText(Path.Combine(s.dir, "lab.txt"), LastReport, new UTF8Encoding(false));
                Debug.Log("[MaterialLab] " + LastReport);
            }
        }
    }
}
