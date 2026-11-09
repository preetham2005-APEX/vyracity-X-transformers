using UnityEditor;
using UnityEngine;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// CLAUDE.md §4: Unity does not recognise Unreal-style collision prefixes. On model import,
    /// UCX_* meshes become convex MeshColliders and UBX_* meshes become BoxColliders; their renderers are removed.
    /// Vehicle models under Art/Vehicles also get consistent import settings.
    /// </summary>
    public sealed class CollisionPrefixPostprocessor : AssetPostprocessor
    {
        const string VehicleArt = "Assets/_Veyra/Art/Vehicles/";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(VehicleArt)) return;
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importBlendShapes = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        }

        void OnPostprocessModel(GameObject root)
        {
            int convex = 0, boxes = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                bool ucx = t.name.StartsWith("UCX_");
                bool ubx = t.name.StartsWith("UBX_");
                if (!ucx && !ubx) continue;
                var mf = t.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                if (ucx)
                {
                    var mc = t.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = true;
                    convex++;
                }
                else
                {
                    var b = mf.sharedMesh.bounds;
                    var bc = t.gameObject.AddComponent<BoxCollider>();
                    bc.center = b.center;
                    bc.size = b.size;
                    boxes++;
                }
                var mr = t.GetComponent<MeshRenderer>();
                if (mr != null) Object.DestroyImmediate(mr, true);
                if (ubx) Object.DestroyImmediate(mf, true);
            }
            if (convex + boxes > 0) Debug.Log($"[Veyra] {assetPath}: {convex} convex + {boxes} box colliders from UCX_/UBX_ nodes");
        }
    }
}
