using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace OneJS.GPU {
    /// <summary>
    /// Bridge class exposing compute shader functionality to JavaScript.
    /// All methods are static and designed to be called via the CS proxy.
    /// </summary>
    public static class GPUBridge {
        // Shader registry: name -> ComputeShader
        static readonly Dictionary<string, ComputeShader> _shaderRegistry = new Dictionary<string, ComputeShader>();

        // Handle tables for shaders, buffers, and render textures
        static int _nextShaderHandle = 1;
        static int _nextBufferHandle = 1;
        static int _nextReadbackHandle = 1;
        static int _nextRenderTextureHandle = 1;
        static readonly Dictionary<int, ComputeShader> _shaderHandles = new Dictionary<int, ComputeShader>();
        static readonly Dictionary<int, ComputeBuffer> _bufferHandles = new Dictionary<int, ComputeBuffer>();
        static readonly Dictionary<int, RenderTexture> _renderTextureHandles = new Dictionary<int, RenderTexture>();
        static readonly Dictionary<int, AsyncGPUReadbackRequest> _readbackRequests = new Dictionary<int, AsyncGPUReadbackRequest>();
        static readonly Dictionary<int, float[]> _readbackResults = new Dictionary<int, float[]>();
        static readonly object _lock = new object();

        // Platform capability properties (as properties for C# usage)
        public static bool SupportsCompute => SystemInfo.supportsComputeShaders;
        public static bool SupportsAsyncReadback => SystemInfo.supportsAsyncGPUReadback;
        public static int MaxComputeWorkGroupSizeX => SystemInfo.maxComputeWorkGroupSizeX;
        public static int MaxComputeWorkGroupSizeY => SystemInfo.maxComputeWorkGroupSizeY;
        public static int MaxComputeWorkGroupSizeZ => SystemInfo.maxComputeWorkGroupSizeZ;

        // Platform capability methods for JavaScript (CS proxy treats uppercase as methods)
        public static bool GetSupportsCompute() => SystemInfo.supportsComputeShaders;
        public static bool GetSupportsAsyncReadback() => SystemInfo.supportsAsyncGPUReadback;
        public static int GetMaxComputeWorkGroupSizeX() => SystemInfo.maxComputeWorkGroupSizeX;
        public static int GetMaxComputeWorkGroupSizeY() => SystemInfo.maxComputeWorkGroupSizeY;
        public static int GetMaxComputeWorkGroupSizeZ() => SystemInfo.maxComputeWorkGroupSizeZ;

        /// <summary>
        /// Register a compute shader with a name for JavaScript access.
        /// Call this from a MonoBehaviour to make shaders available.
        /// </summary>
        public static void Register(string name, ComputeShader shader) {
            if (shader == null) {
                Debug.LogWarning($"[GPUBridge] Cannot register null shader with name '{name}'");
                return;
            }
            lock (_lock) {
                _shaderRegistry[name] = shader;
            }
        }

        /// <summary>
        /// Unregister a shader by name.
        /// </summary>
        public static void Unregister(string name) {
            lock (_lock) {
                _shaderRegistry.Remove(name);
            }
        }

        /// <summary>
        /// Clear all registered shaders.
        /// </summary>
        public static void ClearRegistry() {
            lock (_lock) {
                _shaderRegistry.Clear();
            }
        }

        // ============ JS API Methods ============

        /// <summary>
        /// Load a shader by registered name. Returns handle or -1 if not found.
        /// </summary>
        public static int LoadShader(string name) {
            lock (_lock) {
                if (!_shaderRegistry.TryGetValue(name, out var shader)) {
                    Debug.LogWarning($"[GPUBridge] Shader '{name}' not found in registry");
                    return -1;
                }

                int handle = _nextShaderHandle++;
                _shaderHandles[handle] = shader;
                return handle;
            }
        }

        /// <summary>
        /// Register an externally-provided ComputeShader (e.g., from JSRunner globals).
        /// Returns handle or -1 if shader is null.
        /// </summary>
        public static int RegisterShader(ComputeShader shader) {
            if (shader == null) {
                Debug.LogWarning("[GPUBridge] Cannot register null shader");
                return -1;
            }

            lock (_lock) {
                int handle = _nextShaderHandle++;
                _shaderHandles[handle] = shader;
                return handle;
            }
        }

        /// <summary>
        /// Dispose a shader handle.
        /// </summary>
        public static void DisposeShader(int handle) {
            lock (_lock) {
                _shaderHandles.Remove(handle);
            }
        }

        /// <summary>
        /// Find a kernel by name. Returns kernel index or -1 if not found.
        /// </summary>
        public static int FindKernel(int shaderHandle, string kernelName) {
            lock (_lock) {
                if (!_shaderHandles.TryGetValue(shaderHandle, out var shader)) {
                    return -1;
                }
                try {
                    return shader.FindKernel(kernelName);
                } catch {
                    return -1;
                }
            }
        }

        /// <summary>
        /// Set a float uniform.
        /// </summary>
        public static void SetFloat(int shaderHandle, string name, float value) {
            lock (_lock) {
                if (_shaderHandles.TryGetValue(shaderHandle, out var shader)) {
                    shader.SetFloat(name, value);
                }
            }
        }

        /// <summary>
        /// Set an int uniform.
        /// </summary>
        public static void SetInt(int shaderHandle, string name, int value) {
            lock (_lock) {
                if (_shaderHandles.TryGetValue(shaderHandle, out var shader)) {
                    shader.SetInt(name, value);
                }
            }
        }

        /// <summary>
        /// Set a bool uniform (as int 0 or 1).
        /// </summary>
        public static void SetBool(int shaderHandle, string name, bool value) {
            lock (_lock) {
                if (_shaderHandles.TryGetValue(shaderHandle, out var shader)) {
                    shader.SetBool(name, value);
                }
            }
        }

        /// <summary>
        /// Set a vector uniform.
        /// </summary>
        public static void SetVector(int shaderHandle, string name, float x, float y, float z, float w) {
            lock (_lock) {
                if (_shaderHandles.TryGetValue(shaderHandle, out var shader)) {
                    shader.SetVector(name, new Vector4(x, y, z, w));
                }
            }
        }

        /// <summary>
        /// Set a matrix uniform from JSON array of 16 floats.
        /// </summary>
        public static void SetMatrix(int shaderHandle, string name, string matrixJson) {
            lock (_lock) {
                if (!_shaderHandles.TryGetValue(shaderHandle, out var shader)) {
                    return;
                }

                var floats = ParseFloatArray(matrixJson);
                if (floats.Length != 16) {
                    Debug.LogWarning($"[GPUBridge] SetMatrix expects 16 floats, got {floats.Length}");
                    return;
                }

                var matrix = new Matrix4x4();
                for (int i = 0; i < 16; i++) {
                    matrix[i] = floats[i];
                }
                shader.SetMatrix(name, matrix);
            }
        }

        /// <summary>
        /// Create a compute buffer. Returns handle or -1 on failure.
        /// </summary>
        public static int CreateBuffer(int count, int stride) {
            if (count <= 0 || stride <= 0) {
                Debug.LogWarning($"[GPUBridge] Invalid buffer parameters: count={count}, stride={stride}");
                return -1;
            }

            lock (_lock) {
                try {
                    var buffer = new ComputeBuffer(count, stride);
                    int handle = _nextBufferHandle++;
                    _bufferHandles[handle] = buffer;
                    return handle;
                } catch (Exception ex) {
                    Debug.LogError($"[GPUBridge] Failed to create buffer: {ex.Message}");
                    return -1;
                }
            }
        }

        /// <summary>
        /// Dispose a buffer handle.
        /// </summary>
        public static void DisposeBuffer(int handle) {
            lock (_lock) {
                if (_bufferHandles.TryGetValue(handle, out var buffer)) {
                    buffer.Release();
                    _bufferHandles.Remove(handle);
                }
            }
        }

        /// <summary>
        /// Set buffer data from JSON float array.
        /// </summary>
        public static void SetBufferData(int handle, string dataJson) {
            lock (_lock) {
                if (!_bufferHandles.TryGetValue(handle, out var buffer)) {
                    return;
                }

                var floats = ParseFloatArray(dataJson);
                buffer.SetData(floats);
            }
        }

        /// <summary>
        /// Bind a buffer to a shader kernel.
        /// </summary>
        public static void BindBuffer(int shaderHandle, int kernelIndex, string name, int bufferHandle) {
            lock (_lock) {
                if (!_shaderHandles.TryGetValue(shaderHandle, out var shader)) {
                    return;
                }
                if (!_bufferHandles.TryGetValue(bufferHandle, out var buffer)) {
                    return;
                }

                shader.SetBuffer(kernelIndex, name, buffer);
            }
        }

        /// <summary>
        /// Dispatch a compute shader kernel.
        /// </summary>
        public static void Dispatch(int shaderHandle, int kernelIndex, int groupsX, int groupsY, int groupsZ) {
            lock (_lock) {
                if (!_shaderHandles.TryGetValue(shaderHandle, out var shader)) {
                    return;
                }

                shader.Dispatch(kernelIndex, groupsX, groupsY, groupsZ);
            }
        }

        // ============ RenderTexture API ============

        /// <summary>
        /// Create a RenderTexture for compute shader output.
        /// </summary>
        /// <param name="width">Texture width</param>
        /// <param name="height">Texture height</param>
        /// <param name="enableRandomWrite">Enable for RWTexture2D in compute shaders</param>
        /// <returns>Handle or -1 on failure</returns>
        public static int CreateRenderTexture(int width, int height, bool enableRandomWrite = true) {
            if (width <= 0 || height <= 0) {
                Debug.LogWarning($"[GPUBridge] Invalid RenderTexture dimensions: {width}x{height}");
                return -1;
            }

            lock (_lock) {
                try {
                    var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) {
                        enableRandomWrite = enableRandomWrite,
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp
                    };
                    rt.Create();

                    int handle = _nextRenderTextureHandle++;
                    _renderTextureHandles[handle] = rt;
                    return handle;
                } catch (Exception ex) {
                    Debug.LogError($"[GPUBridge] Failed to create RenderTexture: {ex.Message}");
                    return -1;
                }
            }
        }

        /// <summary>
        /// Resize an existing RenderTexture in-place.
        /// The same RenderTexture object is reused, preserving UI bindings.
        /// </summary>
        public static bool ResizeRenderTexture(int handle, int width, int height) {
            if (width <= 0 || height <= 0) {
                return false;
            }

            lock (_lock) {
                if (!_renderTextureHandles.TryGetValue(handle, out var rt)) {
                    return false;
                }

                // Skip if size unchanged
                if (rt.width == width && rt.height == height) {
                    return true;
                }

                try {
                    // Resize in-place: release GPU resources, update dimensions, recreate
                    // This keeps the same RenderTexture object, so UI bindings stay valid
                    rt.Release();
                    rt.width = width;
                    rt.height = height;
                    rt.Create();
                    return true;
                } catch (Exception ex) {
                    Debug.LogError($"[GPUBridge] Failed to resize RenderTexture: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Dispose a RenderTexture by handle.
        /// </summary>
        public static void DisposeRenderTexture(int handle) {
            lock (_lock) {
                if (_renderTextureHandles.TryGetValue(handle, out var rt)) {
                    rt.Release();
                    UnityEngine.Object.Destroy(rt);
                    _renderTextureHandles.Remove(handle);
                }
            }
        }

        /// <summary>
        /// Get the actual RenderTexture object.
        /// Returns null if handle is invalid.
        /// </summary>
        public static RenderTexture GetRenderTextureObject(int handle) {
            lock (_lock) {
                _renderTextureHandles.TryGetValue(handle, out var rt);
                return rt;
            }
        }

        /// <summary>
        /// Get the RenderTexture wrapped as a StyleBackground for use with UI Toolkit's backgroundImage.
        /// Returns null if handle is invalid.
        /// </summary>
        public static UnityEngine.UIElements.StyleBackground? GetRenderTextureAsBackground(int handle) {
            lock (_lock) {
                if (!_renderTextureHandles.TryGetValue(handle, out var rt)) {
                    return null;
                }
                return new UnityEngine.UIElements.StyleBackground(UnityEngine.UIElements.Background.FromRenderTexture(rt));
            }
        }

        /// <summary>
        /// Set the backgroundImage style property of a VisualElement directly from a RenderTexture handle.
        /// This avoids struct serialization issues with StyleBackground.
        /// </summary>
        public static void SetElementBackgroundImage(UnityEngine.UIElements.VisualElement element, int rtHandle) {
            if (element == null) return;
            lock (_lock) {
                if (!_renderTextureHandles.TryGetValue(rtHandle, out var rt)) {
                    return;
                }
                element.style.backgroundImage = new UnityEngine.UIElements.StyleBackground(
                    UnityEngine.UIElements.Background.FromRenderTexture(rt)
                );
            }
        }

        /// <summary>
        /// Clear the backgroundImage style property of a VisualElement.
        /// </summary>
        public static void ClearElementBackgroundImage(UnityEngine.UIElements.VisualElement element) {
            if (element == null) return;
            element.style.backgroundImage = UnityEngine.UIElements.StyleKeyword.Null;
        }

        /// <summary>
        /// Get RenderTexture dimensions.
        /// </summary>
        public static int GetRenderTextureWidth(int handle) {
            lock (_lock) {
                return _renderTextureHandles.TryGetValue(handle, out var rt) ? rt.width : 0;
            }
        }

        /// <summary>
        /// Get RenderTexture dimensions.
        /// </summary>
        public static int GetRenderTextureHeight(int handle) {
            lock (_lock) {
                return _renderTextureHandles.TryGetValue(handle, out var rt) ? rt.height : 0;
            }
        }

        /// <summary>
        /// Bind a RenderTexture to a compute shader kernel.
        /// </summary>
        public static void SetTexture(int shaderHandle, int kernelIndex, string name, int textureHandle) {
            lock (_lock) {
                if (!_shaderHandles.TryGetValue(shaderHandle, out var shader)) {
                    return;
                }
                if (!_renderTextureHandles.TryGetValue(textureHandle, out var rt)) {
                    return;
                }

                shader.SetTexture(kernelIndex, name, rt);
            }
        }

        // ============ Screen API ============

        /// <summary>
        /// Get current screen width.
        /// </summary>
        public static int GetScreenWidth() => Screen.width;

        /// <summary>
        /// Get current screen height.
        /// </summary>
        public static int GetScreenHeight() => Screen.height;

        /// <summary>
        /// Request async GPU readback. Returns request ID or -1 on failure.
        /// </summary>
        public static int RequestReadback(int bufferHandle) {
            lock (_lock) {
                if (!_bufferHandles.TryGetValue(bufferHandle, out var buffer)) {
                    return -1;
                }

                if (!SystemInfo.supportsAsyncGPUReadback) {
                    // Fallback: synchronous readback
                    int count = buffer.count;
                    int stride = buffer.stride;
                    int floatCount = (count * stride) / sizeof(float);
                    var data = new float[floatCount];
                    buffer.GetData(data);

                    int requestId = _nextReadbackHandle++;
                    _readbackResults[requestId] = data;
                    return requestId;
                }

                try {
                    var request = AsyncGPUReadback.Request(buffer);
                    int requestId = _nextReadbackHandle++;
                    _readbackRequests[requestId] = request;
                    return requestId;
                } catch (Exception ex) {
                    Debug.LogError($"[GPUBridge] Failed to request readback: {ex.Message}");
                    return -1;
                }
            }
        }

        /// <summary>
        /// Check if a readback request is complete.
        /// </summary>
        public static bool IsReadbackComplete(int requestId) {
            lock (_lock) {
                // Check if we already have the result
                if (_readbackResults.ContainsKey(requestId)) {
                    return true;
                }

                if (!_readbackRequests.TryGetValue(requestId, out var request)) {
                    return true; // Not found = treat as complete (error case)
                }

                if (request.done) {
                    // Process the result
                    if (request.hasError) {
                        Debug.LogError("[GPUBridge] Readback request failed");
                        _readbackResults[requestId] = Array.Empty<float>();
                    } else {
                        var data = request.GetData<float>();
                        var result = new float[data.Length];
                        data.CopyTo(result);
                        _readbackResults[requestId] = result;
                    }
                    _readbackRequests.Remove(requestId);
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Get readback data as JSON array. Returns empty array if not ready.
        /// </summary>
        public static string GetReadbackData(int requestId) {
            lock (_lock) {
                if (!_readbackResults.TryGetValue(requestId, out var data)) {
                    return "[]";
                }

                _readbackResults.Remove(requestId);
                return FloatArrayToJson(data);
            }
        }

        /// <summary>
        /// Clean up all resources.
        /// </summary>
        public static void Cleanup() {
            lock (_lock) {
                foreach (var buffer in _bufferHandles.Values) {
                    buffer.Release();
                }
                foreach (var rt in _renderTextureHandles.Values) {
                    rt.Release();
                    UnityEngine.Object.Destroy(rt);
                }
                _bufferHandles.Clear();
                _renderTextureHandles.Clear();
                _shaderHandles.Clear();
                _readbackRequests.Clear();
                _readbackResults.Clear();
            }
        }

        // ============ Helper Methods ============

        static float[] ParseFloatArray(string json) {
            if (string.IsNullOrEmpty(json) || json == "[]") {
                return Array.Empty<float>();
            }

            // Simple JSON array parser for [1.0, 2.0, ...]
            json = json.Trim();
            if (!json.StartsWith("[") || !json.EndsWith("]")) {
                return Array.Empty<float>();
            }

            json = json.Substring(1, json.Length - 2);
            if (string.IsNullOrEmpty(json)) {
                return Array.Empty<float>();
            }

            var parts = json.Split(',');
            var result = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++) {
                if (float.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float value)) {
                    result[i] = value;
                }
            }
            return result;
        }

        static string FloatArrayToJson(float[] data) {
            if (data == null || data.Length == 0) {
                return "[]";
            }

            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < data.Length; i++) {
                if (i > 0) sb.Append(',');
                sb.Append(data[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
