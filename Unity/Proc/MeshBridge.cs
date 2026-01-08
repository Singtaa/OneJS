using System;
using System.Collections.Generic;
using UnityEngine;

namespace OneJS.Proc {
    /// <summary>
    /// Bridge class exposing mesh creation and manipulation to JavaScript.
    /// All methods are static and designed to be called via the CS proxy.
    /// </summary>
    public static class MeshBridge {
        // Handle tables
        static int _nextMeshHandle = 1;
        static int _nextMaterialHandle = 1;
        static int _nextInstanceHandle = 1;

        static readonly Dictionary<int, Mesh> _meshHandles = new Dictionary<int, Mesh>();
        static readonly Dictionary<int, Material> _materialHandles = new Dictionary<int, Material>();
        static readonly Dictionary<int, GameObject> _instanceHandles = new Dictionary<int, GameObject>();
        static readonly object _lock = new object();

        // Parent transform for instantiated meshes (can be set externally)
        static Transform _parentTransform;

        /// <summary>
        /// Set the parent transform for instantiated meshes.
        /// </summary>
        public static void SetParentTransform(Transform parent) {
            _parentTransform = parent;
        }

        // ============ Primitive Generation ============

        /// <summary>
        /// Create a cube mesh. Returns mesh handle.
        /// </summary>
        public static int CreateCube(float sizeX, float sizeY, float sizeZ) {
            var mesh = MeshGenerator.CreateCube(sizeX, sizeY, sizeZ);
            return RegisterMesh(mesh);
        }

        /// <summary>
        /// Create a sphere mesh. Returns mesh handle.
        /// </summary>
        public static int CreateSphere(float radius, int longitudeSegments, int latitudeSegments) {
            var mesh = MeshGenerator.CreateSphere(radius, longitudeSegments, latitudeSegments);
            return RegisterMesh(mesh);
        }

        /// <summary>
        /// Create a cylinder mesh. Returns mesh handle.
        /// </summary>
        public static int CreateCylinder(float radius, float height, int segments) {
            var mesh = MeshGenerator.CreateCylinder(radius, height, segments);
            return RegisterMesh(mesh);
        }

        /// <summary>
        /// Create a cone mesh. Returns mesh handle.
        /// </summary>
        public static int CreateCone(float radius, float height, int segments) {
            var mesh = MeshGenerator.CreateCone(radius, height, segments);
            return RegisterMesh(mesh);
        }

        /// <summary>
        /// Create a plane mesh. Returns mesh handle.
        /// </summary>
        public static int CreatePlane(float width, float height, int segmentsX, int segmentsZ) {
            var mesh = MeshGenerator.CreatePlane(width, height, segmentsX, segmentsZ);
            return RegisterMesh(mesh);
        }

        /// <summary>
        /// Create a torus mesh. Returns mesh handle.
        /// </summary>
        public static int CreateTorus(float radius, float tubeRadius, int radialSegments, int tubularSegments) {
            var mesh = MeshGenerator.CreateTorus(radius, tubeRadius, radialSegments, tubularSegments);
            return RegisterMesh(mesh);
        }

        /// <summary>
        /// Create a quad mesh. Returns mesh handle.
        /// </summary>
        public static int CreateQuad(float width, float height) {
            var mesh = MeshGenerator.CreateQuad(width, height);
            return RegisterMesh(mesh);
        }

        // ============ Custom Mesh Creation ============

        /// <summary>
        /// Create an empty mesh. Returns mesh handle.
        /// </summary>
        public static int CreateEmpty() {
            var mesh = new Mesh();
            mesh.name = "ProceduralMesh";
            return RegisterMesh(mesh);
        }

        /// <summary>
        /// Set mesh data from arrays.
        /// </summary>
        public static void SetMeshData(
            int handle,
            float[] vertices,
            float[] normals,
            float[] uvs,
            int[] indices
        ) {
            lock (_lock) {
                if (!_meshHandles.TryGetValue(handle, out var mesh)) {
                    Debug.LogWarning($"[MeshBridge] Mesh handle {handle} not found");
                    return;
                }

                int vertexCount = vertices.Length / 3;
                var verts = new Vector3[vertexCount];
                for (int i = 0; i < vertexCount; i++) {
                    verts[i] = new Vector3(vertices[i * 3], vertices[i * 3 + 1], vertices[i * 3 + 2]);
                }
                mesh.vertices = verts;

                if (normals != null && normals.Length > 0) {
                    var norms = new Vector3[vertexCount];
                    for (int i = 0; i < vertexCount; i++) {
                        norms[i] = new Vector3(normals[i * 3], normals[i * 3 + 1], normals[i * 3 + 2]);
                    }
                    mesh.normals = norms;
                }

                if (uvs != null && uvs.Length > 0) {
                    var uv = new Vector2[vertexCount];
                    for (int i = 0; i < vertexCount; i++) {
                        uv[i] = new Vector2(uvs[i * 2], uvs[i * 2 + 1]);
                    }
                    mesh.uv = uv;
                }

                mesh.triangles = indices;
                mesh.RecalculateBounds();
            }
        }

