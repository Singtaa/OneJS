using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Debug = UnityEngine.Debug;

[assembly: InternalsVisibleTo("OneJS.Tests.Editor")]

namespace OneJS.Editor {
    /// <summary>
    /// The bundle a player ships: a fresh <c>npm run build</c> with
    /// <c>NODE_ENV=production</c>, and the editor's own bundle put back afterwards.
    ///
    /// Without this a player shipped whatever <c>npm run watch</c> last wrote,
    /// which is React's development build: about 30% larger, and doing
    /// development-only work on every render on an interpreter with no JIT.
    /// It could also be out of date with the source, if the watcher was not
    /// running when the source last changed.
    ///
    /// The production build writes over the app's app.js.txt, because that file
    /// is the TextAsset the runner references. The development bundle is kept
    /// aside and restored once the player is built, so the editor goes on with
    /// React's warnings and a committed bundle is left as it was.
    /// </summary>
    internal static class PlayerBundle {
        internal enum Outcome {
            /// <summary>Built fresh for the player.</summary>
            Built,
            /// <summary>Not rebuilt; the bundle on disk ships. Reason says why.</summary>
            Skipped,
            /// <summary>npm could not be started; the bundle on disk ships.</summary>
            NpmUnavailable,
        }

        internal readonly struct Result {
            public readonly Outcome Outcome;
            public readonly string Reason;
            /// <summary>The bundle that ships carries React's development build.</summary>
            public readonly bool DevelopmentReact;

            public Result(Outcome outcome, string reason, bool developmentReact) {
                Outcome = outcome;
                Reason = reason;
                DevelopmentReact = developmentReact;
            }
        }

        /// <summary>Generous: esbuild takes a second or two, npm's own start up a few more.</summary>
        const int BuildTimeoutMs = 5 * 60 * 1000;

        /// <summary>Lines of the build's output a failure quotes.</summary>
        const int FailureOutputLines = 40;

        /// <summary>
        /// Where the editor's bundles wait while the player builds. SessionState,
        /// because a build that switches the active target reloads the domain
        /// before the player is built and the bundles come back.
        /// </summary>
        const string StashKey = "OneJS.PlayerBundle.EditorBundles";

        static readonly string StashDir = Path.Combine(Path.GetTempPath(), "OneJS-EditorBundles");

        /// <summary>
        /// The comment esbuild writes above each module of a bundle it does not
        /// minify, for React's development builds. A minified bundle is
        /// production already: esbuild defines NODE_ENV as production when it
        /// minifies.
        /// </summary>
        static readonly Regex DevelopmentReactModule = new Regex(@"/react[\w-]*\.development\.js", RegexOptions.Compiled);

