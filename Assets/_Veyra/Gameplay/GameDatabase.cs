using System;
using UnityEngine;
using VeyraCity.Assists;
using VeyraCity.VehiclePhysics;

namespace VeyraCity.Gameplay
{
    /// <summary>Everything the game needs at runtime, created by the world builder.</summary>
    [CreateAssetMenu(menuName = "Veyra/Game Database")]
    public sealed class GameDatabase : ScriptableObject
    {
        public CarEntry[] Cars = Array.Empty<CarEntry>();
        public AssistPreset Casual, Advanced, AIPreset;
        public SurfaceDefinition DryAsphalt, WetAsphalt, Concrete, Gravel, Grass, Dirt;
        public Material PlaceholderBody, PlaceholderWheel;
        // Runtime material templates moved to the VeyraMaterialLibrary in card F1 (Assets/_Veyra/World/Generated/Materials).
        public GameObject[] TrafficModels = Array.Empty<GameObject>();
        [Tooltip("Driving style layer applied to every car the game spawns (Arcade by default; null = pure simulation)")]
        public HandlingFeel Feel;
        [Tooltip("BUG-087: nitro mode table and timings (null = NITRO_BALANCE_SPEC defaults)")]
        public VeyraCity.Nitro.NitroTuning Nitro;

        public CarEntry Car(string id)
        {
            foreach (var c in Cars) if (c.Id == id) return c;
            return Cars.Length > 0 ? Cars[0] : null;
        }
    }
}
