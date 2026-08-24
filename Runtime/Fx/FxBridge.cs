using System;
using System.Collections.Generic;
using UnityEngine;

namespace OneJS.Fx {
    /// <summary>
    /// Runs an image operation chain described from JavaScript.
    ///
    /// The chain crosses once as a flat float buffer, the same __csArray path
    /// PainterBridge uses, and this replays it with direct typed calls. Spark2D
    /// dispatched per operation instead, so a four step chain cost four full
    /// screen passes and roughly twelve reflection crossings.
    ///
    /// Per pixel operations fuse: a run of them becomes one blit through
    /// OneJS/FxOps, up to MaxFusedOps at a time. Fusion is bounded rather than
    /// unlimited because the chain has to be *data* for a bounded shader loop;
    /// player builds cannot compile shaders at runtime.
    ///
    /// Wire contract: JSModules/onejs-unity/src/fx/ops.ts. Change both together.
    /// </summary>
    public static class FxBridge {
        public const int WireVersion = 1;

        /// <summary>Must match MAX_OPS in OneJS/FxOps.shader and MAX_FUSED_OPS in ops.ts.</summary>
        public const int MaxFusedOps = 16;

        // Opcodes. Sources are below FirstPixelOp, everything at or above it is
        // per pixel and therefore fusable.
        const int OpSourceTexture = 0;
        const int OpSourceColor = 1;
        const int OpSourceNoise = 2;
        const int OpSourceGradient = 3;
        const int OpSourceSdf = 4;
        const int FirstPixelOp = 16;
        const int OpBlend = 96;
        const int OpRamp = 87;
        // Spatial ops change uv before the sample, so they can never fold into a
        // fused run and always take a pass of their own.
        const int FirstSpatialOp = 112;
        const int OpTransform = 112;
        const int OpTile = 113;
        const int OpFlip = 114;
        const int OpCrop = 115;
        // Neighbourhood filters read many pixels, so like the spatial ops they
        // cannot fuse. Unlike them, one of these can be several passes.
        const int FirstFilterOp = 128;
        const int OpBlur = 128;
        const int OpSharpen = 129;
        const int OpEdge = 130;
        const int OpDilate = 131;
        const int OpErode = 132;
        const int OpOutline = 133;

        /// <summary>Must match MAX_TAPS in OneJS/FxFilter.shader.</summary>
        public const int MaxFilterTaps = 32;

        /// <summary>Must match MAX_STOPS in OneJS/FxSources.shader and ops.ts.</summary>
        public const int MaxGradientStops = 8;

        const int ModeScalar = 0;
        const int ModeVector = 1;
        const int ModeTexture = 2;

        // MARK: handles

        static readonly Dictionary<int, Texture> s_Textures = new Dictionary<int, Texture>();
        static readonly HashSet<int> s_Pooled = new HashSet<int>();
        static int s_NextHandle = 1;

        static int Track(Texture t, bool pooled) {
            int h = s_NextHandle++;
            s_Textures[h] = t;
            if (pooled) s_Pooled.Add(h);
            return h;
        }

        /// <summary>The Unity texture behind a handle, for backgroundImage or Image src.</summary>
        public static Texture GetTexture(int handle) {
            return s_Textures.TryGetValue(handle, out var t) ? t : null;
        }

        /// <summary>Loads a texture from the project and returns a handle.</summary>
        public static int LoadTexture(string path) {
            var tex = Resources.Load<Texture>(path);
            if (tex == null) throw new ArgumentException("[onejs fx] no texture at Resources/" + path);
            // Not pooled: it belongs to Resources, not to us.
            return Track(tex, false);
        }

        /// <summary>Returns a handle's target to the pool. Loaded textures are left alone.</summary>
        public static void Release(int handle) {
            if (!s_Textures.TryGetValue(handle, out var t)) return;
            s_Textures.Remove(handle);
            if (s_Pooled.Remove(handle) && t is RenderTexture rt) ReturnToPool(rt);
        }

        // MARK: render target pool

        // Keyed by size, since every target this module makes has the same
        // format. Intermediates are borrowed and returned rather than allocated
        // per operation, which is the difference between a chain costing one
        // allocation and costing one per step.
        static readonly Dictionary<long, Stack<RenderTexture>> s_Pool =
            new Dictionary<long, Stack<RenderTexture>>();

