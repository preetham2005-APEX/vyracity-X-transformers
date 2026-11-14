#ifndef VC_FORWARD_PASS_INCLUDED
#define VC_FORWARD_PASS_INCLUDED

// Forward pass shared by the Veyra/* shaders (card F2). Include order: VC_Input.hlsl, the shader's surface file, then this.
// The surface file defines
//     void VC_Surface(VC_FragInput f, inout SurfaceData s, inout half3 normalWS)
// which fills albedo, metallic, smoothness, occlusion, emission and the per-pixel world normal; lighting, shadows,
// Forward+ lights, SSAO, reflections and fog then come from URP (UniversalFragmentPBR), like URP/Lit.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

struct VC_Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    float2 uv         : TEXCOORD0;
    float2 uv1        : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VC_Varyings
{
    float2 uv          : TEXCOORD0;
    float3 positionWS  : TEXCOORD1;
    float3 normalWS    : TEXCOORD2;
    half4  tangentWS   : TEXCOORD3;
    half   fogFactor   : TEXCOORD4;
    float2 uv1         : TEXCOORD5;
    float4 positionCS  : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

VC_Varyings VC_Vertex(VC_Attributes v)
{
    VC_Varyings o = (VC_Varyings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
    VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
    o.uv = v.uv;
    o.uv1 = v.uv1;
    o.positionWS = p.positionWS;
    o.normalWS = n.normalWS;
    o.tangentWS = half4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
    o.fogFactor = ComputeFogFactor(p.positionCS.z);
    o.positionCS = p.positionCS;
    return o;
}

half4 VC_Fragment(VC_Varyings i) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(i);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(i.positionCS);
#endif

    VC_FragInput f;
    f.uv = i.uv;
    f.uv1 = i.uv1;
    f.positionWS = i.positionWS;
    f.normalWS = normalize(i.normalWS);
    float3 t = i.tangentWS.xyz;
    float sgn = i.tangentWS.w;
    // Procedural meshes without tangents: a horizontal tangent along the wall (UV.u runs along the wall, UV.v up),
    // or +X on horizontal surfaces with planar XZ UVs; Unity's convention then needs sign -1 for +v up / +Z.
    if (dot(t, t) < 1e-4)
    {
        t = cross(f.normalWS, float3(0, 1, 0));
        if (dot(t, t) < 1e-4) t = float3(1, 0, 0);
        sgn = -1.0;
    }
    f.tangentWS = normalize(t);
    f.bitangentWS = cross(f.normalWS, f.tangentWS) * (sgn < 0.0 ? -1.0 : 1.0);
    f.viewDirWS = GetWorldSpaceNormalizeViewDir(i.positionWS);

    SurfaceData s = (SurfaceData)0;
    s.albedo = half3(1, 1, 1);
    s.alpha = 1.0;
    s.occlusion = 1.0;
    s.smoothness = 0.5;
    s.normalTS = half3(0, 0, 1);
    half3 normalWS = f.normalWS;
    VC_Surface(f, s, normalWS);

    InputData d = (InputData)0;
    d.positionWS = i.positionWS;
    d.positionCS = i.positionCS;
    d.normalWS = NormalizeNormalPerPixel(normalWS);
    d.viewDirectionWS = f.viewDirWS;
#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
#else
    d.shadowCoord = float4(0, 0, 0, 0);
#endif
    d.fogCoord = InitializeInputDataFog(float4(i.positionWS, 1.0), i.fogFactor);
    d.vertexLighting = half3(0, 0, 0);
    d.bakedGI = SampleSH(d.normalWS);
    d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
    d.shadowMask = half4(1, 1, 1, 1);

#if defined(_DBUFFER)
    // Realism pass F: decals (road wear, grime, patches, tyre marks) from the DBuffer, like URP/Lit; shaders without the
    // _DBUFFER_MRT pragma (water, neon) compile this out.
    ApplyDecalToSurfaceData(i.positionCS, s, d);
#endif
    half4 color = UniversalFragmentPBR(d, s);
    color.rgb = MixFog(color.rgb, d.fogCoord);
    color.a = 1.0;
    return color;
}

#endif
