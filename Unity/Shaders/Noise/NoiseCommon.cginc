// NoiseCommon.cginc
// Shared noise functions for procedural generation compute shaders.

#ifndef NOISE_COMMON_INCLUDED
#define NOISE_COMMON_INCLUDED

// =============================================================================
// Hash Functions
// =============================================================================

// PCG hash for high-quality randomness
uint pcg_hash(uint input)
{
    uint state = input * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

// Hash 2D coordinates to float [0, 1]
float hash21(float2 p)
{
    uint h = pcg_hash(asuint(p.x) + pcg_hash(asuint(p.y)));
    return float(h) / 4294967295.0;
}

// Hash 3D coordinates to float [0, 1]
float hash31(float3 p)
{
    uint h = pcg_hash(asuint(p.x) + pcg_hash(asuint(p.y) + pcg_hash(asuint(p.z))));
    return float(h) / 4294967295.0;
}

// Hash 2D to 2D
float2 hash22(float2 p)
{
    uint h1 = pcg_hash(asuint(p.x) + pcg_hash(asuint(p.y)));
    uint h2 = pcg_hash(h1);
    return float2(h1, h2) / 4294967295.0;
}

// Hash 3D to 3D
float3 hash33(float3 p)
{
    uint h1 = pcg_hash(asuint(p.x) + pcg_hash(asuint(p.y) + pcg_hash(asuint(p.z))));
    uint h2 = pcg_hash(h1);
    uint h3 = pcg_hash(h2);
    return float3(h1, h2, h3) / 4294967295.0;
}

// =============================================================================
// Interpolation
// =============================================================================

// Quintic smoothstep for Perlin noise
float fade(float t)
{
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

float2 fade2(float2 t)
{
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

float3 fade3(float3 t)
{
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

// =============================================================================
// Gradient Functions
// =============================================================================

// 2D gradient from hash
float2 gradient2D(float2 p)
{
    float h = hash21(p) * 6.28318530718;
    return float2(cos(h), sin(h));
}

// 3D gradient from hash (12 directions)
float3 gradient3D(float3 p)
{
    uint h = pcg_hash(asuint(p.x) + pcg_hash(asuint(p.y) + pcg_hash(asuint(p.z))));
    h = h % 12u;

    // 12 gradient directions
    const float3 grads[12] = {
        float3(1, 1, 0), float3(-1, 1, 0), float3(1, -1, 0), float3(-1, -1, 0),
        float3(1, 0, 1), float3(-1, 0, 1), float3(1, 0, -1), float3(-1, 0, -1),
        float3(0, 1, 1), float3(0, -1, 1), float3(0, 1, -1), float3(0, -1, -1)
    };

    return grads[h];
}

// =============================================================================
// Perlin Noise
// =============================================================================

float perlin2D(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = fade2(f);

    // Gradient dots at corners
    float n00 = dot(gradient2D(i + float2(0, 0)), f - float2(0, 0));
    float n10 = dot(gradient2D(i + float2(1, 0)), f - float2(1, 0));
    float n01 = dot(gradient2D(i + float2(0, 1)), f - float2(0, 1));
    float n11 = dot(gradient2D(i + float2(1, 1)), f - float2(1, 1));

    // Bilinear interpolation
    float nx0 = lerp(n00, n10, u.x);
    float nx1 = lerp(n01, n11, u.x);
    return lerp(nx0, nx1, u.y);
}

float perlin3D(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = fade3(f);

    // Gradient dots at 8 corners
    float n000 = dot(gradient3D(i + float3(0, 0, 0)), f - float3(0, 0, 0));
    float n100 = dot(gradient3D(i + float3(1, 0, 0)), f - float3(1, 0, 0));
    float n010 = dot(gradient3D(i + float3(0, 1, 0)), f - float3(0, 1, 0));
    float n110 = dot(gradient3D(i + float3(1, 1, 0)), f - float3(1, 1, 0));
    float n001 = dot(gradient3D(i + float3(0, 0, 1)), f - float3(0, 0, 1));
    float n101 = dot(gradient3D(i + float3(1, 0, 1)), f - float3(1, 0, 1));
    float n011 = dot(gradient3D(i + float3(0, 1, 1)), f - float3(0, 1, 1));
    float n111 = dot(gradient3D(i + float3(1, 1, 1)), f - float3(1, 1, 1));

    // Trilinear interpolation
    float nx00 = lerp(n000, n100, u.x);
    float nx01 = lerp(n001, n101, u.x);
    float nx10 = lerp(n010, n110, u.x);
    float nx11 = lerp(n011, n111, u.x);
    float nxy0 = lerp(nx00, nx10, u.y);
    float nxy1 = lerp(nx01, nx11, u.y);
    return lerp(nxy0, nxy1, u.z);
}

// =============================================================================
// Simplex Noise
// =============================================================================

// Simplex 2D constants
#define F2 0.3660254037844386
#define G2 0.21132486540518713

float simplex2D(float2 p)
{
    // Skew to simplex space
    float s = (p.x + p.y) * F2;
    float2 i = floor(p + s);

    // Unskew back
    float t = (i.x + i.y) * G2;
    float2 x0 = p - (i - t);

    // Determine which simplex
    float2 i1 = (x0.x > x0.y) ? float2(1, 0) : float2(0, 1);

    float2 x1 = x0 - i1 + G2;
    float2 x2 = x0 - 1.0 + 2.0 * G2;

    // Contributions from corners
    float n0 = 0, n1 = 0, n2 = 0;

    float t0 = 0.5 - dot(x0, x0);
    if (t0 >= 0)
    {
        t0 *= t0;
        n0 = t0 * t0 * dot(gradient2D(i), x0);
    }

    float t1 = 0.5 - dot(x1, x1);
    if (t1 >= 0)
    {
        t1 *= t1;
        n1 = t1 * t1 * dot(gradient2D(i + i1), x1);
    }

    float t2 = 0.5 - dot(x2, x2);
    if (t2 >= 0)
    {
        t2 *= t2;
        n2 = t2 * t2 * dot(gradient2D(i + 1.0), x2);
    }

    return 70.0 * (n0 + n1 + n2);
}

// Simplex 3D constants
#define F3 0.3333333333
#define G3 0.1666666667

float simplex3D(float3 p)
{
    // Skew
    float s = (p.x + p.y + p.z) * F3;
    float3 i = floor(p + s);

    // Unskew
    float t = (i.x + i.y + i.z) * G3;
    float3 x0 = p - (i - t);

    // Determine simplex
    float3 i1, i2;
    if (x0.x >= x0.y)
    {
        if (x0.y >= x0.z) { i1 = float3(1, 0, 0); i2 = float3(1, 1, 0); }
        else if (x0.x >= x0.z) { i1 = float3(1, 0, 0); i2 = float3(1, 0, 1); }
        else { i1 = float3(0, 0, 1); i2 = float3(1, 0, 1); }
    }
    else
    {
        if (x0.y < x0.z) { i1 = float3(0, 0, 1); i2 = float3(0, 1, 1); }
        else if (x0.x < x0.z) { i1 = float3(0, 1, 0); i2 = float3(0, 1, 1); }
        else { i1 = float3(0, 1, 0); i2 = float3(1, 1, 0); }
    }

    float3 x1 = x0 - i1 + G3;
    float3 x2 = x0 - i2 + 2.0 * G3;
    float3 x3 = x0 - 1.0 + 3.0 * G3;

    // Contributions
    float n0 = 0, n1 = 0, n2 = 0, n3 = 0;

    float t0 = 0.6 - dot(x0, x0);
    if (t0 >= 0) { t0 *= t0; n0 = t0 * t0 * dot(gradient3D(i), x0); }

    float t1 = 0.6 - dot(x1, x1);
    if (t1 >= 0) { t1 *= t1; n1 = t1 * t1 * dot(gradient3D(i + i1), x1); }

    float t2 = 0.6 - dot(x2, x2);
    if (t2 >= 0) { t2 *= t2; n2 = t2 * t2 * dot(gradient3D(i + i2), x2); }

    float t3 = 0.6 - dot(x3, x3);
    if (t3 >= 0) { t3 *= t3; n3 = t3 * t3 * dot(gradient3D(i + 1.0), x3); }

    return 32.0 * (n0 + n1 + n2 + n3);
}

// =============================================================================
// Value Noise
// =============================================================================

float value2D(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f); // smoothstep

    float a = hash21(i + float2(0, 0));
    float b = hash21(i + float2(1, 0));
    float c = hash21(i + float2(0, 1));
    float d = hash21(i + float2(1, 1));

    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float value3D(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * (3.0 - 2.0 * f);

    float a = hash31(i + float3(0, 0, 0));
    float b = hash31(i + float3(1, 0, 0));
    float c = hash31(i + float3(0, 1, 0));
    float d = hash31(i + float3(1, 1, 0));
    float e = hash31(i + float3(0, 0, 1));
    float f1 = hash31(i + float3(1, 0, 1));
    float g = hash31(i + float3(0, 1, 1));
    float h = hash31(i + float3(1, 1, 1));

    return lerp(
        lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y),
        lerp(lerp(e, f1, u.x), lerp(g, h, u.x), u.y),
        u.z
    );
}

// =============================================================================
// Worley (Cellular) Noise
// =============================================================================

float worley2D(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);

    float minDist = 1.0;

    // Check 3x3 neighborhood
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float2 neighbor = float2(x, y);
            float2 point = hash22(i + neighbor);
            float2 diff = neighbor + point - f;
            float dist = length(diff);
            minDist = min(minDist, dist);
        }
    }

    return minDist;
}