        static long PoolKey(int w, int h) => ((long)w << 32) | (uint)h;

        static RenderTexture Borrow(int width, int height) {
            var key = PoolKey(width, height);
            if (s_Pool.TryGetValue(key, out var stack)) {
                while (stack.Count > 0) {
                    var candidate = stack.Pop();
                    // A target can be destroyed under us by a domain reload or by
                    // someone else releasing it; a null check here is cheaper than
                    // tracking every way that can happen.
                    if (candidate != null) return candidate;
                }
            }
            var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat) {
                name = "[onejs fx] " + width + "x" + height,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            rt.Create();
            return rt;
        }

        static void ReturnToPool(RenderTexture rt) {
            if (rt == null) return;
            var key = PoolKey(rt.width, rt.height);
            if (!s_Pool.TryGetValue(key, out var stack)) {
                stack = new Stack<RenderTexture>();
                s_Pool[key] = stack;
            }
            stack.Push(rt);
        }

        // MARK: execution

        static Material s_Material;
        static readonly Vector4[] s_Ops = new Vector4[MaxFusedOps];
        static readonly Vector4[] s_Args = new Vector4[MaxFusedOps];
        static int s_OpsId, s_ArgsId, s_OpCountId, s_MainTexId, s_TexBId, s_FlipYId;

        static Material s_SourceMaterial;
        static readonly Vector4[] s_GradColors = new Vector4[MaxGradientStops];
        static readonly Vector4[] s_GradPositions = new Vector4[MaxGradientStops];
        static int s_SourceTypeId, s_AspectId, s_NoiseScaleId, s_NoiseOffsetId;
        static int s_GradAngleId, s_GradStopCountId, s_GradColorsId, s_GradPositionsId;
        static int s_NoiseFbmId;
        static int s_SdfParamsId, s_SdfParams2Id, s_SdfTransformId, s_SdfShapeId;

        static Material EnsureSourceMaterial() {
            if (s_SourceMaterial != null) return s_SourceMaterial;
            var shader = Shader.Find("OneJS/FxSources");
            if (shader == null) throw new InvalidOperationException(
                "[onejs fx] OneJS/FxSources is missing. It ships under Resources/OneJS.");
            s_SourceMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            s_SourceTypeId = Shader.PropertyToID("_SourceType");
            s_AspectId = Shader.PropertyToID("_Aspect");
            s_NoiseScaleId = Shader.PropertyToID("_NoiseScale");
            s_NoiseOffsetId = Shader.PropertyToID("_NoiseOffset");
            s_NoiseFbmId = Shader.PropertyToID("_NoiseFbm");
            s_GradAngleId = Shader.PropertyToID("_GradAngle");
            s_GradStopCountId = Shader.PropertyToID("_GradStopCount");
            s_GradColorsId = Shader.PropertyToID("_GradColors");
            s_GradPositionsId = Shader.PropertyToID("_GradPositions");
            s_SdfParamsId = Shader.PropertyToID("_SdfParams");
            s_SdfParams2Id = Shader.PropertyToID("_SdfParams2");
            s_SdfTransformId = Shader.PropertyToID("_SdfTransform");
            s_SdfShapeId = Shader.PropertyToID("_SdfShape");
            return s_SourceMaterial;
        }

        static Material s_SpatialMaterial;
        static int s_SpOpId, s_XformId, s_Xform2Id, s_TileId, s_FlipId, s_CropId, s_BgColorId;

        static Material EnsureSpatialMaterial() {
            if (s_SpatialMaterial != null) return s_SpatialMaterial;
            var shader = Shader.Find("OneJS/FxSpatial");
            if (shader == null) throw new InvalidOperationException(
                "[onejs fx] OneJS/FxSpatial is missing. It ships under Resources/OneJS.");
            s_SpatialMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            s_SpOpId = Shader.PropertyToID("_Op");
            s_XformId = Shader.PropertyToID("_Xform");
            s_Xform2Id = Shader.PropertyToID("_Xform2");
            s_TileId = Shader.PropertyToID("_Tile");
            s_FlipId = Shader.PropertyToID("_Flip");
            s_CropId = Shader.PropertyToID("_Crop");
            s_BgColorId = Shader.PropertyToID("_BgColor");
            return s_SpatialMaterial;
        }