        /// <summary>
        /// Get mesh data as separate arrays.
        /// Returns: [vertexCount, vertices..., normals..., uvs..., indexCount, indices...]
        /// </summary>
        public static float[] GetMeshData(int handle) {
            lock (_lock) {
                if (!_meshHandles.TryGetValue(handle, out var mesh)) {
                    return new float[0];
                }

                var verts = mesh.vertices;
                var norms = mesh.normals ?? new Vector3[0];
                var uvs = mesh.uv ?? new Vector2[0];
                var indices = mesh.triangles;

                // Pack into single array: vertexCount, verts(x3), norms(x3), uvs(x2), indexCount, indices
                int size = 1 + verts.Length * 3 + norms.Length * 3 + uvs.Length * 2 + 1 + indices.Length;
                var result = new float[size];

                int idx = 0;
                result[idx++] = verts.Length;

                for (int i = 0; i < verts.Length; i++) {
                    result[idx++] = verts[i].x;
                    result[idx++] = verts[i].y;
                    result[idx++] = verts[i].z;
                }

                for (int i = 0; i < norms.Length; i++) {
                    result[idx++] = norms[i].x;
                    result[idx++] = norms[i].y;
                    result[idx++] = norms[i].z;
                }

                for (int i = 0; i < uvs.Length; i++) {
                    result[idx++] = uvs[i].x;
                    result[idx++] = uvs[i].y;
                }

                result[idx++] = indices.Length;
                for (int i = 0; i < indices.Length; i++) {
                    result[idx++] = indices[i];
                }

                return result;
            }
        }

        // ============ Mesh Operations ============

        /// <summary>
        /// Clone a mesh. Returns new handle.
        /// </summary>
        public static int CloneMesh(int handle) {
            lock (_lock) {
                if (!_meshHandles.TryGetValue(handle, out var mesh)) {
                    return -1;
                }
                var clone = UnityEngine.Object.Instantiate(mesh);
                clone.name = mesh.name + "_Clone";
                return RegisterMeshInternal(clone);
            }
        }

        /// <summary>
        /// Recalculate normals.
        /// </summary>
        public static void RecalculateNormals(int handle) {
            lock (_lock) {
                if (_meshHandles.TryGetValue(handle, out var mesh)) {
                    mesh.RecalculateNormals();
                }
            }
        }

        /// <summary>
        /// Recalculate bounds.
        /// </summary>
        public static void RecalculateBounds(int handle) {
            lock (_lock) {
                if (_meshHandles.TryGetValue(handle, out var mesh)) {
                    mesh.RecalculateBounds();
                }
            }
        }

        /// <summary>
        /// Optimize mesh for rendering.
        /// </summary>
        public static void OptimizeMesh(int handle) {
            lock (_lock) {
                if (_meshHandles.TryGetValue(handle, out var mesh)) {
                    mesh.Optimize();
                }
            }
        }

        /// <summary>
        /// Get vertex count.
        /// </summary>
        public static int GetVertexCount(int handle) {
            lock (_lock) {
                if (_meshHandles.TryGetValue(handle, out var mesh)) {
                    return mesh.vertexCount;
                }
                return 0;
            }
        }

        /// <summary>
        /// Get triangle count.
        /// </summary>
        public static int GetTriangleCount(int handle) {
            lock (_lock) {
                if (_meshHandles.TryGetValue(handle, out var mesh)) {
                    return mesh.triangles.Length / 3;
                }
                return 0;
            }
        }

        // ============ Instantiation ============

        /// <summary>
        /// Create a GameObject with MeshFilter and MeshRenderer. Returns instance handle.
        /// </summary>
        public static int Instantiate(int meshHandle, string name) {
            lock (_lock) {
                if (!_meshHandles.TryGetValue(meshHandle, out var mesh)) {
                    Debug.LogWarning($"[MeshBridge] Mesh handle {meshHandle} not found");
                    return -1;
                }

                var go = new GameObject(name ?? "ProceduralMesh");
                if (_parentTransform != null) {
                    go.transform.SetParent(_parentTransform, false);
                }

                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;

                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = GetDefaultMaterial();

                int handle = _nextInstanceHandle++;
                _instanceHandles[handle] = go;
                return handle;
            }
        }

        /// <summary>
        /// Set position of instantiated object.
        /// </summary>
        public static void SetPosition(int instanceHandle, float x, float y, float z) {
            lock (_lock) {
                if (_instanceHandles.TryGetValue(instanceHandle, out var go)) {
                    go.transform.position = new Vector3(x, y, z);
                }
            }
        }

        /// <summary>
        /// Set rotation (euler angles) of instantiated object.
        /// </summary>
        public static void SetRotation(int instanceHandle, float x, float y, float z) {
            lock (_lock) {
                if (_instanceHandles.TryGetValue(instanceHandle, out var go)) {
                    go.transform.eulerAngles = new Vector3(x, y, z);
                }
            }
        }

