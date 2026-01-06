using System;
using System.Diagnostics;
using System.IO;
using OneJS.Editor;
using OneJS.Editor.TypeGenerator;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using PanelScaleMode = UnityEngine.UIElements.PanelScaleMode;
using PanelScreenMatchMode = UnityEngine.UIElements.PanelScreenMatchMode;
using Debug = UnityEngine.Debug;

[CustomEditor(typeof(JSRunner))]
public class JSRunnerEditor : Editor {
    const string TabPrefKey = "JSRunner.ActiveTab";

    JSRunner _target;
    bool _buildInProgress;
    string _buildOutput;

    // Tab system
    VisualElement _tabContent;
    Button[] _tabButtons;
    int _activeTab;

    // UI Elements that need updating
    Label _statusLabel;
    Label _liveReloadStatusLabel;
    Label _reloadCountLabel;
    Label _workingDirLabel;
    Label _watcherStatusLabel;
    Button _reloadButton;
    Button _buildButton;
    HelpBox _buildOutputBox;
    VisualElement _liveReloadInfo;

    // Type generation
    Button _generateTypesButton;
    Label _typeGenStatusLabel;

    // Cartridges
    VisualElement _cartridgeListContainer;

    // Custom lists
    VisualElement _stylesheetsListContainer;
    VisualElement _preloadsListContainer;
    VisualElement _globalsListContainer;

    void OnEnable() {
        _target = (JSRunner)target;
        _activeTab = EditorPrefs.GetInt(TabPrefKey, 0);
        EditorApplication.update += UpdateDynamicUI;

        // Subscribe to watcher events
        NodeWatcherManager.OnWatcherStarted += OnWatcherStateChanged;
        NodeWatcherManager.OnWatcherStopped += OnWatcherStateChanged;

        // Try to reattach to watcher if it was running before domain reload
        var workingDir = _target.WorkingDirFullPath;
        if (!string.IsNullOrEmpty(workingDir)) {
            NodeWatcherManager.TryReattach(workingDir);
        }
    }

    void OnDisable() {
        EditorApplication.update -= UpdateDynamicUI;
        NodeWatcherManager.OnWatcherStarted -= OnWatcherStateChanged;
        NodeWatcherManager.OnWatcherStopped -= OnWatcherStateChanged;
    }

    void OnWatcherStateChanged(string workingDir) {
        // Repaint when watcher state changes (for any JSRunner, in case of shared UI)
        Repaint();
    }

    public override VisualElement CreateInspectorGUI() {
        var root = new VisualElement();

        // Status - always visible at top
        root.Add(CreateStatusSection());

        // Tab bar
        root.Add(CreateTabBar());

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
        _tabContent.style.minHeight = 100;
        root.Add(_tabContent);

        // Show initial tab
        ShowTab(_activeTab);

        // Actions - always visible at bottom
        root.Add(CreateActionsSection());

        return root;
    }

    // MARK: Tab System

    VisualElement CreateTabBar() {
        var container = new VisualElement();
        container.style.flexDirection = FlexDirection.Row;
        container.style.marginTop = 8;

        string[] tabNames = { "Project", "UI", "Cartridges", "Build" };
        _tabButtons = new Button[tabNames.Length];

        var borderColor = new Color(0.14f, 0.14f, 0.14f);

        for (int i = 0; i < tabNames.Length; i++) {
            int tabIndex = i;
            var btn = new Button(() => ShowTab(tabIndex)) { text = tabNames[i] };
            btn.style.flexGrow = 1;
            btn.style.height = 26;
            btn.style.marginTop = btn.style.marginBottom = btn.style.marginLeft = btn.style.marginRight = 0;
            btn.focusable = false;

            // Border: top always, left for first, right only for last (dividers via left border)
            btn.style.borderTopWidth = 1;
            btn.style.borderTopColor = borderColor;
            btn.style.borderLeftWidth = 1; // All have left border (acts as divider for non-first)
            btn.style.borderLeftColor = borderColor;
            btn.style.borderRightWidth = i == tabNames.Length - 1 ? 1 : 0;
            btn.style.borderRightColor = borderColor;
            btn.style.borderBottomWidth = 0; // Will be set in UpdateTabStyles

            // Only outer corners rounded
            btn.style.borderTopLeftRadius = i == 0 ? 3 : 0;
            btn.style.borderTopRightRadius = i == tabNames.Length - 1 ? 3 : 0;
            btn.style.borderBottomLeftRadius = 0;
            btn.style.borderBottomRightRadius = 0;

            // Hover effect (just bg color, no outlines)
            int idx = i;
            btn.RegisterCallback<MouseEnterEvent>(_ => {
                if (idx != _activeTab)
                    btn.style.backgroundColor = new Color(0.26f, 0.26f, 0.26f);
            });
            btn.RegisterCallback<MouseLeaveEvent>(_ => {
                if (idx != _activeTab)
                    btn.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
            });

            _tabButtons[i] = btn;
            container.Add(btn);
        }

        UpdateTabStyles();
        return container;
    }

    void ShowTab(int index) {
        _activeTab = index;
        EditorPrefs.SetInt(TabPrefKey, index);
        UpdateTabStyles();

        _tabContent.Clear();

        switch (index) {
            case 0: BuildProjectTab(_tabContent); break;
            case 1: BuildUITab(_tabContent); break;
            case 2: BuildCartridgesTab(_tabContent); break;
            case 3: BuildBuildTab(_tabContent); break;
        }

        _tabContent.Bind(serializedObject);
    }

    void UpdateTabStyles() {
        if (_tabButtons == null) return;

        var borderColor = new Color(0.14f, 0.14f, 0.14f);

        for (int i = 0; i < _tabButtons.Length; i++) {
            bool isActive = i == _activeTab;
            var btn = _tabButtons[i];

            btn.style.backgroundColor = isActive
                ? new Color(0.22f, 0.22f, 0.22f)  // Match content bg
                : new Color(0.2f, 0.2f, 0.2f);

            // Active tab: no bottom border (merges with content)
            // Inactive tab: has bottom border
            btn.style.borderBottomWidth = isActive ? 0 : 1;
            btn.style.borderBottomColor = borderColor;
        }
    }

    // MARK: Tab Content Builders

