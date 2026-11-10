using UnityEditor;
using UnityEngine;
using VeyraCity.World;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Builds M_SkyDome (shader Veyra/SkyDome) under World/Generated/Materials and puts it in the material library slot
    /// <see cref="VeyraMat.SkyDome"/> (key 1050, the vehicle/visual session's range). Batch:
    /// <c>-executeMethod VeyraCity.EditorTools.SkyDomeMaterial.Build</c>. Idempotent: an existing asset is updated in place.
    /// </summary>
    public static class SkyDomeMaterial
    {
        public const string Path = "Assets/_Veyra/World/Generated/Materials/M_SkyDome.mat";

        [MenuItem("Veyra/Materials/Build Sky Dome")]
        public static void Build()
        {
            var shader = Shader.Find("Veyra/SkyDome");   // editor code: the material asset keeps the reference for the player
            if (shader == null) { Debug.LogError("[SkyDome] shader Veyra/SkyDome not found"); return; }
            var lib = AssetDatabase.LoadAssetAtPath<VeyraMaterialLibrary>(VeyraMaterialFactory.LibraryPath);
            if (lib == null) { Debug.LogError("[SkyDome] no VeyraMaterialLibrary at " + VeyraMaterialFactory.LibraryPath); return; }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(Path);
            if (mat == null)
            {
                mat = new Material(shader) { name = "M_SkyDome" };
                AssetDatabase.CreateAsset(mat, Path);
            }
            else mat.shader = shader;
            mat.renderQueue = -1;   // from the shader: Transparent-450, after the skybox
            EditorUtility.SetDirty(mat);
            VeyraPalette.SetSlot(lib, VeyraMat.SkyDome, mat);
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SkyDome] {Path} in library slot {VeyraMat.SkyDome} ({(int)VeyraMat.SkyDome}), shader supported: {shader.isSupported}");
        }
    }
}
