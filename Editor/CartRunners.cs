using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using OneJS.Utils;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OneJS.Editor {
    /// <summary>
    /// The editor side of root carts (Specs/ROOT_CARTS.md, 5.4): a cart builds with its own
    /// ojplay, the builder the site uses, never with OneJS's esbuild template.
    ///
    /// Set Up runs <c>ojplay init</c> (when the cart has no package.json) and <c>npm install</c>
    /// in the cart, which is what any machine does with a cart. The editor build goes to
    /// <c>Library/OneJS/Carts/&lt;folder&gt;</c> and the watcher rewrites it there, where the
    /// runner polls it for hot reload as it polls an app's app.js.txt. ojplay is run through
    /// npm (<c>npm exec</c>), so the cart's own copy runs and Windows and WSL go the way every
    /// other npm call here goes.
    ///
    /// Everything here acts on cart runners only. JSRunnerAutoWatch keeps apps under Assets
    /// and skips carts.
    /// </summary>
    [InitializeOnLoad]
    public static class CartRunners {
        /// <summary>The cart folders with a Set Up or a one-shot build running, so a second is not started.</summary>
        static readonly HashSet<string> _busy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static double _nextTick;
        const double TickInterval = 1.0;

        static CartRunners() {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            // The scan is cheap and has no cache that matters, but a folder renamed or an
            // oj.json edited outside Unity should show the moment the editor is looked at.
            EditorApplication.focusChanged += _ => RootCarts.Invalidate();
        }

        /// <summary>Whether a Set Up or a one-shot build is running for this cart.</summary>
        public static bool IsBusy(RootCarts.Cart cart) => cart != null && _busy.Contains(cart.FullPath);

        // MARK: Commands

        /// <summary>The npm arguments that run the cart's ojplay's Unity build into `outDir`.</summary>
        public static string BuildArguments(string outDir, bool watch, bool production) {
            var args = $"exec --no -- ojplay build --unity --out {QuotePath(outDir)}";
            if (watch) args += " --watch";
            if (production) args += " --production";
            return args;
        }

        /// <summary>A path as one argument, in the form the shell that runs npm reads.</summary>
        static string QuotePath(string path) {
#if UNITY_EDITOR_WIN
            // Under WSL npm runs inside bash -lc "...", so the path is the distro's and single quoted.
            if (OneJSWslHelper.UseWsl) return "'" + OneJSWslHelper.ToWslPath(path).Replace("'", "'\\''") + "'";
#endif
            return "\"" + path + "\"";
        }

        // MARK: Edit mode

        /// <summary>
        /// Once a second in edit mode: every cart runner that is set up gets its watcher, and a
        /// preview once its bundle exists. The watcher's first build is what builds a cart on a
        /// machine that has never built it, which is the cart's equivalent of an app's
        /// committed bundle; the preview's own start-up gives up after a few seconds, long
        /// before a first build lands.
        /// </summary>
        static void Tick() {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.timeSinceStartup < _nextTick) return;
            _nextTick = EditorApplication.timeSinceStartup + TickInterval;

            foreach (var runner in JSRunner.Instances.ToArray()) {
                if (runner == null || !runner.IsCart || !runner.enabled || !runner.gameObject.activeInHierarchy) continue;
                var cart = runner.CartFolder;
                if (cart == null || IsBusy(cart) || !RootCarts.IsSetUp(cart)) continue;
                EnsureWatcher(cart);
                if (runner.PanelSettingsAsset != null && File.Exists(runner.EntryFileFullPath ?? "")) runner.StartEditModePreviewIfReady();
            }
        }

        /// <summary>Starts the cart's ojplay watcher into its Library build folder, unless one runs.</summary>
        public static bool EnsureWatcher(RootCarts.Cart cart) {
            if (NodeWatcherManager.IsRunning(cart.FullPath) || NodeWatcherManager.IsStarting(cart.FullPath)) return true;
            var outDir = RootCarts.EditorBuildDir(cart);
            Directory.CreateDirectory(outDir);
            return NodeWatcherManager.StartWatcher(cart.FullPath, BuildArguments(outDir, watch: true, production: false));
        }

        // MARK: Play mode

        static void OnPlayModeStateChanged(PlayModeStateChange state) {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            RootCarts.Invalidate();
            foreach (var runner in FindCompat.FindObjectsByType<JSRunner>()) {
                if (runner == null || !runner.IsCart || !runner.enabled || !runner.gameObject.activeInHierarchy) continue;
                var cart = runner.CartFolder;
                // A name that resolves to no cart, or two, is the runner's to report: it does not start.
                if (cart == null) continue;
                PrepareForPlay(runner, cart);
            }
        }

        /// <summary>
        /// What Play does for a cart unprompted, as it does for an app with no node_modules:
        /// Set Up when the tooling is missing, a build when there is no bundle, then the watcher.
        /// The runner is reloaded once its first bundle lands.
        /// </summary>
        static void PrepareForPlay(JSRunner runner, RootCarts.Cart cart) {
            if (IsBusy(cart)) return;
            var hadBundle = File.Exists(runner.EntryFileFullPath ?? "");
            void WatchAndReload() {
                if (!EditorApplication.isPlaying || runner == null) return;
                if (!hadBundle) runner.ForceReload();
                EnsureWatcher(cart);
            }
            if (!RootCarts.IsSetUp(cart)) {
                SetUp(cart, onDone: ok => { if (ok) BuildOnce(cart, ok2 => { if (ok2) WatchAndReload(); }); });
            } else if (!hadBundle) {
                BuildOnce(cart, ok => { if (ok) WatchAndReload(); });
            } else {
                WatchAndReload();
            }
        }

        // MARK: Set Up and one-shot builds

        /// <summary>
        /// Installs the cart's tooling: <c>ojplay init</c> when it has no package.json (init
        /// never overwrites a file), <c>npm install</c>, and an ojplay new enough for the Unity
        /// target when the installed one is older. Asynchronous; `onDone` runs on the main thread.
        /// </summary>
        public static void SetUp(RootCarts.Cart cart, Action<bool> onDone = null) {
            if (!_busy.Add(cart.FullPath)) return;
            var steps = new List<(string args, string what)>();
            if (!File.Exists(Path.Combine(cart.FullPath, "package.json")))
                steps.Add(("exec --yes -- ojplay init", "ojplay init"));
            steps.Add(("install", "npm install"));
            void Finish(bool ok) {
                _busy.Remove(cart.FullPath);
                RootCarts.Invalidate();
                onDone?.Invoke(ok);
            }
            var updated = false;
            void Next(int i) {
                if (i < steps.Count) {
                    Debug.Log($"[JSRunner] {cart.Name}: {steps[i].what} in {cart.Folder}/...");
                    RunNpm(cart.FullPath, steps[i].args, code => {
                        if (code == 0) {
                            Next(i + 1);
                        } else {
                            Debug.LogError($"[JSRunner] {cart.Name}: {steps[i].what} failed in {cart.Folder}/ (exit code {code}).");
                            Finish(false);
                        }
                    });
                    return;
                }
                // npm install installs what package.json names. An ojplay older than the Unity
                // target is moved on once, under devDependencies where init put it, since a
                // bare install can move it to dependencies.
                if (!updated && !RootCarts.IsSetUp(cart)) {
                    updated = true;
                    steps.Add(("install --save-dev ojplay@latest", "npm install --save-dev ojplay@latest"));
                    Next(i);
                    return;
                }
                var ok = RootCarts.IsSetUp(cart);
                if (ok) Debug.Log($"[JSRunner] {cart.Name} is set up in {cart.Folder}/.");
                else Debug.LogError($"[JSRunner] {cart.Name} is still not set up: {SetUpProblem(cart)}");
                Finish(ok);
            }
            Next(0);
        }

        /// <summary>What Set Up would fix, phrased for a log line, or null when the cart is set up.</summary>
        public static string SetUpProblem(RootCarts.Cart cart) {
            if (!Directory.Exists(Path.Combine(cart.FullPath, "node_modules"))) return $"{cart.Name} has no node_modules yet.";
            var version = RootCarts.InstalledOjplay(cart);
            if (version == null) return $"{cart.Name} has no ojplay in its node_modules.";
            if (RootCarts.CompareVersions(version, RootCarts.MinOjplayVersion) < 0)
                return $"{cart.Name}'s ojplay is {version}, and a cart runner needs {RootCarts.MinOjplayVersion} or newer.";
            return null;
        }

        /// <summary>One editor build of the cart into Library, asynchronously. Stops a running watcher first and restarts it after.</summary>
        public static void BuildOnce(RootCarts.Cart cart, Action<bool> onDone = null) {
            if (!_busy.Add(cart.FullPath)) return;
            var outDir = RootCarts.EditorBuildDir(cart);
            Directory.CreateDirectory(outDir);
            Debug.Log($"[JSRunner] Building {cart.Name} into {RootCarts.EditorBuildRoot}/{cart.Folder}/...");
            RunNpm(cart.FullPath, BuildArguments(outDir, watch: false, production: false), code => {
                _busy.Remove(cart.FullPath);
                if (code == 0) Debug.Log($"[JSRunner] Built {cart.Name}.");
                else Debug.LogError($"[JSRunner] {cart.Name} does not build (ojplay exited {code}); its output is above.");
                onDone?.Invoke(code == 0);
            });
        }

        /// <summary>Runs npm in a folder, logging its output; `onExit` gets the exit code on the main thread, -1 when npm could not start.</summary>
        static void RunNpm(string workingDir, string arguments, Action<int> onExit) {
            try {
                var startInfo = OneJSWslHelper.CreateNpmProcessStartInfo(workingDir, arguments, NodeWatcherManager.GetNpmExecutable());
                var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                process.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.Log($"[npm] {e.Data}"); };
                process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.Log($"[npm] {e.Data}"); };
                process.Exited += (_, _) => EditorApplication.delayCall += () => onExit(process.ExitCode);
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            } catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException) {
                // Expected failure: npm could not be started (not installed, not on PATH).
                Debug.LogError($"[JSRunner] Could not run npm {arguments}: {e.Message}. Make sure Node.js is installed and on PATH.");
                onExit(-1);
            }
        }

        // MARK: Player builds

        /// <summary>How long a cart's production build may take.</summary>
        const int PlayerBuildTimeoutMs = 5 * 60 * 1000;

        /// <summary>
        /// The cart's production build into <c>Assets/OneJS.Generated/Carts/&lt;folder&gt;</c>, for a
        /// player. Synchronous; throws <see cref="BuildFailedException"/> when the cart is not set
        /// up or does not build, because a cart has no committed bundle to fall back on.
        /// </summary>
        internal static void BuildForPlayer(RootCarts.Cart cart, string outDir) {
            var problem = SetUpProblem(cart);
            if (problem != null) {
                throw new BuildFailedException($"[JSRunner] {problem} Press Set Up on a runner of it, or run " +
                    $"ojplay init and npm install in {cart.Folder}/, then build again.");
            }
            Directory.CreateDirectory(outDir);
            ProcessStartInfo startInfo;
            try {
                startInfo = OneJSWslHelper.CreateNpmProcessStartInfo(cart.FullPath,
                    BuildArguments(outDir, watch: false, production: true), NodeWatcherManager.GetNpmExecutable());
            } catch (Exception e) when (e is Win32Exception || e is InvalidOperationException) {
                throw new BuildFailedException($"[JSRunner] Could not run npm to build {cart.Name}: {e.Message}");
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
                    throw new BuildFailedException($"[JSRunner] Could not run npm to build {cart.Name}: {e.Message}");
                }
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(PlayerBuildTimeoutMs)) {
                    OneJSProcessUtils.KillProcessTree(process);
                    throw new BuildFailedException($"[JSRunner] The production build of {cart.Name} did not finish in " +
                        $"{PlayerBuildTimeoutMs / 60000} minutes. Output so far:\n{Tail(output)}");
                }
                process.WaitForExit();
                exitCode = process.ExitCode;
            }
            if (exitCode != 0) {
                throw new BuildFailedException($"[JSRunner] {cart.Name} does not build, so the player was not built. " +
                    $"ojplay build --unity --production in {cart.Folder}/ exited {exitCode}:\n{Tail(output)}");
            }
            if (!File.Exists(Path.Combine(outDir, "app.js.txt"))) {
                throw new BuildFailedException($"[JSRunner] ojplay built {cart.Name} but wrote no app.js.txt into {outDir}.");
            }
        }

        static string Tail(StringBuilder output) {
            string text;
            lock (output) text = output.ToString();
            var lines = text.TrimEnd().Split('\n');
            return string.Join("\n", lines.Skip(Math.Max(0, lines.Length - 40)));
        }

        // MARK: files.json

        [Serializable]
        class FilesJson {
            public int version;
            public string @base;
            public FileEntry[] files;
        }

        [Serializable]
        class FileEntry {
            public string from;
            public string to;
        }

        /// <summary>
        /// The files a cart ships, from the files.json its build wrote: each source under the
        /// cart, and where it goes under StreamingAssets. The list follows the site's serving
        /// rule and adds the cart's used carts, so it is not decided here.
        /// </summary>
        internal static List<(string source, string destination)> ReadFiles(RootCarts.Cart cart, string filesJson) {
            if (!File.Exists(filesJson))
                throw new BuildFailedException($"[JSRunner] ojplay built {cart.Name} but wrote no files.json beside its bundle.");
            FilesJson parsed;
            try {
                parsed = JsonUtility.FromJson<FilesJson>(File.ReadAllText(filesJson));
            } catch (ArgumentException e) {
                throw new BuildFailedException($"[JSRunner] {filesJson} is not valid JSON: {e.Message}");
            }
            if (parsed == null || parsed.version != 1 || string.IsNullOrEmpty(parsed.@base))
                throw new BuildFailedException($"[JSRunner] {filesJson} is not a version 1 files.json with a base.");
            var expected = RootCarts.PlayerFilesRoot + "/" + cart.Folder;
            if (parsed.@base != expected)
                throw new BuildFailedException($"[JSRunner] {filesJson} puts {cart.Name}'s files under {parsed.@base}, not {expected}.");

            var result = new List<(string, string)>();
            var cartRoot = Path.GetFullPath(cart.FullPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var entry in parsed.files ?? new FileEntry[0]) {
                if (string.IsNullOrEmpty(entry?.from) || string.IsNullOrEmpty(entry.to))
                    throw new BuildFailedException($"[JSRunner] {filesJson} has an entry without both from and to.");
                var source = Path.GetFullPath(Path.Combine(cart.FullPath, entry.from));
                if (!source.StartsWith(cartRoot, StringComparison.Ordinal) || entry.to.Split('/').Contains(".."))
                    throw new BuildFailedException($"[JSRunner] {filesJson} names a file outside the cart: {entry.from} to {entry.to}.");
                if (!File.Exists(source))
                    throw new BuildFailedException($"[JSRunner] {filesJson} lists {entry.from}, which is not in {cart.Folder}/.");
                result.Add((source, parsed.@base + "/" + entry.to));
            }
            return result;
        }
    }
}
