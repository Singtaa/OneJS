using System;
using System.Collections.Generic;
using UnityEngine;

namespace OneJS.SL {
    /// <summary>
    /// Runs a shader language program on the GPU.
    ///
    /// A program is authored in TypeScript (`onejs-unity/sl`), recorded as a
    /// graph, and encoded by `sl/encode.ts` into a flat float buffer: two texels
    /// per instruction, eight registers, indexed store. This side uploads that
    /// buffer as a texture and lets OneJS/FxProgram.shader evaluate it.
    ///
    /// WHY A VM AT ALL. Unity cannot compile a shader at runtime in a player
    /// build, on any graphics API. Every game on play.onejs.com runs inside a
    /// prebuilt container, so a program written there can never become shader
    /// code; it has to become data a fixed shader interprets. A project with an
    /// editor generates HLSL from the same program and compiles it, so an
    /// ejected game pays none of this. The author writes one thing either way,
    /// which is the point.
    ///
    /// The buffer crosses from JS ONCE per program, not per frame. Uniforms
    /// cross when they change, diffed by value the way ShaderEffect's props are,
    /// so animating a slider does not re-upload a program.
    /// </summary>
    public static class SLProgramBridge {
        /// <summary>Must match REGS in FxProgram.shader and REGISTERS in encode.ts.</summary>
        public const int Registers = 8;
        /// <summary>Must match MAX_INSTR in FxProgram.shader and MAX_INSTRUCTIONS in encode.ts.</summary>
        public const int MaxInstructions = 256;
        const int MaxUniforms = 16;
        const int MaxTextures = 4;
        const int FloatsPerInstruction = 8;   // two RGBA texels

        static readonly int s_Program = Shader.PropertyToID("_Program");
        static readonly int s_InstrCount = Shader.PropertyToID("_InstrCount");
        static readonly int s_ProgramWidth = Shader.PropertyToID("_ProgramWidth");
        static readonly int s_ResultReg = Shader.PropertyToID("_ResultReg");
        static readonly int s_Secs = Shader.PropertyToID("_Secs");
        static readonly int s_FlipY = Shader.PropertyToID("_FlipY");
        static readonly int s_Uniforms = Shader.PropertyToID("_Uniforms");
        static readonly int[] s_TexIds = {
            Shader.PropertyToID("_Tex0"), Shader.PropertyToID("_Tex1"),
            Shader.PropertyToID("_Tex2"), Shader.PropertyToID("_Tex3"),
        };

        static Shader s_Shader;
        static readonly Dictionary<int, Compiled> s_Programs = new Dictionary<int, Compiled>();
        static int s_NextHandle = 1;

        class Compiled : IDisposable {
            public Texture2D ProgramTex;
            public Material Material;
            public int InstructionCount;
            public int ResultRegister;
            public readonly Vector4[] Uniforms = new Vector4[MaxUniforms];
            /// <summary>True when a shader generated from this program was found.</summary>
            public bool Native;
            /// <summary>Uniform names, for the native path's per name properties.</summary>
            public string[] UniformNames;
            public int[] UniformIds;

            public void Dispose() {
                if (ProgramTex != null) UnityEngine.Object.DestroyImmediate(ProgramTex);
                if (Material != null) UnityEngine.Object.DestroyImmediate(Material);
                ProgramTex = null;
                Material = null;
            }
        }

        static Shader VmShader {
            get {
                if (s_Shader == null) s_Shader = Resources.Load<Shader>("OneJS/FxProgram");
                return s_Shader;
            }
        }

        /// <summary>
        /// Uploads an encoded program and returns a handle.
        ///
        /// `data` is the flat buffer `encode()` produced. It is validated here
        /// rather than trusted: a buffer whose length does not match its
        /// instruction count, or that names a register the VM does not have,
        /// would render a wrong picture rather than fail, and a wrong picture is
        /// indistinguishable from an authoring mistake.
        /// </summary>
        /// <summary>
        /// The name a shader generated from a program carries. The hash is the
        /// link between the two, and if it ever fails to match, the runtime
        /// falls back to the VM and NOBODY IS TOLD: correct output, quietly
        /// slower, no error. That is why the hash is a Merkle hash over the
        /// graph rather than a walk of the node array, and why this string is
        /// written once here rather than spelled out at each call site.
        /// </summary>
        public static string GeneratedShaderName(string hash) => "Hidden/SLGenerated/" + hash;

        /// <summary>
        /// True when a compiled shader exists for this program.
        ///
        /// Worth exposing because the difference is otherwise invisible, which
        /// is the design working as intended and also the design's one hazard:
        /// an eject that silently failed to generate shaders looks exactly like
        /// one that worked, only slower.
        /// </summary>
        public static bool IsNative(int handle) =>
            s_Programs.TryGetValue(handle, out var c) && c.Native;