        static Material s_FilterMaterial;
        static int s_FilterId, s_TexelSizeId, s_DirId, s_RadiusId, s_SigmaId, s_AmountId;
        static int s_AltTexId, s_OutlineColorId, s_OutlineOnId;

        static Material EnsureFilterMaterial() {
            if (s_FilterMaterial != null) return s_FilterMaterial;
            var shader = Shader.Find("OneJS/FxFilter");
            if (shader == null) throw new InvalidOperationException(
                "[onejs fx] OneJS/FxFilter is missing. It ships under Resources/OneJS.");
            s_FilterMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            s_FilterId = Shader.PropertyToID("_Filter");
            s_TexelSizeId = Shader.PropertyToID("_TexelSize");
            s_DirId = Shader.PropertyToID("_Dir");
            s_RadiusId = Shader.PropertyToID("_Radius");
            s_SigmaId = Shader.PropertyToID("_Sigma");
            s_AmountId = Shader.PropertyToID("_Amount");
            s_AltTexId = Shader.PropertyToID("_AltTex");
            s_OutlineColorId = Shader.PropertyToID("_OutlineColor");
            s_OutlineOnId = Shader.PropertyToID("_OutlineOn");
            return s_FilterMaterial;
        }

        static readonly Vector4[] s_RampColors = new Vector4[MaxGradientStops];
        static readonly Vector4[] s_RampPositions = new Vector4[MaxGradientStops];
        static int s_RampCountId, s_RampColorsId, s_RampPositionsId;

        static Material EnsureMaterial() {
            if (s_Material != null) return s_Material;
            var shader = Shader.Find("OneJS/FxOps");
            if (shader == null) throw new InvalidOperationException(
                "[onejs fx] OneJS/FxOps is missing. It ships under Resources/OneJS.");
            s_Material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            s_OpsId = Shader.PropertyToID("_Ops");
            s_ArgsId = Shader.PropertyToID("_Args");
            s_OpCountId = Shader.PropertyToID("_OpCount");
            s_MainTexId = Shader.PropertyToID("_MainTex");
            s_TexBId = Shader.PropertyToID("_TexB");
            s_FlipYId = Shader.PropertyToID("_FlipY");
            s_RampCountId = Shader.PropertyToID("_RampCount");
            s_RampColorsId = Shader.PropertyToID("_RampColors");
            s_RampPositionsId = Shader.PropertyToID("_RampPositions");
            return s_Material;
        }

        /// <summary>
        /// Runs an encoded chain and returns a handle to the result.
        ///
        /// The buffer arrives as the {__csArray, __csArrayType:"float"} marker
        /// object, so it goes through the same conversion StyleBridge and
        /// PainterBridge use to get a plain float[].
        /// </summary>
        /// <summary>
        /// A target the caller keeps, rather than one handed out per render.
        ///
        /// An animated chain runs every frame, and if each run produced a new
        /// texture the element showing it would have to be re-pointed every
        /// frame, which means a React render per frame. Rendering into a stable
        /// target instead lets the element be assigned once.
        /// </summary>
        public static int CreateTarget(int width, int height) {
            return Track(Borrow(Mathf.Max(1, width), Mathf.Max(1, height)), true);
        }

        /// <summary>
        /// Runs a chain and blits the result into an existing target, leaving
        /// every intermediate back in the pool. Nothing new is tracked, so an
        /// animation loop does not grow the handle table.
        /// </summary>
        public static void ExecuteInto(int dstHandle, object bufferObj) {
            if (!(GetTexture(dstHandle) is RenderTexture dst))
                throw new ArgumentException("[onejs fx] destination handle is not a live target");
            var result = Run(bufferObj, out _, out _);
            Graphics.Blit(result, dst);
            ReturnToPool(result);
        }

        public static int Execute(object bufferObj) {
            var result = Run(bufferObj, out _, out _);
            return Track(result, true);
        }

