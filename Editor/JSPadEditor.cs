using System;
using System.Diagnostics;
using System.IO;
using OneJS;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

[CustomEditor(typeof(JSPad))]
public class JSPadEditor : Editor {
    JSPad _target;
    Process _currentProcess;
    bool _isProcessing;
    string _statusMessage;

    SerializedProperty _sourceCode;

    // UI Toolkit elements
    VisualElement _root;
    CodeField _codeField;
    Label _statusLabel;
    Button _buildRunButton;
    Button _buildOnlyButton;
    Button _runButton;

    void OnEnable() {
        _target = (JSPad)target;
        _sourceCode = serializedObject.FindProperty("_sourceCode");
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    void OnDisable() {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        KillCurrentProcess();
    }

    // Use GlobalObjectId for a stable key that persists across Play Mode
    string EditorPrefsKey {
        get {
            var globalId = GlobalObjectId.GetGlobalObjectIdSlow(_target);
            return $"JSPad_SourceCode_{globalId}";
        }
    }

    void OnPlayModeStateChanged(PlayModeStateChange state) {
        if (state == PlayModeStateChange.EnteredEditMode) {
            // Delay restoration to ensure editor is fully initialized
            EditorApplication.delayCall += RestoreSourceCodeFromPrefs;
        }
    }

    void RestoreSourceCodeFromPrefs() {
        if (_target == null) return;

        var savedCode = EditorPrefs.GetString(EditorPrefsKey, null);
        if (!string.IsNullOrEmpty(savedCode)) {
            Undo.RecordObject(_target, "Restore JSPad Source Code");
            _target.SourceCode = savedCode;
            EditorUtility.SetDirty(_target);
            EditorPrefs.DeleteKey(EditorPrefsKey);
        }
    }

    void SaveSourceCodeToPrefs() {
        if (Application.isPlaying && _target != null) {
            EditorPrefs.SetString(EditorPrefsKey, _target.SourceCode);
        }
    }

    public override VisualElement CreateInspectorGUI() {
        _root = new VisualElement();

        // Status section
        var statusBox = new VisualElement();
        statusBox.style.backgroundColor = new Color(0.24f, 0.24f, 0.24f);
        statusBox.style.borderTopWidth = statusBox.style.borderBottomWidth =
            statusBox.style.borderLeftWidth = statusBox.style.borderRightWidth = 1;
        statusBox.style.borderTopColor = statusBox.style.borderBottomColor =
            statusBox.style.borderLeftColor = statusBox.style.borderRightColor = new Color(0.14f, 0.14f, 0.14f);
        statusBox.style.borderTopLeftRadius = statusBox.style.borderTopRightRadius =
            statusBox.style.borderBottomLeftRadius = statusBox.style.borderBottomRightRadius = 3;
        statusBox.style.paddingTop = statusBox.style.paddingBottom = 8;
        statusBox.style.paddingLeft = statusBox.style.paddingRight = 10;
        statusBox.style.marginTop = 2;
        statusBox.style.marginBottom = 10;

        var statusRow = new VisualElement();
        statusRow.style.flexDirection = FlexDirection.Row;
        var statusTitle = new Label("Status");
        statusTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        statusTitle.style.width = 50;
        _statusLabel = new Label("Not initialized");
        statusRow.Add(statusTitle);
        statusRow.Add(_statusLabel);
        statusBox.Add(statusRow);

        var tempRow = new VisualElement();
        tempRow.style.flexDirection = FlexDirection.Row;
        tempRow.style.marginTop = 4;
        var tempTitle = new Label("Temp:");
        tempTitle.style.width = 50;
        tempTitle.style.color = new Color(0.6f, 0.6f, 0.6f);
        var relativeTempPath = Path.Combine("Temp", "OneJSPad", Path.GetFileName(_target.TempDir));
        var tempPath = new Label(relativeTempPath);
        tempPath.style.color = new Color(0.5f, 0.5f, 0.5f);
        tempPath.style.fontSize = 10;
        tempPath.style.overflow = Overflow.Hidden;
        tempPath.style.textOverflow = TextOverflow.Ellipsis;
        tempRow.Add(tempTitle);
        tempRow.Add(tempPath);
        statusBox.Add(tempRow);

        _root.Add(statusBox);

        // Code editor (with syntax highlighting, monospace font, and proper indentation)
        _codeField = new CodeField();
        _codeField.bindingPath = "_sourceCode";
        _codeField.style.minHeight = 300;
        _codeField.style.whiteSpace = WhiteSpace.NoWrap;
        _codeField.style.overflow = Overflow.Hidden;

        // Style the text input
        var textInput = _codeField.Q<TextElement>();
        if (textInput != null) {
            textInput.style.fontSize = 12;
            textInput.style.whiteSpace = WhiteSpace.NoWrap;
            textInput.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
            textInput.style.paddingTop = textInput.style.paddingBottom = 8;
            textInput.style.paddingLeft = textInput.style.paddingRight = 8;
        }

        // Style the scroll view inside CodeField
        var scrollView = _codeField.Q<ScrollView>();
        if (scrollView != null) {
            scrollView.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scrollView.horizontalScrollerVisibility = ScrollerVisibility.Auto;
        }

        _codeField.BindProperty(_sourceCode);

        // Auto-resize height based on content, and save to EditorPrefs during Play Mode
        _codeField.RegisterValueChangedCallback(evt => {
            UpdateCodeFieldHeight();
            SaveSourceCodeToPrefs();
        });
        _root.schedule.Execute(UpdateCodeFieldHeight).ExecuteLater(100);

        _root.Add(_codeField);

        // Spacer
        var spacer = new VisualElement();
        spacer.style.height = 10;
        _root.Add(spacer);

        // Row 1: Build & Run, Run/Stop toggle
        var row1 = new VisualElement();
        row1.style.flexDirection = FlexDirection.Row;
        row1.style.marginBottom = 4;

        _buildRunButton = new Button(BuildAndRun) { text = "Build & Run" };
        _buildRunButton.style.flexGrow = 1;
        _buildRunButton.style.height = 28;
        row1.Add(_buildRunButton);

        _runButton = new Button(OnRunStopClicked) { text = "Run" };
        _runButton.style.flexGrow = 1;
        _runButton.style.height = 28;
        _runButton.style.marginLeft = 4;
        row1.Add(_runButton);

        _root.Add(row1);

        // Row 2: Build Only, Open Folder, Clean
        var row2 = new VisualElement();
        row2.style.flexDirection = FlexDirection.Row;

        _buildOnlyButton = new Button(() => Build(false)) { text = "Build Only" };
        _buildOnlyButton.style.flexGrow = 1;
        _buildOnlyButton.style.height = 24;
        row2.Add(_buildOnlyButton);

        var openFolderBtn = new Button(OpenTempFolder) { text = "Open Folder" };
        openFolderBtn.style.flexGrow = 1;
        openFolderBtn.style.height = 24;
        openFolderBtn.style.marginLeft = 4;
        row2.Add(openFolderBtn);

        var cleanBtn = new Button(Clean) { text = "Clean" };
        cleanBtn.style.flexGrow = 1;
        cleanBtn.style.height = 24;
        cleanBtn.style.marginLeft = 4;
        row2.Add(cleanBtn);

        _root.Add(row2);

        // UI Panel Settings foldout
        var panelFoldout = new Foldout { text = "UI Panel", value = false };
        panelFoldout.style.marginTop = 10;

        var panelSettingsProp = serializedObject.FindProperty("_panelSettings");
        var panelSettingsField = new PropertyField(panelSettingsProp);
        panelSettingsField.BindProperty(panelSettingsProp);
        panelFoldout.Add(panelSettingsField);

        var themeStylesheetProp = serializedObject.FindProperty("_defaultThemeStylesheet");
        var themeStylesheetField = new PropertyField(themeStylesheetProp, "Theme Stylesheet");
        themeStylesheetField.BindProperty(themeStylesheetProp);
        panelFoldout.Add(themeStylesheetField);

        var scaleModeProp = serializedObject.FindProperty("_scaleMode");
        var scaleModeField = new PropertyField(scaleModeProp, "Scale Mode");
        scaleModeField.BindProperty(scaleModeProp);
        panelFoldout.Add(scaleModeField);

        // Container for conditional fields
        var scaleWithScreenSizeFields = new VisualElement();
        var constantPixelSizeFields = new VisualElement();

        // Scale With Screen Size fields
        var referenceResolutionProp = serializedObject.FindProperty("_referenceResolution");
        var referenceResolutionField = new PropertyField(referenceResolutionProp, "Reference Resolution");
        referenceResolutionField.BindProperty(referenceResolutionProp);
        scaleWithScreenSizeFields.Add(referenceResolutionField);

        var screenMatchModeProp = serializedObject.FindProperty("_screenMatchMode");
        var screenMatchModeField = new PropertyField(screenMatchModeProp, "Screen Match Mode");
        screenMatchModeField.BindProperty(screenMatchModeProp);
        scaleWithScreenSizeFields.Add(screenMatchModeField);

        var matchProp = serializedObject.FindProperty("_match");
        var matchField = new PropertyField(matchProp, "Match");
        matchField.BindProperty(matchProp);
        scaleWithScreenSizeFields.Add(matchField);

        panelFoldout.Add(scaleWithScreenSizeFields);

        // Constant Pixel Size fields
        var scaleProp = serializedObject.FindProperty("_scale");
        var scaleField = new PropertyField(scaleProp, "Scale");
        scaleField.BindProperty(scaleProp);
        constantPixelSizeFields.Add(scaleField);

        panelFoldout.Add(constantPixelSizeFields);

        var sortOrderProp = serializedObject.FindProperty("_sortOrder");
        var sortOrderField = new PropertyField(sortOrderProp, "Sort Order");
        sortOrderField.BindProperty(sortOrderProp);
        panelFoldout.Add(sortOrderField);

        // Update visibility based on scale mode
        void UpdateScaleModeVisibility() {
            var mode = (PanelScaleMode)scaleModeProp.enumValueIndex;
            scaleWithScreenSizeFields.style.display = mode == PanelScaleMode.ScaleWithScreenSize
                ? DisplayStyle.Flex : DisplayStyle.None;
            constantPixelSizeFields.style.display = mode == PanelScaleMode.ConstantPixelSize
                ? DisplayStyle.Flex : DisplayStyle.None;
        }

        UpdateScaleModeVisibility();
        scaleModeField.RegisterValueChangeCallback(_ => {
            serializedObject.Update();
            UpdateScaleModeVisibility();
        });

        _root.Add(panelFoldout);

        // Schedule status updates
        _root.schedule.Execute(UpdateUI).Every(100);

        return _root;
    }

    void UpdateCodeFieldHeight() {
        if (_codeField == null) return;

        var lines = _codeField.value.Split('\n');
        var lineHeight = 15f;
        var minLines = 10;
        var numLines = Mathf.Max(lines.Length, minLines);
        var height = numLines * lineHeight;

        _codeField.style.height = height;
    }

    void UpdateUI() {
        if (_statusLabel == null) return;

        // Update status
        if (_isProcessing) {
            _statusLabel.text = _statusMessage ?? "Processing...";
            _statusLabel.style.color = new Color(0.9f, 0.7f, 0.2f);
        } else if (Application.isPlaying && _target.IsRunning) {
            _statusLabel.text = "Running";
            _statusLabel.style.color = new Color(0.2f, 0.8f, 0.2f);
        } else if (_target.HasBuiltOutput) {
            _statusLabel.text = "Ready (built)";
            _statusLabel.style.color = new Color(0.5f, 0.8f, 0.5f);
        } else if (_target.HasNodeModules()) {
            _statusLabel.text = "Dependencies installed";
            _statusLabel.style.color = Color.white;
        } else {
            _statusLabel.text = "Not initialized";
            _statusLabel.style.color = Color.white;
        }

        // Update button states
        _buildRunButton.SetEnabled(!_isProcessing);
        _buildRunButton.text = _isProcessing ? (_statusMessage ?? "Processing...") : "Build & Run";
        _buildOnlyButton.SetEnabled(!_isProcessing);

        // Run/Stop toggle button
        var isRunning = Application.isPlaying && _target.IsRunning;
        _runButton.text = isRunning ? "Stop" : "Run";
        _runButton.SetEnabled(Application.isPlaying && (_target.HasBuiltOutput || isRunning) && !_isProcessing);
    }

    void OnRunStopClicked() {
        if (_target.IsRunning) {
            _target.Stop();
        } else {
            _target.RunBuiltScript();
        }
    }

    void OpenTempFolder() {
        _target.EnsureTempDirectory();
        var path = _target.TempDir;

#if UNITY_EDITOR_OSX
        System.Diagnostics.Process.Start("open", $"\"{path}\"");
#elif UNITY_EDITOR_WIN
        System.Diagnostics.Process.Start("explorer.exe", path.Replace("/", "\\"));
#else
        System.Diagnostics.Process.Start("xdg-open", path);
#endif
    }

    void BuildAndRun() {
        Build(runAfter: true);
    }

    void Build(bool runAfter) {
        if (_isProcessing) return;

        // Ensure we're in play mode for running
        if (runAfter && !Application.isPlaying) {
            EditorUtility.DisplayDialog("JSPad", "Enter Play Mode first to run the script.", "OK");
            return;
        }

        _target.EnsureTempDirectory();
        _target.WriteSourceFile();

        // Check if npm install is needed
        if (!_target.HasNodeModules()) {
            RunNpmInstall(() => {
                RunBuild(runAfter);
            });
        } else {
            RunBuild(runAfter);
        }
    }

    void RunNpmInstall(Action onComplete) {
        _isProcessing = true;
        _statusMessage = "Installing dependencies...";
        _target.SetBuildState(JSPad.BuildState.InstallingDeps);
        Repaint();

        try {
            var npmPath = GetNpmCommand();
            var nodeBinDir = Path.GetDirectoryName(npmPath);

            var startInfo = new ProcessStartInfo {
                FileName = npmPath,
                Arguments = "install",
                WorkingDirectory = _target.TempDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var existingPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            startInfo.EnvironmentVariables["PATH"] = nodeBinDir + Path.PathSeparator + existingPath;

            _currentProcess = new Process { StartInfo = startInfo };

            _currentProcess.OutputDataReceived += (s, e) => {
                if (!string.IsNullOrEmpty(e.Data)) Debug.Log($"[JSPad npm] {e.Data}");
            };

            _currentProcess.ErrorDataReceived += (s, e) => {
                if (!string.IsNullOrEmpty(e.Data)) Debug.LogWarning($"[JSPad npm] {e.Data}");
            };

            _currentProcess.EnableRaisingEvents = true;
            _currentProcess.Exited += (s, e) => {
                var exitCode = _currentProcess.ExitCode;
                _currentProcess = null;

                EditorApplication.delayCall += () => {
                    if (exitCode == 0) {
                        Debug.Log("[JSPad] Dependencies installed");
                        onComplete?.Invoke();
                    } else {
                        _isProcessing = false;
                        _statusMessage = null;
                        _target.SetBuildState(JSPad.BuildState.Error, error: $"npm install failed with exit code {exitCode}");
                        Debug.LogError($"[JSPad] npm install failed with exit code {exitCode}");
                        Repaint();
                    }
                };
            };

            _currentProcess.Start();
            _currentProcess.BeginOutputReadLine();
            _currentProcess.BeginErrorReadLine();

        } catch (Exception ex) {
            _isProcessing = false;
            _statusMessage = null;
            _target.SetBuildState(JSPad.BuildState.Error, error: ex.Message);
            Debug.LogError($"[JSPad] npm install error: {ex.Message}");
        }
    }

    void RunBuild(bool runAfter) {
        _isProcessing = true;
        _statusMessage = "Building...";
        _target.SetBuildState(JSPad.BuildState.Building);
        Repaint();

        try {
            var npmPath = GetNpmCommand();
            var nodeBinDir = Path.GetDirectoryName(npmPath);

            var startInfo = new ProcessStartInfo {
                FileName = npmPath,
                Arguments = "run build",
                WorkingDirectory = _target.TempDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var existingPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            startInfo.EnvironmentVariables["PATH"] = nodeBinDir + Path.PathSeparator + existingPath;

            _currentProcess = new Process { StartInfo = startInfo };

            string errorOutput = "";

            _currentProcess.OutputDataReceived += (s, e) => {
                if (!string.IsNullOrEmpty(e.Data)) Debug.Log($"[JSPad build] {e.Data}");
            };

            _currentProcess.ErrorDataReceived += (s, e) => {
                if (!string.IsNullOrEmpty(e.Data)) {
                    errorOutput += e.Data + "\n";
                    Debug.LogError($"[JSPad build] {e.Data}");
                }
            };

            _currentProcess.EnableRaisingEvents = true;
            _currentProcess.Exited += (s, e) => {
                var exitCode = _currentProcess.ExitCode;
                _currentProcess = null;

                EditorApplication.delayCall += () => {
                    _isProcessing = false;
                    _statusMessage = null;

                    if (exitCode == 0) {
                        _target.SetBuildState(JSPad.BuildState.Ready, output: "Build successful");
                        Debug.Log("[JSPad] Build complete");

                        if (runAfter && Application.isPlaying) {
                            _target.RunBuiltScript();
                        }
                    } else {
                        _target.SetBuildState(JSPad.BuildState.Error, error: errorOutput.Trim());
                        Debug.LogError($"[JSPad] Build failed with exit code {exitCode}");
                    }
                    Repaint();
                };
            };

            _currentProcess.Start();
            _currentProcess.BeginOutputReadLine();
            _currentProcess.BeginErrorReadLine();

        } catch (Exception ex) {
            _isProcessing = false;
            _statusMessage = null;
            _target.SetBuildState(JSPad.BuildState.Error, error: ex.Message);
            Debug.LogError($"[JSPad] Build error: {ex.Message}");
        }
    }

    void Clean() {
        KillCurrentProcess();

        if (_target.IsRunning) {
            _target.Stop();
        }

        var tempDir = _target.TempDir;
        if (Directory.Exists(tempDir)) {
            try {
                Directory.Delete(tempDir, recursive: true);
                Debug.Log($"[JSPad] Cleaned temp directory: {tempDir}");
            } catch (Exception ex) {
                Debug.LogError($"[JSPad] Failed to clean: {ex.Message}");
            }
        } else {
            Debug.Log("[JSPad] Nothing to clean");
        }

        _target.SetBuildState(JSPad.BuildState.Idle);
        Repaint();
    }

    void KillCurrentProcess() {
        if (_currentProcess != null && !_currentProcess.HasExited) {
            try {
                _currentProcess.Kill();
            } catch { }
            _currentProcess = null;
        }
        _isProcessing = false;
        _statusMessage = null;
    }

    string _cachedNpmPath;

    string GetNpmCommand() {
        if (!string.IsNullOrEmpty(_cachedNpmPath)) return _cachedNpmPath;

#if UNITY_EDITOR_WIN
        _cachedNpmPath = "npm.cmd";
        return _cachedNpmPath;
#else
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string[] fixedPaths = {
            "/usr/local/bin/npm",
            "/opt/homebrew/bin/npm",
            "/usr/bin/npm",
        };

        foreach (var path in fixedPaths) {
            if (File.Exists(path)) {
                _cachedNpmPath = path;
                return _cachedNpmPath;
            }
        }

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

        var nDir = Path.Combine(home, "n/bin/npm");
        if (File.Exists(nDir)) {
            _cachedNpmPath = nDir;
            return _cachedNpmPath;
        }

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

        return "npm";
#endif
    }
}
