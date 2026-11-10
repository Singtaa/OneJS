// A mesh model's surface options that read more than its own material (onejs-play's `scene.mesh({
// soften, deepColor })`), as functions only, for URP's lit pass (ModelLitForward.hlsl), which reads
// the depth behind the pixel. The built-in pipeline's surface shader has none: there Vulkan's
// compiler refused its forward pass once Surf read the values, and ModelBridge refuses them.
// Every branch here is on a material value, so every pixel of a draw takes it the same way, and a
// mesh given neither option pays one compare. Never under MODEL_SPLAT.
#ifndef ONEJS_MODEL_SURFACE_INCLUDED
#define ONEJS_MODEL_SURFACE_INCLUDED

// The eye depth the camera's depth texture holds at a pixel, from its raw value: `zBuffer` is
// _ZBufferParams, `projection` _ProjectionParams and `ortho` unity_OrthoParams.w.
float ModelSceneEyeDepth(float raw, float4 zBuffer, float4 projection, float ortho) {
    if (ortho < 0.5) return 1.0 / (zBuffer.z * raw + zBuffer.w);
    #if defined(UNITY_REVERSED_Z)
        raw = 1.0 - raw;
    #endif
    return lerp(projection.y, projection.z, raw);
}

// How many metres of the mesh's volume the ray to this pixel crosses before it meets the opaque
// world behind: the eye depths' difference, along the ray rather than the view axis, so a grazing
// look through shallow water counts the long path it is. `rayLength` is the pixel's distance from
// the camera; an orthographic ray runs along the view axis.
float ModelThickness(float sceneEye, float pixelEye, float rayLength, float ortho) {
    float behind = max(sceneEye - pixelEye, 0.0);
    return ortho < 0.5 ? behind * rayLength / max(pixelEye, 1e-4) : behind;
}

// The base colour and alpha of a see-through mesh with `thickness` metres of it in front of the
// world (_DepthFade: x is 1 over `soften`'s metres, y is 3 over `deepColor`'s metres, each 0 for
// none). The colour and alpha move toward `deep` (_DeepColor, linear) as 1 - exp(-3 t / metres),
// 95% there at `metres`, and the alpha fades to nothing at the shore over `soften` metres. Before
// the surface is lit, so the deep colour takes the hour's light as the rest of the mesh does.
half4 ModelDepthFadeBy(half4 base, float thickness, float4 fade, half4 deep) {
    if (fade.y > 0) base = lerp(base, deep, (half)(1.0 - exp(-thickness * fade.y)));
    if (fade.x > 0) base.a *= (half)saturate(thickness * fade.x);
    return base;
}

#endif
