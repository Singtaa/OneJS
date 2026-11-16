// A scattered copy (_SCATTER, on the materials ModelBridge.Scatter draws with): drawn by
// Graphics.RenderMeshPrimitives, one draw a chunk, part and submesh, with no per-instance arrays at
// all. Each copy's place, tint and baked light's rectangle are written once, when the scatter is
// made, into its scatter's copy texture, and a vertex reads its copy's by SV_InstanceID past the
// draw's first record. RenderMeshInstanced, which drew copies before, uploads its arrays at their
// whole capacity on every draw of every pass, 128 copies a draw on WebGPU: 12 KB a draw of 128
// copies whether or not anything moved (PrimoEden, 16 Nov 2026).
//
// A texture rather than a storage buffer, as the skin's palette is (ModelSkin.hlsl): the same
// variant runs on WebGL 2, which has no storage buffers, and on a WebGPU device that allows none in
// the vertex stage.
//
// Everything else reaches the world through these functions in place of URP's own, which read the
// draw's matrix: a draw with no renderer has none worth reading.
#ifndef ONEJS_MODEL_SCATTER_COPY_INCLUDED
#define ONEJS_MODEL_SCATTER_COPY_INCLUDED

#ifdef MODEL_SCATTERED
    // ModelBridge.CopyTextureWide and ModelBridge.CopyTexels: a record is a copy as one part draws
    // it, its matrix's three rows (the part's own place in the model applied), its tint (linear,
    // its alpha 1, or 0 for none) and its rectangle of the baked light's atlas (0 for none).
    #define MODEL_COPY_WIDE 1024u
    #define MODEL_COPY_TEXELS 5u

    TEXTURE2D_FLOAT(_OjScatterCopies);
    // The draw's first record, a whole number.
    float _OjScatterBase;

    static float4x4 _OjCopyMatrix;
    static float4 _OjCopyTint;
    static float4 _OjCopyLightmapST;

    float4 ModelCopyTexel(uint t) {
        return LOAD_TEXTURE2D_LOD(_OjScatterCopies, int2(t % MODEL_COPY_WIDE, t / MODEL_COPY_WIDE), 0);
    }

    // Reads the copy `instance` of the draw, in each stage that reads it. A stage that uses none of
    // the values loads none of them: the compiler drops what is never read.
    void ModelCopySetup(uint instance) {
        uint t = ((uint)_OjScatterBase + instance) * MODEL_COPY_TEXELS;
        _OjCopyMatrix = float4x4(ModelCopyTexel(t), ModelCopyTexel(t + 1u), ModelCopyTexel(t + 2u), float4(0, 0, 0, 1));
        _OjCopyTint = ModelCopyTexel(t + 3u);
        _OjCopyLightmapST = ModelCopyTexel(t + 4u);
    }

    #define MODEL_OBJECT_TO_WORLD _OjCopyMatrix
    #define MODEL_COPY_INPUT uint copyID : SV_InstanceID;
    #define MODEL_COPY_VARYING nointerpolation uint copyID : TEXCOORD9;
    #define MODEL_COPY_SETUP_VERTEX(v, o) ModelCopySetup(v.copyID); o.copyID = v.copyID;
    #define MODEL_COPY_SETUP(i) ModelCopySetup(i.copyID);

    float3 ModelObjectToWorld(float3 positionOS) {
        return mul(_OjCopyMatrix, float4(positionOS, 1)).xyz;
    }

    // A copy scales alike on every axis (ModelBridge.ScaledAlike), so its normals turn with its matrix.
    float3 ModelObjectToWorldDir(float3 dirOS) {
        return SafeNormalize(mul((float3x3)_OjCopyMatrix, dirOS));
    }

    float3 ModelObjectToWorldNormal(float3 normalOS) {
        return ModelObjectToWorldDir(normalOS);
    }

    real ModelOddNegativeScale() {
        return determinant((float3x3)_OjCopyMatrix) < 0 ? -1 : 1;
    }
#else
    #define MODEL_OBJECT_TO_WORLD GetObjectToWorldMatrix()
    #define MODEL_COPY_INPUT
    #define MODEL_COPY_VARYING
    #define MODEL_COPY_SETUP_VERTEX(v, o)
    #define MODEL_COPY_SETUP(i)

    float3 ModelObjectToWorld(float3 positionOS) {
        return TransformObjectToWorld(positionOS);
    }

    float3 ModelObjectToWorldDir(float3 dirOS) {
        return TransformObjectToWorldDir(dirOS);
    }

    float3 ModelObjectToWorldNormal(float3 normalOS) {
        return TransformObjectToWorldNormal(normalOS);
    }

    real ModelOddNegativeScale() {
        return GetOddNegativeScale();
    }
#endif

// URP's GetVertexNormalInputs, through the copy's matrix where there is one.
VertexNormalInputs ModelNormalInputs(float3 normalOS, float4 tangentOS) {
    VertexNormalInputs n;
    n.normalWS = ModelObjectToWorldNormal(normalOS);
    n.tangentWS = ModelObjectToWorldDir(tangentOS.xyz);
    n.bitangentWS = cross(n.normalWS, n.tangentWS) * (tangentOS.w * ModelOddNegativeScale());
    return n;
}

#endif
