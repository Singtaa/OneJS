// ModelLit's and GroundLit's built-in pipeline SubShader: Unity's Standard lighting through a
// surface shader, which brings its shadows (cast and received) and forward lights with it. The
// shader file declares the surface pragma; this is its code. Under MODEL_SPLAT (GroundLit) the
// albedo is the splat's layers mixed by the vertex's weights, as in ModelLitForward.hlsl.
#ifndef ONEJS_MODEL_LIT_SURFACE_INCLUDED
#define ONEJS_MODEL_LIT_SURFACE_INCLUDED

#include "ModelDissolve.hlsl"
#include "ModelLightmap.hlsl"
#include "ModelWind.hlsl"
#include "ModelRange.hlsl"
#ifdef MODEL_SPLAT
    #include "ModelSplat.hlsl"
    #ifndef _VERTEX_COLOR
        #define _VERTEX_COLOR 1
    #endif
#endif

sampler2D _BaseMap;
#ifdef MODEL_SPLAT
    sampler2D _SplatLayer0;
    sampler2D _SplatLayer1;
    sampler2D _SplatLayer2;
    sampler2D _SplatLayer3;
    half _HeightBlend;
    float4 _SplatTriplanar;
    float4 _SplatFar;
#else
    sampler2D _MetallicRoughnessMap;
    sampler2D _NormalMap;
    sampler2D _OcclusionMap;
    sampler2D _EmissionMap;
#endif
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
// A mesh model's sway (see ModelLitURP.hlsl). A surface shader lights a back face with its normal
// as given, so `_KeepBackNormal` is already what it does.
half _Sway;
// The actor's own dissolve (see ModelLitURP.hlsl); the built-in pipeline reads receiving
// shadows from the renderer.
float _ActorDissolve;
// The baked light (see ModelLitURP.hlsl). The surface shader adds the ambient probe itself,
// so ModelBridge gives a baked actor a zero probe and its light arrives here as emission.
UNITY_INSTANCING_BUFFER_START(ModelActorLight)
    UNITY_DEFINE_INSTANCED_PROP(float4, _ActorLightmapST)
    // A scattered copy's tint (see ModelLitURP.hlsl).
    UNITY_DEFINE_INSTANCED_PROP(float4, _ActorTint)
UNITY_INSTANCING_BUFFER_END(ModelActorLight)
UNITY_DECLARE_TEX2DARRAY(_OjLightmap);

struct Input {
    float2 uv_BaseMap;
    float3 positionOS;
    // The uv on the bake's sheet (ModelLightmap.hlsl) in xy, and the copy's draw range (ModelRange.hlsl) in zw.
    float4 lightmapUVRange;
    // Where the pixel is, for the range's dither (and a splat's triplanar layers): a surface shader
    // already carries it for its lights, so it costs no interpolator of its own.
    float3 worldPos;
    // Filled from the mesh's COLOR by the surface shader; read only under _VERTEX_COLOR.
    float4 color : COLOR;
    #ifdef MODEL_SPLAT
        float4 weights;
        // For a triplanar layer. The splat writes no o.Normal, so worldNormal is the surface's own.
        float3 worldNormal;
    #endif
};

void Vert(inout appdata_full v, out Input o) {
    UNITY_INITIALIZE_OUTPUT(Input, o);
    o.positionOS = v.vertex.xyz;
    float4 lightmapST = UNITY_ACCESS_INSTANCED_PROP(ModelActorLight, _ActorLightmapST);
    float3 origin = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
    o.lightmapUVRange = float4(ModelLightmapUV(v.texcoord1.xy, lightmapST), ModelRange(origin));
    // Swayed in the world and brought back, since a surface shader's vertex is in object space.
    if (_Sway > 0.5) {
        float3 positionWS = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1)).xyz;
        v.vertex.xyz = mul(unity_WorldToObject, float4(ModelSwayed(positionWS, unity_ObjectToWorld, v.texcoord3.x), 1)).xyz;
    }
    // A copy its range drops whole collapses onto its origin (ModelRangeKeep), after the sway.
    #ifdef UNITY_PASS_SHADOWCASTER
        v.vertex.xyz *= ModelRangeShadowKeep(o.lightmapUVRange.zw);
    #else
        v.vertex.xyz *= ModelRangeKeep(o.lightmapUVRange.zw);
    #endif
    #ifdef MODEL_SPLAT
        o.weights = v.texcoord2;
    #endif
}

