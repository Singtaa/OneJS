// The fog ModelLit, GroundLit and the Sky draw, from three globals ModelFog sets before each frame
// (ModelFog.cs) rather than Unity's fog keywords: one variant draws every mode, and a build's fog
// stripping cannot take one away. Plain HLSL, so URP's passes, the built-in surface shader and
// the Sky's UnityCG pass all include it.
//
// Every branch here is on a global, so every pixel of a draw takes the same one: a per-pixel
// branch can compile, for WebGL 2, to a compare mask in a float register, a NaN's bits a GPU may
// rewrite (ShaderWebGL2NaNTests).
#ifndef ONEJS_FOG_INCLUDED
#define ONEJS_FOG_INCLUDED

// rgb: the fog's colour, linear in a linear project. a: 0 for no fog, 1 linear, 2 exp, 3 exp2.
float4 _OneJSFogColor;
// x, y: the linear ramp, the share of the surface left at z metres being z * x + y. z: where exp
// and exp2 start, metres along the ray. w: their density a metre.
float4 _OneJSFogParams;
// x: the height where the density is _OneJSFogParams.w. y: 1 over the metres a step of e in it
// takes, or 0 for a density the same at every height.
float4 _OneJSFogHeight;

// The optical depth `length` metres of the ray gathers from a point at height `y`, rising `rise`
// a metre, through a density that falls by e every 1 / _OneJSFogHeight.y metres above
// _OneJSFogHeight.x: the integral of density * exp(-(y + rise * t - base) * k) over t, in closed
// form. It is density * length * exp(-(y - base) * k) * (1 - exp(-x)) / x with x = rise * length * k,
// written as exp(max(0, -x)) * (1 - exp(-|x|)) / |x| so that no exponent overflows: |x| is kept
// off 0 (a level ray, where the fraction is 1) and the exponent under 80, so a stretch of no
// length times a density past a float's range is 0, never a NaN.
float OneJSFogHeightDepth(float y, float rise, float length) {
    float k = _OneJSFogHeight.y;
    float x = rise * length * k;
    float w = max(abs(x), 1e-3);
    float spread = (1 - exp(-w)) / w;
    float thinned = exp(min(max(0, -x) - (y - _OneJSFogHeight.x) * k, 80));
    return _OneJSFogParams.w * length * thinned * spread;
}

// The share of fog past `start` metres along a ray of `length` metres from `cameraWS` in `dir`
// (unit), for exp and exp2.
float OneJSFogLaw(float3 cameraWS, float3 dir, float start, float length) {
    float tau = _OneJSFogHeight.y > 0
        ? OneJSFogHeightDepth(cameraWS.y + dir.y * start, dir.y, length)
        : _OneJSFogParams.w * length;
    if (_OneJSFogColor.a > 2.5) tau *= tau;
    return 1 - exp(-tau);
}

// The share of the fog's colour a surface at `positionWS` takes, from 0 to 1, seen from
// `cameraWS` with `eyeDepth` metres between them along the camera's view. Linear fog is URP's
// own, per pixel, so a scene drawn with it before draws the same: the ramp over the eye depth
// past the near plane. Exp and exp2 measure along the pixel's ray from _OneJSFogParams.z, so
// turning the camera never changes how fogged a thing is.
float OneJSFogAmount(float3 positionWS, float3 cameraWS, float eyeDepth, float nearPlane) {
    if (_OneJSFogColor.a < 0.5) return 0;
    if (_OneJSFogColor.a < 1.5) return 1 - saturate(max(eyeDepth - nearPlane, 0) * _OneJSFogParams.x + _OneJSFogParams.y);
    float3 ray = positionWS - cameraWS;
    float distance = length(ray);
    float start = min(_OneJSFogParams.z, distance);
    return OneJSFogLaw(cameraWS, ray / max(distance, 1e-6), start, distance - start);
}

// The share of fog in the sky along `dir` (unit) from `cameraWS`: with height fog, the haze
// along the ray to infinity, which thins as the ray climbs and is all of it at and below the
// horizon, so the land and the sky meet in one colour. Any other fog is a band at the horizon,
// as the sky drew it before there was height fog: a density the same everywhere would hide
// the whole sky.
float OneJSFogSky(float3 dir, float3 cameraWS) {
    if (_OneJSFogColor.a < 0.5) return 0;
    if (_OneJSFogColor.a > 1.5 && _OneJSFogHeight.y > 0) return OneJSFogLaw(cameraWS, dir, _OneJSFogParams.z, 1e6);
    return 1 - smoothstep(0.0, 0.25, abs(dir.y));
}

#endif
