// World iteration 13: coloured light pools on wet ground under neon signs (NeonSignBuilder). Additive, unlit:
// colour = _EmissionMap (soft radial blob) x _EmissionColor (HDR). WorldEnvironment drives _EmissionColor (night-only
// emissive: black, so invisible, by day). Fog fades the pool to nothing. No depth write, no shadows.
Shader "Veyra/World/GlowPool"
{
    Properties
    {
        [NoScaleOffset] _EmissionMap("Pool Shape (r)", 2D) = "black" {}
        [HDR] _EmissionColor("Pool Colour (HDR)", Color) = (0, 0, 0, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "GlowPool"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _EmissionColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half fog : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half m = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).r;
                half3 c = _EmissionColor.rgb * m;
                c = MixFogColor(c, half3(0, 0, 0), i.fog);
                return half4(c, 0);
            }
            ENDHLSL
        }
    }
}
