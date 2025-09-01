using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;

#if UNITY_WEBGL
using UnityEditor.Callbacks;
#endif

public class PuertsWebglBuildProcessing : IPreprocessBuildWithReport, IPostprocessBuildWithReport {
    public int callbackOrder => 2;

    public void OnPreprocessBuild(BuildReport report) {
#if UNITY_WEBGL
        if (string.IsNullOrEmpty(PlayerSettings.WebGL.emscriptenArgs)) {
            PlayerSettings.WebGL.emscriptenArgs = "-s ALLOW_TABLE_GROWTH=1";
        } else if (!PlayerSettings.WebGL.emscriptenArgs.Contains("-s ALLOW_TABLE_GROWTH=1")) {
            PlayerSettings.WebGL.emscriptenArgs += " -s ALLOW_TABLE_GROWTH=1";
        }
#endif
    }

    public void OnPostprocessBuild(BuildReport report) {
#if UNITY_WEBGL
#if UNITY_2022_1_OR_NEWER
        foreach (var file in report.GetFiles())
#else
        foreach (var file in report.files)
#endif
        {
            if (file.path.EndsWith("index.html")) {
                var dir = Path.GetDirectoryName(file.path);
                PackJsResources(dir);
            }
        }
#endif
    }

#if UNITY_WEBGL
    static string _webglRoot;

    static string WebglRoot {
        get {
            if (_webglRoot != null) return _webglRoot;

            var guids = AssetDatabase.FindAssets("PuertsWebglBuildProcessing t:Script");
            if (guids == null || guids.Length == 0)
                throw new Exception("Cannot locate PuertsWebglBuildProcessing.cs");

            var scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            _webglRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(scriptPath), ".."));
            return _webglRoot;
        }
    }

    static void PackJsResources(string output) {
        Debug.Log($"[PuerTS] >>>> Pack JavaScript Resources to {output}");

        if (!Directory.Exists(output)) Directory.CreateDirectory(output);

        // copy puerts-runtime.js
        File.Copy(
            Path.Combine(WebglRoot, "Javascripts~", "PuertsDLLMock", "dist", "puerts-runtime.js"),
            Path.Combine(output, "puerts-runtime.js"),
            true
        );

        // collect *.mjs / *.cjs / *.js.txt under Resources
        var resourcesPatterns = new List<string> {
            Application.dataPath + "/**/Resources/**/*.mjs",
            Application.dataPath + "/**/Resources/**/*.cjs",
            Path.Combine(Path.GetDirectoryName(WebglRoot)!, "**/Resources/**/*.mjs")
        };

        var unittestPath = Path.GetFullPath("Packages/com.tencent.puerts.unittest/");
        if (Directory.Exists(unittestPath)) {
            resourcesPatterns.AddRange(new[] {
                unittestPath + "/**/Resources/**/*.mjs",
                unittestPath + "/**/Resources/**/*.cjs",
                unittestPath + "/**/Resources/**/*.js.txt"
            });
        }

        var command = "buildForBrowser";
        var cliEntry = Path.Combine(WebglRoot, "Cli", "Javascripts~", "index.js");
        var args = $"\"{cliEntry}\" {command} -p {string.Join(" ", resourcesPatterns.ConvertAll(p => $"\"{p.Replace("\\", "/")}\""))} -o \"{output}\"";

        var executeFileName = "node";

#if !UNITY_EDITOR_WIN
        string userHome = Environment.GetEnvironmentVariable("HOME");
        string nvmScriptPath = $"{userHome}/.nvm/nvm.sh";
        if (File.Exists(nvmScriptPath))
        {
            args = $"-c 'source \"{nvmScriptPath}\" && node {args}'";
            executeFileName = "bash";
        }
#endif

        Debug.Log($"[PuerTS] executing cmd: {executeFileName} {args}");

        var startInfo = new System.Diagnostics.ProcessStartInfo {
            FileName = executeFileName,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using (var process = System.Diagnostics.Process.Start(startInfo)) {
            process.OutputDataReceived += (sender, e) => {
                if (!string.IsNullOrEmpty(e.Data))
                    Debug.Log(e.Data);
            };
            process.ErrorDataReceived += (sender, e) => {
                if (!string.IsNullOrEmpty(e.Data))
                    Debug.LogError(e.Data);
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            if (process.ExitCode != 0) {
                Debug.LogError($"Node process exited with code: {process.ExitCode}");
            }
        }

        // inject scripts into index.html
        var indexPath = Path.Combine(output, "index.html");
        var indexContent = File.ReadAllText(indexPath);

        if (!indexContent.Contains("puerts-runtime.js") || !indexContent.Contains("puerts_browser_js_resources.js")) {
            Debug.Log($"[PuerTS] >>>> inject to {indexPath}");
            var pos = indexContent.IndexOf("</head>", StringComparison.Ordinal);
            if (pos >= 0) {
                indexContent = indexContent.Substring(0, pos) +
                               "  <script src=\"./puerts-runtime.js\"></script>\n    <script src=\"./puerts_browser_js_resources.js\"></script>" +
                               indexContent.Substring(pos);
                File.WriteAllText(indexPath, indexContent);
            } else {
                Debug.LogError("[PuerTS] Could not inject script tags – </head> not found.");
            }
        }
    }
#endif
}
