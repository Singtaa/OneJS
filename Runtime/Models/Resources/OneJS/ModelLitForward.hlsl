// ModelLit's and GroundLit's lit pass in URP, after ModelLitURP.hlsl. The shader file declares the
// pass's pragmas; this is its code. Under MODEL_SPLAT (GroundLit) the base colour is the splat's
// layers mixed by the vertex's weights (TEXCOORD2), the vertex colours are always read, and the
// glTF maps a ground has none of are not sampled: it is a rough non-metal, lit by its vertex normal.
#ifndef ONEJS_MODEL_LIT_FORWARD_INCLUDED
#define ONEJS_MODEL_LIT_FORWARD_INCLUDED

#ifdef MODEL_SPLAT
    #include "ModelSplat.hlsl"
    // A splat mesh always carries colours: ModelBridge fills them white when a cart gives none.
    #ifndef _VERTEX_COLOR
        #define _VERTEX_COLOR 1
    #endif
#endif
#define _ENVIRONMENTREFLECTIONS_OFF 1

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

#ifdef MODEL_SPLAT
// One splat layer: at the uvs, or with `perMetre` above 0 projected along each world axis at that
// many repeats a metre and blended by `planes` (SplatPlanes). The choice is a material value, so
// every pixel of a draw takes the same branch. All three projections are read, a plane SplatPlanes
// dropped at weight 0: a per-pixel branch that skipped its read compiled, for WebGL 2, to a compare
// mask in a float register, a NaN's bits a GPU may rewrite (ShaderWebGL2NaNTests).
half4 SplatLayer(TEXTURE2D_PARAM(layer, layerSampler), float2 uv, float perMetre, float3 positionWS, float3 planes) {
    if (perMetre <= 0) return SAMPLE_TEXTURE2D(layer, layerSampler, uv);
    float3 p = positionWS * perMetre;
    float3 dx = ddx(p), dy = ddy(p);
    return SAMPLE_TEXTURE2D_GRAD(layer, layerSampler, p.zy, dx.zy, dy.zy) * planes.x
        + SAMPLE_TEXTURE2D_GRAD(layer, layerSampler, p.xz, dx.xz, dy.xz) * planes.y
        + SAMPLE_TEXTURE2D_GRAD(layer, layerSampler, p.xy, dx.xy, dy.xy) * planes.z;
}
#endif

struct Attributes {
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    float2 lightmapUV : TEXCOORD1;
    #ifdef _VERTEX_COLOR
        float4 color : COLOR;
    #endif
    #ifdef MODEL_SPLAT
        float4 weights : TEXCOORD2;
    #endif
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
    float2 lightmapUV : TEXCOORD7;
    #ifdef _VERTEX_COLOR
        half4 color : TEXCOORD6;
    #endif
    #ifdef MODEL_SPLAT
        half4 weights : TEXCOORD8;
    #endif
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
    o.lightmapUV = ModelLightmapUV(v.lightmapUV, MODEL_ACTOR_LIGHTMAP_ST);
    o.positionOS = v.positionOS.xyz;
    #ifdef _VERTEX_COLOR
        o.color = v.color;
    #endif
    #ifdef MODEL_SPLAT
        o.weights = v.weights;
    #endif
    o.fogAndVertexLight.x = ComputeFogFactor(p.positionCS.z);
    #ifdef _ADDITIONAL_LIGHTS_VERTEX
        o.fogAndVertexLight.yzw = VertexLighting(p.positionWS, n.normalWS);
    #endif
    return o;
}

