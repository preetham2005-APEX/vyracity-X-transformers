using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Tower crowns and corner fins from the facade kit (ArtSource/Blender/Facade), placed round the roofline of every
    /// building at least <see cref="MinHeight"/> tall.
    /// <para>
    /// Why: the night skyline reads as plain extruded boxes with lit rectangles on them. The shading side of that gap is
    /// done (view-angle window falloff, floor and core coherence, reveal and spill, commit 02224645) and it made the
    /// facades better but could not give a box a shape it does not have. Massing is geometry. Full-height window reveals
    /// are not affordable - a 240 m tower is ~259,200 triangles of reveal, ~190x the building itself - but a crown is a
    /// band round one edge: ~37 pieces at 72 triangles on a 55 m tower, about 2,700 triangles, and it is the part of a
    /// tower that actually makes a silhouette.
    /// </para>
    /// Buildings come from their UBX collision boxes (centre, yaw, size), the same source RooftopDetails uses, so this
    /// needs no building registry. Pieces are prefab instances rather than a merged mesh: each FBX carries its own
    /// LODGroup, instance counts are in the thousands rather than the hundreds of thousands, and Unity's static batching
    /// handles the draw calls. If that proves costly it can be merged later - with a measurement rather than a guess.
    /// Grouped per 250 m cell. No colliders (a roofline is out of reach). Idempotent. Report: Logs/C/tower_crowns.txt.
    /// </summary>
    public static class CrownBuilder
    {
        const string KitDir = "Assets/_Veyra/Art/Models/Kitbash/VC_Facade";
        const string RootName = "TowerCrowns";
        /// <summary>Only buildings this tall get a crown: roughly seven storeys, where a roofline starts to read.</summary>
        const float MinHeight = 25f;
        const float MinFootprint = 9f;
        /// <summary>VC_Fac_CrownSetback is 6.00 m wide; VC_Fac_CrownFin is 0.86 x 0.58 x 3.58.</summary>
        const float PieceWidth = 6f;
        const float Cell = 250f;

        [MenuItem("Veyra/World/Build Tower Crowns")]
        public static void Menu()
        {
            string report = Run();
            Debug.Log("[Veyra] " + report.Replace("\n", " "));
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        public static string Run()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var world = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "World");
            if (world == null) return "FAIL: no World root\n";

            var setback = AssetDatabase.LoadAssetAtPath<GameObject>($"{KitDir}/VC_Fac_CrownSetback.fbx");
            var fin = AssetDatabase.LoadAssetAtPath<GameObject>($"{KitDir}/VC_Fac_CrownFin.fbx");
            if (setback == null || fin == null) return $"FAIL: crown models missing under {KitDir}\n";

            var old = world.transform.Find(RootName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject(RootName) { isStatic = true }.transform;
            root.SetParent(world.transform, false);

            // Buildings, from their UBX boxes. Same filter as RooftopDetails so the two agree about what a building is.
            var boxes = Object.FindObjectsByType<BoxCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var cells = new Dictionary<long, Transform>();
            int towers = 0, pieces = 0, fins = 0;
            var seen = new HashSet<long>();

            foreach (var box in boxes)
            {
                string n = box.name;
                if (!n.StartsWith("UBX_") || n.EndsWith("_Leg")) continue;
                var t = box.transform;
                Vector3 size = Vector3.Scale(box.size, t.lossyScale);
                if (size.y < MinHeight || size.x < MinFootprint || size.z < MinFootprint) continue;
                Vector3 c = t.TransformPoint(box.center);
                // a cell streaming back in presents the same box again; dedupe by name and position like RooftopDetails
                long id = RoofId(n, c);
                if (!seen.Add(id)) continue;

                float yaw = t.eulerAngles.y;
                var rot = Quaternion.Euler(0f, yaw, 0f);
                float hw = size.x * 0.5f, hl = size.z * 0.5f;
                float topY = c.y + size.y * 0.5f;
                var parent = CellRoot(root, cells, c);
                towers++;

                // four edges; local outward is +Z after the VC_Unity_Prop export, so each piece looks outward
                pieces += Edge(setback, parent, c, rot, topY, new Vector3(0f, 0f, hl), Vector3.forward, hw * 2f);
                pieces += Edge(setback, parent, c, rot, topY, new Vector3(0f, 0f, -hl), Vector3.back, hw * 2f);
                pieces += Edge(setback, parent, c, rot, topY, new Vector3(hw, 0f, 0f), Vector3.right, hl * 2f);
                pieces += Edge(setback, parent, c, rot, topY, new Vector3(-hw, 0f, 0f), Vector3.left, hl * 2f);

                // a fin on each corner: the edge rows leave a gap there, and a vertical accent is what reads from far away
                foreach (var corner in new[] { new Vector3(hw, 0f, hl), new Vector3(-hw, 0f, hl), new Vector3(hw, 0f, -hl), new Vector3(-hw, 0f, -hl) })
                {
                    var p = c + rot * corner;
                    p.y = topY;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(fin, parent);
                    go.transform.SetPositionAndRotation(p, rot * Quaternion.LookRotation(new Vector3(corner.x, 0f, corner.z).normalized, Vector3.up));
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic);
                    fins++;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Tower crowns: {towers} buildings >= {MinHeight} m, {pieces} setback pieces + {fins} corner fins in {cells.Count} cells");
            sb.AppendLine($"triangles (LOD0): {pieces * 72 + fins * 48} ({pieces} x 72 + {fins} x 48)");
            Directory.CreateDirectory("Logs/C");
            File.WriteAllText("Logs/C/tower_crowns.txt", sb.ToString());
            return sb.ToString();
        }

        /// <summary>One row of pieces along an edge, centred on it; a part-piece at the end is dropped rather than scaled.</summary>
        static int Edge(GameObject model, Transform parent, Vector3 centre, Quaternion rot, float topY, Vector3 localOffset, Vector3 outward, float length)
        {
            int n = Mathf.FloorToInt(length / PieceWidth);
            if (n < 1) return 0;
            float span = n * PieceWidth;
            // along the edge, perpendicular to the outward direction
            Vector3 along = new Vector3(outward.z, 0f, -outward.x);
            for (int i = 0; i < n; i++)
            {
                float s = -span * 0.5f + PieceWidth * (i + 0.5f);
                Vector3 local = localOffset + along * s;
                Vector3 p = centre + rot * local;
                p.y = topY;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
                go.transform.SetPositionAndRotation(p, rot * Quaternion.LookRotation(outward, Vector3.up));
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic);
            }
            return n;
        }

        static Transform CellRoot(Transform root, Dictionary<long, Transform> cells, Vector3 p)
        {
            int cx = Mathf.FloorToInt(p.x / Cell), cz = Mathf.FloorToInt(p.z / Cell);
            long key = ((long)cx << 32) ^ (uint)cz;
            if (cells.TryGetValue(key, out var t)) return t;
            var go = new GameObject($"Crowns_{cx}_{cz}") { isStatic = true };
            go.transform.SetParent(root, false);
            cells[key] = go.transform;
            return go.transform;
        }

        static long RoofId(string name, Vector3 c)
        {
            unchecked
            {
                long h = name.GetHashCode();
                h = h * 486187739 + Mathf.RoundToInt(c.x * 2f);
                h = h * 486187739 + Mathf.RoundToInt(c.y * 2f);
                h = h * 486187739 + Mathf.RoundToInt(c.z * 2f);
                return h;
            }
        }
    }
}