        /// <summary>
        /// Builds the material for a program, choosing the backend, without
        /// taking a handle.
        ///
        /// Exists so an element that already owns a render target and a clock,
        /// like ShaderEffectElement, can run a program without a second copy of
        /// the target, tick and backgroundImage machinery beside it. `native`
        /// reports which backend was chosen, because from the outside the two
        /// are indistinguishable, which is the design working and also the thing
        /// that makes a silently failed generation impossible to notice.
        /// </summary>
        /// <summary>
        /// Kept for callers that only want a material and never set a uniform.
        /// </summary>
        public static Material CreateMaterial(float[] data, int instructionCount, int resultRegister,
                                              string hash, out bool native) {
            return CreateMaterial(data, instructionCount, resultRegister, hash, out native, out _);
        }

        /// <summary>
        /// A material for a program, AND a handle that can set its uniforms.
        /// </summary>
        /// <remarks>
        /// The handle is the part that was missing. This used to hand back a
        /// bare material and register nothing, so SetUniform had no program to
        /// find and every uniform stayed at whatever the shader defaulted to:
        /// zero. A program's uniforms silently did nothing, on both backends,
        /// for as long as an element used this rather than Upload.
        ///
        /// Registering here rather than asking the element to call Upload keeps
        /// the material the caller renders with and the material SetUniform
        /// writes to the same object. Two of them would put the values
        /// somewhere real and still show none of them.
        /// </remarks>
        public static Material CreateMaterial(float[] data, int instructionCount, int resultRegister,
                                              string hash, out bool native, out int handle,
                                              string[] uniformNames = null) {
            Validate(data, instructionCount, resultRegister);
            native = false;

            var c = new Compiled {
                InstructionCount = instructionCount,
                ResultRegister = resultRegister,
                UniformNames = uniformNames,
            };

            Shader gen = string.IsNullOrEmpty(hash) ? null : Shader.Find(GeneratedShaderName(hash));
            if (gen != null) {
                native = true;
                c.Native = true;
                c.Material = new Material(gen);
                if (uniformNames != null) {
                    c.UniformIds = new int[uniformNames.Length];
                    for (int u = 0; u < uniformNames.Length; u++) {
                        c.UniformIds[u] = Shader.PropertyToID("_u_" + uniformNames[u]);
                    }
                }
            } else {
                if (VmShader == null) {
                    throw new InvalidOperationException(
                        "[OneJS sl] OneJS/FxProgram.shader is missing from Resources.");
                }
                c.Material = new Material(VmShader);
                // Held on the Compiled, not just handed to the material, so
                // Release disposes it. A local would leak one float texture per
                // program for the life of the context.
                c.ProgramTex = BuildProgramTexture(data, instructionCount);
                c.Material.SetTexture(s_Program, c.ProgramTex);
                c.Material.SetFloat(s_InstrCount, instructionCount);
                c.Material.SetFloat(s_ProgramWidth, instructionCount * 2);
                c.Material.SetFloat(s_ResultReg, resultRegister);
            }

            handle = s_NextHandle++;
            s_Programs[handle] = c;
            return c.Material;
        }

        static Texture2D BuildProgramTexture(float[] data, int instructionCount) {
            int texels = instructionCount * 2;
            var tex = new Texture2D(texels, 1, TextureFormat.RGBAFloat, false, true) {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "sl program",
            };
            var px = new Color[texels];
            for (int t = 0; t < texels; t++) {
                int o = t * 4;
                px[t] = new Color(data[o], data[o + 1], data[o + 2], data[o + 3]);
            }
            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }

        static void Validate(float[] data, int instructionCount, int resultRegister) {
            if (instructionCount <= 0 || instructionCount > MaxInstructions) {
                throw new ArgumentException(
                    $"[OneJS sl] a program has {instructionCount} instructions; the VM runs 1 to {MaxInstructions}.");
            }
            if (data == null || data.Length != instructionCount * FloatsPerInstruction) {
                throw new ArgumentException(
                    $"[OneJS sl] the buffer is {data?.Length ?? 0} floats and {instructionCount} instructions " +
                    $"needs {instructionCount * FloatsPerInstruction}. The encoder and this side disagree " +
                    "about the encoding, which would decode as different instructions.");
            }
            if (resultRegister < 0 || resultRegister >= Registers) {
                throw new ArgumentException(
                    $"[OneJS sl] the result is in register {resultRegister} and the VM has {Registers}.");
            }
            for (int i = 0; i < instructionCount; i++) {
                int dst = (int)data[i * FloatsPerInstruction + 1];
                if (dst < 0 || dst >= Registers) {
                    throw new ArgumentException(
                        $"[OneJS sl] instruction {i} writes register {dst} and the VM has {Registers}. " +
                        "The allocator and the VM disagree about the register file size.");
                }
            }
        }

