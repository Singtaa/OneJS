// UI Toolkit shader for the OneJS 2D particle engine (ParticleSystem2D).
//
// A thin wrapper over the engine-shipped UIE include: uie_std_vert/uie_std_frag
// are the exact entry points the built-in UIR shader compiles, so vertex
// streams, dynamic transforms, clip rects and opacity pages all behave
// identically to default UITK rendering. The single deliberate difference is
// the premultiplied blend state:
//
//     Blend One OneMinusSrcAlpha
//
// combined with CPU-premultiplied vertex tints written by ParticleSystem2D
//     tint.rgb = color.rgb * alpha
//     tint.a   = alpha * (1 - additiveness)
// this yields a per-particle continuum between normal alpha blending
// (additiveness 0) and pure additive (additiveness 1) in a single draw call.
//
// Particle textures must be premultiplied and must NOT be registered with
// TextureOptions.PremultipliedAlpha (uie_std_frag_texture would un-premultiply
// flagged textures). The built-in soft-disc texture satisfies this.
//
// The include resolves against the running editor's copy at compile time, so
// it is version-matched automatically. If a Unity upgrade renames the entry
// points this fails loudly at import; ParticleSystem2D detects the unusable
// shader and falls back to the default material (normal alpha blending).
Shader "OneJS/UIEParticles"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "isCustomUITKShader" = "true"
        }

        Pass
        {
            Cull Off
            ZWrite Off
            Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha

            CGPROGRAM
            #pragma target 3.5
            #pragma vertex uie_std_vert
            #pragma fragment uie_std_frag
            #pragma multi_compile_local _ _UIE_FORCE_GAMMA
            #pragma multi_compile_local _ _UIE_TEXTURE_SLOT_COUNT_4 _UIE_TEXTURE_SLOT_COUNT_2 _UIE_TEXTURE_SLOT_COUNT_1
            #pragma multi_compile_local _ _UIE_RENDER_TYPE_SOLID _UIE_RENDER_TYPE_TEXTURE _UIE_RENDER_TYPE_TEXT _UIE_RENDER_TYPE_GRADIENT
            #include "Internal/UnityUIE.cginc"
            ENDCG
        }
    }
}