    void BuildProjectTab(VisualElement container) {
        // Scene save warning
        if (!_target.IsSceneSaved) {
            var warning = new HelpBox("Scene must be saved before JSRunner can be configured.", HelpBoxMessageType.Warning);
            warning.style.marginBottom = 8;
            container.Add(warning);
        }

        var liveReloadProp = serializedObject.FindProperty("_liveReload");
        var liveReloadField = new PropertyField(liveReloadProp);
        container.Add(liveReloadField);

        var liveReloadSettings = new VisualElement { style = { marginLeft = 15 } };
        liveReloadSettings.Add(new PropertyField(serializedObject.FindProperty("_pollInterval")));

        var janitorField = new PropertyField(serializedObject.FindProperty("_enableJanitor"), "Enable Janitor");
        janitorField.tooltip = "When enabled, spawns a Janitor GameObject on first run. On reload, all GameObjects after the Janitor in the hierarchy are destroyed.";
        liveReloadSettings.Add(janitorField);

        container.Add(liveReloadSettings);

        liveReloadSettings.style.display = liveReloadProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
        liveReloadField.RegisterValueChangeCallback(_ =>
            liveReloadSettings.style.display = liveReloadProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None);

        AddSpacer(container);

        // Preloads section
        var preloadsHeader = CreateRow();
        preloadsHeader.style.marginBottom = 4;
        var preloadsLabel = new Label("Preloads");
        preloadsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        preloadsLabel.style.flexGrow = 1;
        preloadsLabel.tooltip = "TextAssets eval'd before entry file (e.g., polyfills)";
        preloadsHeader.Add(preloadsLabel);

        var addPreloadBtn = new Button(() => {
            var prop = serializedObject.FindProperty("_preloads");
            prop.arraySize++;
            serializedObject.ApplyModifiedProperties();
            RebuildPreloadsList();
        }) { text = "+" };
        addPreloadBtn.style.width = 24;
        addPreloadBtn.style.height = 20;
        addPreloadBtn.tooltip = "Add a preload TextAsset";
        preloadsHeader.Add(addPreloadBtn);
        container.Add(preloadsHeader);

        _preloadsListContainer = new VisualElement();
        container.Add(_preloadsListContainer);
        RebuildPreloadsList();

        AddSpacer(container);

        // Globals section
        var globalsHeader = CreateRow();
        globalsHeader.style.marginBottom = 4;
        var globalsLabel = new Label("Globals");
        globalsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        globalsLabel.style.flexGrow = 1;
        globalsLabel.tooltip = "Key-value pairs injected as globalThis[key]";
        globalsHeader.Add(globalsLabel);

        var addGlobalBtn = new Button(() => {
            var prop = serializedObject.FindProperty("_globals");
            prop.arraySize++;
            serializedObject.ApplyModifiedProperties();
            RebuildGlobalsList();
        }) { text = "+" };
        addGlobalBtn.style.width = 24;
        addGlobalBtn.style.height = 20;
        addGlobalBtn.tooltip = "Add a global variable";
        globalsHeader.Add(addGlobalBtn);
        container.Add(globalsHeader);

        _globalsListContainer = new VisualElement();
        container.Add(_globalsListContainer);
        RebuildGlobalsList();
    }

