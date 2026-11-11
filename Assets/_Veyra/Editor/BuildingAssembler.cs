using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.Gameplay;
using VeyraCity.World;
using VeyraCity.World.Data;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Phase C (roadmap Phase 2): "Veyra/Buildings/Assemble Buildings". Replaces procedural shells with kit models where a kit fits,
    /// for every record of world_building_placements.csv (2,708), in this order:
    /// <list type="number">
    /// <item>Landmark records (style_kit Landmark) keep their generated shell until the A4 hero asset exists.</item>
    /// <item>Kit fit: a <see cref="KitbashCatalog"/> entry that is not excluded, whose styles include the record's style_kit
    /// (GlassModern/ArtDeco records accept either), that lists the record's district, and whose footprint fits the data
    /// footprint within ±15 % after a uniform scale of 0.9–1.1 (either orientation), with the height within 0.75–1.33 of the
    /// data height. Seeded choice from the record seed; never the same model as the previous two records in the same chunk.</item>
    /// <item>Everything else (all D5 suburban houses, industrial, rural, alpine, aviation) keeps the BuildingGenerator shell —
    /// the A2 modular recipes are not built yet (recorded in BUG_TRACKER).</item>
    /// </list>
    /// Kit instances (prefab links, stable GUIDs) go to World/Buildings/&lt;chunk&gt;/Kit, at the data position and yaw, sunk onto
    /// the data ground with a concrete plinth reaching 2 m below it; their own colliders are disabled because every record
    /// keeps its data-footprint collider from BuildingGenerator (physics does not change with the art). Static flags on.
    /// Idempotent: the Buildings root is regenerated (shells for the records without a kit) and kits are re-placed.
    /// Report: Logs/C/assembler.txt.
    /// </summary>
    public static class BuildingAssembler
    {
        const string Gen = "Assets/_Veyra/World/Generated";
        const string PlinthMesh = Gen + "/Meshes/KB_Plinth.asset";

        [MenuItem("Veyra/Buildings/Assemble Buildings")]
        public static void Menu()
        {
            if (EditorSceneManager.GetActiveScene().path != CellDistributor.AuthoringScene)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(CellDistributor.AuthoringScene);
            }
            Debug.Log(Run());
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        static bool StyleMatch(KitbashCatalog.Entry e, string kit)
        {
            if (kit.StartsWith("GlassModern")) return e.Styles.Contains("GlassModern") || e.Styles.Contains("ArtDeco");
            return e.Styles.Contains(kit);
        }

        public static string Run()
        {
            var sb = new StringBuilder($"Building assembler {DateTime.Now:yyyy-MM-dd HH:mm}\n");
            var cat = AssetDatabase.LoadAssetAtPath<KitbashCatalog>(KitbashCatalogBuilder.CatalogPath);
            if (cat == null) return "FAIL: no KitbashCatalog (run Veyra/Buildings/Build Kitbash Catalog)";
            var world = EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == "World");
            if (world == null) return "FAIL: no World root";
            var oldRoot = world.transform.Find("Buildings");
            if (oldRoot == null) return "FAIL: no World/Buildings (CityBuilder has not run)";
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/_Veyra/Data/GameDatabase.asset");
            if (db == null) return "FAIL: Data/GameDatabase.asset missing";

            // the shell materials, by the names BuildingGenerator uses ("<mat>_LOD0" renderers)
            var mats = new Dictionary<string, Material>();
            foreach (var mr in oldRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!mr.name.EndsWith("_LOD0") || mr.sharedMaterial == null) continue;
                string key = mr.name.Substring(0, mr.name.Length - 5);
                if (!mats.ContainsKey(key)) mats[key] = mr.sharedMaterial;
            }
            if (!mats.ContainsKey("concrete")) mats["concrete"] = mats.Values.FirstOrDefault();
            var plinthMat = mats.TryGetValue("concrete", out var cm) ? cm : mats.Values.First();

            var records = PlacementReader.Buildings(VeyraDataValidator.LoadText("world_building_placements.csv"), "world_building_placements.csv");
            var usable = cat.Entries.Where(e => !e.Excluded && e.Prefab != null).ToList();
            var choice = new Dictionary<string, (KitbashCatalog.Entry e, float scale, bool turned)>();
            var recent = new Dictionary<string, Queue<string>>();
            var noFit = new Dictionary<string, int>();
            foreach (var b in records)
            {
                if (b.StyleKit == "Landmark") { Count(noFit, "landmark (A4 hero pending)"); continue; }
                var cands = new List<(KitbashCatalog.Entry e, float s, bool turned)>();
                foreach (var e in usable)
                {
                    if (!StyleMatch(e, b.StyleKit) || !e.Districts.Contains(b.DistrictId)) continue;
                    for (int t = 0; t < 2; t++)
                    {
                        float kw = t == 0 ? e.Footprint.x : e.Footprint.y, kl = t == 0 ? e.Footprint.y : e.Footprint.x;
                        // iteration 1: Â±22 % footprint, uniform 0.85-1.2, height 0.65-1.5 x (C1: Â±15 % / 0.9-1.1 fitted 70 of 2,708)
                        float s = Mathf.Clamp(Mathf.Sqrt(b.FootprintWM * b.FootprintLM / Mathf.Max(1f, kw * kl)), 0.85f, 1.2f);
                        float fw = kw * s / b.FootprintWM, fl = kl * s / b.FootprintLM, fh = e.Height * s / Mathf.Max(1f, b.HeightM);
                        if (fw < 0.78f || fw > 1.22f || fl < 0.78f || fl > 1.22f || fh < 0.65f || fh > 1.5f) continue;
                        cands.Add((e, s, t == 1));
                    }
                }
                if (!recent.TryGetValue(b.Chunk, out var q)) recent[b.Chunk] = q = new Queue<string>();
                var pool = cands.Where(c => !q.Contains(c.e.Id)).ToList();
                if (pool.Count == 0) pool = cands;
                if (pool.Count == 0) { Count(noFit, $"{b.DistrictId} {b.StyleKit}: no kit fits"); continue; }
                var pick = pool[Mathf.Abs((int)(b.Seed * 7919f + b.UnityX * 13f + b.UnityZ * 7f)) % pool.Count];
                choice[b.Id] = pick;
                q.Enqueue(pick.e.Id);
                while (q.Count > 2) q.Dequeue();
            }

            // regenerate the shells (render skipped where a kit stands; every record keeps its data collider)
            int siblingIndex = oldRoot.GetSiblingIndex();
            UnityEngine.Object.DestroyImmediate(oldRoot.gameObject);
            BuildingGenerator.SkipRender = id => choice.ContainsKey(id);
            string shells;
            try { shells = BuildingGenerator.Build(world.transform, mats, db, Gen); }
            finally { BuildingGenerator.SkipRender = null; }
            var root = world.transform.Find("Buildings");
            root.SetSiblingIndex(siblingIndex);

            // kit instances
            var plinth = PlinthAsset();
            int placed = 0, floating = 0, fitted = 0; float maxStretch = 0f; long tris = 0;
            var perDistrict = new Dictionary<string, int>();
            foreach (var b in records)
            {
                if (!choice.TryGetValue(b.Id, out var c)) continue;
                var chunk = root.Find(b.Chunk) ?? NewChild(root, b.Chunk);
                var kit = chunk.Find("Kit") ?? NewChild(chunk, "Kit");
                var go = (GameObject)PrefabUtility.InstantiatePrefab(c.e.Prefab, kit);
                go.name = $"KB_{b.Id}";
                float yaw = b.UnityYawDeg + (c.turned ? 90f : 0f);
                float ground = float.IsNaN(b.GroundMinM) ? b.UnityYBase : b.GroundMinM;
                go.transform.SetPositionAndRotation(new Vector3(b.UnityX, ground, b.UnityZ), Quaternion.Euler(0f, yaw, 0f) * go.transform.rotation);
                go.transform.localScale = go.transform.localScale * c.scale;
                foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.enabled = false;
                // iteration 2: collision/helper meshes never render (review CAM_05: frames and slabs floating over towers)
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    string rn = r.name.ToUpperInvariant();
                    if (rn.StartsWith("UCX_") || rn.StartsWith("UBX_") || rn.StartsWith("COL_") || rn.Contains("COLLISION") || rn.Contains("COLLIDER")) r.enabled = false;
                }
                floating += HideUnsupported(go, ground);
                // iteration 14: the data height is the authority (RooftopDetails puts roof props on the data collision box,
                // so a shorter kit left them hovering, CAM_05): stretch the kit along its up axis to base + HeightM
                float sy = FitHeight(go, ground, b.UnityYBase + b.HeightM);
                if (Mathf.Abs(sy - 1f) > 0.001f) { fitted++; maxStretch = Mathf.Max(maxStretch, Mathf.Abs(sy - 1f)); }
                KitbashMaterials.Apply(go, c.e);   // the packs' own textures (the export strips them from the FBX)
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = true;
                // plinth: footprint × (2 m below ground .. 0.3 m above the lowest corner, hidden where the ground is higher)
                var p = new GameObject("Plinth") { isStatic = true };
                p.transform.SetParent(go.transform, true);
                p.transform.SetPositionAndRotation(new Vector3(b.UnityX, ground - 0.85f, b.UnityZ), Quaternion.Euler(0f, b.UnityYawDeg, 0f));
                p.transform.localScale = Vector3.one;
                p.transform.localScale = new Vector3(b.FootprintWM, 2.3f, b.FootprintLM) / Mathf.Max(0.01f, c.scale);
                // undo the parent's height stretch on the plinth (its world size stays footprint x 2.3 m)
                var ls = p.transform.lossyScale; var want = new Vector3(b.FootprintWM, 2.3f, b.FootprintLM);
                p.transform.localScale = new Vector3(p.transform.localScale.x * want.x / Mathf.Max(0.001f, ls.x), p.transform.localScale.y * want.y / Mathf.Max(0.001f, ls.y), p.transform.localScale.z * want.z / Mathf.Max(0.001f, ls.z));
                p.AddComponent<MeshFilter>().sharedMesh = plinth;
                var pr = p.AddComponent<MeshRenderer>();
                pr.sharedMaterial = plinthMat;
                pr.shadowCastingMode = ShadowCastingMode.Off;
                placed++;
                if (c.e.Triangles.Length > 0) tris += c.e.Triangles[0];
                Count(perDistrict, b.DistrictId);
            }
            AssetDatabase.SaveAssets();
            sb.AppendLine($"records {records.Count}; kit models placed {placed} ({tris:N0} LOD0 tris); shells kept {records.Count - placed}; floating kit parts hidden {floating}; kits stretched to the data height {fitted} (max {maxStretch * 100f:0} %)");
            sb.AppendLine("kits per district: " + string.Join(", ", perDistrict.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}")));
            sb.AppendLine("shells kept because: " + string.Join("; ", noFit.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}")));
            sb.AppendLine("models used: " + choice.Values.Select(v => v.e.Id).Distinct().Count() + " distinct of " + usable.Count + " usable");
            sb.AppendLine("shells: " + shells);
            Directory.CreateDirectory("Logs/C");
            File.WriteAllText("Logs/C/assembler.txt", sb.ToString());
            return sb.ToString();
        }

        /// <summary>
        /// Iteration 14 (CAM_05: slabs and an empty frame hovering over downtown kit towers): a kit renderer is supported
        /// when it starts at the ground or rests on (or overlaps) a supported renderer below it (bounds overlap in x/z,
        /// within 0.75 m vertically). Unsupported renderers are hidden. Returns how many were hidden.
        /// </summary>
        static int HideUnsupported(GameObject go, float ground)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).Select(r => (r, b: r.bounds)).OrderBy(x => x.b.min.y).ToList();
            var ok = new List<Bounds>();
            int hidden = 0;
            foreach (var (r, b) in rs)
            {
                bool sup = b.min.y <= ground + 1.5f;
                if (!sup)
                    foreach (var s in ok)
                    {
                        bool xz = b.min.x < s.max.x - 0.2f && b.max.x > s.min.x + 0.2f && b.min.z < s.max.z - 0.2f && b.max.z > s.min.z + 0.2f;
                        if (xz && s.max.y >= b.min.y - 0.75f) { sup = true; break; }
                    }
                if (sup) ok.Add(b);
                else { r.enabled = false; hidden++; }
            }
            return hidden;
        }

        /// <summary>
        /// Scales the kit along the local axis that points up so its rendered top reaches <paramref name="top"/> (the pivot is
        /// at the base, so the base stays). Returns the factor applied (1 when the kit already fits or its root is tilted).
        /// </summary>
        static float FitHeight(GameObject go, float ground, float top)
        {
            float cur = float.NegativeInfinity;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) if (r.enabled) cur = Mathf.Max(cur, r.bounds.max.y);
            if (float.IsInfinity(cur) || cur - ground < 2f) return 1f;
            float sy = (top - ground) / (cur - ground);
            if (Mathf.Abs(sy - 1f) < 0.01f) return 1f;
            var up = Quaternion.Inverse(go.transform.rotation) * Vector3.up;
            int axis = Mathf.Abs(up.x) > Mathf.Abs(up.y) ? (Mathf.Abs(up.x) > Mathf.Abs(up.z) ? 0 : 2) : (Mathf.Abs(up.y) > Mathf.Abs(up.z) ? 1 : 2);
            if (Mathf.Abs(up[axis]) < 0.99f) return 1f;
            var s = go.transform.localScale; s[axis] *= sy; go.transform.localScale = s;
            return sy;
        }

        static void Count(Dictionary<string, int> d, string k) => d[k] = d.TryGetValue(k, out int n) ? n + 1 : 1;

        static Transform NewChild(Transform parent, string name)
        {
            var t = new GameObject(name) { isStatic = true }.transform;
            t.SetParent(parent, false);
            return t;
        }

        /// <summary>A unit cube (centre origin, no bottom face) used for kit plinths.</summary>
        static Mesh PlinthAsset()
        {
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(PlinthMesh);
            if (m != null) return m;
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m = UnityEngine.Object.Instantiate(tmp.GetComponent<MeshFilter>().sharedMesh);
            UnityEngine.Object.DestroyImmediate(tmp);
            m.name = "KB_Plinth";
            AssetDatabase.CreateAsset(m, PlinthMesh);
            return m;
        }
    }
}
