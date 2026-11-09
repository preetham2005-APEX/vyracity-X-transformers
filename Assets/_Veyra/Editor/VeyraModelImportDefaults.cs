using UnityEditor;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card S4: import defaults for models from the Blender pipeline (Tools/Blender/vc_tools.py presets VC_Unity_Prop and
    /// VC_Unity_World): Scale Factor 1, Convert Units on, Bake Axis Conversion on, Read/Write off, lightmap UVs only for
    /// static architecture (world chunks and Models/Architecture). Applied on a model's first import only, so a setting
    /// changed later by hand (or by card B3's postprocessor) is not overwritten. Vehicles keep their own importer settings.
    /// </summary>
    public sealed class VeyraModelImportDefaults : AssetPostprocessor
    {
        public const string ModelsRoot = "Assets/_Veyra/Art/Models/";
        public const string ChunksRoot = "Assets/_Veyra/Art/World/Chunks/";

        public static bool Applies(string path) => path.StartsWith(ModelsRoot) || path.StartsWith(ChunksRoot);
        public static bool IsStaticArchitecture(string path) => path.StartsWith(ChunksRoot) || path.StartsWith(ModelsRoot + "Architecture/");

        void OnPreprocessModel()
        {
            if (!Applies(assetPath) || !assetImporter.importSettingsMissing) return;
            if (!(assetImporter is ModelImporter mi)) return;
            mi.globalScale = 1f;
            mi.useFileScale = true;        // "Convert Units"
            mi.bakeAxisConversion = true;
            mi.isReadable = false;
            mi.generateSecondaryUV = IsStaticArchitecture(assetPath);
            mi.importCameras = false;
            mi.importLights = false;
        }
    }
}
