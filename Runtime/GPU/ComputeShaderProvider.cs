using System;
using UnityEngine;

namespace OneJS.GPU {
    /// <summary>
    /// Component that registers compute shaders with the GPUBridge for JavaScript access.
    /// Add this to a GameObject and assign compute shaders in the inspector.
    /// </summary>
    public class ComputeShaderProvider : MonoBehaviour {
        [Serializable]
        public class ShaderEntry {
            [Tooltip("Name used to load this shader from JavaScript via compute.load(name)")]
            public string name;

            [Tooltip("The compute shader asset")]
            public ComputeShader shader;
        }

        [Tooltip("Compute shaders to register for JavaScript access")]
        public ShaderEntry[] shaders;

        [Tooltip("If true, shaders are registered on Awake. Otherwise call Register() manually.")]
        public bool registerOnAwake = true;

        void Awake() {
            if (registerOnAwake) {
                Register();
            }
        }

        void OnDestroy() {
            Unregister();
        }

        /// <summary>
        /// Register all shaders with the GPUBridge.
        /// </summary>
        public void Register() {
            if (shaders == null) return;

            foreach (var entry in shaders) {
                if (string.IsNullOrEmpty(entry.name)) {
                    Debug.LogWarning($"[ComputeShaderProvider] Shader entry has empty name, skipping");
                    continue;
                }
                if (entry.shader == null) {
                    Debug.LogWarning($"[ComputeShaderProvider] Shader '{entry.name}' is null, skipping");
                    continue;
                }
                GPUBridge.Register(entry.name, entry.shader);
            }
        }

        /// <summary>
        /// Unregister all shaders from the GPUBridge.
        /// </summary>
        public void Unregister() {
            if (shaders == null) return;

            foreach (var entry in shaders) {
                if (!string.IsNullOrEmpty(entry.name)) {
                    GPUBridge.Unregister(entry.name);
                }
            }
        }
    }
}
