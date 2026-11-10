// The scene's sky (onejs-play's `scene.sky`) as a colour along a direction: what Sky.shader draws
// behind the world, and what ModelLit reflects once a cart asks (ModelBridge.SetSkyReflect). One
// source, so the reflection and the sky cannot drift apart. Plain HLSL with no pipeline library,
// since Sky.shader is written against UnityCG and ModelLit against URP.
#ifndef ONEJS_SKY_INCLUDED
#define ONEJS_SKY_INCLUDED

// The gradient from the horizon up to `top` and down to `bottom`. The square root keeps the
// horizon's colour to a band, as a real sky's haze is.
half3 OneJSSkyGradient(float3 d, half3 top, half3 horizon, half3 bottom) {
    float y = d.y;
    return y >= 0
        ? lerp(horizon, top, sqrt(saturate(y)))
        : lerp(horizon, bottom, sqrt(saturate(-y)));
}

// The sun's glow at `angle` radians from its centre, reaching `radius` radians out, 0 to 0.5.
half OneJSSkyGlow(float angle, float radius) {
    return radius > 0 ? pow(saturate(1.0 - angle / radius), 3.0) * 0.5 : 0;
}

// The sky as a surface of `perceptualRoughness` reflects it along `r`: the gradient, softened
// toward each half's average as the surface roughens, and the sun's glow, widened by the
// roughness and dimmed to keep its light. Not the sun's disc, which the sun's own highlight
// already is, and not the stars, which would shimmer on a curved mirror. The fog is the caller's.
half3 OneJSSkyReflected(float3 r, half perceptualRoughness, half3 top, half3 horizon, half3 bottom,
                        half3 sunColor, float4 sunDir, float glowRadius) {
    half3 sharp = OneJSSkyGradient(r, top, horizon, bottom);
    // The square root's average over a half is two thirds of the way from the horizon.
    half3 up = lerp(horizon, top, 2.0 / 3.0);
    half3 down = lerp(horizon, bottom, 2.0 / 3.0);
    half3 col = lerp(sharp, lerp(down, up, smoothstep(-0.5, 0.5, r.y)), perceptualRoughness);
    if (sunDir.w > 0 && glowRadius > 0) {
        float angle = acos(clamp(dot(r, normalize(sunDir.xyz)), -1.0, 1.0));
        float wide = glowRadius + perceptualRoughness * 0.6;
        half glow = OneJSSkyGlow(angle, wide) * (glowRadius * glowRadius) / (wide * wide);
        col = lerp(col, sunColor, saturate(glow));
    }
    return col;
}

#endif