        public static int Upload(float[] data, int instructionCount, int resultRegister,
                                 string hash = null, string[] uniformNames = null) {
            if (VmShader == null) {
                throw new InvalidOperationException(
                    "[OneJS sl] OneJS/FxProgram.shader is missing from Resources. " +
                    "Without it a program cannot run at all.");
            }
            Validate(data, instructionCount, resultRegister);

            var c = new Compiled {
                InstructionCount = instructionCount,
                ResultRegister = resultRegister,
                UniformNames = uniformNames,
            };

            // THE EJECT PATH. A project with an editor generates a shader per
            // program at import time; this looks for one and uses it when it is
            // there. Play has no such shader and gets the VM. The caller cannot
            // tell the difference, which is the entire point: an author writes
            // one program and never learns that two backends exist.
            Shader native = string.IsNullOrEmpty(hash) ? null : Shader.Find(GeneratedShaderName(hash));
            if (native != null) {
                c.Native = true;
                c.Material = new Material(native);
                if (uniformNames != null) {
                    c.UniformIds = new int[uniformNames.Length];
                    for (int u = 0; u < uniformNames.Length; u++) {
                        c.UniformIds[u] = Shader.PropertyToID("_u_" + uniformNames[u]);
                    }
                }
                int nativeHandle = s_NextHandle++;
                s_Programs[nativeHandle] = c;
                return nativeHandle;
            }

            c.Material = new Material(VmShader);

            // One row, two texels per instruction. Point filtered and clamped:
            // the shader fetches exact texel centres, and any filtering would
            // blend two instructions into a third that does not exist.
            int texels = instructionCount * 2;
            c.ProgramTex = new Texture2D(texels, 1, TextureFormat.RGBAFloat, false, true) {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "sl program",
            };
            var px = new Color[texels];
            for (int t = 0; t < texels; t++) {
                int o = t * 4;
                px[t] = new Color(data[o], data[o + 1], data[o + 2], data[o + 3]);
            }
            c.ProgramTex.SetPixels(px);
            c.ProgramTex.Apply(false, false);

            c.Material.SetTexture(s_Program, c.ProgramTex);
            c.Material.SetFloat(s_InstrCount, instructionCount);
            c.Material.SetFloat(s_ProgramWidth, texels);
            c.Material.SetFloat(s_ResultReg, resultRegister);

            int handle = s_NextHandle++;
            s_Programs[handle] = c;
            return handle;
        }

        /// <summary>Sets one uniform slot. Cheap enough to call per frame.</summary>
        public static void SetUniform(int handle, int slot, float x, float y, float z, float w) {
            if (!s_Programs.TryGetValue(handle, out var c)) return;
            if (slot < 0 || slot >= MaxUniforms) {
                throw new ArgumentException($"[OneJS sl] uniform slot {slot} is outside 0..{MaxUniforms - 1}.");
            }
            // Same call, either backend. A generated shader carries one property
            // per uniform, so the value goes by name; the VM reads a single
            // array indexed by slot. A caller that had to know which is which
            // would be a caller that has to know it ejected.
            if (c.Native) {
                if (c.UniformIds != null && slot < c.UniformIds.Length) {
                    c.Material.SetVector(c.UniformIds[slot], new Vector4(x, y, z, w));
                }
                return;
            }
            c.Uniforms[slot] = new Vector4(x, y, z, w);
            c.Material.SetVectorArray(s_Uniforms, c.Uniforms);
        }

        public static void SetTexture(int handle, int slot, Texture tex) {
            if (!s_Programs.TryGetValue(handle, out var c)) return;
            if (slot < 0 || slot >= MaxTextures) {
                throw new ArgumentException(
                    $"[OneJS sl] texture slot {slot} is outside 0..{MaxTextures - 1}. " +
                    "The VM binds its samplers by name, so this is a fixed set rather than a budget.");
            }
            c.Material.SetTexture(s_TexIds[slot], tex);
        }

        /// <summary>Renders the program into a target. `seconds` drives the time input.</summary>
        public static void Render(int handle, RenderTexture target, float seconds) {
            if (!s_Programs.TryGetValue(handle, out var c)) {
                throw new ArgumentException($"[OneJS sl] no program with handle {handle}.");
            }
            c.Material.SetFloat(s_Secs, seconds);
            // Both backends declare _Secs and _FlipY, so nothing here branches.
            // Render target UV origin differs across graphics APIs, and the VM
            // corrects it in the vertex stage so an author never has to. Getting
            // this wrong is how an effect ends up upside down in a browser and
            // right way up in the editor.
            c.Material.SetFloat(s_FlipY, SystemInfo.graphicsUVStartsAtTop ? 1f : 0f);
            Graphics.Blit(null, target, c.Material);
        }

        public static void Release(int handle) {
            if (!s_Programs.TryGetValue(handle, out var c)) return;
            c.Dispose();
            s_Programs.Remove(handle);
        }

        /// <summary>Context teardown safety net, matching the other bridges.</summary>
        public static void DisposeAll() {
            foreach (var c in s_Programs.Values) c.Dispose();
            s_Programs.Clear();
        }

        public static int LiveProgramCount => s_Programs.Count;
    }
}