    void BuildUITab(VisualElement container) {
        var panelSettingsProp = serializedObject.FindProperty("_panelSettings");
        var panelSettingsField = new PropertyField(panelSettingsProp, "Panel Settings Asset");
        container.Add(panelSettingsField);

        // Inline settings container
        var inlineSettings = new VisualElement();
        inlineSettings.style.marginLeft = 15;
        inlineSettings.style.marginTop = 4;
        inlineSettings.style.paddingLeft = 10;
        inlineSettings.style.borderLeftWidth = 2;
        inlineSettings.style.borderLeftColor = new Color(0.3f, 0.3f, 0.3f);

        inlineSettings.Add(new PropertyField(serializedObject.FindProperty("_defaultThemeStylesheet"), "Theme Stylesheet"));

        var scaleModeProp = serializedObject.FindProperty("_scaleMode");
        var scaleModeField = new PropertyField(scaleModeProp, "Scale Mode");
        inlineSettings.Add(scaleModeField);

        // Scale mode specific containers
        var constantPixelContainer = new VisualElement();
        constantPixelContainer.Add(new PropertyField(serializedObject.FindProperty("_scale"), "Scale"));
        inlineSettings.Add(constantPixelContainer);

        var constantPhysicalContainer = new VisualElement();
        constantPhysicalContainer.Add(new PropertyField(serializedObject.FindProperty("_referenceDpi"), "Reference DPI"));
        constantPhysicalContainer.Add(new PropertyField(serializedObject.FindProperty("_fallbackDpi"), "Fallback DPI"));
        inlineSettings.Add(constantPhysicalContainer);

        var scaleWithScreenContainer = new VisualElement();
        scaleWithScreenContainer.Add(new PropertyField(serializedObject.FindProperty("_referenceResolution"), "Reference Resolution"));
        var screenMatchModeField = new PropertyField(serializedObject.FindProperty("_screenMatchMode"), "Screen Match Mode");
        scaleWithScreenContainer.Add(screenMatchModeField);
        var matchField = new PropertyField(serializedObject.FindProperty("_match"), "Match");
        scaleWithScreenContainer.Add(matchField);
        inlineSettings.Add(scaleWithScreenContainer);

        inlineSettings.Add(new PropertyField(serializedObject.FindProperty("_sortOrder"), "Sort Order"));
        container.Add(inlineSettings);

        // Visibility logic
        void UpdateScaleModeVisibility() {
            var mode = (PanelScaleMode)scaleModeProp.enumValueIndex;
            constantPixelContainer.style.display = mode == PanelScaleMode.ConstantPixelSize ? DisplayStyle.Flex : DisplayStyle.None;
            constantPhysicalContainer.style.display = mode == PanelScaleMode.ConstantPhysicalSize ? DisplayStyle.Flex : DisplayStyle.None;
            scaleWithScreenContainer.style.display = mode == PanelScaleMode.ScaleWithScreenSize ? DisplayStyle.Flex : DisplayStyle.None;

            if (mode == PanelScaleMode.ScaleWithScreenSize) {
                var matchMode = (PanelScreenMatchMode)serializedObject.FindProperty("_screenMatchMode").enumValueIndex;
                matchField.style.display = matchMode == PanelScreenMatchMode.MatchWidthOrHeight ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        void UpdateInlineVisibility() {
            bool hasPanelSettings = panelSettingsProp.objectReferenceValue != null;
            inlineSettings.style.display = hasPanelSettings ? DisplayStyle.None : DisplayStyle.Flex;
            if (!hasPanelSettings) UpdateScaleModeVisibility();
        }

        UpdateInlineVisibility();

        panelSettingsField.RegisterValueChangeCallback(_ => { serializedObject.Update(); UpdateInlineVisibility(); });
        scaleModeField.RegisterValueChangeCallback(_ => { serializedObject.Update(); UpdateScaleModeVisibility(); });
        screenMatchModeField.RegisterValueChangeCallback(_ => {
            serializedObject.Update();
            var matchMode = (PanelScreenMatchMode)serializedObject.FindProperty("_screenMatchMode").enumValueIndex;
            matchField.style.display = matchMode == PanelScreenMatchMode.MatchWidthOrHeight ? DisplayStyle.Flex : DisplayStyle.None;
        });

        // Help box for inline settings
        var helpBox = new HelpBox(
            "No Panel Settings asset assigned. Settings above will be used to create one at runtime.",
            HelpBoxMessageType.Info
        );
        helpBox.style.marginTop = 4;
        helpBox.style.display = panelSettingsProp.objectReferenceValue == null ? DisplayStyle.Flex : DisplayStyle.None;
        panelSettingsField.RegisterValueChangeCallback(_ =>
            helpBox.style.display = panelSettingsProp.objectReferenceValue == null ? DisplayStyle.Flex : DisplayStyle.None);
        container.Add(helpBox);

        AddSpacer(container);

        // Stylesheets section
        var stylesheetsHeader = CreateRow();
        stylesheetsHeader.style.marginBottom = 4;
        var stylesheetsLabel = new Label("Stylesheets");
        stylesheetsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        stylesheetsLabel.style.flexGrow = 1;
        stylesheetsLabel.tooltip = "Global USS stylesheets applied on init/reload";
        stylesheetsHeader.Add(stylesheetsLabel);

        var addStylesheetBtn = new Button(() => {
            var prop = serializedObject.FindProperty("_stylesheets");
            prop.arraySize++;
            serializedObject.ApplyModifiedProperties();
            RebuildStylesheetsList();
        }) { text = "+" };
        addStylesheetBtn.style.width = 24;
        addStylesheetBtn.style.height = 20;
        addStylesheetBtn.tooltip = "Add a USS stylesheet";
        stylesheetsHeader.Add(addStylesheetBtn);
        container.Add(stylesheetsHeader);

        _stylesheetsListContainer = new VisualElement();
        container.Add(_stylesheetsListContainer);
        RebuildStylesheetsList();
    }

    void BuildBuildTab(VisualElement container) {
        // Build Settings
        AddSectionHeader(container, "Build Output");

        // Show current bundle asset status
        var bundleAsset = _target.BundleAsset;
        var sourceMapAsset = _target.SourceMapAsset;

        var statusBox = CreateStyledBox();
        statusBox.style.marginBottom = 8;

        var bundleRow = CreateRow();
        bundleRow.Add(CreateLabel("Bundle:", 80));
        var bundleStatus = new Label(bundleAsset != null ? $"✓ {bundleAsset.name}" : "Not generated");
        bundleStatus.style.color = bundleAsset != null ? new Color(0.4f, 0.8f, 0.4f) : new Color(0.6f, 0.6f, 0.6f);
        bundleRow.Add(bundleStatus);
        statusBox.Add(bundleRow);

        var sourceMapRow = CreateRow();
        sourceMapRow.Add(CreateLabel("Source Map:", 80));
        var sourceMapStatus = new Label(sourceMapAsset != null ? $"✓ {sourceMapAsset.name}" : "Not generated");
        sourceMapStatus.style.color = sourceMapAsset != null ? new Color(0.4f, 0.8f, 0.4f) : new Color(0.6f, 0.6f, 0.6f);
        sourceMapRow.Add(sourceMapStatus);
        statusBox.Add(sourceMapRow);

        container.Add(statusBox);

        // Source map option
        container.Add(new PropertyField(serializedObject.FindProperty("_includeSourceMap"), "Include Source Map"));

        var buildHelpBox = new HelpBox(
            "Bundle TextAsset is auto-generated during Unity build.\n" +
            "Path: " + (_target.BundleAssetPath ?? "(save scene first)"),
            HelpBoxMessageType.Info
        );
        buildHelpBox.style.marginTop = 4;
        container.Add(buildHelpBox);

        AddSpacer(container);

        // Type Generation
        AddSectionHeader(container, "Type Generation");

        var assembliesField = new PropertyField(serializedObject.FindProperty("_typingAssemblies"));
        assembliesField.label = "Assemblies";
        assembliesField.tooltip = "C# assemblies to generate TypeScript typings for";
        container.Add(assembliesField);

        var autoGenerateProp = serializedObject.FindProperty("_autoGenerateTypings");
        var autoGenerateField = new PropertyField(autoGenerateProp, "Auto Generate");
        container.Add(autoGenerateField);

        var outputPathField = new PropertyField(serializedObject.FindProperty("_typingsOutputPath"), "Output Path");
        container.Add(outputPathField);

        var typeGenRow = CreateRow();
        typeGenRow.style.marginTop = 5;
        _generateTypesButton = new Button(GenerateTypings) { text = "Generate Types Now" };
        _generateTypesButton.style.height = 24;
        _generateTypesButton.style.flexGrow = 1;
        typeGenRow.Add(_generateTypesButton);
        container.Add(typeGenRow);

        _typeGenStatusLabel = new Label();
        _typeGenStatusLabel.style.marginTop = 4;
        _typeGenStatusLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
        _typeGenStatusLabel.style.fontSize = 10;
        container.Add(_typeGenStatusLabel);
        UpdateTypeGenStatus();

        AddSpacer(container);

        // Scaffolding
        AddSectionHeader(container, "Scaffolding");

        var defaultFilesField = new PropertyField(serializedObject.FindProperty("_defaultFiles"), "Default Files");
        defaultFilesField.tooltip = "Template files scaffolded when working directory is empty";
        container.Add(defaultFilesField);

        var scaffoldRow = CreateRow();
        scaffoldRow.style.marginTop = 5;
        var repopulateButton = new Button(() => {
            _target.PopulateDefaultFiles();
            serializedObject.Update();
            EditorUtility.SetDirty(_target);
        }) { text = "Reset to Defaults" };
        repopulateButton.style.height = 22;
        repopulateButton.style.flexGrow = 1;
        repopulateButton.tooltip = "Repopulate the default files list from OneJS templates";
        scaffoldRow.Add(repopulateButton);
        container.Add(scaffoldRow);
    }

    void BuildCartridgesTab(VisualElement container) {
        // Header with Add button
        var headerRow = CreateRow();
        headerRow.style.marginBottom = 4;

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

        container.Add(headerRow);

        // Cartridge list container
        _cartridgeListContainer = new VisualElement();
        container.Add(_cartridgeListContainer);

        // Initial build
        RebuildCartridgeList();

        // Help box
        var helpBox = new HelpBox(
            "Files are extracted to @cartridges/{slug}/. Objects are injected as __cartridges.{slug}.{key} at runtime.\n" +
            "E = Extract (overwrites existing), D = Delete extracted folder, X = Remove from list",
            HelpBoxMessageType.Info
        );
        helpBox.style.marginTop = 4;
        container.Add(helpBox);

        // Extract All / Delete All buttons
        var bulkRow = CreateRow();
        bulkRow.style.marginTop = 8;

        var extractAllBtn = new Button(() => ExtractAllCartridges()) { text = "Extract All" };
        extractAllBtn.style.flexGrow = 1;
        extractAllBtn.style.height = 22;
        extractAllBtn.tooltip = "Extract all cartridges (with confirmation)";
        bulkRow.Add(extractAllBtn);

        var deleteAllBtn = new Button(() => DeleteAllCartridges()) { text = "Delete All Extracted" };
        deleteAllBtn.style.flexGrow = 1;
        deleteAllBtn.style.height = 22;
        deleteAllBtn.tooltip = "Delete all extracted cartridge folders (with confirmation)";
        bulkRow.Add(deleteAllBtn);

        container.Add(bulkRow);
    }

    // MARK: Section Helpers

    void AddSectionHeader(VisualElement container, string title) {
        var header = new Label(title);
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.marginTop = 4;
        header.style.marginBottom = 4;
        header.style.fontSize = 12;
        container.Add(header);
    }

    void AddSpacer(VisualElement container, float height = 12) {
        var spacer = new VisualElement();
        spacer.style.height = height;
        container.Add(spacer);
    }

    // MARK: List Management (Stylesheets, Preloads, Globals)

    void RebuildStylesheetsList() {
        if (_stylesheetsListContainer == null) return;

        _stylesheetsListContainer.Clear();
        serializedObject.Update();

        var prop = serializedObject.FindProperty("_stylesheets");

        if (prop.arraySize == 0) {
            var emptyLabel = new Label("No stylesheets. Click + to add one.");
            emptyLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            emptyLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            emptyLabel.style.paddingTop = 4;
            emptyLabel.style.paddingBottom = 4;
            _stylesheetsListContainer.Add(emptyLabel);
            return;
        }

        for (int i = 0; i < prop.arraySize; i++) {
            var row = CreateStylesheetItemRow(prop, i);
            _stylesheetsListContainer.Add(row);
        }
    }

    VisualElement CreateStylesheetItemRow(SerializedProperty arrayProp, int index) {
        var elementProp = arrayProp.GetArrayElementAtIndex(index);

        var row = CreateRow();
        row.style.marginBottom = 2;

        var indexLabel = new Label($"{index}");
        indexLabel.style.width = 16;
        indexLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
        row.Add(indexLabel);

        var objectField = new ObjectField();
        objectField.objectType = typeof(StyleSheet);
        objectField.value = elementProp.objectReferenceValue as StyleSheet;
        objectField.style.flexGrow = 1;
        objectField.style.marginLeft = 4;
        objectField.RegisterValueChangedCallback(evt => {
            elementProp.objectReferenceValue = evt.newValue;
            serializedObject.ApplyModifiedProperties();
        });
        row.Add(objectField);

        var removeBtn = new Button(() => {
            arrayProp.GetArrayElementAtIndex(index).objectReferenceValue = null;
            arrayProp.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            RebuildStylesheetsList();
        }) { text = "X" };
        removeBtn.style.width = 24;
        removeBtn.style.height = 20;
        removeBtn.style.marginLeft = 4;
        removeBtn.tooltip = "Remove this stylesheet";
        row.Add(removeBtn);

        return row;
    }

    void RebuildPreloadsList() {
        if (_preloadsListContainer == null) return;

        _preloadsListContainer.Clear();
        serializedObject.Update();

        var prop = serializedObject.FindProperty("_preloads");

        if (prop.arraySize == 0) {
            var emptyLabel = new Label("No preloads. Click + to add one.");
            emptyLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            emptyLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            emptyLabel.style.paddingTop = 4;
            emptyLabel.style.paddingBottom = 4;
            _preloadsListContainer.Add(emptyLabel);
            return;
        }

        for (int i = 0; i < prop.arraySize; i++) {
            var row = CreatePreloadItemRow(prop, i);
            _preloadsListContainer.Add(row);
        }
    }

    VisualElement CreatePreloadItemRow(SerializedProperty arrayProp, int index) {
        var elementProp = arrayProp.GetArrayElementAtIndex(index);

        var row = CreateRow();
        row.style.marginBottom = 2;

        var indexLabel = new Label($"{index}");
        indexLabel.style.width = 16;
        indexLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
        row.Add(indexLabel);

        var objectField = new ObjectField();
        objectField.objectType = typeof(TextAsset);
        objectField.value = elementProp.objectReferenceValue as TextAsset;
        objectField.style.flexGrow = 1;
        objectField.style.marginLeft = 4;
        objectField.RegisterValueChangedCallback(evt => {
            elementProp.objectReferenceValue = evt.newValue;
            serializedObject.ApplyModifiedProperties();
        });
        row.Add(objectField);

        var removeBtn = new Button(() => {
            arrayProp.GetArrayElementAtIndex(index).objectReferenceValue = null;
            arrayProp.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            RebuildPreloadsList();
        }) { text = "X" };
        removeBtn.style.width = 24;
        removeBtn.style.height = 20;
        removeBtn.style.marginLeft = 4;
        removeBtn.tooltip = "Remove this preload";
        row.Add(removeBtn);

        return row;
    }

    void RebuildGlobalsList() {
        if (_globalsListContainer == null) return;

        _globalsListContainer.Clear();
        serializedObject.Update();

        var prop = serializedObject.FindProperty("_globals");

        if (prop.arraySize == 0) {
            var emptyLabel = new Label("No globals. Click + to add one.");
            emptyLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            emptyLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            emptyLabel.style.paddingTop = 4;
            emptyLabel.style.paddingBottom = 4;
            _globalsListContainer.Add(emptyLabel);
            return;
        }

        for (int i = 0; i < prop.arraySize; i++) {
            var row = CreateGlobalItemRow(prop, i);
            _globalsListContainer.Add(row);
        }
    }

    VisualElement CreateGlobalItemRow(SerializedProperty arrayProp, int index) {
        var elementProp = arrayProp.GetArrayElementAtIndex(index);
        var keyProp = elementProp.FindPropertyRelative("key");
        var valueProp = elementProp.FindPropertyRelative("value");

        var row = CreateRow();
        row.style.marginBottom = 2;

        var indexLabel = new Label($"{index}");
        indexLabel.style.width = 16;
        indexLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
        row.Add(indexLabel);

        var keyField = new TextField();
        keyField.value = keyProp.stringValue;
        keyField.style.width = 130;
        keyField.style.marginLeft = 4;
        keyField.RegisterValueChangedCallback(evt => {
            keyProp.stringValue = evt.newValue;
            serializedObject.ApplyModifiedProperties();
        });
        row.Add(keyField);

        var arrow = new Label("→");
        arrow.style.marginLeft = 4;
        arrow.style.marginRight = 4;
        arrow.style.color = new Color(0.5f, 0.5f, 0.5f);
        row.Add(arrow);

        var valueField = new ObjectField();
        valueField.objectType = typeof(UnityEngine.Object);
        valueField.value = valueProp.objectReferenceValue;
        valueField.style.flexGrow = 1;
        valueField.RegisterValueChangedCallback(evt => {
            valueProp.objectReferenceValue = evt.newValue;
            serializedObject.ApplyModifiedProperties();
        });
        row.Add(valueField);

        var removeBtn = new Button(() => {
            arrayProp.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            RebuildGlobalsList();
        }) { text = "X" };
        removeBtn.style.width = 24;
        removeBtn.style.height = 20;
        removeBtn.style.marginLeft = 4;
        removeBtn.tooltip = "Remove this global";
        row.Add(removeBtn);

        return row;
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
        row.style.SetBorderRadius(3);

        // Index label
        var indexLabel = new Label($"{index}");
        indexLabel.style.width = 16;
        indexLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
        indexLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        row.Add(indexLabel);

        // Object field
        var objectField = new ObjectField();
        objectField.objectType = typeof(UICartridge);
        objectField.value = cartridge;
        objectField.style.flexGrow = 1;
        objectField.style.marginLeft = 4;
        objectField.style.marginRight = 4;
        objectField.RegisterValueChangedCallback(evt => {
            elementProp.objectReferenceValue = evt.newValue;
            serializedObject.ApplyModifiedProperties();
            RebuildCartridgeList();
        });
        row.Add(objectField);

        // Status indicator
        var statusLabel = new Label();
        statusLabel.style.width = 70;
        statusLabel.style.fontSize = 10;
        statusLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        statusLabel.style.marginRight = 4;

        if (cartridge != null && !string.IsNullOrEmpty(cartridge.Slug) && _target.IsSceneSaved) {
            var cartridgePath = _target.GetCartridgePath(cartridge);
            bool isExtracted = !string.IsNullOrEmpty(cartridgePath) && Directory.Exists(cartridgePath);

            if (isExtracted) {
                statusLabel.text = "Extracted";
                statusLabel.style.color = new Color(0.4f, 0.8f, 0.4f);
            } else {
                statusLabel.text = "Not extracted";
                statusLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            }
        } else if (!_target.IsSceneSaved) {
            statusLabel.text = "Save scene";
            statusLabel.style.color = new Color(0.8f, 0.6f, 0.2f);
        } else {
            statusLabel.text = cartridge == null ? "" : "No slug";
            statusLabel.style.color = new Color(0.8f, 0.6f, 0.2f);
        }
        row.Add(statusLabel);

        // Extract button
        var extractBtn = new Button(() => ExtractCartridge(cartridge, index)) { text = "E" };
        extractBtn.style.width = 24;
        extractBtn.style.height = 20;
        extractBtn.tooltip = "Extract cartridge files to @cartridges/" + (cartridge?.Slug ?? "");
        extractBtn.SetEnabled(cartridge != null && !string.IsNullOrEmpty(cartridge?.Slug) && _target.IsSceneSaved);
        row.Add(extractBtn);

        // Delete button
        var deleteBtn = new Button(() => DeleteCartridge(cartridge, index)) { text = "D" };
        deleteBtn.style.width = 24;
        deleteBtn.style.height = 20;
        deleteBtn.style.marginLeft = 2;
        deleteBtn.tooltip = "Delete extracted cartridge folder";
        var cartPath = _target.IsSceneSaved ? _target.GetCartridgePath(cartridge) : null;
        bool canDelete = cartridge != null && !string.IsNullOrEmpty(cartridge?.Slug) &&
                         !string.IsNullOrEmpty(cartPath) && Directory.Exists(cartPath);
        deleteBtn.SetEnabled(canDelete);
        row.Add(deleteBtn);

        // Remove from list button
        var removeBtn = new Button(() => RemoveCartridgeFromList(index)) { text = "X" };
        removeBtn.style.width = 24;
        removeBtn.style.height = 20;
        removeBtn.style.marginLeft = 2;
        removeBtn.style.backgroundColor = new Color(0.5f, 0.2f, 0.2f);
        removeBtn.tooltip = "Remove from list (does not delete extracted files)";
        row.Add(removeBtn);

        return row;
    }

    void ExtractCartridge(UICartridge cartridge, int index) {
        if (cartridge == null || string.IsNullOrEmpty(cartridge.Slug)) return;

        var destPath = _target.GetCartridgePath(cartridge);
        bool alreadyExists = Directory.Exists(destPath);

        string message = alreadyExists
            ? $"Cartridge '{cartridge.DisplayName}' is already extracted at:\n\n{destPath}\n\nOverwrite existing files?"
            : $"Extract cartridge '{cartridge.DisplayName}' to:\n\n{destPath}?";

        string title = alreadyExists ? "Overwrite Cartridge?" : "Extract Cartridge?";

        if (!EditorUtility.DisplayDialog(title, message, alreadyExists ? "Overwrite" : "Extract", "Cancel")) {
            return;
        }

        try {
            if (alreadyExists) {
                Directory.Delete(destPath, true);
            }

            Directory.CreateDirectory(destPath);

            int fileCount = 0;
            foreach (var file in cartridge.Files) {
                if (string.IsNullOrEmpty(file.path) || file.content == null) continue;

                var filePath = Path.Combine(destPath, file.path);
                var fileDir = Path.GetDirectoryName(filePath);

                if (!string.IsNullOrEmpty(fileDir) && !Directory.Exists(fileDir)) {
                    Directory.CreateDirectory(fileDir);
                }

                File.WriteAllText(filePath, file.content.text);
                fileCount++;
            }

            var dts = OneJS.CartridgeTypeGenerator.Generate(cartridge);
            File.WriteAllText(Path.Combine(destPath, $"{cartridge.Slug}.d.ts"), dts);

            Debug.Log($"[JSRunner] Extracted cartridge '{cartridge.DisplayName}' ({fileCount} files + .d.ts) to: {destPath}");
            AssetDatabase.Refresh();
            RebuildCartridgeList();

        } catch (Exception ex) {
            Debug.LogError($"[JSRunner] Failed to extract cartridge '{cartridge.DisplayName}': {ex.Message}");
            EditorUtility.DisplayDialog("Extract Failed", $"Failed to extract cartridge:\n\n{ex.Message}", "OK");
        }
    }

    void DeleteCartridge(UICartridge cartridge, int index) {
        if (cartridge == null || string.IsNullOrEmpty(cartridge.Slug)) return;

        var destPath = _target.GetCartridgePath(cartridge);
        if (!Directory.Exists(destPath)) {
            EditorUtility.DisplayDialog("Nothing to Delete", $"Cartridge folder does not exist:\n\n{destPath}", "OK");
            return;
        }

        int fileCount = 0;
        try {
            fileCount = Directory.GetFiles(destPath, "*", SearchOption.AllDirectories).Length;
        } catch { }

        if (!EditorUtility.DisplayDialog(
            "Delete Extracted Cartridge?",
            $"Delete the extracted cartridge folder for '{cartridge.DisplayName}'?\n\n" +
            $"Path: {destPath}\n" +
            $"Files: {fileCount}\n\n" +
            "This cannot be undone.",
            "Delete", "Cancel")) {
            return;
        }

        try {
            Directory.Delete(destPath, true);
            Debug.Log($"[JSRunner] Deleted cartridge folder: {destPath}");
            AssetDatabase.Refresh();
            RebuildCartridgeList();

        } catch (Exception ex) {
            Debug.LogError($"[JSRunner] Failed to delete cartridge folder: {ex.Message}");
            EditorUtility.DisplayDialog("Delete Failed", $"Failed to delete cartridge folder:\n\n{ex.Message}", "OK");
        }
    }

    void RemoveCartridgeFromList(int index) {
        var cartridgesProp = serializedObject.FindProperty("_cartridges");
        if (index < 0 || index >= cartridgesProp.arraySize) return;

        var cartridge = cartridgesProp.GetArrayElementAtIndex(index).objectReferenceValue as UICartridge;
        string name = cartridge?.DisplayName ?? $"Item {index}";

        if (!EditorUtility.DisplayDialog(
            "Remove from List?",
            $"Remove '{name}' from the cartridge list?\n\n" +
            "(This does not delete any extracted files)",
            "Remove", "Cancel")) {
            return;
        }

        cartridgesProp.GetArrayElementAtIndex(index).objectReferenceValue = null;
        cartridgesProp.DeleteArrayElementAtIndex(index);
        serializedObject.ApplyModifiedProperties();
        RebuildCartridgeList();
    }

    void ExtractAllCartridges() {
        if (!_target.IsSceneSaved) {
            EditorUtility.DisplayDialog("Scene Not Saved", "Save the scene before extracting cartridges.", "OK");
            return;
        }

        var cartridges = _target.Cartridges;
        if (cartridges == null || cartridges.Count == 0) {
            EditorUtility.DisplayDialog("No Cartridges", "No cartridges to extract.", "OK");
            return;
        }

        int validCount = 0;
        int existingCount = 0;
        foreach (var c in cartridges) {
            if (c != null && !string.IsNullOrEmpty(c.Slug)) {
                validCount++;
                var path = _target.GetCartridgePath(c);
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) existingCount++;
            }
        }

        if (validCount == 0) {
            EditorUtility.DisplayDialog("No Valid Cartridges", "No cartridges with valid slugs to extract.", "OK");
            return;
        }

        string message = existingCount > 0
            ? $"Extract {validCount} cartridge(s)?\n\n{existingCount} already exist and will be overwritten."
            : $"Extract {validCount} cartridge(s)?";

        if (!EditorUtility.DisplayDialog("Extract All Cartridges?", message, "Extract All", "Cancel")) {
            return;
        }

        int extracted = 0;
        foreach (var cartridge in cartridges) {
            if (cartridge == null || string.IsNullOrEmpty(cartridge.Slug)) continue;

            try {
                var destPath = _target.GetCartridgePath(cartridge);

                if (Directory.Exists(destPath)) {
                    Directory.Delete(destPath, true);
                }

                Directory.CreateDirectory(destPath);

                foreach (var file in cartridge.Files) {
                    if (string.IsNullOrEmpty(file.path) || file.content == null) continue;

                    var filePath = Path.Combine(destPath, file.path);
                    var fileDir = Path.GetDirectoryName(filePath);

                    if (!string.IsNullOrEmpty(fileDir) && !Directory.Exists(fileDir)) {
                        Directory.CreateDirectory(fileDir);
                    }

                    File.WriteAllText(filePath, file.content.text);
                }

                var dts = OneJS.CartridgeTypeGenerator.Generate(cartridge);
                File.WriteAllText(Path.Combine(destPath, $"{cartridge.Slug}.d.ts"), dts);

                extracted++;
            } catch (Exception ex) {
                Debug.LogError($"[JSRunner] Failed to extract '{cartridge.DisplayName}': {ex.Message}");
            }
        }

        Debug.Log($"[JSRunner] Extracted {extracted} cartridge(s)");
        AssetDatabase.Refresh();
        RebuildCartridgeList();
    }

    void DeleteAllCartridges() {
        if (!_target.IsSceneSaved) {
            EditorUtility.DisplayDialog("Scene Not Saved", "Save the scene first.", "OK");
            return;
        }

        var cartridges = _target.Cartridges;
        if (cartridges == null || cartridges.Count == 0) {
            EditorUtility.DisplayDialog("No Cartridges", "No cartridges in list.", "OK");
            return;
        }

        int existingCount = 0;
        foreach (var c in cartridges) {
            if (c != null && !string.IsNullOrEmpty(c.Slug)) {
                var path = _target.GetCartridgePath(c);
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) {
                    existingCount++;
                }
            }
        }

        if (existingCount == 0) {
            EditorUtility.DisplayDialog("Nothing to Delete", "No extracted cartridge folders found.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog(
            "Delete All Extracted Cartridges?",
            $"Delete {existingCount} extracted cartridge folder(s)?\n\nThis cannot be undone.",
            "Delete All", "Cancel")) {
            return;
        }

        int deleted = 0;
        foreach (var cartridge in cartridges) {
            if (cartridge == null || string.IsNullOrEmpty(cartridge.Slug)) continue;

            var destPath = _target.GetCartridgePath(cartridge);
            if (!Directory.Exists(destPath)) continue;

            try {
                Directory.Delete(destPath, true);
                deleted++;
            } catch (Exception ex) {
                Debug.LogError($"[JSRunner] Failed to delete '{cartridge.DisplayName}' folder: {ex.Message}");
            }
        }

        Debug.Log($"[JSRunner] Deleted {deleted} cartridge folder(s)");
        AssetDatabase.Refresh();
        RebuildCartridgeList();
    }

