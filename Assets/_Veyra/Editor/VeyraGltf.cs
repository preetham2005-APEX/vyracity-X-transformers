using UnityEditor;
using UnityEngine;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Card S4: Veyra design-package glTF/GLB files (VEYRA_CITY_WORLD_DESIGN, "glTF: +X east, +Y up, -Z north; origin = world
    /// centre (x=4000, y=3000)") imported with glTFast arrive turned 180° about Y (glTFast flips X for Unity's left-handed axes,
    /// so east lands on -X and north on -Z). Instantiate them through this helper: the root rotation (0, 180, 0) puts east on
    /// +X and north on +Z, matching Unity = (x - 4000, z, y - 3000). Confirmed by the S4 test (Spire at (170, ~10, -670)).
    /// </summary>
    public static class VeyraGltf
    {
        public static readonly Vector3 PackageRootEuler = new Vector3(0f, 180f, 0f);

        /// <summary>Instantiates an imported Veyra package GLB with the axis fix applied to its root.</summary>
        public static GameObject InstantiatePackageGlb(GameObject glbAsset, Transform parent = null)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(glbAsset, parent);
            go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(PackageRootEuler));
            go.transform.localScale = Vector3.one;
            return go;
        }
    }
}
