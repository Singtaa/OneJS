// GroundLit's blend of its layers, in both pipelines' SubShaders (ModelBridge.Ground.cs). Up to four
// layers, each a colour texture with its height in alpha, mixed by four weights a vertex that need
// not sum to 1. Plain math on what was sampled, so the URP and the built-in SubShaders share it.
#ifndef ONEJS_MODEL_SPLAT_INCLUDED
#define ONEJS_MODEL_SPLAT_INCLUDED

// The weights as given, normalised; all four at or below zero read as the first layer alone.
half4 SplatWeights(half4 weights) {
    weights = max(weights, 0);
    half sum = dot(weights, 1);
    return sum > 1e-4 ? weights / sum : half4(1, 0, 0, 0);
}

// How much of a triplanar layer each projection gives at a surface facing `normalWS`: from the
// side (x), from above (y) and from the front (z). Sharpened by the fourth power, so a face
// takes its nearest plane and blends only near 45 degrees; a plane under 0.01 is dropped
// and the rest renormalised. In float: at half precision FXC kept step's compare as an all-ones
// mask in a half register, which reaches WebGL 2 as a float holding a NaN's bits that a GPU may
// rewrite (ShaderWebGL2NaNTests).
float3 SplatPlanes(float3 normalWS) {
    float3 w = abs(normalize(normalWS));
    w *= w;
    w *= w;
    w /= dot(w, 1);
    w *= step(0.01, w);
    return w / dot(w, 1);
}

// The four layers mixed. With `heightBlend` 0 the weights mix them as they are. Above 0, a layer
// shows where its height (alpha) plus its weight comes within `heightBlend` of the highest, so
// stones stand out of the grass round them and the edge between two layers follows their
// heights rather than the vertex grid. Each share is still scaled by its weight, so a layer with
// none never shows. The alpha that comes back is 1: GroundLit's ground is opaque.
half3 SplatMix(half4 c0, half4 c1, half4 c2, half4 c3, half4 weights, half heightBlend) {
    half4 w = SplatWeights(weights);
    if (heightBlend > 0) {
        half4 h = half4(c0.a, c1.a, c2.a, c3.a) + w;
        half top = max(max(h.x, h.y), max(h.z, h.w));
        half4 share = w * max(h - top + heightBlend, 0);
        half sum = dot(share, 1);
        w = sum > 1e-4 ? share / sum : w;
    }
    return c0.rgb * w.x + c1.rgb * w.y + c2.rgb * w.z + c3.rgb * w.w;
}

// How much coarser a layer is read at `positionWS`, as a factor on its gradients (a triplanar
// layer's) or a mip bias of its log2 (a layer at the uvs). 1 nearer the camera than the fade
// distance; past it, the square of how many fade distances off the point is, so each doubling of
// the distance reads two mips coarser on top of the one the distance itself costs. Far enough
// off, a layer reads its smallest mip, the picture's mean colour, and its motif no longer repeats
// in a grid across a far slope. `perFade` is 1 over the fade distance in metres; 0 makes the
// product 0 and the factor 1, so no fade needs no branch.
float SplatFarScale(float3 positionWS, float3 cameraWS, float perFade) {
    float3 v = (positionWS - cameraWS) * perFade;
    return max(1, dot(v, v));
}

// A lattice point hashed to 0 to 1: Dave Hoskins's hash without sine, in float, since a sine of a
// large argument is not the same number on every GPU and half precision would band it.
float SplatHash(float2 p) {
    float3 p3 = frac(p.xyx * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// Value noise, 0 to 1, eased between lattice points a unit apart.
float SplatNoise(float2 p) {
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3 - 2 * f);
    return lerp(lerp(SplatHash(i), SplatHash(i + float2(1, 0)), u.x),
        lerp(SplatHash(i + float2(0, 1)), SplatHash(i + float2(1, 1)), u.x), u.y);
}

// The ground's brightness at `positionWS`, 1 give or take `variation`: two octaves of value
// noise laid across the world, about 40 m and 160 m across, so a stretch of ground and a far
// slope are never one even shade a repeat after another. The height shifts it too, so a cliff
// changes up its face as well as along it.
half SplatVariation(float3 positionWS, half variation) {
    float2 p = positionWS.xz + positionWS.y * 0.7;
    float n = SplatNoise(p * (1.0 / 40)) * 0.6 + SplatNoise(p * (1.0 / 160) + 17.3) * 0.4;
    return 1 + variation * (half)(n * 2 - 1);
}

#endif