        /// <summary>Walks the chain and returns the target holding the result.</summary>
        static RenderTexture Run(object bufferObj, out int width, out int height) {
            var buffer = QuickJSNative.ConvertToTargetType(bufferObj, typeof(float[])) as float[];
            if (buffer == null || buffer.Length < 2)
                throw new ArgumentException("[onejs fx] chain buffer is empty");

            int version = (int)buffer[0];
            // Accept anything up to what we know. A newer package against an older
            // runtime fails loudly here rather than silently dropping operations,
            // which is the failure mode the particle wire was built to avoid.
            if (version < 1 || version > WireVersion)
                throw new ArgumentException(
                    "[onejs fx] chain wire version " + version + " is not supported by this runtime " +
                    "(understands 1.." + WireVersion + "). Update the OneJS package.");

            int stepCount = (int)buffer[1];
            int cursor = 2;

            RenderTexture current = null;
            width = 0;
            height = 0;
            int fused = 0;
            Texture pendingOperand = null;
            bool pendingRamp = false;

            var mat = EnsureMaterial();

            for (int step = 0; step < stepCount; step++) {
                if (cursor + 3 > buffer.Length)
                    throw new ArgumentException("[onejs fx] chain buffer ended mid operation");
                int op = (int)buffer[cursor];
                int mode = (int)buffer[cursor + 1];
                int argCount = (int)buffer[cursor + 2];
                cursor += 3;
                if (cursor + argCount > buffer.Length)
                    throw new ArgumentException("[onejs fx] chain buffer ended mid arguments");

                if (op < FirstPixelOp) {
                    if (step != 0)
                        throw new ArgumentException("[onejs fx] a source must be the first operation");
                    current = BeginChain(op, buffer, cursor, argCount, out width, out height);
                    cursor += argCount;
                    continue;
                }

                if (current == null)
                    throw new ArgumentException("[onejs fx] chain does not start with a source");

                if (op >= FirstFilterOp) {
                    if (fused > 0) {
                        current = Flush(mat, current, pendingOperand, pendingRamp, fused, width, height);
                        fused = 0;
                        pendingOperand = null;
                        pendingRamp = false;
                    }
                    current = ApplyFilter(op, current, buffer, cursor, argCount, width, height);
                    cursor += argCount;
                    continue;
                }

                if (op >= FirstSpatialOp) {
                    // A spatial op needs the pixels as they stand, so anything
                    // fused so far has to land first.
                    if (fused > 0) {
                        current = Flush(mat, current, pendingOperand, pendingRamp, fused, width, height);
                        fused = 0;
                        pendingOperand = null;
                        pendingRamp = false;
                    }
                    current = ApplySpatial(op, current, buffer, cursor, argCount, ref width, ref height);
                    cursor += argCount;
                    continue;
                }

                // Three things force a flush: the window is full, a second texture
                // operand (the shader has one spare sampler), or a second ramp
                // (one set of ramp uniforms).
                bool wantsTexture = mode == ModeTexture;
                bool wantsRamp = op == OpRamp;
                if (fused == MaxFusedOps
                    || (wantsTexture && pendingOperand != null)
                    || (wantsRamp && pendingRamp)) {
                    current = Flush(mat, current, pendingOperand, pendingRamp, fused, width, height);
                    fused = 0;
                    pendingOperand = null;
                    pendingRamp = false;
                }

                if (wantsRamp) {
                    LoadRamp(mat, buffer, cursor, argCount);
                    pendingRamp = true;
                }

                if (wantsTexture) {
                    var operand = GetTexture((int)buffer[cursor]);
                    if (operand == null)
                        throw new ArgumentException("[onejs fx] operand texture handle is not live");
                    pendingOperand = operand;
                }

                // A blend spends its last two arguments on the mode and the
                // opacity, so they ride in _Ops.zw and leave all four arg slots
                // free for a colour operand.
                float blendMode = 0f, blendOpacity = 0f;
                int operandArgs = argCount;
                if (op == OpBlend) {
                    if (argCount < 2)
                        throw new ArgumentException("[onejs fx] blend needs a mode and an opacity");
                    blendMode = buffer[cursor + argCount - 2];
                    blendOpacity = buffer[cursor + argCount - 1];
                    operandArgs = argCount - 2;
                }

                s_Ops[fused] = new Vector4(op, mode, blendMode, blendOpacity);
                s_Args[fused] = wantsTexture
                    ? Vector4.zero
                    : new Vector4(
                        operandArgs > 0 ? buffer[cursor] : 0f,
                        operandArgs > 1 ? buffer[cursor + 1] : 0f,
                        operandArgs > 2 ? buffer[cursor + 2] : 0f,
                        operandArgs > 3 ? buffer[cursor + 3] : 0f);
                fused++;
                cursor += argCount;
            }

            if (fused > 0)
                current = Flush(mat, current, pendingOperand, pendingRamp, fused, width, height);

            return current;
        }

