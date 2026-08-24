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
            return s_Material;
        }

        /// <summary>
        /// Runs an encoded chain and returns a handle to the result.
        ///
        /// The buffer arrives as the {__csArray, __csArrayType:"float"} marker
        /// object, so it goes through the same conversion StyleBridge and
        /// PainterBridge use to get a plain float[].
        /// </summary>
        public static int Execute(object bufferObj) {
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
            int width = 0, height = 0;
            int fused = 0;
            Texture pendingOperand = null;

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

                // Two things force a flush: the window is full, or this op wants a
                // second texture operand and the shader has only one spare sampler.
                bool wantsTexture = mode == ModeTexture;
                if (fused == MaxFusedOps || (wantsTexture && pendingOperand != null)) {
                    current = Flush(mat, current, pendingOperand, fused, width, height);
                    fused = 0;
                    pendingOperand = null;
                }

                if (wantsTexture) {
                    var operand = GetTexture((int)buffer[cursor]);
                    if (operand == null)
                        throw new ArgumentException("[onejs fx] operand texture handle is not live");
                    pendingOperand = operand;
                }

                s_Ops[fused] = new Vector4(op, mode, 0, 0);
                s_Args[fused] = wantsTexture
                    ? Vector4.zero
                    : new Vector4(
                        argCount > 0 ? buffer[cursor] : 0f,
                        argCount > 1 ? buffer[cursor + 1] : 0f,
                        argCount > 2 ? buffer[cursor + 2] : 0f,
                        argCount > 3 ? buffer[cursor + 3] : 0f);
                fused++;
                cursor += argCount;
            }

            if (fused > 0)
                current = Flush(mat, current, pendingOperand, fused, width, height);

            return Track(current, true);
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
                    // w, h, scaleX, scaleY, octaves, seed, offsetX, offsetY, rotation
                    Need(argCount, 9, "noise");
                    mat.SetFloat(s_SourceTypeId, 0f);
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
                    mat.SetVector(s_SdfShapeId, new Vector4(
                        buffer[cursor + 2], buffer[cursor + 13], buffer[cursor + 14], 0f));
                }

                Graphics.Blit(null, target, mat, 0);
                return target;
            }

            throw new ArgumentException("[onejs fx] unknown source opcode " + op);
        }

        static void Need(int argCount, int required, string what) {
            if (argCount < required)
                throw new ArgumentException(
                    "[onejs fx] " + what + " source needs " + required + " arguments, got " + argCount);
        }

        static RenderTexture Flush(Material mat, RenderTexture src, Texture operand,
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