float worley3D(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);

    float minDist = 1.0;

    for (int z = -1; z <= 1; z++)
    {
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                float3 neighbor = float3(x, y, z);
                float3 point = hash33(i + neighbor);
                float3 diff = neighbor + point - f;
                float dist = length(diff);
                minDist = min(minDist, dist);
            }
        }
    }

    return minDist;
}

// =============================================================================
// FBM (Fractal Brownian Motion)
// =============================================================================

float fbm2D_perlin(float2 p, int octaves, float lacunarity, float persistence)
{
    float value = 0.0;
    float amplitude = 1.0;
    float frequency = 1.0;
    float maxValue = 0.0;

    for (int i = 0; i < octaves; i++)
    {
        value += perlin2D(p * frequency) * amplitude;
        maxValue += amplitude;
        amplitude *= persistence;
        frequency *= lacunarity;
    }

    return value / maxValue;
}

float fbm2D_simplex(float2 p, int octaves, float lacunarity, float persistence)
{
    float value = 0.0;
    float amplitude = 1.0;
    float frequency = 1.0;
    float maxValue = 0.0;

    for (int i = 0; i < octaves; i++)
    {
        value += simplex2D(p * frequency) * amplitude;
        maxValue += amplitude;
        amplitude *= persistence;
        frequency *= lacunarity;
    }

    return value / maxValue;
}