        static RenderTexture BeginChain(int op, float[] buffer, int cursor, int argCount,
                                        out int width, out int height) {
            if (op == OpSourceTexture) {
                if (argCount < 1) throw new ArgumentException("[onejs fx] source texture needs a handle");
                var src = GetTexture((int)buffer[cursor]);
                if (src == null) throw new ArgumentException("[onejs fx] source handle is not live");
                width = src.width;
                height = src.height;
                // Copy in rather than chaining off the caller's texture, so the
                // chain never writes into an input it does not own.
                var target = Borrow(width, height);
                Graphics.Blit(src, target);
                return target;
            }

            if (op == OpSourceColor) {
                if (argCount < 6) throw new ArgumentException("[onejs fx] colour source needs size and rgba");
                width = Mathf.Max(1, (int)buffer[cursor]);
                height = Mathf.Max(1, (int)buffer[cursor + 1]);
                var target = Borrow(width, height);
                var prev = RenderTexture.active;
                RenderTexture.active = target;
                GL.Clear(true, true, new Color(buffer[cursor + 2], buffer[cursor + 3],
                                               buffer[cursor + 4], buffer[cursor + 5]));
                RenderTexture.active = prev;
                return target;
            }

            if (op == OpSourceNoise || op == OpSourceGradient || op == OpSourceSdf) {
                if (argCount < 2) throw new ArgumentException("[onejs fx] a generated source needs a size");
                width = Mathf.Max(1, (int)buffer[cursor]);
                height = Mathf.Max(1, (int)buffer[cursor + 1]);
                var target = Borrow(width, height);
                var mat = EnsureSourceMaterial();
                mat.SetFloat(s_AspectId, height > 0 ? width / (float)height : 1f);

                if (op == OpSourceNoise) {
                    // w, h, scaleX, scaleY, octaves, seed, offsetX, offsetY, rotation,
                    // then optionally lacunarity, gain, kind
                    Need(argCount, 9, "noise");
                    mat.SetFloat(s_SourceTypeId, 0f);
                    // Optional so a buffer written before these existed still
                    // decodes; the defaults are the classic fBm pair.
                    mat.SetVector(s_NoiseFbmId, new Vector4(
                        argCount > 9 ? Mathf.Max(buffer[cursor + 9], 1e-3f) : 2f,
                        argCount > 10 ? Mathf.Clamp01(buffer[cursor + 10]) : 0.5f,
                        argCount > 11 ? buffer[cursor + 11] : 0f, 0f));
                    mat.SetVector(s_NoiseScaleId, new Vector4(
                        buffer[cursor + 2], buffer[cursor + 3],
                        Mathf.Clamp(buffer[cursor + 4], 1f, 4f), buffer[cursor + 5]));
                    mat.SetVector(s_NoiseOffsetId, new Vector4(
                        buffer[cursor + 6], buffer[cursor + 7], buffer[cursor + 8], 0f));
                } else if (op == OpSourceGradient) {
                    // w, h, angle, stopCount, then (r, g, b, a, pos) per stop
                    Need(argCount, 4, "gradient");
                    int stops = (int)buffer[cursor + 3];
                    if (stops < 1 || stops > MaxGradientStops)
                        throw new ArgumentException(
                            "[onejs fx] a gradient takes 1.." + MaxGradientStops + " stops, got " + stops);
                    Need(argCount, 4 + stops * 5, "gradient stops");
                    mat.SetFloat(s_SourceTypeId, 1f);
                    mat.SetFloat(s_GradAngleId, buffer[cursor + 2]);
                    mat.SetFloat(s_GradStopCountId, stops);
                    for (int i = 0; i < MaxGradientStops; i++) {
                        int b = cursor + 4 + i * 5;
                        bool live = i < stops;
                        s_GradColors[i] = live
                            ? new Vector4(buffer[b], buffer[b + 1], buffer[b + 2], buffer[b + 3])
                            : Vector4.zero;
                        // Park unused stops past the end so the shader's lerp never
                        // reaches them even if it reads past the live count.
                        s_GradPositions[i] = new Vector4(live ? buffer[b + 4] : 2f, 0, 0, 0);
                    }
                    mat.SetVectorArray(s_GradColorsId, s_GradColors);
                    mat.SetVectorArray(s_GradPositionsId, s_GradPositions);
                } else {
                    // w, h, shapeId, f1..f6, posX, posY, rot, scale, rounded, onion, softness, field
                    Need(argCount, 17, "sdf");
                    mat.SetFloat(s_SourceTypeId, 2f);
                    mat.SetVector(s_SdfParamsId, new Vector4(
                        buffer[cursor + 3], buffer[cursor + 4], buffer[cursor + 5], buffer[cursor + 6]));
                    mat.SetVector(s_SdfParams2Id, new Vector4(
                        buffer[cursor + 7], buffer[cursor + 8], buffer[cursor + 15], buffer[cursor + 16]));
                    mat.SetVector(s_SdfTransformId, new Vector4(
                        buffer[cursor + 9], buffer[cursor + 10], buffer[cursor + 11], buffer[cursor + 12]));
                    // w defaults to the X scale, so an sdf written before the
                    // second axis existed stays uniform.
                    mat.SetVector(s_SdfShapeId, new Vector4(
                        buffer[cursor + 2], buffer[cursor + 13], buffer[cursor + 14],
                        argCount > 17 ? buffer[cursor + 17] : buffer[cursor + 12]));
                }

                Graphics.Blit(null, target, mat, 0);
                return target;
            }

            throw new ArgumentException("[onejs fx] unknown source opcode " + op);
        }

