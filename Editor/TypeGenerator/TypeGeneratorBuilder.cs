using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OneJS.Editor.TypeGenerator {
    /// <summary>
    /// Fluent builder for configuring and generating TypeScript declarations.
    /// </summary>
    /// <example>
    /// var result = new TypeGeneratorBuilder()
    ///     .AddType&lt;Vector3&gt;()
    ///     .AddType&lt;Transform&gt;()
    ///     .AddNamespace("UnityEngine.UIElements")
    ///     .AddAssemblyByName("MyGame.Core")
    ///     .IncludeDocumentation()
    ///     .ExcludeObsolete()
    ///     .Build();
    ///
    /// result.WriteTo("output.d.ts");
    /// </example>
    public class TypeGeneratorBuilder {
        private readonly HashSet<Type> _types = new();
        private readonly AnalyzerOptions _analyzerOptions = new();
        private readonly EmitterOptions _emitterOptions = new();

        #region Add Types

        /// <summary>
        /// Adds a type to the generation set.
        /// </summary>
        /// <typeparam name="T">Type to add</typeparam>
        /// <returns>This builder for chaining</returns>
        public TypeGeneratorBuilder AddType<T>() {
            return AddType(typeof(T));
        }

        /// <summary>
        /// Adds a type to the generation set.
        /// </summary>
        /// <param name="type">Type to add</param>
        /// <returns>This builder for chaining</returns>
        public TypeGeneratorBuilder AddType(Type type) {
            if (type != null && !TypeMapper.ShouldSkipType(type)) {
                _types.Add(type);
            }
            return this;
        }

        /// <summary>
        /// Adds a type by its fully qualified name. Useful for types that may not exist in all configurations.
        /// </summary>
        /// <param name="fullTypeName">Fully qualified type name (e.g., "UnityEditor.Animations.AnimatorController")</param>
        /// <returns>This builder for chaining</returns>
        public TypeGeneratorBuilder AddTypeByName(string fullTypeName) {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                if (assembly.IsDynamic) continue;
                try {
                    var type = assembly.GetType(fullTypeName);
                    if (type != null) {
                        return AddType(type);
                    }
                } catch {
                    // Continue searching in other assemblies
                }
            }
            return this;
        }

        /// <summary>
        /// Adds multiple types to the generation set.
        /// </summary>
        /// <param name="types">Types to add</param>
        /// <returns>This builder for chaining</returns>
        public TypeGeneratorBuilder AddTypes(params Type[] types) {
            foreach (var type in types) {
                AddType(type);
            }
            return this;
        }

        /// <summary>
        /// Adds multiple types to the generation set.
        /// </summary>
        /// <param name="types">Types to add</param>
        /// <returns>This builder for chaining</returns>
        public TypeGeneratorBuilder AddTypes(IEnumerable<Type> types) {
            foreach (var type in types) {
                AddType(type);
            }
            return this;
        }

        #endregion

        #region Add from Assembly

        /// <summary>
        /// Adds all public types from assemblies matching the name pattern.
        /// </summary>
        /// <param name="assemblyNamePattern">Assembly name or prefix (e.g., "UnityEngine")</param>
        /// <returns>This builder for chaining</returns>
        public TypeGeneratorBuilder AddAssemblyByName(string assemblyNamePattern) {
            var types = TypeGenerator.GetTypesFromAssembly(assemblyNamePattern);
            return AddTypes(types);
        }

        /// <summary>
        /// Adds all public types from the specified assembly.
        /// </summary>
        /// <param name="assembly">Assembly to add types from</param>
        /// <returns>This builder for chaining</returns>
        public TypeGeneratorBuilder AddAssembly(Assembly assembly) {
            if (assembly == null) return this;

            try {
                var types = assembly.GetTypes()
                    .Where(t => t.IsPublic && !TypeMapper.ShouldSkipType(t));
                AddTypes(types);
            } catch (ReflectionTypeLoadException ex) {
                var types = ex.Types
                    .Where(t => t != null && t.IsPublic && !TypeMapper.ShouldSkipType(t));
                AddTypes(types);
            }

            return this;
        }

        /// <summary>
        /// Adds all public types from assemblies matching the predicate.
        /// </summary>
        /// <param name="predicate">Filter for assemblies</param>
        /// <returns>This builder for chaining</returns>
        public TypeGeneratorBuilder AddAssembliesWhere(Func<Assembly, bool> predicate) {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .Where(predicate);

            foreach (var assembly in assemblies) {
                AddAssembly(assembly);
            }

            return this;
        }

        #endregion

        #region Add from Namespace

        /// <summary>
        /// Adds all public types from the specified namespace (prefix match).
        /// </summary>
        /// <param name="namespaceName">Namespace name or prefix</param>
        /// <returns>This builder for chaining</returns>
        public TypeGeneratorBuilder AddNamespace(string namespaceName) {
            var types = TypeGenerator.GetTypesFromNamespace(namespaceName);
            return AddTypes(types);
        }

        /// <summary>
        /// Adds types matching the predicate from all loaded assemblies.
        /// </summary>
        /// <param name="predicate">Filter for types</param>
        /// <returns>This builder for chaining</returns>
        public TypeGeneratorBuilder AddTypesWhere(Func<Type, bool> predicate) {
            var types = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .SelectMany(a => {
                    try {
                        return a.GetTypes().Where(t => t.IsPublic && !TypeMapper.ShouldSkipType(t));
                    } catch (ReflectionTypeLoadException ex) {
                        return ex.Types.Where(t => t != null && t.IsPublic && !TypeMapper.ShouldSkipType(t));
                    }
                })
                .Where(predicate);

            return AddTypes(types);
        }

        #endregion

        #region Analyzer Options

        /// <summary>
        /// Includes non-public members (protected, internal) in the output.
        /// </summary>
        /// <returns>This builder for chaining</returns>
        public TypeGeneratorBuilder IncludeNonPublic() {
            _analyzerOptions.IncludeNonPublic = true;
            return this;
        }

        /// <summary>
        /// Includes obsolete types and members in the output.
        /// </summary>
        /// <returns>This builder for chaining</returns>
        public TypeGeneratorBuilder IncludeObsolete() {
            _analyzerOptions.IncludeObsolete = true;
            _emitterOptions.IncludeObsoleteWarnings = true;
            return this;
        }

        /// <summary>
        /// Excludes obsolete types and members from the output.
        /// </summary>
        /// <returns>This builder for chaining</returns>
        public TypeGeneratorBuilder ExcludeObsolete() {
            _analyzerOptions.IncludeObsolete = false;
            _emitterOptions.IncludeObsoleteWarnings = false;
            return this;
        }

        /// <summary>
        /// Includes nested types in the output.
        /// </summary>
        /// <returns>This builder for chaining</returns>
        public TypeGeneratorBuilder IncludeNestedTypes() {
            _analyzerOptions.IncludeNestedTypes = true;
            return this;
        }

        /// <summary>
        /// Excludes nested types from the output.
        /// </summary>
        /// <returns>This builder for chaining</returns>
        public TypeGeneratorBuilder ExcludeNestedTypes() {
            _analyzerOptions.IncludeNestedTypes = false;
            return this;
        }

        #endregion

        #region Emitter Options

        /// <summary>
        /// Includes JSDoc documentation comments in the output.
        /// </summary>
        /// <returns>This builder for chaining</returns>
        public TypeGeneratorBuilder IncludeDocumentation() {
            _emitterOptions.IncludeDocumentation = true;
            return this;
        }

        /// <summary>
        /// Excludes documentation comments from the output.
        /// </summary>
        /// <returns>This builder for chaining</returns>
        public TypeGeneratorBuilder ExcludeDocumentation() {
            _emitterOptions.IncludeDocumentation = false;
            return this;
        }

        /// <summary>
        /// Emits a module declaration for importing via 'csharp'.
        /// </summary>
        /// <returns>This builder for chaining</returns>
        public TypeGeneratorBuilder EmitModuleDeclaration() {
            _emitterOptions.EmitModuleDeclaration = true;
            return this;
        }

        /// <summary>
        /// Emits the type incompatibility marker (prevents accidental type assignment).
        /// </summary>
        /// <param name="emit">Whether to emit the marker</param>
        /// <returns>This builder for chaining</returns>
        public TypeGeneratorBuilder EmitIncompatibilityMarker(bool emit = true) {
            _emitterOptions.EmitIncompatibilityMarker = emit;
            return this;
        }

        /// <summary>
        /// Uses get/set accessor syntax for read-only/write-only properties.
        /// </summary>
        /// <param name="use">Whether to use accessor syntax</param>
        /// <returns>This builder for chaining</returns>
        public TypeGeneratorBuilder UseAccessorSyntax(bool use = true) {
            _emitterOptions.UseAccessorSyntax = use;
            return this;
        }

        /// <summary>
        /// Skips emitting the header, helper types, and __keep_incompatibility symbol.
        /// Use this when generating files that will be combined into a package with shared declarations.
        /// </summary>
        /// <returns>This builder for chaining</returns>
        public TypeGeneratorBuilder SkipHeader() {
            _emitterOptions.SkipHeader = true;
            return this;
        }

        #endregion

        #region Build

        /// <summary>
        /// Builds the TypeScript declarations with the configured options.
        /// </summary>
        /// <returns>A result containing the generated content</returns>
        public TypeGeneratorResult Build() {
            var analyzer = new TypeAnalyzer(_analyzerOptions);
            var typeInfos = analyzer.AnalyzeTypes(_types);

            var emitter = new TypeScriptEmitter(_emitterOptions);
            var content = emitter.Emit(typeInfos);

            return new TypeGeneratorResult(content, _types.ToList(), typeInfos);
        }

        /// <summary>
        /// Gets the current count of types to be generated.
        /// </summary>
        public int TypeCount => _types.Count;

        /// <summary>
        /// Gets the types currently added to this builder.
        /// </summary>
        public IReadOnlyCollection<Type> Types => _types;

        #endregion
    }
}
