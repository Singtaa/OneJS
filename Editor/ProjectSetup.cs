using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OneJS.Editor {
    /// <summary>
    /// Initialize Project as a call, for a script, an agent or a batch-mode editor with no
    /// inspector to click. The inspector's button goes through <see cref="InitializeRunner"/>
    /// too, so the two cannot drift.
    ///
    /// Headless, in one line:
    /// <code>unity run . -- -nographics -executeMethod OneJS.Editor.ProjectSetup.Initialize</code>
    /// then <c>npm install &amp;&amp; npm run build</c> in the working directory it logs.
    /// </summary>
    public static class ProjectSetup {
        /// <summary>The GameObject a new runner goes on. Its name is also the app folder's.</summary>
        public const string DefaultAppName = "App";

        /// <summary>The scene made for a project that has none to offer.</summary>
        public const string DefaultScenePath = "Assets/Scenes/Main.unity";

        /// <summary>The inspector's "Use Scene Name as Root Folder" choice, which both paths honour.</summary>
        internal const string UseSceneNameAsRootFolderPrefKey = "OneJS.Initialize.UseSceneNameAsRootFolder";

        /// <summary>
        /// Sets OneJS up in this project with no inspector. Parameterless, so <c>-executeMethod</c>
        /// can call it.
        ///
        /// The scene is the active one when it is saved, otherwise the first enabled scene in the
        /// build list, otherwise a new <see cref="DefaultScenePath"/> (added to the build list when
        /// that list is empty). Every JSRunner in that scene is initialized; a scene with none gets
        /// one, on a new GameObject named <see cref="DefaultAppName"/>. In batch mode the scene is
        /// saved, since nobody is there to save it.
        ///
        /// Safe to call again: an initialized runner keeps its folder, no second runner is added,
        /// and a default file the user deleted stays deleted. npm is left to the caller, because a
        /// batch-mode editor quits when this returns; each working directory is logged for it.
        /// Throws when a runner could not be initialized, so a batch-mode run exits non-zero.
        /// </summary>
        public static void Initialize() => WithBareInfoLogs(InitializeScene);

        /// <summary>
        /// Writes each runner's <c>types/csharp.d.ts</c> from C# with no inspector, as the
        /// inspector's Generate Types Now and Tools > OneJS > Regenerate All Project Typings
        /// do, through the same <see cref="TypeGenerator.TypeGeneratorService.GenerateTypingsFor"/>.
        /// Parameterless, so <c>-executeMethod</c> can call it:
        /// <code>unity run . -- -nographics -executeMethod OneJS.Editor.ProjectSetup.GenerateTypings</code>
        ///
        /// It uses the scene <see cref="Initialize"/> would (never making one). A runner that lists
        /// no typing assemblies gets <c>Assembly-CSharp</c>, where a game's scripts live unless an
        /// asmdef claims them; <c>-typingAssemblies A,B</c> on the command line adds others. The
        /// list is saved on the runner, so the editor regenerates on every domain reload from then
        /// on. Throws when no runner could be typed, so a batch-mode run exits non-zero.
        /// </summary>
        public static void GenerateTypings() => WithBareInfoLogs(GenerateSceneTypings);

        static void GenerateSceneTypings() {
            var scene = OpenSceneForSetup(createIfNone: false);
            var runners = scene.IsValid()
                ? scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<JSRunner>(true)).Where(r => !r.IsCart).ToList()
                : new List<JSRunner>();
            if (runners.Count == 0)
                throw new InvalidOperationException("[OneJS] No JSRunner app to generate typings for. " +
                    "Run OneJS.Editor.ProjectSetup.Initialize first.");

            var requested = TypingAssembliesFromArgs(Environment.GetCommandLineArgs());
            var failed = new List<string>();
            foreach (var runner in runners) {
                var assemblies = TypingAssembliesFor(runner.TypingAssemblies, requested, AssemblyIsLoaded);
                if (!assemblies.SequenceEqual(runner.TypingAssemblies)) {
                    var so = new SerializedObject(runner);
                    var list = so.FindProperty("_typingAssemblies");
                    list.ClearArray();
                    for (var i = 0; i < assemblies.Count; i++) {
                        list.InsertArrayElementAtIndex(i);
                        list.GetArrayElementAtIndex(i).stringValue = assemblies[i];
                    }
                    so.ApplyModifiedProperties();
                }
                if (assemblies.Count == 0) {
                    Debug.LogWarning($"[OneJS] {runner.name} has no assembly to type: the project has no Assembly-CSharp. " +
                        "Name yours with -typingAssemblies MyGame.Runtime.", runner);
                    failed.Add(runner.name);
                } else if (!TypeGenerator.TypeGeneratorService.GenerateTypingsFor(runner)) {
                    failed.Add(runner.name);
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (Application.isBatchMode) EditorSceneManager.SaveScene(scene);

            if (failed.Count == runners.Count)
                throw new InvalidOperationException($"[OneJS] No typings were written for {string.Join(", ", failed)}; the warnings above say why.");
        }

        /// <summary>
        /// The assemblies a runner is typed from: its own list plus any named on the command line,
        /// each once, or the game's own scripts when it lists none and none were named.
        /// </summary>
        internal static List<string> TypingAssembliesFor(IReadOnlyList<string> current, IReadOnlyList<string> requested,
            Func<string, bool> exists) {
            var result = current.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToList();
            foreach (var name in requested)
                if (!result.Contains(name)) result.Add(name);
            if (result.Count == 0 && exists("Assembly-CSharp")) result.Add("Assembly-CSharp");
            return result;
        }

        /// <summary>The comma-separated names after <c>-typingAssemblies</c>, if the command line has one.</summary>
        internal static List<string> TypingAssembliesFromArgs(string[] args) {
            var at = Array.IndexOf(args, "-typingAssemblies");
            if (at < 0 || at + 1 >= args.Length) return new List<string>();
            return args[at + 1].Split(',').Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
        }

        static bool AssemblyIsLoaded(string name) =>
            AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == name);

        /// <summary>
        /// Runs setup with info lines logged without stack traces. A batch-mode log prints
        /// one under every line, and an agent reading it took the traces under "Created
        /// default file" for errors. Warnings and errors keep theirs.
        /// </summary>
        internal static void WithBareInfoLogs(Action setup) {
            var info = Application.GetStackTraceLogType(LogType.Log);
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            try {
                setup();
            } finally {
                Application.SetStackTraceLogType(LogType.Log, info);
            }
        }

        static void InitializeScene() {
            var scene = OpenSceneForSetup();
            var runners = scene.GetRootGameObjects()
                .SelectMany(go => go.GetComponentsInChildren<JSRunner>(true))
                .ToList();
            if (runners.Count == 0) runners.Add(CreateRunner(scene));

            var failed = new List<string>();
            foreach (var runner in runners) {
                var workingDir = InitializeRunner(runner);
                if (workingDir == null) failed.Add(runner.name);
                else Debug.Log($"[OneJS] {runner.name} in {scene.path} is set up. Working directory: {workingDir}\n" +
                    "Next, in that directory: npm install && npm run build");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (Application.isBatchMode) {
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }

            if (failed.Count > 0)
                throw new InvalidOperationException($"[OneJS] Setup failed for {string.Join(", ", failed)} in {scene.path}; " +
                    "the warnings above say why.");
        }

        /// <summary>
        /// What Initialize Project does to one runner, short of npm: lists any missing default
        /// templates, creates the project folder and its PanelSettings when none is assigned, and
        /// writes the default files a new app has never been given.
        ///
        /// Returns the working directory when it holds a package.json, ready for npm install and
        /// build. Returns null after logging why when it does not: no project folder could be
        /// resolved (an unsaved scene), the assigned PanelSettings is outside a project folder,
        /// or the app has lost its package.json.
        /// </summary>
        public static string InitializeRunner(JSRunner runner) {
            Undo.RecordObject(runner, "JSRunner Initialize Project");
            runner.AddMissingDefaultFiles();
            runner.EnsureProjectFolderAndAssets(EditorPrefs.GetBool(UseSceneNameAsRootFolderPrefKey, true));
            runner.EnsureProjectSetup();
            EditorUtility.SetDirty(runner);
            AssetDatabase.Refresh();

            var workingDir = runner.WorkingDirFullPath;
            if (string.IsNullOrEmpty(workingDir)) {
                // No working directory means nothing was initialized, which is not the same as a project
                // that simply has no package.json. Report the failure instead of claiming success.
                // The assigned-but-invalid case is already reported by EnsureProjectFolderAndAssets,
                // which names the folder and what it lacks; only the other case is left to report here.
                if (runner.GetInvalidProjectFolderReason() == null)
                    Debug.LogWarning("[JSRunner] Nothing was initialized: no project folder could be resolved. " +
                        "Save the scene first, then Initialize Project again.", runner);
                return null;
            }
            if (!File.Exists(Path.Combine(workingDir, "package.json"))) {
                // An app that already had its default files and has since lost package.json:
                // Initialize Project writes default files only for a new app, so say which are gone.
                Debug.LogWarning(runner.DescribeMissingDefaultFiles() ??
                    $"[JSRunner] {runner.name} has no package.json, so nothing was installed or built.", runner);
                return null;
            }
            return workingDir;
        }

        static Scene OpenSceneForSetup(bool createIfNone = true) {
            var active = SceneManager.GetActiveScene();
            if (active.IsValid() && !string.IsNullOrEmpty(active.path)) return active;

            var listed = EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled && File.Exists(s.path));
            if (listed != null) return EditorSceneManager.OpenScene(listed.path, OpenSceneMode.Single);
            if (!createIfNone) return default;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Directory.CreateDirectory(Path.GetDirectoryName(DefaultScenePath));
            if (!EditorSceneManager.SaveScene(scene, DefaultScenePath))
                throw new InvalidOperationException($"[OneJS] Setup failed: could not save a new scene to {DefaultScenePath}.");
            if (EditorBuildSettings.scenes.Length == 0)
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(DefaultScenePath, true) };
            Debug.Log($"[OneJS] Created {DefaultScenePath}, since the project had no scene to set up.");
            return scene;
        }

        static JSRunner CreateRunner(Scene scene) {
            var go = new GameObject(DefaultAppName);
            SceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, "Create OneJS App");
            return Undo.AddComponent<JSRunner>(go);
        }
    }
}
