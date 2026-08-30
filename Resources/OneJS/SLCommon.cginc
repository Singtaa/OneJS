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

// The 42 signed distance shapes, shared with FxSources rather than rewritten.
// The MATHS is what has to match between backends, and it lives in one file.
#include "SDF2D.cginc"

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

/**
 * Signed distance to a shape, positive outside.
 *
 * The id table is SDF_SHAPES in onejs-unity/src/fx/sdf.ts, shared with the fx
 * pipeline so a shape means the same thing in both. A contract test compares
 * them, because this dispatcher is a second copy of the one in FxSources and a
 * copy is exactly the thing that drifts.
 *
 * The shader language passes four parameters where fx passes six. Position and
 * rotation are deliberately absent: a program transforms the point before
 * calling this, which is both how signed distance code is normally written and
 * more composable than baking a transform into every shape.
 */
float sl_sdfDistance(int id, float2 p, float4 a, float2 b)
    {
        switch (id)
        {
        case 0:  return sdCircle(p, a.x);
        case 1:  return sdRoundedBox(p, a.xy, float4(a.z, a.w, b.x, b.y));
        case 2:  return sdBox(p, a.xy);
        case 3:  return sdOrientedBox(p, a.xy, a.zw, b.x);
        case 4:  return sdSegment(p, a.xy, a.zw);
        case 5:  return sdRhombus(p, a.xy);
        case 6:  return sdTrapezoid(p, a.x, a.y, a.z);
        case 7:  return sdParallelogram(p, a.x, a.y, a.z);
        case 8:  return sdEquilateralTriangle(p, a.x);
        case 9:  return sdTriangleIsosceles(p, a.xy);
        case 10: return sdTriangle(p, a.xy, a.zw, b.xy);
        case 11: return sdUnevenCapsule(p, a.x, a.y, a.z);
        case 12: return sdPentagon(p, a.x);
        case 13: return sdHexagon(p, a.x);
        case 14: return sdOctogon(p, a.x);
        case 15: return sdHexagram(p, a.x);
        case 16: return sdStar5(p, a.x, a.y);
        case 17: return sdStar(p, a.x, (int)a.y, a.z);
        case 18: return sdPie(p, a.xy, a.z);
        case 19: return sdCutDisk(p, a.x, a.y);
        case 20: return sdArc(p, a.xy, a.z, a.w);
        case 21: return sdRing(p, a.xy, a.z, a.w);
        case 22: return sdHorseshoe(p, a.xy, a.z, float2(a.w, b.x));
        case 23: return sdVesica(p, a.x, a.y);
        case 24: return sdOrientedVesica(p, a.xy, a.zw, b.x);
        case 25: return sdMoon(p, a.x, a.y, a.z);
        case 26: return sdRoundedCross(p, a.x);
        case 27: return sdEgg(p, a.x, a.y);
        case 28: return sdHeart(p);
        case 29: return sdCross(p, a.xy, a.z);
        case 30: return sdRoundedX(p, a.x, a.y);
        case 31: return sdEllipse(p, a.xy);
        case 32: return sdParabola(p, a.x);
        case 33: return sdParabolaSegment(p, a.x, a.y);
        case 34: return sdBezier(p, a.xy, a.zw, b.xy);
        case 35: return sdBlobbyCross(p, a.x);
        case 36: return sdTunnel(p, a.xy);
        case 37: return sdStairs(p, a.xy, a.z);
        case 38: return sdQuadraticCircle(p);
        case 39: return sdHyberbola(p, a.x, a.y);
        case 40: return sdCoolS(p);
        case 41: return sdCircleWave(p, a.x, a.y);
        default: return 1e6;
        }
    }

float sl_voronoi(float2 p)
{
    // Distance to the nearest of a jittered lattice of points. Written here
    // rather than in either backend, for the same reason as the noise above.
    float2 cell = floor(p);
    float2 f = frac(p);
    float best = 8.0;
    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            float2 o = float2(x, y);
            float2 jitter = float2(sl_hash21(cell + o), sl_hash21(cell + o + 37.7));
            best = min(best, length(o + jitter - f));
        }
    }
    return best;
}

float sl_luminance(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

#endif
