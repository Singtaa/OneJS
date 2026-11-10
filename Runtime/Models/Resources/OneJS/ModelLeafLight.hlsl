// A mesh model's leaf light (onejs-play's `scene.mesh({ wrap, translucency })`,
// ModelBridge.SetMeshLeafLight), in ModelLit's lit pass in URP, after UniversalFragmentPBR and
// before the fog. Two terms of the sun's light that a plain surface does not get, added to what
// UniversalFragmentPBR worked out, so its lighting is not replaced and a mesh that asks for neither
// draws exactly as before:
//
// - wrap: the sun's diffuse light reaches past the shadow line, max(0, (N.L + w) / (1 + w)) in
//   place of max(0, N.L), as it does through a leaf, moss or skin. Added as the difference, so the
//   lit side keeps what it had (the two meet at N.L = 1) and the side turned away gains.
// - translucency: the sun showing through from behind, toward the eye, tinted by the surface's own
//   colour (its vertex colours too, so a crown's core darkened in them glows less) and by a tint: a
//   leaf held up to the light. The view against the sun's direction bent a little by the normal, to
//   a power, so it glows only near the line of the sun: 4 by default, tighter above.
//
// The sun only: point lights light a leaf as they light anything else. Its shadow and SSAO's
// direct occlusion come from URP's GetMainLight, the same as the lit pass's own; the shadow is read
// a second time here, since UniversalFragmentPBR does not hand its reading back.
#ifndef ONEJS_MODEL_LEAF_LIGHT_INCLUDED
#define ONEJS_MODEL_LEAF_LIGHT_INCLUDED

// How far the normal bends the sun's direction for the light through a leaf: 0 would show the sun
// only exactly behind it, more spreads it over the face.
#define MODEL_LEAF_THROUGH_BEND 0.3

// What wrap (`leaf.x`) and translucency (`leaf.y`), each 0 to 1, add to a pixel lit by
// UniversalFragmentPBR from `d` and `s`, the light through gathered to the power `leaf.z` and
// tinted by `tint`.
half3 ModelLeafLight(InputData d, SurfaceData s, float3 leaf, half3 tint) {
    BRDFData brdf;
    SurfaceData copy = s;
    InitializeBRDFData(copy, brdf);
    AmbientOcclusionFactor ao = CreateAmbientOcclusionFactor(d, s);
    Light sun = GetMainLight(d, d.shadowMask, ao);
    half3 light = sun.color * (sun.distanceAttenuation * sun.shadowAttenuation);
    half nl = dot(d.normalWS, sun.direction);
    half wrapped = saturate((nl + leaf.x) / (1.0 + leaf.x));
    half3 added = brdf.diffuse * light * (wrapped - saturate(nl));
    half3 bent = normalize(sun.direction + d.normalWS * MODEL_LEAF_THROUGH_BEND);
    half through = pow(saturate(dot(d.viewDirectionWS, -bent)), leaf.z);
    return added + s.albedo * tint * light * (through * leaf.y);
}

#endif
