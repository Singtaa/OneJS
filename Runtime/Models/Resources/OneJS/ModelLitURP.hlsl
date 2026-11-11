// What every pass of ModelLit's and GroundLit's URP SubShaders shares: the material's textures and
// values, the per-actor values, the baked light and the clip. GroundLit defines MODEL_SPLAT before
// including it, which adds the splat's layers (ModelSplat.hlsl). A define rather than a keyword, so
// neither shader has a variant for it.
#ifndef ONEJS_MODEL_LIT_URP_INCLUDED
#define ONEJS_MODEL_LIT_URP_INCLUDED

// A scattered copy (_SCATTER, on the materials ModelBridge.Scatter draws with) leaves out of its
// instanced draw every array it never reads, since Unity uploads each at its whole capacity on
// every draw of every pass: the inverse matrix (a copy scales alike on every axis, so its normals
// turn with its matrix), its own probe light (copies take the ambient, or their baked light), its
// renderer's layer and user value (a copy always keeps its shadows) and its dissolve. These are the
// defines `instancing_options assumeuniformscaling nolightprobe` and the lack of `renderinglayer`
// give, made here because a pragma cannot depend on a keyword.
#if defined(_SCATTER) && defined(INSTANCING_ON)
    #define MODEL_SCATTERED 1
    #define UNITY_ASSUME_UNIFORM_SCALING
    #undef UNITY_INSTANCED_SH
    #undef UNITY_INSTANCED_RENDERING_LAYER
#endif

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "ModelDissolve.hlsl"
#include "ModelLightmap.hlsl"
#include "ModelWind.hlsl"
#include "ModelRange.hlsl"
#include "ModelEdgeFade.hlsl"

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
    // The blend's destination factor: 1 (One) for an additive mesh model, which fog fades out
    // rather than toward the fog's colour, since adding the fog's colour would brighten the fog.
    half _DstBlend;
    // 1 where a mesh model's vertices carry how far each may sway (TEXCOORD3's x), 2 where they
    // carry a layered sway (all four: trunk, limb, leaf, phase), 0 elsewhere, so a model without
    // them never reads a stream it does not have.
    half _Sway;
    // 1 lights a double-sided face's back with its normal as given (a mesh model's `twoSided`, for
    // foliage cards whose normals bend out from a crown), 0 flips it, as a glTF file's back faces are.
    half _KeepBackNormal;
    #ifdef MODEL_SPLAT
        half _HeightBlend;
        // Each layer's repeats a metre where it is projected from the sides in world space
        // (`triplanar` in onejs-play), 0 where it is read at the mesh's uvs.
        float4 _SplatTriplanar;
        // x: 1 over the metres past which the layers read coarser mips (`fadeDistance`), 0 for
        // none; y: how far the ground's brightness varies across the world (`variation`).
        float4 _SplatFar;
    #endif
    // A see-through mesh model's fade by the depth behind it (ModelSurface.hlsl): x is 1 over
    // `soften`'s metres, y is 3 over `deepColor`'s metres, each 0 for none; and the deep colour,
    // linear. At the end, after the splat's, so neither shader's earlier layout moves.
    float4 _DepthFade;
    half4 _DeepColor;
    #ifndef MODEL_SPLAT
        // A mesh model's normal map (ModelSurface.hlsl): its repeats a uv, and in _NormalFlow its
        // flow in uvs a second (xy) and 1 in z where its frame is worked out per pixel, since a mesh
        // has no tangents; 0 everywhere else, which reads the map at the base map's uvs as before.
        float4 _NormalMap_ST;
        float4 _NormalFlow;
        // A mesh model's leaf light (ModelLeafLight.hlsl): how far the sun wraps round it in x, how
        // much shows through it from behind in y, and how tightly that gathers round the sun's line
        // in z; x and y 0 everywhere else, which adds nothing. The light through is tinted by
        // _LeafTint (linear) and by the surface's own colour.
        float4 _LeafLight;
        half4 _LeafTint;
        // A mesh model's fade where its faces turn edge on (ModelEdgeFade.hlsl): where it starts and
        // ends in x and y, 0 and 0 everywhere else, which discards nothing.
        float4 _LeafEdge;
        // 1 where a loaded model has a colour mask (_PaintMask), 0 everywhere else, which never reads it.
        half _Painted;
    #endif
CBUFFER_END

// What one actor has of its own, set by ModelBridge only on an actor that needs it: a
// renderer with a property block leaves URP's SRP Batcher, so every other actor has none,
// and 0 (whole, and shadowed) is what an unset value reads. Per instance where the
// material instances, so the actors that do carry them still draw together.
#if defined(UNITY_INSTANCING_ENABLED)
    UNITY_INSTANCING_BUFFER_START(ModelActor)
        #ifndef MODEL_SCATTERED
            UNITY_DEFINE_INSTANCED_PROP(float, _ActorDissolve)
        #endif
        UNITY_DEFINE_INSTANCED_PROP(float4, _ActorLightmapST)
        UNITY_DEFINE_INSTANCED_PROP(float4, _ActorTint)
    UNITY_INSTANCING_BUFFER_END(ModelActor)
    #ifdef MODEL_SCATTERED
        #define MODEL_ACTOR_DISSOLVE 0
    #else
        #define MODEL_ACTOR_DISSOLVE UNITY_ACCESS_INSTANCED_PROP(ModelActor, _ActorDissolve)
    #endif
    #define MODEL_ACTOR_LIGHTMAP_ST UNITY_ACCESS_INSTANCED_PROP(ModelActor, _ActorLightmapST)
    #define MODEL_ACTOR_TINT UNITY_ACCESS_INSTANCED_PROP(ModelActor, _ActorTint)
