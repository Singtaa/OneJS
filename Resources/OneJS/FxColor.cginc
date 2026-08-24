// Colour adjustment and the Photoshop blend modes, for OneJS/FxOps.
//
// Split out of the shader so the fused op loop stays readable next to a
// twenty seven case blend switch.
//
// Blend modes operate on RGB and leave alpha to the caller. The separable ones
// are per channel; Hue, Saturation, Color and Luminosity are not, and use the
// non separable formulation from the PDF blend spec (SetLum / SetSat) rather
// than an HSV round trip, which is what makes them agree with Photoshop.
#ifndef ONEJS_FXCOLOR
#define ONEJS_FXCOLOR

// Rec. 709, the same weights UI Toolkit and Unity use elsewhere.
float onejsLuma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

float3 onejsSaturation(float3 c, float amount)
{
    return lerp(onejsLuma(c).xxx, c, amount);
}

float3 onejsContrast(float3 c, float amount)
{
    // Pivot around mid grey so contrast does not also change brightness.
    return (c - 0.5) * amount + 0.5;
}

float3 onejsHueShift(float3 c, float radians)
{
    // Rotation about the grey axis in YIQ. Cheaper than an RGB to HSV round
    // trip and it does not have HSV's discontinuity at the hue wrap.
    float s, co;
    sincos(radians, s, co);
    float3x3 toYIQ = float3x3(0.299, 0.587, 0.114,
                              0.596, -0.274, -0.322,
                              0.211, -0.523, 0.312);
    float3x3 toRGB = float3x3(1.0, 0.956, 0.621,
                              1.0, -0.272, -0.647,
                              1.0, -1.107, 1.705);
    float3 yiq = mul(toYIQ, c);
    float2 rot = float2(yiq.y * co - yiq.z * s, yiq.y * s + yiq.z * co);
    return mul(toRGB, float3(yiq.x, rot));
}

float3 onejsLevels(float3 c, float inBlack, float inWhite, float gamma, float outBlack, float outWhite)
{
    float3 t = saturate((c - inBlack) / max(inWhite - inBlack, 1e-6));
    t = pow(t, 1.0 / max(gamma, 1e-6));
    return outBlack + t * (outWhite - outBlack);
}

// MARK: blend helpers

float onejsBurn(float b, float s) { return s <= 0.0 ? 0.0 : 1.0 - min(1.0, (1.0 - b) / s); }
float onejsDodge(float b, float s) { return s >= 1.0 ? 1.0 : min(1.0, b / (1.0 - s)); }

float onejsSoftLight1(float b, float s)
{
    // The PDF spec's D(b), not the cheap two branch approximation: the cheap one
    // has a visible kink at s = 0.5 on smooth gradients.
    float d = b <= 0.25 ? ((16.0 * b - 12.0) * b + 4.0) * b : sqrt(b);
    return s <= 0.5 ? b - (1.0 - 2.0 * s) * b * (1.0 - b)
                    : b + (2.0 * s - 1.0) * (d - b);
}

float onejsVividLight1(float b, float s)
{
    return s <= 0.5 ? onejsBurn(b, 2.0 * s) : onejsDodge(b, 2.0 * (s - 0.5));
}

float onejsPinLight1(float b, float s)
{
    return s <= 0.5 ? min(b, 2.0 * s) : max(b, 2.0 * (s - 0.5));
}

// Its own function because overlay is hard light with the operands swapped, and
// HLSL has no recursion, so onejsBlend cannot just call itself for that case.
float3 onejsHardLight3(float3 b, float3 s)
{
    float3 mul = 2.0 * b * s;
    float3 scr = 1.0 - 2.0 * (1.0 - b) * (1.0 - s);
    return float3(s.r <= 0.5 ? mul.r : scr.r,
                  s.g <= 0.5 ? mul.g : scr.g,
                  s.b <= 0.5 ? mul.b : scr.b);
}

// MARK: non separable helpers, from the PDF blend spec

