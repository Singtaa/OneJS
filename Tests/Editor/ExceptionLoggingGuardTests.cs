using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OneJS.Editor;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// A caught exception is logged with its stack: OneJSLog.Exception (or
    /// Debug.LogException), never as text through Debug.Log, LogWarning or
    /// LogError, which drops the stack and the Console's links to it.
    ///
    /// The exceptions are expected failures, where the message is all a reader
    /// needs (a locked file, npm missing, the script's own input). Each is
    /// listed in <see cref="Kept"/> and carries an "Expected failure:" comment
    /// saying why, right above the log call.
    /// </summary>
    public class ExceptionLoggingGuardTests {
        /// <summary>File (from the package root) and a fragment of the kept log call.</summary>
        static readonly (string file, string fragment)[] Kept = {
            ("Runtime/GPU/GPUBridge.cs", "Failed to create buffer"),
            ("Runtime/GPU/GPUBridge.cs", "Failed to create RenderTexture"),
            ("Runtime/GPU/GPUBridge.cs", "Failed to resize RenderTexture"),
            ("Runtime/GPU/GPUBridge.cs", "Failed to request readback"),
            ("Runtime/JSRunner.cs", "Could not rename"),
            ("Runtime/Network.cs", "Failed to parse headers"),
            ("Runtime/QuickJSUIBridge.cs", "LoadStyleSheet error"),
            ("Runtime/ShaderFX/ShaderEffectElement.cs", "[OneJS sl]"),
            ("Runtime/SourceMapParser.cs", "Failed to load source map"),
            ("Runtime/StyleBridge.cs", "ApplyStyles failed"),
            ("Runtime/WebSocketBridge.cs", "Invalid base64 data"),
            ("Editor/AISkillsInstaller.cs", "Could not write"),
            ("Editor/AISkillsInstaller.cs", "No permission to write"),
            ("Editor/JSPadEditor.cs", "npm install error"),
            ("Editor/JSPadEditor.cs", "Build error"),
            ("Editor/JSPadEditor.cs", "Failed to clean"),
            ("Editor/JSRunnerAutoWatch.cs", "Failed to run npm"),
            ("Editor/JSRunnerEditor.cs", "Failed to delete pack folder"),
            ("Editor/JSRunnerEditor.cs", "' folder: {ex.Message}"),
            ("Editor/JSRunnerEditor.cs", "npm error"),
            ("Editor/JSRunnerEditor.cs", "Failed to delete node_modules"),
            ("Editor/JSRunnerEditor.cs", "LogWarning($\"[JSRunner] Failed to open code editor"),
            ("Editor/JSRunnerEditor.cs", "LogError($\"[JSRunner] Failed to open code editor"),
            ("Editor/NodeWatcherManager.cs", "Failed to reattach to watcher"),
            ("Editor/NodeWatcherManager.cs", "Failed to start watcher"),
            ("Editor/NodeWatcherManager.cs", "Error stopping watcher"),
            ("Editor/OneJSProcessUtils.cs", "taskkill"),
            ("Editor/PlayerBundle.cs", "Could not put the editor's bundle back"),
            ("Editor/Recording/PanelRecorder.cs", "Could not remove partial recording"),
            ("Editor/SLShaderGenerator.cs", "so its programs are left out"),
            ("Editor/SLShaderGenerator.cs", "could not read the program manifest"),
            ("Editor/TypeGenerator/Analysis/TypeAnalyzer.cs", "Skipping"),
            ("Editor/TypeGenerator/Analysis/TypeMapper.cs", "failed, using 'any'"),
            ("Tests/Editor/BuildValidationTests.cs", "Error listing .app contents"),
            ("Tests/Editor/BuildValidationTests.cs", "Process error"),
        };

        static readonly Regex Catch = new Regex(@"catch\s*\(\s*[\w.]+\s+(\w+)\s*\)\s*\{");
        static readonly Regex TextLog = new Regex(@"\bDebug\.Log(Error|Warning)?\s*\(");

        internal struct Site {
            public string File;
            public int Line;
            public string Call;
            public bool HasReason;
        }

        static string PackageRoot {
            get {
                var root = AISkillsInstaller.FindPackageRoot();
                Assert.IsNotNull(root, "Could not find the OneJS package root.");
                return root;
            }
        }

        /// <summary>Every Debug.Log, LogWarning or LogError in a catch block that mentions the caught exception.</summary>
        internal static List<Site> FindTextLoggedExceptions(string file, string source) {
            var sites = new List<Site>();
            foreach (Match c in Catch.Matches(source)) {
                var name = c.Groups[1].Value;
                int open = c.Index + c.Length - 1;
                int close = Matching(source, open, '{', '}');
                if (close < 0) continue;
                var block = source.Substring(open, close - open);
                var mentions = new Regex(@"\b" + Regex.Escape(name) + @"\b");
                foreach (Match log in TextLog.Matches(block)) {
                    int argsOpen = log.Index + log.Length - 1;
                    int argsClose = Matching(block, argsOpen, '(', ')');
                    if (argsClose < 0) continue;
                    var call = block.Substring(log.Index, argsClose - log.Index + 1);
                    if (!mentions.IsMatch(call.Substring(log.Length))) continue;
                    int at = open + log.Index;
                    int line = source.Take(at).Count(ch => ch == '\n') + 1;
                    var before = source.Substring(0, at).Split('\n');
                    var above = string.Join("\n", before.Skip(System.Math.Max(0, before.Length - 4)));
                    sites.Add(new Site { File = file, Line = line, Call = call, HasReason = above.Contains("Expected failure:") });
                }
            }
            return sites;
        }

        static int Matching(string s, int open, char o, char c) {
            int depth = 0;
            for (int i = open; i < s.Length; i++) {
                if (s[i] == o) depth++;
                else if (s[i] == c && --depth == 0) return i;
            }
            return -1;
        }

        static List<Site> AllSites() {
            var root = PackageRoot;
            var sites = new List<Site>();
            foreach (var dir in new[] { "Runtime", "Editor", "Tests" }) {
                foreach (var path in Directory.GetFiles(Path.Combine(root, dir), "*.cs", SearchOption.AllDirectories)) {
                    var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
                    // This file's negative control is source text that must be found
                    if (rel == "Tests/Editor/ExceptionLoggingGuardTests.cs") continue;
                    sites.AddRange(FindTextLoggedExceptions(rel, File.ReadAllText(path)));
                }
            }
            return sites;
        }

        static bool IsKept(Site s) => Kept.Any(k => k.file == s.File && s.Call.Contains(k.fragment));

        [Test]
        public void CaughtExceptions_AreLoggedWithTheirStack() {
            var offending = AllSites().Where(s => !IsKept(s)).ToList();
            Assert.IsEmpty(offending,
                "These log a caught exception as text, which drops its stack. Use OneJSLog.Exception " +
                "(or Debug.LogException); for an expected failure, add it to Kept with an " +
                "\"Expected failure:\" comment saying why:\n" +
                string.Join("\n", offending.Select(s => $"  {s.File}:{s.Line}  {s.Call}")));
        }

        [Test]
        public void KeptSites_SayWhyAndStillExist() {
            var sites = AllSites();
            foreach (var k in Kept) {
                var matches = sites.Where(s => s.File == k.file && s.Call.Contains(k.fragment)).ToList();
                Assert.IsNotEmpty(matches, $"Kept entry no longer matches a site, remove it: {k.file} \"{k.fragment}\"");
                foreach (var s in matches)
                    Assert.IsTrue(s.HasReason, $"{s.File}:{s.Line} is kept but has no \"Expected failure:\" comment above it");
            }
        }

        /// <summary>
        /// The native library reaches C# only through its string log callback, so
        /// a line without the error marker arrives as information. Every log call
        /// in quickjs_unity.c goes through log_error, which marks it, or through
        /// js_console_log, whose lines the bootstrap's console already marks; a
        /// callback's exception is not logged there at all but taken by C#.
        /// </summary>
        [Test]
        public void NativeLogLines_GoThroughTheLevelledPaths() {
            var path = Path.Combine(PackageRoot, "Auxiliary~", "quickjs-unity", "src", "quickjs_unity.c");
            var source = File.ReadAllText(path);
            var allowed = new[] { "static void log_error(", "static JSValue js_console_log(" };
            var functionStart = new Regex(@"^(static |QJS_API )[^;{]*\(", RegexOptions.Multiline);
            var offending = new List<string>();
            foreach (Match call in Regex.Matches(source, @"g_callbacks\.log\(")) {
                var enclosing = functionStart.Matches(source.Substring(0, call.Index)).Cast<Match>().LastOrDefault();
                var name = enclosing?.Value ?? "(file scope)";
                if (!allowed.Any(a => name.StartsWith(a))) {
                    int line = source.Take(call.Index).Count(ch => ch == '\n') + 1;
                    offending.Add($"  quickjs_unity.c:{line} in {name}");
                }
            }
            Assert.IsEmpty(offending, "Log an error line through log_error so it carries the error marker:\n" + string.Join("\n", offending));
        }

        [Test]
        public void Finder_CatchesATextLoggedException() {
            // The detector's own negative control: each of these must be found.
            const string source = @"
class A {
    void F() {
        try { G(); } catch (System.Exception ex) { Debug.LogError($""[A] failed: {ex}""); }
        try { G(); } catch (Exception e) {
            Debug.LogWarning(
                $""[A] failed: {e.Message}"");
        }
        try { G(); } catch (Exception ex) { OneJSLog.Exception(""[A] fine"", ex); }
        try { G(); } catch (Exception ex) { Debug.Log(""[A] unrelated""); }
    }
}";
            var sites = FindTextLoggedExceptions("A.cs", source);
            Assert.AreEqual(2, sites.Count, string.Join("\n", sites.Select(s => s.Call)));
        }
    }
}
