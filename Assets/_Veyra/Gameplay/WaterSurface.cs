using UnityEngine;
using VeyraCity.World;

namespace VeyraCity.Gameplay
{
    /// <summary>
    /// Puts the round-2 water (Veyra/Water, library slot <see cref="VeyraMat.WaterSurface"/>) on the sea, river and reservoir
    /// that CityBuilder made with its plain "Water" material: cross-rippling, swelling, sun-glittering water that mirrors the
    /// sky and the city lights. Runs once at start; no scene edits (the scene keeps its material as the fallback).
    /// </summary>
    public sealed class WaterSurface : MonoBehaviour
    {
        void Start()
        {
            var lib = VeyraMaterialLibrary.Active;
            if (lib == null || !lib.TryGet(VeyraMat.WaterSurface, out var water) || water == null)
            {
                Debug.LogWarning("[WaterSurface] no water material in the library (Veyra/Materials/Build Water); keeping the old water");
                return;
            }
            int swapped = 0;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (m == null || m.name != "Water") continue;
                r.sharedMaterial = water;
                swapped++;
            }
            Debug.Log($"[WaterSurface] {swapped} water surfaces now use {water.name}");
        }
    }
}