    // MARK: Status Section

    VisualElement CreateStatusSection() {
        var container = CreateStyledBox();
        container.style.marginTop = 2;
        container.style.marginBottom = 6;

        // Status row
        var statusRow = CreateRow();
        statusRow.Add(CreateLabel("Status", 50, true));
        _statusLabel = new Label("Stopped (Enter Play Mode to run)");
        statusRow.Add(_statusLabel);
        container.Add(statusRow);

        // Live reload info
        _liveReloadInfo = new VisualElement { style = { display = DisplayStyle.None, marginTop = 4 } };
        _liveReloadStatusLabel = new Label();
        _liveReloadInfo.Add(_liveReloadStatusLabel);
        _reloadCountLabel = new Label { style = { display = DisplayStyle.None } };
        _liveReloadInfo.Add(_reloadCountLabel);
        container.Add(_liveReloadInfo);

        // Working directory path
        _workingDirLabel = new Label();
        _workingDirLabel.style.marginTop = 4;
        _workingDirLabel.style.fontSize = 10;
        _workingDirLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
        _workingDirLabel.style.whiteSpace = WhiteSpace.Normal;
        container.Add(_workingDirLabel);

        return container;
    }

    // MARK: Actions Section

    VisualElement CreateActionsSection() {
        var container = new VisualElement { style = { marginTop = 12, marginBottom = 10 } };

        // Divider
        var divider = new VisualElement();
        divider.style.height = 1;
        divider.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
        divider.style.marginBottom = 12;
        container.Add(divider);

        // Row 1: Reload and Build
        var row1 = CreateRow();
        row1.style.marginBottom = 4;

        _reloadButton = new Button(() => _target.ForceReload()) { text = "Refresh" };
        _reloadButton.style.height = 30;
        _reloadButton.style.flexGrow = 1;
        _reloadButton.tooltip = "Refresh the JavaScript runtime (Play Mode only)";
        _reloadButton.SetEnabled(false);
        row1.Add(_reloadButton);

        _buildButton = new Button(RunRebuild) { text = "Rebuild" };
        _buildButton.style.height = 30;
        _buildButton.style.flexGrow = 1;
        _buildButton.tooltip = "Delete node_modules and reinstall dependencies, then build";
        row1.Add(_buildButton);

        container.Add(row1);

        // Watcher status (auto-starts on Play mode)
        _watcherStatusLabel = new Label();
        _watcherStatusLabel.style.marginTop = 4;
        _watcherStatusLabel.style.marginBottom = 4;
        _watcherStatusLabel.style.fontSize = 11;
        container.Add(_watcherStatusLabel);

        // Row 2: Open Folder, Terminal, and VSCode
        var row2 = CreateRow();

        var openFolderButton = new Button(OpenWorkingDirectory) { text = "Open Folder" };
        openFolderButton.style.height = 24;
        openFolderButton.style.flexGrow = 1;
        openFolderButton.tooltip = "Open working directory in file explorer";
        row2.Add(openFolderButton);

        var openTerminalButton = new Button(OpenTerminal) { text = "Open Terminal" };
        openTerminalButton.style.height = 24;
        openTerminalButton.style.flexGrow = 1;
        openTerminalButton.tooltip = "Open terminal at working directory";
        row2.Add(openTerminalButton);

        var openVSCodeButton = new Button(OpenVSCode) { text = "Open VSCode" };
        openVSCodeButton.style.height = 24;
        openVSCodeButton.style.flexGrow = 1;
        openVSCodeButton.tooltip = "Open working directory in Visual Studio Code";
        row2.Add(openVSCodeButton);

        container.Add(row2);

        _buildOutputBox = new HelpBox("", HelpBoxMessageType.Info);
        _buildOutputBox.style.marginTop = 5;
        _buildOutputBox.style.display = DisplayStyle.None;
        container.Add(_buildOutputBox);

        return container;
    }

