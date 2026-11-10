// Veyra/Water (round 2): sea, river and reservoir. Rippled, swelling, glittering PBR water on the shared Veyra forward pass
// (see VC_WaterSurface.hlsl for how the shared material fields are used). Same keyword set as Veyra/Neon; no runtime keywords.
Shader "Veyra/Water"
{
    Properties
    {
        [MainColor] _BaseColor("Deep Water Colour", Color) = (0.012, 0.045, 0.065, 1)
        _GlassColor("Grazing / Scatter Colour", Color) = (0.05, 0.2, 0.22, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.95
        [Normal][NoScaleOffset] _BumpMap("Ripple Normal Map", 2D) = "bump" {}
        _BumpScale("Ripple Strength", Range(0, 2)) = 0.55
        [MainTexture] _BaseMap("Ripple Tiling (use Tiling: repeats per metre)", 2D) = "white" {}
        _MacroScale1("Swell Wavelength 1 (m)", Float) = 38
        _MacroScale2("Swell Wavelength 2 (m)", Float) = 17
        _ScrollSpeed("Current Speed", Float) = 1
        [HDR] _EmissionColor("Sun Glitter Tint (a = intensity)", Color) = (1, 0.95, 0.85, 1)

        [HideInInspector] _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "IgnoreProjector" = "True" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VC_Vertex
            #pragma fragment VC_Fragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #include "VC_Input.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "VC_WaterSurface.hlsl"
            #include "VC_ForwardPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #include "VC_Input.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #include "VC_Input.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
