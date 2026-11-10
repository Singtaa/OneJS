// ModelLit's and GroundLit's three depth passes in URP, after ModelLitURP.hlsl: the shader file
// defines which one a pass is (MODEL_PASS_SHADOW_CASTER, MODEL_PASS_DEPTH_ONLY or
// MODEL_PASS_DEPTH_NORMALS) and declares its pragmas. Each clips as the lit pass does, by the
// base map's alpha (times the vertex colours' under _VERTEX_COLOR), the dissolve and a scatter's
// range, and sways as it does, so a cut-out, dissolving or swaying model casts and occludes what is seen.
#ifndef ONEJS_MODEL_LIT_DEPTH_INCLUDED
#define ONEJS_MODEL_LIT_DEPTH_INCLUDED

#if defined(MODEL_PASS_SHADOW_CASTER)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
    float3 _LightDirection;
    float3 _LightPosition;
#endif

struct Attributes {
    float4 positionOS : POSITION;
    #if !defined(MODEL_PASS_DEPTH_ONLY)
        float3 normalOS : NORMAL;
    #endif
    float2 uv : TEXCOORD0;
    float sway : TEXCOORD3;
    #ifdef _VERTEX_COLOR
        float4 color : COLOR;
    #endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings {
    float4 positionCS : SV_POSITION;
    // The uv in xy, and the copy's draw range (CopyRange) in zw.
    float4 uvRange : TEXCOORD0;
    float3 positionOS : TEXCOORD1;
    #if defined(MODEL_PASS_DEPTH_NORMALS)
        half3 normalWS : TEXCOORD2;
    #endif
    #ifdef _VERTEX_COLOR
        half alpha : TEXCOORD3;
    #endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

Varyings Vert(Attributes v) {
    Varyings o = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, o);
    float2 range = CopyRange();
    #if defined(MODEL_PASS_SHADOW_CASTER)
        float keep = ModelRangeShadowKeep(range);
    #else
        float keep = ModelRangeKeep(range);
    #endif
    float3 positionWS = KeptInRange(SwayedWorld(v.positionOS.xyz, v.sway), keep);
    #if defined(MODEL_PASS_SHADOW_CASTER)
        float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
        #if _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 lightDirectionWS = normalize(_LightPosition - positionWS);
        #else
            float3 lightDirectionWS = _LightDirection;
        #endif
        o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
    #else
        o.positionCS = TransformWorldToHClip(positionWS);
    #endif
    #if defined(MODEL_PASS_DEPTH_NORMALS)
        o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    #endif
    o.uvRange = float4(TRANSFORM_TEX(v.uv, _BaseMap), range);
    o.positionOS = v.positionOS.xyz;
    #ifdef _VERTEX_COLOR
        o.alpha = v.color.a;
    #endif
    return o;
}

// What every depth pass discards: by the range (the shadow's cut, or the camera's dither), then
// as the lit pass clips.
void DepthClip(Varyings i) {
    #if defined(MODEL_PASS_SHADOW_CASTER)
        ModelRangeShadowClip(i.uvRange.zw);
    #else
        ModelRangeClip(i.uvRange.zw, i.positionCS.xy);
        #ifndef MODEL_SPLAT
            ModelEdgeFadeClip(i.positionOS, i.positionCS.xy, _LeafEdge.xy);
        #endif
    #endif
    #ifdef _VERTEX_COLOR
        half alpha = i.alpha;
    #else
        half alpha = 1;
    #endif
    ClipAt(i.positionOS, i.uvRange.xy, alpha);
}

#if defined(MODEL_PASS_SHADOW_CASTER)
    half4 Frag(Varyings i) : SV_Target {
        UNITY_SETUP_INSTANCE_ID(i);
        DepthClip(i);
        return 0;
    }
#elif defined(MODEL_PASS_DEPTH_ONLY)
    half Frag(Varyings i) : SV_Target {
        UNITY_SETUP_INSTANCE_ID(i);
        DepthClip(i);
        return i.positionCS.z;
    }
#else
    half4 Frag(Varyings i) : SV_Target {
        UNITY_SETUP_INSTANCE_ID(i);
        DepthClip(i);
        return half4(NormalizeNormalPerPixel(i.normalWS), 0.0);
    }
#endif

#endif
