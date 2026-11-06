using System.Linq;
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
    }
}
