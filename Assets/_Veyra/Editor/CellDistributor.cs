using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VeyraCity.World.Streaming;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card W7 / Roadmap A2: "Veyra/World/Distribute To Cells". The authoring scene VeyraCity.unity is never modified. From it
    /// the tool generates the streaming scenes:
    /// <list type="bullet">
    /// <item><c>Scenes/Cells/Cell_A1..H6.unity</c>: each cell's terrain tile, 250 m road/kerb chunks, building chunks, drive-surface
    /// ribbons, prop colliders and any other World content whose bounds centre lies in the cell. Groups keep their names
    /// (Cell_X/Terrain, Cell_X/Buildings/Chunk_X_ij, …). Objects that span several cells are split into their children.</item>
    /// <item><c>Scenes/VeyraCity_Main.unity</c>: the bootstrap — Game, cameras, WorldEnvironment, post, sun, global water, the
    /// instanced props, the HLOD proxies (World/HLOD, <see cref="HlodBuilder"/>) and the <see cref="CellStreamer"/>.</item>
    /// <item><c>Scenes/VeyraCity_Boot.unity</c>: build index 0, <see cref="CellBootLoader"/>.</item>
    /// </list>
    /// and sets the build scene list to Boot, Main, the 48 cells, then the other enabled scenes (VeyraCity.unity itself stays
    /// listed but disabled). Re-runnable: every output is overwritten in place (same paths, same GUIDs); nothing accumulates.
    /// The player build runs it first (<see cref="BuildScript"/>), so the streamed scenes always match the authoring scene.
    /// Report: Logs/A2/distribute.txt.
    /// </summary>
    public static class CellDistributor
    {
        public const string AuthoringScene = "Assets/_Veyra/Scenes/VeyraCity.unity";
        public const string MainScene = "Assets/_Veyra/Scenes/VeyraCity_Main.unity";
        public const string BootScene = "Assets/_Veyra/Scenes/VeyraCity_Boot.unity";
        public const string CellDir = "Assets/_Veyra/Scenes/Cells";
        /// <summary>World children that stay in the main scene (global or already streamed by distance).</summary>
        /// <summary>
        /// World children that stay in the main scene: global water, instanced props, and the terrain — A2 run 2 measured 45–68 ms
        /// frames when a 1 km terrain tile (heightfield collider) woke with its cell, so the 48 tiles (62 MB, all resident before
        /// streaming too) stay loaded and only the chunked content streams.
        /// </summary>
        public static readonly HashSet<string> StayInMain = new HashSet<string> { "Sea", "InlandWater", "Props", "Terrain", HlodBuilder.RootName };
        const float SplitExtent = 1100f;   // an object wider than this (x or z) is split into its children

        public static string CellScenePath(int i) => $"{CellDir}/Cell_{CellStreamer.CellName(i)}.unity";
        public static string CellSceneName(int i) => $"Cell_{CellStreamer.CellName(i)}";

        [MenuItem("Veyra/World/Distribute To Cells")]
        static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var r = Run();
            Debug.Log(r);
            EditorUtility.DisplayDialog("Distribute To Cells", r.Length > 1500 ? r.Substring(0, 1500) + " …" : r, "OK");
        }

        /// <summary>Batch entry: -executeMethod VeyraCity.EditorTools.CellDistributor.RunBatch</summary>
        public static void RunBatch()
        {
            string r = Run();
            Debug.Log(r);
            if (Application.isBatchMode) EditorApplication.Exit(r.StartsWith("FAIL") ? 1 : 0);
        }

        public static string Run()
        {
            var log = new StringBuilder();
            var t0 = System.DateTime.Now;
            var scene = EditorSceneManager.OpenScene(AuthoringScene, OpenSceneMode.Single);
            GameObject world = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "World");
            if (world == null) return "FAIL: no World root in " + AuthoringScene;
            Directory.CreateDirectory(CellDir);

            // 1. which object goes to which cell
            var assign = new Dictionary<Transform, int>();
            var stay = new List<string>();
            foreach (Transform group in world.transform)
            {
                if (StayInMain.Contains(group.name)) { stay.Add(group.name); continue; }
                foreach (Transform child in group) Assign(child, assign);
            }
            var perCell = new List<Transform>[48];
            for (int i = 0; i < 48; i++) perCell[i] = new List<Transform>();
            foreach (var kv in assign) perCell[kv.Value].Add(kv.Key);
            log.AppendLine($"Distribute To Cells — {t0:yyyy-MM-dd HH:mm:ss} — {AuthoringScene} (not modified)");
            log.AppendLine($"World groups kept in the main scene: {string.Join(", ", stay)}; streamed objects: {assign.Count}");

            log.Append(CheckCrossSceneReferences(scene, assign));

            // 2. HLOD proxies from the content as authored (before it moves)
            var old = world.transform.Find(HlodBuilder.RootName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var hlodRoot = new GameObject(HlodBuilder.RootName).transform;
            hlodRoot.SetParent(world.transform, false);
            log.Append(HlodBuilder.Build(perCell, hlodRoot));

            // 3. one scene per cell
            int movedTotal = 0;
            var cellNames = new string[48];
            for (int i = 0; i < 48; i++)
            {
                cellNames[i] = CellSceneName(i);
                var cs = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                var root = new GameObject(cellNames[i]) { isStatic = true };
                SceneManager.MoveGameObjectToScene(root, cs);
                var groups = new Dictionary<string, Transform>();
                int objects = 0;
                var activate = new List<GameObject>();
                // ground first (terrain, drive surface, road colliders), then the rest: the streamer wakes them in this order
                int Order(Transform t) { string p = PathOf(t); return p.StartsWith("Terrain") ? 0 : p.StartsWith("DriveSurface") ? 1 : p.StartsWith("CityMeshes") ? 2 : p.StartsWith("PropColliders") ? 3 : 4; }
                foreach (var t in perCell[i].OrderBy(Order).ThenBy(PathOf))
                {
                    string parentPath = PathOf(t.parent);
                    var parent = GroupFor(root.transform, parentPath, groups);
                    t.SetParent(null, true);
                    SceneManager.MoveGameObjectToScene(t.gameObject, cs);
                    t.SetParent(parent, true);
                    // woken time-sliced: a pure group (chunk) stays active and its children are listed one by one, so no single
                    // activation wakes a whole 250 m chunk
                    if (t.gameObject.activeSelf)
                    {
                        bool group = t.childCount > 0 && t.GetComponents<Component>().Length == 1;
                        if (group)
                            foreach (Transform c in t) { if (c.gameObject.activeSelf) { activate.Add(c.gameObject); c.gameObject.SetActive(false); } }
                        else { activate.Add(t.gameObject); t.gameObject.SetActive(false); }
                    }
                    objects++;
                }
                // no build-time static batching in streamed cells: its combined meshes load and free in one frame
                // (A2 run 3: 80+ ms load frames); the SRP Batcher draws the parts
                int unbatched = 0;
                foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                {
                    var flags = GameObjectUtility.GetStaticEditorFlags(tr.gameObject);
                    if ((flags & StaticEditorFlags.BatchingStatic) == 0) continue;
                    GameObjectUtility.SetStaticEditorFlags(tr.gameObject, flags & ~StaticEditorFlags.BatchingStatic);
                    unbatched++;
                }
                var content = root.AddComponent<CellContent>();
                content.Cell = CellStreamer.CellName(i);
                content.Objects = activate.ToArray();
                movedTotal += objects;
                EditorSceneManager.SaveScene(cs, CellScenePath(i));
                log.AppendLine($"  {cellNames[i]}: {objects} objects, {activate.Count} woken time-sliced, {unbatched} unbatched ({string.Join(", ", groups.Keys.Where(k => k.IndexOf('/') < 0))})");
                EditorSceneManager.CloseScene(cs, true);
            }

            // 4. the main (bootstrap) scene
            var streamGo = world.transform.Find("CellStreamer")?.gameObject ?? new GameObject("CellStreamer");
            streamGo.transform.SetParent(world.transform, false);
            hlodRoot.SetParent(streamGo.transform, true);   // the streamer finds its proxies among its children
            var streamer = streamGo.GetComponent<CellStreamer>() ?? streamGo.AddComponent<CellStreamer>();
            streamer.Streamed = true;
            streamer.CellScenes = cellNames;
            EditorSceneManager.SaveScene(scene, MainScene, true);

            // 5. the boot scene
            var boot = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("BootCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;
            var loader = new GameObject("CellBootLoader").AddComponent<CellBootLoader>();
            loader.MainScene = Path.GetFileNameWithoutExtension(MainScene);
            loader.CellScenes = cellNames;
            // every build scene references the material library (MaterialArchitectureTests.EveryBuildScene_ReferencesTheLibrary;
            // the regenerated boot scene lost it)
            var lib = AssetDatabase.LoadAssetAtPath<VeyraCity.World.VeyraMaterialLibrary>(VeyraMaterialFactory.LibraryPath);
            if (lib != null)
            {
                var holder = new GameObject("[VeyraMaterials]").AddComponent<VeyraCity.World.VeyraMaterialsHolder>();
                var so = new SerializedObject(holder);
                var prop = so.FindProperty("materials");
                if (prop != null) { prop.objectReferenceValue = lib; so.ApplyModifiedPropertiesWithoutUndo(); }
            }
            EditorSceneManager.SaveScene(boot, BootScene);

            // 6. build scene list
            var list = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(BootScene, true),
                new EditorBuildSettingsScene(MainScene, true),
            };
            for (int i = 0; i < 48; i++) list.Add(new EditorBuildSettingsScene(CellScenePath(i), true));
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (list.Any(x => x.path == s.path) || s.path.StartsWith(CellDir)) continue;
                list.Add(new EditorBuildSettingsScene(s.path, s.path != AuthoringScene && s.enabled));
            }
            EditorBuildSettings.scenes = list.ToArray();
            AssetDatabase.SaveAssets();
            log.AppendLine($"moved {movedTotal} objects into 48 cell scenes; main {MainScene}; boot {BootScene}; build list {list.Count(s => s.enabled)} enabled scenes; {(System.DateTime.Now - t0).TotalSeconds:0} s");

            // leave the editor on the authoring scene, unmodified
            EditorSceneManager.OpenScene(AuthoringScene, OpenSceneMode.Single);
            string report = log.ToString();
            Directory.CreateDirectory("Logs/A2");
            File.WriteAllText("Logs/A2/distribute.txt", report, new UTF8Encoding(false));
            return report;
        }

        static void Assign(Transform t, Dictionary<Transform, int> assign)
        {
            var b = BoundsOf(t, out bool any);
            bool isPrefabPart = PrefabUtility.IsPartOfPrefabInstance(t) && PrefabUtility.GetOutermostPrefabInstanceRoot(t) != t.gameObject;
            bool tooWide = any && (b.size.x > SplitExtent || b.size.z > SplitExtent);
            if (tooWide && t.childCount > 0 && !PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) && !isPrefabPart && t.GetComponents<Component>().Length == 1)
            {
                foreach (Transform c in t) Assign(c, assign);
                return;
            }
            assign[t] = CellStreamer.IndexOf(any ? b.center : t.position);
        }

        /// <summary>Bounds of every renderer, collider and terrain below t.</summary>
        public static Bounds BoundsOf(Transform t, out bool any)
        {
            bool found = false;
            var b = new Bounds(t.position, Vector3.zero);
            void Add(Bounds x) { if (!found) { b = x; found = true; } else b.Encapsulate(x); }
            foreach (var r in t.GetComponentsInChildren<Renderer>(true)) Add(r.bounds);
            foreach (var c in t.GetComponentsInChildren<Collider>(true))
            {
                if (c is TerrainCollider) continue;
                if (c is MeshCollider mc && mc.sharedMesh != null) Add(TransformBounds(mc.transform, mc.sharedMesh.bounds));
                else if (c.enabled && c.gameObject.activeInHierarchy) Add(c.bounds);
                else Add(new Bounds(c.transform.position, Vector3.zero));
            }
            foreach (var ter in t.GetComponentsInChildren<Terrain>(true))
                if (ter.terrainData != null) Add(new Bounds(ter.GetPosition() + ter.terrainData.size * 0.5f, ter.terrainData.size));
            any = found;
            return b;
        }

        static Bounds TransformBounds(Transform t, Bounds local)
        {
            var c = t.TransformPoint(local.center);
            var e = local.extents;
            var m = t.localToWorldMatrix;
            var ext = new Vector3(
                Mathf.Abs(m.m00) * e.x + Mathf.Abs(m.m01) * e.y + Mathf.Abs(m.m02) * e.z,
                Mathf.Abs(m.m10) * e.x + Mathf.Abs(m.m11) * e.y + Mathf.Abs(m.m12) * e.z,
                Mathf.Abs(m.m20) * e.x + Mathf.Abs(m.m21) * e.y + Mathf.Abs(m.m22) * e.z);
            return new Bounds(c, ext * 2f);
        }

        /// <summary>Path below World, e.g. "Buildings" or "PropColliders/streetlamp_urban_led_10m_0".</summary>
        static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (var x = t; x != null && x.parent != null; x = x.parent) parts.Insert(0, x.name);
            return string.Join("/", parts);
        }

        static Transform GroupFor(Transform root, string path, Dictionary<string, Transform> groups)
        {
            if (string.IsNullOrEmpty(path)) return root;
            if (groups.TryGetValue(path, out var g)) return g;
            int slash = path.LastIndexOf('/');
            var parent = slash < 0 ? root : GroupFor(root, path.Substring(0, slash), groups);
            g = new GameObject(slash < 0 ? path : path.Substring(slash + 1)) { isStatic = true }.transform;
            g.SetParent(parent, false);
            groups[path] = g;
            return g;
        }

        static int CellOfMoved(Transform t, Dictionary<Transform, int> assign)
        {
            for (var x = t; x != null; x = x.parent) if (assign.TryGetValue(x, out int c)) return c;
            return -1;
        }

        /// <summary>Serialized references from the main scene to objects that moved to a cell (they would be lost).</summary>
        static string CheckCrossSceneReferences(Scene main, Dictionary<Transform, int> assign)
        {
            bool Moves(Transform t) { for (var x = t; x != null; x = x.parent) if (assign.ContainsKey(x)) return true; return false; }
            var sb = new StringBuilder();
            int n = 0;
            foreach (var root in main.GetRootGameObjects())
                foreach (var comp in root.GetComponentsInChildren<Component>(true))
                {
                    if (comp == null || comp is Transform || Moves(comp.transform)) continue;
                    var so = new SerializedObject(comp);
                    var it = so.GetIterator();
                    while (it.NextVisible(true))
                    {
                        if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                        var o = it.objectReferenceValue;
                        GameObject go = o as GameObject ?? (o as Component)?.gameObject;
                        if (go == null || EditorUtility.IsPersistent(go) || !Moves(go.transform)) continue;
                        if (n++ < 30) sb.AppendLine($"  WARN cross-scene reference {comp.GetType().Name} on {comp.name}.{it.propertyPath} -> {go.name} (moves to cell {CellStreamer.CellName(CellOfMoved(go.transform, assign))})");
                    }
                }
            sb.Insert(0, $"cross-scene references from the main scene into cells: {n}\n");
            return sb.ToString();
        }
    }
}
