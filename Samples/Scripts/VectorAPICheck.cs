#if UNITY_EDITOR
using System;
using UnityEngine;

namespace OneJS.Samples {
    public class VectorAPICheck : MonoBehaviour {
        
        public GameObject targetObject;
        
#if !UNITY_2022_1_OR_NEWER
        void Awake() {
            if (targetObject != null)
                targetObject.SetActive(false);
            Debug.LogError(
                "This sample uses the UI Toolkit Vector API which requires Unity 2022.1 or later. You are currently using " +
                Application.unityVersion);
        }
#endif
    }
}
#endif