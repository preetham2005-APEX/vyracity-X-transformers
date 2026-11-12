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
            VeyraCity.World.Streaming.CellStreamer.CellLoaded += (cell, sc) => { if (this != null) Swap(); };   // round 3: streamed cells
            Swap();
        }

        void Swap()
        {
            var lib = VeyraMaterialLibrary.Active;
            if (lib == null || !lib.TryGet(VeyraMat.WaterSurface, out var water) || water == null)
            {
                Debug.LogWarning("[WaterSurface] no water material in the library (Veyra/Materials/Build Water); keeping the old water");
                return;
            }
            int swapped = 0, inland = 0;
            MaterialPropertyBlock calm = null;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
            {
                var m = r.sharedMaterial;
                if (m == null || m.name != "Water") continue;
                r.sharedMaterial = water;
                swapped++;
                // iteration 20: the river and the reservoir are calm (a quarter of the sea's swell, softer ripples)
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name == "SeaPlane") continue;
                if (calm == null)
                {
                    calm = new MaterialPropertyBlock();
                    calm.SetFloat("_MacroVariation", 0.25f);
                    calm.SetFloat("_BumpScale", water.GetFloat("_BumpScale") * 0.6f);
                }
                r.SetPropertyBlock(calm);
                inland++;
            }
            Debug.Log($"[WaterSurface] {swapped} water surfaces now use {water.name} ({inland} calm inland)");
        }
    }
}
