// Veyra/Road (card F2a). Hand-written URP shader (the card asks for a Shader Graph; HLSL is used so the material can be
// authored and checked without the graph editor). Forward lighting from URP via VC_ForwardPass.hlsl; ShadowCaster,
// DepthOnly and DepthNormals are URP's own passes. Reads the Veyra globals (_VC_Wetness, _VC_RainIntensity).
Shader "Veyra/Road"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)
        [NoScaleOffset][Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Float) = 1.0
        [NoScaleOffset] _MaskMap("Mask (R metallic, G occlusion, A smoothness)", 2D) = "white" {}
        _Smoothness("Smoothness Scale", Range(0, 1)) = 1.0
        _Metallic("Metallic Scale", Range(0, 1)) = 0.0
        _OcclusionStrength("Occlusion Strength", Range(0, 1)) = 1.0
        [HDR] _EmissionColor("Emission", Color) = (0,0,0,1)
        [NoScaleOffset] _EmissionMap("Emission Map", 2D) = "white" {}

        [Header(Macro variation)]
        _MacroVariation("Albedo Variation", Range(0, 0.3)) = 0.08
        _MacroScale1("Scale 1 (m)", Float) = 23
        _MacroScale2("Scale 2 (m)", Float) = 4.5

        [Header(Wear and patches)]
        _TyreWear("Tyre Wear (needs UV1.x across the road)", Range(0, 1)) = 0
        _DetailMap("Patch / Crack Overlay (rgb albedo, a coverage)", 2D) = "black" {}
        _PatchAmount("Patch Amount", Range(0, 1)) = 0
        _PatchScale("Patch Noise Scale (m)", Float) = 9

        [Header(Wetness)]
        _WetnessResponse("Wetness Response", Range(0, 1)) = 1
        _WetnessOverride("Wetness (test slider)", Range(0, 1)) = 0
        _PuddleAmount("Puddle Amount", Range(0, 1)) = 0.6

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
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3   // F: decals (VC_ForwardPass)
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #include "VC_Input.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "VC_RoadSurface.hlsl"
            #include "VC_ForwardPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "VC_Input.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
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
