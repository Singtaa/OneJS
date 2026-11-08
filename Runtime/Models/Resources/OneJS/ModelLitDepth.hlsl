// ModelLit's and GroundLit's three depth passes in URP, after ModelLitURP.hlsl: the shader file
// defines which one a pass is (MODEL_PASS_SHADOW_CASTER, MODEL_PASS_DEPTH_ONLY or
// MODEL_PASS_DEPTH_NORMALS) and declares its pragmas. Each clips as the lit pass does, by the
// base map's alpha and the dissolve, so a cut-out or dissolving model casts and occludes what is seen.
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
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings {
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 positionOS : TEXCOORD1;
    #if defined(MODEL_PASS_DEPTH_NORMALS)
        half3 normalWS : TEXCOORD2;
    #endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

Varyings Vert(Attributes v) {
    Varyings o = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, o);
    #if defined(MODEL_PASS_SHADOW_CASTER)
        float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
        float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
        #if _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 lightDirectionWS = normalize(_LightPosition - positionWS);
        #else
            float3 lightDirectionWS = _LightDirection;
        #endif
        o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
    #else
        o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
    #endif
    #if defined(MODEL_PASS_DEPTH_NORMALS)
        o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    #endif
    o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
    o.positionOS = v.positionOS.xyz;
    return o;
}

#if defined(MODEL_PASS_SHADOW_CASTER)
    half4 Frag(Varyings i) : SV_Target {
        UNITY_SETUP_INSTANCE_ID(i);
        ClipAt(i.positionOS, i.uv);
        return 0;
    }
#elif defined(MODEL_PASS_DEPTH_ONLY)
    half Frag(Varyings i) : SV_Target {
        UNITY_SETUP_INSTANCE_ID(i);
        ClipAt(i.positionOS, i.uv);
        return i.positionCS.z;
    }
#else
    half4 Frag(Varyings i) : SV_Target {
        UNITY_SETUP_INSTANCE_ID(i);
        ClipAt(i.positionOS, i.uv);
        return half4(NormalizeNormalPerPixel(i.normalWS), 0.0);
    }
#endif

#endif
