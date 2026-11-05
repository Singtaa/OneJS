using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.TestTools;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// Compiles the project's scripts the way a player build does, for this editor's own
    /// standalone target, without building a player.
    ///
    /// Code that names something kept under <c>#if UNITY_EDITOR</c> from outside one compiles in
    /// the editor and passes every EditMode and PlayMode test, since all of those run editor
    /// code; only a player build fails. 3.9.17 shipped that way: JSRunner declared a
    /// BundleWatcher field outside the #if that BundleWatcher.cs sits in, and every player build
    /// stopped at CS0246. The player builds that would have caught it are [Explicit], because
    /// they take minutes, so a release gate skipped them. This takes seconds and runs in every
    /// EditMode battery.
    /// </summary>
    [TestFixture]
    public class PlayerCompileTests {
        const string OutputDir = "Temp/OneJSPlayerCompile";

        static BuildTarget HostTarget() {
#if UNITY_EDITOR_WIN
            return BuildTarget.StandaloneWindows64;
#elif UNITY_EDITOR_OSX
            return BuildTarget.StandaloneOSX;
#else
            return BuildTarget.StandaloneLinux64;
#endif
        }

        [Test]
        public void Runtime_CompilesForAPlayer() {
            if (Directory.Exists(OutputDir)) Directory.Delete(OutputDir, true);
            Directory.CreateDirectory(OutputDir);

            var errors = new List<string>();
            void Collect(string assembly, CompilerMessage[] messages) {
                errors.AddRange(messages.Where(m => m.type == CompilerMessageType.Error).Select(m => m.message));
            }

            // A failed compile logs its errors to the Console as well; this test reports them
            // itself, so the log does not fail it first with only the first line.
            LogAssert.ignoreFailingMessages = true;
            CompilationPipeline.assemblyCompilationFinished += Collect;
            ScriptCompilationResult result;
            try {
                var settings = new ScriptCompilationSettings {
                    target = HostTarget(),
                    group = BuildTargetGroup.Standalone,
                    options = ScriptCompilationOptions.None,
                };
                result = PlayerBuildInterface.CompilePlayerScripts(settings, OutputDir);
            } finally {
                CompilationPipeline.assemblyCompilationFinished -= Collect;
                LogAssert.ignoreFailingMessages = false;
            }

            Assert.That(errors, Is.Empty, $"The scripts do not compile for a {HostTarget()} player:\n{string.Join("\n", errors)}");
            // A compile that produced nothing would also have no errors to report, so the
            // assembly has to be there, written by this run.
            Assert.That(result.assemblies, Does.Contain("OneJS.Runtime.dll"),
                $"OneJS.Runtime was not compiled for a player. Compiled: {string.Join(", ", result.assemblies)}");
            Assert.That(File.Exists(Path.Combine(OutputDir, "OneJS.Runtime.dll")), Is.True,
                $"OneJS.Runtime.dll is not in {OutputDir}");
            Debug.Log($"[PlayerCompile] {result.assemblies.Count} assemblies compiled for {HostTarget()}");
        }
    }
}
