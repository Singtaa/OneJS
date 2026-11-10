// A scatter's draw range (onejs-play's `scene.scatter(model, copies, { from, to, fade })`,
// ModelBridge.Scatter.cs): each copy draws only while the camera is between `from` and `to` of its
// foot, and across `fade` metres centred on either edge it is dithered in or out. Near an edge the
// two sides use complementary patterns, so a near mesh with `to: 140` and a far one with `from: 140`
// share every pixel between them exactly once, with no sorting and nothing drawn twice.
//
// The distance is a copy's, not a pixel's, taken in the vertex stage, so a copy fades as one. A
// shadow pass cuts at the edges without dithering, so each copy of a pair casts once.
#ifndef ONEJS_MODEL_RANGE_INCLUDED
#define ONEJS_MODEL_RANGE_INCLUDED

// From the scatter's draw: from, to, half the fade, and 1 (0 for everything else, which then
// draws whole). From and to arrive far past any camera where a scatter has no such edge.
float4 _ScatterRange;
// The camera a scatter is drawn for, which a shadow pass still measures from (ModelBridge.Scatter.cs).
float4 _OjEye;
// 1 for a scatter whose copies keep no shadow on themselves (`receiveShadows: false`), from its
// draw; 0, unset, for everything else.
float _ScatterShadowless;

// How far a copy at `origin` is into its range, at the near edge (x) and the far (y): 1 inside,
// 0 outside, and between across the fade.
float2 ModelRange(float3 origin) {
    float d = distance(origin, _OjEye.xyz);
    float h = max(_ScatterRange.z, 1e-4);
    float2 inside = saturate(float2(d - _ScatterRange.x + h, _ScatterRange.y + h - d) / (2.0 * h));
    return lerp(float2(1, 1), inside, _ScatterRange.w);
}

// Discards a pixel the range dithers away. `pixel` is its place on the screen in pixels.
void ModelRangeClip(float2 range, float2 pixel) {
    // Interleaved gradient noise, kept off 0 and 1 so a copy wholly inside keeps every pixel.
    float n = 0.002 + 0.996 * frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
    clip(range.y - n);
    clip(range.x - (1.0 - n));
}

// 1 for a copy with pixels left to draw, 0 for one its range drops whole (a shadow pass's
// `ModelRangeShadowKeep` at its hard edge). A vertex is collapsed onto its copy's origin by it, so a
// dropped copy's triangles have no area and cost nothing past its vertices: a chunk is culled whole
// only when all of it is out of range, and a near and a far scatter mostly share their chunks.
// Arithmetic rather than a compare, so no mask reaches WebGL 2 in a float.
float ModelRangeKeep(float2 range) {
    return saturate(min(range.x, range.y) * 1e6);
}

float ModelRangeShadowKeep(float2 range) {
    return saturate((min(range.x, range.y) - 0.5) * 1e6 + 1.0);
}

// A shadow pass's cut: whole inside the edges, nothing outside.
void ModelRangeShadowClip(float2 range) {
    clip(min(range.x, range.y) - 0.5);
}

#endif
