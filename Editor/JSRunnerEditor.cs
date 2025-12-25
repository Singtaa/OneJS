using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using PanelScaleMode = UnityEngine.UIElements.PanelScaleMode;
using PanelScreenMatchMode = UnityEngine.UIElements.PanelScreenMatchMode;
using Debug = UnityEngine.Debug;

[CustomEditor(typeof(JSRunner))]
public class JSRunnerEditor : Editor {
    JSRunner _target;
    bool _buildInProgress;
    string _buildOutput;

    // License activation
    string _licenseKey = "";
    string _licenseStatus = "";
    bool _activationInProgress;

    // UI Elements that need updating
    Label _statusLabel;
    Label _liveReloadStatusLabel;
    Label _reloadCountLabel;
    Label _entryFileStatusLabel;
    Button _reloadButton;
    Button _buildButton;
    HelpBox _buildOutputBox;
    VisualElement _liveReloadInfo;
    TextField _licenseKeyField;
    Button _activateButton;
    HelpBox _licenseStatusBox;

    void OnEnable() {
        _target = (JSRunner)target;
        EditorApplication.update += UpdateDynamicUI;
    }

    void OnDisable() {
        EditorApplication.update -= UpdateDynamicUI;
    }

    public override VisualElement CreateInspectorGUI() {
        var root = new VisualElement();
        root.styleSheets.Add(CreateStyleSheet());

        // Status Section
        root.Add(CreateStatusSection());

        // Settings Section
        root.Add(CreateSettingsSection());

        // UI Panel Section
        root.Add(CreateUIPanelSection());

        // Live Reload Section
        root.Add(CreateLiveReloadSection());

        // Build Settings Section
        root.Add(CreateBuildSettingsSection());

        // Scaffolding Section
        root.Add(CreateScaffoldingSection());

        // Advanced Section
        root.Add(CreateAdvancedSection());

        // Actions Section
        root.Add(CreateActionsSection());

        // Premium Components Section
        root.Add(CreatePremiumSection());

        return root;
    }

    StyleSheet CreateStyleSheet() {
        var styleSheet = ScriptableObject.CreateInstance<StyleSheet>();
        return styleSheet;
    }

    VisualElement CreateStatusSection() {
        var container = new VisualElement();
        container.style.backgroundColor = new Color(0.24f, 0.24f, 0.24f);
        container.style.borderTopWidth = container.style.borderBottomWidth =
            container.style.borderLeftWidth = container.style.borderRightWidth = 1;
        container.style.borderTopColor = container.style.borderBottomColor =
            container.style.borderLeftColor = container.style.borderRightColor = new Color(0.14f, 0.14f, 0.14f);
        container.style.borderTopLeftRadius = container.style.borderTopRightRadius =
            container.style.borderBottomLeftRadius = container.style.borderBottomRightRadius = 3;
        container.style.paddingTop = container.style.paddingBottom = 8;
        container.style.paddingLeft = container.style.paddingRight = 10;
        container.style.marginTop = 2;
        container.style.marginBottom = 10;

        // Status row
        var statusRow = new VisualElement();
        statusRow.style.flexDirection = FlexDirection.Row;
        statusRow.style.alignItems = Align.Center;

        var statusTitle = new Label("Status");
        statusTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        statusTitle.style.width = 50;
        statusRow.Add(statusTitle);

        _statusLabel = new Label("Stopped (Enter Play Mode to run)");
        statusRow.Add(_statusLabel);
        container.Add(statusRow);

        // Live reload info (shown in play mode)
        _liveReloadInfo = new VisualElement();
        _liveReloadInfo.style.display = DisplayStyle.None;
        _liveReloadInfo.style.marginTop = 4;

        _liveReloadStatusLabel = new Label();
        _liveReloadInfo.Add(_liveReloadStatusLabel);

        _reloadCountLabel = new Label();
        _reloadCountLabel.style.display = DisplayStyle.None;
        _liveReloadInfo.Add(_reloadCountLabel);

        container.Add(_liveReloadInfo);

        // Entry file status
        var entryRow = new VisualElement();
        entryRow.style.flexDirection = FlexDirection.Row;
        entryRow.style.alignItems = Align.Center;
        entryRow.style.marginTop = 4;

        var entryTitle = new Label("Entry File:");
        entryTitle.style.width = 70;
        entryRow.Add(entryTitle);

        _entryFileStatusLabel = new Label("Checking...");
        entryRow.Add(_entryFileStatusLabel);
        container.Add(entryRow);

        return container;
    }