    // MARK: UI Helpers

    VisualElement CreateStyledBox() {
        var box = new VisualElement();
        box.style.backgroundColor = new Color(0.24f, 0.24f, 0.24f);
        box.style.SetBorderWidth(1);
        box.style.SetBorderColor(new Color(0.14f, 0.14f, 0.14f));
        box.style.SetBorderRadius(3);
        box.style.paddingTop = box.style.paddingBottom = 8;
        box.style.paddingLeft = box.style.paddingRight = 10;
        return box;
    }

    static VisualElement CreateRow() {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        return row;
    }

    static Label CreateLabel(string text, int width, bool bold = false) {
        var label = new Label(text);
        label.style.width = width;
        if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
        return label;
    }

    // MARK: Type Generation

    void GenerateTypings() {
        if (TypeGeneratorService.GenerateTypingsFor(_target, silent: false)) {
            _typeGenStatusLabel.text = $"Generated at {DateTime.Now:HH:mm:ss}";
            _typeGenStatusLabel.style.color = new Color(0.2f, 0.8f, 0.2f);
        } else {
            _typeGenStatusLabel.text = "Generation failed - check console";
            _typeGenStatusLabel.style.color = new Color(0.8f, 0.4f, 0.4f);
        }
    }

    void UpdateTypeGenStatus() {
        if (_typeGenStatusLabel == null) return;

        var outputPath = _target.TypingsFullPath;
        if (string.IsNullOrEmpty(outputPath)) {
            _typeGenStatusLabel.text = "Output: (save scene first)";
        } else if (File.Exists(outputPath)) {
            var lastWrite = File.GetLastWriteTime(outputPath);
            _typeGenStatusLabel.text = $"Output: {_target.TypingsOutputPath} (last updated: {lastWrite:g})";
        } else {
            _typeGenStatusLabel.text = $"Output: {_target.TypingsOutputPath} (not yet generated)";
        }
    }

