using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// No shader OneJS ships holds a NaN bit pattern in a float for WebGL 2. An int FXC keeps in a
    /// typeless register reaches HLSLcc's GLSL as a float holding its bits, and a constant whose bits
    /// are a NaN (a -1 is) can be rewritten by the GPU: the light bake's "no hit" of -1 became a hit on
    /// every ray, and a WebGL 2 bake came out black while WebGPU's was lit.
    ///
    /// Every pass of every shader in the package, both stages, with no keywords and with each of the
    /// pass's keywords alone. The negative controls are two earlier LightBake.shaders: this scanner
    /// found 112 such constants in 3e2c4bd's, and 5 in 9a4d617's, where `incoming` held a bool as an
    /// all-ones mask. Neither is kept in the package, since it would be a shader this test walks;
    /// <see cref="TheScannerFindsANaNInEitherBitsToFloat"/> holds their lines.
    /// </summary>
    public class ShaderWebGL2NaNTests {
        static readonly Regex Hex = new Regex(@"0x([0-9A-Fa-f]{8})u?");

        static bool IsNaN(uint bits) => (bits & 0x7F800000u) == 0x7F800000u && (bits & 0x007FFFFFu) != 0;

        /// <summary>Each line of GLSL that reinterprets a NaN's bits as a float.</summary>
        static IEnumerable<string> NaNLines(string glsl) {
            foreach (var line in glsl.Split('\n')) {
                if (line.IndexOf("BitsToFloat", StringComparison.Ordinal) < 0) continue;
                if (Hex.Matches(line).Cast<Match>().Any(m => IsNaN(Convert.ToUInt32(m.Groups[1].Value, 16))))
                    yield return line.Trim();
            }
        }

        /// <summary>The package's folder, found from this file: under Assets/ in a checkout, Packages/ once installed.</summary>
        static string PackageRoot() {
            var paths = AssetDatabase.FindAssets($"{nameof(ShaderWebGL2NaNTests)} t:MonoScript")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith($"/Tests/Editor/{nameof(ShaderWebGL2NaNTests)}.cs", StringComparison.Ordinal))
                .ToArray();
            Assert.AreEqual(1, paths.Length, "this test's own script: " + string.Join(", ", paths));
            return paths[0].Substring(0, paths[0].Length - $"/Tests/Editor/{nameof(ShaderWebGL2NaNTests)}.cs".Length);
        }

        [Test]
        public void TheScannerFindsANaNInEitherBitsToFloat() {
            // Lines as HLSLcc wrote them for LightBake.shader: its int -1 before 9a4d617, and a bool it held as
            // an all-ones mask until the walk below found it.
            var glsl = string.Join("\n",
                "    u_xlat3.x = intBitsToFloat(int(0xFFFFFFFFu));",
                "    u_xlat31.x = uintBitsToFloat(uint(uint(floatBitsToUint(u_xlat27.x)) | (uint(u_xlatb43) * 0xffffffffu)));",
                "    u_xlatb1 = u_xlat3.x!=intBitsToFloat(int(0xFFFFFFFFu));",
                "    u_xlat0 = uintBitsToFloat(uvec4(0x3F800000u, 0x7FC00000u, 0u, 0u));",
                // Not NaNs: a one, an infinity, a NaN's bits kept in an int.
                "    u_xlat4 = intBitsToFloat(int(0x3F800000u));",
                "    u_xlat5 = uintBitsToFloat(0x7F800000u);",
                "    u_xlati6 = int(0xFFFFFFFFu);");
            Assert.AreEqual(new[] {
                "u_xlat3.x = intBitsToFloat(int(0xFFFFFFFFu));",
                "u_xlat31.x = uintBitsToFloat(uint(uint(floatBitsToUint(u_xlat27.x)) | (uint(u_xlatb43) * 0xffffffffu)));",
                "u_xlatb1 = u_xlat3.x!=intBitsToFloat(int(0xFFFFFFFFu));",
                "u_xlat0 = uintBitsToFloat(uvec4(0x3F800000u, 0x7FC00000u, 0u, 0u));",
            }, NaNLines(glsl).ToArray());
        }

        [Test]
        public void NoShaderInThePackageHoldsANaNPatternInAFloatForWebGL2() {
            var root = PackageRoot();
            var shaders = AssetDatabase.FindAssets("t:Shader", new[] { root })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".shader", StringComparison.Ordinal))
                .ToArray();
            // An empty walk passes anything, so the bake's shader has to be one it reached.
            Assert.That(shaders, Has.Some.EndsWith("/LightBake.shader"), $"the shaders under {root}");

            var found = new List<string>();
            var variants = 0;
            foreach (var path in shaders) {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                var data = ShaderUtil.GetShaderData(shader);
                for (var si = 0; si < data.SubshaderCount; si++) {
                    var sub = data.GetSubshader(si);
                    for (var pi = 0; pi < sub.PassCount; pi++) {
                        var pass = sub.GetPass(pi);
                        var id = new PassIdentifier((uint)si, (uint)pi);
                        foreach (var stage in new[] { ShaderType.Vertex, ShaderType.Fragment }) {
                            var sets = new List<string[]> { new string[0] };
                            sets.AddRange(ShaderUtil.GetPassKeywords(shader, id, stage).Select(k => k.name).Distinct().Select(k => new[] { k }));
                            foreach (var set in sets) {
                                var where = $"{path} subshader {si} pass {pi} {stage} [{string.Join(" ", set)}]";
                                // For an external tool: the GLSL as text, where a build's is packed.
                                var info = pass.CompileVariant(stage, set, ShaderCompilerPlatform.GLES3x, BuildTarget.WebGL, true);
                                var errors = info.Messages.Where(m => m.severity == ShaderCompilerMessageSeverity.Error).Select(m => m.message);
                                Assert.IsTrue(info.Success, $"{where}: {string.Join("; ", errors)}");
                                var glsl = Encoding.UTF8.GetString(info.ShaderData);
                                Assert.That(glsl, Does.Contain("void main"), $"{where} compiled to GLSL text");
                                variants++;
                                found.AddRange(NaNLines(glsl).Select(line => $"{where}: {line}"));
                            }
                        }
                    }
                }
            }
            Assert.IsEmpty(found, $"{found.Count} NaN patterns in {variants} variants of {shaders.Length} shaders:\n" + string.Join("\n", found.Take(20)));
            TestContext.Out.WriteLine($"{variants} variants of {shaders.Length} shaders under {root}, no NaN pattern");
        }
    }
}
