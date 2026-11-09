using UnityEngine;
using UnityEngine.Rendering;

namespace VeyraCity.World
{
    /// <summary>
    /// Material templates for runtime-created effects. Shaders looked up with Shader.Find are stripped from
    /// player builds unless a material asset references them, so the game database supplies these templates
    /// (set at startup); Shader.Find is only a fallback for the editor.
    /// </summary>
    public static class RuntimeMaterials
    {
        public static Material Lit, Unlit, ParticleAlpha, ParticleAdditive;

        public static Material NewLit(string name)
        {
            var m = Lit != null ? new Material(Lit) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.name = name;
            return m;
        }

        public static Material NewParticle(string name, bool additive)
        {
            var src = additive ? ParticleAdditive : ParticleAlpha;
            Material m;
            if (src != null) m = new Material(src);
            else
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                ConfigureTransparent(m, additive);
            }
            m.name = name;
            return m;
        }

        public static Material NewUnlitTransparent(string name, bool additive)
        {
            Material m;
            if (Unlit != null) m = new Material(Unlit);
            else
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                ConfigureTransparent(m, additive);
            }
            m.name = name;
            return m;
        }

        public static void ConfigureTransparent(Material m, bool additive)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", additive ? (int)BlendMode.One : (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.renderQueue = 3000;
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
    }
}