float3 onejsSetLum(float3 c, float l)
{
    c += l - onejsLuma(c);
    // Clip back into gamut by pulling toward the luminance, which is what keeps
    // an out of range result from simply saturating to a different hue.
    float lum = onejsLuma(c);
    float mn = min(c.r, min(c.g, c.b));
    float mx = max(c.r, max(c.g, c.b));
    if (mn < 0.0) c = lum + (c - lum) * lum / max(lum - mn, 1e-6);
    if (mx > 1.0) c = lum + (c - lum) * (1.0 - lum) / max(mx - lum, 1e-6);
    return c;
}

float onejsSat(float3 c) { return max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b)); }

float3 onejsSetSat(float3 c, float s)
{
    float mn = min(c.r, min(c.g, c.b));
    float mx = max(c.r, max(c.g, c.b));
    float range = mx - mn;
    // A flat colour has no ordering to preserve, so it stays flat.
    return range > 1e-6 ? (c - mn) * s / range : float3(0, 0, 0);
}

/// Blend `s` (source, the operand) over `b` (backdrop, the chain so far).
/// Mode ids match BLEND in onejs-unity/src/fx/ops.ts.
float3 onejsBlend(int mode, float3 b, float3 s, float2 uv)
{
    if (mode == 0) return s;                                        // normal
    if (mode == 1)                                                  // dissolve
    {
        float r = frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
        return r < 0.5 ? s : b;
    }
    if (mode == 2) return min(b, s);                                // darken
    if (mode == 3) return b * s;                                    // multiply
    if (mode == 4) return float3(onejsBurn(b.r, s.r), onejsBurn(b.g, s.g), onejsBurn(b.b, s.b));
    if (mode == 5) return max(b + s - 1.0, 0.0);                    // linear burn
    if (mode == 6) return onejsLuma(s) < onejsLuma(b) ? s : b;      // darker colour
    if (mode == 7) return max(b, s);                                // lighten
    if (mode == 8) return b + s - b * s;                            // screen
    if (mode == 9) return float3(onejsDodge(b.r, s.r), onejsDodge(b.g, s.g), onejsDodge(b.b, s.b));
    if (mode == 10) return min(b + s, 1.0);                         // linear dodge
    if (mode == 11) return onejsLuma(s) > onejsLuma(b) ? s : b;     // lighter colour
    if (mode == 12) return onejsHardLight3(s, b);                   // overlay
    if (mode == 13) return onejsHardLight3(b, s);                   // hard light
    if (mode == 14) return float3(onejsSoftLight1(b.r, s.r), onejsSoftLight1(b.g, s.g), onejsSoftLight1(b.b, s.b));
    if (mode == 15) return float3(onejsVividLight1(b.r, s.r), onejsVividLight1(b.g, s.g), onejsVividLight1(b.b, s.b));
    if (mode == 16) return saturate(b + 2.0 * s - 1.0);             // linear light
    if (mode == 17) return float3(onejsPinLight1(b.r, s.r), onejsPinLight1(b.g, s.g), onejsPinLight1(b.b, s.b));
    if (mode == 18)                                                 // hard mix
    {
        float3 v = saturate(b + 2.0 * s - 1.0);
        return step(0.5, v);
    }
    if (mode == 19) return abs(b - s);                              // difference
    if (mode == 20) return b + s - 2.0 * b * s;                     // exclusion
    if (mode == 21) return max(b - s, 0.0);                         // subtract
    if (mode == 22) return s <= 0.0 ? 1.0 : saturate(b / max(s, 1e-6)); // divide
    if (mode == 23) return onejsSetLum(onejsSetSat(s, onejsSat(b)), onejsLuma(b)); // hue
    if (mode == 24) return onejsSetLum(onejsSetSat(b, onejsSat(s)), onejsLuma(b)); // saturation
    if (mode == 25) return onejsSetLum(s, onejsLuma(b));            // colour
    if (mode == 26) return onejsSetLum(b, onejsLuma(s));            // luminosity
    return s;
}

#endif
