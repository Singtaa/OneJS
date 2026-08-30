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
        public static int Upload(float[] data, int instructionCount, int resultRegister) {
            if (VmShader == null) {
                throw new InvalidOperationException(
                    "[OneJS sl] OneJS/FxProgram.shader is missing from Resources. " +
                    "Without it a program cannot run at all.");
            }
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

            var c = new Compiled {
                InstructionCount = instructionCount,
                ResultRegister = resultRegister,
                Material = new Material(VmShader),
            };

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
