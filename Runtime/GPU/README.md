# GPU Module Overview

Compute shaders from JavaScript (`GPUBridge`), plus the frosted glass element (`FrostedGlassElement`, `BackdropBlurManager`).

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
| `ComputeShaderProvider.cs` | MonoBehaviour for registering shaders via inspector (`registerOnAwake`, or call `Register()`) |
| `FrostedGlassElement.cs` | `[UxmlElement]` VisualElement (`ojs-frostedglass`, React `<FrostedGlass blur tint>`) showing the blurred scene behind it |
| `BackdropBlurManager.cs` | Captures and blurs the scene for every FrostedGlassElement; users never touch it |

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
| `Register(name, shader)` / `Unregister(name)` / `ClearRegistry()` | `void` | Named shader registry for JS access |
| `LoadShader(name)` | `int` | Get handle for registered shader |
| `RegisterShader(shader)` / `DisposeShader(handle)` | `int` / `void` | Handle for a shader object passed directly (`compute.register`) |
| `FindKernel(handle, name)` | `int` | Get kernel index |
| `SetFloat/Int/Bool/Vector/Matrix` | `void` | Set shader uniforms by name |
| `PropertyToID(name)` then `SetFloatById/SetIntById/SetVectorById` | `int` / `void` | Set uniforms by cached property id |
| `CreateBuffer(count, stride)` / `DisposeBuffer(handle)` | `int` / `void` | Compute buffer lifetime |
| `SetBufferData(handle, json)` | `void` | Upload data to buffer |
| `BindBuffer(shader, kernel, name, buffer)` | `void` | Bind buffer to kernel |
| `CreateRenderTexture(w, h, randomWrite)` / `ResizeRenderTexture` / `DisposeRenderTexture` | `int` / `bool` / `void` | Render targets for `textureRW` |
| `SetTexture` / `SetTextureById(shader, kernel, name or id, rt)` | `void` | Bind a render texture to a kernel |
| `SetElementBackgroundImage(element, rt)` / `ClearElementBackgroundImage(element)` | `void` | Show a render texture on a VisualElement |
| `Dispatch(shader, kernel, x, y, z)` | `void` | Execute kernel |
| `RequestReadback(buffer)` | `int` | Start async readback |
| `IsReadbackComplete(id)` | `bool` | Check readback status |
| `GetReadbackData(id)` | `string` | Get readback result as JSON |
| `GetZeroAllocBindingIds()` | `ZeroAllocBindingIds` | Binding ids for `__zaInvokeN` (see below) |
| `Cleanup()` | `void` | Dispose every buffer, texture and handle |

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

// Per frame: truly zero allocations
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

2. **Zero-alloc bindings**: `GPUBridge.InitializeZeroAllocBindings()` registers each operation once through the generic `QuickJSNative.Bind<>` overloads, which convert arguments through `UnsafeUtility.As` and therefore never box. The `KernelDispatcher` calls them by binding id.

3. **Native Dispatch**: Arguments are passed as primitives through native `__zaInvokeN` functions with stack-allocated arrays.

### Binding IDs

GPUBridge exposes pre-registered binding IDs via `GetZeroAllocBindingIds()` (registering them on first call):

| Binding | Purpose |
|---------|---------|
| `setFloatById` | Set float uniform by property ID |
| `setIntById` | Set int uniform by property ID |
| `setVectorById` | Set Vector4 uniform by property ID |
| `setTextureById` | Set texture by property ID |
| `dispatch` | Dispatch compute kernel |
| `getScreenWidth` / `getScreenHeight` | `Screen.width` / `Screen.height` |
| `propertyToId` | Name to property ID, for caching at init |
| `setFloat` / `setInt` / `setBool` / `setVector` / `setTexture` | Name-based variants (the string argument allocates; setup only) |

### Profiling

With zero-alloc bindings properly configured, `JSRunner.Update()` should show **0B GC Alloc** in Unity Profiler after warmup. The `QuickJSZeroAllocProfilerTest` demonstrates this pattern.

## Frosted Glass

`<FrostedGlass blur={10} tint="rgba(255,255,255,0.15)">` from onejs-react renders `FrostedGlassElement`. No camera or render texture setup:

- The element registers with `BackdropBlurManager` on attach and unregisters on detach. The manager creates itself with the first element and destroys itself with the last.
- The manager renders the 3D scene only (no UI) through a clone camera into a half-resolution target, then runs a two-pass Gaussian blur (`Hidden/OneJS/BackdropBlur`) sized for the largest `blur` in use. Pipeline-agnostic: Built-in, URP, HDRP.
- The element shows that target through an internal background child, counter-rotated so it stays screen-aligned, with a tint overlay above it and below the user's children.
- `blur` is in screen pixels (default 10). `tint` sets hue and opacity (default white at 0.15); the React wrapper parses only `rgb()`/`rgba()` strings.

## Notes

- Shaders must be registered before they can be loaded from JavaScript
- Use `Resources.Load<ComputeShader>()` for test shaders
- Buffer data is transferred as JSON arrays (simple but not zero-copy)
- For high-performance scenarios, consider reducing readback frequency
- Use `createDispatcher()` for zero-alloc per-frame GPU operations
