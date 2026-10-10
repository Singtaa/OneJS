# Type Generator

Generates TypeScript declaration files (`.d.ts`) from C# types for use with OneJS. This enables full IntelliSense and type checking when writing JavaScript/TypeScript code that interacts with Unity's C# APIs.

## Quick Start

### Using the UI

Open the Type Generator window via **Tools > OneJS > Type Generator** in the Unity menu.

1. Select assemblies from the left panel
2. Choose types from the middle panel
3. Preview the generated TypeScript in the right panel
4. Click **Generate** to write the `.d.ts` file

### Per JSRunner

Each JSRunner's Build tab has a Type Generation section: pick assemblies, and the typings are written to its output path (default `types/csharp.d.ts` inside `~/`). With **Auto Generate** on they regenerate after every domain reload (`TypeGeneratorService`); **Tools > OneJS > Regenerate All Project Typings** regenerates every runner's typings at once.

### A JSRunner's Globals

`GlobalsTypings` declares each object in a runner's Globals list in `types/jsrunner.d.ts`, beside the output path's file: `declare const ghostPrefab: CS.UnityEngine.GameObject`. A type the app's typings do not declare falls back to its nearest declared base, ending at `CS.UnityEngine.Object`, never `any`. "Declared" is read from the `.d.ts` text the app compiles against (`node_modules/unity-types` and the runner's own typings), not assumed from assembly names, since unity-types' files do not map one to one onto assemblies. The file is written when the inspector's Globals list changes, on domain reload and after typings generate, only when its content changes, and is removed when the list empties. It needs no typing assemblies. The name says where the declarations come from, as `csharp.d.ts` does, and keeps clear of the app's own `global.d.ts`.

### Using the API

#### One-Liners

```csharp
// Generate from specific types
TypeGenerator.Generate("output.d.ts", typeof(Vector3), typeof(GameObject));

// Generate from assembly
TypeGenerator.GenerateFromAssembly("output.d.ts", "UnityEngine");

// Generate from namespace
TypeGenerator.GenerateFromNamespace("output.d.ts", "UnityEngine.UIElements");

// Generate project types
TypeGenerator.GenerateProjectTypes("output.d.ts");
```

#### Fluent Builder

```csharp
TypeGenerator.Create()
    .AddType<Vector3>()
    .AddType<Transform>()
    .AddNamespace("UnityEngine.UIElements")
    .AddAssemblyByName("MyGame.Core")
    .IncludeDocumentation()
    .ExcludeObsolete()
    .Build()
    .WriteTo("Assets/Typings/index.d.ts");
```

#### Presets

```csharp
// Use built-in presets
TypeGenerator.Presets.UnityCore.WriteTo("unity-core.d.ts");
TypeGenerator.Presets.UIToolkit.WriteTo("uitoolkit.d.ts");
TypeGenerator.Presets.Physics.WriteTo("physics.d.ts");

// Combine presets
TypeGenerator.CombinePresets(
    TypeGenerator.Presets.UnityCore,
    TypeGenerator.Presets.Physics
).WriteTo("unity.d.ts");

// Get all Unity types
TypeGenerator.Presets.All.WriteTo("unity-all.d.ts");
```

## API Reference

### TypeGenerator (Static Facade)

```csharp
public static class TypeGenerator {
    // Quick generation
    static void Generate(string outputPath, params Type[] types);
    static void GenerateFromAssembly(string outputPath, string assemblyNamePattern);
    static void GenerateFromAssemblies(string outputPath, params string[] assemblyNamePatterns);
    static void GenerateFromNamespace(string outputPath, string namespaceName);
    static void GenerateProjectTypes(string outputPath = null);  // null: DefaultOutputPath
    const string DefaultOutputPath = "Assets/Gen/Typings/csharp/index.d.ts";
    static TypeGeneratorResult GenerateToResult(params Type[] types);

    // Builder access
    static TypeGeneratorBuilder Create();

    // Presets
    static class Presets {
        static TypeGeneratorResult UnityCore { get; }
        static TypeGeneratorResult UIToolkit { get; }
        static TypeGeneratorResult Physics { get; }
        static TypeGeneratorResult Animation { get; }
        static TypeGeneratorResult Audio { get; }
        static TypeGeneratorResult InputSystem { get; }
        static TypeGeneratorResult All { get; }
    }
    static TypeGeneratorResult CombinePresets(params TypeGeneratorResult[] results);

    // Utilities
    static IEnumerable<Assembly> GetAssemblies(string namePattern);
    static IEnumerable<Type> GetTypesFromAssembly(string assemblyPattern);
    static IEnumerable<Type> GetTypesFromNamespace(string namespaceName);
}
```

