using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Engine {
    [RequireComponent(typeof(UIDocument))]
    public class DPIAdjuster : MonoBehaviour {
        float currentDPI;
        string currentReolutionStr;
        UIDocument uiDocument;
        PanelSettings panelSettings;

        void Awake() {
            uiDocument = GetComponent<UIDocument>();
            panelSettings = uiDocument.panelSettings;
            Set();
        }

        void Update() {
            if (Math.Abs(currentDPI - Screen.dpi) > 0.1f &&
                currentReolutionStr != Screen.currentResolution.ToString()) {
                Set();
            }
        }

        void Set() {
            currentDPI = Screen.dpi;
            currentReolutionStr = Screen.currentResolution.ToString();
            panelSettings.scale = currentDPI > 130 ? 2f : 1f;
        }
    }
}