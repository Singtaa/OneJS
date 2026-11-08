using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Tests.Models {
    /// <summary>
    /// ModelLit and GroundLit compile, plain and instanced, for every graphics API a cart's models draw on. The
    /// play-mode tests draw it on this machine's API alone, and the Play container is WebGL 2 and
    /// WebGPU, where a pass that fails would surface only in a browser, as models that never draw.
    /// The per-actor values (dissolve, shadows, the baked light's rectangle) live in the instancing
    /// buffer, which is the variant those tests reach least.
    /// </summary>
    public class ModelLitCompileTests {
        static readonly (ShaderCompilerPlatform platform, BuildTarget target)[] Targets = {
            (ShaderCompilerPlatform.GLES3x, BuildTarget.WebGL),
            (ShaderCompilerPlatform.WebGPU, BuildTarget.WebGL),
            (ShaderCompilerPlatform.Vulkan, BuildTarget.Android),
            (ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64),
            (ShaderCompilerPlatform.Metal, BuildTarget.iOS),
        };

        // GroundLit is ModelLit's pass code with MODEL_SPLAT defined, so both are compiled.
        [TestCase("OneJS/ModelLit")]
        [TestCase("OneJS/GroundLit")]
        public void EveryPassCompilesPlainAndInstancedForWebGLWebGPUVulkanD3DAndMetal(string name) {
            var shader = Resources.Load<Shader>(name);
            Assert.IsNotNull(shader, name);
            var data = ShaderUtil.GetShaderData(shader);
            var compiled = 0;
            for (var s = 0; s < data.SubshaderCount; s++) {
                var sub = data.GetSubshader(s);
                for (var p = 0; p < sub.PassCount; p++) {
                    var pass = sub.GetPass(p);
                    foreach (var (platform, target) in Targets) {
                        foreach (var keywords in new[] { new string[0], new[] { "INSTANCING_ON" } }) {
                            foreach (var stage in new[] { ShaderType.Vertex, ShaderType.Fragment }) {
                                var info = pass.CompileVariant(stage, keywords, platform, target);
                                var errors = info.Messages.Where(m => m.severity == ShaderCompilerMessageSeverity.Error).Select(m => $"line {m.line}: {m.message}");
                                Assert.IsTrue(info.Success, $"subshader {s} pass {p} ({pass.Name}) {stage} {string.Join(" ", keywords)} for {platform}: {string.Join("; ", errors)}");
                                compiled++;
                            }
                        }
                    }
                }
            }
            Assert.Greater(compiled, 0, "no pass was compiled: the subshaders' package requirements left nothing");
        }
    }
}
