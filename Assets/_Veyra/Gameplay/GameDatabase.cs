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
        [Tooltip("Templates so runtime-created materials keep their shaders in player builds")]
        public Material LitTemplate, UnlitAdditiveTemplate, ParticleAlphaTemplate, ParticleAdditiveTemplate;
        public GameObject[] TrafficModels = Array.Empty<GameObject>();

        public CarEntry Car(string id)
        {
            foreach (var c in Cars) if (c.Id == id) return c;
            return Cars.Length > 0 ? Cars[0] : null;
        }
    }
}
