using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace OneJS.Editor.TypeGenerator.Tests {
    /// <summary>
    /// Tests for the TypeGenerator facade and related classes.
    /// </summary>
    [TestFixture]
    public class TypeGeneratorTests {
        #region TypeGenerator Static Methods

        [Test]
        public void GenerateToResult_WithSingleType_ReturnsValidContent() {
            var result = TypeGenerator.GenerateToResult(typeof(Vector3));

            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Content);
            Assert.Greater(result.Content.Length, 0);
            Assert.AreEqual(1, result.TypeCount);
            StringAssert.Contains("Vector3", result.Content);
            StringAssert.Contains("declare namespace CS", result.Content);
        }

        [Test]
        public void GenerateToResult_WithMultipleTypes_ReturnsAllTypes() {
            var result = TypeGenerator.GenerateToResult(typeof(Vector3), typeof(Quaternion));

            Assert.AreEqual(2, result.TypeCount);
            StringAssert.Contains("Vector3", result.Content);
            StringAssert.Contains("Quaternion", result.Content);
        }

        [Test]
        public void GetTypesFromAssembly_WithValidPattern_ReturnsTypes() {
            var types = TypeGenerator.GetTypesFromAssembly("UnityEngine").ToList();

            Assert.Greater(types.Count, 0);
            Assert.IsTrue(types.Any(t => t.Name == "Vector3"));
        }

        [Test]
        public void GetTypesFromNamespace_WithValidNamespace_ReturnsTypes() {
            var types = TypeGenerator.GetTypesFromNamespace("UnityEngine").ToList();

            Assert.Greater(types.Count, 0);
            Assert.IsTrue(types.Any(t => t.Name == "Vector3"));
        }

        #endregion

        #region TypeGeneratorBuilder

        [Test]
        public void Builder_AddType_Generic_AddsType() {
            var builder = TypeGenerator.Create()
                .AddType<Vector3>();

            Assert.AreEqual(1, builder.TypeCount);
            Assert.IsTrue(builder.Types.Contains(typeof(Vector3)));
        }

        [Test]
        public void Builder_AddTypes_AddsMultipleTypes() {
            var builder = TypeGenerator.Create()
                .AddTypes(typeof(Vector3), typeof(Quaternion), typeof(Transform));

            Assert.AreEqual(3, builder.TypeCount);
        }

        [Test]
        public void Builder_AddNamespace_AddsTypesFromNamespace() {
            var builder = TypeGenerator.Create()
                .AddNamespace("UnityEngine.UIElements");

            Assert.Greater(builder.TypeCount, 0);
        }

        [Test]
        public void Builder_AddAssemblyByName_AddsTypesFromAssembly() {
            var builder = TypeGenerator.Create()
                .AddAssemblyByName("UnityEngine");

            Assert.Greater(builder.TypeCount, 0);
        }

        [Test]
        public void Builder_ChainedMethods_ReturnsBuilder() {
            var builder = TypeGenerator.Create()
                .AddType<Vector3>()
                .AddType<Quaternion>()
                .IncludeDocumentation()
                .ExcludeObsolete()
                .EmitIncompatibilityMarker();

            Assert.IsNotNull(builder);
            Assert.AreEqual(2, builder.TypeCount);
        }

        [Test]
        public void Builder_Build_ReturnsValidResult() {
            var result = TypeGenerator.Create()
                .AddType<Vector3>()
                .AddType<Transform>()
                .Build();

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.TypeCount);
            Assert.Greater(result.ContentSize, 0);
            Assert.Greater(result.LineCount, 0);
        }

        [Test]
        public void Builder_AddTypesWhere_FiltersTypes() {
            var builder = TypeGenerator.Create()
                .AddTypesWhere(t => t.Namespace == "UnityEngine" && t.Name.StartsWith("Vector"));

            Assert.Greater(builder.TypeCount, 0);
            Assert.IsTrue(builder.Types.All(t => t.Name.StartsWith("Vector")));
        }

        #endregion

        #region TypeGeneratorResult

        [Test]
        public void Result_ImplicitStringConversion_ReturnsContent() {
            var result = TypeGenerator.GenerateToResult(typeof(Vector3));
            string content = result;

            Assert.AreEqual(result.Content, content);
        }

        [Test]
        public void Result_ToString_ReturnsContent() {
            var result = TypeGenerator.GenerateToResult(typeof(Vector3));

            Assert.AreEqual(result.Content, result.ToString());
        }

        [Test]
        public void Result_Filter_FiltersTypes() {
            var result = TypeGenerator.GenerateToResult(typeof(Vector3), typeof(Vector2), typeof(Quaternion));
            var filtered = result.Filter(t => t.Name.StartsWith("Vector"));

            Assert.AreEqual(2, filtered.TypeCount);
            StringAssert.Contains("Vector3", filtered.Content);
            StringAssert.Contains("Vector2", filtered.Content);
            StringAssert.DoesNotContain("Quaternion", filtered.Content);
        }

        [Test]
        public void Result_Exclude_ExcludesTypes() {
            var result = TypeGenerator.GenerateToResult(typeof(Vector3), typeof(Vector2), typeof(Quaternion));
            var filtered = result.Exclude(t => t.Name == "Quaternion");

            Assert.AreEqual(2, filtered.TypeCount);
            StringAssert.DoesNotContain("Quaternion", filtered.Content);
        }

        [Test]
        public void Result_Combine_CombinesResults() {
            var result1 = TypeGenerator.GenerateToResult(typeof(Vector3));
            var result2 = TypeGenerator.GenerateToResult(typeof(Quaternion));
            var combined = TypeGeneratorResult.Combine(result1, result2);

            Assert.AreEqual(2, combined.TypeCount);
            StringAssert.Contains("Vector3", combined.Content);
            StringAssert.Contains("Quaternion", combined.Content);
        }

        [Test]
        public void Result_CombineWith_CombinesResults() {
            var result1 = TypeGenerator.GenerateToResult(typeof(Vector3));
            var result2 = TypeGenerator.GenerateToResult(typeof(Quaternion));
            var combined = result1.CombineWith(result2);

            Assert.AreEqual(2, combined.TypeCount);
        }

        [Test]
        public void Result_WriteTo_TextWriter_WritesContent() {
            var result = TypeGenerator.GenerateToResult(typeof(Vector3));
            using var writer = new StringWriter();

            result.WriteTo(writer);

            Assert.AreEqual(result.Content, writer.ToString());
        }

        #endregion

        #region TypeGeneratorPresets

        [Test]
        public void Presets_UnityCore_ReturnsValidResult() {
            var result = TypeGenerator.Presets.UnityCore;

            Assert.IsNotNull(result);
            Assert.Greater(result.TypeCount, 0);
            StringAssert.Contains("Vector3", result.Content);
            StringAssert.Contains("GameObject", result.Content);
            StringAssert.Contains("Transform", result.Content);
        }

        [Test]
        public void Presets_UIToolkit_ReturnsValidResult() {
            var result = TypeGenerator.Presets.UIToolkit;

            Assert.IsNotNull(result);
            Assert.Greater(result.TypeCount, 0);
            StringAssert.Contains("VisualElement", result.Content);
        }

        [Test]
        public void Presets_Physics_ReturnsValidResult() {
            var result = TypeGenerator.Presets.Physics;

            Assert.IsNotNull(result);
            Assert.Greater(result.TypeCount, 0);
            StringAssert.Contains("Rigidbody", result.Content);
            StringAssert.Contains("Collider", result.Content);
        }

        [Test]
        public void Presets_Animation_ReturnsValidResult() {
            var result = TypeGenerator.Presets.Animation;

            Assert.IsNotNull(result);
            Assert.Greater(result.TypeCount, 0);
            StringAssert.Contains("Animator", result.Content);
        }

        [Test]
        public void Presets_Audio_ReturnsValidResult() {
            var result = TypeGenerator.Presets.Audio;

            Assert.IsNotNull(result);
            Assert.Greater(result.TypeCount, 0);
            StringAssert.Contains("AudioSource", result.Content);
        }

        [Test]
        public void Presets_All_CombinesAllPresets() {
            var all = TypeGenerator.Presets.All;
            var core = TypeGenerator.Presets.UnityCore;
            var physics = TypeGenerator.Presets.Physics;

            Assert.IsNotNull(all);
            Assert.GreaterOrEqual(all.TypeCount, core.TypeCount);
            StringAssert.Contains("Vector3", all.Content);
            StringAssert.Contains("Rigidbody", all.Content);
        }

        [Test]
        public void CombinePresets_CombinesMultiplePresets() {
            var combined = TypeGenerator.CombinePresets(
                TypeGenerator.Presets.UnityCore,
                TypeGenerator.Presets.Physics
            );

            Assert.IsNotNull(combined);
            StringAssert.Contains("Vector3", combined.Content);
            StringAssert.Contains("Rigidbody", combined.Content);
        }

        #endregion

        #region Content Validation

        [Test]
        public void GeneratedContent_HasHeader() {
            var result = TypeGenerator.GenerateToResult(typeof(Vector3));

            StringAssert.Contains("Generated by OneJS Type Generator", result.Content);
        }

        [Test]
        public void GeneratedContent_HasHelperTypes() {
            var result = TypeGenerator.GenerateToResult(typeof(Vector3));

            StringAssert.Contains("$Ref<T>", result.Content);
            StringAssert.Contains("$Out<T>", result.Content);
            StringAssert.Contains("$Task<T>", result.Content);
        }

        [Test]
        public void GeneratedContent_HasNamespaceWrapper() {
            var result = TypeGenerator.GenerateToResult(typeof(Vector3));

            StringAssert.Contains("declare namespace CS", result.Content);
            StringAssert.Contains("namespace UnityEngine", result.Content);
        }

        [Test]
        public void NamespaceModules_AreOffByDefault() {
            // On by default they would redeclare the engine namespaces that
            // unity-types already ships, and two `export =` for one module do
            // not merge.
            var result = TypeGenerator.GenerateToResult(typeof(Vector3));

            StringAssert.DoesNotContain("declare module \"UnityEngine\"", result.Content);
        }

        [Test]
        public void NamespaceModules_LetProjectCodeBeImported() {
            var result = TypeGenerator.Create()
                .AddType<Vector3>()
                .EmitNamespaceModules()
                .Build();

            StringAssert.Contains("declare module \"UnityEngine\"", result.Content);
            StringAssert.Contains("export = CS.UnityEngine;", result.Content);
        }

        [Test]
        public void NamespaceModules_SeparateNestedNamespacesWithSlashes() {
            // "UnityEngine/UIElements", the shape unity-types uses and the one
            // the esbuild import transform resolves.
            var result = TypeGenerator.Create()
                .AddType<UnityEngine.UIElements.VisualElement>()
                .EmitNamespaceModules()
                .Build();

            StringAssert.Contains("declare module \"UnityEngine/UIElements\"", result.Content);
            StringAssert.Contains("export = CS.UnityEngine.UIElements;", result.Content);
        }

        [Test]
        public void GeneratedContent_HasIncompatibilityMarker() {
            var result = TypeGenerator.Create()
                .AddType<Vector3>()
                .EmitIncompatibilityMarker()
                .Build();

            StringAssert.Contains("__keep_incompatibility", result.Content);
        }

        [Test]
        public void GeneratedContent_HasClassDeclaration() {
            var result = TypeGenerator.GenerateToResult(typeof(Vector3));

            StringAssert.Contains("class Vector3", result.Content);
        }

        [Test]
        public void GeneratedContent_HasStaticMembers() {
            var result = TypeGenerator.GenerateToResult(typeof(Vector3));

            StringAssert.Contains("static", result.Content);
        }

        #endregion

        #region Edge Cases

        [Test]
        public void GenerateToResult_WithNoTypes_ReturnsEmptyResult() {
            var result = TypeGenerator.GenerateToResult();

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.TypeCount);
        }

        [Test]
        public void GenerateToResult_WithNullType_SkipsNull() {
            var result = TypeGenerator.GenerateToResult(null, typeof(Vector3));

            Assert.AreEqual(1, result.TypeCount);
        }

        [Test]
        public void Builder_AddType_WithNull_DoesNotThrow() {
            var builder = TypeGenerator.Create()
                .AddType(null)
                .AddType<Vector3>();

            Assert.AreEqual(1, builder.TypeCount);
        }

        [Test]
        public void Builder_DuplicateTypes_DeduplicatesAutomatically() {
            var builder = TypeGenerator.Create()
                .AddType<Vector3>()
                .AddType<Vector3>()
                .AddType<Vector3>();

            Assert.AreEqual(1, builder.TypeCount);
        }

        #endregion

        #region Arrays

        /// <summary>
        /// A C# array Type's Name carries its brackets ("Single[]"), and the
        /// invalid-TypeScript-identifier guard rejects '[' and ']'. With that
        /// guard ahead of the array branch, every array mapped to `any`: the
        /// branch was dead code, and unity-types carried ~1300 hand-written
        /// System.Array$1&lt;T&gt; annotations patching the output by hand.
        /// </summary>
        [Test]
        public void MapType_Array_IsAnArrayNotAny() {
            var mapped = TypeMapper.MapType(typeof(float[]));

            Assert.IsTrue(mapped.IsArray, "a float[] must map as an array");
            Assert.AreEqual("System.Array$1<number>", mapped.ToTypeScript());
        }

        [Test]
        public void MapType_ArrayOfReferenceType_KeepsItsElementType() {
            Assert.AreEqual("System.Array$1<string>", TypeMapper.MapType(typeof(string[])).ToTypeScript());
            Assert.AreEqual("System.Array$1<UnityEngine.Vector3>", TypeMapper.MapType(typeof(Vector3[])).ToTypeScript());
        }

        /// <summary>
        /// The element still goes through MapType, so an array of something
        /// unrepresentable degrades to Array$1&lt;any&gt; and not to a bare any:
        /// the caller still learns it is holding an array.
        /// </summary>
        [Test]
        public void MapType_ArrayOfPointer_KeepsTheArrayAndDegradesTheElement() {
            var mapped = TypeMapper.MapType(typeof(int).MakePointerType().MakeArrayType());

            Assert.IsTrue(mapped.IsArray);
            Assert.AreEqual("System.Array$1<any>", mapped.ToTypeScript());
        }

        [Test]
        public void MapType_NestedArray_NestsBothLevels() {
            Assert.AreEqual("System.Array$1<System.Array$1<number>>",
                TypeMapper.MapType(typeof(float[][])).ToTypeScript());
        }

        /// <summary>
        /// The signature that sent this looking: AudioSource.GetOutputData takes
        /// a float[] and returned `any` in every published unity-types build the
        /// generator produced.
        /// </summary>
        [Test]
        public void Generate_MethodTakingAnArray_TypesTheParameter() {
            var result = TypeGenerator.GenerateToResult(typeof(AudioSource));

            StringAssert.Contains("System.Array$1<number>", result.Content);
        }

        #endregion

        #region Type parameters widen to TypeLike

        /// <summary>
        /// JS passes a class reference (a CS path proxy) where C# declares a
        /// System.Type, so a Type PARAMETER has to accept both. unity-types
        /// carried ~118 of these patched by hand before the rule lived here.
        /// </summary>
        [Test]
        public void Emit_TypeParameter_WidensToTypeLike() {
            var p = new TsParameterInfo { Name = "type", Type = TypeMapper.MapType(typeof(Type)) };

            Assert.AreEqual("$type: System.TypeLike", p.ToTypeScript());
        }

        [Test]
        public void Emit_ParamsArrayOfType_WidensItsElement() {
            var p = new TsParameterInfo {
                Name = "components", IsParams = true, Type = TypeMapper.MapType(typeof(Type[]))
            };

            Assert.AreEqual("...components: System.TypeLike[]", p.ToTypeScript());
        }

        /// <summary>
        /// The other direction must NOT widen: a member returning a Type returns
        /// a real one, and saying TypeLike there would let a caller treat it as
        /// a class reference it is not.
        /// </summary>
        [Test]
        public void Emit_TypeReturn_StaysType() {
            var result = TypeGenerator.GenerateToResult(typeof(System.Reflection.MethodInfo));

            StringAssert.Contains("): System.Type", result.Content);
            StringAssert.DoesNotContain("): System.TypeLike", result.Content);
        }

        [Test]
        public void Emit_NonTypeParameter_IsUntouched() {
            var p = new TsParameterInfo { Name = "n", Type = TypeMapper.MapType(typeof(int)) };

            Assert.AreEqual("$n: number", p.ToTypeScript());
        }

        #endregion

        #region Overload curation

        /// <summary>
        /// JS hands C# the class reference as a VALUE, so this is the form OneJS
        /// callers must write: a type parameter would erase and leave the runtime
        /// nothing to dispatch on. Without this overload `GetComponent(MeshRenderer)`
        /// returns Component and every member access after it fails to typecheck.
        /// unity-types carried these by hand until 6000.5.0 regenerated without them.
        /// </summary>
        [Test]
        public void Curate_TypeArgumentGetter_ReturnsTheTypeItWasGiven() {
            var result = TypeGenerator.GenerateToResult(typeof(UnityEngine.GameObject));

            StringAssert.Contains(
                "GetComponent<T extends UnityEngine.Component>($type: { new(...args: any[]): T }): T",
                result.Content);
            StringAssert.Contains(
                "AddComponent<T extends UnityEngine.Component>($componentType: { new(...args: any[]): T }): T",
                result.Content);
        }

        /// <summary>
        /// The curation must not fire when the generic sibling returns T[] rather
        /// than a bare T: there the type argument refines the ELEMENT, so binding it
        /// to the whole return value would claim GetComponents gives back one component.
        /// </summary>
        [Test]
        public void Curate_CollectionReturningGetter_IsLeftAlone() {
            var result = TypeGenerator.GenerateToResult(typeof(UnityEngine.GameObject));

            StringAssert.DoesNotContain(
                "GetComponents<T extends UnityEngine.Component>($type: { new(...args: any[]): T }): T",
                result.Content);
        }

        /// <summary>
        /// TypeScript resolves to the FIRST matching overload, so a non-generic
        /// sibling sitting ahead of a generic one silently wins and widens the
        /// return type. Order is part of the contract, which is how a regeneration
        /// that kept every signature still broke `Instantiate(material)`.
        /// </summary>
        [Test]
        public void Curate_GenericOverloads_ComeBeforeTheirNonGenericSiblings() {
            var result = TypeGenerator.GenerateToResult(typeof(UnityEngine.Object));
            var lines = result.Content.Split('\n');

            var firstGeneric = Array.FindIndex(lines, l => l.Contains("static Instantiate<"));
            var firstNonGeneric = Array.FindIndex(lines, l => l.Contains("static Instantiate($"));

            Assert.Greater(firstGeneric, -1, "no generic Instantiate overload was emitted");
            Assert.Greater(firstNonGeneric, -1, "no non-generic Instantiate overload was emitted");
            Assert.Less(firstGeneric, firstNonGeneric,
                "the non-generic Instantiate precedes the generic one, so Instantiate(material) resolves to Object");
        }

        #endregion
    }
}
