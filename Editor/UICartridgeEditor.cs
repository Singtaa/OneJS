using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[CustomEditor(typeof(UICartridge))]
public class UICartridgeEditor : Editor {
    UICartridge _target;

    // List containers
    VisualElement _filesListContainer;
    VisualElement _objectsListContainer;

    // Path preview
    Label _pathPreviewLabel;

    void OnEnable() {
        _target = (UICartridge)target;
    }

    public override VisualElement CreateInspectorGUI() {
        var root = new VisualElement();

        // Header with cartridge icon feel
        root.Add(CreateHeaderSection());

        // Identity section
        root.Add(CreateIdentitySection());

        // Files section
        root.Add(CreateFilesSection());

        // Objects section
        root.Add(CreateObjectsSection());

        return root;
    }

    // MARK: Header Section

    VisualElement CreateHeaderSection() {
        var container = new VisualElement();
        container.style.backgroundColor = new Color(0.18f, 0.28f, 0.38f);
        container.style.SetBorderRadius(4);
        container.style.paddingTop = container.style.paddingBottom = 12;
        container.style.paddingLeft = container.style.paddingRight = 14;
        container.style.marginBottom = 10;
        container.style.marginTop = 10;

        // Path preview
        _pathPreviewLabel = new Label();
        _pathPreviewLabel.style.fontSize = 11;
        _pathPreviewLabel.style.color = new Color(0.7f, 0.85f, 1f);
        _pathPreviewLabel.style.whiteSpace = WhiteSpace.Normal;
        container.Add(_pathPreviewLabel);

        UpdatePathPreview();

        return container;
    }

    void UpdatePathPreview() {
        if (_pathPreviewLabel == null) return;

        var slug = _target.Slug;
        var ns = _target.Namespace;

        if (string.IsNullOrEmpty(slug)) {
            _pathPreviewLabel.text = "Set a slug to see the extraction path";
            _pathPreviewLabel.style.color = new Color(0.8f, 0.6f, 0.4f);
        } else {
            var relativePath = _target.RelativePath;
            _pathPreviewLabel.text = $"@cartridges/{relativePath}/";
            _pathPreviewLabel.style.color = new Color(0.7f, 0.85f, 1f);

            if (!string.IsNullOrEmpty(ns)) {
                _pathPreviewLabel.text += $"\n__cart('@{ns}/{slug}')";
            } else {
                _pathPreviewLabel.text += $"\n__cart('{slug}')";
            }
        }
    }

    // MARK: Identity Section

    VisualElement CreateIdentitySection() {
        var container = CreateSectionContainer("Identity");

        // Namespace field
        var namespaceRow = CreateFieldRow();
        var namespaceProp = serializedObject.FindProperty("_namespace");
        var namespaceField = new TextField("Namespace");
        namespaceField.value = namespaceProp.stringValue;
        namespaceField.style.flexGrow = 1;
        namespaceField.tooltip = "Optional namespace for organizing cartridges (e.g., 'myCompany')";
        namespaceField.RegisterValueChangedCallback(evt => {
            namespaceProp.stringValue = evt.newValue;
            serializedObject.ApplyModifiedProperties();
            UpdatePathPreview();
        });
        namespaceRow.Add(namespaceField);

        var nsHint = new Label("optional");
        nsHint.style.color = new Color(0.5f, 0.5f, 0.5f);
        nsHint.style.fontSize = 10;
        nsHint.style.marginLeft = 6;
        nsHint.style.unityTextAlign = TextAnchor.MiddleCenter;
        namespaceRow.Add(nsHint);

        container.Add(namespaceRow);

        // Slug field
        var slugProp = serializedObject.FindProperty("_slug");
        var slugField = new TextField("Slug");
        slugField.value = slugProp.stringValue;
        slugField.tooltip = "Identifier used for folder name and JS access (e.g., 'foo-bar')";
        slugField.RegisterValueChangedCallback(evt => {
            slugProp.stringValue = evt.newValue;
            serializedObject.ApplyModifiedProperties();
            UpdatePathPreview();
        });
        container.Add(slugField);

        // Display Name field
        var displayNameProp = serializedObject.FindProperty("_displayName");
        var displayNameField = new TextField("Display Name");
        displayNameField.value = displayNameProp.stringValue;
        displayNameField.tooltip = "Human-readable name (optional, defaults to slug)";
        displayNameField.RegisterValueChangedCallback(evt => {
            displayNameProp.stringValue = evt.newValue;
            serializedObject.ApplyModifiedProperties();
        });
        container.Add(displayNameField);

        // Description field
        var descProp = serializedObject.FindProperty("_description");
        var descField = new TextField("Description");
        descField.value = descProp.stringValue;
        descField.multiline = true;
        descField.style.minHeight = 50;
        descField.tooltip = "Description of what this cartridge provides";
        descField.RegisterValueChangedCallback(evt => {
            descProp.stringValue = evt.newValue;
            serializedObject.ApplyModifiedProperties();
        });
        container.Add(descField);

        return container;
    }