        /// <summary>
        /// Set scale of instantiated object.
        /// </summary>
        public static void SetScale(int instanceHandle, float x, float y, float z) {
            lock (_lock) {
                if (_instanceHandles.TryGetValue(instanceHandle, out var go)) {
                    go.transform.localScale = new Vector3(x, y, z);
                }
            }
        }

        /// <summary>
        /// Get the Unity GameObject for an instance handle.
        /// </summary>
        public static GameObject GetGameObject(int instanceHandle) {
            lock (_lock) {
                _instanceHandles.TryGetValue(instanceHandle, out var go);
                return go;
            }
        }

        // ============ Materials ============

        /// <summary>
        /// Create a material with the specified shader. Returns handle.
        /// </summary>
        public static int CreateMaterial(string shaderName) {
            var shader = Shader.Find(shaderName);
            if (shader == null) {
                shader = Shader.Find("Standard");
            }
            if (shader == null) {
                Debug.LogWarning($"[MeshBridge] Shader '{shaderName}' not found");
                return -1;
            }

            var material = new Material(shader);
            lock (_lock) {
                int handle = _nextMaterialHandle++;
                _materialHandles[handle] = material;
                return handle;
            }
        }

        /// <summary>
        /// Set material color.
        /// </summary>
        public static void SetMaterialColor(int handle, float r, float g, float b, float a) {
            lock (_lock) {
                if (_materialHandles.TryGetValue(handle, out var material)) {
                    material.color = new Color(r, g, b, a);
                }
            }
        }

        /// <summary>
        /// Set material float property.
        /// </summary>
        public static void SetMaterialFloat(int handle, string name, float value) {
            lock (_lock) {
                if (_materialHandles.TryGetValue(handle, out var material)) {
                    material.SetFloat(name, value);
                }
            }
        }

        /// <summary>
        /// Register an external material. Returns handle.
        /// </summary>
        public static int RegisterMaterial(Material material) {
            if (material == null) return -1;

            lock (_lock) {
                int handle = _nextMaterialHandle++;
                _materialHandles[handle] = material;
                return handle;
            }
        }

        /// <summary>
        /// Assign material to a game object instance.
        /// </summary>
        public static void SetInstanceMaterial(int instanceHandle, int materialHandle) {
            lock (_lock) {
                if (!_instanceHandles.TryGetValue(instanceHandle, out var go)) return;
                if (!_materialHandles.TryGetValue(materialHandle, out var material)) return;

                var renderer = go.GetComponent<MeshRenderer>();
                if (renderer != null) {
                    renderer.sharedMaterial = material;
                }
            }
        }

        // ============ Cleanup ============

        /// <summary>
        /// Dispose a mesh handle.
        /// </summary>
        public static void DisposeMesh(int handle) {
            lock (_lock) {
                if (_meshHandles.TryGetValue(handle, out var mesh)) {
                    UnityEngine.Object.Destroy(mesh);
                    _meshHandles.Remove(handle);
                }
            }
        }

        /// <summary>
        /// Dispose an instance (destroys the GameObject).
        /// </summary>
        public static void DisposeInstance(int handle) {
            lock (_lock) {
                if (_instanceHandles.TryGetValue(handle, out var go)) {
                    UnityEngine.Object.Destroy(go);
                    _instanceHandles.Remove(handle);
                }
            }
        }

        /// <summary>
        /// Dispose a material handle.
        /// </summary>
        public static void DisposeMaterial(int handle) {
            lock (_lock) {
                if (_materialHandles.TryGetValue(handle, out var material)) {
                    UnityEngine.Object.Destroy(material);
                    _materialHandles.Remove(handle);
                }
            }
        }

        /// <summary>
        /// Clear all handles and destroy all objects.
        /// </summary>
        public static void Cleanup() {
            lock (_lock) {
                foreach (var mesh in _meshHandles.Values) {
                    if (mesh != null) UnityEngine.Object.Destroy(mesh);
                }
                foreach (var go in _instanceHandles.Values) {
                    if (go != null) UnityEngine.Object.Destroy(go);
                }
                foreach (var mat in _materialHandles.Values) {
                    if (mat != null) UnityEngine.Object.Destroy(mat);
                }

                _meshHandles.Clear();
                _instanceHandles.Clear();
                _materialHandles.Clear();
            }
        }

        // ============ Internal Helpers ============

        static int RegisterMesh(Mesh mesh) {
            lock (_lock) {
                return RegisterMeshInternal(mesh);
            }
        }

        static int RegisterMeshInternal(Mesh mesh) {
            int handle = _nextMeshHandle++;
            _meshHandles[handle] = mesh;
            return handle;
        }

        static Material _defaultMaterial;
        static Material GetDefaultMaterial() {
            if (_defaultMaterial == null) {
                var shader = Shader.Find("Standard");
                if (shader != null) {
                    _defaultMaterial = new Material(shader);
                    _defaultMaterial.color = Color.white;
                }
            }
            return _defaultMaterial;
        }
    }
}
