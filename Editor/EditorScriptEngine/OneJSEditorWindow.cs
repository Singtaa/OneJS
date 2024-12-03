using System;
using UnityEditor;
using UnityEngine;

namespace OneJS.Editor {
    public class OneJSEditorWindow : EditorWindow {
        public event Action OnTeardown;
        
        void OnDestroy() {
            OnTeardown?.Invoke();
        }
    }
}