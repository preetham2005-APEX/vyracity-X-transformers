using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card C3 (BUG-043): instanced props have no GameObjects and so no colliders; cars drove through lamps and trees.
    /// "Veyra/Physics/Build Prop Colliders" adds static colliders under World/PropColliders/&lt;cell&gt;:
    /// lamps → capsule on the pole (r 0.15 m, full height); trees within 15 m of a road centre line → capsule on the trunk
    /// (r 0.25 m, 3 m); bushes, grass and small decoration get none (card: decorative props &lt; 0.5 m have no collider).
    /// Layer Default (the vehicle body collides with it; wheel rays ignore nothing, so a pole under a wheel is never sampled:
    /// the capsules stand beside the carriageway). Idempotent: the root is rebuilt on every run.
    /// </summary>
    public static class PropColliderBuilder
    {
        const float TreeReach = 15f;

        [MenuItem("Veyra/Physics/Build Prop Colliders")]
        static void Menu() => Debug.Log(Build(true));

        static bool IsTree(string n)
        {
            n = n.ToLowerInvariant();
            return n.Contains("tree") || n.Contains("pine") || n.Contains("palm") || n.Contains("deciduous") || n.Contains("oak") || n.Contains("birch");
        }

        public static string Build(bool save)
        {
            var ipr = Object.FindAnyObjectByType<InstancedPropRenderer>();
            if (ipr == null) return "no InstancedPropRenderer in the open scene";
            var scene = EditorSceneManager.GetActiveScene();
            GameObject world = null;
            foreach (var r in scene.GetRootGameObjects()) if (r.name == "World") world = r;
            if (world == null) return "no World root";
            var old = world.transform.Find("PropColliders");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("PropColliders") { isStatic = true }.transform;
            root.SetParent(world.transform, false);
            WorldData.EnsureLoaded();
            int lamps = 0, trees = 0, skipped = 0;
            for (int ci = 0; ci < ipr.Cells.Length; ci++)
            {
                var cell = ipr.Cells[ci];
                if (cell.Prototype < 0 || cell.Prototype >= ipr.Prototypes.Length || cell.Placements == null) continue;
                var proto = ipr.Prototypes[cell.Prototype];
                bool lamp = proto.IsLamp, tree = !lamp && IsTree(proto.Name);
                if (!lamp && !tree) { skipped += cell.Placements.Length; continue; }
                float height = proto.Mesh != null ? proto.Mesh.bounds.size.y : (lamp ? 10f : 6f);
                Transform cellRoot = null;
                for (int i = 0; i < cell.Placements.Length; i++)
                {
                    var p = cell.Placements[i];
                    float scale = cell.Scales != null && i < cell.Scales.Length ? cell.Scales[i] : 1f;
                    var pos = new Vector3(p.x, p.y, p.z);
                    if (tree && !(WorldData.NearestRoad(pos, TreeReach + 20f, out var hit) && hit.Distance < hit.RoadPath.HalfWidth + TreeReach)) { skipped++; continue; }
                    if (cellRoot == null) { cellRoot = new GameObject($"{proto.Name}_{ci}") { isStatic = true }.transform; cellRoot.SetParent(root, false); }
                    var go = new GameObject(lamp ? "Lamp" : "Trunk") { isStatic = true };
                    go.transform.SetParent(cellRoot, false);
                    go.transform.position = pos;
                    var cap = go.AddComponent<CapsuleCollider>();
                    cap.direction = 1;
                    cap.radius = (lamp ? 0.15f : 0.25f) * scale;
                    cap.height = lamp ? height * scale : 3f * scale;
                    cap.center = new Vector3(0f, cap.height / 2f, 0f);
                    if (lamp) lamps++; else trees++;
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (save) EditorSceneManager.SaveScene(scene);
            return $"C3 prop colliders: {lamps:N0} lamp capsules, {trees:N0} roadside tree-trunk capsules (within {TreeReach} m of a carriageway), {skipped:N0} props without a collider";
        }
    }
}
