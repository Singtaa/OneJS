// A mesh model's surface options that read more than its own material (onejs-play's `scene.mesh({
// soften, deepColor, normalMap })`), as functions only, for URP's lit pass (ModelLitForward.hlsl), which reads
// the depth behind the pixel and works out a frame for a mesh's normal map. The built-in pipeline's
// surface shader has none: there Vulkan's compiler refused its forward pass once Surf read the
// depth fade's values, and ModelBridge refuses them.
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

// Where a mesh model's normal map is read: its own repeats of the mesh's uvs (`baseUV` is the base
// map's, undone by `baseST`), slid by `flow` uvs a second for `seconds` of the scene's clock. The
// slide is taken modulo one repeat, so an hour's flow keeps the texel a float can hold.
float2 ModelNormalUV(float2 baseUV, float4 baseST, float4 normalST, float2 flow, float seconds) {
    float2 uv = (baseUV - baseST.zw) / baseST.xy;
    return uv * normalST.xy + frac(flow * seconds * normalST.xy);
}

// A tangent frame worked out per pixel from how the world position and the uvs change across the
// screen (the cotangent frame), for a surface without tangents: about 15 ALU. Flat across a triangle,
// which a normal map's own detail hides. Rows are tangent, bitangent and normal, for
// TransformTangentToWorld. Never `GetWorldToObjectMatrix`: a scattered copy may not carry it.
half3x3 ModelCotangentFrame(float3 normalWS, float3 positionWS, float2 uv) {
    float3 dp1 = ddx(positionWS), dp2 = ddy(positionWS);
    float2 duv1 = ddx(uv), duv2 = ddy(uv);
    float3 dp2perp = cross(dp2, normalWS), dp1perp = cross(normalWS, dp1);
    float3 t = dp2perp * duv1.x + dp1perp * duv2.x;
    float3 b = dp2perp * duv1.y + dp1perp * duv2.y;
    // Scaled to unit length, keeping the sign of the system's determinant, which the scale alone
    // drops: a render target drawn upside down, or uvs that run mirrored, flip it.
    float det = dot(dp1, dp2perp);
    float s = rsqrt(max(max(dot(t, t), dot(b, b)), 1e-20)) * (det < 0 ? -1.0 : 1.0);
    return half3x3(t * s, b * s, normalWS);
}

// Water's second ripple layer (onejs-play's `water`): the same normal map read again 2.37 times as
// finely, sliding across the first's flow at 1.3 times its speed, so the two never line up and the
// surface never shows one picture's repeat. Arguments as ModelNormalUV's, `flow` the first's slide.
float2 ModelRipplesUV(float2 baseUV, float4 baseST, float4 normalST, float2 flow, float seconds) {
    float2 across = float2(flow.y, -flow.x) * 1.3;
    float4 finer = float4(normalST.xy * 2.37, normalST.zw);
    return ModelNormalUV(baseUV, baseST, finer, across, seconds) + 0.37;
}

// Two tangent-space normals as one (whiteout blending): each layer's slope adds, and neither
// flattens the other.
half3 ModelRipplesBlend(half3 a, half3 b) {
    return normalize(half3(a.xy + b.xy, a.z * b.z));
}

// How much of water's foam shows where `thickness` metres of it lie in front of the world, with
// `invMetres` 1 over the foam's metres: whole at the shore, gone by the foam's metres out, its edge
// broken up by the ripples' slope (`ripple`, the tangent-space normal's xy) as painted foam is.
half ModelFoam(float thickness, float invMetres, half2 ripple) {
    float edge = 1.0 - thickness * invMetres + (ripple.x + ripple.y) * 0.6;
    return (half)smoothstep(0.35, 0.55, edge);
}

// Water's colour once lit: `c` moved toward `sky`, what its surface reflects, by Schlick's fresnel
// for water (2% face on, all of it edge on) along the normal its ripples and swell bent, and the
// reflection kept out of its foam. A see-through surface's alpha rises with the reflection, which is
// as solid as the sky it shows; the colour is divided back so the blend shows each in its share.
half4 ModelWaterReflect(half4 c, half3 normalWS, float3 viewWS, half3 sky, half foam, bool seeThrough) {
    half f = (half)(0.02 + 0.98 * pow(1.0 - saturate(dot(normalWS, viewWS)), 5.0)) * (1.0 - foam);
    if (!seeThrough) return half4(lerp(c.rgb, sky, f), c.a);
    half a = lerp(c.a, (half)1.0, f);
    return half4((c.rgb * c.a * (1.0 - f) + sky * f) / max(a, (half)1e-3), a);
}

#endif
