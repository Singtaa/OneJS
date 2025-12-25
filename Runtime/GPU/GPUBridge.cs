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

        // Handle tables for shaders and buffers
        static int _nextShaderHandle = 1;
        static int _nextBufferHandle = 1;
        static int _nextReadbackHandle = 1;
        static readonly Dictionary<int, ComputeShader> _shaderHandles = new Dictionary<int, ComputeShader>();
        static readonly Dictionary<int, ComputeBuffer> _bufferHandles = new Dictionary<int, ComputeBuffer>();
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
                _bufferHandles.Clear();
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
