// ModelLit for ground painted from up to four tiling layers (onejs-play's `scene.mesh({ splat })`,
// ModelBridge.Ground.cs): each layer a colour texture with its height in alpha, mixed by four
// weights a vertex in TEXCOORD2, times the vertex colours. Lit, shadowed, baked, fogged and
// dissolved by ModelLit's own pass code, which it includes with MODEL_SPLAT defined.
//
// A shader of its own rather than a keyword on ModelLit: a mesh model's material is made at run
// time, after a build's stripping has decided, so a shader_feature would be stripped and the splat
// would draw white, and a multi_compile would double every variant of ModelLit in every build. This
// one costs its variants once, and needs no keyword at all.
Shader "OneJS/GroundLit" {
    Properties {
        // Never sampled for colour: its scale and offset are the layers' tiling, and its white is
        // what the depth passes and the bake read for alpha and albedo.
        _BaseMap ("Tiling", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _SplatLayer0 ("Layer 0 (RGB) Height (A)", 2D) = "white" {}
        _SplatLayer1 ("Layer 1 (RGB) Height (A)", 2D) = "white" {}
        _SplatLayer2 ("Layer 2 (RGB) Height (A)", 2D) = "white" {}
        _SplatLayer3 ("Layer 3 (RGB) Height (A)", 2D) = "white" {}
        _HeightBlend ("Height Blend", Range(0, 1)) = 0
        _SplatTriplanar ("Triplanar (repeats a metre, 0 reads the uvs)", Vector) = (0, 0, 0, 0)
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Roughness ("Roughness", Range(0, 1)) = 1
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
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        // A mesh model's sway (ModelWind.hlsl) and two-sided lighting: see ModelLitURP.hlsl.
        [HideInInspector] _Sway ("Sway", Float) = 0
        [HideInInspector] _KeepBackNormal ("Keep Back Normal", Float) = 0
    }

    SubShader {
        PackageRequirements {
            "com.unity.render-pipelines.universal"
            "com.unity.cloud.gltfast"
        }
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #define MODEL_SPLAT 1
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
            // Nine interpolators: ModelLit's eight and the weights.
            #pragma target 3.5
            #pragma require 2darray
            #pragma vertex Vert
            #pragma fragment Frag
            // ModelLit's lighting keywords (see ModelLit.shader).
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            // Each instance's renderer values, for MODEL_ACTOR_SHADOWLESS (ModelLitURP.hlsl).
            #pragma instancing_options renderinglayer
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

    // The built-in pipeline, through ModelLit's surface shader (ModelLitSurface.hlsl).
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
        #pragma target 3.5
        #pragma require 2darray
        #define MODEL_SPLAT 1
        #include "ModelLitSurface.hlsl"
        ENDCG
    }
}
