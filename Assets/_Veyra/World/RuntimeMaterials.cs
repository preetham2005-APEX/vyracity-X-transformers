using UnityEngine;

namespace VeyraCity.World
{
    /// <summary>
    /// Per-instance materials for things built at runtime (effects, markers, garage, procedural traffic): each one is a copy of
    /// a template from the active <see cref="VeyraMaterialLibrary"/>, so its shader and keywords come from a material asset in
    /// the build (card F1, BUG-002). No shader lookups by name and no keyword changes happen here: the templates already carry
    /// their keywords (FxLit has _EMISSION on with black emission; the transparent ones are set up as transparent).
    /// Each method returns null and logs one precise error when the library or its slot is missing.
    /// </summary>
    public static class RuntimeMaterials
    {
        /// <summary>A copy of the FxLit template (URP/Lit, _EMISSION on): set _BaseColor, _Smoothness, _Metallic, _EmissionColor.</summary>
        public static Material NewLit(string name) => Copy(VeyraMat.FxLit, name);

        /// <summary>A copy of a URP/Particles/Unlit template, alpha-blended or additive.</summary>
        public static Material NewParticle(string name, bool additive) => Copy(additive ? VeyraMat.FxParticleAdditive : VeyraMat.FxParticleAlpha, name);

        /// <summary>A copy of a URP/Unlit transparent template, alpha-blended or additive.</summary>
        public static Material NewUnlitTransparent(string name, bool additive) => Copy(additive ? VeyraMat.FxUnlitAdditive : VeyraMat.FxUnlitAlpha, name);

        static bool reportedNoLibrary;

        static Material Copy(VeyraMat key, string name)
        {
            var lib = VeyraMaterialLibrary.Active;
            if (lib == null)
            {
                if (!reportedNoLibrary)
                {
                    reportedNoLibrary = true;
                    Debug.LogError($"[RuntimeMaterials] no active VeyraMaterialLibrary while creating '{name}' ({key}): the scene needs GameFlow, " +
                                   "WorldEnvironment or a VeyraMaterialsHolder with the library assigned (Veyra/Materials/Build Material Library).");
                }
                return null;
            }
            var template = lib.Get(key);
            if (template == null) return null;
            return new Material(template) { name = name };
        }
    }
}