    // MARK: Dynamic UI Updates

    void UpdateDynamicUI() {
        if (_statusLabel == null) return;

        // Status
        if (Application.isPlaying) {
            _statusLabel.text = _target.IsRunning ? "Running" : "Loading...";
            _statusLabel.style.color = _target.IsRunning ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.8f, 0.6f, 0.2f);
        } else {
            _statusLabel.text = "Stopped (Enter Play Mode to run)";
            _statusLabel.style.color = Color.white;
        }

        // Live reload info
        if (Application.isPlaying && _target.IsRunning) {
            _liveReloadInfo.style.display = DisplayStyle.Flex;
            var pollInterval = serializedObject.FindProperty("_pollInterval").floatValue;
            _liveReloadStatusLabel.text = _target.IsLiveReloadEnabled
                ? $"Live Reload: Enabled (polling every {pollInterval}s)"
                : "Live Reload: Disabled";

            _reloadCountLabel.style.display = _target.ReloadCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _reloadCountLabel.text = $"Reloads: {_target.ReloadCount}";
        } else {
            _liveReloadInfo.style.display = DisplayStyle.None;
        }

        // Working directory path
        if (_workingDirLabel != null) {
            var workingDir = _target.WorkingDirFullPath;
            _workingDirLabel.text = string.IsNullOrEmpty(workingDir) ? "(save scene first)" : workingDir;
        }

