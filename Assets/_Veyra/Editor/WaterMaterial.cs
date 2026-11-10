using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Builds M_Water (shader Veyra/Water) under World/Generated/Materials, puts it in library slot
    /// <see cref="VeyraMat.WaterSurface"/> (Water range 800, agreed with the F/L-card session), takes the ripple normal map
    /// from CityBuilder's Water material, and adds the shader's variants to VC_Variants by copying the keyword sets recorded
    /// for Veyra/Neon (same pragmas), so the water is preloaded like the other Veyra shaders.
    /// Batch: <c>-executeMethod VeyraCity.EditorTools.WaterMaterial.Build</c>. Idempotent.
    /// </summary>
    public static class WaterMaterial
    {
        public const string Path = "Assets/_Veyra/World/Generated/Materials/M_Water.mat";
        const string OldWater = "Assets/_Veyra/World/Generated/Materials/Water.mat";

        [MenuItem("Veyra/Materials/Build Water")]
        public static void Build()
        {
            var shader = Shader.Find("Veyra/Water");   // editor code: the material asset carries the reference into builds
            if (shader == null) { Debug.LogError("[Water] shader Veyra/Water not found"); return; }
            var lib = AssetDatabase.LoadAssetAtPath<VeyraMaterialLibrary>(VeyraMaterialFactory.LibraryPath);
            if (lib == null) { Debug.LogError("[Water] no VeyraMaterialLibrary"); return; }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(Path);
            if (mat == null)
            {
                mat = new Material(shader) { name = "M_Water" };
                AssetDatabase.CreateAsset(mat, Path);
            }
            else mat.shader = shader;
            var old = AssetDatabase.LoadAssetAtPath<Material>(OldWater);
            if (old != null && old.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", old.GetTexture("_BumpMap"));
            mat.SetColor("_BaseColor", new Color(0.012f, 0.045f, 0.065f, 1f));
            mat.SetColor("_GlassColor", new Color(0.05f, 0.2f, 0.22f, 1f));
            mat.SetFloat("_Smoothness", 0.95f);
            mat.SetFloat("_BumpScale", 0.55f);
            mat.SetTextureScale("_BaseMap", new Vector2(1f / 9f, 1f / 9f));
            mat.SetFloat("_MacroScale1", 38f);
            mat.SetFloat("_MacroScale2", 17f);
            mat.SetFloat("_ScrollSpeed", 1f);
            mat.SetColor("_EmissionColor", new Color(1f, 0.95f, 0.85f, 1f));
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            VeyraPalette.SetSlot(lib, VeyraMat.WaterSurface, mat);
            EditorUtility.SetDirty(lib);
            int added = AddVariants(lib.Variants, shader, Shader.Find("Veyra/Neon"));
            AssetDatabase.SaveAssets();
            Debug.Log($"[Water] {Path} in slot {VeyraMat.WaterSurface} ({(int)VeyraMat.WaterSurface}), supported {shader.isSupported}, {added} variants added to VC_Variants");
        }

        static int AddVariants(ShaderVariantCollection collection, Shader target, Shader template)
        {
            if (collection == null || template == null) return 0;
            var so = new SerializedObject(collection);
            var shaders = so.FindProperty("m_Shaders");
            var sets = new List<(PassType pass, string[] keywords)>();
            for (int i = 0; i < shaders.arraySize; i++)
            {
                var e = shaders.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("first").objectReferenceValue != template) continue;
                var variants = e.FindPropertyRelative("second.variants");
                for (int v = 0; v < variants.arraySize; v++)
                {
                    var ve = variants.GetArrayElementAtIndex(v);
                    string kw = ve.FindPropertyRelative("keywords").stringValue ?? "";
                    var pass = (PassType)ve.FindPropertyRelative("passType").intValue;
                    if (pass == PassType.ShadowCaster) continue;   // the water casts no shadows
                    sets.Add((pass, kw.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries)));
                }
            }
            int added = 0;
            foreach (var (pass, keywords) in sets)
            {
                try
                {
                    if (collection.Add(new ShaderVariantCollection.ShaderVariant(target, pass, keywords))) added++;
                }
                catch (System.ArgumentException) { }   // a keyword the water's pass does not declare
            }
            EditorUtility.SetDirty(collection);
            return added;
        }
    }
}
