#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using System.Runtime.InteropServices;
using OneJS.Samples;
using Object = UnityEngine.Object;

namespace OneJS.CI {
    public class WorkflowTests {
        static readonly string TMP_TEST_WORKING_DIR = "ONEJS_TMP_WORKDIR_DELETE_ME";
        static Camera _mainCamera;
        static ScriptEngine _scriptEngine;
        static Bundler _bundler;
        static Runner _runner;
        static SampleCharacter _sampleCharacter;

        // May consider using `Application.logMessageReceived += Catch` for log catching
        // Also note that WaitForEndOfFrame is not supported in batchmode (CI)

        [OneTimeSetUp]
        public static void OneTimeSetUp() {
            var tmpWorkDirPath = Path.Combine(Path.GetDirectoryName(Application.dataPath)!,
                TMP_TEST_WORKING_DIR);
            if (Directory.Exists(tmpWorkDirPath)) {
                Directory.Delete(tmpWorkDirPath, true);
            }

            var cameraGO = new GameObject("Main Camera");
            _mainCamera = cameraGO.AddComponent<Camera>();
            var prefab = LoadFromGUID<GameObject>("f99b6aec6fc021f4c9572906776c6555");
            prefab.SetActive(false);
            var scriptEngineGO = Object.Instantiate(prefab);
            prefab.SetActive(true);

            _scriptEngine = scriptEngineGO.GetComponent<ScriptEngine>();
            _bundler = scriptEngineGO.GetComponent<Bundler>();
            _runner = scriptEngineGO.GetComponent<Runner>();

            _scriptEngine.editorWorkingDirInfo.relativePath = TMP_TEST_WORKING_DIR;
            _scriptEngine.playerWorkingDirInfo.relativePath = TMP_TEST_WORKING_DIR;

            var samGO = new GameObject("Sample Character");
            _sampleCharacter = samGO.AddComponent<SampleCharacter>();
            _scriptEngine.OnPostInit += (jsEnv) => {
                _scriptEngine.AddToGlobal("sam", _sampleCharacter);
            };
        }

        [OneTimeTearDown]
        public static void OneTimeTearDown() {
            Object.DestroyImmediate(_scriptEngine.gameObject);
            Object.DestroyImmediate(_mainCamera.gameObject);
            Object.DestroyImmediate(_sampleCharacter.gameObject);

            Directory.Delete(_scriptEngine.WorkingDir, true);
        }

        [UnitySetUp]
        public IEnumerator SetUp() {
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            yield return null;
        }

        [UnityTest]
        public IEnumerator WorkflowTest() {
            LogAssert.Expect(LogType.Log, new Regex("OneJS is good to go"));
            _scriptEngine.gameObject.SetActive(true);
            _runner.enabled = false;
            yield return null;
            yield return null;

            RunCommand($"npm run setup");
            WriteContent("a55d96be65534ffa89b4819c967a16ba");
            BuildAndReload();

            yield return null;
            yield return null;

            var uiDoc = _scriptEngine.GetComponent<UIDocument>();
            var root = uiDoc.rootVisualElement;
            var allNodes = root.Query().ToList();
            Assert.AreEqual(10, allNodes.Count, "Node Count mismatch");

            Assert.AreEqual(100f, allNodes[8].resolvedStyle.width, "Width mismatch");
            Assert.AreEqual(20f, allNodes[8].resolvedStyle.borderBottomLeftRadius, "BottomLeftRadius mismatch");
            Assert.AreEqual(30f, allNodes[8].resolvedStyle.rotate.angle.value, "BottomLeftRadius mismatch");
            Assert.AreEqual(Color.red, allNodes[8].resolvedStyle.backgroundColor, "BackgroundColor mismatch");

            yield return null;
        }

#if ONEJS_LOCAL_DEV
        [UnityTest]
        public IEnumerator FortniteTest() {
            LogAssert.Expect(LogType.Log, new Regex("OneJS is good to go"));
            _scriptEngine.gameObject.SetActive(true);
            _runner.enabled = false;
            yield return null;
            yield return null;

            RunCommand($"npm run setup && npx postcss input.css -o ../Assets/tailwind.uss");
            RunCommand($"npm install fortnite-sample");
            yield return null;
            var twss = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/tailwind.uss");
            _scriptEngine.styleSheets = new[] { _scriptEngine.styleSheets[0], twss };
            yield return null;

            WriteContent("b17b25d1b82a4af2a3eb62785de08671");
            BuildAndReload();

            yield return null;
            yield return null;

            var uiDoc = _scriptEngine.GetComponent<UIDocument>();
            var root = uiDoc.rootVisualElement;
            var allNodes = root.Query().ToList();
            Assert.AreEqual(76, allNodes.Count, "Node Count mismatch");

            yield return null;
        }


        [UnityTest]
        public IEnumerator GameUIsTest() {
            _scriptEngine.gameObject.SetActive(true);
            _runner.enabled = false;
            yield return null;
            RunCommand($"npm run setup && npx postcss input.css -o ../Assets/tailwind.uss");
            RunCommand($"npx oj add all");
            yield return null;
            var twss = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/tailwind.uss");
            _scriptEngine.styleSheets = new[] { _scriptEngine.styleSheets[0], twss };
            yield return null;
            WriteContent("a260cf8a72e3468daeec5ea3ffd90842");
            BuildAndReload();
            yield return null;
            yield return null;

            var uiDoc = _scriptEngine.GetComponent<UIDocument>();
            var root = uiDoc.rootVisualElement;
            var allNodes = root.Query().ToList();
            Assert.AreEqual(36, allNodes.Count, "Node Count mismatch");

            yield return null;
        }
#endif

        #region Static Helpers
        // MARK: - Static Helpers
        public static void BuildAndReload() {
            // npx may have PATH issues on linux because each RunCommand creates a new shell
            // try to avoid "--save-dev" because certain test envs may have NODE_ENV=production set
            RunCommand(
                $"tsc && " +
                $"node esbuild.mjs --once && " +
                $"npx postcss input.css -o ../Assets/tailwind.uss");

            _runner.Reload();
        }

        public static void WriteContent(string guid, string path = "index.tsx") {
            var indexContent = LoadFromGUID<TextAsset>(guid).text;
            var indexPath = Path.Combine(_scriptEngine.WorkingDir, path);
            File.WriteAllText(indexPath, indexContent);
        }

        public static T LoadFromGUID<T>(string guid) where T : Object {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) {
                Debug.LogError($"Invalid GUID: {guid}");
                return null;
            }

            T prefab = AssetDatabase.LoadAssetAtPath<T>(path);
            return prefab;
        }

        private static void RunCommand(string command) {
            bool isWin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            var shell = Environment.GetEnvironmentVariable("SHELL");
            if (string.IsNullOrEmpty(shell)) shell = isMac ? "/bin/zsh" : "/bin/bash";

            var process = new System.Diagnostics.Process {
                StartInfo = new System.Diagnostics.ProcessStartInfo {
                    FileName = isWin ? "cmd.exe" : shell,
                    Arguments = isWin ? $"/c {command}" : $"-l -i -c \"{command}\"",
                    WorkingDirectory = _scriptEngine.WorkingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0) {
                string error = process.StandardError.ReadToEnd();
                throw new System.Exception($"Command failed with exit code {process.ExitCode}: {error}");
            }
        }
        #endregion
    }
}
#endif