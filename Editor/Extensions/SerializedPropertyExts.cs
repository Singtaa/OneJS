using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace OneJS.Editor {
    public static class SerializedPropertyExts {
        public static string[] ToStringArray(this SerializedProperty property) {
            if (property == null) {
                Debug.LogError("SerializedProperty is null");
                return new string[0];
            }

            if (!property.isArray) {
                Debug.LogError("SerializedProperty is not an array");
                return new string[0];
            }

            List<string> result = new List<string>();

            for (int i = 0; i < property.arraySize; i++) {
                SerializedProperty element = property.GetArrayElementAtIndex(i);

                if (element.propertyType == SerializedPropertyType.String) {
                    result.Add(element.stringValue);
                } else {
                    Debug.LogWarning($"Element at index {i} is not a string. Skipping.");
                }
            }

            return result.ToArray();
        }
    }
}