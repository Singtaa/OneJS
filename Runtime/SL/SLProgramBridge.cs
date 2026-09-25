using System;
using System.Collections.Generic;
using UnityEngine;

namespace OneJS.SL {
    /// <summary>
    /// Runs a shader language program on the GPU, compiled.
    ///
    /// A program is authored in TypeScript (`onejs-unity/sl`) or a `.sl` file
    /// and arrives here with its hash. The editor generates a shader from it
    /// (SLShaderGenerator, in the editor assembly), a native player ships those
    /// shaders (<see cref="SLShaderRegistry"/>), and a WebGL page compiles the
    /// program itself (<see cref="SLWeb"/>). Where none of that has happened
    /// yet, the program draws nothing.
    ///
    /// THE VM. The program also arrives encoded by `onejs-sl/src/encode.ts` as a
    /// flat float buffer (two texels per instruction, eight registers), which
    /// OneJS/FxProgram.shader can evaluate. That used to draw every program in
    /// a native player and every program the editor had not compiled yet. It
    /// is kept behind ONEJS_SL_VM for one release (<see cref="VmAllowed"/>),
    /// and the parity harness keeps it on the web with ONEJS_SL_WEB_VM.
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

        /// <summary>
        /// The newest VM encoding FxProgram.shader runs. Must match
        /// SL_WIRE_VERSION in onejs-unity. A payload carries the lowest version
        /// that can run it, so this refuses only programs that use an
        /// instruction it does not have, and only where the VM would run them.
        /// </summary>
        public const int WireVersion = 2;

        /// <summary>Refuses a buffer newer than this VM, rather than drawing it wrong.</summary>
        static void CheckWire(int wire) {
            if (wire <= WireVersion) return;
            throw new ArgumentException(
                $"[OneJS sl] this program needs the shader language VM at wire version {wire}, " +
                $"and this OneJS runs up to {WireVersion}. Update OneJS, or build the program " +
                "with the onejs-unity this OneJS shipped with.");
        }

        static readonly int s_Program = Shader.PropertyToID("_Program");
        static readonly int s_InstrCount = Shader.PropertyToID("_InstrCount");
        static readonly int s_ProgramWidth = Shader.PropertyToID("_ProgramWidth");
        static readonly int s_ResultReg = Shader.PropertyToID("_ResultReg");
        static readonly int s_Secs = Shader.PropertyToID("_Secs");
        static readonly int s_FlipY = Shader.PropertyToID("_FlipY");
        static readonly int s_Res = Shader.PropertyToID("_Res");
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
            /// <summary>The program hash, which is what links it to a generated shader.</summary>
            public string Hash;
            /// <summary>By slot, for the compiled web path, which binds them itself.</summary>
            public readonly Texture[] Textures = new Texture[MaxTextures];
            /// <summary>The browser compiled program, 0 when there is none. See <see cref="SLWeb"/>.</summary>
            public int WebId;
            /// <summary>Cleared by the host to force the VM, which is how parity is measured.</summary>
            public bool WebAllowed = true;
            /// <summary>True when the last frame was drawn compiled rather than on the VM.</summary>
            public bool DrewCompiled;
            /// <summary>
            /// When the editor started waiting for this program's shader, or -1.
            /// See <see cref="CurrentMaterial"/>.
            /// </summary>
            public float AwaitingSince = -1f;

            public void Dispose() {
                SLWeb.Release(WebId);
                WebId = 0;
                if (ProgramTex != null) UnityEngine.Object.DestroyImmediate(ProgramTex);
                if (Material != null) UnityEngine.Object.DestroyImmediate(Material);
                ProgramTex = null;
                Material = null;
            }
        }

        /// <summary>
        /// True in a WebGL player: a program there is drawn only compiled, by
        /// the page (<see cref="SLWeb"/>), never by the VM. Measured faster by
        /// 90 to 600 times and matching it within 1/255, so the VM would only
        /// ever be a slower copy of the same picture.
        ///
        /// A page that cannot compile (the startup handle check failed) draws
        /// nothing and says why, rather than falling back quietly; the Play
        /// container's smoke test is what catches that. ONEJS_SL_WEB_VM keeps
        /// the VM in a WebGL build, for measuring the two against each other
        /// (Tools/sl-web-parity in the container).
        /// </summary>
        public static bool CompiledOnly =>
#if UNITY_WEBGL && !UNITY_EDITOR && !ONEJS_SL_WEB_VM
            true;