    VisualElement CreateSettingsSection() {
        var container = new VisualElement();
        container.style.marginBottom = 10;

        var header = new Label("Settings");
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.marginBottom = 4;
        container.Add(header);

        var workingDirField = new PropertyField(serializedObject.FindProperty("_workingDir"));
        container.Add(workingDirField);

        var entryFileField = new PropertyField(serializedObject.FindProperty("_entryFile"));
        container.Add(entryFileField);

        return container;
    }

    VisualElement CreateUIPanelSection() {
        var container = new VisualElement();
        container.style.marginBottom = 10;

        var header = new Label("UI Panel");
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.marginBottom = 4;
        header.style.marginTop = 6;
        container.Add(header);

        // Panel Settings asset field
        var panelSettingsProp = serializedObject.FindProperty("_panelSettings");
        var panelSettingsField = new PropertyField(panelSettingsProp, "Panel Settings");
        container.Add(panelSettingsField);

        // Inline settings container (shown when _panelSettings is null)
        var inlineSettings = new VisualElement();
        inlineSettings.style.marginLeft = 15;
        inlineSettings.style.marginTop = 4;
        inlineSettings.style.paddingLeft = 10;
        inlineSettings.style.borderLeftWidth = 2;
        inlineSettings.style.borderLeftColor = new Color(0.3f, 0.3f, 0.3f);

        // Theme Stylesheet
        var themeStylesheetField = new PropertyField(serializedObject.FindProperty("_defaultThemeStylesheet"), "Theme Stylesheet");
        inlineSettings.Add(themeStylesheetField);

        // Scale Mode
        var scaleModeProp = serializedObject.FindProperty("_scaleMode");
        var scaleModeField = new PropertyField(scaleModeProp, "Scale Mode");
        inlineSettings.Add(scaleModeField);

        // ConstantPixelSize settings
        var constantPixelContainer = new VisualElement();
        var scaleField = new PropertyField(serializedObject.FindProperty("_scale"), "Scale");
        constantPixelContainer.Add(scaleField);
        inlineSettings.Add(constantPixelContainer);

        // ConstantPhysicalSize settings
        var constantPhysicalContainer = new VisualElement();
        var referenceDpiField = new PropertyField(serializedObject.FindProperty("_referenceDpi"), "Reference DPI");
        var fallbackDpiField = new PropertyField(serializedObject.FindProperty("_fallbackDpi"), "Fallback DPI");
        constantPhysicalContainer.Add(referenceDpiField);
        constantPhysicalContainer.Add(fallbackDpiField);
        inlineSettings.Add(constantPhysicalContainer);

        // ScaleWithScreenSize settings
        var scaleWithScreenContainer = new VisualElement();
        var referenceResolutionField = new PropertyField(serializedObject.FindProperty("_referenceResolution"), "Reference Resolution");
        var screenMatchModeField = new PropertyField(serializedObject.FindProperty("_screenMatchMode"), "Screen Match Mode");
        var matchField = new PropertyField(serializedObject.FindProperty("_match"), "Match");
        scaleWithScreenContainer.Add(referenceResolutionField);
        scaleWithScreenContainer.Add(screenMatchModeField);
        scaleWithScreenContainer.Add(matchField);
        inlineSettings.Add(scaleWithScreenContainer);

        // Sort Order (always visible when inline)
        var sortOrderField = new PropertyField(serializedObject.FindProperty("_sortOrder"), "Sort Order");
        inlineSettings.Add(sortOrderField);

        container.Add(inlineSettings);

        // Helper to update visibility based on scale mode
        void UpdateScaleModeVisibility() {
            var mode = (PanelScaleMode)scaleModeProp.enumValueIndex;
            constantPixelContainer.style.display = mode == PanelScaleMode.ConstantPixelSize ? DisplayStyle.Flex : DisplayStyle.None;
            constantPhysicalContainer.style.display = mode == PanelScaleMode.ConstantPhysicalSize ? DisplayStyle.Flex : DisplayStyle.None;
            scaleWithScreenContainer.style.display = mode == PanelScaleMode.ScaleWithScreenSize ? DisplayStyle.Flex : DisplayStyle.None;

            // Match slider only visible for MatchWidthOrHeight
            if (mode == PanelScaleMode.ScaleWithScreenSize) {
                var screenMatchProp = serializedObject.FindProperty("_screenMatchMode");
                var matchMode = (PanelScreenMatchMode)screenMatchProp.enumValueIndex;
                matchField.style.display = matchMode == PanelScreenMatchMode.MatchWidthOrHeight ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // Helper to update inline settings visibility
        void UpdateInlineVisibility() {
            var hasPanelSettings = panelSettingsProp.objectReferenceValue != null;
            inlineSettings.style.display = hasPanelSettings ? DisplayStyle.None : DisplayStyle.Flex;
            if (!hasPanelSettings) {
                UpdateScaleModeVisibility();
            }
        }

        // Initial visibility
        UpdateInlineVisibility();

        // Register callbacks for dynamic updates
        panelSettingsField.RegisterValueChangeCallback(evt => {
            serializedObject.Update();
            UpdateInlineVisibility();
        });

        scaleModeField.RegisterValueChangeCallback(evt => {
            serializedObject.Update();
            UpdateScaleModeVisibility();
        });

        screenMatchModeField.RegisterValueChangeCallback(evt => {
            serializedObject.Update();
            var screenMatchProp = serializedObject.FindProperty("_screenMatchMode");
            var matchMode = (PanelScreenMatchMode)screenMatchProp.enumValueIndex;
            matchField.style.display = matchMode == PanelScreenMatchMode.MatchWidthOrHeight ? DisplayStyle.Flex : DisplayStyle.None;
        });

        // Help box
        var helpBox = new HelpBox(
            "No Panel Settings asset assigned. A PanelSettings will be created at runtime using the settings above.",
            HelpBoxMessageType.Info
        );
        helpBox.style.marginTop = 4;
        helpBox.style.display = panelSettingsProp.objectReferenceValue == null ? DisplayStyle.Flex : DisplayStyle.None;

        panelSettingsField.RegisterValueChangeCallback(evt => {
            helpBox.style.display = panelSettingsProp.objectReferenceValue == null ? DisplayStyle.Flex : DisplayStyle.None;
        });

        container.Add(helpBox);

        return container;
    }

    VisualElement CreateLiveReloadSection() {
        var container = new VisualElement();
        container.style.marginBottom = 10;

        var liveReloadProp = serializedObject.FindProperty("_liveReload");
        var liveReloadField = new PropertyField(liveReloadProp);
        container.Add(liveReloadField);

        var pollIntervalContainer = new VisualElement();
        pollIntervalContainer.style.marginLeft = 15;
        var pollIntervalField = new PropertyField(serializedObject.FindProperty("_pollInterval"));
        pollIntervalContainer.Add(pollIntervalField);
        container.Add(pollIntervalContainer);

        // Update visibility based on liveReload value
        pollIntervalContainer.style.display = liveReloadProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
        liveReloadField.RegisterValueChangeCallback(evt => {
            pollIntervalContainer.style.display = liveReloadProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
        });

        return container;
    }

    VisualElement CreateBuildSettingsSection() {
        var container = new VisualElement();
        container.style.marginBottom = 10;

        var header = new Label("Build Settings");
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.marginBottom = 4;
        header.style.marginTop = 6;
        container.Add(header);

        var embeddedScriptField = new PropertyField(serializedObject.FindProperty("_embeddedScript"));
        container.Add(embeddedScriptField);

        var streamingAssetsPathField = new PropertyField(serializedObject.FindProperty("_streamingAssetsPath"));
        container.Add(streamingAssetsPathField);

        // Help box for build info
        var embeddedScriptProp = serializedObject.FindProperty("_embeddedScript");
        var streamingAssetsPathProp = serializedObject.FindProperty("_streamingAssetsPath");

        var helpBox = new HelpBox(
            $"Bundle will be auto-copied to StreamingAssets/{streamingAssetsPathProp.stringValue} during build.",
            HelpBoxMessageType.Info
        );
        helpBox.style.marginTop = 4;
        helpBox.style.display = embeddedScriptProp.objectReferenceValue == null ? DisplayStyle.Flex : DisplayStyle.None;
        container.Add(helpBox);

        embeddedScriptField.RegisterValueChangeCallback(evt => {
            helpBox.style.display = embeddedScriptProp.objectReferenceValue == null ? DisplayStyle.Flex : DisplayStyle.None;
        });

        streamingAssetsPathField.RegisterValueChangeCallback(evt => {
            helpBox.text = $"Bundle will be auto-copied to StreamingAssets/{streamingAssetsPathProp.stringValue} during build.";
        });

        return container;
    }

    VisualElement CreateScaffoldingSection() {
        var container = new VisualElement();
        container.style.marginBottom = 10;

        var header = new Label("Scaffolding");
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.marginBottom = 4;
        header.style.marginTop = 6;
        container.Add(header);

        // Default files list
        var defaultFilesField = new PropertyField(serializedObject.FindProperty("_defaultFiles"));
        defaultFilesField.label = "Default Files";
        container.Add(defaultFilesField);

        return container;
    }

    VisualElement CreateAdvancedSection() {
        var container = new VisualElement();
        container.style.marginBottom = 10;

        var header = new Label("Advanced");
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.marginBottom = 4;
        header.style.marginTop = 6;
        container.Add(header);

        // Stylesheets list
        var stylesheetsField = new PropertyField(serializedObject.FindProperty("_stylesheets"));
        stylesheetsField.label = "Stylesheets";
        container.Add(stylesheetsField);

        // Preloads list
        var preloadsField = new PropertyField(serializedObject.FindProperty("_preloads"));
        preloadsField.label = "Preloads";
        container.Add(preloadsField);

        // Globals list
        var globalsField = new PropertyField(serializedObject.FindProperty("_globals"));
        globalsField.label = "Globals";
        container.Add(globalsField);

        return container;
    }

    VisualElement CreateActionsSection() {
        var container = new VisualElement();
        container.style.marginBottom = 10;

        var header = new Label("Actions");
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.marginBottom = 4;
        container.Add(header);

        // First row: Reload and Build
        var row1 = new VisualElement();
        row1.style.flexDirection = FlexDirection.Row;
        row1.style.marginBottom = 4;

        _reloadButton = new Button(() => _target.ForceReload()) { text = "Reload Now" };
        _reloadButton.style.height = 30;
        _reloadButton.style.flexGrow = 1;
        _reloadButton.SetEnabled(false);
        row1.Add(_reloadButton);

        _buildButton = new Button(RunBuild) { text = "Build" };
        _buildButton.style.height = 30;
        _buildButton.style.flexGrow = 1;
        row1.Add(_buildButton);

        container.Add(row1);

        // Second row: Open Folder and Open Terminal
        var row2 = new VisualElement();
        row2.style.flexDirection = FlexDirection.Row;

        var openFolderButton = new Button(OpenWorkingDirectory) { text = "Open Folder" };
        openFolderButton.style.height = 24;
        openFolderButton.style.flexGrow = 1;
        row2.Add(openFolderButton);

        var openTerminalButton = new Button(OpenTerminal) { text = "Open Terminal" };
        openTerminalButton.style.height = 24;
        openTerminalButton.style.flexGrow = 1;
        row2.Add(openTerminalButton);

        container.Add(row2);

        // Build output box
        _buildOutputBox = new HelpBox("", HelpBoxMessageType.Info);
        _buildOutputBox.style.marginTop = 5;
        _buildOutputBox.style.display = DisplayStyle.None;
        container.Add(_buildOutputBox);

        return container;
    }

    VisualElement CreatePremiumSection() {
        var container = new VisualElement();
        container.style.marginTop = 10;

        var header = new Label("Premium Components");
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.marginBottom = 4;
        container.Add(header);

        var box = new VisualElement();
        box.style.backgroundColor = new Color(0.24f, 0.24f, 0.24f);
        box.style.borderTopWidth = box.style.borderBottomWidth =
            box.style.borderLeftWidth = box.style.borderRightWidth = 1;
        box.style.borderTopColor = box.style.borderBottomColor =
            box.style.borderLeftColor = box.style.borderRightColor = new Color(0.14f, 0.14f, 0.14f);
        box.style.borderTopLeftRadius = box.style.borderTopRightRadius =
            box.style.borderBottomLeftRadius = box.style.borderBottomRightRadius = 3;
        box.style.paddingTop = box.style.paddingBottom = 8;
        box.style.paddingLeft = box.style.paddingRight = 10;

        // License key row
        var keyRow = new VisualElement();
        keyRow.style.flexDirection = FlexDirection.Row;
        keyRow.style.alignItems = Align.Center;
        keyRow.style.marginBottom = 5;

        var keyLabel = new Label("License Key");
        keyLabel.style.width = 80;
        keyRow.Add(keyLabel);

        _licenseKeyField = new TextField();
        _licenseKeyField.style.flexGrow = 1;
        _licenseKeyField.RegisterValueChangedCallback(evt => _licenseKey = evt.newValue);
        keyRow.Add(_licenseKeyField);

        box.Add(keyRow);

        // Buttons row
        var buttonsRow = new VisualElement();
        buttonsRow.style.flexDirection = FlexDirection.Row;
        buttonsRow.style.marginBottom = 5;

        _activateButton = new Button(ActivateLicense) { text = "Activate" };
        _activateButton.style.height = 24;
        _activateButton.style.flexGrow = 1;
        buttonsRow.Add(_activateButton);

        var checkUpdatesButton = new Button(CheckForUpdates) { text = "Check Updates" };
        checkUpdatesButton.style.height = 24;
        checkUpdatesButton.style.flexGrow = 1;
        buttonsRow.Add(checkUpdatesButton);

        box.Add(buttonsRow);

        // License status box
        _licenseStatusBox = new HelpBox("", HelpBoxMessageType.None);
        _licenseStatusBox.style.display = DisplayStyle.None;
        _licenseStatusBox.style.marginBottom = 5;
        box.Add(_licenseStatusBox);

        // Info text
        var infoLabel = new Label("Premium components will be downloaded to: comps/");
        infoLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
        infoLabel.style.fontSize = 10;
        box.Add(infoLabel);

        container.Add(box);
        return container;
    }

    void UpdateDynamicUI() {
        if (_statusLabel == null) return;

        // Update status
        if (Application.isPlaying) {
            if (_target.IsRunning) {
                _statusLabel.text = "Running";
                _statusLabel.style.color = new Color(0.2f, 0.8f, 0.2f);
            } else {
                _statusLabel.text = "Loading...";
                _statusLabel.style.color = new Color(0.8f, 0.6f, 0.2f);
            }
        } else {
            _statusLabel.text = "Stopped (Enter Play Mode to run)";
            _statusLabel.style.color = Color.white;
        }

        // Update live reload info
        if (Application.isPlaying && _target.IsRunning) {
            _liveReloadInfo.style.display = DisplayStyle.Flex;

            var pollInterval = serializedObject.FindProperty("_pollInterval").floatValue;
            if (_target.IsLiveReloadEnabled) {
                _liveReloadStatusLabel.text = $"Live Reload: Enabled (polling every {pollInterval}s)";
            } else {
                _liveReloadStatusLabel.text = "Live Reload: Disabled";
            }

            if (_target.ReloadCount > 0) {
                _reloadCountLabel.style.display = DisplayStyle.Flex;
                _reloadCountLabel.text = $"Reloads: {_target.ReloadCount}";
            } else {
                _reloadCountLabel.style.display = DisplayStyle.None;
            }
        } else {
            _liveReloadInfo.style.display = DisplayStyle.None;
        }

        // Update entry file status
        var entryPath = _target.EntryFileFullPath;
        var fileExists = File.Exists(entryPath);
        _entryFileStatusLabel.text = fileExists ? "Found" : "Not Found";
        _entryFileStatusLabel.style.color = fileExists ? Color.white : new Color(0.8f, 0.4f, 0.4f);

        // Update reload button state
        _reloadButton.SetEnabled(Application.isPlaying && _target.IsRunning);

        // Update build button state
        _buildButton.SetEnabled(!_buildInProgress);
        _buildButton.text = _buildInProgress ? "Building..." : "Build";

        // Update build output
        if (!string.IsNullOrEmpty(_buildOutput)) {
            _buildOutputBox.style.display = DisplayStyle.Flex;
            _buildOutputBox.text = _buildOutput;
        }

        // Update activate button state
        _activateButton.SetEnabled(!_activationInProgress && !string.IsNullOrEmpty(_licenseKey));
        _activateButton.text = _activationInProgress ? "Activating..." : "Activate";

        // Update license status
        if (!string.IsNullOrEmpty(_licenseStatus)) {
            _licenseStatusBox.style.display = DisplayStyle.Flex;
            _licenseStatusBox.text = _licenseStatus;
            _licenseStatusBox.messageType = _licenseStatus.StartsWith("Error") ? HelpBoxMessageType.Error :
                                            _licenseStatus.StartsWith("Success") ? HelpBoxMessageType.Info :
                                            HelpBoxMessageType.None;
        }
    }

    async void ActivateLicense() {
        if (string.IsNullOrEmpty(_licenseKey)) return;

        _activationInProgress = true;
        _licenseStatus = "Contacting server...";

        try {
            // TODO: Make API URL configurable
            var apiUrl = "http://localhost:8790/api/activate";
            var payload = $"{{\"key\":\"{_licenseKey}\"}}";

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(apiUrl, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            // Parse response (simple JSON parsing)
            if (responseBody.Contains("\"success\":true")) {
                // Extract download URL
                var downloadUrlStart = responseBody.IndexOf("\"downloadUrl\":\"") + 15;
                var downloadUrlEnd = responseBody.IndexOf("\"", downloadUrlStart);
                var downloadPath = responseBody.Substring(downloadUrlStart, downloadUrlEnd - downloadUrlStart);

                // Build full download URL
                var baseUrl = apiUrl.Substring(0, apiUrl.LastIndexOf("/api/"));
                var downloadUrl = baseUrl + downloadPath;

                _licenseStatus = "Downloading components...";

                await DownloadAndExtract(downloadUrl);
            } else {
                // Extract error message
                var errorStart = responseBody.IndexOf("\"error\":\"");
                if (errorStart >= 0) {
                    errorStart += 9;
                    var errorEnd = responseBody.IndexOf("\"", errorStart);
                    var error = responseBody.Substring(errorStart, errorEnd - errorStart);
                    _licenseStatus = $"Error: {error}";
                } else {
                    _licenseStatus = "Error: Activation failed";
                }
            }
        } catch (HttpRequestException ex) {
            _licenseStatus = $"Error: Could not connect to server. Is the website running?\n{ex.Message}";
        } catch (TaskCanceledException) {
            _licenseStatus = "Error: Request timed out";
        } catch (Exception ex) {
            _licenseStatus = $"Error: {ex.Message}";
        } finally {
            _activationInProgress = false;
        }
    }

    async Task DownloadAndExtract(string downloadUrl) {
        try {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(5);

            var response = await client.GetAsync(downloadUrl);

            if (!response.IsSuccessStatusCode) {
                var errorBody = await response.Content.ReadAsStringAsync();
                if (errorBody.Contains("Asset not found")) {
                    _licenseStatus = "Error: Component package not yet available. Please contact support.";
                } else {
                    _licenseStatus = $"Error: Download failed ({response.StatusCode})";
                }
                return;
            }

            var zipBytes = await response.Content.ReadAsByteArrayAsync();

            // Save to temp file
            var tempPath = Path.Combine(Application.temporaryCachePath, "onejs-comps.zip");
            File.WriteAllBytes(tempPath, zipBytes);

            // Extract to working directory
            var extractPath = Path.Combine(_target.WorkingDirFullPath, "comps");

            // Delete existing directory to allow overwrite
            if (Directory.Exists(extractPath)) {
                Directory.Delete(extractPath, recursive: true);
            }
            Directory.CreateDirectory(extractPath);

            // Extract
            ZipFile.ExtractToDirectory(tempPath, extractPath);

            // Clean up temp file
            File.Delete(tempPath);

            _licenseStatus = $"Success! Components installed to: comps/";

            // Refresh asset database
            AssetDatabase.Refresh();

        } catch (Exception ex) {
            _licenseStatus = $"Error extracting: {ex.Message}";
        }
    }

    void CheckForUpdates() {
        // TODO: Implement version checking
        _licenseStatus = "Update checking not yet implemented";
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
