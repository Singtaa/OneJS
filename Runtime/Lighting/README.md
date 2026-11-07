# Lighting

Baked light at runtime, for a game that cannot use Unity's lightmapper because
it runs in a player: OJPlay's `scene.bake` (onejs-play, `src/light/`), and any
OneJS project that bakes a scene it builds as it runs. The design and its
measurements are `PlaySite/docs/lightmapping.md` in the container repo.

| File | Contents |
|---|---|
| `LightBaker.cs` | `BakeScene` (what is baked), `BakeSettings`, and `LightBaker`, which bakes it a slice at a time |
| `Bvh.cs` | The triangle hierarchy the trace walks, built on the CPU |
| `ProbeSolve.cs` | The probe pass's sums as `SphericalHarmonicsL2` coefficients, and probes inside solids filled from their neighbours |
| `TextureRead.cs` | A float or half-float render texture read back to the CPU: `AsyncGPUReadback` where the GPU has it, landing a frame or more later, and `ReadPixels` where it does not, landing at once. Both browser backends have it |
| `LightFile.cs` | A finished bake as one PNG and back: the lightmap as 8-bit RGBM over a range of 8, and under it the probes, each float as its four bytes in one pixel, so they read back exactly |
| `Resources/OneJS/LightBake.shader` | The passes: G-buffer, lightmap trace, probe trace, resolve, dilation, denoise |

## What it bakes

The static triangles arrive in world space, already laid out in one atlas (the
unwrap is onejs-play's `src/light/unwrap.ts`). Each triangle receives light and
blocks and bounces it. The bake is:

- **the sky and the ground** of the ambient, as two hemispheres of constant
  radiance. That is exactly the light `ModelBridge.HemisphereProbe` gives an
  open surface (its irradiance is the average plus half the difference times
  the normal's height), so a floor with nothing over it bakes to what it shows
  unbaked, and baking changes where light lands, never how much there is;
- **the sun's bounce**: every hit after the first. The sun's first hit stays
  live, with its shadow map, so a game can move its sun without its shadows
  going wrong;
- two bounces, in ModelLit's units: a sun of colour C lights a surface facing
  it C, and a surface of albedo A lit L sends A L on.

**Probes** sit on a grid and hold the light arriving there, from every
direction, as `SphericalHarmonicsL2` holds the ambient probe: ready for a
moving actor's shader to read in place of the one ambient probe. A probe most
of whose rays meet the back of a face is inside a solid and takes its
neighbours' mean, filled in from outside a ring at a time.

## How it runs

Fragment passes, not compute, so WebGL 2 runs them. The G-buffer is drawn once;
the probes are traced first, so moving things are lit within a few frames; then
the lightmap, a tile at a time, as a running mean blended into a half-float
target (no ping-pong, so a tile writes only its own texels). A draft is finished
at `DraftSamples` and the final at `Samples`, each by resolve, four steps of
dilation and an edge-aware denoise of the whole map. `Step(paths)` does about
that many paths, at least one draw; `RunToEnd` bakes everything, waiting on the
GPU between submissions so none grows long enough to trip a watchdog. A bake is
`Done` once its lightmap is finished and its probes are read back, which on
WebGPU lands a frame or more after the last probe pass: until then `Step` keeps
tracing the lightmap, and once that is finished it only looks for the read.

A GPU needs float render targets (positions in half floats are centimetres off
at a hundred metres), half-float ones, four targets at once and float textures.
`LightBaker.Supported` says which is missing; a game without them keeps its flat
ambient.

Memory while baking: 72 bytes a texel (positions and normals in float, the sum,
two work targets, the draft and the final in half), 19 MB at 512 by 512. The
draft and the final stay; the rest is freed at the end.

## Who calls it

`ModelBridge.Light.cs` (in `Runtime/Models`) is the one caller in OneJS: it builds the `BakeScene`
from the scene's static actors and scattered copies, steps the baker a frame at a time for
onejs-play's `scene.bake`, and puts the result on the models. It also writes a finished bake as a
light file (`LightFile`) and shows one instead of baking (`LoadLight`), which is how a cart's saved
light reaches a player without a bake on their device. The baker disposes its textures with
`DestroyImmediate` in the editor's edit mode, since a bake runs in the edit-mode preview too.

## Traps

- **WebGPU cannot `ReadPixels`.** Unity 6's `Texture2D.ReadPixels` there logs
  "Texture Readback is not supported by WebGPU" and reads black, so every read
  of the bake goes through `TextureRead`, which uses `AsyncGPUReadback` wherever
  `SystemInfo.supportsAsyncGPUReadback` holds. Its reads land later, so nothing
  that reads back can answer in the call that started it: the probes, the light
  file and a texture's albedo mean all wait. WebGL 2 reports async readback
  too, served by Unity through a pixel pack buffer, so both browser backends
  take that path. `TextureRead.UseReadPixels` and `Hold` let a test take the
  `ReadPixels` path, and hold a read as a slow GPU would.
- **No int sentinel through a float on WebGL 2.** FXC keeps ints in typeless
  registers, and HLSLcc's GLSL moves them through floats by their bits. The
  trace's "no hit" of -1 is a NaN pattern there, which the GPU rewrote, so every
  ray counted as a hit and the bake was black while WebGPU's was lit. The hit,
  the node index, the traversal stack, `incoming`'s `inside` (a bool is an
  all-ones mask) and `testTriangles`' counter are float values for that
  reason: a small int's bits are a denormal, which a GPU may flush to 0. Pass 4
  multiplies by its coverage rather than choosing, since HLSLcc moves a chosen
  texel through an int register. `ShaderWebGL2NaNTests` (`Tests/Editor`) fails
  on a NaN pattern, or a computed int moved into a float by its bits, in any
  variant's WebGL 2 GLSL, for every shader in the package.

- **xatlas centres a chart's border texels on its edges.** A floor texel at the
  foot of a wall then sits on the wall's plane and its rays cross the wall at
  distance 0, letting the sky in along every corner. The G-buffer moves each
  texel 5 mm towards its triangle's centre.
- **Which row is which.** Only the G-buffer places anything, at its uv, flipped
  where `UNITY_UV_STARTS_AT_TOP`, so a texture read at that uv finds it. Every
  other pass reads and writes its own pixel. The probes are read back by the
  index each carries, never by row, since a read-back's row order is the one
  thing platforms disagree on. `LightBakerTests` checks the orientation by
  sampling the lightmap as a model does.
