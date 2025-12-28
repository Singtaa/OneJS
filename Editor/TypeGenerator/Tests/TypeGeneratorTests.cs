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
    }
}