#else
    float _ActorDissolve;
    float4 _ActorLightmapST;
    float4 _ActorTint;
    #define MODEL_ACTOR_DISSOLVE _ActorDissolve
    #define MODEL_ACTOR_LIGHTMAP_ST _ActorLightmapST
    #define MODEL_ACTOR_TINT _ActorTint
#endif

// Whether an actor keeps no shadow on itself: bit 0 of its renderer's user value
// (ModelBridge.Apply), in UnityPerDraw where the SRP Batcher draws it and in the instanced
// draw's arrays (`instancing_options renderinglayer` in the forward pass) where it instances.
// Unlike a property block, it keeps the actor in the SRP Batcher. A scattered copy reads 0.
// A scattered copy's is its scatter's (_ScatterShadowless, ModelRange.hlsl), whichever variant draws it.
#ifdef MODEL_SCATTERED
    #define MODEL_ACTOR_SHADOWLESS ((uint)_ScatterShadowless)
#else
    #define MODEL_ACTOR_SHADOWLESS ((unity_RendererUserValue & 1u) | (uint)_ScatterShadowless)
#endif

// An actor's own colours (ModelBridge.Colors.cs): its row of _OjActorColors is its renderer's user
// value above the shadow bit, 0 for an actor with none, which reads nothing. The row's first texel
// is its tint, multiplied into the whole of it; the next four are multiplied in where the model's
// mask says, red for the first to alpha for the fourth. The texture is sRGB, so a load is linear.
// The row rides where the shadow bit does, so a coloured actor stays in the SRP Batcher.
TEXTURE2D(_OjActorColors);
#ifndef MODEL_SPLAT
    TEXTURE2D(_PaintMask); SAMPLER(sampler_PaintMask);
#endif

half3 Painted(half3 color, float2 uv) {
    #ifdef MODEL_SCATTERED
        return color;
    #else
        uint row = unity_RendererUserValue >> 1;
        if (row == 0u) return color;
        color *= LOAD_TEXTURE2D(_OjActorColors, int2(0, row)).rgb;
        #ifndef MODEL_SPLAT
            if (_Painted > 0.5) {
                half4 mask = SAMPLE_TEXTURE2D(_PaintMask, sampler_PaintMask, uv);
                color *= lerp(half3(1, 1, 1), LOAD_TEXTURE2D(_OjActorColors, int2(1, row)).rgb, mask.r);
                color *= lerp(half3(1, 1, 1), LOAD_TEXTURE2D(_OjActorColors, int2(2, row)).rgb, mask.g);
                color *= lerp(half3(1, 1, 1), LOAD_TEXTURE2D(_OjActorColors, int2(3, row)).rgb, mask.b);
                color *= lerp(half3(1, 1, 1), LOAD_TEXTURE2D(_OjActorColors, int2(4, row)).rgb, mask.a);
            }
        #endif
        return color;
    #endif
}

// A scattered copy's tint (linear, its alpha 1), multiplied into its colour; unset, it reads 0 and
// changes nothing.
half3 Tinted(half3 color) {
    float4 tint = MODEL_ACTOR_TINT;
    return color * lerp(float3(1, 1, 1), tint.rgb, tint.a);
}

// Where a vertex lands in the world, swayed by the wind when its model sways. A branch on a
// material value, so every vertex of a draw takes the same way.
// A plain sway's stream has one number a vertex, which a float4 input reads as (sway, 0, 0, 1).
float3 SwayedWorld(float3 positionOS, float3 normalOS, float4 sway) {
    float3 positionWS = TransformObjectToWorld(positionOS);
    if (_Sway > 1.5) positionWS = ModelSwayedLayered(positionWS, TransformObjectToWorldNormal(normalOS), GetObjectToWorldMatrix(), sway);
    else if (_Sway > 0.5) positionWS = ModelSwayed(positionWS, GetObjectToWorldMatrix(), sway.x);
    return positionWS;
}

// The draw range of the copy being drawn (ModelRange.hlsl), from where its model stands.
float2 CopyRange() {
    float4x4 m = GetObjectToWorldMatrix();
    return ModelRange(float3(m._m03, m._m13, m._m23));
}

// A vertex at `positionWS`, or its copy's origin when `keep` (ModelRangeKeep) is 0. After the
// sway, which would otherwise spread a collapsed triangle out again.
float3 KeptInRange(float3 positionWS, float keep) {
    float4x4 m = GetObjectToWorldMatrix();
    return lerp(float3(m._m03, m._m13, m._m23), positionWS, keep);
}

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

// The lit pass's clip for a depth pass, by the base map's alpha times the colour's and `vertexAlpha`.
float ClipAt(float3 positionOS, float2 uv, half vertexAlpha) {
    float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a * _BaseColor.a * vertexAlpha;
    return ModelClip(positionOS, alpha, _AlphaClip, _Cutoff, Dissolved(), _DissolveScale);
}

#endif
