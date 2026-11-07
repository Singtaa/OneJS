// Where ModelLit reads the baked light, in both pipelines' SubShaders (ModelBridge.Light.cs). A
// rectangle's uv scale and offset are in pages of the bake's sheet (Lighting/LightSheet.cs): the
// first page runs 0 to 1, and a second column or row starts 2 texels before the first ends, so the
// texels either side of the split are on both pages and a bilinear read finds all it needs on one.
#ifndef ONEJS_MODEL_LIGHTMAP_INCLUDED
#define ONEJS_MODEL_LIGHTMAP_INCLUDED

// From xy on, in pages, a read takes the second column and row, which start at zw: LightSheet.Seams.
// Past any uv when there is no second, so one page reads as it did before pages.
float4 _OjLightmapSeams;

// A rectangle's uv on the sheet, in pages.
float2 ModelLightmapUV(float2 uv, float4 st) {
    return uv * st.xy + st.zw;
}

// A uv on the sheet as the uv on its page, and the page, numbered across then down. Per pixel, so a
// rectangle may cross a seam.
float3 ModelLightmapPage(float2 at) {
    float2 second = step(_OjLightmapSeams.xy, at);
    return float3(at - second * _OjLightmapSeams.zw, second.x + 2.0 * second.y);
}

#endif