half4 Frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target {
    UNITY_SETUP_INSTANCE_ID(i);
    #ifdef MODEL_SPLAT
        float3 planes = SplatPlanes(i.normalWS);
        half3 mixed = SplatMix(
            SplatLayer(TEXTURE2D_ARGS(_SplatLayer0, sampler_SplatLayer0), i.uv, _SplatTriplanar.x, i.positionWS, planes),
            SplatLayer(TEXTURE2D_ARGS(_SplatLayer1, sampler_SplatLayer1), i.uv, _SplatTriplanar.y, i.positionWS, planes),
            SplatLayer(TEXTURE2D_ARGS(_SplatLayer2, sampler_SplatLayer2), i.uv, _SplatTriplanar.z, i.positionWS, planes),
            SplatLayer(TEXTURE2D_ARGS(_SplatLayer3, sampler_SplatLayer3), i.uv, _SplatTriplanar.w, i.positionWS, planes),
            i.weights, _HeightBlend);
        half4 base = half4(mixed, 1) * _BaseColor;
    #else
        half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
    #endif
    #ifdef _VERTEX_COLOR
        base *= i.color;
    #endif
    float cut = ModelClip(i.positionOS, base.a, _AlphaClip, _Cutoff, Dissolved(), _DissolveScale);
    #ifdef MODEL_SPLAT
        half3 emission = ModelEdge(cut, _DissolveEdge.rgb);
    #else
        half3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb * _EmissionColor.rgb
            + ModelEdge(cut, _DissolveEdge.rgb);
    #endif

    InputData d = (InputData)0;
    d.positionWS = i.positionWS;
    d.fogCoord = InitializeInputDataFog(float4(i.positionWS, 1.0), i.fogAndVertexLight.x);

    if (_Unlit > 0.5) {
        half3 flat = MixFog(base.rgb + emission, d.fogCoord);
        return half4(flat, OutputAlpha(base.a, IsSurfaceTypeTransparent(_Surface)));
    }

    SurfaceData s = (SurfaceData)0;
    s.albedo = base.rgb;
    s.alpha = base.a;
    s.emission = emission;
    // A double-sided material's back faces light as the side the viewer sees.
    half3 normalWS = i.normalWS * IS_FRONT_VFACE(face, 1.0, -1.0);
    #ifdef MODEL_SPLAT
        s.metallic = _Metallic;
        s.smoothness = 1.0 - _Roughness;
        s.occlusion = 1.0;
        s.normalTS = half3(0, 0, 1);
        d.normalWS = NormalizeNormalPerPixel(normalWS);
    #else
        half4 mr = SAMPLE_TEXTURE2D(_MetallicRoughnessMap, sampler_MetallicRoughnessMap, i.uv);
        s.metallic = mr.b * _Metallic;
        // glTF roughness is perceptual roughness, which is what URP's smoothness inverts.
        s.smoothness = 1.0 - mr.g * _Roughness;
        s.normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv), _NormalScale);
        s.occlusion = lerp(1.0, SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, i.uv).r, _OcclusionStrength);
        float3 bitangent = i.tangentWS.w * cross(normalWS, i.tangentWS.xyz);
        d.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(s.normalTS, half3x3(i.tangentWS.xyz, bitangent, normalWS)));
    #endif
    d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
    #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
        // A shadow coordinate past the far plane reads as unshadowed, on either depth convention.
        bool receives = _ReceiveShadows > 0.5 && MODEL_ACTOR_SHADOWLESS < 0.5;
        d.shadowCoord = receives ? TransformWorldToShadowCoord(i.positionWS) : float4(0, 0, UNITY_RAW_FAR_CLIP_VALUE, 0);
    #endif
    #ifdef _ADDITIONAL_LIGHTS_VERTEX
        d.vertexLighting = i.fogAndVertexLight.yzw;
    #endif
    d.bakedGI = MODEL_ACTOR_LIGHTMAP_ST.x > 0
        ? BakedLight(i.lightmapUV)
        : SampleSH(d.normalWS);
    d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
    d.shadowMask = half4(1, 1, 1, 1);

    half4 color = UniversalFragmentPBR(d, s);
    color.rgb = MixFog(color.rgb, d.fogCoord);
    color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent(_Surface));
    return color;
}

#endif