    // MARK: Files Section

    VisualElement CreateFilesSection() {
        var container = CreateSectionContainer("Files");

        // Header with add button
        var headerRow = CreateRow();
        headerRow.style.marginBottom = 6;

        var headerLabel = new Label("Cartridge files to extract");
        headerLabel.style.flexGrow = 1;
        headerLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
        headerLabel.style.fontSize = 11;
        headerRow.Add(headerLabel);

        var addButton = CreateAddButton(() => {
            var prop = serializedObject.FindProperty("_files");
            prop.arraySize++;
            serializedObject.ApplyModifiedProperties();
            RebuildFilesList();
        });
        addButton.tooltip = "Add a file entry";
        headerRow.Add(addButton);

        container.Add(headerRow);

        // Files list
        _filesListContainer = new VisualElement();
        container.Add(_filesListContainer);
        RebuildFilesList();

        // Help text
        var helpLabel = new Label("path \u2190 TextAsset content");
        helpLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
        helpLabel.style.fontSize = 10;
        helpLabel.style.marginTop = 4;
        container.Add(helpLabel);

        return container;
    }

    void RebuildFilesList() {
        if (_filesListContainer == null) return;

        _filesListContainer.Clear();
        serializedObject.Update();

        var prop = serializedObject.FindProperty("_files");

        if (prop.arraySize == 0) {
            var emptyLabel = new Label("No files. Click + to add one.");
            emptyLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            emptyLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            emptyLabel.style.paddingTop = 4;
            emptyLabel.style.paddingBottom = 4;
            _filesListContainer.Add(emptyLabel);
            return;
        }

        for (int i = 0; i < prop.arraySize; i++) {
            var row = CreateFileItemRow(prop, i);
            _filesListContainer.Add(row);
        }
    }

    VisualElement CreateFileItemRow(SerializedProperty arrayProp, int index) {
        var elementProp = arrayProp.GetArrayElementAtIndex(index);
        var pathProp = elementProp.FindPropertyRelative("path");
        var contentProp = elementProp.FindPropertyRelative("content");

        var row = CreateListItemRow();

        // Path field
        var pathField = new TextField();
        pathField.value = pathProp.stringValue;
        pathField.style.width = 140;
        pathField.RegisterValueChangedCallback(evt => {
            pathProp.stringValue = evt.newValue;
            serializedObject.ApplyModifiedProperties();
        });
        row.Add(pathField);

        // Arrow
        var arrow = new Label("\u2190");
        arrow.style.marginLeft = 6;
        arrow.style.marginRight = 6;
        arrow.style.color = new Color(0.5f, 0.5f, 0.5f);
        row.Add(arrow);

        // Content field
        var contentField = new ObjectField();
        contentField.objectType = typeof(TextAsset);
        contentField.value = contentProp.objectReferenceValue as TextAsset;
        contentField.style.flexGrow = 1;
        contentField.RegisterValueChangedCallback(evt => {
            contentProp.objectReferenceValue = evt.newValue;
            serializedObject.ApplyModifiedProperties();
        });
        row.Add(contentField);

        // Remove button
        var removeBtn = CreateRemoveButton(() => {
            arrayProp.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            RebuildFilesList();
        });
        row.Add(removeBtn);

        return row;
    }

    // MARK: Objects Section

    VisualElement CreateObjectsSection() {
        var container = CreateSectionContainer("Objects");

        // Header with add button
        var headerRow = CreateRow();
        headerRow.style.marginBottom = 6;

        var headerLabel = new Label("Unity objects to inject as globals");
        headerLabel.style.flexGrow = 1;
        headerLabel.style.color = new Color(0.7f, 0.7f, 0.7f);
        headerLabel.style.fontSize = 11;
        headerRow.Add(headerLabel);

        var addButton = CreateAddButton(() => {
            var prop = serializedObject.FindProperty("_objects");
            prop.arraySize++;
            serializedObject.ApplyModifiedProperties();
            RebuildObjectsList();
        });
        addButton.tooltip = "Add an object entry";
        headerRow.Add(addButton);

        container.Add(headerRow);

        // Objects list
        _objectsListContainer = new VisualElement();
        container.Add(_objectsListContainer);
        RebuildObjectsList();

        // Help text
        var helpLabel = new Label("key \u2192 __cart('slug').{key}");
        helpLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
        helpLabel.style.fontSize = 10;
        helpLabel.style.marginTop = 4;
        container.Add(helpLabel);

        return container;
    }

