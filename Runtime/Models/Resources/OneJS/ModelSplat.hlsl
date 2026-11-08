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
// and the rest renormalised, so the shader skips its read (ModelLitForward.hlsl).
half3 SplatPlanes(float3 normalWS) {
    half3 w = (half3)abs(normalize(normalWS));
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

#endif
