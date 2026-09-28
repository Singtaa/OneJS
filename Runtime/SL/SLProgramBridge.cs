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
    /// A program crosses from JS ONCE, not per frame. Uniforms cross when they
    /// change, diffed by value the way ShaderEffect's props are, so animating a
    /// slider does not rebuild a program.
    /// </summary>
    public static class SLProgramBridge {
        const int MaxUniforms = 16;
        const int MaxTextures = 4;

        static readonly int s_Secs = Shader.PropertyToID("_Secs");
        static readonly int s_FlipY = Shader.PropertyToID("_FlipY");
        static readonly int s_Res = Shader.PropertyToID("_Res");
        static readonly int[] s_TexIds = {
            Shader.PropertyToID("_Tex0"), Shader.PropertyToID("_Tex1"),
            Shader.PropertyToID("_Tex2"), Shader.PropertyToID("_Tex3"),
        };

        static readonly Dictionary<int, Compiled> s_Programs = new Dictionary<int, Compiled>();
        static int s_NextHandle = 1;

        class Compiled : IDisposable {
            /// <summary>The generated shader's material, or null until there is one.</summary>
            public Material Material;
            /// <summary>
            /// By slot. A generated shader takes each one by name as it is set;
            /// the page's program and a later adoption read them from here.
            /// </summary>
            public readonly Vector4[] Uniforms = new Vector4[MaxUniforms];
            /// <summary>True when a shader generated from this program was found.</summary>
            public bool Native;
            /// <summary>Uniform names, for the generated shader's per name properties.</summary>
            public string[] UniformNames;
            public int[] UniformIds;
            /// <summary>The program hash, which is what links it to a generated shader.</summary>
            public string Hash;
            /// <summary>By slot, for the compiled web path, which binds them itself.</summary>
            public readonly Texture[] Textures = new Texture[MaxTextures];
            /// <summary>The browser compiled program, 0 when there is none. See <see cref="SLWeb"/>.</summary>
            public int WebId;
            /// <summary>True when the last frame was drawn by the page.</summary>
            public bool DrewCompiled;
            /// <summary>
            /// When the editor started waiting for this program's shader, or -1.
            /// See <see cref="CurrentMaterial"/>.
            /// </summary>
            public float AwaitingSince = -1f;

            public void Dispose() {
                SLWeb.Release(WebId);
                WebId = 0;
                if (Material != null) UnityEngine.Object.DestroyImmediate(Material);
                Material = null;
            }
        }

        /// <summary>
        /// True in a WebGL player: a program there is drawn by the page
        /// (<see cref="SLWeb"/>), which compiles it from the WGSL and GLSL its
        /// host sends, so it never has a generated shader and nothing waits for
        /// one.
        ///
        /// A page that cannot compile (the startup handle check failed) draws
        /// nothing and says why; the Play container's smoke test is what
        /// catches that, and Tools/sl-web-parity in the container holds the
        /// compiled picture to onejs-sl's goldens.
        /// </summary>
        public static bool CompiledOnly =>
#if UNITY_WEBGL && !UNITY_EDITOR
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
                   "so the program will not draw.";
        }

        /// <summary>
        /// The name a shader generated from a program carries. The hash is the
        /// link between the two, and if it ever fails to match, the program
        /// finds no shader and draws nothing. That is why the hash is a Merkle
        /// hash over the graph rather than a walk of the node array, and why
        /// this string is written once here rather than spelled out at each
        /// call site.
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

        static readonly HashSet<string> s_WarnedMissing = new HashSet<string>();

        /// <summary>
        /// A program with no compiled shader draws nothing until one exists. The
        /// editor waits for it, because it is about to generate one
        /// (<see cref="CurrentMaterial"/>); a player never will, so it says so
        /// now.
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
                "build again.");
        }

        /// <summary>
        /// True when a compiled shader exists for this program and its material
        /// uses it.
        ///
        /// Worth exposing because a program with no shader draws nothing, and
        /// from the outside nothing and a transparent program look alike.
        /// </summary>
        public static bool IsNative(int handle) =>
            s_Programs.TryGetValue(handle, out var c) && c.Native;

        /// <summary>
        /// Set by an editor that can turn a program into a compiled shader.
        /// Null in a player, so a program in Play costs nothing here.
        ///
        /// Programs exist only once JavaScript has run, so nothing can find them
        /// by reading source. This is where they are found instead: the host
        /// asks <see cref="WantsSource"/> when it has just sent one, hands over
        /// the HLSL through <see cref="RecordSource"/>, and the editor side
        /// writes the manifest and generates the shader. That is how an ejected
        /// game ends up compiled without anybody writing a manifest.
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
        /// Gives every program whose shader now exists a material on it, and
        /// returns how many it gave one. The element picks the material up
        /// through <see cref="CurrentMaterial"/>. Uniforms carry over by being
        /// set again under the generated shader's per name properties, and
        /// textures by slot.
        /// </summary>
        public static int AdoptGenerated() {
            int adopted = 0;
            foreach (var c in s_Programs.Values) {
                if (c.Native || string.IsNullOrEmpty(c.Hash)) continue;
                var gen = FindGenerated(c.Hash);
                if (gen == null) continue;
                c.Material = new Material(gen) { hideFlags = HideFlags.HideAndDontSave };
                for (int t = 0; t < MaxTextures; t++) {
                    if (c.Textures[t] != null) c.Material.SetTexture(s_TexIds[t], c.Textures[t]);
                }
                c.AwaitingSince = -1f;
                c.Native = true;
                BindUniformIds(c);
                if (c.UniformIds != null) {
                    for (int u = 0; u < c.UniformIds.Length && u < MaxUniforms; u++) {
                        c.Material.SetVector(c.UniformIds[u], c.Uniforms[u]);
                    }
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
        /// A material for a program, AND a handle that can set its uniforms.
        /// </summary>
        /// <remarks>
        /// The material is null until there is a generated shader. In a WebGL
        /// player the page draws the program (<see cref="TryRenderCompiled"/>)
        /// once its host hands over the WGSL and GLSL; in the editor it waits for
        /// the shader the editor is about to generate; a native player whose
        /// build left it out says so once. `native` says which.
        ///
        /// The handle is the part that was once missing. This used to hand back
        /// a bare material and register nothing, so SetUniform had no program to
        /// find and every uniform stayed at whatever the shader defaulted to.
        /// Registering here keeps the material the caller renders with and the
        /// material SetUniform writes to the same object.
        /// </remarks>
        public static Material CreateMaterial(string hash, out bool native, out int handle,
                                              string[] uniformNames = null) {
            handle = Register(hash, uniformNames);
            var c = s_Programs[handle];
            native = c.Native;
            return c.Material;
        }

        /// <summary>Registers a program and returns its handle, for a caller that draws through <see cref="Render"/>.</summary>
        public static int Upload(string hash, string[] uniformNames = null) => Register(hash, uniformNames);

        static int Register(string hash, string[] uniformNames) {
            var why = Unrunnable();
            if (why != null) throw new InvalidOperationException(why);

            var c = new Compiled { UniformNames = uniformNames, Hash = hash };
            Shader gen = FindGenerated(hash);
            if (gen != null) {
                c.Native = true;
                c.Material = new Material(gen);
                BindUniformIds(c);
            } else if (!CompiledOnly) {
                AwaitShader(c);
            }
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
            // By slot here, by name on a generated shader, which carries one
            // property per uniform. The page's program reads the slots, and so
            // does an adoption, which sets them again by name.
            c.Uniforms[slot] = new Vector4(x, y, z, w);
            if (c.Native && c.UniformIds != null && slot < c.UniformIds.Length) {
                c.Material.SetVector(c.UniformIds[slot], c.Uniforms[slot]);
            }
        }

        public static void SetTexture(int handle, int slot, Texture tex) {
            if (!s_Programs.TryGetValue(handle, out var c)) return;
            if (slot < 0 || slot >= MaxTextures) {
                throw new ArgumentException(
                    $"[OneJS sl] texture slot {slot} is outside 0..{MaxTextures - 1}. " +
                    "A program's textures are _Tex0 to _Tex3 on every backend, so this is a fixed set rather than a budget.");
            }
            if (c.Material != null) c.Material.SetTexture(s_TexIds[slot], tex);
            c.Textures[slot] = tex;
        }

        /// <summary>
        /// Hands over the program as WGSL and GLSL ES, which a WebGL player
        /// compiles and draws (<see cref="SLWeb"/>), the only way a program
        /// draws there. Does nothing anywhere else, so a host can call it
        /// unconditionally.
        /// </summary>
        public static void SetWebSource(int handle, string wgsl, string glsl) {
            if (!s_Programs.TryGetValue(handle, out var c) || c.Native) return;
            if (string.IsNullOrEmpty(wgsl) && string.IsNullOrEmpty(glsl)) return;
            if (!SLWeb.Available) return;
            SLWeb.Release(c.WebId);
            c.WebId = SLWeb.Create(wgsl, glsl);
        }

        /// <summary>
        /// True while the handle names a live program: false once it has been
        /// released, and for every handle after <see cref="DisposeAll"/>.
        /// </summary>
        public static bool Exists(int handle) => s_Programs.ContainsKey(handle);

        /// <summary>
        /// True when the program draws compiled: through a generated shader, or,
        /// in a WebGL player, when its last frame was drawn by the page.
        /// </summary>
        public static bool IsCompiled(int handle) =>
            s_Programs.TryGetValue(handle, out var c) && DrawsCompiled(c);

        static bool DrawsCompiled(Compiled c) => c.Native || c.DrewCompiled;

        /// <summary>
        /// Sorts every live program's hash into the ones drawing compiled and
        /// the ones drawing nothing. For a player build test, which has to
        /// assert on every program rather than the one it happened to look at.
        /// </summary>
        public static void Census(ICollection<string> compiled, ICollection<string> nothing = null) {
            foreach (var c in s_Programs.Values) {
                var into = DrawsCompiled(c) ? compiled : nothing;
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
        /// Draws the page's compiled program into `target` when there is one and
        /// it is ready. False means it drew nothing: in a WebGL player the
        /// element then draws nothing this frame, which is what happens while
        /// the browser is still compiling and, for good, after a compile error
        /// (the host has said why). Elsewhere there is no page program, and the
        /// caller draws the program's material instead.
        /// </summary>
        public static bool TryRenderCompiled(int handle, RenderTexture target, float seconds) {
            if (!s_Programs.TryGetValue(handle, out var c)) return false;
            c.DrewCompiled = false;
            if (c.WebId <= 0) return false;
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
        /// A program with no material draws through the page where there is one
        /// (a WebGL player), and draws nothing until the page has compiled it: a
        /// caller reading back straight away renders again on a later frame,
        /// once <see cref="IsCompiled"/> says it drew.
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
            // Never flipped, as in ShaderEffectElement: a Blit into a render
            // target puts v = 0 on texel row 0 on every API. Flipping where
            // graphicsUVStartsAtTop is true drew upside down on WebGPU (#127).
            c.Material.SetFloat(s_FlipY, 0f);
            // The target's size, because _ScreenParams is not it. Unity sets
            // that per camera and leaves it alone for a Blit, so a program
            // drawn into a 64x256 element read the game view's 1737x1226 and
            // `aspect` stretched every circle by the shape of the window.
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
