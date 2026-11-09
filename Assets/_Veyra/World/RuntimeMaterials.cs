using UnityEngine;
using UnityEngine.Rendering;

namespace VeyraCity.World
{
    /// <summary>
    /// Material templates for runtime-created effects. Shaders looked up with Shader.Find are stripped from
    /// player builds unless a material asset references them, so the game database supplies these templates
    /// (set at startup); Shader.Find is only an editor-time fallback.
    /// </summary>
    public static class RuntimeMaterials
    {
        public static Material Lit, Unlit, ParticleAlpha, ParticleAdditive;

        public static Material NewLit(string name)
        {
            Material m = null;
            if (Lit != null)
            {
                m = new Material(Lit);
            }
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") 
                          ?? Shader.Find("Hidden/Universal Render Pipeline/FallbackError");
                if (shader != null) m = new Material(shader);
            }
            if (m != null) m.name = name;
            return m;
        }

        public static Material NewParticle(string name, bool additive)
        {
            var src = additive ? ParticleAdditive : ParticleAlpha;
            Material m = null;
            if (src != null)
            {
                m = new Material(src);
            }
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") 
                          ?? Shader.Find("Universal Render Pipeline/Unlit")
                          ?? Shader.Find("Particles/Standard Unlit");
                if (shader != null)
                {
                    m = new Material(shader);
                    ConfigureTransparent(m, additive);
                }
            }
            if (m != null) m.name = name;
            return m;
        }

        public static Material NewUnlitTransparent(string name, bool additive)
        {
            Material m = null;
            if (Unlit != null)
            {
                m = new Material(Unlit);
            }
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") 
                          ?? Shader.Find("Hidden/Universal Render Pipeline/FallbackError");
                if (shader != null)
                {
                    m = new Material(shader);
                    ConfigureTransparent(m, additive);
                }
            }
            if (m != null) m.name = name;
            return m;
        }

        public static void ConfigureTransparent(Material m, bool additive)
        {
            if (m == null) return;
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
