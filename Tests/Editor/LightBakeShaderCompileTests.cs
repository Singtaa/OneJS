using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OneJS.Lighting;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// The bake's passes compile for every graphics API a bake runs on. The GPU tests run on this
    /// machine's API alone, and the Play container is WebGL 2 and WebGPU, where a pass that does not
    /// compile would surface only in a browser, as a bake that never ends.
    /// </summary>
    public class LightBakeShaderCompileTests {
        static readonly (ShaderCompilerPlatform platform, BuildTarget target)[] Targets = {
            (ShaderCompilerPlatform.GLES3x, BuildTarget.WebGL),
            (ShaderCompilerPlatform.WebGPU, BuildTarget.WebGL),
            (ShaderCompilerPlatform.Vulkan, BuildTarget.Android),
            (ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64),
            (ShaderCompilerPlatform.Metal, BuildTarget.iOS),
        };

        [Test]
        public void EveryPassCompilesForWebGLWebGPUVulkanD3DAndMetal() {
            var shader = Resources.Load<Shader>(LightBaker.ShaderPath);
            Assert.IsNotNull(shader, LightBaker.ShaderPath);
            var sub = ShaderUtil.GetShaderData(shader).GetSubshader(0);
            Assert.Greater(sub.PassCount, 0);
            for (var p = 0; p < sub.PassCount; p++) {
                var pass = sub.GetPass(p);
                foreach (var (platform, target) in Targets) {
                    foreach (var stage in new[] { ShaderType.Vertex, ShaderType.Fragment }) {
                        var info = pass.CompileVariant(stage, new string[0], platform, target);
                        var errors = info.Messages.Where(m => m.severity == ShaderCompilerMessageSeverity.Error).Select(m => $"line {m.line}: {m.message}");
                        Assert.IsTrue(info.Success, $"pass {p} {stage} for {platform}: {string.Join("; ", errors)}");
                    }
                }
            }
        }

        /// <summary>
        /// An int FXC keeps in a typeless register reaches HLSLcc's GLSL as a float holding its bits. A
        /// constant whose bits are a NaN (a -1 is) can be rewritten by the GPU, and the trace's "no hit"
        /// of -1 became a hit on every ray: a WebGL 2 bake came out black while WebGPU's was lit.
        /// </summary>
        [Test]
        public void NoPassHoldsANaNPatternInAFloatForWebGL2() {
            var shader = Resources.Load<Shader>(LightBaker.ShaderPath);
            var sub = ShaderUtil.GetShaderData(shader).GetSubshader(0);
            var bits = new Regex(@"intBitsToFloat\(int\(0x([0-9A-Fa-f]{8})u\)\)");
            for (var p = 0; p < sub.PassCount; p++) {
                // For an external tool: the GLSL as text, where a build's is packed.
                var info = sub.GetPass(p).CompileVariant(ShaderType.Fragment, new string[0], ShaderCompilerPlatform.GLES3x, BuildTarget.WebGL, true);
                Assert.IsTrue(info.Success, $"pass {p}");
                var glsl = Encoding.UTF8.GetString(info.ShaderData);
                Assert.That(glsl, Does.Contain("void main"), $"pass {p} compiled to GLSL text");
                foreach (Match m in bits.Matches(glsl)) {
                    var v = System.Convert.ToUInt32(m.Groups[1].Value, 16);
                    var nan = (v & 0x7F800000u) == 0x7F800000u && (v & 0x007FFFFFu) != 0;
                    Assert.IsFalse(nan, $"pass {p} holds 0x{v:X8}, a NaN, in a float: {m.Value}");
                }
            }
        }
    }
}
