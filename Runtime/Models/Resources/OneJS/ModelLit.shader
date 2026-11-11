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
        // A mesh model's sway (ModelWind.hlsl) and two-sided lighting: see ModelLitURP.hlsl.
        [HideInInspector] _Sway ("Sway", Float) = 0
        [HideInInspector] _KeepBackNormal ("Keep Back Normal", Float) = 0
        // A see-through mesh model's fade by the depth behind it and its deep colour: see ModelLitURP.hlsl.
        [HideInInspector] _DepthFade ("Depth Fade", Vector) = (0, 0, 0, 0)
        [HideInInspector] _DeepColor ("Deep Color", Color) = (0, 0, 0, 0)
        // A mesh model's normal map flow and own frame: see ModelLitURP.hlsl.
        [HideInInspector] _NormalFlow ("Normal Flow", Vector) = (0, 0, 0, 0)
        // A mesh model's leaf light, wrap in x, backLight in y and its focus in z: see ModelLeafLight.hlsl.
        [HideInInspector] _LeafLight ("Leaf Light", Vector) = (0, 0, 4, 0)
        [HideInInspector] _LeafTint ("Leaf Tint", Color) = (1, 1, 1, 1)
        // A mesh model's fade where its faces turn edge on: see ModelEdgeFade.hlsl.
        [HideInInspector] _LeafEdge ("Leaf Edge", Vector) = (0, 0, 0, 0)
        // A loaded model's colour mask, and 1 where it has one: see Painted in ModelLitURP.hlsl.
        [HideInInspector] [NoScaleOffset] _PaintMask ("Paint Mask", 2D) = "black" {}
        [HideInInspector] _Painted ("Painted", Float) = 0
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
            #pragma multi_compile_instancing
            // A scattered copy's arrays (ModelLitURP.hlsl); a build keeps it only instanced (ModelShaderStripper).
            #pragma multi_compile_local _ _SCATTER
            // Each instance's renderer values, for MODEL_ACTOR_SHADOWLESS (ModelLitURP.hlsl).
            #pragma instancing_options renderinglayer
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
            // A scattered copy's arrays (ModelLitURP.hlsl); a build keeps it only instanced (ModelShaderStripper).
            #pragma multi_compile_local _ _SCATTER
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_local _ _VERTEX_COLOR
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
            // A scattered copy's arrays (ModelLitURP.hlsl); a build keeps it only instanced (ModelShaderStripper).
            #pragma multi_compile_local _ _SCATTER
            // A mesh model's colours, whose alpha clips its depth as it clips its colour (ModelLitDepth.hlsl).
            #pragma multi_compile_local _ _VERTEX_COLOR
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
            // A scattered copy's arrays (ModelLitURP.hlsl); a build keeps it only instanced (ModelShaderStripper).
            #pragma multi_compile_local _ _SCATTER
            // A mesh model's colours, whose alpha clips its depth as it clips its colour (ModelLitDepth.hlsl).
            #pragma multi_compile_local _ _VERTEX_COLOR
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
        #pragma surface Surf Standard fullforwardshadows addshadow keepalpha vertex:Vert finalcolor:Fogged nofog
        #pragma target 3.0
        #pragma require 2darray
        #pragma multi_compile_local _ _VERTEX_COLOR
        #include "ModelLitSurface.hlsl"
        ENDCG
    }
}