float fbm3D_perlin(float3 p, int octaves, float lacunarity, float persistence)
{
    float value = 0.0;
    float amplitude = 1.0;
    float frequency = 1.0;
    float maxValue = 0.0;

    for (int i = 0; i < octaves; i++)
    {
        value += perlin3D(p * frequency) * amplitude;
        maxValue += amplitude;
        amplitude *= persistence;
        frequency *= lacunarity;
    }

    return value / maxValue;
}

float fbm3D_simplex(float3 p, int octaves, float lacunarity, float persistence)
{
    float value = 0.0;
    float amplitude = 1.0;
    float frequency = 1.0;
    float maxValue = 0.0;

    for (int i = 0; i < octaves; i++)
    {
        value += simplex3D(p * frequency) * amplitude;
        maxValue += amplitude;
        amplitude *= persistence;
        frequency *= lacunarity;
    }

    return value / maxValue;
}

// =============================================================================
// Turbulence (Absolute FBM)
// =============================================================================

float turbulence2D(float2 p, int octaves, float lacunarity, float persistence)
{
    float value = 0.0;
    float amplitude = 1.0;
    float frequency = 1.0;
    float maxValue = 0.0;

    for (int i = 0; i < octaves; i++)
    {
        value += abs(perlin2D(p * frequency)) * amplitude;
        maxValue += amplitude;
        amplitude *= persistence;
        frequency *= lacunarity;
    }

    return value / maxValue;
}

float turbulence3D(float3 p, int octaves, float lacunarity, float persistence)
{
    float value = 0.0;
    float amplitude = 1.0;
    float frequency = 1.0;
    float maxValue = 0.0;

    for (int i = 0; i < octaves; i++)
    {
        value += abs(perlin3D(p * frequency)) * amplitude;
        maxValue += amplitude;
        amplitude *= persistence;
        frequency *= lacunarity;
    }

    return value / maxValue;
}

#endif // NOISE_COMMON_INCLUDED
