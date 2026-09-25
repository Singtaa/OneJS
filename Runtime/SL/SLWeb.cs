using System.Runtime.InteropServices;
using UnityEngine;

namespace OneJS.SL {
    /// <summary>
    /// Shader language programs compiled by the browser, on Unity's own device.
    ///
    /// The VM exists because Unity cannot compile a shader in a player. A
    /// browser can, so on WebGL a program also carries WGSL and GLSL ES
    /// (printed at build time by onejs-unity), and `Plugins/WebGL/OneJSSLWeb.jslib`
    /// compiles whichever one the device speaks and draws it into the element's
    /// RenderTexture. A WebGL player has no VM for programs
    /// (<see cref="SLProgramBridge.CompiledOnly"/>) unless it is built with
    /// ONEJS_SL_WEB_VM, which keeps it as the fallback for comparing the two.
    ///
    /// The host reads three handles private to Unity's framework, so whether
    /// any of this works is checked once, at first use, by drawing nothing:
    /// <see cref="Describe"/> makes a small RenderTexture and asks the host
    /// whether it can see it. A Unity upgrade that renames a handle then reads
    /// "unavailable" here (and in the container's smoke test) instead of
    /// drawing wrong.
    /// </summary>
    public static class SLWeb {
        /// <summary>Uniform slots, each a float4. Must match WEB_UNIFORM_SLOTS in onejs-unity.</summary>
        public const int UniformFloats = 16 * 4;
        /// <summary>Ints per texture slot handed to the host: native pointer, wrapU, wrapV, filter, has mips.</summary>
        const int TextureInts = 5;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern string OneJS_SLWeb_Check(int nativeId, int webgpu);
        [DllImport("__Internal")] static extern int OneJS_SLWeb_Create(string wgsl, string glsl, int webgpu);
        [DllImport("__Internal")] static extern int OneJS_SLWeb_Draw(int id, int target, int w, int h, float secs,
            int linear, float[] uniforms, int[] textures, int textureCount);
        [DllImport("__Internal")] static extern void OneJS_SLWeb_Release(int id);
        [DllImport("__Internal")] static extern void OneJS_SLWeb_SetRestore(int on);
#endif

        static string s_Description;
        static bool s_Available;
        static readonly int[] s_Textures = new int[16 * TextureInts];

        static bool IsWebGPU => SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.WebGPU;

        /// <summary>
        /// "webgpu ok &lt;format&gt;", "webgl2 ok", or "unavailable: &lt;why&gt;".
        /// The startup handle check; run once and remembered.
        /// </summary>
        public static string Describe() {
            if (s_Description != null) return s_Description;
#if UNITY_WEBGL && !UNITY_EDITOR
            var probe = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32) {
                name = "OneJS sl web probe", hideFlags = HideFlags.HideAndDontSave,
            };
            probe.Create();
            try {
                s_Description = OneJS_SLWeb_Check(NativeId(probe), IsWebGPU ? 1 : 0);
            } finally {
                probe.Release();
                Object.Destroy(probe);
            }
            s_Available = s_Description == "webgl2 ok" || s_Description.StartsWith("webgpu ok ");
            if (!s_Available) Debug.LogWarning($"[OneJS sl] compiled programs are {s_Description}.");
#else
            s_Description = "unavailable: not a WebGL player";
#endif
            return s_Description;
        }

        /// <summary>True when this player can draw compiled programs.</summary>
        public static bool Available {
            get {
                if (s_Description == null) Describe();
                return s_Available;
            }
        }

        /// <summary>A program id for the current device, or 0 when there is none.</summary>
        internal static int Create(string wgsl, string glsl) {
            if (!Available) return 0;
#if UNITY_WEBGL && !UNITY_EDITOR
            return OneJS_SLWeb_Create(wgsl ?? "", glsl ?? "", IsWebGPU ? 1 : 0);
#else
            return 0;
#endif
        }

        /// <summary>1 drew, 0 not ready yet (draw the VM), -1 never will (draw the VM, stop asking).</summary>
        internal static int Draw(int id, RenderTexture target, float seconds, float[] uniforms, Texture[] textures) {
#if UNITY_WEBGL && !UNITY_EDITOR
            int count = 0;
            for (int i = 0; i < textures.Length; i++) {
                // An unset slot samples white, which is what the VM and a
                // generated shader declare for it. The host binds only the
                // slots the program samples.
                var t = textures[i] != null ? textures[i] : Texture2D.whiteTexture;
                int o = i * TextureInts;
                s_Textures[o] = NativeId(t);
                s_Textures[o + 1] = (int)t.wrapModeU;
                s_Textures[o + 2] = (int)t.wrapModeV;
                s_Textures[o + 3] = (int)t.filterMode;
                s_Textures[o + 4] = t.mipmapCount > 1 ? 1 : 0;
                count = i + 1;
            }
            int linear = QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0;
            return OneJS_SLWeb_Draw(id, NativeId(target), target.width, target.height, seconds,
                linear, uniforms, s_Textures, count);
#else
            return -1;
#endif
        }

        /// <summary>
        /// False stops a WebGL2 draw from putting Unity's GL state back. Only
        /// for the harness's negative control (`Tools/sl-web-parity`): Unity's
        /// frame must visibly break, or the checks that pass with it on are
        /// blind to a missing restore.
        /// </summary>
        public static void SetRestoreGLState(bool on) {
#if UNITY_WEBGL && !UNITY_EDITOR
            OneJS_SLWeb_SetRestore(on ? 1 : 0);
#endif
        }

        internal static void Release(int id) {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (id > 0) OneJS_SLWeb_Release(id);
#endif
        }

        /// <summary>
        /// The texture's name in the page's table. wasm32, so a pointer fits an
        /// int; on WebGL2 it indexes GL.textures, on WebGPU lib_webgpu's table.
        /// </summary>
        static int NativeId(Texture t) => (int)(long)t.GetNativeTexturePtr();
    }
}
