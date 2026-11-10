#ifndef VC_COMMON_INCLUDED
#define VC_COMMON_INCLUDED

// Veyra global shader properties (cards F2/F3). Set every frame by VeyraGlobals (World/VeyraGlobals.cs, driven by
// WorldEnvironment) and read by every Veyra/* shader; no shader keyword is toggled at runtime (card F1).
// Unset globals read as 0: noon-like (day), dry, no rain, no wind.
float _VC_TimeOfDay;      // 0–24 h
float _VC_NightFactor;    // 0 = day … 1 = night
float _VC_Wetness;        // 0 = dry … 1 = soaked
float _VC_RainIntensity;  // 0 … 1
float _VC_WindStrength;   // 0 … 1
float4 _VC_WindDirection; // xyz world direction (normalised)
float4 _VC_SunDirection;  // xyz direction towards the sun

float VC_Hash12(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float2 VC_Hash22(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float VC_ValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = VC_Hash12(i);
    float b = VC_Hash12(i + float2(1, 0));
    float c = VC_Hash12(i + float2(0, 1));
    float d = VC_Hash12(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// Fractal value noise in [0, 1].
float VC_Fbm(float2 p, int octaves)
{
    float sum = 0.0, amp = 0.5, norm = 0.0;
    [unroll(5)]
    for (int o = 0; o < octaves; o++)
    {
        sum += VC_ValueNoise(p) * amp;
        norm += amp;
        amp *= 0.5;
        p = p * 2.03 + 17.17;
    }
    return sum / max(norm, 1e-4);
}

// Animated rain-drop rings: a tangent-space normal offset (xy) at world position p (metres), time t (seconds).
float2 VC_Ripples(float2 p, float t)
{
    float2 cell = floor(p);
    float2 f = frac(p);
    float2 n = 0;
    [unroll]
    for (int j = -1; j <= 1; j++)
    {
        [unroll]
        for (int i = -1; i <= 1; i++)
        {
            float2 c = cell + float2(i, j);
            float2 h = VC_Hash22(c);
            float phase = frac(t * 0.85 + h.x);
            float2 d = f - (float2(i, j) + h);
            float r = length(d);
            float ring = r - phase * 0.9;
            float wave = sin(ring * 42.0) * exp(-ring * ring * 140.0) * (1.0 - phase);
            n += (d / max(r, 1e-3)) * wave;
        }
    }
    return n * 0.35;
}

#endif