        /// <summary>
        /// Builds one app for a player. Returns what happened; throws
        /// <see cref="BuildFailedException"/> when the app does not build, since a
        /// player carrying a bundle older than its source looks exactly like a
        /// player whose app is broken.
        ///
        /// An app that cannot be rebuilt here (no build script, dependencies not
        /// installed, no npm on the machine) ships the bundle on disk, as every
        /// build before 3.9.5 did, so a build machine with a committed bundle and
        /// no Node keeps working.
        /// </summary>
        internal static Result BuildForPlayer(string workingDir, string bundlePath, string appName) {
            if (string.IsNullOrEmpty(workingDir) || !Directory.Exists(workingDir))
                return Skip("it has no working directory", bundlePath);

            var packageJson = Path.Combine(workingDir, "package.json");
            if (!File.Exists(packageJson) || !HasBuildScript(File.ReadAllText(packageJson)))
                return Skip("its package.json has no build script", bundlePath);

            if (!Directory.Exists(Path.Combine(workingDir, "node_modules")))
                return Skip($"its dependencies are not installed (run npm install in {workingDir})", bundlePath);

            var mapPath = SourceMapOf(bundlePath);
            var stash = Stash(bundlePath, mapPath);

            ProcessStartInfo startInfo;
            try {
                startInfo = OneJSWslHelper.CreateNpmProcessStartInfo(workingDir, "run build", NodeWatcherManager.GetNpmExecutable());
            } catch (Exception e) {
                Unstash(stash);
                return new Result(Outcome.NpmUnavailable, e.Message, CarriesDevelopmentReact(ReadOrEmpty(bundlePath)));
            }
            startInfo.EnvironmentVariables["NODE_ENV"] = "production";
            // wsl.exe passes on only the variables WSLENV names.
            if (startInfo.FileName == "wsl.exe") {
                var wslEnv = Environment.GetEnvironmentVariable("WSLENV");
                startInfo.EnvironmentVariables["WSLENV"] = string.IsNullOrEmpty(wslEnv) ? "NODE_ENV/u" : wslEnv + ":NODE_ENV/u";
            }

            var output = new StringBuilder();
            int exitCode;
            using (var process = new Process { StartInfo = startInfo }) {
                DataReceivedEventHandler collect = (_, e) => {
                    if (e.Data == null) return;
                    lock (output) output.AppendLine(e.Data);
                };
                process.OutputDataReceived += collect;
                process.ErrorDataReceived += collect;
                try {
                    process.Start();
                } catch (Win32Exception e) {
                    Unstash(stash);
                    return new Result(Outcome.NpmUnavailable, e.Message, CarriesDevelopmentReact(ReadOrEmpty(bundlePath)));
                }
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(BuildTimeoutMs)) {
                    OneJSProcessUtils.KillProcessTree(process);
                    Unstash(stash);
                    throw new BuildFailedException(
                        $"[JSRunner] The production build of {appName} did not finish in {BuildTimeoutMs / 60000} minutes " +
                        $"(npm run build in {workingDir}). Output so far:\n{Tail(output)}");
                }
                // The parameterless wait flushes the redirected output.
                process.WaitForExit();
                exitCode = process.ExitCode;
            }

            if (exitCode != 0) {
                Unstash(stash);
                throw new BuildFailedException(
                    $"[JSRunner] {appName} does not build, so the player was not built rather than ship a bundle " +
                    $"older than its source. npm run build in {workingDir} exited {exitCode}:\n{Tail(output)}");
            }
            if (!File.Exists(bundlePath)) {
                Unstash(stash);
                throw new BuildFailedException(
                    $"[JSRunner] npm run build for {appName} succeeded but wrote no {bundlePath}. " +
                    "Its esbuild config must write the bundle there, as oneJSConfig does by default.");
            }

            var production = File.ReadAllText(bundlePath);
            Remember(stash, Hash(production));
            return new Result(Outcome.Built, "", CarriesDevelopmentReact(production));
        }

        /// <summary>
        /// Puts back every editor bundle a player build set aside, unless the
        /// file has changed since: a watcher that rebuilt it meanwhile wrote a
        /// newer development bundle than the one kept. Safe to call any number
        /// of times.
        /// </summary>
        internal static void RestoreEditorBundles() {
            foreach (var entry in Load()) {
                try {
                    if (File.Exists(entry.bundle) && Hash(File.ReadAllText(entry.bundle)) == entry.productionHash) {
                        Put(entry.keptBundle, entry.bundle);
                        Put(entry.keptMap, entry.map);
                    }
                } catch (Exception e) {
                    Debug.LogWarning($"[JSRunner] Could not put the editor's bundle back at {entry.bundle}: {e.Message}. " +
                        "The next build or watcher rebuild replaces it.");
                }
                Delete(entry.keptBundle);
                Delete(entry.keptMap);
            }
            ForgetEditorBundles();
        }

        /// <summary>Drops the record of kept bundles without restoring them.</summary>
        internal static void ForgetEditorBundles() {
            SessionState.EraseString(StashKey);
        }

        internal static bool CarriesDevelopmentReact(string bundle) =>
            !string.IsNullOrEmpty(bundle) && DevelopmentReactModule.IsMatch(bundle);

        internal static string DevelopmentReactAdvice(string appName) =>
            $"[JSRunner] {appName}'s player bundle carries React's development build, which is about 30% larger " +
            "and does extra work on every render. Its esbuild config does not pass NODE_ENV on to esbuild. " +
            "Build with oneJSConfig from onejs-unity/esbuild, as new projects do, or add " +
            "define: { \"process.env.NODE_ENV\": JSON.stringify(process.env.NODE_ENV || \"development\") } to the config.";

        // MARK: Helpers

        static Result Skip(string reason, string bundlePath) =>
            new Result(Outcome.Skipped, reason, CarriesDevelopmentReact(ReadOrEmpty(bundlePath)));