        // Buttons
        _reloadButton?.SetEnabled(Application.isPlaying && _target.IsRunning);
        if (_buildButton != null) {
            _buildButton.SetEnabled(!_buildInProgress);
            _buildButton.text = _buildInProgress ? "Rebuilding..." : "Rebuild";
        }

        // Watcher status
        if (_watcherStatusLabel != null) {
            var workingDir = _target.WorkingDirFullPath;
            if (string.IsNullOrEmpty(workingDir)) {
                _watcherStatusLabel.text = "Watcher: (save scene first)";
                _watcherStatusLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            } else {
                var isWatching = NodeWatcherManager.IsRunning(workingDir);
                var isStarting = NodeWatcherManager.IsStarting(workingDir);

                if (isStarting) {
                    _watcherStatusLabel.text = "Watcher: Starting...";
                    _watcherStatusLabel.style.color = new Color(0.8f, 0.6f, 0.2f);
                } else if (isWatching) {
                    _watcherStatusLabel.text = "Watcher: Running (auto-rebuilds on file changes)";
                    _watcherStatusLabel.style.color = new Color(0.4f, 0.8f, 0.4f);
                } else if (Application.isPlaying) {
                    _watcherStatusLabel.text = "Watcher: Starting on play mode...";
                    _watcherStatusLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
                } else {
                    _watcherStatusLabel.text = "Watcher: Auto-starts on Play mode";
                    _watcherStatusLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
                }
            }
        }

