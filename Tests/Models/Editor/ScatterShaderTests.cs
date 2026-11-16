using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Tests.Models {
    /// <summary>
    /// What a draw of ModelLit and GroundLit carries per instance, read from the compiled variant's
    /// constant buffers. Unity uploads an instanced draw's arrays at their whole capacity whatever its
    /// count, so every array a pass declares is paid on every draw of every pass. A scattered copy
    /// (`_SCATTER`, without Unity's instancing) carries none at all: it reads its matrix, tint and baked
    /// light's rectangle from its scatter's copy texture, from the draw's first record. An actor drawn
    /// instanced keeps its arrays: its inverse matrix, its probe light, its layer and its dissolve.
    /// </summary>
    public class ScatterShaderTests {
        static readonly (ShaderCompilerPlatform platform, BuildTarget target)[] Targets = {
            (ShaderCompilerPlatform.WebGPU, BuildTarget.WebGL),
            (ShaderCompilerPlatform.GLES3x, BuildTarget.WebGL),
        };

        // What a copy never needs, each the per-instance array of one.
        static readonly string[] NotForACopy = {
            "unity_WorldToObjectArray", "unity_SHArArray", "unity_RenderingLayerArray", "_ActorDissolve",
        };

        static IEnumerable<string> Names(IEnumerable<ShaderData.ConstantInfo> fields) {
            foreach (var f in fields) {
                yield return f.Name;
                if (f.StructFields != null) foreach (var n in Names(f.StructFields)) yield return n;
            }
        }

        // Every constant the pass's two stages read, by name, and the bytes of the instancing buffers among them.
        static (HashSet<string> names, int instanced) Read(ShaderData.Pass pass, string[] keywords, ShaderCompilerPlatform platform, BuildTarget target) {
            var names = new HashSet<string>();
            int instanced = 0;
            foreach (var stage in new[] { ShaderType.Vertex, ShaderType.Fragment }) {
                var info = pass.CompileVariant(stage, keywords, platform, target);
                Assert.IsTrue(info.Success, $"{pass.Name} {stage} {string.Join(" ", keywords)} for {platform}");
                foreach (var cb in info.ConstantBuffers) {
                    var inCb = Names(cb.Fields).ToList();
                    names.UnionWith(inCb);
                    if (cb.Name.StartsWith("UnityInstancing")) instanced += cb.Size;
                }
            }
            return (names, instanced);
        }

        [TestCase("OneJS/ModelLit")]
        [TestCase("OneJS/GroundLit")]
        public void AScatteredCopy_CarriesItsMatrixAndNoArrayItNeverReads_AndAnActorKeepsThem(string name) {
            var shader = Resources.Load<Shader>(name);
            Assert.IsNotNull(shader, name);
            var sub = ShaderUtil.GetShaderData(shader).GetSubshader(0);
            int passes = 0;
            for (int p = 0; p < sub.PassCount; p++) {
                var pass = sub.GetPass(p);
                foreach (var (platform, target) in Targets) {
                    var actor = Read(pass, new[] { "INSTANCING_ON" }, platform, target);
                    var copy = Read(pass, new[] { "_SCATTER" }, platform, target);
                    TestContext.WriteLine($"{name} {pass.Name} {platform}: an actor's instancing buffers {actor.instanced} B, a copy's {copy.instanced} B");
                    Assert.That(copy.instanced, Is.Zero, $"{pass.Name} {platform}: a copy's draw has no per-instance arrays");
                    Assert.That(copy.names, Does.Contain("_OjScatterBase"), $"{pass.Name} {platform}: a copy reads its records from the draw's first");
                    foreach (var never in NotForACopy) Assert.That(copy.names, Does.Not.Contain(never), $"{pass.Name} {platform}");
                    if (pass.Name == "ForwardLit") {
                        // The actor's: its normals through the inverse, its probe light, its shadows and its dissolve.
                        foreach (var kept in NotForACopy) Assert.That(actor.names, Does.Contain(kept), $"an actor's {pass.Name} {platform}");
                    }
                    passes++;
                }
            }
            Assert.That(passes, Is.EqualTo(8), "four URP passes on two APIs");
        }
    }
}
