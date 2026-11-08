// What every pass of ModelLit's and GroundLit's URP SubShaders shares: the material's textures and
// values, the per-actor values, the baked light and the clip. GroundLit defines MODEL_SPLAT before
// including it, which adds the splat's layers (ModelSplat.hlsl). A define rather than a keyword, so
// neither shader has a variant for it.
#ifndef ONEJS_MODEL_LIT_URP_INCLUDED
#define ONEJS_MODEL_LIT_URP_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "ModelDissolve.hlsl"
#include "ModelLightmap.hlsl"

TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
#ifdef MODEL_SPLAT
    TEXTURE2D(_SplatLayer0); SAMPLER(sampler_SplatLayer0);
    TEXTURE2D(_SplatLayer1); SAMPLER(sampler_SplatLayer1);
    TEXTURE2D(_SplatLayer2); SAMPLER(sampler_SplatLayer2);
    TEXTURE2D(_SplatLayer3); SAMPLER(sampler_SplatLayer3);
#else
    TEXTURE2D(_MetallicRoughnessMap); SAMPLER(sampler_MetallicRoughnessMap);
    TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
    TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
    TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
#endif

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _Metallic;
    half _Roughness;
    half _NormalScale;
    half _OcclusionStrength;
    half4 _EmissionColor;
    half _Cutoff;
    half _AlphaClip;
    half _Unlit;
    half _Dissolve;
    half _DissolveScale;
    half4 _DissolveEdge;
    half _ReceiveShadows;
    half _Surface;
    #ifdef MODEL_SPLAT
        half _HeightBlend;
    #endif
CBUFFER_END

// What one actor has of its own, set by ModelBridge only on an actor that needs it: a
// renderer with a property block leaves URP's SRP Batcher, so every other actor has none,
// and 0 (whole, and shadowed) is what an unset value reads. Per instance where the
// material instances, so the actors that do carry them still draw together.
#if defined(UNITY_INSTANCING_ENABLED)
    UNITY_INSTANCING_BUFFER_START(ModelActor)
        UNITY_DEFINE_INSTANCED_PROP(float, _ActorDissolve)
        UNITY_DEFINE_INSTANCED_PROP(float, _ActorShadowless)
        UNITY_DEFINE_INSTANCED_PROP(float4, _ActorLightmapST)
    UNITY_INSTANCING_BUFFER_END(ModelActor)
    #define MODEL_ACTOR_DISSOLVE UNITY_ACCESS_INSTANCED_PROP(ModelActor, _ActorDissolve)
    #define MODEL_ACTOR_SHADOWLESS UNITY_ACCESS_INSTANCED_PROP(ModelActor, _ActorShadowless)
    #define MODEL_ACTOR_LIGHTMAP_ST UNITY_ACCESS_INSTANCED_PROP(ModelActor, _ActorLightmapST)
#else
    float _ActorDissolve;
    float _ActorShadowless;
    float4 _ActorLightmapST;
    #define MODEL_ACTOR_DISSOLVE _ActorDissolve
    #define MODEL_ACTOR_SHADOWLESS _ActorShadowless
    #define MODEL_ACTOR_LIGHTMAP_ST _ActorLightmapST
#endif

// The scene's baked light (ModelBridge.Light.cs): light arriving at a static surface, sky and
// bounce without the sun's first hit, in the units SampleSH gives, a slice a page. An actor
// with a rectangle of it (_ActorLightmapST's scale above 0) reads it at its TEXCOORD1 in
// place of the probe. A branch on a per-actor value rather than a keyword, since one material
// draws baked and moving actors alike, and a run-time keyword would be stripped from a build.
TEXTURE2D_ARRAY(_OjLightmap); SAMPLER(sampler_OjLightmap);

// The baked light at a uv on the bake's sheet (ModelLightmap.hlsl).
half3 BakedLight(float2 at) {
    float3 p = ModelLightmapPage(at);
    return SAMPLE_TEXTURE2D_ARRAY(_OjLightmap, sampler_OjLightmap, p.xy, p.z).rgb;
}

// The material's dissolve or the actor's, whichever is further gone.
float Dissolved() {
    return max(_Dissolve, MODEL_ACTOR_DISSOLVE);
}

float ClipAt(float3 positionOS, float2 uv) {
    float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a * _BaseColor.a;
    return ModelClip(positionOS, alpha, _AlphaClip, _Cutoff, Dissolved(), _DissolveScale);
}

#endif
