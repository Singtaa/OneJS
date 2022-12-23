#if UNITY_EDITOR
using System;
using UnityEngine;

namespace OneJS.Misc {
    public class VectorAPICheck : MonoBehaviour {
#if !UNITY_2022_1_OR_NEWER
        void Awake() {
            Debug.LogError(
                "This sample uses the UI Toolkit Vector API which requires Unity 2022.1 or later. You are currently using " +
                Application.unityVersion);
        }
#endif
    }
}
#endif