// The shader OneJS draws glTF models with (ModelMaterialGenerator builds every material on it).
//
// glTF's metallic-roughness material as glTF lays it out (metal in B and roughness in G of one
// texture, occlusion in R), lit by the pipeline's own PBR lighting: the sun's soft cascaded
// shadows, point lights, ambient from the probe, SSAO and fog. Plus a noise dissolve, and a mesh
// model's vertex colours.
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
        // 15 writes every channel; 0 writes depth alone, as a shadow fan or an occluder wants, without blending a
        // colour nobody sees (Gunyard, Tachi 5f9c606d).
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
    }

    SubShader {
        PackageRequirements {
            "com.unity.render-pipelines.universal"
            "com.unity.cloud.gltfast"
        }
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "ModelLitURP.hlsl"
        ENDHLSL

        Pass {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ColorMask [_ColorMask]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            // The baked light's pages are a texture array.
            #pragma require 2darray
            #pragma vertex Vert
            #pragma fragment Frag

            // URP's lighting keywords, as its Lit shader declares them, less Unity's lightmaps, decals,
            // reflection probes and debug views, which a cart's scene never has. Its baked light is
            // the bake's own (_OjLightmap in ModelLitURP.hlsl), which needs no keyword.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            // A mesh model's own colours (ModelBridge.CreateMesh). A glTF material never enables it, so a
            // file's model reads no COLOR and draws as it did. multi_compile, since the material that
            // enables it is made at run time, after a build's stripping has decided.
            #pragma multi_compile_local _ _VERTEX_COLOR
            #include "ModelLitForward.hlsl"
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
            #define MODEL_PASS_SHADOW_CASTER
            #include "ModelLitDepth.hlsl"
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
            #define MODEL_PASS_DEPTH_ONLY
            #include "ModelLitDepth.hlsl"
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
            #define MODEL_PASS_DEPTH_NORMALS
            #include "ModelLitDepth.hlsl"
            ENDHLSL
        }
    }

    // The built-in pipeline: Unity's Standard lighting through a surface shader (ModelLitSurface.hlsl).
    SubShader {
        PackageRequirements {
            "com.unity.cloud.gltfast"
        }
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        ColorMask [_ColorMask]
        Cull [_Cull]

        CGPROGRAM
        #pragma surface Surf Standard fullforwardshadows addshadow keepalpha vertex:Vert
        #pragma target 3.0
        #pragma require 2darray
        #pragma multi_compile_local _ _VERTEX_COLOR
        #include "ModelLitSurface.hlsl"
        ENDCG
    }
}
