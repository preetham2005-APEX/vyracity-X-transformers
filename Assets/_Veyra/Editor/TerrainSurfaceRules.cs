using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VeyraCity.Gameplay;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.EditorTools
{
    /// <summary>
    /// Master prompt §1.1 (every terrain was tagged Grass, so beaches, plazas and rock drove like a bog): WorldBuildBatch
    /// step `terrainsurface`. Every Terrain's VehicleSurface keeps Grass as the fallback and gets D2's per-splat-layer rules
    /// (GameDatabase.DefaultTerrainLayerRules: Sand, Rock, Paving, Dirt / ForestFloor / Farmland -> Dirt, Lawn / Grass ->
    /// Grass), resolved at the wheel contact by VehicleController. Idempotent (rules replaced, not appended). Adds the
    /// component where a terrain had none. Report Logs/Shoulders/terrain_surfaces.txt.
    /// </summary>
    public static class TerrainSurfaceRules
    {
        public static string Run()
        {
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>("Assets/_Veyra/Data/GameDatabase.asset");
            if (db == null) return "FAIL: GameDatabase.asset missing\n";
            var rules = db.DefaultTerrainLayerRules();
            int terrains = 0, added = 0;
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
            {
                var vs = t.GetComponent<VehicleSurface>();
                if (vs == null) { vs = t.gameObject.AddComponent<VehicleSurface>(); added++; }
                if (vs.Surface == null) vs.Surface = db.Grass;
                vs.TerrainLayers = rules;
                EditorUtility.SetDirty(vs);
                terrains++;
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            string rep = $"Terrain surfaces: {terrains} terrains get {rules.Length} splat-layer rules ({string.Join(", ", rules.Select(r => (r.NameContains ?? r.Layer?.name) + "->" + (r.Surface != null ? r.Surface.name : "null")))}); VehicleSurface added on {added}\n";
            Directory.CreateDirectory("Logs/Shoulders");
            File.WriteAllText("Logs/Shoulders/terrain_surfaces.txt", rep);
            return rep;
        }
    }
}
