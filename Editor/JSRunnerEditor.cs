using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

[CustomEditor(typeof(JSRunner))]
public class JSRunnerEditor : Editor {
    JSRunner _target;
    bool _buildInProgress;
    string _buildOutput;

    SerializedProperty _workingDir;
    SerializedProperty _entryFile;
    SerializedProperty _liveReload;
    SerializedProperty _pollInterval;
    SerializedProperty _embeddedScript;
    SerializedProperty _streamingAssetsPath;

    void OnEnable() {
        _target = (JSRunner)target;

        _workingDir = serializedObject.FindProperty("_workingDir");
        _entryFile = serializedObject.FindProperty("_entryFile");
        _liveReload = serializedObject.FindProperty("_liveReload");
        _pollInterval = serializedObject.FindProperty("_pollInterval");
        _embeddedScript = serializedObject.FindProperty("_embeddedScript");
        _streamingAssetsPath = serializedObject.FindProperty("_streamingAssetsPath");
    }

    public override void OnInspectorGUI() {
        serializedObject.Update();

        // Status Section
        DrawStatusSection();

        EditorGUILayout.Space(10);

        // Main Settings
        EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_workingDir);
        EditorGUILayout.PropertyField(_entryFile);

        EditorGUILayout.Space(5);

        // Live Reload Section
        EditorGUILayout.PropertyField(_liveReload);
        if (_liveReload.boolValue) {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_pollInterval);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(10);

        // WebGL Section (Header comes from [Header] attribute)
        EditorGUILayout.PropertyField(_embeddedScript);
        EditorGUILayout.PropertyField(_streamingAssetsPath);

        EditorGUILayout.Space(10);

        // Actions Section
        DrawActionsSection();

