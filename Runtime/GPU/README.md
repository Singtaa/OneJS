# GPU Module Overview

This module provides compute shader functionality accessible from JavaScript.

## Architecture

```
JavaScript (onejs-unity/gpu)
    │
    ▼
CS.OneJS.GPU.GPUBridge (static methods)
    │
    ▼
Unity ComputeShader API
```

## Files

| File | Purpose |
|------|---------|
| `GPUBridge.cs` | Static bridge exposing compute APIs to JavaScript |
| `ComputeShaderProvider.cs` | MonoBehaviour for registering shaders via inspector |

## Usage

### 1. Register Shaders (C#)

Add `ComputeShaderProvider` to a GameObject and assign shaders:

```csharp
// Or register programmatically:
GPUBridge.Register("MyShader", myComputeShader);
```

### 2. Use from JavaScript

```typescript
import { compute, Platform } from "onejs-unity/gpu"

// Check platform support
if (!Platform.supportsCompute) {
    console.log("Compute shaders not supported")
    return
}

// Load shader by registered name
const shader = await compute.load("MyShader")

// Create buffer with initial data
const data = new Float32Array([1, 2, 3, 4])
const buffer = compute.buffer({ data })

// Dispatch kernel
shader.kernel("CSMain")
    .float("multiplier", 2.0)
    .buffer("data", buffer)
    .dispatch(1)

// Read results
const result = await buffer.read()
console.log(result) // Float32Array [2, 4, 6, 8]

// Clean up
buffer.dispose()
shader.dispose()
```

## API Reference

### GPUBridge Static Properties

| Property | Type | Description |
|----------|------|-------------|
| `SupportsCompute` | `bool` | Whether compute shaders are supported |
| `SupportsAsyncReadback` | `bool` | Whether async GPU readback is supported |
| `MaxComputeWorkGroupSizeX/Y/Z` | `int` | Maximum work group dimensions |

### GPUBridge Static Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `Register(name, shader)` | `void` | Register a shader for JS access |
| `Unregister(name)` | `void` | Remove a registered shader |
| `LoadShader(name)` | `int` | Get handle for registered shader |
| `FindKernel(handle, name)` | `int` | Get kernel index |
| `SetFloat/Int/Bool/Vector/Matrix` | `void` | Set shader uniforms |
| `CreateBuffer(count, stride)` | `int` | Create compute buffer |
| `SetBufferData(handle, json)` | `void` | Upload data to buffer |
| `BindBuffer(shader, kernel, name, buffer)` | `void` | Bind buffer to kernel |
| `Dispatch(shader, kernel, x, y, z)` | `void` | Execute kernel |
| `RequestReadback(buffer)` | `int` | Start async readback |
| `IsReadbackComplete(id)` | `bool` | Check readback status |
| `GetReadbackData(id)` | `string` | Get readback result as JSON |

## Platform Support

| Platform | Compute Support | Async Readback |
|----------|-----------------|----------------|
| Windows/macOS/Linux | ✅ | ✅ |
| iOS/Android | ✅ (most devices) | ✅ |
| WebGL | ❌ | ❌ |
| WebGPU | ✅ | ✅ |

## Zero-Allocation Per-Frame Dispatch

For per-frame GPU operations (e.g., setting uniforms, dispatching kernels), the GPU module provides truly zero-allocation bindings.

### The Problem

Using the standard CS proxy allocates on every call:
```typescript
// This allocates strings and boxes values every frame
CS.OneJS.GPU.GPUBridge.SetFloat(handle, "_Time", time)  // ❌ ~100B per call
```

### The Solution: Property ID Caching + Specialized Bindings

```typescript
import { compute, RenderTexture } from "onejs-unity/gpu"

// Create dispatcher once at init
const shader = await compute.load("MyShader")
const dispatch = shader.createDispatcher("CSMain")

// Per-frame - truly zero allocations
function update(time: number) {
    dispatch
        .float("_Time", time)           // Uses cached property ID
        .vec2("_Resolution", 1920, 1080) // Uses specialized binding
        .textureRW("_Result", texture)   // Uses specialized binding
        .dispatch(16, 16, 1)             // Uses specialized binding
}
```

### How It Works

1. **Property ID Caching**: First call to `.float("_Time", ...)` converts the string to an integer ID via `Shader.PropertyToID()`. Subsequent calls use the cached ID.

2. **Specialized Bindings**: The `KernelDispatcher` uses `BindGpu*` methods that bypass C# generics entirely - no boxing, no allocations.

3. **Native Dispatch**: Arguments are passed as primitives through native `__zaInvokeN` functions with stack-allocated arrays.

### Binding IDs

GPUBridge exposes pre-registered binding IDs via `GetZeroAllocBindingIds()`:

| Binding | Purpose |
|---------|---------|
| `setFloatById` | Set float uniform by property ID |
| `setIntById` | Set int uniform by property ID |
| `setVectorById` | Set Vector4 uniform by property ID |
| `setTextureById` | Set texture by property ID |
| `dispatch` | Dispatch compute kernel |
| `getScreenWidth` | Get screen width (cached per-frame) |
| `getScreenHeight` | Get screen height (cached per-frame) |

### Profiling

With zero-alloc bindings properly configured, `JSRunner.Update()` should show **0B GC Alloc** in Unity Profiler after warmup. The `QuickJSZeroAllocProfilerTest` demonstrates this pattern.

## Notes

- Shaders must be registered before they can be loaded from JavaScript
- Use `Resources.Load<ComputeShader>()` for test shaders
- Buffer data is transferred as JSON arrays (simple but not zero-copy)
- For high-performance scenarios, consider reducing readback frequency
- Use `createDispatcher()` for zero-alloc per-frame GPU operations