### TypeGeneratorBuilder

```csharp
public class TypeGeneratorBuilder {
    // Add types
    TypeGeneratorBuilder AddType<T>();
    TypeGeneratorBuilder AddType(Type type);
    TypeGeneratorBuilder AddTypes(params Type[] types);
    TypeGeneratorBuilder AddTypes(IEnumerable<Type> types);
    TypeGeneratorBuilder AddTypeByName(string fullTypeName);

    // Add from sources. The two by name leave out test assemblies (those referencing
    // NUnit) unless includeTestAssemblies is true.
    TypeGeneratorBuilder AddAssemblyByName(string assemblyNamePattern, bool includeTestAssemblies = false);
    TypeGeneratorBuilder AddAssembly(Assembly assembly);
    TypeGeneratorBuilder AddNamespace(string namespaceName, bool includeTestAssemblies = false);
    TypeGeneratorBuilder AddTypesWhere(Func<Type, bool> predicate);
    TypeGeneratorBuilder AddAssembliesWhere(Func<Assembly, bool> predicate);

    // Analyzer options
    TypeGeneratorBuilder IncludeNonPublic();
    TypeGeneratorBuilder IncludeObsolete();
    TypeGeneratorBuilder ExcludeObsolete();
    TypeGeneratorBuilder IncludeNestedTypes();
    TypeGeneratorBuilder ExcludeNestedTypes();

    // Emitter options
    TypeGeneratorBuilder IncludeDocumentation();
    TypeGeneratorBuilder ExcludeDocumentation();
    TypeGeneratorBuilder EmitModuleDeclaration();
    TypeGeneratorBuilder EmitNamespaceModules(bool emit = true);
    TypeGeneratorBuilder EmitIncompatibilityMarker(bool emit = true);
    TypeGeneratorBuilder UseAccessorSyntax(bool use = true);
    TypeGeneratorBuilder SkipHeader();

    // Build
    TypeGeneratorResult Build();
    int TypeCount { get; }
    IReadOnlyCollection<Type> Types { get; }
}
```

### TypeGeneratorResult

```csharp
public class TypeGeneratorResult {
    // Properties
    string Content { get; }
    IReadOnlyList<Type> SourceTypes { get; }
    IReadOnlyList<TsTypeInfo> TypeInfos { get; }
    int TypeCount { get; }
    int ContentSize { get; }
    int LineCount { get; }

    // Output
    void WriteTo(string path, bool refreshAssetDatabase = true);
    void WriteTo(TextWriter writer);

    // Combining
    static TypeGeneratorResult Combine(params TypeGeneratorResult[] results);
    TypeGeneratorResult CombineWith(TypeGeneratorResult other);

    // Filtering
    TypeGeneratorResult Filter(Func<Type, bool> predicate);
    TypeGeneratorResult Exclude(Func<Type, bool> predicate);

    // Conversions
    static implicit operator string(TypeGeneratorResult result);
}
```

## Generated Output Example

```typescript
// Generated by OneJS Type Generator
// Date: 2025-01-15 10:30:00

// Helper types for C# interop
declare interface $Ref<T> { __doNotAccess: T; }
declare interface $Out<T> { __doNotAccess: T; }
declare interface $Task<T> { __doNotAccess: T; }

declare namespace CS {
    const __keep_incompatibility: symbol;

    namespace UnityEngine {
        class Vector3 {
            protected [__keep_incompatibility]: never;
            public static zero: Vector3;
            public static one: Vector3;
            public x: number;
            public y: number;
            public z: number;
            constructor(x: number, y: number, z: number);
            public Normalize(): void;
            public static Dot(lhs: Vector3, rhs: Vector3): number;
        }
    }
}
```

