// A mesh model's edge fade (onejs-play's `scene.mesh({ edgeFade })`, ModelBridge.SetMeshEdgeFade):
// a card seen nearly edge on is dithered away, so a crown of leaf cards shows no dark lines where
// its cards turn side on to the eye. By the triangle's own face, not the normal the cart gave,
// which a crown bends out from its centre and so says nothing about which way a card lies.
//
// In the lit pass and the camera's depth passes alike, so SSAO's depth normals match what is drawn;
// not in the shadow caster, whose eye is the sun's. The face is worked out from the screen
// derivatives of the unswayed world position, the same in every pass, so the passes discard the
// same pixels. A branch on a material value, so every pixel of a draw takes the same way.
#ifndef ONEJS_MODEL_EDGE_FADE_INCLUDED
#define ONEJS_MODEL_EDGE_FADE_INCLUDED

// Discards a pixel of a face turned edge on: `edge` is where the fade starts and ends, in how
// squarely the face meets the eye (|N.V|, 0 edge on, 1 face on), each 0 for none. `pixel` is its
// place on the screen in pixels.
void ModelEdgeFadeClip(float3 positionOS, float2 pixel, float2 edge) {
    if (edge.y <= 0) return;
    float3 p = TransformObjectToWorld(positionOS);
    float3 face = normalize(cross(ddy(p), ddx(p)));
    float facing = abs(dot(face, GetWorldSpaceNormalizeViewDir(p)));
    float keep = saturate((facing - edge.x) / max(edge.y - edge.x, 1e-4));
    // Interleaved gradient noise, as ModelRangeClip's, kept off 0 and 1 so a face wholly kept keeps every pixel.
    float n = 0.002 + 0.996 * frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
    clip(keep - n);
}

#endif
