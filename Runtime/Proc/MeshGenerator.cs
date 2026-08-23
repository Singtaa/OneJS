using System.Collections.Generic;
using UnityEngine;

namespace OneJS.Proc {
    /// <summary>
    /// Procedural mesh generation algorithms.
    /// </summary>
    public static class MeshGenerator {
        /// <summary>
        /// Create a cube mesh.
        /// </summary>
        public static Mesh CreateCube(float sizeX, float sizeY, float sizeZ) {
            var mesh = new Mesh();
            mesh.name = "ProceduralCube";

            float hx = sizeX * 0.5f;
            float hy = sizeY * 0.5f;
            float hz = sizeZ * 0.5f;

            // 24 vertices (4 per face for proper normals)
            var vertices = new Vector3[] {
                // Front face
                new Vector3(-hx, -hy, hz), new Vector3(hx, -hy, hz),
                new Vector3(hx, hy, hz), new Vector3(-hx, hy, hz),
                // Back face
                new Vector3(hx, -hy, -hz), new Vector3(-hx, -hy, -hz),
                new Vector3(-hx, hy, -hz), new Vector3(hx, hy, -hz),
                // Top face
                new Vector3(-hx, hy, hz), new Vector3(hx, hy, hz),
                new Vector3(hx, hy, -hz), new Vector3(-hx, hy, -hz),
                // Bottom face
                new Vector3(-hx, -hy, -hz), new Vector3(hx, -hy, -hz),
                new Vector3(hx, -hy, hz), new Vector3(-hx, -hy, hz),
                // Right face
                new Vector3(hx, -hy, hz), new Vector3(hx, -hy, -hz),
                new Vector3(hx, hy, -hz), new Vector3(hx, hy, hz),
                // Left face
                new Vector3(-hx, -hy, -hz), new Vector3(-hx, -hy, hz),
                new Vector3(-hx, hy, hz), new Vector3(-hx, hy, -hz)
            };

            var normals = new Vector3[] {
                // Front
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
                // Back
                Vector3.back, Vector3.back, Vector3.back, Vector3.back,
                // Top
                Vector3.up, Vector3.up, Vector3.up, Vector3.up,
                // Bottom
                Vector3.down, Vector3.down, Vector3.down, Vector3.down,
                // Right
                Vector3.right, Vector3.right, Vector3.right, Vector3.right,
                // Left
                Vector3.left, Vector3.left, Vector3.left, Vector3.left
            };

            var uvs = new Vector2[] {
                // Front
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                // Back
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                // Top
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                // Bottom
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                // Right
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                // Left
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1)
            };

            var triangles = new int[] {
                0, 2, 1, 0, 3, 2,       // Front
                4, 6, 5, 4, 7, 6,       // Back
                8, 10, 9, 8, 11, 10,    // Top
                12, 14, 13, 12, 15, 14, // Bottom
                16, 18, 17, 16, 19, 18, // Right
                20, 22, 21, 20, 23, 22  // Left
            };

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>
        /// Create a UV sphere mesh.
        /// </summary>
        public static Mesh CreateSphere(float radius, int longitudeSegments, int latitudeSegments) {
            var mesh = new Mesh();
            mesh.name = "ProceduralSphere";

            longitudeSegments = Mathf.Max(3, longitudeSegments);
            latitudeSegments = Mathf.Max(2, latitudeSegments);

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            // Generate vertices
            for (int lat = 0; lat <= latitudeSegments; lat++) {
                float theta = lat * Mathf.PI / latitudeSegments;
                float sinTheta = Mathf.Sin(theta);
                float cosTheta = Mathf.Cos(theta);

                for (int lon = 0; lon <= longitudeSegments; lon++) {
                    float phi = lon * 2 * Mathf.PI / longitudeSegments;
                    float sinPhi = Mathf.Sin(phi);
                    float cosPhi = Mathf.Cos(phi);

                    float x = cosPhi * sinTheta;
                    float y = cosTheta;
                    float z = sinPhi * sinTheta;

                    vertices.Add(new Vector3(x, y, z) * radius);
                    normals.Add(new Vector3(x, y, z));
                    uvs.Add(new Vector2((float)lon / longitudeSegments, 1f - (float)lat / latitudeSegments));
                }
            }

            // Generate triangles
            for (int lat = 0; lat < latitudeSegments; lat++) {
                for (int lon = 0; lon < longitudeSegments; lon++) {
                    int current = lat * (longitudeSegments + 1) + lon;
                    int next = current + longitudeSegments + 1;

                    triangles.Add(current);
                    triangles.Add(next + 1);
                    triangles.Add(current + 1);

                    triangles.Add(current);
                    triangles.Add(next);
                    triangles.Add(next + 1);
                }
            }

            mesh.vertices = vertices.ToArray();
            mesh.normals = normals.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>
        /// Create a cylinder mesh.
        /// </summary>
        public static Mesh CreateCylinder(float radius, float height, int segments) {
            var mesh = new Mesh();
            mesh.name = "ProceduralCylinder";

            segments = Mathf.Max(3, segments);

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            float halfHeight = height * 0.5f;

            // Side vertices
            for (int i = 0; i <= segments; i++) {
                float angle = i * 2 * Mathf.PI / segments;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                Vector3 normal = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));

                // Bottom ring
                vertices.Add(new Vector3(x, -halfHeight, z));
                normals.Add(normal);
                uvs.Add(new Vector2((float)i / segments, 0));

                // Top ring
                vertices.Add(new Vector3(x, halfHeight, z));
                normals.Add(normal);
                uvs.Add(new Vector2((float)i / segments, 1));
            }

