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
#include "OneJSFog.hlsl"
#include "OneJSSky.hlsl"

// The scene's sky, set by ModelBridge.ReflectSky while a cart asks for it to be reflected:
// _OjSkyReflect.x is 1 then and 0 otherwise. Globals, so no material or variant changes.
float4 _OjSkyReflect;
half4 _OjSkyTop, _OjSkyHorizon, _OjSkyBottom, _OjSkySunColor;
float4 _OjSkySunDir, _OjSkySunShape;

// What the sky adds to a lit surface over the ambient's flat average: URP's own environment term,
// with the sky along the reflected ray in place of _GlossyEnvironmentColor, weighted as URP weights
// it (EnvironmentBRDFSpecular) and darkened by the same occlusion. Diffuse, lights and shadows are
// left as UniversalFragmentPBR worked them out, and the fog follows.
half3 SkyReflection(InputData d, SurfaceData s) {
    BRDFData brdf;
    SurfaceData copy = s;
    InitializeBRDFData(copy, brdf);
    AmbientOcclusionFactor ao = CreateAmbientOcclusionFactor(d, s);
    float3 mirror = reflect(-d.viewDirectionWS, d.normalWS);
    // A rough surface's lobe leans from the mirror ray toward the normal (Frostbite's dominant
    // direction), since the half of it under the surface is hidden: a rough lake at a grazing
    // angle reflects the sky above it, not the colour below the horizon.
    float a = brdf.roughness;
    float lean = (1.0 - a) * (sqrt(1.0 - a) + a);
    float3 r = normalize(lerp(d.normalWS, mirror, lean));
    half fresnel = Pow4(1.0 - saturate(dot(d.normalWS, d.viewDirectionWS)));
    half3 sky = OneJSSkyReflected(r, brdf.perceptualRoughness, _OjSkyTop.rgb, _OjSkyHorizon.rgb, _OjSkyBottom.rgb,
        _OjSkySunColor.rgb, _OjSkySunDir, _OjSkySunShape.y);
    sky = lerp(sky, (half3)_OneJSFogColor.rgb, (half)OneJSFogSky(r, _WorldSpaceCameraPos));
    return (sky - _GlossyEnvironmentColor.rgb) * EnvironmentBRDFSpecular(brdf, fresnel) * ao.indirectAmbientOcclusion;
}
#ifndef MODEL_SPLAT
    #include "ModelSurface.hlsl"
    #include "ModelLeafLight.hlsl"

// The camera's depth texture, as URP's DeclareDepthTexture.hlsl names it, without the loose
// _CameraDepthTexture_TexelSize uniform it adds, which Vulkan's compiler refuses beside the SRP
// Batcher's buffers. Read at the pixel's own coordinates: it is the size of the camera's target.
TEXTURE2D_X_FLOAT(_CameraDepthTexture);

// A see-through mesh's base colour faded by the opaque depth behind it (ModelSurface.hlsl): the
// camera's depth texture, which ModelBridge asks for while a mesh with either option lives.
half4 ModelDepthFade(half4 base, float3 positionWS, float4 positionCS) {
    if (_DepthFade.x <= 0 && _DepthFade.y <= 0) return base;
    float raw = LOAD_TEXTURE2D_X(_CameraDepthTexture, uint2(positionCS.xy)).r;
    float ortho = unity_OrthoParams.w;
    float sceneEye = ModelSceneEyeDepth(raw, _ZBufferParams, _ProjectionParams, ortho);
    float pixelEye = -TransformWorldToView(positionWS).z;
    float thickness = ModelThickness(sceneEye, pixelEye, length(positionWS - _WorldSpaceCameraPos), ortho);
    return ModelDepthFadeBy(base, thickness, _DepthFade, _DeepColor);
}
#endif

