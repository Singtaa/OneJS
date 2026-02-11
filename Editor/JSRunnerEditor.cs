using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using OneJS.Editor;
using OneJS.Editor.TypeGenerator;
using Unity.CodeEditor;
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
    const string CodeEditorPathPrefKey = "OneJS.CodeEditorPath";
    const string UseSceneNameAsRootFolderPrefKey = "OneJS.Initialize.UseSceneNameAsRootFolder";

    JSRunner _target;
    bool _buildInProgress;
    string _buildOutput;

    VisualElement _tabBarContainer;
    VisualElement _tabContent;
    Button[] _tabButtons;
    int _activeTab;
    VisualElement _actionsContainer;
    VisualElement _statusContainer;
    VisualElement _statusRow;
    Label _statusLabel;
    Label _statusLabelNotInit;
    Label _statusLabelNotValid;
    Label _reloadCountLabel;
    VisualElement _watcherRow;
    Label _watcherStatusLabel;
    Label _watcherStateLabel;
    Label _workingDirLabel;
    VisualElement _statusNotInitializedContainer;
    VisualElement _statusNotValidContainer;
    Button _reloadButton;
    Button _buildButton;
    VisualElement _buildOutputContainer;
    Label _buildOutputLabel;

    // Type generation
    Button _generateTypesButton;
    Label _typeGenStatusLabel;
    Label _buildOutputPathLabel;

    // Cartridges
    VisualElement _cartridgeListContainer;

    // Custom lists
    VisualElement _stylesheetsListContainer;
    VisualElement _preloadsListContainer;
    VisualElement _globalsListContainer;

    // Track PanelSettings render mode to refresh UIDocument inspector
    int _lastRenderMode;

    void OnEnable() {
        _target = (JSRunner)target;
        _activeTab = EditorPrefs.GetInt(TabPrefKey, 0);
        EditorApplication.update += UpdateDynamicUI;

        // Subscribe to watcher events
        NodeWatcherManager.OnWatcherStarted += OnWatcherStateChanged;
        NodeWatcherManager.OnWatcherStopped += OnWatcherStateChanged;

        // Only reattach to watcher when in Play mode; in Edit mode keep no watcher so folder can be moved/renamed
        if (Application.isPlaying) {
            var workingDir = _target.WorkingDirFullPath;
            if (!string.IsNullOrEmpty(workingDir))
                NodeWatcherManager.TryReattach(workingDir);
        }

        // Initialize render mode tracking (PanelSettings is on JSRunner; UIDocument is added at runtime)
        var ps = serializedObject.FindProperty("_panelSettings").objectReferenceValue as PanelSettings;
        if (ps != null) {
            var psSO = new SerializedObject(ps);
            var renderModeProp = psSO.FindProperty("m_RenderMode");
            if (renderModeProp != null) {
                _lastRenderMode = renderModeProp.enumValueIndex;
            }
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

        // Panel Settings + Initialize (above everything, including status)
        root.Add(CreatePanelSettingsSection());

        // Status (hidden when Panel Settings not attached or invalid)
        _statusContainer = CreateStatusSection();
        root.Add(_statusContainer);

        // Tab bar (hidden when Panel Settings not attached or invalid)
        _tabBarContainer = CreateTabBar();
        root.Add(_tabBarContainer);

        // Tab content container
        _tabContent = new VisualElement();
        _tabContent.style.backgroundColor = OneJSEditorDesign.Colors.ContentBg;
        _tabContent.style.borderTopWidth = 0;
        _tabContent.style.borderLeftWidth = _tabContent.style.borderRightWidth = _tabContent.style.borderBottomWidth = 1;
        _tabContent.style.borderLeftColor = _tabContent.style.borderRightColor = _tabContent.style.borderBottomColor = OneJSEditorDesign.Colors.Border;
        _tabContent.style.borderTopLeftRadius = _tabContent.style.borderTopRightRadius = 0;
        _tabContent.style.borderBottomLeftRadius = _tabContent.style.borderBottomRightRadius = 3;
        _tabContent.style.paddingTop = _tabContent.style.paddingBottom = 10;
        _tabContent.style.paddingLeft = _tabContent.style.paddingRight = 10;
        _tabContent.style.minHeight = 100;
        root.Add(_tabContent);

        // Show initial tab
        ShowTab(_activeTab);

        // Actions (hidden when Panel Settings not attached or invalid)
        _actionsContainer = CreateActionsSection();
        root.Add(_actionsContainer);

        // Initial visibility of tabs + actions: show only when Panel Settings is valid (status block always visible)
        serializedObject.Update();
        var hasPSInitial = serializedObject.FindProperty("_panelSettings").objectReferenceValue != null;
        var validInitial = hasPSInitial && _target.IsPanelSettingsInValidProjectFolder();
        var debugModeInitialProp = serializedObject.FindProperty("_enableDebugMode");
        var debugModeInitial = debugModeInitialProp != null && debugModeInitialProp.boolValue;
        var showTabsAndActions = validInitial || debugModeInitial;
        _tabBarContainer.style.display = showTabsAndActions ? DisplayStyle.Flex : DisplayStyle.None;
        _tabContent.style.display = showTabsAndActions ? DisplayStyle.Flex : DisplayStyle.None;
        _actionsContainer.style.display = showTabsAndActions ? DisplayStyle.Flex : DisplayStyle.None;

        return root;
    }

    // MARK: Panel Settings (above tabs)

    VisualElement CreatePanelSettingsSection() {
        var section = new VisualElement();
        section.style.marginTop = 2;
        section.style.marginBottom = 6; // match space below status block

        var panelSettingsPropProject = serializedObject.FindProperty("_panelSettings");

        // Panel Settings field first
        var panelSettingsRefField = new PropertyField(panelSettingsPropProject, "Panel Settings");
        section.Add(panelSettingsRefField);

        return section;
    }

    // MARK: Tab System

    VisualElement CreateTabBar() {
        var container = new VisualElement();
        container.style.flexDirection = FlexDirection.Row;
        container.style.marginTop = 6; // with status marginBottom 2 → total 8 (matches space above status)

        string[] tabNames = { OneJSEditorDesign.Texts.TabProject, OneJSEditorDesign.Texts.TabUI, OneJSEditorDesign.Texts.TabCartridges, OneJSEditorDesign.Texts.TabBuild };
        _tabButtons = new Button[tabNames.Length];

        var borderColor = OneJSEditorDesign.Colors.Border;

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
            btn.style.borderLeftWidth = 1;
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
                    btn.style.backgroundColor = OneJSEditorDesign.Colors.TabHover;
            });
            btn.RegisterCallback<MouseLeaveEvent>(_ => {
                if (idx != _activeTab)
                    btn.style.backgroundColor = OneJSEditorDesign.Colors.TabInactive;
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

        var borderColor = OneJSEditorDesign.Colors.Border;

        for (int i = 0; i < _tabButtons.Length; i++) {
            bool isActive = i == _activeTab;
            var btn = _tabButtons[i];

            btn.style.backgroundColor = isActive
                ? OneJSEditorDesign.Colors.ContentBg
                : OneJSEditorDesign.Colors.TabInactive;

            // Active tab: no bottom border (merges with content)
            // Inactive tab: has bottom border
            btn.style.borderBottomWidth = isActive ? 0 : 1;
            btn.style.borderBottomColor = borderColor;
        }
    }

    // MARK: Tab Content Builders

    void BuildProjectTab(VisualElement container) {
        var liveReloadProp = serializedObject.FindProperty("_liveReload");
        var liveReloadRow = CreateRow();
        var liveReloadLabel = new Label(OneJSEditorDesign.Texts.LiveReload);
        liveReloadLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        liveReloadLabel.style.width = 192;
        liveReloadRow.Add(liveReloadLabel);
        var liveReloadField = new PropertyField(liveReloadProp, "");
        liveReloadRow.Add(liveReloadField);
        container.Add(liveReloadRow);

        var liveReloadSettings = new VisualElement();
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
        var preloadsLabel = new Label(OneJSEditorDesign.Texts.Preloads);
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
        var globalsLabel = new Label(OneJSEditorDesign.Texts.Globals);
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
        // Stylesheets section (at top)
        var stylesheetsHeader = CreateRow();
        stylesheetsHeader.style.marginBottom = 4;
        var stylesheetsLabel = new Label(OneJSEditorDesign.Texts.Stylesheets);
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

        AddSpacer(container);

        // Panel Settings section header (reference field is on Project tab; configure the asset here)
        var panelSettingsHeader = CreateRow();
        panelSettingsHeader.style.marginBottom = 4;
        var panelSettingsLabel = new Label(OneJSEditorDesign.Texts.PanelSettings);
        panelSettingsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        panelSettingsLabel.style.flexGrow = 1;
        panelSettingsLabel.tooltip = "Configure the assigned PanelSettings asset. Assign the asset in the Project tab.";
        panelSettingsHeader.Add(panelSettingsLabel);
        container.Add(panelSettingsHeader);

        var panelSettingsProp = serializedObject.FindProperty("_panelSettings");
        var psInspectorContainer = new VisualElement();
        psInspectorContainer.style.marginTop = 4;
        container.Add(psInspectorContainer);

        // Function to rebuild embedded inspector
        void RebuildPanelSettingsInspector() {
            psInspectorContainer.Clear();

            var ps = panelSettingsProp.objectReferenceValue as PanelSettings;
            if (ps != null) {
                var inspector = new InspectorElement(ps);
                inspector.style.paddingLeft = 0;
                inspector.style.marginLeft = 0;
                psInspectorContainer.Add(inspector);
            } else {
                // Match Unity's empty-list hint style (e.g. "No stylesheets...")
                var emptyLabel = new Label(OneJSEditorDesign.Texts.NoSettings);
                emptyLabel.style.color = OneJSEditorDesign.Colors.TextMuted;
                emptyLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
                emptyLabel.style.paddingTop = 4;
                emptyLabel.style.paddingBottom = 4;
                psInspectorContainer.Add(emptyLabel);
            }
        }

        RebuildPanelSettingsInspector();
    }

    void BuildBuildTab(VisualElement container) {
        // Build Settings
        AddSectionHeader(container, OneJSEditorDesign.Texts.BuildOutput);

        // Show current bundle asset status (same style as message boxes)
        var bundleAsset = _target.BundleAsset;
        var sourceMapAsset = _target.SourceMapAsset;

        var statusBox = CreateInfoStyleContainer();
        statusBox.style.marginBottom = 8;

        var bundleLabel = CreateLabel("Bundle:", 80);
        bundleLabel.style.color = OneJSEditorDesign.Colors.TextInfo;
        var bundleRow = CreateRow();
        bundleRow.Add(bundleLabel);
        var bundleStatus = new Label(bundleAsset != null ? $"✓ {bundleAsset.name}" : OneJSEditorDesign.Texts.NotGenerated);
        bundleStatus.style.color = bundleAsset != null ? OneJSEditorDesign.Colors.StatusSuccess : OneJSEditorDesign.Colors.TextInfo;
        bundleRow.Add(bundleStatus);
        statusBox.Add(bundleRow);

        var sourceMapLabel = CreateLabel("Source Map:", 80);
        sourceMapLabel.style.color = OneJSEditorDesign.Colors.TextInfo;
        var sourceMapRow = CreateRow();
        sourceMapRow.Add(sourceMapLabel);
        var sourceMapStatus = new Label(sourceMapAsset != null ? $"✓ {sourceMapAsset.name}" : OneJSEditorDesign.Texts.NotGenerated);
        sourceMapStatus.style.color = sourceMapAsset != null ? OneJSEditorDesign.Colors.StatusSuccess : OneJSEditorDesign.Colors.TextInfo;
        sourceMapRow.Add(sourceMapStatus);
        statusBox.Add(sourceMapRow);

        container.Add(statusBox);

        // Source map option
        container.Add(new PropertyField(serializedObject.FindProperty("_includeSourceMap"), "Include Source Map"));

        var buildHelpBox = CreateInfoBox("Bundle TextAsset is auto-generated during Unity build.");
        buildHelpBox.style.marginTop = 4;
        container.Add(buildHelpBox);

        // Output: {path} below message box, same style as Type Generation output
        _buildOutputPathLabel = new Label();
        _buildOutputPathLabel.style.marginTop = 4;
        _buildOutputPathLabel.style.color = OneJSEditorDesign.Colors.TextMuted;
        _buildOutputPathLabel.style.fontSize = 10;
        container.Add(_buildOutputPathLabel);
        UpdateBuildOutputPath();

        AddSpacer(container);

        // Type Generation
        AddSectionHeader(container, OneJSEditorDesign.Texts.TypeGeneration);

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
        _generateTypesButton = new Button(GenerateTypings) { text = OneJSEditorDesign.Texts.GenerateTypesNow };
        _generateTypesButton.style.height = _generateTypesButton.style.minHeight = 22;
        _generateTypesButton.style.flexGrow = 1;
        typeGenRow.Add(_generateTypesButton);
        container.Add(typeGenRow);

        _typeGenStatusLabel = new Label();
        _typeGenStatusLabel.style.marginTop = 4;
        _typeGenStatusLabel.style.color = OneJSEditorDesign.Colors.TextMuted;
        _typeGenStatusLabel.style.fontSize = 10;
        container.Add(_typeGenStatusLabel);
        UpdateTypeGenStatus();

        AddSpacer(container);

        // Scaffolding
        AddSectionHeader(container, OneJSEditorDesign.Texts.Scaffolding);

        var defaultFilesField = new PropertyField(serializedObject.FindProperty("_defaultFiles"), "Default Files");
        defaultFilesField.tooltip = "Template files scaffolded when working directory is empty";
        container.Add(defaultFilesField);

        var scaffoldRow = CreateRow();
        scaffoldRow.style.marginTop = 5;
        var repopulateButton = new Button(() => {
            _target.PopulateDefaultFiles();
            serializedObject.Update();
            EditorUtility.SetDirty(_target);
        }) { text = OneJSEditorDesign.Texts.ResetToDefaults };
        repopulateButton.style.height = repopulateButton.style.minHeight = 22;
        repopulateButton.style.flexGrow = 1;
        repopulateButton.tooltip = "Repopulate the default files list from OneJS templates";
        scaffoldRow.Add(repopulateButton);
        container.Add(scaffoldRow);
    }

    void BuildCartridgesTab(VisualElement container) {
        // Header with Add button
        var headerRow = CreateRow();
        headerRow.style.marginBottom = 4;

        var headerLabel = new Label(OneJSEditorDesign.Texts.UICartridges);
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

        // Help box (gray info style)
        var helpBox = CreateInfoBox(
            "Files are extracted to @cartridges/{path}/. Access via __cart('slug') or __cart('@namespace/slug') at runtime.\n" +
            "E = Extract (overwrites existing), D = Delete extracted folder, X = Remove from list");
        helpBox.style.marginTop = 4;
        container.Add(helpBox);

        // Extract All / Delete All buttons
        var bulkRow = CreateRow();
        bulkRow.style.marginTop = 8;

        var extractAllBtn = new Button(() => ExtractAllCartridges()) { text = OneJSEditorDesign.Texts.ExtractAll };
        extractAllBtn.style.flexGrow = 1;
        extractAllBtn.style.height = extractAllBtn.style.minHeight = 22;
        extractAllBtn.tooltip = "Extract all cartridges (with confirmation)";
        bulkRow.Add(extractAllBtn);

        var deleteAllBtn = new Button(() => DeleteAllCartridges()) { text = OneJSEditorDesign.Texts.DeleteAllExtracted };
        deleteAllBtn.style.flexGrow = 1;
        deleteAllBtn.style.height = deleteAllBtn.style.minHeight = 22;
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
            var emptyLabel = new Label(OneJSEditorDesign.Texts.NoStylesheets);
            emptyLabel.style.color = OneJSEditorDesign.Colors.TextMuted;
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
        indexLabel.style.color = OneJSEditorDesign.Colors.TextDim;
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
            var emptyLabel = new Label(OneJSEditorDesign.Texts.NoPreloads);
            emptyLabel.style.color = OneJSEditorDesign.Colors.TextMuted;
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
        indexLabel.style.color = OneJSEditorDesign.Colors.TextDim;
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
            var emptyLabel = new Label(OneJSEditorDesign.Texts.NoGlobals);
            emptyLabel.style.color = OneJSEditorDesign.Colors.TextMuted;
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
        indexLabel.style.color = OneJSEditorDesign.Colors.TextDim;
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
        arrow.style.color = OneJSEditorDesign.Colors.TextDim;
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
            var emptyLabel = new Label(OneJSEditorDesign.Texts.NoCartridges);
            emptyLabel.style.color = OneJSEditorDesign.Colors.TextMuted;
            emptyLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
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
        row.style.backgroundColor = OneJSEditorDesign.Colors.RowBg;
        row.style.SetBorderRadius(3);

        // Index label
        var indexLabel = new Label($"{index}");
        indexLabel.style.width = 16;
        indexLabel.style.color = OneJSEditorDesign.Colors.TextDim;
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
                statusLabel.text = OneJSEditorDesign.Texts.Extracted;
                statusLabel.style.color = OneJSEditorDesign.Colors.StatusSuccess;
            } else {
                statusLabel.text = OneJSEditorDesign.Texts.NotExtracted;
                statusLabel.style.color = OneJSEditorDesign.Colors.TextMuted;
            }
        } else if (!_target.IsSceneSaved) {
            statusLabel.text = OneJSEditorDesign.Texts.NonInitializedYet;
            statusLabel.style.color = OneJSEditorDesign.Colors.StatusWarning;
        } else {
            statusLabel.text = cartridge == null ? "" : OneJSEditorDesign.Texts.NoSlug;
            statusLabel.style.color = OneJSEditorDesign.Colors.StatusWarning;
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
        removeBtn.style.backgroundColor = OneJSEditorDesign.Colors.ButtonDanger;
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
        container.style.marginBottom = 2; // space below status; tab bar marginTop 6 gives total 8 (matches above)

        // Right-click on status dialogue: Run in Background toggle
        container.RegisterCallback<ContextClickEvent>(evt => {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Run in Background"), PlayerSettings.runInBackground, () => {
                PlayerSettings.runInBackground = !PlayerSettings.runInBackground;
            });
            menu.AddItem(new GUIContent("Use Scene Name as Root Folder"), EditorPrefs.GetBool(UseSceneNameAsRootFolderPrefKey, true), () => {
                EditorPrefs.SetBool(UseSceneNameAsRootFolderPrefKey, !EditorPrefs.GetBool(UseSceneNameAsRootFolderPrefKey, true));
            });
            menu.ShowAsContext();
            evt.StopPropagation();
        });

        // Status row (hidden when showing not-initialized/not-valid blocks; those have their own status row inside)
        _statusRow = CreateRow();
        _statusRow.Add(CreateLabel(OneJSEditorDesign.Texts.Status, 50, true));
        _statusLabel = new Label(OneJSEditorDesign.Texts.Stopped);
        _statusLabel.style.color = OneJSEditorDesign.Colors.StatusStopped;
        _statusRow.Add(_statusLabel);
        container.Add(_statusRow);

        // Live reload count (shown when > 0)
        _reloadCountLabel = new Label();
        _reloadCountLabel.style.marginTop = 4;
        _reloadCountLabel.style.display = DisplayStyle.None;
        container.Add(_reloadCountLabel);

        // Watcher status ("Watcher: " in gray + state in color) — hidden when Panel Settings not set/invalid
        _watcherRow = CreateRow();
        _watcherRow.style.marginTop = 4;
        _watcherStatusLabel = new Label(OneJSEditorDesign.Texts.Watcher);
        _watcherStatusLabel.style.fontSize = 11;
        _watcherStatusLabel.style.color = OneJSEditorDesign.Colors.TextMuted;
        _watcherRow.Add(_watcherStatusLabel);
        _watcherStateLabel = new Label();
        _watcherStateLabel.style.fontSize = 11;
        _watcherRow.Add(_watcherStateLabel);
        container.Add(_watcherRow);

        // Working directory path — hidden when Panel Settings not set/invalid
        _workingDirLabel = new Label();
        _workingDirLabel.style.marginTop = 4;
        _workingDirLabel.style.fontSize = 11;
        _workingDirLabel.style.color = OneJSEditorDesign.Colors.TextMuted;
        _workingDirLabel.style.whiteSpace = WhiteSpace.Normal;
        container.Add(_workingDirLabel);

        // When Panel Settings not set: one row with [ status+message column | button column ] so button centers in full height
        _statusNotInitializedContainer = new VisualElement();
        _statusNotInitializedContainer.style.flexDirection = FlexDirection.Row;
        _statusNotInitializedContainer.style.alignItems = Align.Stretch;
        _statusNotInitializedContainer.style.display = DisplayStyle.None;
        var notInitLeft = new VisualElement();
        notInitLeft.style.flexGrow = 1;
        notInitLeft.style.flexShrink = 1;
        notInitLeft.style.minWidth = 0;
        notInitLeft.style.marginRight = 12;
        var notInitStatusRow = CreateRow();
        notInitStatusRow.Add(CreateLabel(OneJSEditorDesign.Texts.Status, 50, true));
        _statusLabelNotInit = new Label(OneJSEditorDesign.Texts.NotInitialized);
        _statusLabelNotInit.style.color = OneJSEditorDesign.Colors.TextNeutral;
        notInitStatusRow.Add(_statusLabelNotInit);
        notInitLeft.Add(notInitStatusRow);
        var notInitMsg = new Label("Initialize a new OneJS project, or assign a Panel Settings asset. The assigned asset’s folder becomes the active project folder for this runner.");
        notInitMsg.style.marginTop = 4;
        notInitMsg.style.whiteSpace = WhiteSpace.Normal;
        notInitMsg.style.fontSize = 11;
        notInitMsg.style.color = OneJSEditorDesign.Colors.TextMuted;
        notInitLeft.Add(notInitMsg);
        _statusNotInitializedContainer.Add(notInitLeft);
        var initBtnColumn = new VisualElement();
        initBtnColumn.style.flexShrink = 0;
        initBtnColumn.style.flexDirection = FlexDirection.Row;
        initBtnColumn.style.alignItems = Align.Center;
        initBtnColumn.style.justifyContent = Justify.Center;
        var initBtn = new Button(RunInitializeProject) { text = OneJSEditorDesign.Texts.InitializeProject };
        initBtn.style.height = 22;
        initBtn.style.flexShrink = 0;
        initBtn.style.backgroundColor = OneJSEditorDesign.Colors.ButtonPrimaryBg;
        initBtn.style.color = OneJSEditorDesign.Colors.ButtonPrimaryText;
        initBtn.RegisterCallback<MouseEnterEvent>(_ => initBtn.style.backgroundColor = OneJSEditorDesign.Colors.ButtonPrimaryHover);
        initBtn.RegisterCallback<MouseLeaveEvent>(_ => initBtn.style.backgroundColor = OneJSEditorDesign.Colors.ButtonPrimaryBg);
        initBtnColumn.Add(initBtn);
        _statusNotInitializedContainer.Add(initBtnColumn);
        container.Add(_statusNotInitializedContainer);

        // When Panel Settings invalid: one row with [ status+message column | button column ] so button centers in full height
        _statusNotValidContainer = new VisualElement();
        _statusNotValidContainer.style.flexDirection = FlexDirection.Row;
        _statusNotValidContainer.style.alignItems = Align.Stretch;
        _statusNotValidContainer.style.display = DisplayStyle.None;
        var notValidLeft = new VisualElement();
        notValidLeft.style.flexGrow = 1;
        notValidLeft.style.flexShrink = 1;
        notValidLeft.style.minWidth = 0;
        notValidLeft.style.marginRight = 12;
        var notValidStatusRow = CreateRow();
        notValidStatusRow.Add(CreateLabel(OneJSEditorDesign.Texts.Status, 50, true));
        _statusLabelNotValid = new Label(OneJSEditorDesign.Texts.NotValid);
        _statusLabelNotValid.style.color = OneJSEditorDesign.Colors.StatusWarning;
        notValidStatusRow.Add(_statusLabelNotValid);
        notValidLeft.Add(notValidStatusRow);
        var notValidMsg = new Label("Panel Settings is not valid. The asset must be located in a valid project folder that contains OneJS project files.");
        notValidMsg.style.marginTop = 4;
        notValidMsg.style.whiteSpace = WhiteSpace.Normal;
        notValidMsg.style.fontSize = 11;
        notValidMsg.style.color = OneJSEditorDesign.Colors.TextMuted;
        notValidLeft.Add(notValidMsg);
        _statusNotValidContainer.Add(notValidLeft);
        var removeBtnColumn = new VisualElement();
        removeBtnColumn.style.flexShrink = 0;
        removeBtnColumn.style.flexDirection = FlexDirection.Row;
        removeBtnColumn.style.alignItems = Align.Center;
        removeBtnColumn.style.justifyContent = Justify.Center;
        var removeBtn = new Button(RunRemovePanelSettings) { text = OneJSEditorDesign.Texts.RemoveSettings };
        removeBtn.style.height = 22;
        removeBtn.style.flexShrink = 0;
        removeBtn.style.backgroundColor = OneJSEditorDesign.Colors.ButtonPrimaryBg;
        removeBtn.style.color = OneJSEditorDesign.Colors.ButtonPrimaryText;
        removeBtn.RegisterCallback<MouseEnterEvent>(_ => removeBtn.style.backgroundColor = OneJSEditorDesign.Colors.ButtonPrimaryHover);
        removeBtn.RegisterCallback<MouseLeaveEvent>(_ => removeBtn.style.backgroundColor = OneJSEditorDesign.Colors.ButtonPrimaryBg);
        removeBtnColumn.Add(removeBtn);
        _statusNotValidContainer.Add(removeBtnColumn);
        container.Add(_statusNotValidContainer);

        return container;
    }

    // MARK: Actions Section

    VisualElement CreateActionsSection() {
        var container = new VisualElement { style = { marginTop = 12, marginBottom = 10 } };

        // Row 1: Reload and Build
        var row1 = CreateRow();
        row1.style.marginBottom = 4;

        _reloadButton = new Button(() => _target.ForceReload()) { text = OneJSEditorDesign.Texts.Reload };
        _reloadButton.style.height = 30;
        _reloadButton.style.flexGrow = 1;
        _reloadButton.tooltip = "Reload/Rerun this JavaScript runtime";
        _reloadButton.SetEnabled(false);
        row1.Add(_reloadButton);

        _buildButton = new Button(RunRebuild) { text = OneJSEditorDesign.Texts.Rebuild };
        _buildButton.style.height = 30;
        _buildButton.style.flexGrow = 1;
        _buildButton.tooltip = "Delete node_modules and reinstall dependencies, then build";
        row1.Add(_buildButton);

        container.Add(row1);

        // Row 2: Open Folder, Terminal, and Code Editor
        var row2 = CreateRow();

        var openFolderButton = new Button(OpenWorkingDirectory) { text = "Open Folder" };
        openFolderButton.style.height = 24;
        openFolderButton.style.flexGrow = 1;
        openFolderButton.tooltip = "Open working folder in file explorer.";
        row2.Add(openFolderButton);

        var openTerminalButton = new Button(OpenTerminal) { text = "Open Terminal" };
        openTerminalButton.style.height = 24;
        openTerminalButton.style.flexGrow = 1;
        openTerminalButton.tooltip = OneJSWslHelper.IsWslInstalled
            ? "Open terminal at working folder. Right-click for WSL option."
            : "Open terminal at working folder";
        row2.Add(openTerminalButton);

#if UNITY_EDITOR_WIN
        openTerminalButton.RegisterCallback<ContextClickEvent>(evt => ShowOpenTerminalContextMenu(evt));
#endif

        var openCodeEditorButton = new Button() { text = "Open Code Editor" };
        openCodeEditorButton.style.height = 24;
        openCodeEditorButton.style.flexGrow = 1;
        openCodeEditorButton.tooltip = "Open working folder in the code editor configured in Preferences > External Tools. Right-click to select a custom editor.";
        openCodeEditorButton.RegisterCallback<ClickEvent>(evt => OpenCodeEditor(evt.ctrlKey || evt.commandKey));
        openCodeEditorButton.RegisterCallback<ContextClickEvent>(evt => ShowOpenCodeEditorContextMenu(evt));
        row2.Add(openCodeEditorButton);

        container.Add(row2);

        CreateInfoBoxWithLabel(out _buildOutputContainer, out _buildOutputLabel);
        _buildOutputContainer.style.marginTop = 5;
        _buildOutputContainer.style.display = DisplayStyle.None;
        container.Add(_buildOutputContainer);

        return container;
    }

    // MARK: UI Helpers

    VisualElement CreateStyledBox() {
        var box = new VisualElement();
        box.style.backgroundColor = OneJSEditorDesign.Colors.BoxBg;
        box.style.SetBorderWidth(1);
        box.style.SetBorderColor(OneJSEditorDesign.Colors.Border);
        box.style.SetBorderRadius(3);
        box.style.paddingTop = box.style.paddingBottom = 8;
        box.style.paddingLeft = box.style.paddingRight = 10;
        return box;
    }

    /// <summary>Empty container with message-box (info) style. Use for custom content like status rows.</summary>
    static VisualElement CreateInfoStyleContainer() {
        var box = new VisualElement();
        box.style.paddingTop = box.style.paddingBottom = 8;
        box.style.paddingLeft = box.style.paddingRight = 10;
        box.style.backgroundColor = OneJSEditorDesign.Colors.InfoBoxBg;
        box.style.borderTopLeftRadius = box.style.borderTopRightRadius = box.style.borderBottomLeftRadius = box.style.borderBottomRightRadius = 3;
        return box;
    }

    /// <summary>Info message box: same structure as the init warning box but gray (dark gray bg, gray left border).</summary>
    static VisualElement CreateInfoBox(string message) {
        var box = CreateInfoStyleContainer();
        var label = new Label(message);
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.color = OneJSEditorDesign.Colors.TextInfo;
        box.Add(label);
        return box;
    }

    /// <summary>Info box with an editable label (for dynamic content like build output). Returns (container, label).</summary>
    static void CreateInfoBoxWithLabel(out VisualElement container, out Label label) {
        container = CreateInfoStyleContainer();
        label = new Label();
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.color = OneJSEditorDesign.Colors.TextInfo;
        container.Add(label);
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

    /// <summary>Format full path for display: no drive letter, start from Assets, forward slashes (same as Working Folder in status).</summary>
    static string FormatPathFromAssets(JSRunner runner, string fullPath) {
        if (string.IsNullOrEmpty(fullPath)) return null;
        if (runner == null || string.IsNullOrEmpty(runner.ProjectRoot)) return fullPath;
        try {
            var relative = System.IO.Path.GetRelativePath(runner.ProjectRoot, fullPath);
            return relative.Replace(System.IO.Path.DirectorySeparatorChar, '/');
        } catch {
            return fullPath;
        }
    }

    static string FormatTimeAgo(DateTime time) {
        if (time == default) return "never";

        var elapsed = DateTime.Now - time;

        if (elapsed.TotalSeconds < 5) return "just now";
        if (elapsed.TotalSeconds < 60) return $"{(int)elapsed.TotalSeconds}s ago";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
        return time.ToString("MMM d, HH:mm");
    }

    // MARK: Type Generation

    void GenerateTypings() {
        if (TypeGeneratorService.GenerateTypingsFor(_target, silent: false)) {
            _typeGenStatusLabel.text = $"Generated at {DateTime.Now:HH:mm:ss}";
            _typeGenStatusLabel.style.color = OneJSEditorDesign.Colors.StatusSuccessAlt;
        } else {
            _typeGenStatusLabel.text = "Generation failed - check console";
            _typeGenStatusLabel.style.color = OneJSEditorDesign.Colors.StatusError;
        }
    }

    void UpdateTypeGenStatus() {
        if (_typeGenStatusLabel == null) return;

        var outputPath = _target.TypingsFullPath;
        if (string.IsNullOrEmpty(outputPath)) {
            _typeGenStatusLabel.style.display = DisplayStyle.None;
            _typeGenStatusLabel.text = "";
            return;
        } else if (File.Exists(outputPath)) {
            _typeGenStatusLabel.style.display = DisplayStyle.Flex;
            var lastWrite = File.GetLastWriteTime(outputPath);
            _typeGenStatusLabel.text = $"{OneJSEditorDesign.Texts.Output}{_target.TypingsOutputPath} (last updated: {lastWrite:g})";
        } else {
            _typeGenStatusLabel.style.display = DisplayStyle.Flex;
            _typeGenStatusLabel.text = $"{OneJSEditorDesign.Texts.Output}{_target.TypingsOutputPath} (not yet generated)";
        }
    }

    void UpdateBuildOutputPath() {
        if (_buildOutputPathLabel == null) return;

        var path = FormatPathFromAssets(_target, _target.BundleAssetPath);
        if (string.IsNullOrEmpty(path)) {
            _buildOutputPathLabel.style.display = DisplayStyle.None;
            _buildOutputPathLabel.text = "";
        } else {
            _buildOutputPathLabel.style.display = DisplayStyle.Flex;
            _buildOutputPathLabel.text = OneJSEditorDesign.Texts.Output + path;
        }
    }

    // MARK: Dynamic UI Updates

    void UpdateDynamicUI() {
        // Check if target was destroyed (e.g., user deleted the GameObject)
        if (_target == null || _statusLabel == null) return;
        serializedObject.Update();

        var panelSettingsRef = serializedObject.FindProperty("_panelSettings").objectReferenceValue;
        bool hasPanelSettings = panelSettingsRef != null;
        bool validFolder = hasPanelSettings && _target.IsPanelSettingsInValidProjectFolder();
        bool showAsNotInitialized = !hasPanelSettings || !validFolder;
        bool treatAsNotInitializedInPlay = Application.isPlaying && hasPanelSettings && !validFolder;

        // Status block always visible; content switches by Panel Settings state
        if (showAsNotInitialized) {
            if (_statusRow != null) _statusRow.style.display = DisplayStyle.None; // hide main status row; not-init/not-valid blocks have their own
            if (hasPanelSettings && !validFolder) {
                if (_statusLabelNotValid != null) {
                    _statusLabelNotValid.text = OneJSEditorDesign.Texts.NotValid;
                    _statusLabelNotValid.style.color = OneJSEditorDesign.Colors.StatusWarning;
                }
            } else {
                if (_statusLabelNotInit != null) {
                    _statusLabelNotInit.text = OneJSEditorDesign.Texts.NotInitialized;
                    _statusLabelNotInit.style.color = OneJSEditorDesign.Colors.TextNeutral;
                }
            }
            if (_watcherRow != null) _watcherRow.style.display = DisplayStyle.None;
            if (_workingDirLabel != null) _workingDirLabel.style.display = DisplayStyle.None;
            if (_statusNotInitializedContainer != null) _statusNotInitializedContainer.style.display = (hasPanelSettings && !validFolder) ? DisplayStyle.None : DisplayStyle.Flex;
            if (_statusNotValidContainer != null) _statusNotValidContainer.style.display = (hasPanelSettings && !validFolder) ? DisplayStyle.Flex : DisplayStyle.None;
            if (_reloadCountLabel != null) _reloadCountLabel.style.display = DisplayStyle.None;
        } else {
            if (_statusRow != null) _statusRow.style.display = DisplayStyle.Flex;
            if (_watcherRow != null) _watcherRow.style.display = DisplayStyle.Flex;
            if (_workingDirLabel != null) _workingDirLabel.style.display = DisplayStyle.Flex;
            if (_statusNotInitializedContainer != null) _statusNotInitializedContainer.style.display = DisplayStyle.None;
            if (_statusNotValidContainer != null) _statusNotValidContainer.style.display = DisplayStyle.None;

            // Status text (normal)
            if (Application.isPlaying) {
                _statusLabel.text = _target.IsRunning ? OneJSEditorDesign.Texts.Running : OneJSEditorDesign.Texts.Loading;
                _statusLabel.style.color = _target.IsRunning ? OneJSEditorDesign.Colors.StatusRunning : OneJSEditorDesign.Colors.StatusWarning;
            } else {
                _statusLabel.text = OneJSEditorDesign.Texts.Stopped;
                _statusLabel.style.color = OneJSEditorDesign.Colors.StatusStopped;
            }

            // Last reload time
            if (_reloadCountLabel != null) {
                if (Application.isPlaying && _target.IsRunning && _target.ReloadCount > 0) {
                    _reloadCountLabel.style.display = DisplayStyle.Flex;
                    _reloadCountLabel.text = $"{OneJSEditorDesign.Texts.LastReload}{FormatTimeAgo(_target.LastReloadTime)}";
                } else {
                    _reloadCountLabel.style.display = DisplayStyle.None;
                }
            }

            // Watcher status
            if (_watcherStateLabel != null) {
                var workingDir = _target.WorkingDirFullPath;
                if (string.IsNullOrEmpty(workingDir)) {
                    _watcherStateLabel.text = OneJSEditorDesign.Texts.NonInitializedYet;
                    _watcherStateLabel.style.color = OneJSEditorDesign.Colors.TextMuted;
                } else {
                    var isWatching = NodeWatcherManager.IsRunning(workingDir);
                    var isStarting = NodeWatcherManager.IsStarting(workingDir);
                    if (isStarting) {
                        _watcherStateLabel.text = OneJSEditorDesign.Texts.WatcherStarting;
                        _watcherStateLabel.style.color = OneJSEditorDesign.Colors.StatusWarning;
                    } else if (isWatching) {
                        _watcherStateLabel.text = OneJSEditorDesign.Texts.Running;
                        _watcherStateLabel.style.color = OneJSEditorDesign.Colors.StatusSuccess;
                    } else if (Application.isPlaying) {
                        _watcherStateLabel.text = OneJSEditorDesign.Texts.WatcherStarting;
                        _watcherStateLabel.style.color = OneJSEditorDesign.Colors.TextMuted;
                    } else {
                        _watcherStateLabel.text = OneJSEditorDesign.Texts.WatcherIdle;
                        _watcherStateLabel.style.color = OneJSEditorDesign.Colors.TextMuted;
                    }
                }
            }

            // Working directory path
            if (_workingDirLabel != null) {
                if (_target.InstanceFolder != null) {
                    var workingDir = _target.WorkingDirFullPath;
                    if (!string.IsNullOrEmpty(workingDir)) {
                        var relative = System.IO.Path.GetRelativePath(_target.ProjectRoot, workingDir).Replace(System.IO.Path.DirectorySeparatorChar, '/');
                        _workingDirLabel.text = OneJSEditorDesign.Texts.ProjectFolder + relative;
                    } else {
                        _workingDirLabel.text = OneJSEditorDesign.Texts.ProjectFolder + OneJSEditorDesign.Texts.NonInitializedYet;
                    }
                } else if (!_target.IsSceneSaved) {
                    _workingDirLabel.text = "Project Folder: (save scene first)";
                } else {
                    _workingDirLabel.text = OneJSEditorDesign.Texts.ProjectFolder + OneJSEditorDesign.Texts.NonInitializedYet;
                }
            }
        }

        // Hide tab bar, tab content, and actions when Panel Settings not attached or invalid
        var debugModeProp = serializedObject.FindProperty("_enableDebugMode");
        var debugMode = debugModeProp != null && debugModeProp.boolValue;
        var showBlocks = debugMode || (hasPanelSettings && validFolder);
        if (_tabBarContainer != null)
            _tabBarContainer.style.display = showBlocks ? DisplayStyle.Flex : DisplayStyle.None;
        if (_tabContent != null)
            _tabContent.style.display = showBlocks ? DisplayStyle.Flex : DisplayStyle.None;
        if (_actionsContainer != null)
            _actionsContainer.style.display = showBlocks ? DisplayStyle.Flex : DisplayStyle.None;

        // Buttons
        _reloadButton?.SetEnabled(_target.IsRunning);
        if (_buildButton != null) {
            _buildButton.SetEnabled(!_buildInProgress);
            _buildButton.text = _buildInProgress ? OneJSEditorDesign.Texts.Rebuilding : OneJSEditorDesign.Texts.Rebuild;
        }

        // Build output
        if (_buildOutputContainer != null && _buildOutputLabel != null) {
            if (!string.IsNullOrEmpty(_buildOutput)) {
                _buildOutputContainer.style.display = DisplayStyle.Flex;
                _buildOutputLabel.text = _buildOutput;
            } else {
                _buildOutputContainer.style.display = DisplayStyle.None;
            }
        }

        UpdateBuildOutputPath();
        UpdateTypeGenStatus();

        // Check if PanelSettings render mode changed - refresh inspector if so
        var panelSettings = serializedObject.FindProperty("_panelSettings").objectReferenceValue as PanelSettings;
        if (panelSettings != null) {
            var psSO = new SerializedObject(panelSettings);
            var renderModeProp = psSO.FindProperty("m_RenderMode");
            if (renderModeProp != null) {
                var currentRenderMode = renderModeProp.enumValueIndex;
                if (currentRenderMode != _lastRenderMode) {
                    _lastRenderMode = currentRenderMode;
                    // Force rebuild of all inspectors to update UIDocument's inspector
                    ActiveEditorTracker.sharedTracker.ForceRebuild();
                }
            }
        }
    }

    // MARK: Build

    void RunNpmCommand(string workingDir, string arguments, Action onSuccess, Action<int> onFailure) {
        try {
            var startInfo = OneJSWslHelper.CreateNpmProcessStartInfo(workingDir, arguments, GetNpmCommand());

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
        return _cachedNpmPath = OneJSWslHelper.GetWindowsNpmPath();
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

    // MARK: Utilities

    void RunRemovePanelSettings() {
        serializedObject.Update();
        var prop = serializedObject.FindProperty("_panelSettings");
        if (prop != null && prop.objectReferenceValue != null) {
            Undo.RecordObject(_target, "JSRunner Remove Panel Settings");
            prop.objectReferenceValue = null;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(_target);
        }
    }

    void RunInitializeProject() {
        Undo.RecordObject(_target, "JSRunner Initialize Project");
        _target.PopulateDefaultFiles();
        _target.EnsureProjectFolderAndAssets(EditorPrefs.GetBool(UseSceneNameAsRootFolderPrefKey, true));
        _target.EnsureProjectSetup();
        EditorUtility.SetDirty(_target);
        serializedObject.Update();
        AssetDatabase.Refresh();

        var workingDir = _target.WorkingDirFullPath;
        if (!string.IsNullOrEmpty(workingDir) && File.Exists(Path.Combine(workingDir, "package.json"))) {
            RunNpmCommand(workingDir, "install", onSuccess: () => {
                RunNpmCommand(workingDir, "run build", onSuccess: () => {
                    AssetDatabase.Refresh();
                    Debug.Log("[JSRunner] Project initialized. Working directory, default files, node_modules, and build created.");
                }, onFailure: (code) => {
                    Debug.LogWarning($"[JSRunner] Build step failed (exit code {code}). Project and node_modules are ready; you can run 'npm run build' manually.");
                });
            }, onFailure: (code) => {
                Debug.LogWarning($"[JSRunner] npm install failed (exit code {code}). Default files and folder were created.");
            });
        } else {
            Debug.Log("[JSRunner] Project initialized. Working directory and default files created.");
        }
    }

    void OpenWorkingDirectory() {
        var path = _target.WorkingDirFullPath;
        if (Directory.Exists(path)) EditorUtility.RevealInFinder(path);
        else Debug.LogWarning($"[JSRunner] Directory not found: {path}");
    }

    void ShowOpenTerminalContextMenu(ContextClickEvent evt) {
        if (!OneJSWslHelper.IsWslInstalled) return;

        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Windows (cmd)"), !OneJSWslHelper.UseWsl, () => OneJSWslHelper.UseWsl = false);
        menu.AddItem(new GUIContent("WSL (bash)"), OneJSWslHelper.UseWsl, () => OneJSWslHelper.UseWsl = true);
        menu.ShowAsContext();
    }

    void ShowOpenCodeEditorContextMenu(ContextClickEvent evt) {
        var menu = new GenericMenu();
        var currentPath = EditorPrefs.GetString(CodeEditorPathPrefKey, null);
        var useDefault = string.IsNullOrEmpty(currentPath);

        menu.AddItem(new GUIContent("Unity Default Editor"), useDefault, () => {
            EditorPrefs.DeleteKey(CodeEditorPathPrefKey);
        });
        menu.AddSeparator("");

        var editors = GetAvailableScriptEditors();
        if (editors != null && editors.Count > 0) {
            foreach (var kv in editors) {
                var path = kv.Key;
                var displayName = kv.Value;
                if (string.IsNullOrEmpty(displayName))
                    displayName = path;
                var isSelected = path == currentPath;
                menu.AddItem(new GUIContent(displayName), isSelected, () => {
                    EditorPrefs.SetString(CodeEditorPathPrefKey, path);
                });
            }
        }

        menu.ShowAsContext();
    }

    static Dictionary<string, string> GetAvailableScriptEditors() {
        try {
            var codeEditor = CodeEditor.Editor;
            if (codeEditor != null)
                return codeEditor.GetFoundScriptEditorPaths();
        } catch { }
        return null;
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
        if (OneJSWslHelper.UseWsl) {
            var wslPath = OneJSWslHelper.ToWslPath(path);
            Process.Start(new ProcessStartInfo {
                FileName = "wsl.exe",
                Arguments = $"--cd \"{wslPath}\"",
                UseShellExecute = true
            });
        } else {
            Process.Start(new ProcessStartInfo {
                FileName = "cmd.exe",
                Arguments = $"/K cd /d \"{path}\"",
                UseShellExecute = true
            });
        }
#elif UNITY_EDITOR_LINUX
        try { Process.Start("gnome-terminal", $"--working-directory=\"{path}\""); }
        catch {
            try { Process.Start("konsole", $"--workdir \"{path}\""); }
            catch { Process.Start("xterm", $"-e 'cd \"{path}\" && bash'"); }
        }
#endif
    }

    void OpenCodeEditor(bool singleInstance = false) {
        var path = _target.WorkingDirFullPath;
        if (!Directory.Exists(path)) {
            Debug.LogWarning($"[JSRunner] Directory not found: {path}");
            return;
        }

        var fullPath = Path.GetFullPath(path);
        var entryFilePath = Path.Combine(fullPath, "index.tsx");
        var openEntryFile = File.Exists(entryFilePath);

        var editorPath = EditorPrefs.GetString(CodeEditorPathPrefKey, null);
        if (string.IsNullOrEmpty(editorPath))
            editorPath = CodeEditor.CurrentEditorPath;

        if (!string.IsNullOrEmpty(editorPath) && File.Exists(editorPath)) {
            try {
                string args;
                if (singleInstance) {
                    // Open only the document/folder in the current window (new tab), don't open project root
                    var pathToOpen = openEntryFile ? entryFilePath : fullPath;
                    args = "--reuse-window " + CodeEditor.QuoteForProcessStart(pathToOpen);
                } else {
                    args = CodeEditor.QuoteForProcessStart(fullPath);
                    if (openEntryFile)
                        args += " " + CodeEditor.QuoteForProcessStart(entryFilePath);
                }
                if (CodeEditor.OSOpenFile(editorPath, args))
                    return;
            } catch (Exception ex) {
                Debug.LogWarning($"[JSRunner] Failed to open code editor: {ex.Message}");
            }
        }

        OpenCodeEditorFallback(fullPath, entryFilePath, openEntryFile, singleInstance);
    }

    void OpenCodeEditorFallback(string fullPath, string entryFilePath, bool openEntryFile, bool singleInstance) {
        var reuseFlag = singleInstance ? " -r" : "";
        string pathToOpen = singleInstance ? (openEntryFile ? entryFilePath : fullPath) : fullPath;
        string pathToOpen2 = (!singleInstance && openEntryFile) ? entryFilePath : null;
#if UNITY_EDITOR_WIN
        var codePath = GetCodeExecutablePathOnWindows();
        if (!string.IsNullOrEmpty(codePath)) {
            try {
                var args = (singleInstance ? "--reuse-window " : "") + $"\"{pathToOpen}\"";
                if (pathToOpen2 != null)
                    args += " \"" + pathToOpen2 + "\"";
                Process.Start(new ProcessStartInfo {
                    FileName = codePath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                return;
            } catch { }
        }
        var cmdArgs = $"/C code{reuseFlag} \"{pathToOpen}\"";
        if (pathToOpen2 != null)
            cmdArgs += " \"" + pathToOpen2 + "\"";
        Process.Start(new ProcessStartInfo {
            FileName = "cmd.exe",
            Arguments = cmdArgs,
            UseShellExecute = true,
            CreateNoWindow = true
        });
#elif UNITY_EDITOR_OSX
        var pathArg = ShellEscape(pathToOpen);
        var cmd = pathToOpen2 != null
            ? $"code -n{reuseFlag} -- '{pathArg}' '{ShellEscape(pathToOpen2)}'"
            : $"code -n{reuseFlag} -- '{pathArg}'";
        if (!LaunchViaLoginShell(cmd)) {
            try {
                var openArgs = (singleInstance ? "--reuse-window " : "") + $"\"{pathToOpen}\"";
                if (pathToOpen2 != null)
                    openArgs += " \"" + pathToOpen2 + "\"";
                Process.Start("open", $"-n -b com.microsoft.VSCode --args {openArgs}");
            } catch (Exception ex) {
                Debug.LogError($"[JSRunner] Failed to open code editor: {ex.Message}");
            }
        }
#elif UNITY_EDITOR_LINUX
        var linuxCmd = pathToOpen2 != null
            ? $"code -n{reuseFlag} \"{pathToOpen}\" \"{pathToOpen2}\""
            : $"code -n{reuseFlag} \"{pathToOpen}\"";
        LaunchViaLoginShell(linuxCmd);
#endif
    }

#if UNITY_EDITOR_WIN
    string GetCodeExecutablePathOnWindows() {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        string[] searchPaths = {
            Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(programFiles, "Microsoft VS Code", "Code.exe"),
        };

        foreach (var p in searchPaths) {
            if (File.Exists(p)) return p;
        }

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