        serializedObject.ApplyModifiedProperties();
    }

    void DrawStatusSection() {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // Status indicator
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Status", EditorStyles.boldLabel, GUILayout.Width(50));

        if (Application.isPlaying) {
            if (_target.IsRunning) {
                var style = new GUIStyle(EditorStyles.label);
                style.normal.textColor = new Color(0.2f, 0.8f, 0.2f);
                EditorGUILayout.LabelField("Running", style);
            } else {
                var style = new GUIStyle(EditorStyles.label);
                style.normal.textColor = new Color(0.8f, 0.6f, 0.2f);
                EditorGUILayout.LabelField("Loading...", style);
            }
        } else {
            EditorGUILayout.LabelField("Stopped (Enter Play Mode to run)");
        }
        EditorGUILayout.EndHorizontal();

        // Additional info in Play mode
        if (Application.isPlaying && _target.IsRunning) {
            if (_target.IsLiveReloadEnabled) {
                EditorGUILayout.LabelField($"Live Reload: Enabled (polling every {_pollInterval.floatValue}s)");
            } else {
                EditorGUILayout.LabelField("Live Reload: Disabled");
            }

            if (_target.ReloadCount > 0) {
                EditorGUILayout.LabelField($"Reloads: {_target.ReloadCount}");
            }
        }

        // Entry file path
        var entryPath = _target.EntryFileFullPath;
        var fileExists = File.Exists(entryPath);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Entry File:", GUILayout.Width(70));
        var pathStyle = new GUIStyle(EditorStyles.label);
        pathStyle.normal.textColor = fileExists ? Color.white : new Color(0.8f, 0.4f, 0.4f);
        EditorGUILayout.LabelField(fileExists ? "Found" : "Not Found", pathStyle, GUILayout.Width(80));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    void DrawActionsSection() {
        EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();

        // Reload button (only in Play mode)
        EditorGUI.BeginDisabledGroup(!Application.isPlaying || !_target.IsRunning);
        if (GUILayout.Button("Reload Now", GUILayout.Height(30))) {
            _target.ForceReload();
        }
        EditorGUI.EndDisabledGroup();

        // Build button
        EditorGUI.BeginDisabledGroup(_buildInProgress);
        if (GUILayout.Button(_buildInProgress ? "Building..." : "Build", GUILayout.Height(30))) {
            RunBuild();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();

        // Open folder button
        if (GUILayout.Button("Open Folder", GUILayout.Height(24))) {
            OpenWorkingDirectory();
        }

        // Open terminal button
        if (GUILayout.Button("Open Terminal", GUILayout.Height(24))) {
            OpenTerminal();
        }

        EditorGUILayout.EndHorizontal();

        // Show build output if available
        if (!string.IsNullOrEmpty(_buildOutput)) {
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(_buildOutput, MessageType.Info);
        }
    }

    void RunBuild() {
        var workingDir = _target.WorkingDirFullPath;

        if (!Directory.Exists(workingDir)) {
            Debug.LogError($"[JSRunner] Working directory not found: {workingDir}");
            return;
        }

        // Check if package.json exists
        var packageJsonPath = Path.Combine(workingDir, "package.json");
        if (!File.Exists(packageJsonPath)) {
            Debug.LogError($"[JSRunner] package.json not found in {workingDir}. Run 'npm init' first.");
            return;
        }

        _buildInProgress = true;
        _buildOutput = "Building...";

        try {
            var npmPath = GetNpmCommand();
            var nodeBinDir = Path.GetDirectoryName(npmPath);

            var startInfo = new ProcessStartInfo {
                FileName = npmPath,
                Arguments = "run build",
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Add node's bin directory to PATH so npm can find node
            var existingPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            startInfo.EnvironmentVariables["PATH"] = nodeBinDir + Path.PathSeparator + existingPath;

            var process = new Process { StartInfo = startInfo };

            process.OutputDataReceived += (sender, args) => {
                if (!string.IsNullOrEmpty(args.Data)) {
                    Debug.Log($"[Build] {args.Data}");
                }
            };

            process.ErrorDataReceived += (sender, args) => {
                if (!string.IsNullOrEmpty(args.Data)) {
                    Debug.LogError($"[Build] {args.Data}");
                }
            };

            process.EnableRaisingEvents = true;
            process.Exited += (sender, args) => {
                EditorApplication.delayCall += () => {
                    _buildInProgress = false;
                    if (process.ExitCode == 0) {
                        _buildOutput = "Build completed successfully!";
                        Debug.Log("[JSRunner] Build completed successfully!");
                    } else {
                        _buildOutput = $"Build failed with exit code {process.ExitCode}";
                        Debug.LogError($"[JSRunner] Build failed with exit code {process.ExitCode}");
                    }
                    Repaint();
                };
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

        } catch (Exception ex) {
            _buildInProgress = false;
            _buildOutput = $"Build error: {ex.Message}";
            Debug.LogError($"[JSRunner] Build error: {ex.Message}");

            if (ex.Message.Contains("npm") || ex.Message.Contains("not found")) {
                Debug.LogError("[JSRunner] npm not found. Make sure Node.js is installed and in your PATH.");
            }
        }
    }

    string _cachedNpmPath;

    string GetNpmCommand() {
        if (!string.IsNullOrEmpty(_cachedNpmPath)) return _cachedNpmPath;

#if UNITY_EDITOR_WIN
        _cachedNpmPath = "npm.cmd";
        return _cachedNpmPath;
#else
        // On macOS/Linux, Unity doesn't inherit terminal PATH
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Check common fixed paths first
        string[] fixedPaths = {
            "/usr/local/bin/npm",           // Homebrew (Intel Mac) / standard
            "/opt/homebrew/bin/npm",        // Homebrew (Apple Silicon)
            "/usr/bin/npm",                 // System install
        };

        foreach (var path in fixedPaths) {
            if (File.Exists(path)) {
                _cachedNpmPath = path;
                return _cachedNpmPath;
            }
        }

        // Check nvm directory (has version subdirectories)
        var nvmDir = Path.Combine(home, ".nvm/versions/node");
        if (Directory.Exists(nvmDir)) {
            try {
                foreach (var nodeVersionDir in Directory.GetDirectories(nvmDir)) {
                    var npmPath = Path.Combine(nodeVersionDir, "bin", "npm");
                    if (File.Exists(npmPath)) {
                        _cachedNpmPath = npmPath;
                        return _cachedNpmPath;
                    }
                }
            } catch { }
        }

        // Check n version manager
        var nDir = Path.Combine(home, "n/bin/npm");
        if (File.Exists(nDir)) {
            _cachedNpmPath = nDir;
            return _cachedNpmPath;
        }

        // Fallback: try to get path from login shell
        try {
            var process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = "/bin/bash",
                    Arguments = "-l -c \"which npm\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var result = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            if (!string.IsNullOrEmpty(result) && File.Exists(result)) {
                _cachedNpmPath = result;
                return _cachedNpmPath;
            }
        } catch { }

        // Last resort
        return "npm";
#endif
    }

    void OpenWorkingDirectory() {
        var path = _target.WorkingDirFullPath;
        if (Directory.Exists(path)) {
            EditorUtility.RevealInFinder(path);
        } else {
            Debug.LogWarning($"[JSRunner] Directory not found: {path}");
        }
    }

    void OpenTerminal() {
        var path = _target.WorkingDirFullPath;
        if (!Directory.Exists(path)) {
            Debug.LogWarning($"[JSRunner] Directory not found: {path}");
            return;
        }

#if UNITY_EDITOR_OSX
        Process.Start("open", $"-a Terminal \"{path}\"");
#elif UNITY_EDITOR_WIN
        Process.Start(new ProcessStartInfo {
            FileName = "cmd.exe",
            Arguments = $"/K cd /d \"{path}\"",
            UseShellExecute = true
        });
#elif UNITY_EDITOR_LINUX
        // Try common terminal emulators
        try {
            Process.Start("gnome-terminal", $"--working-directory=\"{path}\"");
        } catch {
            try {
                Process.Start("konsole", $"--workdir \"{path}\"");
            } catch {
                Process.Start("xterm", $"-e 'cd \"{path}\" && bash'");
            }
        }
#endif
    }
}
