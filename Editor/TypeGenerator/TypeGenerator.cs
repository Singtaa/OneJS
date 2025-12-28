using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace OneJS.Editor.TypeGenerator {
    /// <summary>
    /// High-level static API for generating TypeScript declarations from C# types.
    /// Provides convenient one-liner methods and fluent builder access.
    /// </summary>
    /// <example>
    /// // Quick generation from types
    /// TypeGenerator.Generate("output.d.ts", typeof(Vector3), typeof(GameObject));
    ///
    /// // Generate from assembly
    /// TypeGenerator.GenerateFromAssembly("output.d.ts", "UnityEngine");
    ///
    /// // Generate from namespace
    /// TypeGenerator.GenerateFromNamespace("output.d.ts", "UnityEngine.UIElements");
    ///
    /// // Fluent builder
    /// TypeGenerator.Create()
    ///     .AddType&lt;Vector3&gt;()
    ///     .AddNamespace("UnityEngine.UIElements")
    ///     .IncludeDocumentation()
    ///     .Build()
    ///     .WriteTo("output.d.ts");
    /// </example>
    public static class TypeGenerator {
        /// <summary>
        /// Default output path for generated typings
        /// </summary>
        public const string DefaultOutputPath = "Assets/Gen/Typings/csharp/index.d.ts";

        #region Quick Generation Methods

        /// <summary>
        /// Generates TypeScript declarations for the specified types and writes to a file.
        /// </summary>
        /// <param name="outputPath">Output file path (relative to project root or absolute)</param>
        /// <param name="types">Types to generate declarations for</param>
        public static void Generate(string outputPath, params Type[] types) {
            Create()
                .AddTypes(types)
                .Build()
                .WriteTo(outputPath);
        }

        /// <summary>
        /// Generates TypeScript declarations for types in the specified assembly.
        /// </summary>
        /// <param name="outputPath">Output file path</param>
        /// <param name="assemblyNamePattern">Assembly name or prefix (e.g., "UnityEngine", "Assembly-CSharp")</param>
        public static void GenerateFromAssembly(string outputPath, string assemblyNamePattern) {
            Create()
                .AddAssemblyByName(assemblyNamePattern)
                .Build()
                .WriteTo(outputPath);
        }

        /// <summary>
        /// Generates TypeScript declarations for types in assemblies matching multiple patterns.
        /// </summary>
        /// <param name="outputPath">Output file path</param>
        /// <param name="assemblyNamePatterns">Assembly name patterns</param>
        public static void GenerateFromAssemblies(string outputPath, params string[] assemblyNamePatterns) {
            var builder = Create();
            foreach (var pattern in assemblyNamePatterns) {
                builder.AddAssemblyByName(pattern);
            }
            builder.Build().WriteTo(outputPath);
        }

        /// <summary>
        /// Generates TypeScript declarations for types in the specified namespace.
        /// </summary>
        /// <param name="outputPath">Output file path</param>
        /// <param name="namespaceName">Namespace to include (exact match or prefix)</param>
        public static void GenerateFromNamespace(string outputPath, string namespaceName) {
            Create()
                .AddNamespace(namespaceName)
                .Build()
                .WriteTo(outputPath);
        }

        /// <summary>
        /// Generates TypeScript declarations for types in the project assemblies (Assembly-CSharp*).
        /// </summary>
        /// <param name="outputPath">Output file path (defaults to DefaultOutputPath)</param>
        public static void GenerateProjectTypes(string outputPath = null) {
            Create()
                .AddAssemblyByName("Assembly-CSharp")
                .Build()
                .WriteTo(outputPath ?? DefaultOutputPath);
        }

        /// <summary>
        /// Generates a result without writing to file. Use for inspection or custom output.
        /// </summary>
        /// <param name="types">Types to generate declarations for</param>
        /// <returns>A TypeGeneratorResult containing the generated content</returns>
        public static TypeGeneratorResult GenerateToResult(params Type[] types) {
            return Create()
                .AddTypes(types)
                .Build();
        }

        #endregion

        #region Builder Access

        /// <summary>
        /// Creates a new TypeGeneratorBuilder for fluent configuration.
        /// </summary>
        /// <returns>A new builder instance</returns>
        public static TypeGeneratorBuilder Create() {
            return new TypeGeneratorBuilder();
        }

        #endregion

        #region Preset Access

        /// <summary>
        /// Pre-configured type sets for common use cases.
        /// </summary>
        public static class Presets {
            /// <summary>
            /// Core Unity types (Vector3, Quaternion, Transform, GameObject, etc.)
            /// </summary>
            public static TypeGeneratorResult UnityCore => TypeGeneratorPresets.UnityCore();

            /// <summary>
            /// UI Toolkit types (VisualElement, Label, Button, etc.)
            /// </summary>
            public static TypeGeneratorResult UIToolkit => TypeGeneratorPresets.UIToolkit();

            /// <summary>
            /// Physics types (Rigidbody, Collider, Physics, etc.)
            /// </summary>
            public static TypeGeneratorResult Physics => TypeGeneratorPresets.Physics();

            /// <summary>
            /// Animation types (Animator, AnimationClip, etc.)
            /// </summary>
            public static TypeGeneratorResult Animation => TypeGeneratorPresets.Animation();

            /// <summary>
            /// Audio types (AudioSource, AudioClip, etc.)
            /// </summary>
            public static TypeGeneratorResult Audio => TypeGeneratorPresets.Audio();

            /// <summary>
            /// Input System types (requires Input System package)
            /// </summary>
            public static TypeGeneratorResult InputSystem => TypeGeneratorPresets.InputSystem();

            /// <summary>
            /// All Unity types combined (Core + UIToolkit + Physics + Animation + Audio)
            /// </summary>
            public static TypeGeneratorResult All => TypeGeneratorPresets.All();
        }

        /// <summary>
        /// Combines multiple results into a single output.
        /// </summary>
        /// <param name="results">Results to combine</param>
        /// <returns>A combined result</returns>
        public static TypeGeneratorResult CombinePresets(params TypeGeneratorResult[] results) {
            return TypeGeneratorResult.Combine(results);
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Gets all loaded assemblies matching the specified pattern.
        /// </summary>
        /// <param name="namePattern">Name or prefix to match</param>
        /// <returns>Matching assemblies</returns>
        public static IEnumerable<Assembly> GetAssemblies(string namePattern) {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .Where(a => a.GetName().Name.StartsWith(namePattern, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets all public types from assemblies matching the pattern.
        /// </summary>
        /// <param name="assemblyPattern">Assembly name pattern</param>
        /// <returns>Public types from matching assemblies</returns>
        public static IEnumerable<Type> GetTypesFromAssembly(string assemblyPattern) {
            return GetAssemblies(assemblyPattern)
                .SelectMany(a => {
                    try {
                        return a.GetTypes().Where(t => t.IsPublic && !TypeMapper.ShouldSkipType(t));
                    } catch (ReflectionTypeLoadException ex) {
                        return ex.Types.Where(t => t != null && t.IsPublic && !TypeMapper.ShouldSkipType(t));
                    }
                });
        }

        /// <summary>
        /// Gets all public types from the specified namespace.
        /// </summary>
        /// <param name="namespaceName">Namespace name (prefix match)</param>
        /// <returns>Types in the namespace</returns>
        public static IEnumerable<Type> GetTypesFromNamespace(string namespaceName) {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .SelectMany(a => {
                    try {
                        return a.GetTypes()
                            .Where(t => t.IsPublic && !TypeMapper.ShouldSkipType(t))
                            .Where(t => t.Namespace != null && t.Namespace.StartsWith(namespaceName));
                    } catch (ReflectionTypeLoadException ex) {
                        return ex.Types
                            .Where(t => t != null && t.IsPublic && !TypeMapper.ShouldSkipType(t))
                            .Where(t => t.Namespace != null && t.Namespace.StartsWith(namespaceName));
                    }
                });
        }

        #endregion
    }
}
