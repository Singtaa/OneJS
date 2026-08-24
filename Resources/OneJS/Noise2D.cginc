// Scrolling fBm value noise, shared by OneJS/TextureFX and OneJS/FxSources.
//
// Computed from a seed rather than sampled, so an effect needs no texture, any
// seed works without shipping art for it, and the field is infinite: scrolling
// never repeats and never has to tile.
//
// This lives in its own include because two shaders want it. Keeping a second
// copy in each would let them drift, and a noise field that differs by shader
// is the kind of thing nobody notices until two effects that should match do
// not.
#ifndef ONEJS_NOISE2D
#define ONEJS_NOISE2D

float onejsHash21(float2 p, float seed)
{
    p = frac(p * float2(123.34, 456.21) + seed * 0.1731);
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float onejsVNoise(float2 p, float seed)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = onejsHash21(i, seed);
    float b = onejsHash21(i + float2(1, 0), seed);
    float c = onejsHash21(i + float2(0, 1), seed);
    float d = onejsHash21(i + float2(1, 1), seed);
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// Octaves are capped at 4 and the loop is unrolled to that, because a dynamic
// trip count here costs more than the octaves it saves.
/// lacunarity is how much finer each octave gets, gain how much quieter.
/// The classic pair is 2 and 0.5; pushing lacunarity up and gain toward 1 gives
/// the stringy, turbulent look that reads as fire or smoke rather than cloud.
float onejsFbm(float2 p, float seed, int octaves, float lacunarity, float gain)
{
    float sum = 0, amp = 0.5, norm = 0;
    [unroll(4)]
    for (int o = 0; o < 4; o++)
    {
        if (o >= octaves) break;
        sum += onejsVNoise(p, seed + o * 19.0) * amp;
        norm += amp;
        p *= lacunarity;
        amp *= gain;
    }
    return sum / max(norm, 1e-4);
}

/// The classic parameters, for callers that do not care.
float onejsFbm(float2 p, float seed, int octaves)
{
    return onejsFbm(p, seed, octaves, 2.0, 0.5);
}

// MARK: simplex
//
// Ashima/McEwan 2D simplex. Worth carrying next to the value noise because the
// two fail differently: value noise interpolates a square grid, so at high
// octave gain its cells show through as blocks, which is exactly what spoiled
// the first pass at the fire sample. Simplex is built on triangles and has no
// axis-aligned structure to leak.

float2 onejsMod289(float2 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
float3 onejsMod289(float3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
float3 onejsPermute(float3 x) { return onejsMod289(((x * 34.0) + 1.0) * x); }

/// Raw simplex, roughly -1..1.
float onejsSimplexRaw(float2 v)
{
    const float4 C = float4(0.211324865405187, 0.366025403784439,
                            -0.577350269189626, 0.024390243902439);
    float2 i = floor(v + dot(v, C.yy));
    float2 x0 = v - i + dot(i, C.xx);
    float2 i1 = (x0.x > x0.y) ? float2(1.0, 0.0) : float2(0.0, 1.0);
    float4 x12 = x0.xyxy + C.xxzz;
    x12.xy -= i1;
    i = onejsMod289(i);
    float3 p = onejsPermute(onejsPermute(i.y + float3(0.0, i1.y, 1.0))
                            + i.x + float3(0.0, i1.x, 1.0));
    float3 m = max(0.5 - float3(dot(x0, x0), dot(x12.xy, x12.xy), dot(x12.zw, x12.zw)), 0.0);
    m = m * m; m = m * m;
    float3 x = 2.0 * frac(p * C.www) - 1.0;
    float3 h = abs(x) - 0.5;
    float3 ox = floor(x + 0.5);
    float3 a0 = x - ox;
    m *= 1.79284291400159 - 0.85373472095314 * (a0 * a0 + h * h);
    float3 g;
    g.x = a0.x * x0.x + h.x * x0.y;
    g.yz = a0.yz * x12.xz + h.yz * x12.yw;
    return 130.0 * dot(m, g);
}

/// 0..1, and seeded by displacing the input: simplex has no seed of its own.
float onejsSimplex(float2 p, float seed)
{
    return onejsSimplexRaw(p + seed * 137.13) * 0.5 + 0.5;
}

float onejsFbmSimplex(float2 p, float seed, int octaves, float lacunarity, float gain)
{
    float sum = 0, amp = 0.5, norm = 0;
    [unroll(4)]
    for (int o = 0; o < 4; o++)
    {
        if (o >= octaves) break;
        sum += onejsSimplex(p, seed + o * 19.0) * amp;
        norm += amp;
        p *= lacunarity;
        amp *= gain;
    }
    return sum / max(norm, 1e-4);
}

/// Dispatches on the noise kind: 0 value, 1 simplex.
float onejsFbmKind(int kind, float2 p, float seed, int octaves, float lacunarity, float gain)
{
    return kind == 1 ? onejsFbmSimplex(p, seed, octaves, lacunarity, gain)
                     : onejsFbm(p, seed, octaves, lacunarity, gain);
}

#endif
