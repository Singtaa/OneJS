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
using Object = UnityEngine.Object;

namespace OneJS.CI {
    public class WorkflowTests {
        static readonly string TMP_TEST_WORKING_DIR = "ONEJS_TMP_WORKDIR_DELETE_ME";
        static Camera _mainCamera;
        static ScriptEngine _scriptEngine;
        static Bundler _bundler;
        static Runner _runner;

        // May consider using `Application.logMessageReceived += Catch` for log catching

        [OneTimeSetUp]
        public static void OneTimeSetUp() {
            if (File.Exists("/home/libpuerts.so")) {
                Debug.Log("zzzzzzzzzzzz");
            }

            var tmpWorkDirPath = Path.Combine(Path.GetDirectoryName(Application.dataPath)!,
                TMP_TEST_WORKING_DIR);
            if (Directory.Exists(tmpWorkDirPath)) {
                Directory.Delete(tmpWorkDirPath, true);
            }

            var cameraGO = new GameObject("Main Camera");
            _mainCamera = cameraGO.AddComponent<Camera>();
            var prefab = LoadFromGUID<GameObject>("f99b6aec6fc021f4c9572906776c6555");
            prefab.SetActive(false);
            var go = Object.Instantiate(prefab);
            prefab.SetActive(true);

            _scriptEngine = go.GetComponent<ScriptEngine>();
            _bundler = go.GetComponent<Bundler>();
            _runner = go.GetComponent<Runner>();

            _scriptEngine.editorWorkingDirInfo.relativePath = TMP_TEST_WORKING_DIR;
            _scriptEngine.playerWorkingDirInfo.relativePath = TMP_TEST_WORKING_DIR;
        }

        [OneTimeTearDown]
        public static void OneTimeTearDown() {
            Object.DestroyImmediate(_scriptEngine.gameObject);
            Object.DestroyImmediate(_mainCamera.gameObject);

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
            var indexContent = LoadFromGUID<TextAsset>("a55d96be65534ffa89b4819c967a16ba").text;
            var indexPath = Path.Combine(_scriptEngine.WorkingDir, "index.tsx");
            File.WriteAllText(indexPath, indexContent);

            RunCommand(
                "npm run setup && " +
                "npm install --no-audit --no-fund --save-dev typescript && " +
                "npm exec --yes -- tsc && " +
                "node esbuild.mjs --once");

            // RunCommand("npm run setup");
            // RunCommand("npm install typescript --save-dev");
            // RunCommand("npm exec --yes -- tsc"); // npx will have PATH issues on linux because each RunCommand creates a new shell
            // RunCommand("node esbuild.mjs --once");

            // yield return new WaitForSeconds(3); // Wait for runner to pick up the change

            _runner.Reload();
            yield return null;

            var uiDoc = _scriptEngine.GetComponent<UIDocument>();
            var root = uiDoc.rootVisualElement;
            var allNodes = root.Query().ToList();
            Assert.AreEqual(10, allNodes.Count, "Node Count mismatch");

            Assert.AreEqual(100f, allNodes[8].resolvedStyle.width, "Width mismatch");
            Assert.AreEqual(20f, allNodes[8].resolvedStyle.borderBottomLeftRadius, "BottomLeftRadius mismatch");
            Assert.AreEqual(30f, allNodes[8].resolvedStyle.rotate.angle.value, "BottomLeftRadius mismatch");

            yield return null;
        }

        #region Static Helpers
        // MARK: - Static Helpers
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

            var process = new System.Diagnostics.Process {
                StartInfo = new System.Diagnostics.ProcessStartInfo {
                    FileName = isWin ? "cmd.exe" : "/bin/bash",
                    Arguments = isWin ? $"/c {command}" : $"-lc \"{command}\"",
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