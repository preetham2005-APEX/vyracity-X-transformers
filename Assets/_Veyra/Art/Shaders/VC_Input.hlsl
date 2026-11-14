#ifndef VC_INPUT_INCLUDED
#define VC_INPUT_INCLUDED

// One material layout for every Veyra/* shader (card F2), so every pass of every Veyra shader has the same
// UnityPerMaterial buffer (SRP Batcher) and URP's ShadowCaster/DepthOnly/DepthNormals passes can be reused as they are
// (they need _BaseMap, _BaseMap_ST, _BaseColor and _Cutoff from here and SurfaceInput.hlsl).

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "VC_Common.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _DetailMap_ST;
    half4 _BaseColor;
    half4 _EmissionColor;
    half _Cutoff;
    half _Smoothness;
    half _Metallic;
    half _BumpScale;
    half _OcclusionStrength;

    // Road (Veyra/Road)
    half _MacroVariation;   // ± albedo fraction from world-space noise (0.08 = ±8 %)
    half _MacroScale1;      // metres
    half _MacroScale2;      // metres
    half _WetnessResponse;  // how strongly the global _VC_Wetness affects this material (0–1)
    half _WetnessOverride;  // test slider: added to the global wetness
    half _PuddleAmount;     // 0–1
    half _TyreWear;         // 0–1, wear lanes from UV1.x (0–1 across the carriageway) when the mesh has it
    half _PatchAmount;      // 0–1 patch/crack overlay from _DetailMap
    half _PatchScale;       // metres per patch-noise cell

    // Facade (Veyra/Facade)
    float4 _WallTiling;     // xy: wall texture repeats per UV tile
    float4 _WindowRect;     // x: width fraction of a bay, y: sill height, z: window height (fractions of a floor), w: frame width
    half4 _GlassColor;
    half _BaysPerTile;
    half _FloorsPerTile;
    half _CurtainWall;      // 0 = punched windows, 1 = glass curtain wall with mullions and spandrels
    half _GlassSmoothness;
    half _RoomDepth;        // interior depth in bay widths
    half _InteriorDim;      // interior brightness by day
    half _LitFractionMin;
    half _LitFractionMax;
    half _WindowEmission;   // HDR intensity of a lit window
    half _Office;           // 1 = offices dim after 23:00
    half _Grime;
    half _Seed;

    // Kit glass (Veyra/KitGlass): the authored building kit carries its piers, mullions and floor bands as real
    // geometry, so its glazing is lit per floor and per bay from world position instead of from a UV window grid.
    half _KitFloorHeight;   // metres per floor
    half _KitBayWidth;      // metres per structural bay

    // Neon / emissive (Veyra/Neon)
    half4 _NeonColor;
    half _NeonIntensity;    // HDR multiplier at night (2.5–8)
    half _DayFactor;        // fraction of the night emission by day (0.1)
    half _FlickerChance;    // share of signs (per ~6 m cell) that flicker
    half _ChaseCount;       // marquee bulbs per UV unit (0 = off)
    half _ChaseSpeed;
    half _ScrollSpeed;      // LED screen UV scroll per second
    half _TubeFalloff;
CBUFFER_END

// What a Veyra surface function receives from the forward pass (VC_ForwardPass.hlsl).
struct VC_FragInput
{
    float2 uv;
    float2 uv1;
    float3 positionWS;
    float3 normalWS;
    float3 tangentWS;
    float3 bitangentWS;
    float3 viewDirWS;    // from the surface towards the camera
};

TEXTURE2D(_MaskMap);        SAMPLER(sampler_MaskMap);        // R metallic, G occlusion, A smoothness (linear)
TEXTURE2D(_DetailMap);      SAMPLER(sampler_DetailMap);      // patch / crack overlay: rgb albedo, a coverage
TEXTURE2D(_InteriorAtlas);  SAMPLER(sampler_InteriorAtlas);  // 4 x 4 room back walls

#endif