            // Side triangles
            for (int i = 0; i < segments; i++) {
                int bl = i * 2;
                int tl = bl + 1;
                int br = bl + 2;
                int tr = bl + 3;

                triangles.Add(bl); triangles.Add(tl); triangles.Add(br);
                triangles.Add(br); triangles.Add(tl); triangles.Add(tr);
            }

            // Top cap
            int topCenterIdx = vertices.Count;
            vertices.Add(new Vector3(0, halfHeight, 0));
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int i = 0; i <= segments; i++) {
                float angle = i * 2 * Mathf.PI / segments;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;

                vertices.Add(new Vector3(x, halfHeight, z));
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(Mathf.Cos(angle) * 0.5f + 0.5f, Mathf.Sin(angle) * 0.5f + 0.5f));
            }

            for (int i = 0; i < segments; i++) {
                triangles.Add(topCenterIdx);
                triangles.Add(topCenterIdx + 1 + i);
                triangles.Add(topCenterIdx + 2 + i);
            }

            // Bottom cap
            int bottomCenterIdx = vertices.Count;
            vertices.Add(new Vector3(0, -halfHeight, 0));
            normals.Add(Vector3.down);
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int i = 0; i <= segments; i++) {
                float angle = i * 2 * Mathf.PI / segments;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;

                vertices.Add(new Vector3(x, -halfHeight, z));
                normals.Add(Vector3.down);
                uvs.Add(new Vector2(Mathf.Cos(angle) * 0.5f + 0.5f, Mathf.Sin(angle) * 0.5f + 0.5f));
            }

            for (int i = 0; i < segments; i++) {
                triangles.Add(bottomCenterIdx);
                triangles.Add(bottomCenterIdx + 2 + i);
                triangles.Add(bottomCenterIdx + 1 + i);
            }

            mesh.vertices = vertices.ToArray();
            mesh.normals = normals.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>
        /// Create a cone mesh.
        /// </summary>
        public static Mesh CreateCone(float radius, float height, int segments) {
            var mesh = new Mesh();
            mesh.name = "ProceduralCone";

            segments = Mathf.Max(3, segments);

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            float halfHeight = height * 0.5f;

            // Apex
            int apexIdx = 0;
            vertices.Add(new Vector3(0, halfHeight, 0));
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 1));

            // Side vertices
            float slopeAngle = Mathf.Atan2(radius, height);
            float cosSlope = Mathf.Cos(slopeAngle);
            float sinSlope = Mathf.Sin(slopeAngle);

            for (int i = 0; i <= segments; i++) {
                float angle = i * 2 * Mathf.PI / segments;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;

                // Normal points outward and up along the cone surface
                Vector3 normal = new Vector3(Mathf.Cos(angle) * cosSlope, sinSlope, Mathf.Sin(angle) * cosSlope).normalized;

                vertices.Add(new Vector3(x, -halfHeight, z));
                normals.Add(normal);
                uvs.Add(new Vector2((float)i / segments, 0));
            }

            // Side triangles
            for (int i = 0; i < segments; i++) {
                triangles.Add(apexIdx);
                triangles.Add(1 + i);
                triangles.Add(2 + i);
            }

            // Bottom cap
            int bottomCenterIdx = vertices.Count;
            vertices.Add(new Vector3(0, -halfHeight, 0));
            normals.Add(Vector3.down);
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int i = 0; i <= segments; i++) {
                float angle = i * 2 * Mathf.PI / segments;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;

                vertices.Add(new Vector3(x, -halfHeight, z));
                normals.Add(Vector3.down);
                uvs.Add(new Vector2(Mathf.Cos(angle) * 0.5f + 0.5f, Mathf.Sin(angle) * 0.5f + 0.5f));
            }

            for (int i = 0; i < segments; i++) {
                triangles.Add(bottomCenterIdx);
                triangles.Add(bottomCenterIdx + 2 + i);
                triangles.Add(bottomCenterIdx + 1 + i);
            }

            mesh.vertices = vertices.ToArray();
            mesh.normals = normals.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>
        /// Create a plane mesh.
        /// </summary>
        public static Mesh CreatePlane(float width, float height, int segmentsX, int segmentsZ) {
            var mesh = new Mesh();
            mesh.name = "ProceduralPlane";

            segmentsX = Mathf.Max(1, segmentsX);
            segmentsZ = Mathf.Max(1, segmentsZ);

            int vertCountX = segmentsX + 1;
            int vertCountZ = segmentsZ + 1;

            var vertices = new Vector3[vertCountX * vertCountZ];
            var normals = new Vector3[vertCountX * vertCountZ];
            var uvs = new Vector2[vertCountX * vertCountZ];

            float halfWidth = width * 0.5f;
            float halfHeight = height * 0.5f;

            for (int z = 0; z < vertCountZ; z++) {
                for (int x = 0; x < vertCountX; x++) {
                    int idx = z * vertCountX + x;
                    float px = ((float)x / segmentsX - 0.5f) * width;
                    float pz = ((float)z / segmentsZ - 0.5f) * height;

                    vertices[idx] = new Vector3(px, 0, pz);
                    normals[idx] = Vector3.up;
                    uvs[idx] = new Vector2((float)x / segmentsX, (float)z / segmentsZ);
                }
            }

            var triangles = new int[segmentsX * segmentsZ * 6];
            int ti = 0;

            for (int z = 0; z < segmentsZ; z++) {
                for (int x = 0; x < segmentsX; x++) {
                    int bl = z * vertCountX + x;
                    int br = bl + 1;
                    int tl = bl + vertCountX;
                    int tr = tl + 1;

                    triangles[ti++] = bl;
                    triangles[ti++] = tl;
                    triangles[ti++] = br;
                    triangles[ti++] = br;
                    triangles[ti++] = tl;
                    triangles[ti++] = tr;
                }
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>
        /// Create a torus mesh.
        /// </summary>
        public static Mesh CreateTorus(float radius, float tubeRadius, int radialSegments, int tubularSegments) {
            var mesh = new Mesh();
            mesh.name = "ProceduralTorus";

            radialSegments = Mathf.Max(3, radialSegments);
            tubularSegments = Mathf.Max(3, tubularSegments);

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            for (int i = 0; i <= radialSegments; i++) {
                float u = (float)i / radialSegments * 2 * Mathf.PI;
                float cosU = Mathf.Cos(u);
                float sinU = Mathf.Sin(u);

                for (int j = 0; j <= tubularSegments; j++) {
                    float v = (float)j / tubularSegments * 2 * Mathf.PI;
                    float cosV = Mathf.Cos(v);
                    float sinV = Mathf.Sin(v);

                    float x = (radius + tubeRadius * cosV) * cosU;
                    float y = tubeRadius * sinV;
                    float z = (radius + tubeRadius * cosV) * sinU;

                    vertices.Add(new Vector3(x, y, z));

                    // Normal = direction from center of tube to vertex
                    Vector3 centerOfTube = new Vector3(radius * cosU, 0, radius * sinU);
                    normals.Add((new Vector3(x, y, z) - centerOfTube).normalized);

                    uvs.Add(new Vector2((float)i / radialSegments, (float)j / tubularSegments));
                }
            }

            for (int i = 0; i < radialSegments; i++) {
                for (int j = 0; j < tubularSegments; j++) {
                    int a = i * (tubularSegments + 1) + j;
                    int b = a + tubularSegments + 1;
                    int c = a + 1;
                    int d = b + 1;

                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    triangles.Add(c); triangles.Add(b); triangles.Add(d);
                }
            }

            mesh.vertices = vertices.ToArray();
            mesh.normals = normals.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>
        /// Create a quad mesh.
        /// </summary>
        public static Mesh CreateQuad(float width, float height) {
            var mesh = new Mesh();
            mesh.name = "ProceduralQuad";

            float hx = width * 0.5f;
            float hy = height * 0.5f;

            var vertices = new Vector3[] {
                new Vector3(-hx, -hy, 0),
                new Vector3(hx, -hy, 0),
                new Vector3(hx, hy, 0),
                new Vector3(-hx, hy, 0)
            };

            var normals = new Vector3[] {
                Vector3.back, Vector3.back, Vector3.back, Vector3.back
            };

            var uvs = new Vector2[] {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1),
                new Vector2(0, 1)
            };

            var triangles = new int[] {
                0, 2, 1, 0, 3, 2
            };

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}