    void RebuildObjectsList() {
        if (_objectsListContainer == null) return;

        _objectsListContainer.Clear();
        serializedObject.Update();

        var prop = serializedObject.FindProperty("_objects");

        if (prop.arraySize == 0) {
            var emptyLabel = new Label("No objects. Click + to add one.");
            emptyLabel.style.color = new Color(0.5f, 0.5f, 0.5f);
            emptyLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            emptyLabel.style.paddingTop = 4;
            emptyLabel.style.paddingBottom = 4;
            _objectsListContainer.Add(emptyLabel);
            return;
        }

        for (int i = 0; i < prop.arraySize; i++) {
            var row = CreateObjectItemRow(prop, i);
            _objectsListContainer.Add(row);
        }
    }

    VisualElement CreateObjectItemRow(SerializedProperty arrayProp, int index) {
        var elementProp = arrayProp.GetArrayElementAtIndex(index);
        var keyProp = elementProp.FindPropertyRelative("key");
        var valueProp = elementProp.FindPropertyRelative("value");

        var row = CreateListItemRow();

        // Key field
        var keyField = new TextField();
        keyField.value = keyProp.stringValue;
        keyField.style.width = 120;
        keyField.RegisterValueChangedCallback(evt => {
            keyProp.stringValue = evt.newValue;
            serializedObject.ApplyModifiedProperties();
        });
        row.Add(keyField);

        // Arrow
        var arrow = new Label("\u2192");
        arrow.style.marginLeft = 6;
        arrow.style.marginRight = 6;
        arrow.style.color = new Color(0.5f, 0.5f, 0.5f);
        row.Add(arrow);

        // Value field
        var valueField = new ObjectField();
        valueField.objectType = typeof(Object);
        valueField.value = valueProp.objectReferenceValue;
        valueField.style.flexGrow = 1;
        valueField.RegisterValueChangedCallback(evt => {
            valueProp.objectReferenceValue = evt.newValue;
            serializedObject.ApplyModifiedProperties();
        });
        row.Add(valueField);

        // Remove button
        var removeBtn = CreateRemoveButton(() => {
            arrayProp.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            RebuildObjectsList();
        });
        row.Add(removeBtn);

        return row;
    }

    // MARK: UI Helpers

    VisualElement CreateSectionContainer(string title) {
        var container = new VisualElement();
        container.style.backgroundColor = new Color(0.22f, 0.22f, 0.22f);
        container.style.SetBorderWidth(1);
        container.style.SetBorderColor(new Color(0.14f, 0.14f, 0.14f));
        container.style.SetBorderRadius(4);
        container.style.paddingTop = container.style.paddingBottom = 10;
        container.style.paddingLeft = container.style.paddingRight = 12;
        container.style.marginBottom = 8;

        var header = new Label(title);
        header.style.unityFontStyleAndWeight = FontStyle.Bold;
        header.style.fontSize = 12;
        header.style.marginBottom = 8;
        header.style.color = new Color(0.9f, 0.9f, 0.9f);
        container.Add(header);

        return container;
    }

    VisualElement CreateRow() {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        return row;
    }

    VisualElement CreateFieldRow() {
        var row = CreateRow();
        row.style.marginBottom = 4;
        return row;
    }

    VisualElement CreateListItemRow() {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 3;
        row.style.paddingTop = row.style.paddingBottom = 3;
        row.style.paddingLeft = row.style.paddingRight = 6;
        row.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
        row.style.SetBorderRadius(3);
        return row;
    }

    Button CreateAddButton(System.Action onClick) {
        var btn = new Button(onClick) { text = "+" };
        btn.style.width = 24;
        btn.style.height = 20;
        btn.style.fontSize = 14;
        btn.style.unityFontStyleAndWeight = FontStyle.Bold;
        btn.style.backgroundColor = new Color(0.2f, 0.4f, 0.3f);
        btn.style.SetBorderRadius(3);
        return btn;
    }

    Button CreateRemoveButton(System.Action onClick) {
        var btn = new Button(onClick) { text = "\u2212" }; // Minus sign
        btn.style.width = 22;
        btn.style.height = 18;
        btn.style.marginLeft = 6;
        btn.style.fontSize = 12;
        btn.style.backgroundColor = new Color(0.4f, 0.2f, 0.2f);
        btn.style.SetBorderRadius(3);
        return btn;
    }
}
