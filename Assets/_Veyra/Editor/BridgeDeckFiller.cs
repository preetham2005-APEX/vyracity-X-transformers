using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.VehiclePhysics;
using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card C1 (BUG-050): "Veyra/Physics/Fill Missing Decks". The blockout has no deck on parts of some structures (the
    /// Lantern Strait Bridge main span on C1 is only towers and cables, so cars fall into the sea). Every road is sampled on its
    /// centre line every 2 m; runs of 6 m or more with no drivable collider within 6 m below the (eased) profile become a deck:
    /// 0.8 m slab (asphalt top, concrete sides and soffit) following the profile at the road's width, a non-convex MeshCollider
    /// with the dry-asphalt surface, and 1.1 m concrete parapets with their own colliders. Objects go under World/RoadFixes,
    /// meshes to Assets/_Veyra/World/Generated/Fixes. Idempotent: existing fix objects and meshes are rebuilt in place.
    /// </summary>
    public static class BridgeDeckFiller
    {
        const string Dir = "Assets/_Veyra/World/Generated/Fixes";
        const float Step = 2f, MinRun = 6f, Slab = 0.8f, Parapet = 1.1f, ParapetWidth = 0.4f;

        [MenuItem("Veyra/Physics/Fill Missing Decks")]
        static void Menu() => Debug.Log(Fill());

        static bool HasDrivable(Vector3 p)
        {
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 2.5f, Vector3.down, 8.5f, ~0, QueryTriggerInteraction.Ignore))
                if (h.collider.GetComponentInParent<VehicleSurface>() != null) return true;
            return false;
        }

        public static string Fill()
        {
            var log = new StringBuilder("C1 fill missing decks\n");
            WorldData.EnsureLoaded();
            RoadProfileEaser.Reset();
            Physics.SyncTransforms();
            var lib = AssetDatabase.LoadAssetAtPath<VeyraMaterialLibrary>(VeyraMaterialFactory.LibraryPath);
            var asphalt = lib != null ? lib.Get(VeyraMat.RoadAsphaltNew) : null;
            var concrete = lib != null ? lib.Get(VeyraMat.RoadConcreteDeck) : null;
            var surface = AssetDatabase.LoadAssetAtPath<SurfaceDefinition>("Assets/_Veyra/Data/Surfaces/dry_asphalt.asset");
            var concreteSurface = AssetDatabase.LoadAssetAtPath<SurfaceDefinition>("Assets/_Veyra/Data/Surfaces/concrete.asset");
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/_Veyra/World/Generated", "Fixes");

            var scene = EditorSceneManager.GetActiveScene();
            GameObject world = null;
            foreach (var r in scene.GetRootGameObjects()) if (r.name == "World") world = r;
            if (world == null) return "no World root in the open scene";
            var fixes = world.transform.Find("RoadFixes");
            if (fixes == null) { fixes = new GameObject("RoadFixes").transform; fixes.SetParent(world.transform, false); fixes.gameObject.isStatic = true; }
            // Rebuild from scratch: drop the old decks first so they do not hide the gaps from the scan (eased ribbons stay).
            for (int i = fixes.childCount - 1; i >= 0; i--) if (fixes.GetChild(i).name.StartsWith("Deck_")) Object.DestroyImmediate(fixes.GetChild(i).gameObject);
            Physics.SyncTransforms();

            int decks = 0;
            float total = 0f;
            var keep = new HashSet<string>();
            for (int r = 0; r < WorldData.Roads.Count; r++)
            {
                var road = WorldData.Roads[r];
                if (road.Data.cls == "RW") continue;
                float len = road.Path.Length;
                float runStart = -1f;
                for (float s = 0f; s <= len + Step; s += Step)
                {
                    bool gap = false;
                    if (s <= len)
                    {
                        var p = road.Path.PositionAt(s);
                        p.y += RoadProfileEaser.Delta(r, s);
                        gap = !HasDrivable(p);
                    }
                    if (gap && runStart < 0f) runStart = s;
                    if (!gap && runStart >= 0f)
                    {
                        float a = Mathf.Max(0f, runStart - Step), b = Mathf.Min(len, s);
                        if (b - a >= MinRun)
                        {
                            string name = $"Deck_{road.Data.id}_{Mathf.RoundToInt(a)}";
                            Build(fixes, name, r, a, b, asphalt, concrete, surface, concreteSurface, true, Slab);
                            keep.Add(name);
                            decks++;
                            total += b - a;
                            log.AppendLine($"{name}: s {a:F0}..{b:F0} ({b - a:F0} m), width {road.HalfWidth * 2f:F1} m");
                        }
                        runStart = -1f;
                    }
                }
            }
            // Remove mesh assets of decks that are no longer needed.
            foreach (var f in Directory.GetFiles(Dir, "Deck_*.asset"))
            {
                string n = Path.GetFileNameWithoutExtension(f);
                string baseName = n.EndsWith("_col") ? n.Substring(0, n.Length - 4) : n.EndsWith("_par") ? n.Substring(0, n.Length - 4) : n;
                if (!keep.Contains(baseName)) AssetDatabase.DeleteAsset(f.Replace('\\', '/'));
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine($"decks: {decks}, total {total:F0} m");
            return log.ToString();
        }

        public static Mesh MeshAsset(string name)
        {
            string path = $"{Dir}/{name}.asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m == null) { m = new Mesh { name = name }; AssetDatabase.CreateAsset(m, path); }
            m.Clear();
            m.indexFormat = IndexFormat.UInt32;
            return m;
        }

        /// <summary>A road ribbon on the eased profile from S <paramref name="a"/> to <paramref name="b"/>: top (render + collider),
        /// sides down by <paramref name="slab"/>, optional parapets. Used for missing decks and for C2's eased approaches.</summary>
        public static void Build(Transform parent, string name, int r, float a, float b, Material asphalt, Material concrete, SurfaceDefinition surface, SurfaceDefinition concreteSurface, bool parapets, float slab, float raise = 0.012f, System.Func<float, float> extra = null)
        {
            var road = WorldData.Roads[r];
            float half = road.HalfWidth;
            var centre = new List<Vector3>();
            var right = new List<Vector3>();
            for (float s = a; ; s = Mathf.Min(b, s + Step))
            {
                var p = road.Path.PositionAt(s);
                p.y += RoadProfileEaser.Delta(r, s) + raise + (extra != null ? extra(s) : 0f);
                var d = road.Path.PositionAt(Mathf.Min(road.Path.Length, s + 1f)) - road.Path.PositionAt(Mathf.Max(0f, s - 1f));
                d.y = 0f;
                centre.Add(p);
                right.Add(new Vector3(d.z, 0f, -d.x).normalized);
                if (s >= b) break;
            }
            int n = centre.Count;
            // Top (asphalt, also the collider), sides + soffit (concrete).
            var top = new List<Vector3>(); var topUv = new List<Vector2>(); var topT = new List<int>();
            var side = new List<Vector3>(); var sideT = new List<int>();
            for (int i = 0; i < n; i++)
            {
                var L = centre[i] - right[i] * half; var R = centre[i] + right[i] * half;
                top.Add(L); top.Add(R);
                float v = i * Step / 4f;
                topUv.Add(new Vector2(0f, v)); topUv.Add(new Vector2(half / 2f, v));
                side.Add(L); side.Add(R); side.Add(L + Vector3.down * slab); side.Add(R + Vector3.down * slab);
            }
            for (int i = 0; i + 1 < n; i++)
            {
                int k = i * 2, j = (i + 1) * 2;
                topT.AddRange(new[] { k, j, k + 1, k + 1, j, j + 1 });       // facing up
                int s0 = i * 4, s1 = (i + 1) * 4;
                sideT.AddRange(new[] { s0, s0 + 2, s1, s1, s0 + 2, s1 + 2 }); // left side, facing out
                sideT.AddRange(new[] { s0 + 1, s1 + 1, s0 + 3, s0 + 3, s1 + 1, s1 + 3 }); // right side
                sideT.AddRange(new[] { s0 + 2, s0 + 3, s1 + 2, s1 + 2, s0 + 3, s1 + 3 }); // soffit, facing down
            }
            var deck = MeshAsset(name);
            int topCount = top.Count;
            var verts = new List<Vector3>(top); verts.AddRange(side);
            var uv = new List<Vector2>(topUv); for (int i = 0; i < side.Count; i++) uv.Add(new Vector2(side[i].x * 0.25f, side[i].y * 0.25f));
            deck.SetVertices(verts);
            deck.SetUVs(0, uv);
            deck.subMeshCount = 2;
            deck.SetTriangles(topT, 0);
            var shifted = new List<int>(sideT.Count); foreach (var t in sideT) shifted.Add(t + topCount);
            deck.SetTriangles(shifted, 1);
            deck.RecalculateNormals(); deck.RecalculateTangents(); deck.RecalculateBounds();
            var col = MeshAsset(name + "_col");
            col.SetVertices(top); col.SetTriangles(topT, 0); col.RecalculateBounds();

            var go = new GameObject(name) { isStatic = true };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = deck;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { asphalt, concrete };
            var c = new GameObject("Collider_asphalt") { isStatic = true };
            c.transform.SetParent(go.transform, false);
            c.AddComponent<MeshCollider>().sharedMesh = col;
            c.AddComponent<VehicleSurface>().Surface = surface;
            EditorUtility.SetDirty(deck); EditorUtility.SetDirty(col);
            if (!parapets) return;

            // Parapets: boxes along both edges.
            var par = MeshAsset(name + "_par");
            var pv = new List<Vector3>(); var pt = new List<int>();
            foreach (float sideSign in new[] { -1f, 1f })
                for (int i = 0; i + 1 < n; i++)
                {
                    Vector3 o0 = centre[i] + right[i] * sideSign * half, o1 = centre[i + 1] + right[i + 1] * sideSign * half;
                    Vector3 i0 = o0 - right[i] * sideSign * ParapetWidth, i1 = o1 - right[i + 1] * sideSign * ParapetWidth;
                    Vector3 up = Vector3.up * Parapet;
                    int bse = pv.Count;
                    pv.AddRange(new[] { i0, i1, i0 + up, i1 + up, o0, o1, o0 + up, o1 + up });
                    // inner face, top, outer face (winding chosen per side so faces point away from the box)
                    int[] q = sideSign < 0 ? new[] { 0, 2, 1, 1, 2, 3, 2, 6, 3, 3, 6, 7, 4, 5, 6, 6, 5, 7 } : new[] { 0, 1, 2, 2, 1, 3, 2, 3, 6, 6, 3, 7, 4, 6, 5, 5, 6, 7 };
                    foreach (var x in q) pt.Add(bse + x);
                }
            par.SetVertices(pv); par.SetTriangles(pt, 0); par.RecalculateNormals(); par.RecalculateBounds();
            var pg = new GameObject("Parapets") { isStatic = true };
            pg.transform.SetParent(go.transform, false);
            pg.AddComponent<MeshFilter>().sharedMesh = par;
            pg.AddComponent<MeshRenderer>().sharedMaterial = concrete;
            pg.AddComponent<MeshCollider>().sharedMesh = par;
            pg.AddComponent<VehicleSurface>().Surface = concreteSurface;
            EditorUtility.SetDirty(deck); EditorUtility.SetDirty(col); EditorUtility.SetDirty(par);
        }
    }
}
