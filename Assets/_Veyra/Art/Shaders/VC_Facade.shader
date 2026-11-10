// Veyra/Facade (card F2b): window grid from UVs, interior mapping, seeded night windows, grime. Hand-written URP shader
// (deviation from the card's Shader Graph; see VC_Road.shader). Reads _VC_NightFactor and _VC_TimeOfDay.
Shader "Veyra/Facade"
{
    Properties
    {
        [MainTexture] _BaseMap("Wall Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Wall Color", Color) = (1,1,1,1)
        [NoScaleOffset][Normal] _BumpMap("Wall Normal", 2D) = "bump" {}
        _BumpScale("Normal Scale", Float) = 1.0
        [NoScaleOffset] _MaskMap("Wall Mask (R metallic, G occlusion, A smoothness)", 2D) = "white" {}
        _Smoothness("Smoothness Scale", Range(0, 1)) = 1.0
        _Metallic("Metallic Scale", Range(0, 1)) = 0.0
        _OcclusionStrength("Occlusion Strength", Range(0, 1)) = 1.0
        _WallTiling("Wall Texture Repeats per UV Tile (xy)", Vector) = (2, 2, 0, 0)

        [Header(Window grid)]
        _BaysPerTile("Bays per UV Tile", Float) = 4
        _FloorsPerTile("Floors per UV Tile", Float) = 4
        _CurtainWall("Glass Curtain Wall", Range(0, 1)) = 0
        _WindowRect("Window (x width, y sill, z height, w frame)", Vector) = (0.56, 0.24, 0.56, 0.035)

        [Header(Glass and interior)]
        _GlassColor("Glass Tint", Color) = (0.32, 0.4, 0.46, 1)
        _GlassSmoothness("Glass Smoothness", Range(0.5, 1)) = 0.94
        [NoScaleOffset] _InteriorAtlas("Interior Atlas (4 x 4 rooms)", 2D) = "grey" {}
        _RoomDepth("Room Depth (bays)", Range(0.3, 2)) = 1.1
        _InteriorDim("Interior Brightness by Day", Range(0, 1)) = 0.32

        [Header(Night windows)]
        _LitFractionMin("Lit Fraction Min", Range(0, 1)) = 0.25
        _LitFractionMax("Lit Fraction Max", Range(0, 1)) = 0.7
        _WindowEmission("Lit Window Emission (HDR)", Range(0, 4)) = 2.2
        _Office("Office (dims after 23:00)", Range(0, 1)) = 0
        _Seed("Seed", Float) = 0

        [Header(Weathering)]
        _Grime("Grime", Range(0, 1)) = 0.5

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
            #include "VC_FacadeSurface.hlsl"
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