        /// <summary>Uploads a ramp's stops. Only one ramp fits per fused pass.</summary>
        static void LoadRamp(Material mat, float[] buffer, int cursor, int argCount) {
            if (argCount < 1) throw new ArgumentException("[onejs fx] ramp needs a stop count");
            int stops = (int)buffer[cursor];
            if (stops < 1 || stops > MaxGradientStops)
                throw new ArgumentException(
                    "[onejs fx] a ramp takes 1.." + MaxGradientStops + " stops, got " + stops);
            Need(argCount, 1 + stops * 5, "ramp stops");
            for (int i = 0; i < MaxGradientStops; i++) {
                int b = cursor + 1 + i * 5;
                bool live = i < stops;
                s_RampColors[i] = live
                    ? new Vector4(buffer[b], buffer[b + 1], buffer[b + 2], buffer[b + 3])
                    : Vector4.zero;
                // Park dead stops past the end so the shader's lerp never reaches
                // them even if it reads beyond the live count.
                s_RampPositions[i] = new Vector4(live ? buffer[b + 4] : 2f, 0, 0, 0);
            }
            mat.SetFloat(s_RampCountId, stops);
            mat.SetVectorArray(s_RampColorsId, s_RampColors);
            mat.SetVectorArray(s_RampPositionsId, s_RampPositions);
        }

        /// <summary>
        /// Runs one spatial op as its own pass. Crop is the only one that changes
        /// the target size, so it reports the new one back.
        /// </summary>
        static RenderTexture ApplySpatial(int op, RenderTexture src, float[] buffer, int cursor,
                                          int argCount, ref int width, ref int height) {
            var mat = EnsureSpatialMaterial();
            int dstW = width, dstH = height;

            if (op == OpTransform) {
                // offsetX, offsetY, rotation, scale, pivotX, pivotY, wrap, bg rgba
                Need(argCount, 11, "transform");
                mat.SetFloat(s_SpOpId, 0f);
                mat.SetVector(s_XformId, new Vector4(
                    buffer[cursor], buffer[cursor + 1], buffer[cursor + 2], buffer[cursor + 3]));
                mat.SetVector(s_Xform2Id, new Vector4(
                    buffer[cursor + 4], buffer[cursor + 5], buffer[cursor + 6], 0f));
                mat.SetVector(s_BgColorId, new Vector4(
                    buffer[cursor + 7], buffer[cursor + 8], buffer[cursor + 9], buffer[cursor + 10]));
            } else if (op == OpTile) {
                Need(argCount, 4, "tile");
                mat.SetFloat(s_SpOpId, 1f);
                mat.SetVector(s_TileId, new Vector4(
                    buffer[cursor], buffer[cursor + 1], buffer[cursor + 2], buffer[cursor + 3]));
            } else if (op == OpFlip) {
                Need(argCount, 2, "flip");
                mat.SetFloat(s_SpOpId, 2f);
                mat.SetVector(s_FlipId, new Vector4(buffer[cursor], buffer[cursor + 1], 0, 0));
            } else if (op == OpCrop) {
                // x, y, w, h, all in uv
                Need(argCount, 4, "crop");
                mat.SetFloat(s_SpOpId, 3f);
                float cw = Mathf.Clamp01(buffer[cursor + 2]);
                float ch = Mathf.Clamp01(buffer[cursor + 3]);
                mat.SetVector(s_CropId, new Vector4(
                    buffer[cursor], buffer[cursor + 1], cw, ch));
                // A crop that rounded to zero would make an unusable target, so
                // it keeps at least one pixel.
                dstW = Mathf.Max(1, Mathf.RoundToInt(width * cw));
                dstH = Mathf.Max(1, Mathf.RoundToInt(height * ch));
            } else {
                throw new ArgumentException("[onejs fx] unknown spatial opcode " + op);
            }

            var dst = Borrow(dstW, dstH);
            Graphics.Blit(src, dst, mat, 0);
            ReturnToPool(src);
            width = dstW;
            height = dstH;
            return dst;
        }