        /// <summary>A "build" entry inside package.json's "scripts".</summary>
        static bool HasBuildScript(string packageJson) =>
            Regex.IsMatch(packageJson, "\"scripts\"\\s*:\\s*\\{[^}]*\"build\"\\s*:", RegexOptions.Singleline);

        /// <summary>app.js.txt's map, app.js.map.txt, the name JSRunner gives it.</summary>
        static string SourceMapOf(string bundlePath) => bundlePath.EndsWith(".js.txt")
            ? bundlePath.Substring(0, bundlePath.Length - ".js.txt".Length) + ".js.map.txt"
            : bundlePath + ".map";

        static string ReadOrEmpty(string path) => File.Exists(path) ? File.ReadAllText(path) : "";

        static string Tail(StringBuilder output) {
            string text;
            lock (output) text = output.ToString();
            var lines = text.TrimEnd().Split('\n');
            return string.Join("\n", lines.Skip(Math.Max(0, lines.Length - FailureOutputLines)));
        }

        static string Hash(string text) {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }

        static void Put(string from, string to) {
            if (string.IsNullOrEmpty(from) || !File.Exists(from)) return;
            File.Copy(from, to, true);
            Reimport(to);
        }

        static void Delete(string path) {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
        }

        /// <summary>Tells Unity a bundle under the project changed; ignores any other path.</summary>
        static void Reimport(string path) {
            var relative = FileUtil.GetProjectRelativePath(path.Replace('\\', '/'));
            if (relative.StartsWith("Assets/") || relative.StartsWith("Packages/"))
                AssetDatabase.ImportAsset(relative, ImportAssetOptions.ForceSynchronousImport);
        }

        // MARK: Kept bundles

        [Serializable]
        class Entry {
            public string bundle;
            public string map;
            public string keptBundle;
            public string keptMap;
            public string productionHash;
        }

        [Serializable]
        class Entries {
            public List<Entry> items = new List<Entry>();
        }

        /// <summary>Copies the editor's bundle and map aside. Nothing is recorded until the build succeeds.</summary>
        static Entry Stash(string bundlePath, string mapPath) {
            Directory.CreateDirectory(StashDir);
            var id = Guid.NewGuid().ToString("N");
            var entry = new Entry { bundle = bundlePath, map = mapPath };
            if (File.Exists(bundlePath)) {
                entry.keptBundle = Path.Combine(StashDir, id + ".js.txt");
                File.Copy(bundlePath, entry.keptBundle, true);
            }
            if (File.Exists(mapPath)) {
                entry.keptMap = Path.Combine(StashDir, id + ".js.map.txt");
                File.Copy(mapPath, entry.keptMap, true);
            }
            return entry;
        }

        /// <summary>After a failed build: the editor's files back as they were, and nothing recorded.</summary>
        static void Unstash(Entry entry) {
            try {
                if (entry.keptBundle != null) File.Copy(entry.keptBundle, entry.bundle, true);
                if (entry.keptMap != null) File.Copy(entry.keptMap, entry.map, true);
            } finally {
                Delete(entry.keptBundle);
                Delete(entry.keptMap);
            }
        }

        /// <summary>
        /// Records a kept bundle for restoring. A first build of an app has no
        /// editor bundle to give back, so there is nothing to record.
        /// </summary>
        static void Remember(Entry entry, string productionHash) {
            if (entry.keptBundle == null) {
                Delete(entry.keptMap);
                return;
            }
            entry.productionHash = productionHash;
            var all = new Entries { items = Load() };
            all.items.Add(entry);
            SessionState.SetString(StashKey, JsonUtility.ToJson(all));
            // The player build's postprocess restores these. This covers a build
            // that fails after the bundles were built, which skips postprocess:
            // the next editor tick runs once the build returns either way.
            EditorApplication.delayCall -= RestoreEditorBundles;
            EditorApplication.delayCall += RestoreEditorBundles;
        }

        static List<Entry> Load() {
            var json = SessionState.GetString(StashKey, "");
            if (string.IsNullOrEmpty(json)) return new List<Entry>();
            return JsonUtility.FromJson<Entries>(json)?.items ?? new List<Entry>();
        }
    }
}