#ifdef MODEL_SPLAT
// One splat layer: at the uvs, or with `perMetre` above 0 projected along each world axis at that
// many repeats a metre and blended by `planes` (SplatPlanes). The choice is a material value, so
// every pixel of a draw takes the same branch. All three projections are read, a plane SplatPlanes
// dropped at weight 0: a per-pixel branch that skipped its read compiled, for WebGL 2, to a compare
// mask in a float register, a NaN's bits a GPU may rewrite (ShaderWebGL2NaNTests). `farScale`
// (SplatFarScale) reads it coarser with distance: a bias of its log2 at the uvs, which keeps the
// hardware's own derivatives, and a factor on the gradients a projection already passes.
half4 SplatLayer(TEXTURE2D_PARAM(layer, layerSampler), float2 uv, float perMetre, float3 positionWS, float3 planes, float farScale) {
    if (perMetre <= 0) return SAMPLE_TEXTURE2D_BIAS(layer, layerSampler, uv, log2(farScale));
    float3 p = positionWS * perMetre;
    float3 dx = ddx(p) * farScale, dy = ddy(p) * farScale;
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
    // How far the vertex may sway, read only where the model sways (_Sway).
    float sway : TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings {
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    half3 normalWS : TEXCOORD2;
    half4 tangentWS : TEXCOORD3;
    float3 positionOS : TEXCOORD4;
    half3 vertexLight : TEXCOORD5;
    // The uv on the bake's sheet in xy, and the copy's draw range (CopyRange) in zw.
    float4 lightmapUVRange : TEXCOORD7;
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
    float2 range = CopyRange();
    float3 positionWS = KeptInRange(SwayedWorld(v.positionOS.xyz, v.sway), ModelRangeKeep(range));
    VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
    o.positionCS = TransformWorldToHClip(positionWS);
    o.positionWS = positionWS;
    o.normalWS = n.normalWS;
    o.tangentWS = half4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
    o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
    o.lightmapUVRange = float4(ModelLightmapUV(v.lightmapUV, MODEL_ACTOR_LIGHTMAP_ST), range);
    o.positionOS = v.positionOS.xyz;
    #ifdef _VERTEX_COLOR
        o.color = v.color;
    #endif
    #ifdef MODEL_SPLAT
        o.weights = v.weights;
    #endif
    #ifdef _ADDITIONAL_LIGHTS_VERTEX
        o.vertexLight = VertexLighting(positionWS, n.normalWS);
    #endif
    return o;
}

half4 Frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target {
    UNITY_SETUP_INSTANCE_ID(i);
    ModelRangeClip(i.lightmapUVRange.zw, i.positionCS.xy);
    #ifdef MODEL_SPLAT
        float3 planes = SplatPlanes(i.normalWS);
        float farScale = SplatFarScale(i.positionWS, _WorldSpaceCameraPos, _SplatFar.x);
        half3 mixed = SplatMix(
            SplatLayer(TEXTURE2D_ARGS(_SplatLayer0, sampler_SplatLayer0), i.uv, _SplatTriplanar.x, i.positionWS, planes, farScale),
            SplatLayer(TEXTURE2D_ARGS(_SplatLayer1, sampler_SplatLayer1), i.uv, _SplatTriplanar.y, i.positionWS, planes, farScale),
            SplatLayer(TEXTURE2D_ARGS(_SplatLayer2, sampler_SplatLayer2), i.uv, _SplatTriplanar.z, i.positionWS, planes, farScale),
            SplatLayer(TEXTURE2D_ARGS(_SplatLayer3, sampler_SplatLayer3), i.uv, _SplatTriplanar.w, i.positionWS, planes, farScale),
            i.weights, _HeightBlend);
        // A material value, so every pixel of a draw takes the same branch, and a ground without
        // variation pays nothing for the noise.
        if (_SplatFar.y > 0) mixed *= SplatVariation(i.positionWS, _SplatFar.y);
        half4 base = half4(mixed, 1) * _BaseColor;
    #else
        half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
    #endif
    #ifdef _VERTEX_COLOR
        base *= i.color;
    #endif
    base.rgb = Tinted(base.rgb);
    #ifndef MODEL_SPLAT
        base = ModelDepthFade(base, i.positionWS, i.positionCS);
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
    // OneJS's own fog (OneJSFog.hlsl), per pixel.
    half fog = OneJSFogAmount(i.positionWS, _WorldSpaceCameraPos, -TransformWorldToView(i.positionWS).z, _ProjectionParams.y);

    if (_Unlit > 0.5) {
        half3 flat = lerp(base.rgb + emission, (half3)_OneJSFogColor.rgb, fog);
        return half4(flat, OutputAlpha(base.a, IsSurfaceTypeTransparent(_Surface)));
    }

    SurfaceData s = (SurfaceData)0;
    s.albedo = base.rgb;
    s.alpha = base.a;
    s.emission = emission;
    // A double-sided material's back faces light as the side the viewer sees, or as the front
    // where the normals were bent for it (_KeepBackNormal).
    half3 normalWS = i.normalWS * IS_FRONT_VFACE(face, 1.0, 2.0 * _KeepBackNormal - 1.0);
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
        // A mesh model's own normal map (_NormalFlow.z) reads at its own repeats, sliding by the
        // scene's clock, in a frame worked out per pixel; a glTF model's, at the base map's uvs in
        // the frame its tangents give. One read either way.
        bool ownFrame = _NormalFlow.z > 0.5;
        float2 normalUV = ownFrame ? ModelNormalUV(i.uv, _BaseMap_ST, _NormalMap_ST, _NormalFlow.xy, _OjWindClock.x) : i.uv;
        s.normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, normalUV), _NormalScale);
        s.occlusion = lerp(1.0, SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, i.uv).r, _OcclusionStrength);
        float3 bitangent = i.tangentWS.w * cross(normalWS, i.tangentWS.xyz);
        half3x3 frame = half3x3(i.tangentWS.xyz, bitangent, normalWS);
        if (ownFrame) frame = ModelCotangentFrame(normalWS, i.positionWS, normalUV);
        d.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(s.normalTS, frame));
    #endif
    d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
    #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
        // A shadow coordinate past the far plane reads as unshadowed, on either depth convention.
        bool receives = _ReceiveShadows > 0.5 && MODEL_ACTOR_SHADOWLESS == 0;
        d.shadowCoord = receives ? TransformWorldToShadowCoord(i.positionWS) : float4(0, 0, UNITY_RAW_FAR_CLIP_VALUE, 0);
    #endif
    #ifdef _ADDITIONAL_LIGHTS_VERTEX
        d.vertexLighting = i.vertexLight;
    #endif
    d.bakedGI = MODEL_ACTOR_LIGHTMAP_ST.x > 0
        ? BakedLight(i.lightmapUVRange.xy)
        : SampleSH(d.normalWS);
    d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
    d.shadowMask = half4(1, 1, 1, 1);

    half4 color = UniversalFragmentPBR(d, s);
    // Every pixel of a frame takes the same way: off, color is what UniversalFragmentPBR gave.
    if (_OjSkyReflect.x > 0.5) color.rgb += SkyReflection(d, s);
    #ifndef MODEL_SPLAT
        // A material value, so every pixel of a draw takes the same way: a mesh that asks for
        // neither adds nothing to what UniversalFragmentPBR gave.
        if (_LeafLight.x > 0 || _LeafLight.y > 0) color.rgb += ModelLeafLight(d, s, (half2)_LeafLight.xy);
    #endif
    color.rgb = lerp(color.rgb, (half3)_OneJSFogColor.rgb, fog);
    color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent(_Surface));
    return color;
}

#endif