#ifdef MODEL_SPLAT
// One splat layer, at the uvs or projected along each world axis, read coarser with distance by
// `farScale`: ModelLitForward.hlsl's SplatLayer.
half4 SplatLayer(sampler2D layer, float2 uv, float perMetre, float3 positionWS, float3 planes, float farScale) {
    if (perMetre <= 0) return tex2Dbias(layer, float4(uv, 0, log2(farScale)));
    float3 p = positionWS * perMetre;
    float3 dx = ddx(p) * farScale, dy = ddy(p) * farScale;
    return tex2Dgrad(layer, p.zy, dx.zy, dy.zy) * planes.x
        + tex2Dgrad(layer, p.xz, dx.xz, dy.xz) * planes.y
        + tex2Dgrad(layer, p.xy, dx.xy, dy.xy) * planes.z;
}
#endif

void Surf(Input i, inout SurfaceOutputStandard o) {
    #ifdef UNITY_PASS_SHADOWCASTER
        ModelRangeShadowClip(i.lightmapUVRange.zw);
    #else
        float4 clipPos = UnityWorldToClipPos(i.worldPos);
        ModelRangeClip(i.lightmapUVRange.zw, (clipPos.xy / clipPos.w * 0.5 + 0.5) * _ScreenParams.xy);
    #endif
    float2 uv = i.uv_BaseMap;
    #ifdef MODEL_SPLAT
        float3 planes = SplatPlanes(i.worldNormal);
        float farScale = SplatFarScale(i.worldPos, _WorldSpaceCameraPos, _SplatFar.x);
        half3 mixed = SplatMix(
            SplatLayer(_SplatLayer0, uv, _SplatTriplanar.x, i.worldPos, planes, farScale),
            SplatLayer(_SplatLayer1, uv, _SplatTriplanar.y, i.worldPos, planes, farScale),
            SplatLayer(_SplatLayer2, uv, _SplatTriplanar.z, i.worldPos, planes, farScale),
            SplatLayer(_SplatLayer3, uv, _SplatTriplanar.w, i.worldPos, planes, farScale),
            i.weights, _HeightBlend);
        if (_SplatFar.y > 0) mixed *= SplatVariation(i.worldPos, _SplatFar.y);
        half4 base = half4(mixed, 1) * _BaseColor;
    #else
        half4 base = tex2D(_BaseMap, uv) * _BaseColor;
    #endif
    #ifdef _VERTEX_COLOR
        base *= i.color;
    #endif
    float4 tint = UNITY_ACCESS_INSTANCED_PROP(ModelActorLight, _ActorTint);
    base.rgb *= lerp(float3(1, 1, 1), tint.rgb, tint.a);
    float cut = ModelClip(i.positionOS, base.a, _AlphaClip, _Cutoff, max(_Dissolve, _ActorDissolve), _DissolveScale);
    #ifdef MODEL_SPLAT
        half3 emission = ModelEdge(cut, _DissolveEdge.rgb);
    #else
        half3 emission = tex2D(_EmissionMap, uv).rgb * _EmissionColor.rgb + ModelEdge(cut, _DissolveEdge.rgb);
    #endif
    o.Alpha = base.a;
    if (_Unlit > 0.5) {
        o.Albedo = 0;
        o.Emission = base.rgb + emission;
        return;
    }
    o.Albedo = base.rgb;
    #ifdef MODEL_SPLAT
        o.Metallic = _Metallic;
        o.Smoothness = 1.0 - _Roughness;
        o.Occlusion = 1.0;
    #else
        half4 mr = tex2D(_MetallicRoughnessMap, uv);
        o.Metallic = mr.b * _Metallic;
        o.Smoothness = 1.0 - mr.g * _Roughness;
        o.Normal = UnpackScaleNormal(tex2D(_NormalMap, uv), _NormalScale);
        o.Occlusion = lerp(1.0, tex2D(_OcclusionMap, uv).r, _OcclusionStrength);
    #endif
    o.Emission = emission;
    if (UNITY_ACCESS_INSTANCED_PROP(ModelActorLight, _ActorLightmapST).x > 0) {
        o.Emission += o.Albedo * (1.0 - o.Metallic) * o.Occlusion * UNITY_SAMPLE_TEX2DARRAY(_OjLightmap, ModelLightmapPage(i.lightmapUVRange.xy)).rgb;
    }
}

#endif
