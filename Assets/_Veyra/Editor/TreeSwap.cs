using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Phase B (A7): "Veyra/World/Use Authored Trees". Replaces the procedural blob/cone tree meshes (CityBuilder.PropMeshes) of the
    /// InstancedPropRenderer prototypes — and so StreetTrees, which draws the broadleaf prototype — with the original trees
    /// authored in Blender by Tools/Blender/vc_trees.py (Art/Models/Vegetation/VC_Tree_*.fbx, LOD0 → near mesh, LOD1 → far mesh).
    /// Materials (Generated/Materials): M_Tree_Bark (URP Lit, T_Bark) and M_Tree_Foliage (URP Lit, alpha clip 0.45, two-sided,
    /// GPU instancing, T_Foliage_Atlas). Prototype names, placements and scales are unchanged (the trees keep the heights the
    /// placements were scaled for). Re-runnable.
    /// </summary>
    public static class TreeSwap
    {
        const string ModelDir = "Assets/_Veyra/Art/Models/Vegetation";
        const string TexDir = "Assets/_Veyra/Art/Textures/Vegetation";
        const string MatDir = "Assets/_Veyra/World/Generated/Materials";

        static readonly (string proto, string model, float height)[] Map =
        {
            ("pine_tree_14m", "VC_Tree_Pine", 14f),
            ("deciduous_tree_10m", "VC_Tree_Broadleaf", 10f),
            ("palm_tree_12m", "VC_Tree_Palm", 12f),
            ("bush_2m", "VC_Tree_Bush", 2f),
            ("deciduous_tree_10m_b", "VC_Tree_Broadleaf2", 10f),
        };

        [MenuItem("Veyra/World/Use Authored Trees")]
        public static void Menu() => Debug.Log(Run());

        public static string Run()
        {
            var sb = new StringBuilder("Authored trees\n");
            var ipr = Object.FindAnyObjectByType<InstancedPropRenderer>();
            if (ipr == null) return "no InstancedPropRenderer in the open scene";
            var bark = Mat("M_Tree_Bark", $"{TexDir}/T_Bark.png", false);
            var leaf = Mat("M_Tree_Foliage", $"{TexDir}/T_Foliage_Atlas.png", true);
            foreach (var (protoName, model, height) in Map)
            {
                var proto = ipr.Prototypes.FirstOrDefault(p => p != null && p.Name == protoName);
                if (proto == null) { sb.AppendLine($"  {protoName}: no such prototype"); continue; }
                var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{model}.fbx");
                if (go == null) { sb.AppendLine($"  {protoName}: {model}.fbx not imported"); continue; }
                Mesh Lod(int l, out Material[] mats)
                {
                    mats = null;
                    var t = go.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name.EndsWith($"_LOD{l}"));
                    if (t == null) return null;
                    var mr = t.GetComponent<MeshRenderer>();
                    mats = mr.sharedMaterials.Select(m => m != null && m.name.Contains("Bark") ? bark : leaf).ToArray();
                    return t.sharedMesh;
                }
                var m0 = Lod(0, out var mats0);
                // far mesh: LOD2 (≈ 15 % of LOD0) — the prototype has two levels and the far one covers 90 m to 1.4 km
                // (B1 run 1 with LOD1 there: 6.4 M triangles p95, GPU p95 9.7 ms)
                var m1 = Lod(2, out var mats1) ?? Lod(1, out mats1);
                if (m0 == null) { sb.AppendLine($"  {protoName}: {model} has no _LOD0"); continue; }
                proto.Mesh = m0; proto.Materials = mats0;
                proto.LowMesh = m1; proto.LowMaterials = mats1;
                sb.AppendLine($"  {protoName} <- {model}: LOD0 {m0.triangles.Length / 3} tris, far (LOD2) {(m1 != null ? m1.triangles.Length / 3 : 0)} tris, height {m0.bounds.max.y:0.0} m (expected {height:0} m), slots {string.Join("/", mats0.Select(x => x.name))}");
            }
            sb.Append(Variant(ipr, bark));
            EditorUtility.SetDirty(ipr);
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        /// <summary>
        /// World iteration 7: species variety. A second broadleaf prototype (VC_Tree_Broadleaf2: taller, narrower crown; warmer
        /// foliage tint M_Tree_Foliage_B) takes ~40 % of the deciduous placements (seeded by position, data positions
        /// unchanged). The split runs once: when the variant prototype already exists only its meshes and materials refresh.
        /// </summary>
        static string Variant(InstancedPropRenderer ipr, Material bark)
        {
            const string A = "deciduous_tree_10m", B = "deciduous_tree_10m_b";
            int ia = System.Array.FindIndex(ipr.Prototypes, p => p != null && p.Name == A);
            if (ia < 0) return "  variant: no deciduous prototype\n";
            var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/VC_Tree_Broadleaf2.fbx");
            if (go == null) return "  variant: VC_Tree_Broadleaf2.fbx not imported\n";
            var leafB = Mat("M_Tree_Foliage_B", $"{TexDir}/T_Foliage_Atlas.png", true);
            leafB.SetColor("_BaseColor", new Color(1.12f, 1.08f, 0.72f));
            EditorUtility.SetDirty(leafB);
            Mesh MeshOf(int l, out Material[] mats)
            {
                mats = null;
                var mf = go.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name.EndsWith($"_LOD{l}"));
                if (mf == null) return null;
                mats = mf.GetComponent<MeshRenderer>().sharedMaterials.Select(m => m != null && m.name.Contains("Bark") ? bark : leafB).ToArray();
                return mf.sharedMesh;
            }
            int ib = System.Array.FindIndex(ipr.Prototypes, p => p != null && p.Name == B);
            bool split = ib < 0;
            if (split)
            {
                var a = ipr.Prototypes[ia];
                var list = ipr.Prototypes.ToList();
                list.Add(new InstancedPropRenderer.Prototype { Name = B, LodDistance = a.LodDistance, DrawDistance = a.DrawDistance, ShadowDistance = a.ShadowDistance });
                ipr.Prototypes = list.ToArray();
                ib = ipr.Prototypes.Length - 1;
            }
            var pb = ipr.Prototypes[ib];
            pb.Mesh = MeshOf(0, out var m0); pb.Materials = m0;
            pb.LowMesh = MeshOf(2, out var m2); pb.LowMaterials = m2;
            int moved = 0;
            if (split)
            {
                var cells = ipr.Cells.ToList();
                int count = cells.Count;
                for (int c = 0; c < count; c++)
                {
                    var cell = cells[c];
                    if (cell.Prototype != ia || cell.Placements == null) continue;
                    var keepP = new List<Vector4>(); var keepS = new List<float>(); var moveP = new List<Vector4>(); var moveS = new List<float>();
                    for (int i = 0; i < cell.Placements.Length; i++)
                    {
                        var p = cell.Placements[i];
                        uint h = (uint)(Mathf.FloorToInt(p.x * 3.1f) * 73856093) ^ (uint)(Mathf.FloorToInt(p.z * 2.7f) * 19349663);
                        bool toB = (h % 100) < 40;
                        float s = cell.Scales != null && i < cell.Scales.Length ? cell.Scales[i] : 1f;
                        if (toB) { moveP.Add(p); moveS.Add(s); } else { keepP.Add(p); keepS.Add(s); }
                    }
                    if (moveP.Count == 0) continue;
                    cell.Placements = keepP.ToArray(); cell.Scales = keepS.ToArray();
                    cells.Add(new InstancedPropRenderer.Cell { Prototype = ib, Bounds = cell.Bounds, Placements = moveP.ToArray(), Scales = moveS.ToArray() });
                    moved += moveP.Count;
                }
                ipr.Cells = cells.ToArray();
            }
            return $"  {B} <- VC_Tree_Broadleaf2 (warm tint): {(split ? $"{moved} placements moved from {A}" : "meshes/materials refreshed")}\n";
        }

        static Material Mat(string name, string texPath, bool foliage)
        {
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };   // Editor code: allowed
                AssetDatabase.CreateAsset(mat, path);
            }
            if (AssetImporter.GetAtPath(texPath) is TextureImporter ti)
            {
                bool dirty = false;
                if (foliage && !ti.alphaIsTransparency) { ti.alphaIsTransparency = true; dirty = true; }
                if (foliage && !ti.mipMapsPreserveCoverage) { ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = 0.45f; dirty = true; }
                if (ti.textureCompression != TextureImporterCompression.CompressedHQ) { ti.textureCompression = TextureImporterCompression.CompressedHQ; dirty = true; }
                if (dirty) ti.SaveAndReimport();
            }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", foliage ? 0.12f : 0.1f);   // iteration 8: less sheen (pines read grey)
            mat.SetFloat("_Metallic", 0f);
            if (foliage)
            {
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.45f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetFloat("_Cull", 0f);       // two-sided cards
                mat.doubleSidedGI = true;
            }
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
