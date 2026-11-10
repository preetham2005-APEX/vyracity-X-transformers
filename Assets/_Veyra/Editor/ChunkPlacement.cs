using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VeyraCity.World.Data;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card B3: "Veyra/World/Place Chunks" reads a B2 chunks_manifest.json and instantiates each chunk prefab (the imported
    /// FBX) under World/Chunks/&lt;Cell&gt;/ at its south-west corner (manifest corner_unity_xz, height 0: the chunk meshes carry
    /// their heights). Idempotent: an existing instance of the same chunk is moved, never duplicated; one whose prefab
    /// changed is replaced. Then every COL_Road collider is sampled on a 2 m grid with downward raycasts and each hit is
    /// compared with the Veyra road network (road_network_geometry.json, data → Unity (x − 4000, z, y − 3000)): a chunk is
    /// FLAGGED when more than 5 % of its road samples lie farther than half the road width + 1.5 m (+ 4 m sidewalk allowance,
    /// because COL_Road includes the sidewalks) from every centreline, or when none touches the network (not connected).
    /// </summary>
    public static class ChunkPlacement
    {
        public const string ManifestPath = "Assets/_Veyra/Art/World/Chunks/chunks_manifest.json";
        public const string TestManifestPath = "Assets/_Veyra/Art/World/Chunks/_Test/chunks_manifest.json";
        public const string CityScene = "Assets/_Veyra/Scenes/VeyraCity.unity";
        public const string TestScene = "Assets/_Veyra/Scenes/Test/T_ChunkImport.unity";
        const string RoadsJson = "Assets/_Veyra/Data/road_network_geometry.json";
        const float Grid = 2f, Tolerance = 1.5f, SidewalkAllowance = 4f, OffNetworkShare = 0.05f;

        [MenuItem("Veyra/World/Place Chunks")]
        static void MenuPlace() => Debug.Log(Place(ManifestPath, CityScene));

        [MenuItem("Veyra/World/Place Test Chunks (T_ChunkImport)")]
        static void MenuPlaceTest() => Debug.Log(Place(TestManifestPath, TestScene, createScene: true));

        /// <summary>Places the manifest's chunks into <paramref name="scenePath"/> and saves it. Returns the report text.</summary>
        public static string Place(string manifestPath, string scenePath, bool createScene = false)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Place Chunks: manifest {manifestPath} -> {scenePath}");
            if (!File.Exists(manifestPath))
            {
                sb.AppendLine("no manifest: nothing to place (no ALIGNED map has been processed into Art/World/Chunks yet)");
                return sb.ToString();
            }
            var manifest = JObject.Parse(File.ReadAllText(manifestPath));
            var scene = OpenOrCreate(scenePath, createScene);
            var root = FindOrCreate(null, "World", scene);
            var chunksRoot = FindOrCreate(root.transform, "Chunks", scene);
            int placed = 0, moved = 0, replaced = 0, missing = 0;
            var instances = new List<GameObject>();
            foreach (var c in manifest["chunks"] ?? new JArray())
            {
                string name = (string)c["chunk"], cell = (string)c["cell"], file = (string)c["file"];
                var corner = c["corner_unity_xz"];
                var pos = new Vector3((float)corner[0], 0f, (float)corner[1]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(file);
                if (prefab == null) { sb.AppendLine($"MISSING {name}: {file} is not an imported model"); missing++; continue; }
                var cellRoot = FindOrCreate(chunksRoot.transform, cell, scene);
                var existing = cellRoot.transform.Find(name);
                if (existing != null && PrefabUtility.GetCorrespondingObjectFromSource(existing.gameObject) == prefab)
                {
                    if (existing.position != pos || existing.rotation != Quaternion.identity) { existing.SetPositionAndRotation(pos, Quaternion.identity); moved++; }
                    instances.Add(existing.gameObject);
                    continue;
                }
                if (existing != null) { Object.DestroyImmediate(existing.gameObject); replaced++; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, cellRoot.transform);
                go.name = name;
                go.transform.SetPositionAndRotation(pos, Quaternion.identity);
                instances.Add(go);
                placed++;
            }
            sb.AppendLine($"chunks: {instances.Count} in scene ({placed} new, {moved} moved, {replaced} replaced, {missing} missing)");
            sb.Append(CheckRoads(instances));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, scenePath);
            return sb.ToString();
        }

        static Scene OpenOrCreate(string path, bool create)
        {
            if (File.Exists(path)) return EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            if (!create) throw new FileNotFoundException(path);
            var s = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            EditorSceneManager.SaveScene(s, path);
            return s;
        }

        static GameObject FindOrCreate(Transform parent, string name, Scene scene)
        {
            if (parent != null)
            {
                var t = parent.Find(name);
                if (t != null) return t.gameObject;
            }
            else
            {
                foreach (var r in scene.GetRootGameObjects()) if (r.name == name) return r;
            }
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            else SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        // ------------------------------------------------------------------------------------------------ evidence

        /// <summary>Renders the test scene twice from a street-corner camera: as rendered, and as collision only (render meshes
        /// hidden; COL_ meshes magenta, UBX_/UCX_ cyan, drawn from the colliders' own data). Editor-only evidence for B3.</summary>
        public static string CaptureEvidence(string outDir)
        {
            var scene = EditorSceneManager.OpenScene(TestScene, OpenSceneMode.Single);
            var cam = Object.FindAnyObjectByType<Camera>();
            if (cam == null) return "no camera in " + TestScene;
            cam.transform.position = new Vector3(-835f, 70f, -1615f);
            cam.transform.LookAt(new Vector3(-745f, 8f, -1495f));
            cam.fieldOfView = 55f;
            cam.farClipPlane = 3000f;
            Directory.CreateDirectory(outDir);
            var log = new StringBuilder();
            log.AppendLine(Shot(cam, Path.Combine(outDir, "T_ChunkImport_render.png")));
            var hidden = new List<Renderer>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
                if (r.enabled) { r.enabled = false; hidden.Add(r); }
            var temp = new List<Object>();
            Material Mat(Color c)
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.SetColor("_BaseColor", c);
                temp.Add(m);
                return m;
            }
            var magenta = Mat(new Color(1f, 0.15f, 0.8f));
            var cyan = Mat(new Color(0.1f, 0.85f, 1f));
            int meshCols = 0, boxCols = 0;
            foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude))
            {
                GameObject proxy;
                if (c is MeshCollider mc && mc.sharedMesh != null)
                {
                    proxy = new GameObject("proxy_" + c.name);
                    proxy.AddComponent<MeshFilter>().sharedMesh = mc.sharedMesh;
                    proxy.AddComponent<MeshRenderer>().sharedMaterial = mc.convex ? cyan : magenta;
                    proxy.transform.SetPositionAndRotation(c.transform.position, c.transform.rotation);
                    proxy.transform.localScale = c.transform.lossyScale;
                    meshCols++;
                }
                else if (c is BoxCollider bc)
                {
                    proxy = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Object.DestroyImmediate(proxy.GetComponent<Collider>());
                    proxy.GetComponent<MeshRenderer>().sharedMaterial = cyan;
                    proxy.transform.SetPositionAndRotation(bc.transform.TransformPoint(bc.center), bc.transform.rotation);
                    proxy.transform.localScale = Vector3.Scale(bc.size, bc.transform.lossyScale);
                    boxCols++;
                }
                else continue;
                temp.Add(proxy);
            }
            log.AppendLine($"collision view: {meshCols} mesh colliders (magenta non-convex / cyan convex), {boxCols} box colliders (cyan)");
            log.AppendLine(Shot(cam, Path.Combine(outDir, "T_ChunkImport_colliders.png")));
            foreach (var o in temp) Object.DestroyImmediate(o);
            foreach (var r in hidden) r.enabled = true;
            EditorSceneManager.OpenScene(TestScene, OpenSceneMode.Single); // drop the temporary objects without saving
            return log.ToString();
        }

        static string Shot(Camera cam, string path)
        {
            // Offscreen renders skip objects whose shader variant is still compiling asynchronously: compile synchronously.
            bool asyncCompile = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.Render();
            ShaderUtil.allowAsyncCompilation = asyncCompile;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            return "saved " + path;
        }

        // ------------------------------------------------------------------------------------------------ road check

        struct Seg { public Vector2 A, B; public float Half; public string Road; }

        /// <summary>Raycast-samples every COL_Road collider of the instances and compares the hits with the Veyra network.</summary>
        public static string CheckRoads(List<GameObject> instances)
        {
            var sb = new StringBuilder();
            List<Seg> segs;
            try { segs = LoadSegments(); }
            catch (System.Exception e) { return $"road check skipped: {e.Message}\n"; }
            Physics.SyncTransforms();
            int flagged = 0;
            foreach (var inst in instances)
            {
                foreach (var col in inst.GetComponentsInChildren<MeshCollider>(true))
                {
                    if (!col.name.StartsWith("COL_Road")) continue;
                    var b = col.bounds;
                    int samples = 0, off = 0, touching = 0;
                    float worst = 0f;
                    for (float x = b.min.x + Grid / 2; x < b.max.x; x += Grid)
                    for (float z = b.min.z + Grid / 2; z < b.max.z; z += Grid)
                    {
                        var ray = new Ray(new Vector3(x, b.max.y + 5f, z), Vector3.down);
                        if (!col.Raycast(ray, out _, b.size.y + 10f)) continue;
                        samples++;
                        float excess = Excess(segs, new Vector2(x, z), out bool inside);
                        if (inside) touching++;
                        if (excess > Tolerance + SidewalkAllowance) off++;
                        worst = Mathf.Max(worst, excess);
                    }
                    float share = samples > 0 ? off / (float)samples : 0f;
                    bool flag = samples > 0 && (share > OffNetworkShare || touching == 0);
                    if (flag) flagged++;
                    sb.AppendLine($"{(flag ? "FLAG" : "ok  ")} {inst.name}/{col.name}: {samples} samples, {share:P1} beyond half width + {Tolerance} m (+{SidewalkAllowance} m sidewalk), " +
                                  $"{touching} on the carriageway, worst {worst:F1} m{(touching == 0 && samples > 0 ? " — NOT CONNECTED to the Veyra network" : "")}");
                }
            }
            sb.AppendLine($"road check: {flagged} flagged COL_Road collider(s)");
            return sb.ToString();
        }

        static List<Seg> LoadSegments()
        {
            var data = RoadNetwork.Parse(File.ReadAllText(RoadsJson), RoadsJson);
            var segs = new List<Seg>();
            foreach (var r in data.Roads)
            {
                float half = RoadClasses.TryGet(r.Cls, out var info) ? info.WidthM / 2f : 5f;
                var pts = r.CentrelineXY;
                for (int i = 1; i < pts.Length; i++)
                    segs.Add(new Seg
                    {
                        A = new Vector2(pts[i - 1][0] - 4000f, pts[i - 1][1] - 3000f),
                        B = new Vector2(pts[i][0] - 4000f, pts[i][1] - 3000f),
                        Half = half, Road = r.Id,
                    });
            }
            return segs;
        }

        /// <summary>Distance beyond the nearest road's half width (0 when on a carriageway); <paramref name="inside"/> when on one.</summary>
        static float Excess(List<Seg> segs, Vector2 p, out bool inside)
        {
            float best = float.MaxValue;
            foreach (var s in segs)
            {
                var ab = s.B - s.A;
                float t = Mathf.Clamp01(Vector2.Dot(p - s.A, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                float d = Vector2.Distance(p, s.A + ab * t) - s.Half;
                if (d < best) best = d;
            }
            inside = best <= 0f;
            return Mathf.Max(0f, best);
        }
    }
}
