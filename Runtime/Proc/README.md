# Procedural Generation Utilities

Static utility classes for procedural mesh generation, callable from JavaScript via the CS proxy.

## MeshGenerator

Static methods that create Unity `Mesh` objects with proper vertices, normals, UVs, and triangles.

### Primitives

| Method | Parameters | Description |
|--------|------------|-------------|
| `CreateCube` | `(sizeX, sizeY, sizeZ)` | Box mesh with 24 vertices (4 per face for hard edges) |
| `CreateSphere` | `(radius, lonSegments, latSegments)` | UV sphere with configurable resolution |
| `CreateCylinder` | `(radius, height, segments)` | Cylinder with top and bottom caps |
| `CreateCone` | `(radius, height, segments)` | Cone with bottom cap |
| `CreatePlane` | `(width, height, segmentsX, segmentsZ)` | Subdivided plane on XZ axis |
| `CreateTorus` | `(radius, tubeRadius, radialSegs, tubularSegs)` | Donut shape |
| `CreateQuad` | `(width, height)` | Simple quad (2 triangles) |

### JavaScript Usage

```javascript
const { GameObject, MeshFilter, MeshRenderer, Material, Shader } = CS.UnityEngine
const MeshGenerator = CS.OneJS.Proc.MeshGenerator

// Create a sphere mesh
const sphereMesh = MeshGenerator.CreateSphere(1.0, 32, 16)

// Attach to a GameObject
const go = new GameObject("MySphere")
const filter = go.AddComponent(MeshFilter)
const renderer = go.AddComponent(MeshRenderer)

filter.mesh = sphereMesh
renderer.material = new Material(Shader.Find("Standard"))
```

### Custom Meshes

For custom geometry, create meshes directly using array marshaling:

```javascript
const mesh = new CS.UnityEngine.Mesh()
mesh.name = "CustomMesh"

// Set vertices as Vector3 array (objects with x, y, z)
mesh.vertices = [
    { x: 0, y: 0, z: 0 },
    { x: 1, y: 0, z: 0 },
    { x: 0.5, y: 1, z: 0 }
]

// Set normals
mesh.normals = [
    { x: 0, y: 0, z: -1 },
    { x: 0, y: 0, z: -1 },
    { x: 0, y: 0, z: -1 }
]

// Set UVs as Vector2 array
mesh.uv = [
    { x: 0, y: 0 },
    { x: 1, y: 0 },
    { x: 0.5, y: 1 }
]

// Set triangles as int array
mesh.triangles = new Int32Array([0, 1, 2])

mesh.RecalculateBounds()
```

See `Runtime/README.md` section "Array Marshaling" for details on how JS arrays are converted to C# arrays.
