// Helpers shared by BOTH shader language backends.
//
// The VM (OneJS/FxProgram.shader) includes this, and every shader generated
// from a program by the HLSL emitter includes it too. That sharing is the whole
// reason this file exists: the two backends have to agree, and the fastest way
// to make them disagree is to write `noise` twice.
//
// Anything an opcode needs that is more than one expression belongs here rather
// than in either backend. If it lives in only one, the golden image comparison
// fails on every program that touches it, and the failure looks like a bug in
// the program.
#ifndef ONEJS_SL_COMMON_INCLUDED
#define ONEJS_SL_COMMON_INCLUDED

float sl_hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float sl_valueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = sl_hash21(i);
    float b = sl_hash21(i + float2(1, 0));
    float c = sl_hash21(i + float2(0, 1));
    float d = sl_hash21(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// The simplex opcode is currently value noise on an offset, rotated lattice.
// It is NOT simplex noise and the name is a promise this does not yet keep;
// what matters for now is that both backends compute the same wrong thing
// rather than two different ones.
float sl_simplex(float2 p)
{
    return sl_valueNoise(p * 1.37 + 11.7);
}

float sl_fbm(float2 p, int octaves)
{
    float sum = 0, amp = 0.5, norm = 0;
    [unroll]
    for (int o = 0; o < 8; o++)
    {
        if (o >= octaves) break;
        sum += sl_valueNoise(p) * amp;
        norm += amp;
        p *= 2.0;
        amp *= 0.5;
    }
    return norm > 0 ? sum / norm : 0;
}

float3 sl_hsv2rgb(float3 c)
{
    float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
}

float sl_luminance(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

#endif
