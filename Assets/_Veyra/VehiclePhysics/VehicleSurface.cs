using System;
using System.Collections.Generic;
using UnityEngine;

namespace VeyraCity.VehiclePhysics
{
    /// <summary>
    /// Marks a collider (or its children) as a driving surface. On a terrain it can also map the terrain's splat layers to
    /// surfaces (realism §1.1, D2 runtime / world editor tagging): the wheel then drives on the dominant layer at its contact
    /// point (sand, rock, paving, dirt...) instead of one surface for the whole tile. <see cref="Surface"/> stays the fallback.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleSurface : MonoBehaviour
    {
        public SurfaceDefinition Surface;

        [Serializable]
        public struct LayerRule
        {
            [Tooltip("Terrain layer (preferred) ...")] public TerrainLayer Layer;
            [Tooltip("... or a case-insensitive part of the layer name, e.g. \"Sand\"")] public string NameContains;
            public SurfaceDefinition Surface;
        }

        [Tooltip("Terrains only: surface per splat layer; first matching rule wins, unmatched layers use Surface")]
        public LayerRule[] TerrainLayers = Array.Empty<LayerRule>();

        Terrain terrain;
        byte[] dominant;                  // dominant layer index per alphamap texel
        SurfaceDefinition[] layerSurface; // surface per terrain layer index
        int res;
        Vector3 origin, size;

        public bool HasLayerRules => TerrainLayers != null && TerrainLayers.Length > 0;

        /// <summary>Surface under a world point: the dominant splat layer's surface on a mapped terrain, else <see cref="Surface"/>.</summary>
        public SurfaceDefinition SurfaceAt(Vector3 world)
        {
            if (!HasLayerRules) return Surface;
            if (dominant == null && !Build()) return Surface;
            int x = Mathf.Clamp((int)((world.x - origin.x) / size.x * res), 0, res - 1);
            int z = Mathf.Clamp((int)((world.z - origin.z) / size.z * res), 0, res - 1);
            var s = layerSurface[dominant[z * res + x]];
            return s != null ? s : Surface;
        }

        /// <summary>Caches the dominant layer per texel once (no per-wheel GetAlphamaps allocation while driving).</summary>
        bool Build()
        {
            if (terrain == null) terrain = GetComponent<Terrain>();
            if (terrain == null || terrain.terrainData == null) return false;
            var td = terrain.terrainData;
            var layers = td.terrainLayers;
            if (layers == null || layers.Length == 0) return false;
            layerSurface = new SurfaceDefinition[layers.Length];
            for (int i = 0; i < layers.Length; i++) layerSurface[i] = Match(layers[i]);
            res = td.alphamapResolution;
            var maps = td.GetAlphamaps(0, 0, res, res);   // [z, x, layer], once per tile
            int n = Mathf.Min(layers.Length, maps.GetLength(2));
            dominant = new byte[res * res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    int best = 0; float bw = -1f;
                    for (int l = 0; l < n; l++) { float w = maps[z, x, l]; if (w > bw) { bw = w; best = l; } }
                    dominant[z * res + x] = (byte)Mathf.Min(best, 255);
                }
            origin = terrain.GetPosition();
            size = td.size;
            return true;
        }

        SurfaceDefinition Match(TerrainLayer layer)
        {
            if (layer == null) return null;
            foreach (var r in TerrainLayers)
                if (r.Surface != null && r.Layer == layer) return r.Surface;
            string n = layer.name.ToLowerInvariant();
            foreach (var r in TerrainLayers)
                if (r.Surface != null && r.Layer == null && !string.IsNullOrEmpty(r.NameContains) && n.Contains(r.NameContains.ToLowerInvariant())) return r.Surface;
            return null;
        }

        /// <summary>Drops the cache (after the terrain is repainted).</summary>
        public void Invalidate() { dominant = null; }
    }
}
