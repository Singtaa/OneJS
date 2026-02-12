#if UNITY_6000_0_OR_NEWER && UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Editor {
    [InitializeOnLoad]
    static class OneJSOverlayUpdateModeBridge {
        internal enum UpdateMode {
            None = 0,
            Selected = 1,
            Camera = 2,
            All = 3
        }

        const string UpdateModePrefKey = "OneJS.EditMode.UpdateMode";

        internal static UpdateMode CurrentMode {
            get {
                var storedValue = EditorPrefs.GetInt(UpdateModePrefKey, (int)UpdateMode.Selected);
                if (storedValue < (int)UpdateMode.None || storedValue > (int)UpdateMode.All) {
                    return UpdateMode.Selected;
                }
                return (UpdateMode)storedValue;
            }
            set => EditorPrefs.SetInt(UpdateModePrefKey, (int)value);
        }

        static OneJSOverlayUpdateModeBridge() {
            JSRunner.EditModeUpdateFilter = ShouldUpdateRunner;
        }

        static bool ShouldUpdateRunner(JSRunner runner) {
            if (runner == null) return false;

            switch (CurrentMode) {
                case UpdateMode.None:
                    return false;
                case UpdateMode.Selected:
                    return Selection.Contains(runner.gameObject);
                case UpdateMode.Camera:
                    return IsInLastActiveSceneViewCamera(runner);
                case UpdateMode.All:
                default:
                    return true;
            }
        }

        static bool IsInLastActiveSceneViewCamera(JSRunner runner) {
            var sceneView = SceneView.lastActiveSceneView;
            var camera = sceneView != null ? sceneView.camera : null;
            if (camera == null) return false;

            var runnerGameObject = runner.gameObject;
            if (runnerGameObject.TryGetComponent<Renderer>(out var renderer) && renderer.enabled && runnerGameObject.activeInHierarchy) {
                var frustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);
                if (GeometryUtility.TestPlanesAABB(frustumPlanes, renderer.bounds)) {
                    return true;
                }
            }

            var viewportPoint = camera.WorldToViewportPoint(runner.transform.position);
            return viewportPoint.z > 0f &&
                   viewportPoint.x >= 0f && viewportPoint.x <= 1f &&
                   viewportPoint.y >= 0f && viewportPoint.y <= 1f;
        }
    }

    [Overlay(typeof(SceneView), "OneJS")]
    internal class OneJSFpsOverlay : Overlay {
        const int SampleWindow = 45;
        const double UiRefreshIntervalSeconds = 0.25d;
        const float FieldLabelWidth = 58f;

        readonly double[] _frameDurations = new double[SampleWindow];
        int _sampleCount;
        int _nextSampleIndex;
        double _lastSampleTime;
        double _nextUiRefreshTime;
        bool _isSubscribed;
        Button _fpsValueButton;

        public override VisualElement CreatePanelContent() {
            var root = new VisualElement {
                style = {
                    flexDirection = FlexDirection.Column,
                    minWidth = 170
                }
            };

            var fpsRow = new VisualElement {
                style = {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center
                }
            };
            var fpsLabel = new Label("FPS:");
            fpsLabel.style.minWidth = FieldLabelWidth;
            fpsLabel.style.width = FieldLabelWidth;
            fpsRow.Add(fpsLabel);

            _fpsValueButton = new Button { text = "--", tooltip = "Editor FPS (updates in Edit Mode)" };
            _fpsValueButton.SetEnabled(false);
            _fpsValueButton.style.flexGrow = 1f;
            _fpsValueButton.style.unityTextAlign = TextAnchor.MiddleCenter;
            fpsRow.Add(_fpsValueButton);
            root.Add(fpsRow);

            var updateOptions = new List<string> {
                "None",
                "Selected",
                "Camera",
                "All"
            };

            var currentMode = OneJSOverlayUpdateModeBridge.CurrentMode;
            var currentIndex = currentMode switch {
                OneJSOverlayUpdateModeBridge.UpdateMode.None => 0,
                OneJSOverlayUpdateModeBridge.UpdateMode.Selected => 1,
                OneJSOverlayUpdateModeBridge.UpdateMode.Camera => 2,
                OneJSOverlayUpdateModeBridge.UpdateMode.All => 3,
                _ => 1
            };

            var updatePopup = new PopupField<string>("Update:", updateOptions, currentIndex);
            updatePopup.labelElement.style.minWidth = FieldLabelWidth;
            updatePopup.labelElement.style.width = FieldLabelWidth;
            updatePopup.RegisterValueChangedCallback(evt => {
                OneJSOverlayUpdateModeBridge.CurrentMode = evt.newValue switch {
                    "None" => OneJSOverlayUpdateModeBridge.UpdateMode.None,
                    "Camera" => OneJSOverlayUpdateModeBridge.UpdateMode.Camera,
                    "All" => OneJSOverlayUpdateModeBridge.UpdateMode.All,
                    _ => OneJSOverlayUpdateModeBridge.UpdateMode.Selected
                };
            });
            root.Add(updatePopup);

            _lastSampleTime = EditorApplication.timeSinceStartup;
            SubscribeToEditorUpdate();
            root.RegisterCallback<DetachFromPanelEvent>(_ => UnsubscribeFromEditorUpdate());
            root.RegisterCallback<AttachToPanelEvent>(_ => SubscribeToEditorUpdate());
            return root;
        }

        void SubscribeToEditorUpdate() {
            if (_isSubscribed) return;
            EditorApplication.update += OnEditorUpdate;
            _isSubscribed = true;
        }

        void UnsubscribeFromEditorUpdate() {
            if (!_isSubscribed) return;
            EditorApplication.update -= OnEditorUpdate;
            _isSubscribed = false;
        }

        void OnEditorUpdate() {
            if (_fpsValueButton == null) return;

            var now = EditorApplication.timeSinceStartup;
            var delta = now - _lastSampleTime;
            if (delta <= 0d) return;

            _lastSampleTime = now;
            _frameDurations[_nextSampleIndex] = delta;
            _nextSampleIndex = (_nextSampleIndex + 1) % SampleWindow;
            if (_sampleCount < SampleWindow) _sampleCount++;

            if (now < _nextUiRefreshTime || _sampleCount == 0) return;
            _nextUiRefreshTime = now + UiRefreshIntervalSeconds;

            double sum = 0d;
            for (var i = 0; i < _sampleCount; i++) sum += _frameDurations[i];

            var averageFrameTime = sum / _sampleCount;
            var fps = averageFrameTime > 0d ? 1d / averageFrameTime : 0d;
            _fpsValueButton.text = Mathf.RoundToInt((float)fps).ToString();
        }
    }
}
#endif
