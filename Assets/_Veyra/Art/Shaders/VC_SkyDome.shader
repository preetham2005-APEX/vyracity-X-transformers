// Veyra/SkyDome (visual iteration 5; owner: vehicle/visual session, agreed with the F/L-card session): a sky layer drawn
// over the procedural skybox, behind everything else. It adds what the GTA V / Cyberpunk references have and the plain
// skybox lacked: drifting fBm clouds lit by the sun (golden-hour tint, silver lining, grey overcast and rain decks), a
// horizon haze band matched to the fog colour, orange city light-pollution glow at night, stars and a moon (the moon sits
// where WorldEnvironment puts the moonlight). Reads only the VC globals (VC_Common.hlsl) plus per-material values that
// SkyDome.cs sets each frame (cloudiness, haze colour). No keywords. Depth: the vertices are pushed to the far plane and
// ZTest LEqual, so the dome shows only where no geometry was drawn (like a skybox), in the transparent queue after it.
Shader "Veyra/SkyDome"
{
    Properties
    {
        _Cloudiness("Cloudiness (set by SkyDome.cs)", Range(0, 1)) = 0.3
        _CloudScale("Cloud Scale", Float) = 0.55
        _CloudSpeed("Cloud Drift (UV/s)", Float) = 0.006
        _StarDensity("Star Density", Range(0, 1)) = 0.4
        _HazeStrength("Horizon Haze", Range(0, 1)) = 0.5
        _HazeColor("Haze Colour (fog colour, set by SkyDome.cs)", Color) = (0.66, 0.72, 0.8, 1)
        _CityGlow("Night City Glow", Range(0, 1)) = 0.45
        [HDR] _CityGlowColor("City Glow Colour", Color) = (1, 0.46, 0.26, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent-450" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Skybox" }

        Pass
        {
            Name "SkyDome"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "VC_Common.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Cloudiness;
                float _CloudScale;
                float _CloudSpeed;
                float _StarDensity;
                float _HazeStrength;
                float4 _HazeColor;
                float _CityGlow;
                float4 _CityGlowColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 dir = normalize(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(_WorldSpaceCameraPos + dir * 1000.0);
            #if UNITY_REVERSED_Z
                o.positionCS.z = o.positionCS.w * 1e-6;
            #else
                o.positionCS.z = o.positionCS.w * (1.0 - 1e-6);
            #endif
                o.dir = dir;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 v = normalize(i.dir);
                float3 sd = _VC_SunDirection.xyz;
                float3 sun = dot(sd, sd) > 1e-4 ? normalize(sd) : normalize(float3(0.3, 0.75, 0.4));
                float night = saturate(_VC_NightFactor);
                float day = 1.0 - night;
                float rain = saturate(_VC_RainIntensity);
                float cloudy = saturate(max(_Cloudiness, rain));
                float up = v.y;
                float above = smoothstep(-0.03, 0.05, up);
                float golden = saturate(1.0 - abs(sun.y) * 3.5) * day;
                float sunVis = smoothstep(-0.05, 0.05, sun.y) * day;
                float mu = dot(v, sun);

                // ---- sun glow (the skybox draws the disc) and the moon (WorldEnvironment's night light direction)
                float3 sunCol = lerp(float3(1.0, 0.95, 0.86), float3(1.0, 0.55, 0.25), golden);
                float sunGlow = (pow(saturate(mu), 8.0) * 0.22 + pow(saturate(mu), 90.0) * 0.5) * (1.0 + golden * 1.6);
                float3 col = sunCol * sunGlow * sunVis * lerp(1.0, 0.3, cloudy);
                float moonK = smoothstep(0.85, 1.0, night) * above;
                float moonDisc = smoothstep(0.99962, 0.99978, mu);
                float maria = 0.72 + 0.28 * VC_ValueNoise(float2(dot(v, normalize(cross(sun, float3(0, 1, 0.001)))), v.y) * 700.0);
                float moonGlow = pow(saturate(mu), 120.0) * 0.12 + pow(saturate(mu), 1200.0) * 0.35;
                col += float3(0.82, 0.88, 1.0) * (moonDisc * 1.6 * maria + moonGlow) * moonK * (1.0 - 0.7 * cloudy);

                // ---- stars: one in ~1 % of 0.6° cells, twinkling; few in a lit city, none behind cloud
                float2 sp = float2(atan2(v.x, v.z), asin(clamp(up, -1.0, 1.0))) * (57.29578 * 1.6);
                float2 cell = floor(sp);
                float h = VC_Hash12(cell + 7.31);
                float2 off = VC_Hash22(cell) * 0.7 + 0.15;
                float star = smoothstep(0.12, 0.0, length(frac(sp) - off)) * step(1.0 - 0.025 * _StarDensity, h);
                float tw = 0.6 + 0.4 * sin(_Time.y * (1.5 + h * 4.0) + h * 40.0);
                col += float3(0.9, 0.94, 1.0) * star * tw * 1.6 * night * smoothstep(0.1, 0.4, up);

                // ---- horizon haze by day (matches the fog colour), city light pollution by night
                float haze = exp(-max(up, 0.0) * 5.5) * above;
                float ha = saturate(haze * (_HazeStrength * day + _CityGlow * night * 0.8));
                float3 hazeCol = lerp(_HazeColor.rgb * (0.6 + 0.4 * sunVis), _CityGlowColor.rgb * 0.22 + _HazeColor.rgb, night);
                hazeCol = lerp(hazeCol, hazeCol * float3(1.15, 0.85, 0.7), golden * pow(saturate(mu * 0.5 + 0.5), 2.0));
                col = col * (1.0 - ha) + hazeCol * ha;
                float a = ha;

                // ---- sunset band (round 2): a warm orange-to-pink gradient low in the sky around the sun at golden hour
                float az = saturate(mu * 0.5 + 0.5);
                float band = exp(-max(up, 0.0) * 9.0) * above * golden * (0.35 + 0.65 * pow(az, 2.0));
                float3 bandCol = lerp(float3(0.95, 0.42, 0.55), float3(1.0, 0.55, 0.22), pow(az, 3.0));
                col = col * (1.0 - band * 0.55) + bandCol * band * 0.55;
                a = a * (1.0 - band * 0.55) + band * 0.55;

                // ---- cirrus (round 2): a thin, high, wind-streaked layer above the cumulus deck
                float2 cdrift = (float2(1.0, 0.35) + _VC_WindDirection.xz * (0.5 + 2.0 * _VC_WindStrength)) * (_Time.y * _CloudSpeed);
                float2 cuv = v.xz / max(up, 0.06) * (_CloudScale * 0.33) + cdrift * 1.8;
                cuv = float2(cuv.x * 0.35 + cuv.y * 0.2, cuv.y * 1.6);   // stretched along the wind
                float cn = VC_Fbm(cuv * 2.3 + 11.7, 4);
                float cir = smoothstep(0.52, 0.78, cn) * smoothstep(0.05, 0.35, up) * (1.0 - 0.7 * cloudy) * 0.45;
                float3 cirCol = lerp(float3(0.97, 0.98, 1.0) * (0.35 + 0.65 * sunVis), float3(1.0, 0.62, 0.55), golden * (0.4 + 0.6 * az));
                cirCol = lerp(cirCol, float3(0.06, 0.06, 0.09) + _CityGlowColor.rgb * 0.04, night);
                col = col * (1.0 - cir) + cirCol * cir;
                a = a * (1.0 - cir) + cir;

                // ---- clouds: a deck projected on a plane, drifting with the wind
                float2 wind = _VC_WindDirection.xz * (0.5 + 2.0 * _VC_WindStrength);
                float2 drift = (float2(1.0, 0.35) + wind) * (_Time.y * _CloudSpeed);
                float2 uv = v.xz / max(up, 0.04) * _CloudScale + drift;
                float n = VC_Fbm(uv, 5);
                float nl = VC_Fbm(uv + sun.xz * 0.08, 3);
                float coverT = lerp(0.64, 0.28, cloudy);
                float dens = smoothstep(coverT, coverT + 0.22, n);
                float ca = dens * smoothstep(0.02, 0.24, up) * lerp(0.9, 1.0, cloudy);
                float lit = saturate(0.55 + (n - nl) * 4.0);
                float3 baseDay = lerp(float3(0.6, 0.64, 0.72), float3(0.42, 0.44, 0.49), cloudy);
                float3 cDay = lerp(baseDay, float3(1.0, 1.0, 1.0), lit) * lerp(1.0, 0.58, cloudy * 0.8 + rain * 0.2);
                float toward = pow(saturate(mu * 0.5 + 0.5), 4.0);
                cDay = lerp(cDay, cDay * float3(1.3, 0.72, 0.48), golden * (0.4 + 0.6 * toward));
                cDay *= 0.3 + 0.7 * sunVis;
                cDay += sunCol * pow(saturate(mu), 20.0) * 0.55 * (1.0 - dens * 0.6) * sunVis;
                float3 cNight = float3(0.03, 0.035, 0.05) * (0.7 + 0.6 * lit)
                              + _CityGlowColor.rgb * _CityGlow * 0.2 * exp(-max(up, 0.0) * 3.0);
                // round 2: cloud edges toward the sun light up pink-gold at golden hour
                cDay += float3(1.0, 0.5, 0.45) * golden * pow(az, 6.0) * (1.0 - dens) * 0.8;
                float3 cloudCol = lerp(cDay, cNight, night);
                col = col * (1.0 - ca) + cloudCol * ca;
                a = a * (1.0 - ca) + ca;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
