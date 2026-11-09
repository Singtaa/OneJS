// ModelLit's and GroundLit's lighting in URP: URP's UniversalFragmentPBR (Lighting.hlsl), with the
// ground's shade laid on the sun's shadow. After Lighting.hlsl and ModelLitURP.hlsl.
//
// The shade (onejs-play's `scene.mesh({ shade })`, ModelBridge.Shade.cs) is the baked term of URP's
// own mix of realtime and baked shadows, MixRealtimeAndBakedShadows under LIGHTMAP_SHADOW_MIXING:
// min(lerp(realtime, 1, fade), baked). GetMainLight's shadowAttenuation is already the realtime
// shadow faded out toward the shadow distance, so taking the darker of it and the shade is the
// whole mix: a spot both shaded and in a realtime shadow darkens once, and past the shadow
// distance the shade carries on alone. It dims the sun's light alone, as a realtime shadow does,
// so a shaded spot keeps its ambient and point lights, and the two kinds of shadow look the same
// where one hands over to the other.
//
// URP has no hook between GetMainLight and the BRDF, and its own baked term comes from a lightmap
// or a probe, never a texture of a mesh's own; so this is a copy of UniversalFragmentPBR. URP
// 17.3, 17.5 and 17.6 differ in it only by Meta Quest branches, which this leaves out. Lighting
// .hlsl's copy is held to the versions it was compared with by ModelLightingCopyTests, which
// fails on any other, naming this file: compare the two, carry the change here, and add the hash.
#ifndef ONEJS_MODEL_LIGHTING_INCLUDED
#define ONEJS_MODEL_LIGHTING_INCLUDED

// How much of the sun reaches a pixel by the mesh's shade: 1 where it has none. `uv` is the mesh's
// own, before tiling. The shade's samples sit on the corners of its grid, as vertices do, so its
// first and last rows land on the mesh's edges and a chunk's edge reads what its neighbour's does.
half SunlitByShade(float2 uv) {
    float4 texel = _ShadeMap_TexelSize;
    float2 at = uv * (1.0 - texel.xy) + 0.5 * texel.xy;
    return 1.0 - SAMPLE_TEXTURE2D(_ShadeMap, sampler_ShadeMap, at).r;
}

// UniversalFragmentPBR, with the sun's shadow the darker of its own and `sunlit`.
half4 ModelFragmentPBR(InputData inputData, SurfaceData surfaceData, half sunlit)
{
    #if defined(_SPECULARHIGHLIGHTS_OFF)
    bool specularHighlightsOff = true;
    #else
    bool specularHighlightsOff = false;
    #endif
    BRDFData brdfData;

    // NOTE: can modify "surfaceData"...
    InitializeBRDFData(surfaceData, brdfData);

    #if defined(DEBUG_DISPLAY)
    half4 debugColor;

    if (CanDebugOverrideOutputColor(inputData, surfaceData, brdfData, debugColor))
    {
        return debugColor;
    }
    #endif

    // Clear-coat calculation...
    BRDFData brdfDataClearCoat = CreateClearCoatBRDFData(surfaceData, brdfData);
    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData, surfaceData);
    uint meshRenderingLayers = GetMeshRenderingLayer();
    Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);
    // OneJS: the ground's shade, by min (see the top of this file).
    mainLight.shadowAttenuation = min(mainLight.shadowAttenuation, sunlit);

    // NOTE: We don't apply AO to the GI here because it's done in the lighting calculation below...
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);

    LightingData lightingData = CreateLightingData(inputData, surfaceData);

    lightingData.giColor = GlobalIllumination(brdfData, brdfDataClearCoat, surfaceData.clearCoatMask,
                                              inputData.bakedGI, aoFactor.indirectAmbientOcclusion, inputData.positionWS,
                                              inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);
#ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
#endif
    {
        lightingData.mainLightColor = LightingPhysicallyBased(brdfData, brdfDataClearCoat,
                                                              mainLight,
                                                              inputData.normalWS, inputData.viewDirectionWS,
                                                              surfaceData.clearCoatMask, specularHighlightsOff);
    }

    #if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();

    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK

        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);

#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            lightingData.additionalLightsColor += LightingPhysicallyBased(brdfData, brdfDataClearCoat, light,
                                                                          inputData.normalWS, inputData.viewDirectionWS,
                                                                          surfaceData.clearCoatMask, specularHighlightsOff);
        }
    }
    #endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);

#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            lightingData.additionalLightsColor += LightingPhysicallyBased(brdfData, brdfDataClearCoat, light,
                                                                          inputData.normalWS, inputData.viewDirectionWS,
                                                                          surfaceData.clearCoatMask, specularHighlightsOff);
        }
    LIGHT_LOOP_END
    #endif

    #if defined(_ADDITIONAL_LIGHTS_VERTEX)
    lightingData.vertexLightingColor += inputData.vertexLighting * brdfData.diffuse;
    #endif

#if REAL_IS_HALF
    // Clamp any half.inf+ to HALF_MAX
    return min(CalculateFinalColor(lightingData, surfaceData.alpha), HALF_MAX);
#else
    return CalculateFinalColor(lightingData, surfaceData.alpha);
#endif
}

#endif
