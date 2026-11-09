// The scene's wind, moving a mesh model's vertices by how far each may sway (onejs-play's
// `scene.mesh({ sway })` and `scene.wind`), in every pass of ModelLit and GroundLit, so a tree's
// shadow and depth sway with it. In the vertex stage, for actors and scattered copies alike: a wood
// of 12,000 copies costs nothing on the CPU.
//
// A vertex moves along the wind by its sway (in the model's own units, so a copy scaled up sways
// as far again) times the wind's strength. It leans downwind and swings about the lean, at a phase
// taken from where its copy stands, so a wood does not sway in step, and a gust passes along the
// wind as a slow swell in the strength. The clock is the scene's (ModelBridge.SetWindClock), not
// Unity's, so a paused cart's trees stop and a frame-stepped recording sways the same every time.
#ifndef ONEJS_MODEL_WIND_INCLUDED
#define ONEJS_MODEL_WIND_INCLUDED

#include "ModelDissolve.hlsl"

// x and y the wind's direction over the ground (x and z, of length 1), z its strength, w its gust.
float4 _OjWind;
// The scene's clock in seconds, in x.
float4 _OjWindClock;

// Where a vertex at `positionWS` of a model drawn at `objectToWorld` is moved to by `sway`.
float3 ModelSwayed(float3 positionWS, float4x4 objectToWorld, float sway) {
    float3 origin = float3(objectToWorld._m03, objectToWorld._m13, objectToWorld._m23);
    float scale = length(float3(objectToWorld._m01, objectToWorld._m11, objectToWorld._m21));
    float2 wind = _OjWind.xy;
    float t = _OjWindClock.x;
    float along = dot(origin.xz, wind);
    float phase = along * 0.08 + ModelHash(origin * 0.13) * 6.2831853;
    float gust = 1.0 + _OjWind.w * sin(along * 0.045 - t * 0.7);
    float swing = 0.7 * sin(t * 0.9 + phase) + 0.3 * sin(t * 2.3 + 1.7 * phase);
    float reach = sway * scale * _OjWind.z * gust * (0.35 + 0.65 * swing);
    return positionWS + float3(wind.x, 0.0, wind.y) * reach;
}

#endif
