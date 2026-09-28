// GENERATED from lib/noise2d.hlsl in onejs-sl (github.com/Singtaa/onejs-sl).
// Do not edit this copy: change the source there and run `npm run lib`,
// which rewrites it. The web backends are translated from the same text.
#ifndef ONEJS_NOISE2D
#define ONEJS_NOISE2D

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

// MARK: hash
//
// The lattice hash is integer arithmetic, and has to stay integer arithmetic.
// A float hash, frac of a large product as most shader noise is written, is
// only as exact as the compiler lets it be: one that contracts a multiply and
// an add into a single rounding computes a different hash, and a different
// hash is a different picture. Dawn on Windows contracted frac(p * k + c), and
// Metal computes dot(p, q) as fma(p.y, q.y, p.x * q.x), so one program drew a
// different noise on each. 32 bit unsigned multiply, add, xor and shift wrap
// modulo 2^32 on every target (HLSL, Metal, GLSL ES 3.00 highp and WGSL), so
// nothing is left for a compiler to round. goldens/probes.hlsl holds both
// hashes to the bit, and src/lib/lib.test.ts refuses a frac in any of them.

/// pcg2d (Jarzynski and Olano, "Hash Functions for GPU Rendering", 2020):
/// two 32 bit words in, two well mixed words out.
uint2 onejsPcg2d(uint2 v)
{
    v = v * 1664525u + 1013904223u;
    v.x += v.y * 1664525u;
    v.y += v.x * 1664525u;
    v = v ^ (v >> 16u);
    v.x += v.y * 1664525u;
    v.y += v.x * 1664525u;
    v = v ^ (v >> 16u);
    return v;
}

/// A lattice cell and a seed as the two words the hash mixes.
///
/// The cell is a whole number already, the floor of a point. A float outside
/// int's range has no defined conversion in HLSL or GLSL, so it is clamped
/// first: every cell past -2^31 or 2^31 - 128 (the last float below 2^31) is
/// the edge cell. -0 is 0. A NaN coordinate gives an undefined cell, because
/// clamp returns either bound for a NaN depending on the backend.
///
/// The seed is its whole part, clamped the same way, and its fraction in
/// 65536ths, so seeds closer than 1/65536 draw the same field. Each is spread
/// by a large odd multiplier before it is added, so a seed moves the field far
/// across the lattice rather than one cell over.
uint2 onejsHashKey(float2 cell, float seed)
{
    float2 c = clamp(cell, float2(-2147483648.0, -2147483648.0), float2(2147483520.0, 2147483520.0));
    float s = clamp(seed, -2147483648.0, 2147483520.0);
    float whole = floor(s);
    uint2 key = uint2((uint)(int)whole, (uint)(int)((s - whole) * 65536.0));
    return uint2((uint)(int)c.x, (uint)(int)c.y) + key * uint2(2654435769u, 2246822507u);
}

/// A key's hash as 0..1: the top 24 bits, which a float holds exactly.
float onejsHashUnit(uint2 key)
{
    return (float)(onejsPcg2d(key).x >> 8u) * 5.9604644775390625e-8;
}

float onejsHash21(float2 p, float seed)
{
    return onejsHashUnit(onejsHashKey(p, seed));
}

float onejsVNoise(float2 p, float seed)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    // The neighbours are found in integers, so they stay distinct past 2^24,
    // where i + 1 as a float is i again.
    uint2 k = onejsHashKey(i, seed);
    float a = onejsHashUnit(k);
    float b = onejsHashUnit(k + uint2(1u, 0u));
    float c = onejsHashUnit(k + uint2(0u, 1u));
    float d = onejsHashUnit(k + uint2(1u, 1u));
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

// MARK: turbulence and ridged
//
// fBm above sums signed octaves, so octaves cancel as often as they add and the
// field reads as cloud. Turbulence sums the ABSOLUTE value of each octave: the
// zero crossings become creases, and the creases of every octave stack into
// the veins and licks that read as fire, smoke and marble. Ridged inverts the
// same crease so it is the bright line rather than the dark one, and squares
// it so the ridges stay sharp under the finer octaves. Both are built on
// simplex; a value noise crease follows the grid and shows as blocks.

/// 0..1. Perlin's turbulence: sum over octaves of |signed simplex|.
float onejsTurbulence(float2 p, float seed, int octaves, float lacunarity, float gain)
{
    float sum = 0, amp = 0.5, norm = 0;
    [unroll(4)]
    for (int o = 0; o < 4; o++)
    {
        if (o >= octaves) break;
        sum += abs(onejsSimplexRaw(p + (seed + o * 19.0) * 137.13)) * amp;
        norm += amp;
        p *= lacunarity;
        amp *= gain;
    }
    return sum / max(norm, 1e-4);
}

/// 0..1. Musgrave's ridged multifractal, without the feedback term: the
/// crease of |signed simplex| turned into a bright ridge and squared.
float onejsRidged(float2 p, float seed, int octaves, float lacunarity, float gain)
{
    float sum = 0, amp = 0.5, norm = 0;
    [unroll(4)]
    for (int o = 0; o < 4; o++)
    {
        if (o >= octaves) break;
        float r = 1.0 - abs(onejsSimplexRaw(p + (seed + o * 19.0) * 137.13));
        sum += r * r * amp;
        norm += amp;
        p *= lacunarity;
        amp *= gain;
    }
    return sum / max(norm, 1e-4);
}

/// Dispatches on the noise kind: 0 value fBm, 1 simplex fBm, 2 turbulence,
/// 3 ridged. The numbers are the contract with onejs-unity/src/fx/image.ts.
///
/// One return, at the end. Written with early returns, every path still
/// returns, but Unity's Metal compile warns of a potentially uninitialized
/// variable named after this function in every project that imports OneJS.
float onejsFbmKind(int kind, float2 p, float seed, int octaves, float lacunarity, float gain)
{
    float n;
    if (kind == 2) n = onejsTurbulence(p, seed, octaves, lacunarity, gain);
    else if (kind == 3) n = onejsRidged(p, seed, octaves, lacunarity, gain);
    else if (kind == 1) n = onejsFbmSimplex(p, seed, octaves, lacunarity, gain);
    else n = onejsFbm(p, seed, octaves, lacunarity, gain);
    return n;
}

#endif