        // Build output
        if (_buildOutputBox != null && !string.IsNullOrEmpty(_buildOutput)) {
            _buildOutputBox.style.display = DisplayStyle.Flex;
            _buildOutputBox.text = _buildOutput;
        }
    }

    // MARK: Build

    void RunNpmCommand(string workingDir, string arguments, Action onSuccess, Action<int> onFailure) {
        try {
            var npmPath = GetNpmCommand();
            var nodeBinDir = Path.GetDirectoryName(npmPath);

            var startInfo = new ProcessStartInfo {
                FileName = npmPath,
                Arguments = arguments,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var existingPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            if (!string.IsNullOrEmpty(nodeBinDir)) {
                startInfo.EnvironmentVariables["PATH"] = nodeBinDir + Path.PathSeparator + existingPath;
            }

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            process.OutputDataReceived += (_, args) => {
                if (!string.IsNullOrEmpty(args.Data)) Debug.Log($"[npm] {args.Data}");
            };
            process.ErrorDataReceived += (_, args) => {
                if (!string.IsNullOrEmpty(args.Data)) Debug.Log($"[npm] {args.Data}");
            };
            process.Exited += (_, _) => {
                EditorApplication.delayCall += () => {
                    if (process.ExitCode == 0) {
                        onSuccess?.Invoke();
                    } else {
                        onFailure?.Invoke(process.ExitCode);
                    }
                };
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        } catch (Exception ex) {
            Debug.LogError($"[JSRunner] npm error: {ex.Message}");
            if (ex.Message.Contains("npm") || ex.Message.Contains("not found")) {
                Debug.LogError("[JSRunner] npm not found. Make sure Node.js is installed and in your PATH.");
            }
            onFailure?.Invoke(-1);
        }
    }

    void RunRebuild() {
        var workingDir = _target.WorkingDirFullPath;

        if (string.IsNullOrEmpty(workingDir)) {
            Debug.LogError("[JSRunner] Scene must be saved before rebuilding.");
            _buildOutput = "Scene must be saved first.";
            return;
        }

        if (!Directory.Exists(workingDir)) {
            Debug.LogError($"[JSRunner] Working directory not found: {workingDir}");
            return;
        }

        if (!File.Exists(Path.Combine(workingDir, "package.json"))) {
            Debug.LogError($"[JSRunner] package.json not found in {workingDir}. Run 'npm init' first.");
            return;
        }

        _buildInProgress = true;

        // Delete node_modules for a clean rebuild
        var nodeModulesPath = Path.Combine(workingDir, "node_modules");
        if (Directory.Exists(nodeModulesPath)) {
            _buildOutput = "Deleting node_modules...";
            try {
                Directory.Delete(nodeModulesPath, true);
                Debug.Log("[JSRunner] Deleted node_modules");
            } catch (Exception ex) {
                _buildInProgress = false;
                _buildOutput = $"Failed to delete node_modules: {ex.Message}";
                Debug.LogError($"[JSRunner] Failed to delete node_modules: {ex.Message}");
                return;
            }
        }

        // Install dependencies then build
        _buildOutput = "Installing dependencies...";
        RunNpmCommand(workingDir, "install", onSuccess: () => {
            _buildOutput = "Building...";
            RunNpmCommand(workingDir, "run build", onSuccess: () => {
                _buildInProgress = false;
                _buildOutput = "Rebuild completed successfully!";
                Debug.Log("[JSRunner] Rebuild completed successfully!");
            }, onFailure: (code) => {
                _buildInProgress = false;
                _buildOutput = $"Build failed with exit code {code}";
                Debug.LogError($"[JSRunner] Build failed with exit code {code}");
            });
        }, onFailure: (code) => {
            _buildInProgress = false;
            _buildOutput = $"Install failed with exit code {code}";
            Debug.LogError($"[JSRunner] npm install failed with exit code {code}");
        });
    }

    // MARK: npm Resolution

    string _cachedNpmPath;

    string GetNpmCommand() {
        if (!string.IsNullOrEmpty(_cachedNpmPath)) return _cachedNpmPath;

#if UNITY_EDITOR_WIN
        return _cachedNpmPath = "npm.cmd";
#else
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string[] searchPaths = {
            "/usr/local/bin/npm",
            "/opt/homebrew/bin/npm",
            "/usr/bin/npm",
            Path.Combine(home, "n/bin/npm"),
        };

        foreach (var path in searchPaths) {
            if (File.Exists(path)) return _cachedNpmPath = path;
        }

        // Check nvm
        var nvmDir = Path.Combine(home, ".nvm/versions/node");
        if (Directory.Exists(nvmDir)) {
            try {
                foreach (var nodeDir in Directory.GetDirectories(nvmDir)) {
                    var npmPath = Path.Combine(nodeDir, "bin", "npm");
                    if (File.Exists(npmPath)) return _cachedNpmPath = npmPath;
                }
            } catch { }
        }

        // Fallback: login shell
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
            if (!string.IsNullOrEmpty(result) && File.Exists(result)) return _cachedNpmPath = result;
        } catch { }

        return "npm";
#endif
    }

    // MARK: Context Menu

    [MenuItem("CONTEXT/JSRunner/Reset Working Directory")]
    static void ResetWorkingDirectory(MenuCommand command) {
        var runner = (JSRunner)command.context;
        var workingDir = runner.WorkingDirFullPath;

        if (!Directory.Exists(workingDir)) {
            EditorUtility.DisplayDialog(
                "Reset Working Directory",
                $"Working directory does not exist:\n{workingDir}\n\nNothing to reset.",
                "OK"
            );
            return;
        }

        int fileCount = 0;
        int dirCount = 0;
        try {
            fileCount = Directory.GetFiles(workingDir, "*", SearchOption.AllDirectories).Length;
            dirCount = Directory.GetDirectories(workingDir, "*", SearchOption.AllDirectories).Length;
        } catch { }

        var confirmed = EditorUtility.DisplayDialog(
            "Reset Working Directory",
            $"WARNING: This will PERMANENTLY DELETE everything in:\n\n" +
            $"{workingDir}\n\n" +
            $"This includes:\n" +
            $"  {fileCount} files\n" +
            $"  {dirCount} folders\n" +
            $"  All your source code, node_modules, and configuration\n\n" +
            $"After deletion, the directory will be re-scaffolded with default files.\n\n" +
            $"THIS CANNOT BE UNDONE!\n\n" +
            $"Are you absolutely sure?",
            "DELETE EVERYTHING",
            "Cancel"
        );

        if (!confirmed) return;

        var doubleConfirmed = EditorUtility.DisplayDialog(
            "Final Warning",
            "You are about to delete all files in the working directory.\n\n" +
            "Type 'DELETE' mentally and click confirm only if you're certain.",
            "Yes, Delete Everything",
            "Cancel"
        );

        if (!doubleConfirmed) return;

        try {
            foreach (var file in Directory.GetFiles(workingDir)) {
                File.Delete(file);
            }
            foreach (var dir in Directory.GetDirectories(workingDir)) {
                Directory.Delete(dir, true);
            }

            Debug.Log($"[JSRunner] Deleted all contents from: {workingDir}");

            var method = typeof(JSRunner).GetMethod("ScaffoldDefaultFiles",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method?.Invoke(runner, null);

            Debug.Log("[JSRunner] Re-scaffolded default files");

            AssetDatabase.Refresh();

            Debug.Log("[JSRunner] Working directory reset complete. Run 'Build' to restore dependencies.");
        } catch (Exception ex) {
            Debug.LogError($"[JSRunner] Reset failed: {ex.Message}");
            EditorUtility.DisplayDialog(
                "Reset Failed",
                $"Failed to reset working directory:\n\n{ex.Message}",
                "OK"
            );
        }
    }

    // MARK: Utilities

    void OpenWorkingDirectory() {
        var path = _target.WorkingDirFullPath;
        if (Directory.Exists(path)) EditorUtility.RevealInFinder(path);
        else Debug.LogWarning($"[JSRunner] Directory not found: {path}");
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
        try { Process.Start("gnome-terminal", $"--working-directory=\"{path}\""); }
        catch {
            try { Process.Start("konsole", $"--workdir \"{path}\""); }
            catch { Process.Start("xterm", $"-e 'cd \"{path}\" && bash'"); }
        }
#endif
    }

    void OpenVSCode() {
        var path = _target.WorkingDirFullPath;
        if (!Directory.Exists(path)) {
            Debug.LogWarning($"[JSRunner] Directory not found: {path}");
            return;
        }

        var fullPath = Path.GetFullPath(path);

#if UNITY_EDITOR_WIN
        var codePath = GetCodeExecutablePathOnWindows();
        if (!string.IsNullOrEmpty(codePath)) {
            try {
                Process.Start(new ProcessStartInfo {
                    FileName = codePath,
                    Arguments = $"\"{fullPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                return;
            } catch { }
        }
        // Fallback to shell
        Process.Start(new ProcessStartInfo {
            FileName = "cmd.exe",
            Arguments = $"/C code \"{fullPath}\"",
            UseShellExecute = true,
            CreateNoWindow = true
        });
#elif UNITY_EDITOR_OSX
        // Try login shell first
        if (!LaunchViaLoginShell($"code -n -- '{ShellEscape(fullPath)}'")) {
            // Fallback to open command
            try {
                Process.Start("open", $"-n -b com.microsoft.VSCode --args \"{fullPath}\"");
            } catch (Exception ex) {
                Debug.LogError($"[JSRunner] Failed to open VS Code: {ex.Message}");
            }
        }
#elif UNITY_EDITOR_LINUX
        LaunchViaLoginShell($"code -n \"{fullPath}\"");
#endif
    }

#if UNITY_EDITOR_WIN
    string GetCodeExecutablePathOnWindows() {
        // Common installation paths
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        string[] searchPaths = {
            Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(programFiles, "Microsoft VS Code", "Code.exe"),
        };

        foreach (var p in searchPaths) {
            if (File.Exists(p)) return p;
        }

        // Check PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator)) {
            var codePath = Path.Combine(dir, "code.cmd");
            if (File.Exists(codePath)) return codePath;
        }

        return null;
    }
#endif

#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
    bool LaunchViaLoginShell(string command) {
        try {
            var shell = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/zsh";
            var process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = shell,
                    Arguments = $"-l -i -c \"{command}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.Start();
            return true;
        } catch {
            return false;
        }
    }

    string ShellEscape(string s) => s.Replace("'", "'\\''");
#endif
}

// MARK: Style Extensions
static class StyleExtensions {
    public static void SetBorderWidth(this IStyle style, float width) {
        style.borderTopWidth = style.borderBottomWidth = style.borderLeftWidth = style.borderRightWidth = width;
    }

    public static void SetBorderColor(this IStyle style, Color color) {
        style.borderTopColor = style.borderBottomColor = style.borderLeftColor = style.borderRightColor = color;
    }

    public static void SetBorderRadius(this IStyle style, float radius) {
        style.borderTopLeftRadius = style.borderTopRightRadius =
            style.borderBottomLeftRadius = style.borderBottomRightRadius = radius;
    }
}
