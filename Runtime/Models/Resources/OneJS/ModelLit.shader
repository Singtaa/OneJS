// The one shader OneJS draws glTF models with (ModelMaterialGenerator builds every material on it).
// Base colour, optional normal and occlusion maps, the main directional light, flat ambient, and a
// noise dissolve driven by _Dissolve (0 whole, 1 gone) with a glowing edge.
//
// Two subshaders over one body: URP reads its main light from _MainLightPosition/_MainLightColor,
// the built-in pipeline from _WorldSpaceLightPos0/_LightColor0. Neither includes a pipeline's own
// headers, so the file compiles in a project that has only one of them.
Shader "OneJS/ModelLit" {
    Properties {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        [Normal] _NormalMap ("Normal Map", 2D) = "bump" {}
        _OcclusionMap ("Occlusion", 2D) = "white" {}
        _Dissolve ("Dissolve", Range(0, 1)) = 0
        _DissolveScale ("Dissolve Noise Scale", Float) = 6
        [HDR] _DissolveEdge ("Dissolve Edge", Color) = (0.6, 0.9, 2.5, 1)
        [HideInInspector] _Cull ("Cull", Float) = 2
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    #pragma multi_compile_instancing
    // multi_compile, not shader_feature: every material is made at runtime, so a build sees
    // none that use these and would strip the variants a model needs.
    #pragma multi_compile_local _ _NORMALMAP
    #pragma multi_compile_local _ _OCCLUSIONMAP

    sampler2D _BaseMap; float4 _BaseMap_ST;
    sampler2D _NormalMap;
    sampler2D _OcclusionMap;
    float4 _BaseColor;
    float _DissolveScale;
    float4 _DissolveEdge;
    UNITY_INSTANCING_BUFFER_START(Props)
        UNITY_DEFINE_INSTANCED_PROP(float, _Dissolve)
    UNITY_INSTANCING_BUFFER_END(Props)

    struct Attributes {
        float4 vertex : POSITION;
        float3 normal : NORMAL;
        float4 tangent : TANGENT;
        float2 uv : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
        float3 normalWS : TEXCOORD1;
        float4 tangentWS : TEXCOORD2;
        float3 objectPos : TEXCOORD3;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    Varyings vert(Attributes v) {
        Varyings o;
        UNITY_SETUP_INSTANCE_ID(v);
        UNITY_TRANSFER_INSTANCE_ID(v, o);
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
        o.normalWS = UnityObjectToWorldNormal(v.normal);
        o.tangentWS = float4(UnityObjectToWorldDir(v.tangent.xyz), v.tangent.w);
        o.objectPos = v.vertex.xyz;
        return o;
    }

    float hash3(float3 p) {
        p = frac(p * 0.3183099 + 0.1);
        p *= 17.0;
        return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
    }

    float valueNoise(float3 p) {
        float3 i = floor(p), f = frac(p);
        f = f * f * (3.0 - 2.0 * f);
        return lerp(
            lerp(lerp(hash3(i), hash3(i + float3(1, 0, 0)), f.x),
                 lerp(hash3(i + float3(0, 1, 0)), hash3(i + float3(1, 1, 0)), f.x), f.y),
            lerp(lerp(hash3(i + float3(0, 0, 1)), hash3(i + float3(1, 0, 1)), f.x),
                 lerp(hash3(i + float3(0, 1, 1)), hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
    }

    float4 shade(Varyings i, float3 lightDir, float3 lightColor) {
        UNITY_SETUP_INSTANCE_ID(i);
        float dissolve = UNITY_ACCESS_INSTANCED_PROP(Props, _Dissolve);
        float n = valueNoise(i.objectPos * _DissolveScale) * 0.65 + valueNoise(i.objectPos * _DissolveScale * 2.3) * 0.35;
        float cut = n - dissolve * 1.08;
        clip(cut);

        float3 normal = normalize(i.normalWS);
        #if defined(_NORMALMAP)
            float3 t = normalize(i.tangentWS.xyz);
            float3 b = cross(normal, t) * i.tangentWS.w;
            float3 tn = UnpackNormal(tex2D(_NormalMap, i.uv));
            normal = normalize(tn.x * t + tn.y * b + tn.z * normal);
        #endif

        float4 base = tex2D(_BaseMap, i.uv) * _BaseColor;
        float ao = 1;
        #if defined(_OCCLUSIONMAP)
            ao = tex2D(_OcclusionMap, i.uv).r;
        #endif
        float3 ambient = unity_AmbientSky.rgb;
        float3 lit = base.rgb * (ambient * ao + lightColor * saturate(dot(normal, lightDir)));
        float edge = (dissolve > 0.001) ? 1 - smoothstep(0, 0.06, cut) : 0;
        return float4(lit + _DissolveEdge.rgb * edge, 1);
    }
    ENDCG

    SubShader {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass {
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 _MainLightPosition;
            half4 _MainLightColor;
            float4 frag(Varyings i) : SV_Target { return shade(i, normalize(_MainLightPosition.xyz), _MainLightColor.rgb); }
            ENDCG
        }
        Pass {
            Tags { "LightMode" = "DepthOnly" }
            ColorMask 0
            Cull [_Cull]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth
            float4 _MainLightPosition;
            float4 fragDepth(Varyings i) : SV_Target { shade(i, float3(0, 1, 0), 0); return 0; }
            ENDCG
        }
    }

    SubShader {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass {
            Tags { "LightMode" = "ForwardBase" }
            Cull [_Cull]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 _LightColor0;
            float4 frag(Varyings i) : SV_Target { return shade(i, normalize(_WorldSpaceLightPos0.xyz), _LightColor0.rgb); }
            ENDCG
        }
    }
}