        /// <summary>
        /// Runs one neighbourhood filter, which may take several passes. The
        /// separable ones go horizontal then vertical; a blur wider than the
        /// shader's tap budget goes round again rather than sampling sparsely.
        /// </summary>
        static RenderTexture ApplyFilter(int op, RenderTexture src, float[] buffer, int cursor,
                                         int argCount, int width, int height) {
            var mat = EnsureFilterMaterial();
            mat.SetVector(s_TexelSizeId, new Vector4(1f / width, 1f / height, width, height));

            if (op == OpSharpen || op == OpEdge) {
                Need(argCount, 1, op == OpSharpen ? "sharpen" : "edge");
                mat.SetFloat(s_FilterId, op == OpSharpen ? 1f : 2f);
                mat.SetFloat(s_AmountId, buffer[cursor]);
                var dst = Borrow(width, height);
                Graphics.Blit(src, dst, mat, 0);
                ReturnToPool(src);
                return dst;
            }

            if (op == OpBlur) {
                Need(argCount, 1, "blur");
                return Blur(mat, src, buffer[cursor], width, height);
            }

            if (op == OpDilate || op == OpErode) {
                Need(argCount, 1, op == OpDilate ? "dilate" : "erode");
                mat.SetFloat(s_FilterId, op == OpDilate ? 3f : 4f);
                return Separable(mat, src, Mathf.Min(buffer[cursor], MaxFilterTaps), width, height);
            }

            if (op == OpOutline) {
                // width, rgba, then 1 to key on luminance instead of alpha
                Need(argCount, 6, "outline");
                float w = Mathf.Min(buffer[cursor], MaxFilterTaps);
                // Dilating a copy leaves the original intact for the compose,
                // which is the whole point: the ring is the difference.
                var grown = Borrow(width, height);
                Graphics.Blit(src, grown);
                mat.SetFloat(s_FilterId, 3f); // dilate
                grown = Separable(mat, grown, w, width, height);

                mat.SetFloat(s_FilterId, 5f);
                mat.SetTexture(s_AltTexId, src);
                mat.SetVector(s_OutlineColorId, new Vector4(
                    buffer[cursor + 1], buffer[cursor + 2], buffer[cursor + 3], buffer[cursor + 4]));
                mat.SetFloat(s_OutlineOnId, buffer[cursor + 5]);
                var dst = Borrow(width, height);
                Graphics.Blit(grown, dst, mat, 0);
                ReturnToPool(grown);
                ReturnToPool(src);
                return dst;
            }

            throw new ArgumentException("[onejs fx] unknown filter opcode " + op);
        }

