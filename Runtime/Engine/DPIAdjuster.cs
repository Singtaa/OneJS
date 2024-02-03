using System;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace OneJS.Engine {
    [RequireComponent(typeof(UIDocument))]
    public class DPIAdjuster : MonoBehaviour {
        [Tooltip("Automatically set scale based on DPI")]
        [SerializeField] bool _autoSetUIScale;

        [Tooltip("Default UI scale for Panel Settings")]
        [SerializeField] float _defaultUIScale = 1f;

        [Tooltip("DPI Threshold for switching between 1x and 2x scale")]
        [SerializeField] int _dpiThreshold = 130;

        float _currentDPI;
        string _currentReolutionStr;
        UIDocument _uiDocument;
        PanelSettings _panelSettings;

        void Awake() {
            _uiDocument = GetComponent<UIDocument>();
            _panelSettings = _uiDocument.panelSettings;
            _panelSettings.scale = _defaultUIScale;
            if (_panelSettings.scaleMode != PanelScaleMode.ConstantPixelSize)
                this.enabled = false;
        }

        void OnEnable() {
            if (_autoSetUIScale)
                SetScale();
        }

        void Update() {
            if (_autoSetUIScale && Math.Abs(_currentDPI - Screen.dpi) > 0.1f &&
                _currentReolutionStr != Screen.currentResolution.ToString()) {
                SetScale();
            }
        }

        void SetScale() {
            _currentDPI = Screen.dpi;
            _currentReolutionStr = Screen.currentResolution.ToString();
            _panelSettings.scale = _currentDPI > _dpiThreshold ? 2f * _defaultUIScale : _defaultUIScale;
        }
    }
}