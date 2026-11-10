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

// A layered sway (onejs-play's `scene.mesh({ sway: { trunk, limb, leaf, phase } })`,
// ModelBridge.SetMeshSwayLayers), `s` a vertex's trunk, limb, leaf and phase. The wind the plain
// sway has, and three things it lacks:
// - the trunk bends rather than slides: a vertex keeps its distance from its copy's foot, so a
//   tree leaning downwind sinks a little at its crown instead of stretching;
// - a gust is a field over the ground blowing downwind, sampled at each copy's foot, so a gust
//   crosses a wood as a patch rather than as one swell along the wind;
// - a limb swings at its own quicker beat and a leaf flutters along its normal at a quicker one
//   still, a limb at its limb's phase and a leaf at its card's, so a branch's wood and the roots
//   of its cards move as one, and each card flutters whole, apart from the next.
// `s.w` holds both phases, in turns: the limb's in 256ths as its whole part, and the card's as
// its fraction (onejs-play's SwayLayers packs them), so the stream stays four wide.
float3 ModelSwayedLayered(float3 positionWS, float3 normalWS, float4x4 objectToWorld, float4 s) {
    float3 origin = float3(objectToWorld._m03, objectToWorld._m13, objectToWorld._m23);
    float scale = length(float3(objectToWorld._m01, objectToWorld._m11, objectToWorld._m21));
    float2 wind = _OjWind.xy;
    float3 way = float3(wind.x, 0.0, wind.y);
    float t = _OjWindClock.x;
    float along = dot(origin.xz, wind);
    float phase = along * 0.08 + ModelHash(origin * 0.13) * 6.2831853;
    // The gust field: value noise 30 m across, carried downwind at 4 m a second.
    float2 field = (origin.xz - wind * (t * 4.0)) / 30.0;
    float gust = 1.0 + _OjWind.w * (2.0 * ModelNoise(float3(field.x, 0.0, field.y)) - 1.0);
    float push = scale * _OjWind.z * gust;
    float limbTurns = floor(s.w);
    float limbOwn = limbTurns * (6.2831853 / 256.0);
    float leafOwn = (s.w - limbTurns) * 6.2831853;
    // The limb's swing, downwind with a little lift, and the leaf's flutter along its normal: the
    // same at every vertex of a limb, and of a card, but for how far each moves.
    float limb = sin(t * 2.1 + phase + limbOwn) + 0.4 * sin(t * 3.7 + 1.3 * limbOwn);
    float leaf = sin(t * 7.3 + phase + limbOwn + leafOwn) + 0.3 * sin(t * 11.9 + 2.0 * leafOwn);
    float3 moved = positionWS + (way + float3(0.0, 0.3, 0.0)) * (s.y * push * limb) + normalWS * (s.z * push * leaf);
    // The trunk's bend, as the plain sway's reach, then held to the vertex's distance from the foot.
    float swing = 0.7 * sin(t * 0.9 + phase) + 0.3 * sin(t * 2.3 + 1.7 * phase);
    float3 rel = moved - origin;
    float3 bent = rel + way * (s.x * push * (0.35 + 0.65 * swing));
    float reach = length(rel);
    return origin + (reach > 1e-5 ? bent * (reach / max(length(bent), 1e-5)) : bent);
}

#endif
