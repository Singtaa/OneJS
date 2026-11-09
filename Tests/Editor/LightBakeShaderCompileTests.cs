using System.Linq;
using NUnit.Framework;
using OneJS.Lighting;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
// Unity 6000.6 adds UnityEditor.Rendering.LightBaker; this file means OneJS's.
using LightBaker = OneJS.Lighting.LightBaker;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// The bake's passes compile for every graphics API a bake runs on. The GPU tests run on this
    /// machine's API alone, and the Play container is WebGL 2 and WebGPU, where a pass that does not
    /// compile would surface only in a browser, as a bake that never ends. ShaderWebGL2NaNTests holds
    /// every shader in the package, this one included, to what WebGL 2 does with the bits it compiles to.
    /// </summary>
    public class LightBakeShaderCompileTests {
        static readonly (ShaderCompilerPlatform platform, BuildTarget target)[] Targets = {
            (ShaderCompilerPlatform.GLES3x, BuildTarget.WebGL),
            (ShaderCompilerPlatform.WebGPU, BuildTarget.WebGL),
            (ShaderCompilerPlatform.Vulkan, BuildTarget.Android),
            (ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64),
            (ShaderCompilerPlatform.Metal, BuildTarget.iOS),
        };

        /// <summary>
        /// Every read of a texel's neighbour, in dilation and the denoiser, is skipped past the working
        /// textures' edge. A read there is undefined: WebGL 2 gives 0, which reads as uncovered, and no bake in
        /// the editor has shown it, but WebGPU gave a one-page bake's border texels another texel's light.
        /// </summary>
        [Test]
        public void EveryNeighbourReadIsBoundsChecked() {
            var path = AssetDatabase.GetAssetPath(Resources.Load<Shader>(LightBaker.ShaderPath));
            var lines = System.IO.File.ReadAllLines(path).Select(l => l.Trim()).ToArray();
            const string neighbour = "c + int2(x, y)";
            var reads = 0;
            for (var i = 0; i < lines.Length; i++) {
                if (!lines[i].Contains(neighbour) || lines[i].StartsWith("if (!inside(")) continue;
                reads++;
                Assert.AreEqual($"if (!inside({neighbour})) continue;", lines[i - 1], $"line {i + 1}, {lines[i]}, reads a neighbour without checking it is inside");
            }
            Assert.AreEqual(2, reads, "dilation's read and the denoiser's");
            Assert.Contains("bool inside(int2 k) { return k.x >= 0 && k.y >= 0 && k.x < _WorkWidth && k.y < _WorkHeight; }", lines, "inside() is the working textures' bounds");
        }

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
