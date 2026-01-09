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
[InitializeOnLoad]
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

    // Tabs
    VisualElement _tabContainer;
    VisualElement _tabContent;
    int _selectedTab = 0;
    readonly string[] _tabNames = { "Modules", "UI", "Cartridges" };

    // Lists
    VisualElement _moduleListContainer;
    VisualElement _stylesheetListContainer;
    VisualElement _cartridgeListContainer;

    // Static initialization to handle play mode changes for ALL JSPad instances
    static JSPadEditor() {
        EditorApplication.playModeStateChanged += OnPlayModeStateChangedStatic;
    }

    static void OnPlayModeStateChangedStatic(PlayModeStateChange state) {
        if (state == PlayModeStateChange.ExitingEditMode) {
            // Build all JSPad instances before entering play mode
            BuildAllJSPadsSync();
        }
    }

    static void BuildAllJSPadsSync() {
        var jsPads = UnityEngine.Object.FindObjectsByType<JSPad>(FindObjectsSortMode.None);
        foreach (var jsPad in jsPads) {
            if (jsPad == null || !jsPad.gameObject.activeInHierarchy) continue;
            BuildJSPadSync(jsPad);
        }
    }

    static void BuildJSPadSync(JSPad jsPad) {
        jsPad.EnsureTempDirectory();
        jsPad.WriteSourceFile();
        jsPad.ExtractCartridges();

        var npmPath = GetNpmCommandStatic();
        var nodeBinDir = Path.GetDirectoryName(npmPath);

        // Install dependencies if needed (synchronous)
        if (!jsPad.HasNodeModules()) {
            var installInfo = new ProcessStartInfo {
                FileName = npmPath,
                Arguments = "install",
                WorkingDirectory = jsPad.TempDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            var existingPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            installInfo.EnvironmentVariables["PATH"] = nodeBinDir + Path.PathSeparator + existingPath;

            using (var installProcess = Process.Start(installInfo)) {
                installProcess.WaitForExit();
                if (installProcess.ExitCode != 0) {
                    Debug.LogError($"[JSPad] npm install failed for {jsPad.name}");
                    return;
                }
            }
        }

        // Build (synchronous)
        var buildInfo = new ProcessStartInfo {
            FileName = npmPath,
            Arguments = "run build",
            WorkingDirectory = jsPad.TempDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        buildInfo.EnvironmentVariables["PATH"] = nodeBinDir + Path.PathSeparator + path;

        using (var buildProcess = Process.Start(buildInfo)) {
            buildProcess.WaitForExit();
            if (buildProcess.ExitCode == 0) {
                jsPad.SetBuildState(JSPad.BuildState.Ready, output: "Build successful");
                jsPad.SaveBundleToSerializedFields();
            } else {
                var error = buildProcess.StandardError.ReadToEnd();
                jsPad.SetBuildState(JSPad.BuildState.Error, error: error);
                Debug.LogError($"[JSPad] Build failed for {jsPad.name}: {error}");
            }
        }
    }

    static string _cachedNpmPathStatic;

    static string GetNpmCommandStatic() {
        if (!string.IsNullOrEmpty(_cachedNpmPathStatic)) return _cachedNpmPathStatic;

#if UNITY_EDITOR_WIN
        _cachedNpmPathStatic = "npm.cmd";
        return _cachedNpmPathStatic;
#else
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string[] fixedPaths = {
            "/usr/local/bin/npm",
            "/opt/homebrew/bin/npm",
            "/usr/bin/npm",
        };

        foreach (var p in fixedPaths) {
            if (File.Exists(p)) {
                _cachedNpmPathStatic = p;
                return _cachedNpmPathStatic;
            }
        }

        var nvmDir = Path.Combine(home, ".nvm/versions/node");
        if (Directory.Exists(nvmDir)) {
            try {
                foreach (var nodeVersionDir in Directory.GetDirectories(nvmDir)) {
                    var npmPath = Path.Combine(nodeVersionDir, "bin", "npm");
                    if (File.Exists(npmPath)) {
                        _cachedNpmPathStatic = npmPath;
                        return _cachedNpmPathStatic;
                    }
                }
            } catch { }
        }

        var nDir = Path.Combine(home, "n/bin/npm");
        if (File.Exists(nDir)) {
            _cachedNpmPathStatic = nDir;
            return _cachedNpmPathStatic;
        }

        return "npm";
#endif
    }

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
        // NOTE: Building is handled by the static OnPlayModeStateChangedStatic handler
        // which builds ALL JSPads before entering play mode.
        // Running is handled by JSPad.Start() which auto-runs if HasBuiltOutput.
        // This instance handler only needs to restore source code when exiting play mode.
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
        statusRow.style.alignItems = Align.Center;
        var statusTitle = new Label("Status");
        statusTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        statusTitle.style.width = 50;
        _statusLabel = new Label("Not initialized");
        _statusLabel.style.flexGrow = 1;
        statusRow.Add(statusTitle);
        statusRow.Add(_statusLabel);

        // Overflow menu button
        var menuButton = new Button(ShowOverflowMenu) { text = "⋮" };
        menuButton.style.width = 24;
        menuButton.style.height = 20;
        menuButton.style.marginLeft = 4;
        menuButton.style.unityFontStyleAndWeight = FontStyle.Bold;
        menuButton.style.fontSize = 14;
        menuButton.tooltip = "More options";
        statusRow.Add(menuButton);

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

        // Settings foldout (contains tabs)
        var settingsFoldout = new Foldout { text = "Settings", value = false };
        settingsFoldout.style.marginBottom = 6;
        _root.Add(settingsFoldout);

        // Tabs section
        _tabContainer = new VisualElement();
        _tabContainer.style.flexDirection = FlexDirection.Row;
        settingsFoldout.Add(_tabContainer);

        // Tab content container
        _tabContent = new VisualElement();
        _tabContent.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f);
        _tabContent.style.borderTopWidth = 0; // No top border (tabs handle this)
        _tabContent.style.borderLeftWidth = _tabContent.style.borderRightWidth = _tabContent.style.borderBottomWidth = 1;
        _tabContent.style.borderLeftColor = _tabContent.style.borderRightColor = _tabContent.style.borderBottomColor = new Color(0.14f, 0.14f, 0.14f);
        _tabContent.style.borderTopLeftRadius = _tabContent.style.borderTopRightRadius = 0;
        _tabContent.style.borderBottomLeftRadius = _tabContent.style.borderBottomRightRadius = 3;
        _tabContent.style.paddingTop = _tabContent.style.paddingBottom = 10;
        _tabContent.style.paddingLeft = _tabContent.style.paddingRight = 10;
        _tabContent.style.marginBottom = 10;
        _tabContent.style.minHeight = 80;
        settingsFoldout.Add(_tabContent);

        // Build tabs
        BuildTabs();

        // Code editor (at the bottom)
        _codeField = new CodeField();
        _codeField.bindingPath = "_sourceCode";
        _codeField.AutoHeight = true;
        _codeField.MinLines = 15;
        _codeField.LineHeight = 15f;

        // Style the text input
        var textInput = _codeField.Q<TextElement>();
        if (textInput != null) {
            textInput.style.fontSize = 12;
            textInput.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
            textInput.style.paddingTop = textInput.style.paddingBottom = 8;
            textInput.style.paddingLeft = textInput.style.paddingRight = 8;
        }

        _codeField.BindProperty(_sourceCode);

        // Save to EditorPrefs during Play Mode
        _codeField.RegisterValueChangedCallback(evt => SaveSourceCodeToPrefs());

        _root.Add(_codeField);

        // Schedule status updates
        _root.schedule.Execute(UpdateUI).Every(100);

        return _root;
    }

    void BuildTabs() {
        _tabContainer.Clear();

        var borderColor = new Color(0.14f, 0.14f, 0.14f);

        for (int i = 0; i < _tabNames.Length; i++) {
            var tabIndex = i;
            var tab = new Button(() => SelectTab(tabIndex)) { text = _tabNames[i] };
            tab.style.flexGrow = 1;
            tab.style.height = 24;
            tab.style.marginTop = tab.style.marginBottom = tab.style.marginLeft = tab.style.marginRight = 0;
            tab.focusable = false;

            // Border: top always, left for all (acts as divider), right only for last
            tab.style.borderTopWidth = 1;
            tab.style.borderTopColor = borderColor;
            tab.style.borderLeftWidth = 1; // All have left border (acts as divider for non-first)
            tab.style.borderLeftColor = borderColor;
            tab.style.borderRightWidth = i == _tabNames.Length - 1 ? 1 : 0;
            tab.style.borderRightColor = borderColor;

            // Only outer corners rounded
            tab.style.borderTopLeftRadius = i == 0 ? 3 : 0;
            tab.style.borderTopRightRadius = i == _tabNames.Length - 1 ? 3 : 0;
            tab.style.borderBottomLeftRadius = 0;
            tab.style.borderBottomRightRadius = 0;

            // Active tab: no bottom border (merges with content)
            // Inactive tab: has bottom border
            bool isActive = _selectedTab == i;
            tab.style.borderBottomWidth = isActive ? 0 : 1;
            tab.style.borderBottomColor = borderColor;

            tab.style.backgroundColor = isActive
                ? new Color(0.22f, 0.22f, 0.22f)  // Match content bg
                : new Color(0.2f, 0.2f, 0.2f);

            // Hover effect (just bg color, no outlines)
            tab.RegisterCallback<MouseEnterEvent>(evt => {
                if (_selectedTab != tabIndex)
                    tab.style.backgroundColor = new Color(0.26f, 0.26f, 0.26f);
            });
            tab.RegisterCallback<MouseLeaveEvent>(evt => {
                tab.style.backgroundColor = _selectedTab == tabIndex
                    ? new Color(0.22f, 0.22f, 0.22f)
                    : new Color(0.2f, 0.2f, 0.2f);
            });

            _tabContainer.Add(tab);
        }

        RebuildTabContent();
    }

    void SelectTab(int index) {
        _selectedTab = index;
        BuildTabs();
    }

    void RebuildTabContent() {
        _tabContent.Clear();

        switch (_selectedTab) {
            case 0: // Modules
                BuildModulesTab();
                break;
            case 1: // UI
                BuildUITab();
                break;
            case 2: // Cartridges
                BuildCartridgesTab();
                break;
        }
    }

    void BuildModulesTab() {
        // Header with Add button
        var headerRow = new VisualElement();
        headerRow.style.flexDirection = FlexDirection.Row;
        headerRow.style.marginBottom = 6;

        var headerLabel = new Label("NPM Deps");
        headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        headerLabel.style.flexGrow = 1;
        headerRow.Add(headerLabel);

        var addButton = new Button(() => {
            var prop = serializedObject.FindProperty("_modules");
            prop.arraySize++;
            serializedObject.ApplyModifiedProperties();
            RebuildModuleList();
        }) { text = "+" };
        addButton.style.width = 24;
        addButton.style.height = 20;
        addButton.tooltip = "Add a new module dependency";
        headerRow.Add(addButton);

        _tabContent.Add(headerRow);

        // Module list container
        _moduleListContainer = new VisualElement();
        _tabContent.Add(_moduleListContainer);

        RebuildModuleList();

        // Install button
        var installButton = new Button(InstallDependencies) { text = "Install" };
        installButton.style.marginTop = 6;
        installButton.style.height = 24;
        installButton.tooltip = "Run npm install (clears node_modules first)";
        _tabContent.Add(installButton);
    }

    void InstallDependencies() {
        if (_isProcessing) return;

        // Clear node_modules to force reinstall
        var nodeModulesPath = Path.Combine(_target.TempDir, "node_modules");
        if (Directory.Exists(nodeModulesPath)) {
            try {
                Directory.Delete(nodeModulesPath, recursive: true);
            } catch { }
        }

        _target.EnsureTempDirectory();
        RunNpmInstall(null);
    }

    void RebuildModuleList() {
        if (_moduleListContainer == null) return;

        _moduleListContainer.Clear();
        serializedObject.Update();

        var modulesProp = serializedObject.FindProperty("_modules");

        if (modulesProp.arraySize == 0) {
            var emptyLabel = new Label("No additional modules. Click + to add one.");
            emptyLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            emptyLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            emptyLabel.style.paddingTop = 4;
            emptyLabel.style.paddingBottom = 4;
            _moduleListContainer.Add(emptyLabel);
            return;
        }

        for (int i = 0; i < modulesProp.arraySize; i++) {
            var itemRow = CreateModuleItemRow(modulesProp, i);
            _moduleListContainer.Add(itemRow);
        }
    }

    VisualElement CreateModuleItemRow(SerializedProperty arrayProp, int index) {
        var elementProp = arrayProp.GetArrayElementAtIndex(index);
        var nameProp = elementProp.FindPropertyRelative("name");
        var versionProp = elementProp.FindPropertyRelative("version");

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 2;

        // Name field
        var nameField = new TextField();
        nameField.value = nameProp.stringValue;
        nameField.style.flexGrow = 2;
        nameField.style.marginRight = 4;
        nameField.RegisterValueChangedCallback(evt => {
            nameProp.stringValue = evt.newValue;
            serializedObject.ApplyModifiedProperties();
        });
        row.Add(nameField);

        // Version field
        var versionField = new TextField();
        versionField.value = versionProp.stringValue;
        versionField.style.flexGrow = 1;
        versionField.style.marginRight = 4;
        versionField.RegisterValueChangedCallback(evt => {
            versionProp.stringValue = evt.newValue;
            serializedObject.ApplyModifiedProperties();
        });
        row.Add(versionField);

        // Remove button
        var removeBtn = new Button(() => {
            arrayProp.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            RebuildModuleList();
        }) { text = "X" };
        removeBtn.style.width = 24;
        removeBtn.style.height = 20;
        removeBtn.tooltip = "Remove this module";
        row.Add(removeBtn);

        return row;
    }

    void BuildUITab() {
        // Panel Settings section
        var panelSettingsProp = serializedObject.FindProperty("_panelSettings");
        var panelSettingsField = new PropertyField(panelSettingsProp, "Panel Settings");
        panelSettingsField.BindProperty(panelSettingsProp);
        _tabContent.Add(panelSettingsField);

        var themeStylesheetProp = serializedObject.FindProperty("_defaultThemeStylesheet");
        var themeStylesheetField = new PropertyField(themeStylesheetProp, "Theme Stylesheet");
        themeStylesheetField.BindProperty(themeStylesheetProp);
        _tabContent.Add(themeStylesheetField);

        var scaleModeProp = serializedObject.FindProperty("_scaleMode");
        var scaleModeField = new PropertyField(scaleModeProp, "Scale Mode");
        scaleModeField.BindProperty(scaleModeProp);
        _tabContent.Add(scaleModeField);

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

        _tabContent.Add(scaleWithScreenSizeFields);

        // Constant Pixel Size fields
        var scaleProp = serializedObject.FindProperty("_scale");
        var scaleField = new PropertyField(scaleProp, "Scale");
        scaleField.BindProperty(scaleProp);
        constantPixelSizeFields.Add(scaleField);

        _tabContent.Add(constantPixelSizeFields);

        var sortOrderProp = serializedObject.FindProperty("_sortOrder");
        var sortOrderField = new PropertyField(sortOrderProp, "Sort Order");
        sortOrderField.BindProperty(sortOrderProp);
        _tabContent.Add(sortOrderField);

        // Update visibility based on scale mode
        void UpdateScaleModeVisibility() {
            serializedObject.Update();
            var mode = (PanelScaleMode)scaleModeProp.enumValueIndex;
            scaleWithScreenSizeFields.style.display = mode == PanelScaleMode.ScaleWithScreenSize
                ? DisplayStyle.Flex : DisplayStyle.None;
            constantPixelSizeFields.style.display = mode == PanelScaleMode.ConstantPixelSize
                ? DisplayStyle.Flex : DisplayStyle.None;
        }

        UpdateScaleModeVisibility();
        scaleModeField.RegisterValueChangeCallback(_ => UpdateScaleModeVisibility());

        // Separator
        var separator = new VisualElement();
        separator.style.height = 1;
        separator.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f);
        separator.style.marginTop = 10;
        separator.style.marginBottom = 10;
        _tabContent.Add(separator);

        // Stylesheets section
        var stylesheetsHeader = new VisualElement();
        stylesheetsHeader.style.flexDirection = FlexDirection.Row;
        stylesheetsHeader.style.marginBottom = 6;

        var stylesheetsLabel = new Label("Stylesheets");
        stylesheetsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        stylesheetsLabel.style.flexGrow = 1;
        stylesheetsHeader.Add(stylesheetsLabel);

        var addStylesheetBtn = new Button(() => {
            var prop = serializedObject.FindProperty("_stylesheets");
            prop.arraySize++;
            serializedObject.ApplyModifiedProperties();
            RebuildStylesheetList();
        }) { text = "+" };
        addStylesheetBtn.style.width = 24;
        addStylesheetBtn.style.height = 20;
        addStylesheetBtn.tooltip = "Add a USS stylesheet";
        stylesheetsHeader.Add(addStylesheetBtn);

        _tabContent.Add(stylesheetsHeader);

        // Stylesheet list container
        _stylesheetListContainer = new VisualElement();
        _tabContent.Add(_stylesheetListContainer);

        RebuildStylesheetList();
    }

    void RebuildStylesheetList() {
        if (_stylesheetListContainer == null) return;

        _stylesheetListContainer.Clear();
        serializedObject.Update();

        var stylesheetsProp = serializedObject.FindProperty("_stylesheets");

        if (stylesheetsProp.arraySize == 0) {
            var emptyLabel = new Label("No stylesheets. Click + to add one.");
            emptyLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            emptyLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            emptyLabel.style.paddingTop = 4;
            emptyLabel.style.paddingBottom = 4;
            _stylesheetListContainer.Add(emptyLabel);
            return;
        }

        for (int i = 0; i < stylesheetsProp.arraySize; i++) {
            var itemRow = CreateStylesheetItemRow(stylesheetsProp, i);
            _stylesheetListContainer.Add(itemRow);
        }
    }

    VisualElement CreateStylesheetItemRow(SerializedProperty arrayProp, int index) {
        var elementProp = arrayProp.GetArrayElementAtIndex(index);
        var stylesheet = elementProp.objectReferenceValue as StyleSheet;

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 2;

        // Index label
        var indexLabel = new Label($"{index}");
        indexLabel.style.width = 16;
        indexLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
        row.Add(indexLabel);

        // Object field
        var objectField = new ObjectField();
        objectField.objectType = typeof(StyleSheet);
        objectField.value = stylesheet;
        objectField.style.flexGrow = 1;
        objectField.style.marginLeft = 4;
        objectField.RegisterValueChangedCallback(evt => {
            elementProp.objectReferenceValue = evt.newValue;
            serializedObject.ApplyModifiedProperties();
        });
        row.Add(objectField);

        // Remove button
        var removeBtn = new Button(() => {
            arrayProp.GetArrayElementAtIndex(index).objectReferenceValue = null;
            arrayProp.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            RebuildStylesheetList();
        }) { text = "X" };
        removeBtn.style.width = 24;
        removeBtn.style.height = 20;
        removeBtn.tooltip = "Remove this stylesheet";
        row.Add(removeBtn);

        return row;
    }

    void BuildCartridgesTab() {
        // Header with Add button
        var headerRow = new VisualElement();
        headerRow.style.flexDirection = FlexDirection.Row;
        headerRow.style.marginBottom = 6;

        var headerLabel = new Label("UI Cartridges");
        headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        headerLabel.style.flexGrow = 1;
        headerRow.Add(headerLabel);

        var addButton = new Button(() => {
            var prop = serializedObject.FindProperty("_cartridges");
            prop.arraySize++;
            serializedObject.ApplyModifiedProperties();
            RebuildCartridgeList();
        }) { text = "+" };
        addButton.style.width = 24;
        addButton.style.height = 20;
        addButton.tooltip = "Add a new cartridge slot";
        headerRow.Add(addButton);

        _tabContent.Add(headerRow);

        // Cartridge list container
        _cartridgeListContainer = new VisualElement();
        _tabContent.Add(_cartridgeListContainer);

        RebuildCartridgeList();

        // Help box
        var cartridgesHelp = new HelpBox(
            "Files are auto-extracted to @cartridges/{slug}/ on build. Objects are injected as __cartridges.{slug}.{key} at runtime.",
            HelpBoxMessageType.Info
        );
        cartridgesHelp.style.marginTop = 6;
        _tabContent.Add(cartridgesHelp);
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
            _statusLabel.text = "Ready";
            _statusLabel.style.color = Color.white;
        }
    }

    void ShowOverflowMenu() {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Open Folder"), false, OpenTempFolder);
        menu.AddItem(new GUIContent("Clean"), false, Clean);
        menu.ShowAsContext();
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

    void Build(bool runAfter) {
        if (_isProcessing) return;
        if (runAfter && !Application.isPlaying) return; // Can't run outside play mode

        _target.EnsureTempDirectory();
        _target.WriteSourceFile();
        _target.ExtractCartridges();

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

            _currentProcess.OutputDataReceived += (s, e) => { };
            _currentProcess.ErrorDataReceived += (s, e) => { };

            _currentProcess.EnableRaisingEvents = true;
            _currentProcess.Exited += (s, e) => {
                var exitCode = _currentProcess.ExitCode;
                _currentProcess = null;

                EditorApplication.delayCall += () => {
                    if (exitCode == 0) {
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

            _currentProcess.OutputDataReceived += (s, e) => { };
            _currentProcess.ErrorDataReceived += (s, e) => {
                if (!string.IsNullOrEmpty(e.Data)) errorOutput += e.Data + "\n";
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
                        // Save bundle to serialized fields for standalone builds
                        _target.SaveBundleToSerializedFields();
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
            } catch (Exception ex) {
                Debug.LogError($"[JSPad] Failed to clean: {ex.Message}");
            }
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

    // MARK: Cartridge Management

    void RebuildCartridgeList() {
        if (_cartridgeListContainer == null) return;

        _cartridgeListContainer.Clear();
        serializedObject.Update();

        var cartridgesProp = serializedObject.FindProperty("_cartridges");

        if (cartridgesProp.arraySize == 0) {
            var emptyLabel = new Label("No cartridges. Click + to add one.");
            emptyLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            emptyLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            emptyLabel.style.paddingLeft = 4;
            emptyLabel.style.paddingTop = 4;
            emptyLabel.style.paddingBottom = 4;
            _cartridgeListContainer.Add(emptyLabel);
            return;
        }

        for (int i = 0; i < cartridgesProp.arraySize; i++) {
            var itemRow = CreateCartridgeItemRow(cartridgesProp, i);
            _cartridgeListContainer.Add(itemRow);
        }
    }

    VisualElement CreateCartridgeItemRow(SerializedProperty arrayProp, int index) {
        var elementProp = arrayProp.GetArrayElementAtIndex(index);
        var cartridge = elementProp.objectReferenceValue as UICartridge;

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 2;
        row.style.paddingTop = 2;
        row.style.paddingBottom = 2;
        row.style.paddingLeft = 4;
        row.style.paddingRight = 4;
        row.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f);
        row.style.borderTopLeftRadius = row.style.borderTopRightRadius =
            row.style.borderBottomLeftRadius = row.style.borderBottomRightRadius = 3;

        // Index label
        var indexLabel = new Label($"{index}");
        indexLabel.style.width = 16;
        indexLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
        row.Add(indexLabel);

        // Object field for cartridge
        var objectField = new ObjectField();
        objectField.objectType = typeof(UICartridge);
        objectField.value = cartridge;
        objectField.style.flexGrow = 1;
        objectField.style.marginLeft = 4;
        objectField.RegisterValueChangedCallback(evt => {
            elementProp.objectReferenceValue = evt.newValue;
            serializedObject.ApplyModifiedProperties();
            RebuildCartridgeList();
        });
        row.Add(objectField);

        // Status label
        var statusLabel = new Label();
        statusLabel.style.width = 70;
        statusLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        statusLabel.style.marginRight = 4;
        statusLabel.style.fontSize = 10;

        if (cartridge != null && !string.IsNullOrEmpty(cartridge.Slug)) {
            var cartridgePath = _target.GetCartridgePath(cartridge);
            bool isExtracted = Directory.Exists(cartridgePath);

            if (isExtracted) {
                statusLabel.text = "Extracted";
                statusLabel.style.color = new Color(0.4f, 0.7f, 0.4f);
            } else {
                statusLabel.text = "Not extracted";
                statusLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            }
        } else {
            statusLabel.text = cartridge == null ? "" : "No slug";
            statusLabel.style.color = new Color(0.8f, 0.6f, 0.2f);
        }
        row.Add(statusLabel);

        // Remove button
        var removeBtn = new Button(() => RemoveCartridgeFromList(index)) { text = "X" };
        removeBtn.style.width = 24;
        removeBtn.style.height = 20;
        removeBtn.tooltip = "Remove from list";
        row.Add(removeBtn);

        return row;
    }

    void RemoveCartridgeFromList(int index) {
        var cartridgesProp = serializedObject.FindProperty("_cartridges");
        if (index < 0 || index >= cartridgesProp.arraySize) return;

        var cartridge = cartridgesProp.GetArrayElementAtIndex(index).objectReferenceValue as UICartridge;
        string name = cartridge?.DisplayName ?? $"Item {index}";

        if (!EditorUtility.DisplayDialog(
            "Remove from List?",
            $"Remove '{name}' from the cartridge list?\n\n" +
            "(Extracted files will be cleaned on next build or Clean)",
            "Remove", "Cancel")) {
            return;
        }

        cartridgesProp.GetArrayElementAtIndex(index).objectReferenceValue = null;
        cartridgesProp.DeleteArrayElementAtIndex(index);
        serializedObject.ApplyModifiedProperties();
        RebuildCartridgeList();
    }
}
