using UnityEditor;
using UnityEngine;
using VeyraCity.Gameplay;
using VeyraCity.Nitro;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// BUG-087 (round 3): creates the versioned tuning assets that replace hard-coded tables and assigns them in the
    /// GameDatabase. Idempotent: existing assets are kept (their tuned values are never overwritten).
    /// Batch: -executeMethod VeyraCity.EditorTools.TuningAssets.Ensure
    /// </summary>
    public static class TuningAssets
    {
        const string DbPath = "Assets/_Veyra/Data/GameDatabase.asset";
        const string NitroPath = "Assets/_Veyra/Data/Tuning/NitroTuning.asset";

        /// <summary>D2 round 3 asset step in one batch call: tuning assets, the material library (soft-particle template). -executeMethod VeyraCity.EditorTools.TuningAssets.EnsureD2</summary>
        public static void EnsureD2()
        {
            Ensure();
            Debug.Log("[Tuning] material library: " + VeyraMaterialFactory.BuildLibrary());
            // paint flake (PaintFlake.Apply) is applied on its own after a close-up review (pass 37: too grainy at first)
        }

        [MenuItem("Veyra/Tuning/Ensure Tuning Assets")]
        public static void Ensure()
        {
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DbPath);
            if (db == null) { Debug.LogError($"[Tuning] no GameDatabase at {DbPath}"); return; }
            if (!AssetDatabase.IsValidFolder("Assets/_Veyra/Data/Tuning")) AssetDatabase.CreateFolder("Assets/_Veyra/Data", "Tuning");
            var nitro = AssetDatabase.LoadAssetAtPath<NitroTuning>(NitroPath);
            if (nitro == null)
            {
                nitro = ScriptableObject.CreateInstance<NitroTuning>();
                AssetDatabase.CreateAsset(nitro, NitroPath);
                Debug.Log($"[Tuning] created {NitroPath}");
            }
            if (db.Nitro != nitro)
            {
                db.Nitro = nitro;
                EditorUtility.SetDirty(db);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Tuning] GameDatabase.Nitro = {AssetDatabase.GetAssetPath(db.Nitro)}");
        }
    }
}
