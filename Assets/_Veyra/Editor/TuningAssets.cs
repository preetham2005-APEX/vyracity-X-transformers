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

        // Realism §1.1 (D2): the missing driving surfaces. Values are grip / rolling-resistance multipliers on the tyre model;
        // grass eased from 0.5 / x4 (a bog everywhere the whole terrain was tagged grass) to 0.6 / x2.5.
        static readonly (string id, string field, float grip, float roll)[] Surfaces =
        {
            ("kerb", "Kerb", 0.90f, 1.1f),
            ("cobblestone", "Cobblestone", 0.85f, 1.3f),
            ("metal", "Metal", 0.75f, 1.0f),
            ("sand", "Sand", 0.55f, 3.0f),
            ("rock", "Rock", 0.80f, 1.4f),
            ("paving", "Paving", 0.92f, 1.05f),
        };

        /// <summary>Creates the realism-pass surface assets (kept if they exist) and assigns them in the GameDatabase; eases
        /// grass. Idempotent. -executeMethod VeyraCity.EditorTools.TuningAssets.EnsureSurfaces</summary>
        [MenuItem("Veyra/Tuning/Ensure Surfaces")]
        public static void EnsureSurfaces()
        {
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DbPath);
            if (db == null) { Debug.LogError($"[Tuning] no GameDatabase at {DbPath}"); return; }
            foreach (var s in Surfaces)
            {
                string path = $"Assets/_Veyra/Data/Surfaces/{s.id}.asset";
                var sd = AssetDatabase.LoadAssetAtPath<VeyraCity.VehiclePhysics.SurfaceDefinition>(path);
                if (sd == null)
                {
                    sd = ScriptableObject.CreateInstance<VeyraCity.VehiclePhysics.SurfaceDefinition>();
                    sd.Id = s.id; sd.GripMultiplier = s.grip; sd.RollingResistanceMultiplier = s.roll;
                    AssetDatabase.CreateAsset(sd, path);
                    Debug.Log($"[Tuning] created {path} (grip {s.grip}, rolling x{s.roll})");
                }
                var f = typeof(GameDatabase).GetField(s.field);
                if (f != null && (Object)f.GetValue(db) != sd) { f.SetValue(db, sd); EditorUtility.SetDirty(db); }
            }
            if (db.Grass != null && db.Grass.GripMultiplier < 0.6f)
            {
                db.Grass.GripMultiplier = 0.6f;
                db.Grass.RollingResistanceMultiplier = 2.5f;
                EditorUtility.SetDirty(db.Grass);
                Debug.Log("[Tuning] grass eased to grip 0.6, rolling x2.5");
            }
            AssetDatabase.SaveAssets();
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