        /// <summary>
        /// Gaussian blur of the given pixel radius.
        ///
        /// Beyond the shader's tap budget the pass repeats instead of widening.
        /// Blurring twice with sigma s is a blur with sigma s*sqrt(2), because
        /// variances add, so N passes at sigma/sqrt(N) reproduce the sigma asked
        /// for. Stretching the taps further apart instead would alias.
        /// </summary>
        static RenderTexture Blur(Material mat, RenderTexture src, float radius, int width, int height) {
            if (radius <= 0f) return src;
            mat.SetFloat(s_FilterId, 0f);
            // A Gaussian is visually done by three sigma, which is the usual
            // radius-to-sigma relation and what makes the taps worth their cost.
            float sigma = radius / 3f;
            int passes = Mathf.Max(1, Mathf.CeilToInt(radius / MaxFilterTaps));
            float passRadius = radius / passes;
            float passSigma = sigma / Mathf.Sqrt(passes);
            var current = src;
            for (int i = 0; i < passes; i++) {
                mat.SetFloat(s_SigmaId, passSigma);
                current = Separable(mat, current, passRadius, width, height);
            }
            return current;
        }

        /// <summary>Horizontal then vertical, borrowing one target per direction.</summary>
        static RenderTexture Separable(Material mat, RenderTexture src, float radius,
                                       int width, int height) {
            mat.SetFloat(s_RadiusId, radius);
            mat.SetVector(s_DirId, new Vector4(1, 0, 0, 0));
            var mid = Borrow(width, height);
            Graphics.Blit(src, mid, mat, 0);
            ReturnToPool(src);

            mat.SetVector(s_DirId, new Vector4(0, 1, 0, 0));
            var dst = Borrow(width, height);
            Graphics.Blit(mid, dst, mat, 0);
            ReturnToPool(mid);
            return dst;
        }

        static void Need(int argCount, int required, string what) {
            if (argCount < required)
                throw new ArgumentException(
                    "[onejs fx] " + what + " source needs " + required + " arguments, got " + argCount);
        }

        static RenderTexture Flush(Material mat, RenderTexture src, Texture operand, bool hasRamp,
                                   int opCount, int width, int height) {
            // Zero the tail so a short run does not read whatever the last chain
            // left in the uniform arrays.
            for (int i = opCount; i < MaxFusedOps; i++) {
                s_Ops[i] = Vector4.zero;
                s_Args[i] = Vector4.zero;
            }
            mat.SetVectorArray(s_OpsId, s_Ops);
            mat.SetVectorArray(s_ArgsId, s_Args);
            mat.SetFloat(s_OpCountId, opCount);
            // Materials keep their uniforms between passes, so a pass with no
            // ramp has to say so or it inherits the last one's stops.
            if (!hasRamp) mat.SetFloat(s_RampCountId, 0f);
            mat.SetTexture(s_MainTexId, src);
            mat.SetTexture(s_TexBId, operand != null ? operand : Texture2D.whiteTexture);
            // Blitting into a render target, not to the screen, so the source uv
            // origin is the one the API reports.
            mat.SetFloat(s_FlipYId, 0f);

            var dst = Borrow(width, height);
            Graphics.Blit(src, dst, mat, 0);
            ReturnToPool(src);
            return dst;
        }

        // MARK: teardown

        /// <summary>
        /// Context teardown safety net. JS side disposal runs first through
        /// __onTeardown; this catches whatever it missed.
        /// </summary>
        public static void DisposeAll() {
            foreach (var kv in s_Textures)
                if (s_Pooled.Contains(kv.Key) && kv.Value is RenderTexture rt) Destroy(rt);
            s_Textures.Clear();
            s_Pooled.Clear();
            foreach (var stack in s_Pool.Values)
                while (stack.Count > 0) Destroy(stack.Pop());
            s_Pool.Clear();
            if (s_Material != null) {
                UnityEngine.Object.DestroyImmediate(s_Material);
                s_Material = null;
            }
            if (s_SourceMaterial != null) {
                UnityEngine.Object.DestroyImmediate(s_SourceMaterial);
                s_SourceMaterial = null;
            }
            if (s_SpatialMaterial != null) {
                UnityEngine.Object.DestroyImmediate(s_SpatialMaterial);
                s_SpatialMaterial = null;
            }
            if (s_FilterMaterial != null) {
                UnityEngine.Object.DestroyImmediate(s_FilterMaterial);
                s_FilterMaterial = null;
            }
        }

        static void Destroy(RenderTexture rt) {
            if (rt == null) return;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }

        /// <summary>Live handles and pooled targets, for leak checks in tests.</summary>
        public static int LiveHandleCount => s_Textures.Count;

        public static int PooledTargetCount {
            get {
                int n = 0;
                foreach (var stack in s_Pool.Values) n += stack.Count;
                return n;
            }
        }
    }
}
