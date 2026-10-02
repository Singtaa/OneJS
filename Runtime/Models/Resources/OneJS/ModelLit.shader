// The shader OneJS draws glTF models with (ModelMaterialGenerator builds every material on it).
//
// glTF's metallic-roughness material as glTF lays it out (metal in B and roughness in G of one
// texture, occlusion in R), lit by the pipeline's own PBR lighting: the sun's soft cascaded
// shadows, point lights, ambient from the probe, SSAO and fog. Plus a noise dissolve.
//
// Every SubShader carries PackageRequirements on glTFast, so in a project without the models
// assembly the file compiles to nothing and costs a build nothing. The URP SubShader also needs
// URP; the built-in one is a surface shader. HDRP has neither, and ModelMaterialGenerator hands
// such a project glTFast's own materials instead.
//
// One shader in every host is the point: the Play container and a Unity project draw a cart's
// models with the same code, so they look the same. Specular reflections come from the ambient
// colour rather than a reflection probe for the same reason: a probe is the one input a cart
// does not set, and it differs from scene to scene.
Shader "OneJS/ModelLit" {
    Properties {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _MetallicRoughnessMap ("Metallic (B) Roughness (G)", 2D) = "white" {}
        _Metallic ("Metallic", Range(0, 1)) = 1
        _Roughness ("Roughness", Range(0, 1)) = 1
        [Normal] _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalScale ("Normal Scale", Float) = 1
        _OcclusionMap ("Occlusion (R)", 2D) = "white" {}
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 1
        _EmissionMap ("Emission Map", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        [Toggle] _AlphaClip ("Alpha Clip", Float) = 0
        [Toggle] _Unlit ("Unlit", Float) = 0
        _Dissolve ("Dissolve", Range(0, 1)) = 0
        _DissolveScale ("Dissolve Noise Scale", Float) = 6
        [HDR] _DissolveEdge ("Dissolve Edge", Color) = (0.6, 0.9, 2.5, 1)
        [Toggle] _ReceiveShadows ("Receive Shadows", Float) = 1
        [HideInInspector] _Surface ("Surface", Float) = 0
        [HideInInspector] _Cull ("Cull", Float) = 2
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 1
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
    }

    SubShader {
        PackageRequirements {
            "com.unity.render-pipelines.universal"
            "com.unity.cloud.gltfast"
        }
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "ModelDissolve.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_MetallicRoughnessMap); SAMPLER(sampler_MetallicRoughnessMap);
        TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
        TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
        TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);

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
        CBUFFER_END

        float ClipAt(float3 positionOS, float2 uv) {
            float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a * _BaseColor.a;
            return ModelClip(positionOS, alpha, _AlphaClip, _Cutoff, _Dissolve, _DissolveScale);
        }
        ENDHLSL

        Pass {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            // URP's lighting keywords, as its Lit shader declares them, less lightmaps, decals,
            // reflection probes and debug views, which a cart's scene never has.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #define _ENVIRONMENTREFLECTIONS_OFF 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                float3 positionOS : TEXCOORD4;
                half4 fogAndVertexLight : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes v) {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.tangentWS = half4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.positionOS = v.positionOS.xyz;
                o.fogAndVertexLight.x = ComputeFogFactor(p.positionCS.z);
                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                    o.fogAndVertexLight.yzw = VertexLighting(p.positionWS, n.normalWS);
                #endif
                return o;
            }

            half4 Frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                float cut = ModelClip(i.positionOS, base.a, _AlphaClip, _Cutoff, _Dissolve, _DissolveScale);
                half3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb * _EmissionColor.rgb
                    + ModelEdge(cut, _DissolveEdge.rgb);

                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.fogCoord = InitializeInputDataFog(float4(i.positionWS, 1.0), i.fogAndVertexLight.x);

                if (_Unlit > 0.5) {
                    half3 flat = MixFog(base.rgb + emission, d.fogCoord);
                    return half4(flat, OutputAlpha(base.a, IsSurfaceTypeTransparent(_Surface)));
                }

                half4 mr = SAMPLE_TEXTURE2D(_MetallicRoughnessMap, sampler_MetallicRoughnessMap, i.uv);
                SurfaceData s = (SurfaceData)0;
                s.albedo = base.rgb;
                s.alpha = base.a;
                s.metallic = mr.b * _Metallic;
                // glTF roughness is perceptual roughness, which is what URP's smoothness inverts.
                s.smoothness = 1.0 - mr.g * _Roughness;
                s.normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv), _NormalScale);
                s.occlusion = lerp(1.0, SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, i.uv).r, _OcclusionStrength);
                s.emission = emission;

                // A double-sided material's back faces light as the side the viewer sees.
                half3 normalWS = i.normalWS * IS_FRONT_VFACE(face, 1.0, -1.0);
                float3 bitangent = i.tangentWS.w * cross(normalWS, i.tangentWS.xyz);
                d.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(s.normalTS, half3x3(i.tangentWS.xyz, bitangent, normalWS)));
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    // A shadow coordinate past the far plane reads as unshadowed, on either depth convention.
                    d.shadowCoord = _ReceiveShadows > 0.5 ? TransformWorldToShadowCoord(i.positionWS) : float4(0, 0, UNITY_RAW_FAR_CLIP_VALUE, 0);
                #endif
                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                    d.vertexLighting = i.fogAndVertexLight.yzw;
                #endif
                d.bakedGI = SampleSH(d.normalWS);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(d, s);
                color.rgb = MixFog(color.rgb, d.fogCoord);
                color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent(_Surface));
                return color;
            }
            ENDHLSL
        }

        Pass {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes v) {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.positionOS = v.positionOS.xyz;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target {
                UNITY_SETUP_INSTANCE_ID(i);
                ClipAt(i.positionOS, i.uv);
                return 0;
            }
            ENDHLSL
        }

        Pass {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes v) {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.positionOS = v.positionOS.xyz;
                return o;
            }

            half Frag(Varyings i) : SV_Target {
                UNITY_SETUP_INSTANCE_ID(i);
                ClipAt(i.positionOS, i.uv);
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes v) {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.positionOS = v.positionOS.xyz;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target {
                UNITY_SETUP_INSTANCE_ID(i);
                ClipAt(i.positionOS, i.uv);
                return half4(NormalizeNormalPerPixel(i.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    // The built-in pipeline: Unity's Standard lighting through a surface shader, which brings its
    // shadows (cast and received) and forward lights with it.
    SubShader {
        PackageRequirements {
            "com.unity.cloud.gltfast"
        }
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        Cull [_Cull]

        CGPROGRAM
        #pragma surface Surf Standard fullforwardshadows addshadow keepalpha vertex:Vert
        #pragma target 3.0
        #include "ModelDissolve.hlsl"

        sampler2D _BaseMap;
        sampler2D _MetallicRoughnessMap;
        sampler2D _NormalMap;
        sampler2D _OcclusionMap;
        sampler2D _EmissionMap;
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

        struct Input {
            float2 uv_BaseMap;
            float3 positionOS;
        };

        void Vert(inout appdata_full v, out Input o) {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.positionOS = v.vertex.xyz;
        }

        void Surf(Input i, inout SurfaceOutputStandard o) {
            float2 uv = i.uv_BaseMap;
            half4 base = tex2D(_BaseMap, uv) * _BaseColor;
            float cut = ModelClip(i.positionOS, base.a, _AlphaClip, _Cutoff, _Dissolve, _DissolveScale);
            half3 emission = tex2D(_EmissionMap, uv).rgb * _EmissionColor.rgb + ModelEdge(cut, _DissolveEdge.rgb);
            o.Alpha = base.a;
            if (_Unlit > 0.5) {
                o.Albedo = 0;
                o.Emission = base.rgb + emission;
                return;
            }
            half4 mr = tex2D(_MetallicRoughnessMap, uv);
            o.Albedo = base.rgb;
            o.Metallic = mr.b * _Metallic;
            o.Smoothness = 1.0 - mr.g * _Roughness;
            o.Normal = UnpackScaleNormal(tex2D(_NormalMap, uv), _NormalScale);
            o.Occlusion = lerp(1.0, tex2D(_OcclusionMap, uv).r, _OcclusionStrength);
            o.Emission = emission;
        }
        ENDCG
    }
}
