// An actor's skin on the graphics card (ModelBridge.GpuSkin.cs): each vertex moved by up to four
// joints from the joint palette, in every pass of ModelLit, so a skinned actor's shadow and depth
// are posed as its colour is. The palette is a float texture, PaletteWide texels a row, three
// texels a joint (the rows of its 3x4 matrix, from the renderer's space to the same space posed),
// and where a renderer's joints start rides in its user value above the colour row.
#ifndef ONEJS_MODEL_SKIN_INCLUDED
#define ONEJS_MODEL_SKIN_INCLUDED

#ifdef _OJ_SKIN
    // ModelBridge.PaletteWide and ModelBridge.SkinBaseShift.
    #define MODEL_PALETTE_WIDE 1024u
    #define MODEL_SKIN_BASE_SHIFT 14u

    TEXTURE2D_FLOAT(_OjSkinPalette);

    float4 ModelPaletteTexel(uint t) {
        return LOAD_TEXTURE2D_LOD(_OjSkinPalette, int2(t % MODEL_PALETTE_WIDE, t / MODEL_PALETTE_WIDE), 0);
    }

    float3x4 ModelJoint(uint joint) {
        uint t = joint * 3u;
        return float3x4(ModelPaletteTexel(t), ModelPaletteTexel(t + 1u), ModelPaletteTexel(t + 2u));
    }

    // A mesh with fewer than four joints a vertex leaves the weights' missing components 0 and
    // the fourth 1, as any vertex input does, so weights summing past one mean the fourth is not
    // there. A file's weights sum to one.
    void ModelSkinned(float4 weights, uint4 joints, inout float3 positionOS, inout float3 normalOS, inout float3 tangentOS) {
        if (weights.x + weights.y + weights.z + weights.w > 1.5) weights.w = 0;
        uint base = unity_RendererUserValue >> MODEL_SKIN_BASE_SHIFT;
        float3x4 m = ModelJoint(base + joints.x) * weights.x
            + ModelJoint(base + joints.y) * weights.y
            + ModelJoint(base + joints.z) * weights.z
            + ModelJoint(base + joints.w) * weights.w;
        positionOS = mul(m, float4(positionOS, 1));
        normalOS = SafeNormalize(mul((float3x3)m, normalOS));
        tangentOS = SafeNormalize(mul((float3x3)m, tangentOS));
    }
#endif

#endif
