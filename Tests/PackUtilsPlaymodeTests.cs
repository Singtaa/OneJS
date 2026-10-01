using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests {
    /// <summary>
    /// PlayMode tests for PackUtils methods that require QuickJSUIBridge.
    /// Tests JavaScript injection for pack globals and platform defines.
    /// </summary>
    [TestFixture]
    public class PackUtilsPlaymodeTests {
        QuickJSUIBridge _bridge;
        VisualElement _root;

        [UnitySetUp]
        public IEnumerator SetUp() {
            _root = new VisualElement();
            _bridge = new QuickJSUIBridge(_root, Application.temporaryCachePath);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            _bridge?.Dispose();
            _bridge = null;
            _root = null;
            QuickJSNative.ClearAllHandles();
            yield return null;
        }

        // MARK: InjectPlatformDefines Tests

        [UnityTest]
        public IEnumerator InjectPlatformDefines_InjectsUnityEditor() {
            RunnerUtils.InjectPlatformDefines(_bridge);

            var result = _bridge.Eval("typeof UNITY_EDITOR");
            Assert.AreEqual("boolean", result, "UNITY_EDITOR should be a boolean");

            // In editor tests, this should be true
            result = _bridge.Eval("UNITY_EDITOR");
            Assert.AreEqual("true", result, "UNITY_EDITOR should be true in editor");

            yield return null;
        }

        [UnityTest]
        public IEnumerator InjectPlatformDefines_InjectsUnityWebGL() {
            RunnerUtils.InjectPlatformDefines(_bridge);

            var result = _bridge.Eval("typeof UNITY_WEBGL");
            Assert.AreEqual("boolean", result, "UNITY_WEBGL should be a boolean");

            yield return null;
        }

        [UnityTest]
        public IEnumerator InjectPlatformDefines_InjectsUnityStandalone() {
            RunnerUtils.InjectPlatformDefines(_bridge);

            var result = _bridge.Eval("typeof UNITY_STANDALONE");
            Assert.AreEqual("boolean", result, "UNITY_STANDALONE should be a boolean");

            yield return null;
        }

        [UnityTest]
        public IEnumerator InjectPlatformDefines_InjectsUnityStandaloneOSX() {
            RunnerUtils.InjectPlatformDefines(_bridge);

            var result = _bridge.Eval("typeof UNITY_STANDALONE_OSX");
            Assert.AreEqual("boolean", result, "UNITY_STANDALONE_OSX should be a boolean");

            yield return null;
        }

        [UnityTest]
        public IEnumerator InjectPlatformDefines_InjectsUnityStandaloneWin() {
            RunnerUtils.InjectPlatformDefines(_bridge);

            var result = _bridge.Eval("typeof UNITY_STANDALONE_WIN");
            Assert.AreEqual("boolean", result, "UNITY_STANDALONE_WIN should be a boolean");

            yield return null;
        }

        [UnityTest]
        public IEnumerator InjectPlatformDefines_InjectsUnityStandaloneLinux() {
            RunnerUtils.InjectPlatformDefines(_bridge);

            var result = _bridge.Eval("typeof UNITY_STANDALONE_LINUX");
            Assert.AreEqual("boolean", result, "UNITY_STANDALONE_LINUX should be a boolean");

            yield return null;
        }

        [UnityTest]
        public IEnumerator InjectPlatformDefines_InjectsUnityIOS() {
            RunnerUtils.InjectPlatformDefines(_bridge);

            var result = _bridge.Eval("typeof UNITY_IOS");
            Assert.AreEqual("boolean", result, "UNITY_IOS should be a boolean");

            yield return null;
        }

        [UnityTest]
        public IEnumerator InjectPlatformDefines_InjectsUnityAndroid() {
            RunnerUtils.InjectPlatformDefines(_bridge);

            var result = _bridge.Eval("typeof UNITY_ANDROID");
            Assert.AreEqual("boolean", result, "UNITY_ANDROID should be a boolean");

            yield return null;
        }

        [UnityTest]
        public IEnumerator InjectPlatformDefines_InjectsDEBUG() {
            RunnerUtils.InjectPlatformDefines(_bridge);

            var result = _bridge.Eval("typeof DEBUG");
            Assert.AreEqual("boolean", result, "DEBUG should be a boolean");

            yield return null;
        }

        [UnityTest]
        public IEnumerator InjectPlatformDefines_NullBridge_DoesNotThrow() {
            Assert.DoesNotThrow(() => {
                RunnerUtils.InjectPlatformDefines(null);
            });
            yield return null;
        }

        // MARK: InjectPackGlobals Tests (__pack API)

        [UnityTest]
        public IEnumerator InjectPackGlobals_NullPacks_DoesNotThrow() {
            Assert.DoesNotThrow(() => {
                PackUtils.InjectPackGlobals(_bridge, null);
            });
            yield return null;
        }

        [UnityTest]
        public IEnumerator InjectPackGlobals_EmptyPacks_DoesNotThrow() {
            var packs = new List<Pack>();

            Assert.DoesNotThrow(() => {
                PackUtils.InjectPackGlobals(_bridge, packs);
            });
            yield return null;
        }

        [UnityTest]
        public IEnumerator InjectPackGlobals_NullBridge_DoesNotThrow() {
            var pack = CreateTestPack("test");
            var packs = new List<Pack> { pack };

            Assert.DoesNotThrow(() => {
                PackUtils.InjectPackGlobals(null, packs);
            });

            Object.DestroyImmediate(pack);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InjectPackGlobals_CartIsTheSameFunctionAndRegistry() {
            var pack = CreateTestPack("oldName");
            PackUtils.InjectPackGlobals(_bridge, new List<Pack> { pack });

            Assert.AreEqual("true", _bridge.Eval("String(__cart === __pack && __cartRegistry === __packRegistry)"));
            Assert.AreEqual("object", _bridge.Eval("typeof __cart('oldName')"));

            Object.DestroyImmediate(pack);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InjectPackGlobals_CreatesPackFunction() {
            PackUtils.InjectPackGlobals(_bridge, null);

            var result = _bridge.Eval("typeof __pack");
            Assert.AreEqual("function", result, "__pack should be a function");

            yield return null;
        }

        [UnityTest]
        public IEnumerator InjectPackGlobals_ValidPack_AccessibleViaPack() {
            var pack = CreateTestPack("myCart");
            var packs = new List<Pack> { pack };

            PackUtils.InjectPackGlobals(_bridge, packs);

            // __pack("myCart") should return a plain JS object
            var result = _bridge.Eval("typeof __pack('myCart')");
            Assert.AreEqual("object", result, "__pack('myCart') should return an object");

            Object.DestroyImmediate(pack);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InjectPackGlobals_NamespacedPack_AccessibleViaFullPath() {
            var pack = CreateTestPackWithNamespace("myCompany", "myCart");
            var packs = new List<Pack> { pack };

            PackUtils.InjectPackGlobals(_bridge, packs);

            // __pack("@myCompany/myCart") should return the pack SO
            var result = _bridge.Eval("typeof __pack('@myCompany/myCart')");
            Assert.AreEqual("object", result, "__pack('@myCompany/myCart') should return an object");

            Object.DestroyImmediate(pack);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InjectPackGlobals_MultiplePacks_AllAccessible() {
            var cart1 = CreateTestPack("cart1");
            var cart2 = CreateTestPackWithNamespace("ns", "cart2");
            var packs = new List<Pack> { cart1, cart2 };

            PackUtils.InjectPackGlobals(_bridge, packs);

            var result = _bridge.Eval("typeof __pack('cart1')");
            Assert.AreEqual("object", result);

            result = _bridge.Eval("typeof __pack('@ns/cart2')");
            Assert.AreEqual("object", result);

            Object.DestroyImmediate(cart1);
            Object.DestroyImmediate(cart2);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InjectPackGlobals_SkipsNullPacks() {
            var validCart = CreateTestPack("validCart");
            var packs = new List<Pack> { null, validCart, null };

            Assert.DoesNotThrow(() => {
                PackUtils.InjectPackGlobals(_bridge, packs);
            });

            var result = _bridge.Eval("typeof __pack('validCart')");
            Assert.AreEqual("object", result);

            Object.DestroyImmediate(validCart);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InjectPackGlobals_NotFoundPack_ThrowsError() {
            PackUtils.InjectPackGlobals(_bridge, null);

            // Calling __pack with unknown path should throw
            var result = _bridge.Eval("try { __pack('nonexistent'); 'no error' } catch(e) { 'error: ' + e.message }");
            Assert.IsTrue(result.Contains("error:"), "Should throw error for unknown pack");
            Assert.IsTrue(result.Contains("nonexistent"), "Error should mention the path");

            yield return null;
        }

        [UnityTest]
        public IEnumerator InjectPackGlobals_SlugWithSpecialChars_IsEscaped() {
            var pack = CreateTestPack("my'Cart");
            var packs = new List<Pack> { pack };

            // This should not throw even with special chars in slug
            Assert.DoesNotThrow(() => {
                PackUtils.InjectPackGlobals(_bridge, packs);
            });

            // Access using the path
            var result = _bridge.Eval("typeof __pack(\"my'Cart\")");
            Assert.AreEqual("object", result);

            Object.DestroyImmediate(pack);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InjectPackGlobals_WithObjects_PropertiesAccessible() {
            var tex = new Texture2D(2, 2);
            var pack = CreateTestPackWithObject("assetCart", "myTexture", tex);
            var packs = new List<Pack> { pack };

            PackUtils.InjectPackGlobals(_bridge, packs);

            // Object entry should be directly accessible as a property
            var result = _bridge.Eval("typeof __pack('assetCart').myTexture");
            Assert.AreEqual("object", result, "Object entry should be accessible as property");

            // It should be a wrapped C# object with a handle
            result = _bridge.Eval("typeof __pack('assetCart').myTexture.__csHandle");
            Assert.AreEqual("number", result, "Object entry should have a C# handle");

            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(pack);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InjectPackGlobals_WithoutObjects_ReturnsEmptyObject() {
            var pack = CreateTestPack("emptyCart");
            var packs = new List<Pack> { pack };

            PackUtils.InjectPackGlobals(_bridge, packs);

            var result = _bridge.Eval("typeof __pack('emptyCart')");
            Assert.AreEqual("object", result, "Should return an object even with no entries");

            result = _bridge.Eval("Object.keys(__pack('emptyCart')).length");
            Assert.AreEqual("0", result, "Empty pack should have no properties");

            Object.DestroyImmediate(pack);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InjectPackGlobals_MultipleObjects_AllAccessible() {
            var tex = new Texture2D(2, 2);
            var mat = new Material(Shader.Find("UI/Default"));
            var pack = CreateTestPack("multiCart");
            AddPackObject(pack, "texture", tex);
            AddPackObject(pack, "material", mat);
            var packs = new List<Pack> { pack };

            PackUtils.InjectPackGlobals(_bridge, packs);

            var result = _bridge.Eval("typeof __pack('multiCart').texture");
            Assert.AreEqual("object", result);

            result = _bridge.Eval("typeof __pack('multiCart').material");
            Assert.AreEqual("object", result);

            Object.DestroyImmediate(mat);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(pack);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InjectPackGlobals_SkipsNullObjectEntries() {
            var tex = new Texture2D(2, 2);
            var pack = CreateTestPack("nullEntryCart");
            AddPackObject(pack, "", tex);      // empty key
            AddPackObject(pack, "valid", tex);
            AddPackObject(pack, "nullVal", null); // null value
            var packs = new List<Pack> { pack };

            Assert.DoesNotThrow(() => {
                PackUtils.InjectPackGlobals(_bridge, packs);
            });

            var result = _bridge.Eval("typeof __pack('nullEntryCart').valid");
            Assert.AreEqual("object", result, "Valid entry should be accessible");

            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(pack);
            yield return null;
        }

        // MARK: Helper Methods

        /// <summary>
        /// Sets the slug field on a Pack via reflection.
        /// </summary>
        void SetPackSlug(Pack pack, string slug) {
            var field = typeof(Pack).GetField("_slug",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(pack, slug);
        }

        /// <summary>
        /// Adds an object entry to a Pack via reflection.
        /// </summary>
        void AddPackObject(Pack pack, string key, Object value) {
            var field = typeof(Pack).GetField("_objects",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var objects = (List<PackObjectEntry>)field.GetValue(pack);
            objects.Add(new PackObjectEntry { key = key, value = value });
        }

        /// <summary>
        /// Sets the namespace field on a Pack via reflection.
        /// </summary>
        void SetPackNamespace(Pack pack, string ns) {
            var field = typeof(Pack).GetField("_namespace",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field.SetValue(pack, ns);
        }

        /// <summary>
        /// Creates a test Pack with the given slug.
        /// </summary>
        Pack CreateTestPack(string slug) {
            var pack = ScriptableObject.CreateInstance<Pack>();
            SetPackSlug(pack, slug);
            return pack;
        }

        /// <summary>
        /// Creates a test Pack with namespace and slug.
        /// </summary>
        Pack CreateTestPackWithNamespace(string ns, string slug) {
            var pack = ScriptableObject.CreateInstance<Pack>();
            SetPackNamespace(pack, ns);
            SetPackSlug(pack, slug);
            return pack;
        }

        /// <summary>
        /// Creates a test Pack with a slug and an object entry.
        /// </summary>
        Pack CreateTestPackWithObject(string slug, string objectKey, Object objectValue) {
            var pack = ScriptableObject.CreateInstance<Pack>();
            SetPackSlug(pack, slug);
            AddPackObject(pack, objectKey, objectValue);
            return pack;
        }
    }
}
