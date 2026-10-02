// The dissolve every ModelLit pass shares, so a model vanishes the same way in its colour, its
// shadow and its depth. _Dissolve is 0 for whole and 1 for gone; the noise is in object space, so
// the pattern rides along with an animated mesh instead of swimming over it.
#ifndef ONEJS_MODEL_DISSOLVE_INCLUDED
#define ONEJS_MODEL_DISSOLVE_INCLUDED

float ModelHash(float3 p) {
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float ModelNoise(float3 p) {
    float3 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(
        lerp(lerp(ModelHash(i), ModelHash(i + float3(1, 0, 0)), f.x),
             lerp(ModelHash(i + float3(0, 1, 0)), ModelHash(i + float3(1, 1, 0)), f.x), f.y),
        lerp(lerp(ModelHash(i + float3(0, 0, 1)), ModelHash(i + float3(1, 0, 1)), f.x),
             lerp(ModelHash(i + float3(0, 1, 1)), ModelHash(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

// Discards what the alpha mask and the dissolve remove, and returns how far the fragment is from
// the dissolve's edge (large when there is no dissolve at all).
float ModelClip(float3 positionOS, float alpha, float alphaClip, float cutoff, float dissolve, float scale) {
    if (alphaClip > 0.5) clip(alpha - cutoff);
    if (dissolve <= 0.0) return 1.0;
    float n = ModelNoise(positionOS * scale) * 0.65 + ModelNoise(positionOS * scale * 2.3) * 0.35;
    float cut = n - dissolve * 1.08;
    clip(cut);
    return cut;
}

// The glow along the dissolve's edge, added as emission.
float3 ModelEdge(float cut, float3 edgeColor) {
    return edgeColor * (1.0 - smoothstep(0.0, 0.06, cut));
}

#endif
