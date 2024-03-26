using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS {
    [DefaultExecutionOrder(10)]
    public class ScreenMonitor : MonoBehaviour {
        static string[] screenClasses = new[] {
            "onejs-media-sm", "onejs-media-md", "onejs-media-lg", "onejs-media-xl", "onejs-media-2xl"
        };

        public int[] breakpoints = new[] { 640, 768, 1024, 1280, 1536 };
        public bool pollStandaloneScreen;

        UIDocument _uiDocument;
        float _lastScreenWidth;

        void Awake() {
            _uiDocument = GetComponent<UIDocument>();
        }

        void Start() {
            PollScreenChange();
        }

        void Update() {
#if UNITY_EDITOR
            PollScreenChange();
#else
            if (pollStandaloneScreen) {
                PollScreenChange();
            }
#endif
        }

        void PollScreenChange() {
            var width = _uiDocument.rootVisualElement.resolvedStyle.width;
            if (!Mathf.Approximately(_lastScreenWidth, width)) {
                SetRootMediaClass(width);
                _lastScreenWidth = width;
            }
        }

        void SetRootMediaClass(float width) {
            foreach (var sc in screenClasses) {
                _uiDocument.rootVisualElement.RemoveFromClassList(sc);
            }
            for (int i = 0; i < breakpoints.Length; i++) {
                if (screenClasses.Length <= i) break;
                if (width >= breakpoints[i]) {
                    _uiDocument.rootVisualElement.AddToClassList(screenClasses[i]);
                }
            }
        }
    }
}