#if UNITY_6000_0_OR_NEWER && UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Editor {
    [InitializeOnLoad]
    static class OneJSOverlayUpdateModeBridge {
        internal enum SceneUpdateMode {
            Camera = 0,
            Selected = 1,
            None = 2
        }

        internal enum GameUpdateMode {
            Camera = 0,
            All = 1
        }

        const string SceneUpdateModePrefKey = "OneJS.EditMode.SceneUpdateMode";
        const string GameUpdateModePrefKey = "OneJS.EditMode.GameUpdateMode";
        static readonly Type GameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        static Camera _lastRenderedGameCamera;
        static int _cachedFrustumFrame = -1;
        static Camera _cachedFrustumCamera;
        static Plane[] _cachedFrustumPlanes;

        internal static SceneUpdateMode CurrentSceneMode {
            get {
                var storedValue = EditorPrefs.GetInt(SceneUpdateModePrefKey, (int)SceneUpdateMode.Camera);
                if (storedValue < (int)SceneUpdateMode.Camera || storedValue > (int)SceneUpdateMode.None) {
                    return SceneUpdateMode.Camera;
                }
                return (SceneUpdateMode)storedValue;
            }
            set => EditorPrefs.SetInt(SceneUpdateModePrefKey, (int)value);
        }

        internal static GameUpdateMode CurrentGameMode {
            get {
                var storedValue = EditorPrefs.GetInt(GameUpdateModePrefKey, (int)GameUpdateMode.Camera);
                if (storedValue < (int)GameUpdateMode.Camera || storedValue > (int)GameUpdateMode.All) {
                    return GameUpdateMode.Camera;
                }
                return (GameUpdateMode)storedValue;
            }
            set => EditorPrefs.SetInt(GameUpdateModePrefKey, (int)value);
        }

        static OneJSOverlayUpdateModeBridge() {
            JSRunner.EditModeUpdateFilter = ShouldUpdateRunnerInEditMode;
            JSRunner.PlayModeUpdateFilter = ShouldUpdateRunnerInPlayMode;
            Camera.onPreCull -= OnCameraPreCull;
            Camera.onPreCull += OnCameraPreCull;
        }

        static void OnCameraPreCull(Camera camera) {
            if (camera == null || camera.cameraType != CameraType.Game) return;
            if (!camera.enabled || !camera.gameObject.activeInHierarchy) return;
            _lastRenderedGameCamera = camera;
        }

        static bool ShouldUpdateRunnerInEditMode(JSRunner runner) {
            if (runner == null) return false;
            if (IsGameViewFocused()) {
                return EvaluateGameMode(runner);
            }
            return EvaluateSceneMode(runner);
        }

        static bool ShouldUpdateRunnerInPlayMode(JSRunner runner) {
            if (runner == null) return false;
            return EvaluateGameMode(runner);
        }

        static bool EvaluateSceneMode(JSRunner runner) {
            switch (CurrentSceneMode) {
                case SceneUpdateMode.None:
                    return false;
                case SceneUpdateMode.Selected:
                    return Selection.Contains(runner.gameObject);
                case SceneUpdateMode.Camera:
                default:
                    return IsRunnerVisibleInCamera(runner, GetSceneViewCamera());
            }
        }

        static bool EvaluateGameMode(JSRunner runner) {
            switch (CurrentGameMode) {
                case GameUpdateMode.All:
                    return true;
                case GameUpdateMode.Camera:
                default:
                    return IsRunnerVisibleInCamera(runner, GetGameViewCamera());
            }
        }

        static bool IsGameViewFocused() {
            var focused = EditorWindow.focusedWindow;
            return focused != null &&
                   GameViewType != null &&
                   GameViewType.IsAssignableFrom(focused.GetType());
        }

        static Camera GetSceneViewCamera() {
            var sceneView = SceneView.lastActiveSceneView;
            return sceneView != null ? sceneView.camera : null;
        }

        static Camera GetGameViewCamera() {
            var main = Camera.main;
            if (main != null && main.enabled && main.gameObject.activeInHierarchy) {
                return main;
            }
            if (_lastRenderedGameCamera != null && _lastRenderedGameCamera.enabled && _lastRenderedGameCamera.gameObject.activeInHierarchy) {
                return _lastRenderedGameCamera;
            }
            return null;
        }

        static bool IsRunnerVisibleInCamera(JSRunner runner, Camera camera) {
            if (runner == null) return false;
            if (camera == null) return true;

            var frustumPlanes = GetCachedFrustumPlanes(camera);
            if (frustumPlanes == null) return true;

            var runnerGameObject = runner.gameObject;
            if (runnerGameObject.TryGetComponent<Renderer>(out var renderer) && renderer.enabled && runnerGameObject.activeInHierarchy) {
                if (GeometryUtility.TestPlanesAABB(frustumPlanes, renderer.bounds)) {
                    return true;
                }
            }

            var viewportPoint = camera.WorldToViewportPoint(runner.transform.position);
            return viewportPoint.z > 0f &&
                   viewportPoint.x >= 0f && viewportPoint.x <= 1f &&
                   viewportPoint.y >= 0f && viewportPoint.y <= 1f;
        }

        static Plane[] GetCachedFrustumPlanes(Camera camera) {
            if (camera == null) return null;
            if (_cachedFrustumFrame != Time.frameCount || _cachedFrustumCamera != camera || _cachedFrustumPlanes == null) {
                _cachedFrustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);
                _cachedFrustumFrame = Time.frameCount;
                _cachedFrustumCamera = camera;
            }
            return _cachedFrustumPlanes;
        }
    }

    [Overlay(typeof(SceneView), "OneJS")]
    internal class OneJSEditorOverlay : Overlay, ICreateHorizontalToolbar {
        const int SampleWindow = 45;
        const double UiRefreshIntervalSeconds = 0.25d;
        const float FieldLabelWidth = 58f;
        const float CompactFpsValueWidth = 45f;
        const float CompactDropdownWidth = 115f;

        readonly double[] _frameDurations = new double[SampleWindow];
        readonly List<Button> _fpsValueButtons = new List<Button>();
        int _sampleCount;
        int _nextSampleIndex;
        double _lastSampleTime;
        double _nextUiRefreshTime;
        bool _isSubscribed;

        public override VisualElement CreatePanelContent() {
            var root = new VisualElement {
                style = {
                    flexDirection = FlexDirection.Column,
                    minWidth = 170
                }
            };

            var fpsRow = CreateFpsRow(root, compact: false);
            fpsRow.style.marginBottom = 6f;
            root.Add(fpsRow);

            var updateModeLabel = new Label("Update Mode:");
            root.Add(updateModeLabel);

            var sceneOptions = new List<string> {
                "Camera",
                "Selected",
                "None"
            };
            var sceneIndex = OneJSOverlayUpdateModeBridge.CurrentSceneMode switch {
                OneJSOverlayUpdateModeBridge.SceneUpdateMode.Camera => 0,
                OneJSOverlayUpdateModeBridge.SceneUpdateMode.Selected => 1,
                OneJSOverlayUpdateModeBridge.SceneUpdateMode.None => 2,
                _ => 0
            };
            var scenePopup = CreateScenePopup(sceneOptions, sceneIndex, compact: false);
            root.Add(scenePopup);

            var gameOptions = new List<string> {
                "Camera",
                "All"
            };
            var gameIndex = OneJSOverlayUpdateModeBridge.CurrentGameMode switch {
                OneJSOverlayUpdateModeBridge.GameUpdateMode.Camera => 0,
                OneJSOverlayUpdateModeBridge.GameUpdateMode.All => 1,
                _ => 0
            };
            var gamePopup = CreateGamePopup(gameOptions, gameIndex, compact: false);
            root.Add(gamePopup);

            HookRootLifecycle(root);
            return root;
        }

        public OverlayToolbar CreateHorizontalToolbarContent() {
            var root = new OverlayToolbar {
                style = {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center
                }
            };

            var fpsRow = CreateFpsRow(root, compact: true);
            fpsRow.style.marginRight = 8f;
            root.Add(fpsRow);

            var sceneOptions = new List<string> { "Camera", "Selected", "None" };
            var sceneIndex = OneJSOverlayUpdateModeBridge.CurrentSceneMode switch {
                OneJSOverlayUpdateModeBridge.SceneUpdateMode.Camera => 0,
                OneJSOverlayUpdateModeBridge.SceneUpdateMode.Selected => 1,
                OneJSOverlayUpdateModeBridge.SceneUpdateMode.None => 2,
                _ => 0
            };
            var scenePopup = CreateScenePopup(sceneOptions, sceneIndex, compact: true);
            scenePopup.style.marginRight = 8f;
            root.Add(scenePopup);

            var gameOptions = new List<string> { "Camera", "All" };
            var gameIndex = OneJSOverlayUpdateModeBridge.CurrentGameMode switch {
                OneJSOverlayUpdateModeBridge.GameUpdateMode.Camera => 0,
                OneJSOverlayUpdateModeBridge.GameUpdateMode.All => 1,
                _ => 0
            };
            var gamePopup = CreateGamePopup(gameOptions, gameIndex, compact: true);
            root.Add(gamePopup);

            HookRootLifecycle(root);
            return root;
        }

        VisualElement CreateFpsRow(VisualElement ownerRoot, bool compact) {
            var fpsRow = new VisualElement {
                style = {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center
                }
            };
            var fpsLabel = new Label("FPS:");
            if (compact) {
                fpsLabel.style.minWidth = StyleKeyword.Auto;
                fpsLabel.style.width = StyleKeyword.Auto;
            } else {
                fpsLabel.style.minWidth = FieldLabelWidth;
                fpsLabel.style.width = FieldLabelWidth;
            }
            fpsRow.Add(fpsLabel);

            var fpsValueButton = new Button { text = "---", tooltip = "Editor FPS (updates in Edit Mode)" };
            fpsValueButton.SetEnabled(false);
            fpsValueButton.style.minWidth = compact ? CompactFpsValueWidth : 0f;
            fpsValueButton.style.flexGrow = compact ? 0f : 1f;
            fpsValueButton.style.marginLeft = compact ? 0f : 6f;
            fpsValueButton.style.unityTextAlign = TextAnchor.MiddleCenter;
            fpsRow.Add(fpsValueButton);

            _fpsValueButtons.Add(fpsValueButton);
            ownerRoot.RegisterCallback<DetachFromPanelEvent>(_ => _fpsValueButtons.Remove(fpsValueButton));

            return fpsRow;
        }

        PopupField<string> CreateScenePopup(List<string> options, int index, bool compact) {
            var popup = new PopupField<string>("Scene:", options, index);
            if (compact) {
                popup.labelElement.style.minWidth = StyleKeyword.Auto;
                popup.labelElement.style.width = StyleKeyword.Auto;
                popup.labelElement.style.marginRight = 0f;
                popup.style.minWidth = CompactDropdownWidth;
                popup.style.width = CompactDropdownWidth;
                popup.style.flexShrink = 0f;
            } else {
                popup.labelElement.style.minWidth = FieldLabelWidth;
                popup.labelElement.style.width = FieldLabelWidth;
            }
            popup.RegisterValueChangedCallback(evt => {
                OneJSOverlayUpdateModeBridge.CurrentSceneMode = evt.newValue switch {
                    "Selected" => OneJSOverlayUpdateModeBridge.SceneUpdateMode.Selected,
                    "None" => OneJSOverlayUpdateModeBridge.SceneUpdateMode.None,
                    _ => OneJSOverlayUpdateModeBridge.SceneUpdateMode.Camera
                };
            });
            return popup;
        }

        PopupField<string> CreateGamePopup(List<string> options, int index, bool compact) {
            var popup = new PopupField<string>("Game:", options, index);
            if (compact) {
                popup.labelElement.style.minWidth = StyleKeyword.Auto;
                popup.labelElement.style.width = StyleKeyword.Auto;
                popup.labelElement.style.marginRight = 0f;
                popup.style.minWidth = CompactDropdownWidth;
                popup.style.width = CompactDropdownWidth;
                popup.style.flexShrink = 0f;
            } else {
                popup.labelElement.style.minWidth = FieldLabelWidth;
                popup.labelElement.style.width = FieldLabelWidth;
            }
            popup.RegisterValueChangedCallback(evt => {
                OneJSOverlayUpdateModeBridge.CurrentGameMode = evt.newValue == "All"
                    ? OneJSOverlayUpdateModeBridge.GameUpdateMode.All
                    : OneJSOverlayUpdateModeBridge.GameUpdateMode.Camera;
            });
            return popup;
        }

        void HookRootLifecycle(VisualElement root) {
            _lastSampleTime = EditorApplication.timeSinceStartup;
            SubscribeToEditorUpdate();
            root.RegisterCallback<DetachFromPanelEvent>(_ => UnsubscribeFromEditorUpdate());
            root.RegisterCallback<AttachToPanelEvent>(_ => SubscribeToEditorUpdate());
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
            if (_fpsValueButtons.Count == 0) return;

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
            var fpsText = Mathf.RoundToInt((float)fps).ToString();
            for (var i = _fpsValueButtons.Count - 1; i >= 0; i--) {
                var button = _fpsValueButtons[i];
                if (button == null) {
                    _fpsValueButtons.RemoveAt(i);
                    continue;
                }
                button.text = fpsText;
            }
        }
    }
}
#endif
