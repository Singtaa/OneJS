using System;
using System.Diagnostics;
using System.IO;
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
    const string FoldoutPrefKeyPrefix = "JSRunner.Foldout.";

    JSRunner _target;
    bool _buildInProgress;
    string _buildOutput;

    // UI Elements that need updating
    Label _statusLabel;
    Label _liveReloadStatusLabel;
    Label _reloadCountLabel;
    Label _entryFileStatusLabel;
    Button _reloadButton;
    Button _buildButton;
    HelpBox _buildOutputBox;
    VisualElement _liveReloadInfo;

    // Type generation
    Button _generateTypesButton;
    Label _typeGenStatusLabel;

    void OnEnable() {
        _target = (JSRunner)target;
        EditorApplication.update += UpdateDynamicUI;
    }

    void OnDisable() {
        EditorApplication.update -= UpdateDynamicUI;
    }

    public override VisualElement CreateInspectorGUI() {
        var root = new VisualElement();

        // Status - always visible at top
        root.Add(CreateStatusSection());

        // Collapsible config sections
        root.Add(CreateFoldoutSection("Settings", BuildSettingsContent, defaultOpen: true));
        root.Add(CreateFoldoutSection("UI Panel", BuildUIPanelContent));
        root.Add(CreateFoldoutSection("Live Reload", BuildLiveReloadContent, defaultOpen: true));
        root.Add(CreateFoldoutSection("Build Settings", BuildBuildSettingsContent));
        root.Add(CreateFoldoutSection("Type Generation", BuildTypeGenerationContent));
        root.Add(CreateFoldoutSection("Scaffolding", BuildScaffoldingContent));
        root.Add(CreateFoldoutSection("Cartridges", BuildCartridgesContent));
        root.Add(CreateFoldoutSection("Advanced", BuildAdvancedContent));

        // Actions - always visible at bottom
        root.Add(CreateActionsSection());

        return root;
    }

    // MARK: Section Factory

    /// <summary>
    /// Creates a collapsible foldout section with persistent state.
    /// </summary>
    VisualElement CreateFoldoutSection(string title, Action<VisualElement> buildContent, bool defaultOpen = false) {
        var prefKey = FoldoutPrefKeyPrefix + title.Replace(" ", "");
        var isOpen = EditorPrefs.GetBool(prefKey, defaultOpen);

        var foldout = new Foldout {
            text = title,
            value = isOpen
        };
        foldout.style.marginBottom = 4;
        foldout.style.marginTop = 2;

        // Persist state when toggled
        foldout.RegisterValueChangedCallback(evt => {
            EditorPrefs.SetBool(prefKey, evt.newValue);
        });

        // Build content inside foldout
        var content = new VisualElement();
        content.style.marginLeft = 4;
        buildContent(content);
        foldout.Add(content);

        return foldout;
    }

    /// <summary>
    /// Creates a styled box container.
    /// </summary>
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

    // MARK: Section Content Builders

    void BuildSettingsContent(VisualElement container) {
        container.Add(new PropertyField(serializedObject.FindProperty("_workingDir")));
        container.Add(new PropertyField(serializedObject.FindProperty("_entryFile")));
    }

    void BuildBuildSettingsContent(VisualElement container) {
        var embeddedScriptProp = serializedObject.FindProperty("_embeddedScript");
        var streamingAssetsPathProp = serializedObject.FindProperty("_streamingAssetsPath");

        var embeddedScriptField = new PropertyField(embeddedScriptProp);
        var streamingAssetsPathField = new PropertyField(streamingAssetsPathProp);
        container.Add(embeddedScriptField);
        container.Add(streamingAssetsPathField);

        var helpBox = new HelpBox(
            $"Bundle will be auto-copied to StreamingAssets/{streamingAssetsPathProp.stringValue} during build.",
            HelpBoxMessageType.Info
        );
        helpBox.style.marginTop = 4;
        helpBox.style.display = embeddedScriptProp.objectReferenceValue == null ? DisplayStyle.Flex : DisplayStyle.None;
        container.Add(helpBox);

        embeddedScriptField.RegisterValueChangeCallback(_ =>
            helpBox.style.display = embeddedScriptProp.objectReferenceValue == null ? DisplayStyle.Flex : DisplayStyle.None);

        streamingAssetsPathField.RegisterValueChangeCallback(_ =>
            helpBox.text = $"Bundle will be auto-copied to StreamingAssets/{streamingAssetsPathProp.stringValue} during build.");
    }

    void BuildScaffoldingContent(VisualElement container) {
        var field = new PropertyField(serializedObject.FindProperty("_defaultFiles"));
        field.label = "Default Files";
        container.Add(field);

        // Button to repopulate from templates
        var buttonRow = CreateRow();
        buttonRow.style.marginTop = 5;

        var repopulateButton = new Button(() => {
            _target.PopulateDefaultFiles();
            serializedObject.Update();
            EditorUtility.SetDirty(_target);
        }) { text = "Reset to Defaults" };
        repopulateButton.style.height = 22;
        repopulateButton.style.flexGrow = 1;
        repopulateButton.tooltip = "Repopulate the default files list from OneJS templates";
        buttonRow.Add(repopulateButton);

        container.Add(buttonRow);
    }

    VisualElement _cartridgeListContainer;

    void BuildCartridgesContent(VisualElement container) {
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
            RebuildCartridgeList(); // Rebuild to update status
        });
        row.Add(objectField);

        // Status indicator
        var statusLabel = new Label();
        statusLabel.style.width = 70;
        statusLabel.style.fontSize = 10;
        statusLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        statusLabel.style.marginRight = 4;

        if (cartridge != null && !string.IsNullOrEmpty(cartridge.Slug)) {
            var cartridgePath = _target.GetCartridgePath(cartridge);
            bool isExtracted = Directory.Exists(cartridgePath);

            if (isExtracted) {
                statusLabel.text = "✓ Extracted";
                statusLabel.style.color = new Color(0.4f, 0.8f, 0.4f);
            } else {
                statusLabel.text = "○ Not extracted";
                statusLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            }
        } else {
            statusLabel.text = cartridge == null ? "" : "⚠ No slug";
            statusLabel.style.color = new Color(0.8f, 0.6f, 0.2f);
        }
        row.Add(statusLabel);

        // Extract button
        var extractBtn = new Button(() => ExtractCartridge(cartridge, index)) { text = "E" };
        extractBtn.style.width = 24;
        extractBtn.style.height = 20;
        extractBtn.tooltip = "Extract cartridge files to @cartridges/" + (cartridge?.Slug ?? "");
        extractBtn.SetEnabled(cartridge != null && !string.IsNullOrEmpty(cartridge?.Slug));
        row.Add(extractBtn);

        // Delete button
        var deleteBtn = new Button(() => DeleteCartridge(cartridge, index)) { text = "D" };
        deleteBtn.style.width = 24;
        deleteBtn.style.height = 20;
        deleteBtn.style.marginLeft = 2;
        deleteBtn.tooltip = "Delete extracted cartridge folder";
        bool canDelete = cartridge != null && !string.IsNullOrEmpty(cartridge?.Slug) &&
                         Directory.Exists(_target.GetCartridgePath(cartridge));
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
            // Delete existing if overwriting
            if (alreadyExists) {
                Directory.Delete(destPath, true);
            }

            Directory.CreateDirectory(destPath);

            // Extract files
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

            // Generate TypeScript definitions
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

        // Count files
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

        // Clear the reference first (required for object references)
        cartridgesProp.GetArrayElementAtIndex(index).objectReferenceValue = null;
        cartridgesProp.DeleteArrayElementAtIndex(index);
        serializedObject.ApplyModifiedProperties();
        RebuildCartridgeList();
    }

    void ExtractAllCartridges() {
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
                if (Directory.Exists(_target.GetCartridgePath(c))) existingCount++;
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
        var cartridges = _target.Cartridges;
        if (cartridges == null || cartridges.Count == 0) {
            EditorUtility.DisplayDialog("No Cartridges", "No cartridges in list.", "OK");
            return;
        }

        int existingCount = 0;
        foreach (var c in cartridges) {
            if (c != null && !string.IsNullOrEmpty(c.Slug) && Directory.Exists(_target.GetCartridgePath(c))) {
                existingCount++;
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

    void BuildAdvancedContent(VisualElement container) {
        var stylesheetsField = new PropertyField(serializedObject.FindProperty("_stylesheets"));
        stylesheetsField.label = "Stylesheets";
        container.Add(stylesheetsField);

        var preloadsField = new PropertyField(serializedObject.FindProperty("_preloads"));
        preloadsField.label = "Preloads";
        container.Add(preloadsField);

        var globalsField = new PropertyField(serializedObject.FindProperty("_globals"));
        globalsField.label = "Globals";
        container.Add(globalsField);
    }

    // MARK: Complex Sections (with special logic)

    VisualElement CreateStatusSection() {
        var container = CreateStyledBox();
        container.style.marginTop = 2;
        container.style.marginBottom = 10;

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

        // Entry file status
        var entryRow = CreateRow();
        entryRow.style.marginTop = 4;
        entryRow.Add(CreateLabel("Entry File:", 70));
        _entryFileStatusLabel = new Label("Checking...");
        entryRow.Add(_entryFileStatusLabel);
        container.Add(entryRow);

        return container;
    }

    void BuildUIPanelContent(VisualElement container) {
        var panelSettingsProp = serializedObject.FindProperty("_panelSettings");
        var panelSettingsField = new PropertyField(panelSettingsProp, "Panel Settings");
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

        // Help box
        var helpBox = new HelpBox(
            "No Panel Settings asset assigned. A PanelSettings will be created at runtime using the settings above.",
            HelpBoxMessageType.Info
        );
        helpBox.style.marginTop = 4;
        helpBox.style.display = panelSettingsProp.objectReferenceValue == null ? DisplayStyle.Flex : DisplayStyle.None;
        panelSettingsField.RegisterValueChangeCallback(_ =>
            helpBox.style.display = panelSettingsProp.objectReferenceValue == null ? DisplayStyle.Flex : DisplayStyle.None);
        container.Add(helpBox);
    }

    void BuildLiveReloadContent(VisualElement container) {
        var liveReloadProp = serializedObject.FindProperty("_liveReload");
        var liveReloadField = new PropertyField(liveReloadProp);
        container.Add(liveReloadField);

        var pollIntervalContainer = new VisualElement { style = { marginLeft = 15 } };
        pollIntervalContainer.Add(new PropertyField(serializedObject.FindProperty("_pollInterval")));
        container.Add(pollIntervalContainer);

        pollIntervalContainer.style.display = liveReloadProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None;
        liveReloadField.RegisterValueChangeCallback(_ =>
            pollIntervalContainer.style.display = liveReloadProp.boolValue ? DisplayStyle.Flex : DisplayStyle.None);
    }

    void BuildTypeGenerationContent(VisualElement container) {
        // Assemblies list
        var assembliesField = new PropertyField(serializedObject.FindProperty("_typingAssemblies"));
        assembliesField.label = "Assemblies";
        assembliesField.tooltip = "C# assemblies to generate TypeScript typings for (e.g., 'Assembly-CSharp')";
        container.Add(assembliesField);

        // Auto-generate toggle
        var autoGenerateProp = serializedObject.FindProperty("_autoGenerateTypings");
        var autoGenerateField = new PropertyField(autoGenerateProp);
        autoGenerateField.label = "Auto Generate";
        container.Add(autoGenerateField);

        // Output path
        var outputPathProp = serializedObject.FindProperty("_typingsOutputPath");
        var outputPathField = new PropertyField(outputPathProp);
        outputPathField.label = "Output Path";
        container.Add(outputPathField);

        // Generate button row
        var buttonRow = CreateRow();
        buttonRow.style.marginTop = 5;

        _generateTypesButton = new Button(GenerateTypings) { text = "Generate Types Now" };
        _generateTypesButton.style.height = 24;
        _generateTypesButton.style.flexGrow = 1;
        buttonRow.Add(_generateTypesButton);

        container.Add(buttonRow);

        // Status label
        _typeGenStatusLabel = new Label();
        _typeGenStatusLabel.style.marginTop = 4;
        _typeGenStatusLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
        _typeGenStatusLabel.style.fontSize = 10;
        container.Add(_typeGenStatusLabel);

        // Update status label
        UpdateTypeGenStatus();
    }

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
        if (File.Exists(outputPath)) {
            var lastWrite = File.GetLastWriteTime(outputPath);
            _typeGenStatusLabel.text = $"Output: {_target.TypingsOutputPath} (last updated: {lastWrite:g})";
        } else {
            _typeGenStatusLabel.text = $"Output: {_target.TypingsOutputPath} (not yet generated)";
        }
    }

    VisualElement CreateActionsSection() {
        var container = new VisualElement { style = { marginBottom = 10 } };

        var header = new Label("Actions");
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.marginBottom = 4;
        container.Add(header);

        // Row 1: Reload and Build
        var row1 = CreateRow();
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

        // Row 2: Open Folder and Terminal
        var row2 = CreateRow();

        var openFolderButton = new Button(OpenWorkingDirectory) { text = "Open Folder" };
        openFolderButton.style.height = 24;
        openFolderButton.style.flexGrow = 1;
        row2.Add(openFolderButton);

        var openTerminalButton = new Button(OpenTerminal) { text = "Open Terminal" };
        openTerminalButton.style.height = 24;
        openTerminalButton.style.flexGrow = 1;
        row2.Add(openTerminalButton);

        container.Add(row2);

        _buildOutputBox = new HelpBox("", HelpBoxMessageType.Info);
        _buildOutputBox.style.marginTop = 5;
        _buildOutputBox.style.display = DisplayStyle.None;
        container.Add(_buildOutputBox);

        return container;
    }

    // MARK: UI Helpers

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

        // Entry file status
        var fileExists = File.Exists(_target.EntryFileFullPath);
        _entryFileStatusLabel.text = fileExists ? "Found" : "Not Found";
        _entryFileStatusLabel.style.color = fileExists ? Color.white : new Color(0.8f, 0.4f, 0.4f);

        // Buttons
        _reloadButton.SetEnabled(Application.isPlaying && _target.IsRunning);
        _buildButton.SetEnabled(!_buildInProgress);
        _buildButton.text = _buildInProgress ? "Building..." : "Build";

        // Build output
        if (!string.IsNullOrEmpty(_buildOutput)) {
            _buildOutputBox.style.display = DisplayStyle.Flex;
            _buildOutputBox.text = _buildOutput;
        }
    }

    // MARK: Build

    void RunBuild() {
        var workingDir = _target.WorkingDirFullPath;

        if (!Directory.Exists(workingDir)) {
            Debug.LogError($"[JSRunner] Working directory not found: {workingDir}");
            return;
        }

        if (!File.Exists(Path.Combine(workingDir, "package.json"))) {
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

            var existingPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            startInfo.EnvironmentVariables["PATH"] = nodeBinDir + Path.PathSeparator + existingPath;

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            process.OutputDataReceived += (_, args) => {
                if (!string.IsNullOrEmpty(args.Data)) Debug.Log($"[Build] {args.Data}");
            };
            process.ErrorDataReceived += (_, args) => {
                if (!string.IsNullOrEmpty(args.Data)) Debug.LogError($"[Build] {args.Data}");
            };
            process.Exited += (_, _) => {
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

        // Count files to give user an idea of what will be deleted
        int fileCount = 0;
        int dirCount = 0;
        try {
            fileCount = Directory.GetFiles(workingDir, "*", SearchOption.AllDirectories).Length;
            dirCount = Directory.GetDirectories(workingDir, "*", SearchOption.AllDirectories).Length;
        } catch { }

        var confirmed = EditorUtility.DisplayDialog(
            "⚠️ Reset Working Directory",
            $"WARNING: This will PERMANENTLY DELETE everything in:\n\n" +
            $"{workingDir}\n\n" +
            $"This includes:\n" +
            $"• {fileCount} files\n" +
            $"• {dirCount} folders\n" +
            $"• All your source code, node_modules, and configuration\n\n" +
            $"After deletion, the directory will be re-scaffolded with default files.\n\n" +
            $"THIS CANNOT BE UNDONE!\n\n" +
            $"Are you absolutely sure?",
            "DELETE EVERYTHING",
            "Cancel"
        );

        if (!confirmed) return;

        // Double confirmation for safety
        var doubleConfirmed = EditorUtility.DisplayDialog(
            "🛑 Final Warning",
            "You are about to delete all files in the working directory.\n\n" +
            "Type 'DELETE' mentally and click confirm only if you're certain.",
            "Yes, Delete Everything",
            "Cancel"
        );

        if (!doubleConfirmed) return;

        try {
            // Delete all contents but keep the directory
            foreach (var file in Directory.GetFiles(workingDir)) {
                File.Delete(file);
            }
            foreach (var dir in Directory.GetDirectories(workingDir)) {
                Directory.Delete(dir, true);
            }

            Debug.Log($"[JSRunner] Deleted all contents from: {workingDir}");

            // Trigger re-scaffolding by calling Initialize (which calls ScaffoldDefaultFiles)
            // Since ScaffoldDefaultFiles is private, we need to use reflection or make it accessible
            var method = typeof(JSRunner).GetMethod("ScaffoldDefaultFiles",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method?.Invoke(runner, null);

            Debug.Log("[JSRunner] Re-scaffolded default files");

            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "Reset Complete",
                "Working directory has been reset and re-scaffolded with default files.\n\n" +
                "Run 'npm install' to restore dependencies.",
                "OK"
            );
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