#else
            false;
#endif

        /// <summary>
        /// True where the VM may draw a program that has no compiled shader.
        ///
        /// Off by default. The editor generates a shader the first time it sees
        /// a program and draws nothing until it exists, and a native player ships
        /// every program compiled (<see cref="SLShaderRegistry"/>), so nothing
        /// needs the VM. It stays for one release as a way back: build with
        /// ONEJS_SL_VM and a program with no compiled shader draws on it, as it
        /// did before. A WebGL build with ONEJS_SL_WEB_VM keeps it as well, for
        /// the parity harness. Settable so a test that compares against the VM
        /// can turn it on; nothing else should.
        /// </summary>
        public static bool VmAllowed { get; set; } = VmByDefault;

        const bool VmByDefault =
#if ONEJS_SL_VM || (UNITY_WEBGL && !UNITY_EDITOR && ONEJS_SL_WEB_VM)
            true;
#else
            false;
#endif

        /// <summary>How long the editor waits for a shader before saying a program has none.</summary>
        const float MissingAfterSeconds = 3f;

        /// <summary>Why a program cannot run at all here, or null when it can.</summary>
        static string Unrunnable() {
            if (!CompiledOnly || SLWeb.Available) return null;
            return $"[OneJS sl] this page cannot compile shader programs ({SLWeb.Describe()}), " +
                   "and a WebGL player has no VM to fall back on, so the program will not draw.";
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

        static SLShaderRegistry s_Registry;
        static bool s_RegistryLoaded;

        /// <summary>
        /// The shader generated from a program, or null when there is none.
        ///
        /// The registry first, because it is the only way a player has one: the
        /// build packs every generated shader through it (<see cref="SLShaderRegistry"/>),
        /// and `Shader.Find` alone found nothing there. The editor also falls
        /// back to `Shader.Find`, since it generates shaders mid session, when
        /// it records a program, and the registry is written only by a build.
        /// </summary>
        public static Shader FindGenerated(string hash) {
            if (string.IsNullOrEmpty(hash)) return null;
            if (!s_RegistryLoaded) {
                s_Registry = Resources.Load<SLShaderRegistry>(SLShaderRegistry.ResourcePath);
                s_RegistryLoaded = true;
            }
            var shader = s_Registry != null ? s_Registry.Find(hash) : null;
#if UNITY_EDITOR
            if (shader == null) shader = Shader.Find(GeneratedShaderName(hash));
#endif
            return shader;
        }

        /// <summary>Loads the registry again on the next lookup. For the build step and tests, which rewrite it.</summary>
        public static void ReloadRegistry() {
            s_Registry = null;
            s_RegistryLoaded = false;
        }

        static readonly HashSet<string> s_WarnedVm = new HashSet<string>();
        static readonly HashSet<string> s_WarnedMissing = new HashSet<string>();

        /// <summary>
        /// A program with no compiled shader and no VM to fall back on: it draws
        /// nothing until one exists. The editor waits for it, because it is
        /// about to generate one (<see cref="CurrentMaterial"/>); a player never
        /// will, so it says so now.
        /// </summary>
        static void AwaitShader(Compiled c) {
            if (Application.isEditor) {
                c.AwaitingSince = Time.realtimeSinceStartup;
                return;
            }
            SayMissing(c.Hash, editor: false);
        }

        /// <summary>
        /// Says, once per program, that it has no compiled shader and draws
        /// nothing. An error in a player, where the build left the program out;
        /// a warning in the editor, where its shader did not arrive in time and
        /// still may.
        /// </summary>
        static void SayMissing(string hash, bool editor) {
            if (!s_WarnedMissing.Add(hash ?? "")) return;
            var name = string.IsNullOrEmpty(hash) ? "(no hash)" : hash;
            if (editor) {
                Debug.LogWarning(
                    $"[OneJS sl] program {name} has no compiled shader yet, so it draws nothing. A program " +
                    "from a .sl file gets one from the app.sl.json beside its bundle, so build the app again. " +
                    "A program built in code gets one the first time it is drawn, from the HLSL its host sends.");
                return;
            }
            Debug.LogError(
                $"[OneJS sl] program {name} has no compiled shader in this player, so it draws nothing. The " +
                "build compiles every program listed in a *.sl.json manifest: a .sl file is listed in " +
                "app.sl.json when the app is built, and a program built in code is listed in " +
                "Assets/OneJS/Recorded.sl.json once the editor has drawn it. Run the app in the editor, then " +
                "build again. For this release, building with ONEJS_SL_VM draws it on the VM instead.");
        }

        /// <summary>
        /// Says, once per program, that a player built with ONEJS_SL_VM is
        /// drawing it on the VM.
        ///
        /// Once, because a program that falls back does so on every mount, and
        /// a warning per mount buries the one line that says what to do. Only
        /// in a player: in the editor the VM draws only where a test or the
        /// define asked for it.
        /// </summary>
        static void WarnVmFallback(string hash) {
            if (Application.isEditor || !s_WarnedVm.Add(hash ?? "")) return;
            Debug.LogWarning(
                $"[OneJS sl] program {(string.IsNullOrEmpty(hash) ? "(no hash)" : hash)} has no compiled shader " +
                "in this player, so it draws on the VM, which is slower. The build compiles every program " +
                "listed in a *.sl.json manifest: a .sl file is listed in app.sl.json when the app is built, " +
                "and a program built in code is listed in Assets/OneJS/Recorded.sl.json once the editor has " +
                "drawn it. Run the app in the editor, then build again.");
        }

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
        /// Set by an editor that can turn a program into a compiled shader.
        /// Null in a player, so a program in Play costs nothing here.
        ///
        /// Programs exist only once JavaScript has run, so nothing can find them
        /// by reading source. This is where they are found instead: the host
        /// asks <see cref="WantsSource"/> when it has just interpreted one, hands
        /// over the HLSL through <see cref="RecordSource"/>, and the editor
        /// side writes the manifest and generates the shader. That is how an
        /// ejected game ends up compiled without anybody writing a manifest.
        /// </summary>
        public static Action<string, string> SourceRecorder;

        /// <summary>True when a recorder is attached and this hash has no compiled shader.</summary>
        public static bool WantsSource(string hash) =>
            SourceRecorder != null && !string.IsNullOrEmpty(hash) && FindGenerated(hash) == null;

        public static void RecordSource(string hash, string hlsl) {
            if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(hlsl)) return;
            SourceRecorder?.Invoke(hash, hlsl);
        }

        /// <summary>
        /// Moves every program whose shader now exists onto it, and returns how
        /// many moved: one on the VM in place, one drawing nothing onto a new
        /// material, which its element picks up through <see cref="CurrentMaterial"/>.
        ///
        /// In place matters: the element renders with the Material object it was
        /// handed, so the shader is swapped on that object rather than a new
        /// material issued. Uniform values carry over by being set again under
        /// the generated shader's per name properties; textures keep their
        /// names on both backends, so the material already holds them.
        /// </summary>
        public static int AdoptGenerated() {
            int adopted = 0;
            foreach (var c in s_Programs.Values) {
                if (c.Native || string.IsNullOrEmpty(c.Hash)) continue;
                var gen = FindGenerated(c.Hash);
                if (gen == null) continue;
                if (c.Material == null) {
                    // Nothing was drawing it, since there is no VM here. The
                    // textures were kept by slot for exactly this, and keep
                    // their names on the generated shader.
                    c.Material = new Material(gen) { hideFlags = HideFlags.HideAndDontSave };
                    for (int t = 0; t < MaxTextures; t++) {
                        if (c.Textures[t] != null) c.Material.SetTexture(s_TexIds[t], c.Textures[t]);
                    }
                } else {
                    c.Material.shader = gen;
                }
                c.AwaitingSince = -1f;
                c.Native = true;
                BindUniformIds(c);
                if (c.UniformIds != null) {
                    for (int u = 0; u < c.UniformIds.Length && u < MaxUniforms; u++) {
                        c.Material.SetVector(c.UniformIds[u], c.Uniforms[u]);
                    }
                }
                if (c.ProgramTex != null) {
                    UnityEngine.Object.DestroyImmediate(c.ProgramTex);
                    c.ProgramTex = null;
                }
                adopted++;
            }
            return adopted;
        }

        static void BindUniformIds(Compiled c) {
            if (c.UniformNames == null) return;
            c.UniformIds = new int[c.UniformNames.Length];
            for (int u = 0; u < c.UniformNames.Length; u++) {
                c.UniformIds[u] = Shader.PropertyToID("_u_" + c.UniformNames[u]);
            }
        }

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
                                              string[] uniformNames = null, int wire = 1) {
            Validate(data, instructionCount, resultRegister);
            native = false;
            var why = Unrunnable();
            if (why != null) throw new InvalidOperationException(why);

            var c = new Compiled {
                InstructionCount = instructionCount,
                ResultRegister = resultRegister,
                UniformNames = uniformNames,
                Hash = hash,
            };

            Shader gen = FindGenerated(hash);
            if (gen != null) {
                native = true;
                c.Native = true;
                c.Material = new Material(gen);
                BindUniformIds(c);
            } else if (CompiledOnly) {
                // No material: the page draws it (TryRenderCompiled) once the
                // host hands over its WGSL and GLSL.
            } else if (!VmAllowed) {
                // No material either: nothing draws until the editor has
                // generated a shader and adopted it.
                AwaitShader(c);
            } else {
                if (VmShader == null) {
                    throw new InvalidOperationException(
                        "[OneJS sl] OneJS/FxProgram.shader is missing from Resources.");
                }
                CheckWire(wire);
                WarnVmFallback(hash);
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
                                 string hash = null, string[] uniformNames = null, int wire = 1) {
            var why = Unrunnable();
            if (why != null) throw new InvalidOperationException(why);
            if (CompiledOnly) {
                Validate(data, instructionCount, resultRegister);
                int webHandle = s_NextHandle++;
                s_Programs[webHandle] = new Compiled {
                    InstructionCount = instructionCount, ResultRegister = resultRegister,
                    UniformNames = uniformNames, Hash = hash,
                };
                return webHandle;
            }
            Validate(data, instructionCount, resultRegister);

            var c = new Compiled {
                InstructionCount = instructionCount,
                ResultRegister = resultRegister,
                UniformNames = uniformNames,
                Hash = hash,
            };

            // THE EJECT PATH. A project with an editor generates a shader per
            // program at import time, and its player builds ship them; this
            // looks for one and uses it when it is there. Play has no such
            // shader and gets the VM. The caller cannot tell the difference,
            // which is the entire point: an author writes one program and never
            // learns that two backends exist.
            Shader native = FindGenerated(hash);
            if (native != null) {
                c.Native = true;
                c.Material = new Material(native);
                BindUniformIds(c);
                int nativeHandle = s_NextHandle++;
                s_Programs[nativeHandle] = c;
                return nativeHandle;
            }

            if (!VmAllowed) {
                AwaitShader(c);
                int waitingHandle = s_NextHandle++;
                s_Programs[waitingHandle] = c;
                return waitingHandle;
            }
            if (VmShader == null) {
                throw new InvalidOperationException(
                    "[OneJS sl] OneJS/FxProgram.shader is missing from Resources. " +
                    "Without it a program cannot run at all.");
            }
            CheckWire(wire);
            WarnVmFallback(hash);
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
            if (c.Material != null) c.Material.SetVectorArray(s_Uniforms, c.Uniforms);
        }

        public static void SetTexture(int handle, int slot, Texture tex) {
            if (!s_Programs.TryGetValue(handle, out var c)) return;
            if (slot < 0 || slot >= MaxTextures) {
                throw new ArgumentException(
                    $"[OneJS sl] texture slot {slot} is outside 0..{MaxTextures - 1}. " +
                    "The VM binds its samplers by name, so this is a fixed set rather than a budget.");
            }
            if (c.Material != null) c.Material.SetTexture(s_TexIds[slot], tex);
            c.Textures[slot] = tex;
        }

        /// <summary>
        /// Hands over the program as WGSL and GLSL ES, which a WebGL player
        /// compiles and draws in place of the VM (<see cref="SLWeb"/>). Does
        /// nothing anywhere else, so a host can call it unconditionally.
        /// </summary>
        public static void SetWebSource(int handle, string wgsl, string glsl) {
            if (!s_Programs.TryGetValue(handle, out var c) || c.Native) return;
            if (string.IsNullOrEmpty(wgsl) && string.IsNullOrEmpty(glsl)) return;
            if (!SLWeb.Available) return;
            SLWeb.Release(c.WebId);
            c.WebId = SLWeb.Create(wgsl, glsl);
        }

        /// <summary>
        /// False forces the VM even where the program could run compiled.
        /// Ignored where there is no VM (<see cref="CompiledOnly"/>).
        /// </summary>
        public static void SetCompiledAllowed(int handle, bool allowed) {
            if (s_Programs.TryGetValue(handle, out var c)) c.WebAllowed = allowed || CompiledOnly;
        }

        /// <summary>True when the program has no VM material and draws only compiled.</summary>
        public static bool HasNoVm(int handle) =>
            s_Programs.TryGetValue(handle, out var c) && c.Material == null;

        /// <summary>
        /// True when the program draws compiled: through a generated shader, or,
        /// in a WebGL player, when its last frame was drawn by the page.
        /// </summary>
        public static bool IsCompiled(int handle) =>
            s_Programs.TryGetValue(handle, out var c) && DrawsCompiled(c);

        static bool DrawsCompiled(Compiled c) => c.Native || c.DrewCompiled;

        /// <summary>
        /// Sorts every live program's hash into the ones drawing compiled, the
        /// ones drawing on the VM and the ones drawing nothing. For a player
        /// build test, which has to assert on every program rather than the one
        /// it happened to look at.
        /// </summary>
        public static void Census(ICollection<string> compiled, ICollection<string> vm,
                                  ICollection<string> nothing = null) {
            foreach (var c in s_Programs.Values) {
                var into = DrawsCompiled(c) ? compiled : c.Material != null ? vm : nothing;
                into?.Add(c.Hash ?? "");
            }
        }

        /// <summary>
        /// The material a program draws with now, or null while it has none.
        ///
        /// Asked every frame by the element, because a program with no compiled
        /// shader gets its material later, when the editor has generated one
        /// (<see cref="AdoptGenerated"/>). A program the editor has waited on for
        /// a few seconds says so once, rather than staying blank without a word.
        /// </summary>
        public static Material CurrentMaterial(int handle) {
            if (!s_Programs.TryGetValue(handle, out var c)) return null;
            if (c.Material == null && c.AwaitingSince >= 0f &&
                Time.realtimeSinceStartup - c.AwaitingSince > MissingAfterSeconds) {
                c.AwaitingSince = -1f;
                SayMissing(c.Hash, editor: true);
            }
            return c.Material;
        }

        static readonly float[] s_Flat = new float[SLWeb.UniformFloats];

        /// <summary>
        /// Draws the compiled program into `target` when there is one and it is
        /// ready. False means the caller draws the VM this frame, which is what
        /// happens while a browser is still compiling and, for good, after a
        /// compile error (the host has said why).
        /// </summary>
        public static bool TryRenderCompiled(int handle, RenderTexture target, float seconds) {
            if (!s_Programs.TryGetValue(handle, out var c)) return false;
            c.DrewCompiled = false;
            if (c.WebId <= 0 || !c.WebAllowed) return false;
            for (int i = 0; i < MaxUniforms; i++) {
                var u = c.Uniforms[i];
                s_Flat[i * 4] = u.x; s_Flat[i * 4 + 1] = u.y; s_Flat[i * 4 + 2] = u.z; s_Flat[i * 4 + 3] = u.w;
            }
            int r = SLWeb.Draw(c.WebId, target, seconds, s_Flat, c.Textures);
            if (r < 0) {
                SLWeb.Release(c.WebId);
                c.WebId = 0;
            }
            c.DrewCompiled = r > 0;
            return c.DrewCompiled;
        }

        /// <summary>
        /// Renders the program into a target. `seconds` drives the time input.
        /// A program with no VM (<see cref="CompiledOnly"/>) draws compiled, and
        /// draws nothing until the page has compiled it: a caller reading back
        /// straight away renders again on a later frame, once
        /// <see cref="IsCompiled"/> says it drew.
        /// </summary>
        public static void Render(int handle, RenderTexture target, float seconds) {
            if (!s_Programs.TryGetValue(handle, out var c)) {
                throw new ArgumentException($"[OneJS sl] no program with handle {handle}.");
            }
            if (c.Material == null) {
                TryRenderCompiled(handle, target, seconds);
                return;
            }
            c.Material.SetFloat(s_Secs, seconds);
            // Both backends declare _Secs and _FlipY, so nothing here branches.
            // Never flipped, as in ShaderEffectElement: a Blit into a render
            // target puts v = 0 on texel row 0 on every API. Flipping where
            // graphicsUVStartsAtTop is true drew upside down on WebGPU (#127).
            c.Material.SetFloat(s_FlipY, 0f);
            // The target's size, because _ScreenParams is not it. Unity sets
            // that per camera and leaves it alone for a Blit, so a program
            // drawn into a 64x256 element read the game view's 1737x1226 and
            // `aspect` stretched every circle by the shape of the window. Both
            // backends read it from here now.
            c.Material.SetVector(s_Res, new Vector4(target.width, target.height, 0f, 0f));
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
