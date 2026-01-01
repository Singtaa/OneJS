using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[CustomPropertyDrawer(typeof(CartridgeObjectEntry))]
public class CartridgeObjectEntryDrawer : PropertyDrawer {
    public override VisualElement CreatePropertyGUI(SerializedProperty property) {
        var container = new VisualElement();
        container.style.flexDirection = FlexDirection.Row;
        container.style.alignItems = Align.Center;
        container.style.marginBottom = 2;

        var keyProp = property.FindPropertyRelative("key");
        var valueProp = property.FindPropertyRelative("value");

        // Get separator from PairDrawerAttribute on parent field
        var separator = GetSeparatorFromParent(property);

        // Key field (text input)
        var keyField = new TextField();
        keyField.style.flexGrow = 1;
        keyField.style.flexBasis = 0;
        keyField.BindProperty(keyProp);
        container.Add(keyField);

        // Separator label
        var separatorLabel = new Label(separator);
        separatorLabel.style.marginLeft = 6;
        separatorLabel.style.marginRight = 6;
        separatorLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        separatorLabel.style.minWidth = 16;
        container.Add(separatorLabel);

        // Value field (object picker)
        var valueField = new ObjectField();
        valueField.objectType = typeof(UnityEngine.Object);
        valueField.allowSceneObjects = true;
        valueField.style.flexGrow = 2;
        valueField.style.flexBasis = 0;
        valueField.BindProperty(valueProp);
        container.Add(valueField);

        return container;
    }

    string GetSeparatorFromParent(SerializedProperty property) {
        // Default separator
        const string defaultSeparator = "→";

        // Property path for array element looks like: "_objects.Array.data[0]"
        // We need to get the field info for "_objects"
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
