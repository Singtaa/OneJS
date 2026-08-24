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

#endif
