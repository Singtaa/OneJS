using UnityEditor;
using UnityEngine;

namespace OneJS.Editor {
    [CustomPropertyDrawer(typeof(PairMappingAttribute))]
    public class PairMappingDrawer : PropertyDrawer {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            var pairMapping = (PairMappingAttribute)attribute;

            EditorGUI.BeginProperty(position, label, property);
            var signWidth = 20;
            var halfWidth = (position.width - signWidth) / 2;

            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            var amountRect = new Rect(position.x, position.y, halfWidth, position.height);
            var unitRect = new Rect(position.x + halfWidth + signWidth, position.y, halfWidth, position.height);
            var signRect = new Rect(position.x + halfWidth, position.y, signWidth, position.height);

            EditorGUI.PropertyField(amountRect, property.FindPropertyRelative(pairMapping.@from), GUIContent.none);
            EditorGUI.LabelField(signRect, ">", new GUIStyle() { alignment = TextAnchor.MiddleCenter });
            EditorGUI.PropertyField(unitRect, property.FindPropertyRelative(pairMapping.to), GUIContent.none);

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }
    }
}