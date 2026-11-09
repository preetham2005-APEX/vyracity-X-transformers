using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VeyraCity.Assists;
using VeyraCity.Gameplay;
using VeyraCity.Input;
using VeyraCity.UI;
using VeyraCity.VehiclePhysics;
using Object = UnityEngine.Object;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Generates the Phase 2 physics test environment (VS-2): a 2.4 km straight, a 360 m test pad
    /// with a 50 m skidpad, slalom, bumps, kerb and jump ramp, surface strips (concrete, wet, gravel, dirt,
    /// grass), distance markers and horizon blocks for speed perception. Procedural, tileable PBR textures.
    /// Deterministic: rebuilding gives the same scene.
    /// </summary>
    public static class TestTrackBuilder
    {
        public const string ScenePath = "Assets/_Veyra/Scenes/PhysicsTestTrack.unity";
        public const string KiteStockPrefabPath = "Assets/_Veyra/Art/Vehicles/ArdenKite/ArdenKite_Stock.prefab";
        const string Art = "Assets/_Veyra/Art/TestTrack";
        const int TexSize = 1024;

        [MenuItem("Veyra/Build Physics Test Track Scene")]
        public static void BuildMenu() => Build();

        public static void BuildBatch()
        {
            int code = 0;
            try
            {
                VehicleDataTools.EnsureDefaultData(false);
                Build();
            }
            catch (Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        class Mats
        {
            public Material Asphalt, Wet, Concrete, Gravel, Dirt, Grass, Line, Kerb, Cone, Post, CarBody, CarWheel;
            public Material[] Buildings;
        }

        public static void Build()
        {
            Directory.CreateDirectory(Art);
            var m = CreateMaterials();
            var volumeProfile = CreateVolumeProfile();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- Lighting and atmosphere ----
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.5f;
            sun.color = new Color(1f, 0.955f, 0.89f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.9f;
            sunGo.transform.rotation = Quaternion.Euler(38f, 58f, 0f);
            RenderSettings.sun = sun;
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 350f;
            RenderSettings.fogEndDistance = 2800f;
            RenderSettings.fogColor = new Color(0.72f, 0.78f, 0.86f);

            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = volumeProfile;

            // ---- Camera ----
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 4000f;
            cam.fieldOfView = 60f;
            camGo.AddComponent<AudioListener>();
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var chase = camGo.AddComponent<ChaseCamera>();
            camGo.transform.position = new Vector3(0f, 2.5f, -268f);

            // ---- Ground ----
            var world = new GameObject("Track").transform;
            var dry = VehicleDataTools.LoadSurface("dry_asphalt");
            Slab("Grass_Base", world, new Vector2(0f, 600f), new Vector2(6000f, 6000f), -0.02f, m.Grass, 4f, VehicleDataTools.LoadSurface("grass"));
            Slab("Straight", world, new Vector2(0f, 900f), new Vector2(30f, 2400f), 0f, m.Asphalt, 8f, dry);
            Slab("Pad", world, new Vector2(-195f, 100f), new Vector2(360f, 360f), 0f, m.Asphalt, 8f, dry);
            Slab("Strip_Concrete", world, new Vector2(30f, 100f), new Vector2(30f, 200f), 0f, m.Concrete, 6f, VehicleDataTools.LoadSurface("concrete"));
            Slab("Strip_Wet", world, new Vector2(30f, 300f), new Vector2(30f, 200f), 0f, m.Wet, 8f, VehicleDataTools.LoadSurface("wet_asphalt"));
            Slab("Strip_Gravel", world, new Vector2(30f, 500f), new Vector2(30f, 200f), 0f, m.Gravel, 5f, VehicleDataTools.LoadSurface("gravel"));
            Slab("Strip_Dirt", world, new Vector2(30f, 700f), new Vector2(30f, 200f), 0f, m.Dirt, 6f, VehicleDataTools.LoadSurface("dirt"));
            Slab("Pad_Wet", world, new Vector2(-195f, -40f), new Vector2(120f, 70f), 0.001f, m.Wet, 8f, VehicleDataTools.LoadSurface("wet_asphalt"), withCollider: false);

            // ---- Markings ----
            var quads = new List<(Vector3 c, Vector2 s, float yaw)>();
            for (float z = -295f; z < 2095f; z += 12f) quads.Add((new Vector3(0f, 0f, z), new Vector2(0.18f, 4f), 0f));
            quads.Add((new Vector3(-14.4f, 0f, 900f), new Vector2(0.22f, 2400f), 0f));
            quads.Add((new Vector3(14.4f, 0f, 900f), new Vector2(0.22f, 2400f), 0f));
            for (int i = 0; i <= 10; i++) quads.Add((new Vector3(0f, 0f, 1000f + i * 10f), new Vector2(28.5f, i % 5 == 0 ? 0.6f : 0.25f), 0f));
            quads.Add((new Vector3(0f, 0f, -250f), new Vector2(28.5f, 0.5f), 0f)); // start line
            MeshObject("Markings", world, BuildQuads(quads, 0.004f), m.Line, false);
            MeshObject("Skidpad_Ring_50m", world, BuildRing(new Vector3(-195f, 0.004f, 100f), 50f, 0.3f, 256), m.Line, false);
            MeshObject("Skidpad_Ring_45m", world, BuildRing(new Vector3(-195f, 0.004f, 100f), 45f, 0.12f, 256), m.Line, false);
            MeshObject("Skidpad_Ring_55m", world, BuildRing(new Vector3(-195f, 0.004f, 100f), 55f, 0.12f, 256), m.Line, false);

            // ---- Features ----
            var cone = BuildCone();
            for (int i = 0; i < 6; i++)
            {
                var c = MeshObject($"Cone_{i + 1}", world, cone, m.Cone, false);
                c.transform.position = new Vector3(-50f, 0f, 150f + 18f * i);
                var mc = c.AddComponent<MeshCollider>();
                mc.sharedMesh = cone;
                mc.convex = true;
                var crb = c.AddComponent<Rigidbody>();
                crb.mass = 3f;
                GameObjectUtility.SetStaticEditorFlags(c, 0);
            }
            for (int i = 0; i < 3; i++)
                Block($"SpeedBump_{i + 1}", world, new Vector3(-330f, 0.04f, -40f + 20f * i), new Vector3(7f, 0.08f, 0.6f), Quaternion.identity, m.Kerb, dry);
            Block("Kerb_150mm", world, new Vector3(-350f, 0.075f, 40f), new Vector3(1.2f, 0.15f, 60f), Quaternion.identity, m.Kerb, VehicleDataTools.LoadSurface("concrete"));
            Block("Jump_Ramp", world, new Vector3(-300f, 0.555f, 200f), new Vector3(8f, 0.6f, 14f), Quaternion.Euler(-7f, 0f, 0f), m.Concrete, VehicleDataTools.LoadSurface("concrete"));

            // Distance markers every 100 m on both sides of the straight; taller every 500 m.
            for (int i = 0; i <= 23; i++)
            {
                float z = -250f + i * 100f;
                bool major = i % 5 == 0;
                foreach (float x in new[] { -17.5f, 17.5f })
                {
                    var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    post.name = $"Marker_{i * 100}m";
                    Object.DestroyImmediate(post.GetComponent<Collider>());
                    post.transform.SetParent(world, false);
                    post.transform.localScale = new Vector3(major ? 0.35f : 0.18f, major ? 2.2f : 0.7f, major ? 0.35f : 0.18f);
                    post.transform.position = new Vector3(x, post.transform.localScale.y, z);
                    post.GetComponent<Renderer>().sharedMaterial = m.Post;
                    GameObjectUtility.SetStaticEditorFlags(post, StaticEditorFlags.BatchingStatic);
                }
            }

            // Horizon blocks (no colliders) for parallax and speed perception.
            var rnd = new System.Random(1607);
            var horizon = new GameObject("Horizon").transform;
            horizon.SetParent(world, false);
            int placed = 0;
            while (placed < 70)
            {
                float x = (float)(rnd.NextDouble() * 4400 - 2200);
                float z = (float)(rnd.NextDouble() * 4400 - 1600);
                bool clear = Mathf.Abs(x) > 480f || z > 2350f || z < -560f;
                if (!clear) continue;
                float w = 30f + (float)rnd.NextDouble() * 110f, d = 30f + (float)rnd.NextDouble() * 110f, h = 12f + (float)rnd.NextDouble() * 85f;
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = $"Block_{placed:00}";
                Object.DestroyImmediate(b.GetComponent<Collider>());
                b.transform.SetParent(horizon, false);
                b.transform.localScale = new Vector3(w, h, d);
                b.transform.position = new Vector3(x, h * 0.5f - 0.02f, z);
                b.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 90f, 0f);
                b.GetComponent<Renderer>().sharedMaterial = m.Buildings[placed % m.Buildings.Length];
                GameObjectUtility.SetStaticEditorFlags(b, StaticEditorFlags.BatchingStatic);
                placed++;
            }

            // ---- Spawn and director ----
            var spawn = new GameObject("Spawn").transform;
            spawn.position = new Vector3(0f, 0f, -262f);
            spawn.rotation = Quaternion.identity;

            var dirGo = new GameObject("TestTrackDirector");
            var director = dirGo.AddComponent<TestTrackDirector>();
            dirGo.AddComponent<PlayerDriverInput>();
            dirGo.AddComponent<TelemetryOverlay>();
            var so = new SerializedObject(director);
            var kitePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(KiteStockPrefabPath);
            var vehiclesProp = so.FindProperty("vehicles");
            vehiclesProp.arraySize = VehicleDataTools.VehicleIds.Length;
            for (int i = 0; i < VehicleDataTools.VehicleIds.Length; i++)
            {
                var e = vehiclesProp.GetArrayElementAtIndex(i);
                string id = VehicleDataTools.VehicleIds[i];
                e.FindPropertyRelative("Definition").objectReferenceValue = VehicleDataTools.LoadVehicle(id);
                e.FindPropertyRelative("VisualPrefab").objectReferenceValue = id == "Kite_Stock_RWD" ? kitePrefab : null;
            }
            var presetsProp = so.FindProperty("assistPresets");
            presetsProp.arraySize = 2;
            presetsProp.GetArrayElementAtIndex(0).objectReferenceValue = VehicleDataTools.LoadPreset("Casual");
            presetsProp.GetArrayElementAtIndex(1).objectReferenceValue = VehicleDataTools.LoadPreset("Advanced");
            so.FindProperty("defaultSurface").objectReferenceValue = dry;
            so.FindProperty("spawnPoint").objectReferenceValue = spawn;
            so.FindProperty("chaseCamera").objectReferenceValue = chase;
            so.FindProperty("placeholderBody").objectReferenceValue = m.CarBody;
            so.FindProperty("placeholderWheel").objectReferenceValue = m.CarWheel;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Environment lighting only (no static GI contributors, so no lightmaps).
            Lightmapping.bakedGI = false;
            Lightmapping.realtimeGI = false;
            try { Lightmapping.Bake(); } catch (Exception e) { Debug.LogWarning("[Veyra] Environment bake skipped: " + e.Message); }

            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log(saved ? $"[Veyra] Built {ScenePath} (Kite model: {(kitePrefab != null ? "yes" : "placeholder")})" : $"[Veyra] FAILED to save {ScenePath}");
            RegisterBuildScenes();
        }

        public static void RegisterBuildScenes()
        {
            var list = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            if (File.Exists(SceneTools.FoundationScenePath)) list.Add(new EditorBuildSettingsScene(SceneTools.FoundationScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            AssetDatabase.SaveAssets(); // EditorBuildSettings is only written to ProjectSettings on save
            Debug.Log("[Veyra] Build scenes: " + string.Join(", ", list.ConvertAll(s => s.path)));
        }

        public static void RegisterBuildScenesBatch()
        {
            RegisterBuildScenes();
            EditorApplication.Exit(0);
        }

        // ================= geometry helpers =================
        static GameObject Slab(string name, Transform parent, Vector2 centreXZ, Vector2 sizeXZ, float y, Material mat, float tileMeters,
                               SurfaceDefinition surface, bool withCollider = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(centreXZ.x, y, centreXZ.y);
            var mesh = new Mesh { name = name };
            float hx = sizeXZ.x * 0.5f, hz = sizeXZ.y * 0.5f;
            var v = new[] { new Vector3(-hx, 0, -hz), new Vector3(-hx, 0, hz), new Vector3(hx, 0, hz), new Vector3(hx, 0, -hz) };
            var uv = new Vector2[4];
            for (int i = 0; i < 4; i++) uv[i] = new Vector2((v[i].x + centreXZ.x) / tileMeters, (v[i].z + centreXZ.y) / tileMeters);
            mesh.vertices = v;
            mesh.uv = uv;
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (withCollider)
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, -0.5f, 0f);
                bc.size = new Vector3(sizeXZ.x, 1f, sizeXZ.y);
                go.AddComponent<VehicleSurface>().Surface = surface;
            }
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        static void Block(string name, Transform parent, Vector3 pos, Vector3 size, Quaternion rot, Material mat, SurfaceDefinition surface)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.AddComponent<VehicleSurface>().Surface = surface;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }

        static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material mat, bool isStatic = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            if (isStatic) GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        static Mesh BuildQuads(List<(Vector3 c, Vector2 s, float yaw)> quads, float y)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            foreach (var q in quads)
            {
                var rot = Quaternion.Euler(0f, q.yaw, 0f);
                float hx = q.s.x * 0.5f, hz = q.s.y * 0.5f;
                int b = verts.Count;
                verts.Add(q.c + rot * new Vector3(-hx, y, -hz));
                verts.Add(q.c + rot * new Vector3(-hx, y, hz));
                verts.Add(q.c + rot * new Vector3(hx, y, hz));
                verts.Add(q.c + rot * new Vector3(hx, y, -hz));
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(1, 0));
                tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            var mesh = new Mesh { name = "Markings", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh BuildRing(Vector3 centre, float radius, float width, int segments)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                verts.Add(centre + dir * (radius - width * 0.5f));
                verts.Add(centre + dir * (radius + width * 0.5f));
                if (i < segments)
                {
                    int b = i * 2;
                    tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
                }
            }
            var mesh = new Mesh { name = $"Ring_{radius}m" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh BuildCone()
        {
            const int seg = 20;
            const float r = 0.17f, h = 0.7f, plate = 0.2f, plateH = 0.03f;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            int apex = 0;
            verts.Add(new Vector3(0f, h, 0f));
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(a) * r, plateH, Mathf.Sin(a) * r));
            }
            for (int i = 0; i < seg; i++) tris.AddRange(new[] { apex, 1 + (i + 1) % seg, 1 + i });
            int pb = verts.Count;
            verts.AddRange(new[]
            {
                new Vector3(-plate, plateH, -plate), new Vector3(-plate, plateH, plate), new Vector3(plate, plateH, plate), new Vector3(plate, plateH, -plate),
                new Vector3(-plate, 0f, -plate), new Vector3(-plate, 0f, plate), new Vector3(plate, 0f, plate), new Vector3(plate, 0f, -plate),
            });
            tris.AddRange(new[] { pb, pb + 1, pb + 2, pb, pb + 2, pb + 3 });
            for (int i = 0; i < 4; i++)
            {
                int a0 = pb + i, a1 = pb + (i + 1) % 4, b0 = pb + 4 + i, b1 = pb + 4 + (i + 1) % 4;
                tris.AddRange(new[] { a0, b0, b1, a0, b1, a1 });
            }
            var mesh = new Mesh { name = "TrafficCone" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ================= materials and textures =================
        static Mats CreateMaterials()
        {
            var m = new Mats();
            var asphaltH = Height(seed: 11, speckle: 0.55f, rough: 0.6f);
            m.Asphalt = Lit("M_TT_Asphalt", Tex("T_Asphalt_D", TexSize, (x, y) => AsphaltColour(asphaltH, x, y, wet: false)),
                            Normal("T_Asphalt_N", asphaltH, 2.2f), 0f, 0.38f, albedoSmoothness: true);
            m.Wet = Lit("M_TT_AsphaltWet", Tex("T_AsphaltWet_D", TexSize, (x, y) => AsphaltColour(asphaltH, x, y, wet: true)),
                        Normal("T_AsphaltWet_N", asphaltH, 1.1f), 0f, 0.92f, albedoSmoothness: true);
            var concreteH = Height(seed: 23, speckle: 0.15f, rough: 0.35f);
            m.Concrete = Lit("M_TT_Concrete", Tex("T_Concrete_D", TexSize, (x, y) => ConcreteColour(concreteH, x, y)),
                             Normal("T_Concrete_N", concreteH, 1.4f), 0f, 0.3f, albedoSmoothness: true);
            var gravelH = Height(seed: 37, speckle: 1f, rough: 1f);
            m.Gravel = Lit("M_TT_Gravel", Tex("T_Gravel_D", TexSize, (x, y) => Tint(gravelH, x, y, new Color(0.42f, 0.39f, 0.35f), new Color(0.62f, 0.6f, 0.56f), 0.12f)),
                           Normal("T_Gravel_N", gravelH, 4f), 0f, 0.12f, albedoSmoothness: true);
            var dirtH = Height(seed: 41, speckle: 0.3f, rough: 0.8f);
            m.Dirt = Lit("M_TT_Dirt", Tex("T_Dirt_D", TexSize, (x, y) => Tint(dirtH, x, y, new Color(0.30f, 0.22f, 0.15f), new Color(0.48f, 0.37f, 0.26f), 0.1f)),
                         Normal("T_Dirt_N", dirtH, 2.5f), 0f, 0.1f, albedoSmoothness: true);
            var grassH = Height(seed: 53, speckle: 0.8f, rough: 0.9f);
            m.Grass = Lit("M_TT_Grass", Tex("T_Grass_D", TexSize, (x, y) => Tint(grassH, x, y, new Color(0.17f, 0.27f, 0.09f), new Color(0.33f, 0.45f, 0.17f), 0.08f)),
                          Normal("T_Grass_N", grassH, 2f), 0f, 0.08f, albedoSmoothness: true);
            m.Kerb = Lit("M_TT_Kerb", Tex("T_Kerb_D", 256, (x, y) => ((x / 64) % 2 == 0) ? new Color(0.75f, 0.08f, 0.06f, 0.35f) : new Color(0.88f, 0.88f, 0.86f, 0.35f)),
                         null, 0f, 0.35f, albedoSmoothness: true);
            m.Line = Lit("M_TT_LinePaint", null, null, 0f, 0.35f, colour: new Color(0.86f, 0.86f, 0.84f));
            m.Cone = Lit("M_TT_Cone", null, null, 0f, 0.45f, colour: new Color(0.95f, 0.36f, 0.05f));
            m.Post = Lit("M_TT_MarkerPost", null, null, 0f, 0.4f, colour: new Color(0.95f, 0.78f, 0.1f));
            m.CarBody = Lit("M_Placeholder_CarBody", null, null, 0.6f, 0.78f, colour: new Color(0.55f, 0.06f, 0.08f));
            m.CarWheel = Lit("M_Placeholder_Wheel", null, null, 0f, 0.2f, colour: new Color(0.06f, 0.06f, 0.065f));
            m.Buildings = new[]
            {
                Lit("M_TT_Block_A", null, null, 0f, 0.25f, colour: new Color(0.55f, 0.57f, 0.6f)),
                Lit("M_TT_Block_B", null, null, 0f, 0.3f, colour: new Color(0.42f, 0.47f, 0.55f)),
                Lit("M_TT_Block_C", null, null, 0f, 0.2f, colour: new Color(0.62f, 0.58f, 0.52f)),
            };
            AssetDatabase.SaveAssets();
            return m;
        }

        static Material Lit(string name, Texture2D albedo, Texture2D normal, float metallic, float smoothness,
                            Color? colour = null, bool albedoSmoothness = false)
        {
            string path = $"{Art}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = mat == null;
            if (isNew) mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.name = name;
            mat.SetColor("_BaseColor", colour ?? Color.white);
            if (albedo != null) mat.SetTexture("_BaseMap", albedo);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.SetFloat("_BumpScale", 1f);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (albedoSmoothness && albedo != null)
            {
                mat.SetFloat("_SmoothnessTextureChannel", 1f);
                mat.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            }
            if (isNew) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            return mat;
        }

        // Periodic value noise: tiles seamlessly at the texture border.
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        static float ValueNoise(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int Wrap(int i) => ((i % period) + period) % period;
            float a = Hash(Wrap(x0), Wrap(y0), seed), b = Hash(Wrap(x0 + 1), Wrap(y0), seed);
            float c = Hash(Wrap(x0), Wrap(y0 + 1), seed), d = Hash(Wrap(x0 + 1), Wrap(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float[] Height(int seed, float speckle, float rough)
        {
            var h = new float[TexSize * TexSize];
            for (int y = 0; y < TexSize; y++)
            for (int x = 0; x < TexSize; x++)
            {
                float u = x / (float)TexSize, v = y / (float)TexSize;
                float macro = ValueNoise(u, v, 4, seed) * 0.5f + ValueNoise(u, v, 8, seed + 1) * 0.3f + ValueNoise(u, v, 16, seed + 2) * 0.2f;
                float fine = ValueNoise(u, v, 128, seed + 3) * 0.5f + ValueNoise(u, v, 256, seed + 4) * 0.5f;
                float grain = Hash(x, y, seed + 5);
                float stones = Mathf.Clamp01((ValueNoise(u, v, 512, seed + 6) - 0.62f) * 4f);
                h[y * TexSize + x] = Mathf.Clamp01(0.25f * macro + rough * 0.35f * fine + speckle * 0.4f * stones + 0.08f * grain);
            }
            return h;
        }

        static Color AsphaltColour(float[] h, int x, int y, bool wet)
        {
            float u = x / (float)TexSize, v = y / (float)TexSize;
            float k = h[y * TexSize + x];
            float wear = ValueNoise(u, v, 6, 97);
            float baseL = Mathf.Lerp(0.085f, 0.16f, k) * Mathf.Lerp(0.82f, 1.12f, wear);
            float agg = Mathf.Clamp01((k - 0.55f) * 3f);
            float l = Mathf.Lerp(baseL, 0.33f, agg * 0.6f);
            if (wet)
            {
                l *= 0.62f;
                float puddle = Mathf.Clamp01((ValueNoise(u, v, 5, 131) - 0.48f) * 4f);
                float smooth = Mathf.Lerp(0.72f, 0.97f, puddle);
                return new Color(l * 0.97f, l * 0.98f, l * 1.03f, smooth);
            }
            float s = Mathf.Lerp(0.22f, 0.42f, agg) * Mathf.Lerp(0.85f, 1.1f, 1f - wear);
            return new Color(l, l * 1.0f, l * 1.04f, s);
        }

        static Color ConcreteColour(float[] h, int x, int y)
        {
            float u = x / (float)TexSize, v = y / (float)TexSize;
            float k = h[y * TexSize + x];
            float stain = ValueNoise(u, v, 5, 211);
            float l = Mathf.Lerp(0.42f, 0.58f, k) * Mathf.Lerp(0.88f, 1.05f, stain);
            bool joint = (x % (TexSize / 2)) < 3 || (y % (TexSize / 2)) < 3;
            if (joint) l *= 0.45f;
            return new Color(l * 1.01f, l, l * 0.97f, 0.28f);
        }

        static Color Tint(float[] h, int x, int y, Color dark, Color light, float smooth)
        {
            float k = h[y * TexSize + x];
            var c = Color.Lerp(dark, light, k);
            c.a = smooth;
            return c;
        }

        static Texture2D Tex(string name, int size, Func<int, int, Color> pixel)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++) px[y * size + x] = pixel(x, y);
            tex.SetPixels(px);
            tex.Apply();
            return SaveTexture(name, tex, normalMap: false);
        }

        static Texture2D Normal(string name, float[] h, float strength)
        {
            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false, true);
            var px = new Color[TexSize * TexSize];
            for (int y = 0; y < TexSize; y++)
            for (int x = 0; x < TexSize; x++)
            {
                float hl = h[y * TexSize + (x + TexSize - 1) % TexSize], hr = h[y * TexSize + (x + 1) % TexSize];
                float hd = h[((y + TexSize - 1) % TexSize) * TexSize + x], hu = h[((y + 1) % TexSize) * TexSize + x];
                var n = new Vector3((hl - hr) * strength, (hd - hu) * strength, 1f).normalized;
                px[y * TexSize + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
            tex.SetPixels(px);
            tex.Apply();
            return SaveTexture(name, tex, normalMap: true);
        }

        static Texture2D SaveTexture(string name, Texture2D tex, bool normalMap)
        {
            string path = $"{Art}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = !normalMap;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.anisoLevel = 8;
            imp.mipmapEnabled = true;
            imp.alphaSource = normalMap ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static VolumeProfile CreateVolumeProfile()
        {
            string path = $"{Art}/TestTrack_Volume.asset";
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var tm = profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.25f);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.25f);
            ca.contrast.Override(10f);
            ca.saturation.Override(6f);
            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.18f);
            foreach (var c in profile.components) { c.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector; AssetDatabase.AddObjectToAsset(c, profile); }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }
    }
}