## Architecture

```
TypeGenerator (Static Facade)
    ↓
TypeGeneratorBuilder (Fluent Configuration)
    ↓
TypeAnalyzer (C# Reflection → TsTypeInfo)
    ↓
TypeScriptEmitter (TsTypeInfo → .d.ts)
    ↓
TypeGeneratorResult (Output + Metadata)
```

### Core Components

| Component | Purpose |
|-----------|---------|
| `TypeGenerator` | Static facade with one-liner methods |
| `TypeGeneratorBuilder` | Fluent API for configuration |
| `TypeGeneratorResult` | Output container with write methods |
| `TypeGeneratorPresets` | Pre-configured type collections |
| `TypeAnalyzer` | Reflection-based type analysis |
| `TypeScriptEmitter` | TypeScript code generation |
| `TypeMapper` | C# to TypeScript type mapping |
| `TypeGeneratorService` | Per-JSRunner generation and auto-regeneration |

### Type Mappings

| C# Type | TypeScript Type |
|---------|-----------------|
| `int`, `float`, `double` | `number` |
| `long`, `ulong` | `bigint` |
| `bool` | `boolean` |
| `string` | `string` |
| `void` | `void` |
| `object` | `any` |
| `Task<T>` | `$Task<T>` |
| `ref T` | `$Ref<T>` |
| `out T` | `$Out<T>` |
| `T[]` | `System.Array$1<T>` |
| `List<T>` | `System.Collections.Generic.List$1<T>` |

A generic type is declared and referenced under one name, its C# name plus `$N` for its N type parameters (`class BaseField$1<TValueType>`, `extends BaseField$1<number>`), so `UnityEvent` and `UnityEvent$1` stay apart. A type nested in a generic one counts its parent's parameters too. `TypeMapper.TsTypeName` is the one place that name is made, for declarations and references alike.

## File Structure

```
Assets/Singtaa/OneJS/Editor/TypeGenerator/
├── TypeGenerator.cs              # Static facade
├── TypeGeneratorBuilder.cs       # Fluent builder
├── TypeGeneratorResult.cs        # Output container
├── TypeGeneratorPresets.cs       # Pre-configured presets
├── TypeGeneratorService.cs       # Per-JSRunner typings, auto-generation, Regenerate All menu
├── TypeGeneratorWindow.cs        # Editor UI window (Tools > OneJS > Type Generator)
├── TypeTreeView.cs               # TreeView for type selection
├── Analysis/
│   ├── TypeAnalyzer.cs           # Reflection analysis
│   └── TypeMapper.cs             # Type mapping
├── Emission/
│   └── TypeScriptEmitter.cs      # Code generation
├── Models/
│   ├── TsTypeInfo.cs             # Type definition model
│   ├── TsTypeRef.cs              # Type reference model
│   ├── TsMethodInfo.cs           # Method model
│   ├── TsPropertyInfo.cs         # Property/field models
│   ├── TsParameterInfo.cs        # Parameter model
│   └── TsGenericConstraint.cs    # Generic constraints
└── Tests/
    └── TypeGeneratorTests.cs     # EditMode unit tests
```

## Best Practices

1. **Use presets for common types**: They're optimized and tested
2. **Exclude obsolete types**: Keeps output clean and future-proof
3. **Include documentation**: Enables JSDoc tooltips in your IDE
4. **Combine presets**: Build custom type sets from existing presets
5. **Use the builder for complex cases**: More control over what's included

## Troubleshooting

### Missing Types

If a type isn't appearing in output:
- Check if it's public (non-public types are skipped by default)
- Check if it's compiler-generated (these are always skipped)
- Check if it's a pointer or by-ref-like type (not supported in JS)

### Large Output Files

For very large type sets:
- Use `Filter()` or `Exclude()` on results to reduce size
- Generate separate files for different subsystems
- Use the interactive UI to manually select types
