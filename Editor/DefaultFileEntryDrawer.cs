using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[CustomPropertyDrawer(typeof(DefaultFileEntry))]
public class DefaultFileEntryDrawer : PropertyDrawer {
    public override VisualElement CreatePropertyGUI(SerializedProperty property) {
        var container = new VisualElement();
        container.style.flexDirection = FlexDirection.Row;
        container.style.alignItems = Align.Center;
        container.style.marginBottom = 2;

        var pathProp = property.FindPropertyRelative("path");
        var contentProp = property.FindPropertyRelative("content");

        // Get separator from PairDrawerAttribute on parent field
        var separator = GetSeparatorFromParent(property);

        // Content field (TextAsset) - shown first since separator is "←"
        var contentField = new ObjectField();
        contentField.objectType = typeof(TextAsset);
        contentField.allowSceneObjects = false;
        contentField.style.flexGrow = 2;
        contentField.style.flexBasis = 0;
        contentField.BindProperty(contentProp);
        container.Add(contentField);

        // Separator label
        var separatorLabel = new Label(separator);
        separatorLabel.style.marginLeft = 6;
        separatorLabel.style.marginRight = 6;
        separatorLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        separatorLabel.style.minWidth = 16;
        container.Add(separatorLabel);

        // Path field (text input)
        var pathField = new TextField();
        pathField.style.flexGrow = 1;
        pathField.style.flexBasis = 0;
        pathField.BindProperty(pathProp);
        container.Add(pathField);

        return container;
    }

    string GetSeparatorFromParent(SerializedProperty property) {
        // Default separator
        const string defaultSeparator = "←";

        // Property path for array element looks like: "_defaultFiles.Array.data[0]"
        // We need to get the field info for "_defaultFiles"
        var path = property.propertyPath;
        var dotIndex = path.IndexOf('.');
        if (dotIndex < 0) return defaultSeparator;

        var fieldName = path.Substring(0, dotIndex);
        var targetObject = property.serializedObject.targetObject;
        var targetType = targetObject.GetType();

        var fieldInfo = targetType.GetField(fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (fieldInfo == null) return defaultSeparator;

        var attr = fieldInfo.GetCustomAttribute<PairDrawerAttribute>();
        return attr?.Separator ?? defaultSeparator;
    }
}
