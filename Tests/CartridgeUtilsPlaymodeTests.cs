using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// PlayMode tests for CartridgeUtils methods that require QuickJSUIBridge.
/// Tests JavaScript injection for cartridge globals and platform defines.
/// </summary>
[TestFixture]
public class CartridgeUtilsPlaymodeTests {
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
        CartridgeUtils.InjectPlatformDefines(_bridge);

        var result = _bridge.Eval("typeof UNITY_EDITOR");
        Assert.AreEqual("boolean", result, "UNITY_EDITOR should be a boolean");

        // In editor tests, this should be true
        result = _bridge.Eval("UNITY_EDITOR");
        Assert.AreEqual("true", result, "UNITY_EDITOR should be true in editor");

        yield return null;
    }

    [UnityTest]
    public IEnumerator InjectPlatformDefines_InjectsUnityWebGL() {
        CartridgeUtils.InjectPlatformDefines(_bridge);

        var result = _bridge.Eval("typeof UNITY_WEBGL");
        Assert.AreEqual("boolean", result, "UNITY_WEBGL should be a boolean");

        yield return null;
    }

    [UnityTest]
    public IEnumerator InjectPlatformDefines_InjectsUnityStandalone() {
        CartridgeUtils.InjectPlatformDefines(_bridge);

        var result = _bridge.Eval("typeof UNITY_STANDALONE");
        Assert.AreEqual("boolean", result, "UNITY_STANDALONE should be a boolean");

        yield return null;
    }

    [UnityTest]
    public IEnumerator InjectPlatformDefines_InjectsUnityStandaloneOSX() {
        CartridgeUtils.InjectPlatformDefines(_bridge);

        var result = _bridge.Eval("typeof UNITY_STANDALONE_OSX");
        Assert.AreEqual("boolean", result, "UNITY_STANDALONE_OSX should be a boolean");

        yield return null;
    }

    [UnityTest]
    public IEnumerator InjectPlatformDefines_InjectsUnityStandaloneWin() {
        CartridgeUtils.InjectPlatformDefines(_bridge);

        var result = _bridge.Eval("typeof UNITY_STANDALONE_WIN");
        Assert.AreEqual("boolean", result, "UNITY_STANDALONE_WIN should be a boolean");

        yield return null;
    }

    [UnityTest]
    public IEnumerator InjectPlatformDefines_InjectsUnityStandaloneLinux() {
        CartridgeUtils.InjectPlatformDefines(_bridge);

        var result = _bridge.Eval("typeof UNITY_STANDALONE_LINUX");
        Assert.AreEqual("boolean", result, "UNITY_STANDALONE_LINUX should be a boolean");

        yield return null;
    }

    [UnityTest]
    public IEnumerator InjectPlatformDefines_InjectsUnityIOS() {
        CartridgeUtils.InjectPlatformDefines(_bridge);

        var result = _bridge.Eval("typeof UNITY_IOS");
        Assert.AreEqual("boolean", result, "UNITY_IOS should be a boolean");

        yield return null;
    }

    [UnityTest]
    public IEnumerator InjectPlatformDefines_InjectsUnityAndroid() {
        CartridgeUtils.InjectPlatformDefines(_bridge);

        var result = _bridge.Eval("typeof UNITY_ANDROID");
        Assert.AreEqual("boolean", result, "UNITY_ANDROID should be a boolean");

        yield return null;
    }

    [UnityTest]
    public IEnumerator InjectPlatformDefines_InjectsDEBUG() {
        CartridgeUtils.InjectPlatformDefines(_bridge);

        var result = _bridge.Eval("typeof DEBUG");
        Assert.AreEqual("boolean", result, "DEBUG should be a boolean");

        yield return null;
    }

    [UnityTest]
    public IEnumerator InjectPlatformDefines_NullBridge_DoesNotThrow() {
        Assert.DoesNotThrow(() => {
            CartridgeUtils.InjectPlatformDefines(null);
        });
        yield return null;
    }

    // MARK: InjectCartridgeGlobals Tests (__cart API)

    [UnityTest]
    public IEnumerator InjectCartridgeGlobals_NullCartridges_DoesNotThrow() {
        Assert.DoesNotThrow(() => {
            CartridgeUtils.InjectCartridgeGlobals(_bridge, null);
        });
        yield return null;
    }

    [UnityTest]
    public IEnumerator InjectCartridgeGlobals_EmptyCartridges_DoesNotThrow() {
        var cartridges = new List<UICartridge>();

        Assert.DoesNotThrow(() => {
            CartridgeUtils.InjectCartridgeGlobals(_bridge, cartridges);
        });
        yield return null;
    }

    [UnityTest]
    public IEnumerator InjectCartridgeGlobals_NullBridge_DoesNotThrow() {
        var cartridge = CreateTestCartridge("test");
        var cartridges = new List<UICartridge> { cartridge };

        Assert.DoesNotThrow(() => {
            CartridgeUtils.InjectCartridgeGlobals(null, cartridges);
        });

        Object.DestroyImmediate(cartridge);
        yield return null;
    }

    [UnityTest]
    public IEnumerator InjectCartridgeGlobals_CreatesCartFunction() {
        CartridgeUtils.InjectCartridgeGlobals(_bridge, null);

        var result = _bridge.Eval("typeof __cart");
        Assert.AreEqual("function", result, "__cart should be a function");

        yield return null;
    }

    [UnityTest]
    public IEnumerator InjectCartridgeGlobals_ValidCartridge_AccessibleViaCart() {
        var cartridge = CreateTestCartridge("myCart");
        var cartridges = new List<UICartridge> { cartridge };

        CartridgeUtils.InjectCartridgeGlobals(_bridge, cartridges);

        // __cart("myCart") should return the cartridge SO
        var result = _bridge.Eval("typeof __cart('myCart')");
        Assert.AreEqual("object", result, "__cart('myCart') should return an object");

        // It should have a __csHandle (wrapped C# object)
        result = _bridge.Eval("typeof __cart('myCart').__csHandle");
        Assert.AreEqual("number", result, "Cartridge should have a handle");

        Object.DestroyImmediate(cartridge);
        yield return null;
    }

    [UnityTest]
    public IEnumerator InjectCartridgeGlobals_NamespacedCartridge_AccessibleViaFullPath() {
        var cartridge = CreateTestCartridgeWithNamespace("myCompany", "myCart");
        var cartridges = new List<UICartridge> { cartridge };

        CartridgeUtils.InjectCartridgeGlobals(_bridge, cartridges);

        // __cart("@myCompany/myCart") should return the cartridge SO
        var result = _bridge.Eval("typeof __cart('@myCompany/myCart')");
        Assert.AreEqual("object", result, "__cart('@myCompany/myCart') should return an object");

        Object.DestroyImmediate(cartridge);
        yield return null;
    }

    [UnityTest]
    public IEnumerator InjectCartridgeGlobals_MultipleCartridges_AllAccessible() {
        var cart1 = CreateTestCartridge("cart1");
        var cart2 = CreateTestCartridgeWithNamespace("ns", "cart2");
        var cartridges = new List<UICartridge> { cart1, cart2 };

        CartridgeUtils.InjectCartridgeGlobals(_bridge, cartridges);

        var result = _bridge.Eval("typeof __cart('cart1')");
        Assert.AreEqual("object", result);

        result = _bridge.Eval("typeof __cart('@ns/cart2')");
        Assert.AreEqual("object", result);

        Object.DestroyImmediate(cart1);
        Object.DestroyImmediate(cart2);
        yield return null;
    }

    [UnityTest]
    public IEnumerator InjectCartridgeGlobals_SkipsNullCartridges() {
        var validCart = CreateTestCartridge("validCart");
        var cartridges = new List<UICartridge> { null, validCart, null };

        Assert.DoesNotThrow(() => {
            CartridgeUtils.InjectCartridgeGlobals(_bridge, cartridges);
        });

        var result = _bridge.Eval("typeof __cart('validCart')");
        Assert.AreEqual("object", result);

        Object.DestroyImmediate(validCart);
        yield return null;
    }

    [UnityTest]
    public IEnumerator InjectCartridgeGlobals_NotFoundCartridge_ThrowsError() {
        CartridgeUtils.InjectCartridgeGlobals(_bridge, null);

        // Calling __cart with unknown path should throw
        var result = _bridge.Eval("try { __cart('nonexistent'); 'no error' } catch(e) { 'error: ' + e.message }");
        Assert.IsTrue(result.Contains("error:"), "Should throw error for unknown cartridge");
        Assert.IsTrue(result.Contains("nonexistent"), "Error should mention the path");

        yield return null;
    }

    [UnityTest]
    public IEnumerator InjectCartridgeGlobals_SlugWithSpecialChars_IsEscaped() {
        var cartridge = CreateTestCartridge("my'Cart");
        var cartridges = new List<UICartridge> { cartridge };

        // This should not throw even with special chars in slug
        Assert.DoesNotThrow(() => {
            CartridgeUtils.InjectCartridgeGlobals(_bridge, cartridges);
        });

        // Access using the path
        var result = _bridge.Eval("typeof __cart(\"my'Cart\")");
        Assert.AreEqual("object", result);

        Object.DestroyImmediate(cartridge);
        yield return null;
    }

    // MARK: Helper Methods

    /// <summary>
    /// Sets the slug field on a UICartridge via reflection.
    /// </summary>
    void SetCartridgeSlug(UICartridge cartridge, string slug) {
        var field = typeof(UICartridge).GetField("_slug",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field.SetValue(cartridge, slug);
    }

    /// <summary>
    /// Adds an object entry to a UICartridge via reflection.
    /// </summary>
    void AddCartridgeObject(UICartridge cartridge, string key, Object value) {
        var field = typeof(UICartridge).GetField("_objects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var objects = (List<CartridgeObjectEntry>)field.GetValue(cartridge);
        objects.Add(new CartridgeObjectEntry { key = key, value = value });
    }

    /// <summary>
    /// Sets the namespace field on a UICartridge via reflection.
    /// </summary>
    void SetCartridgeNamespace(UICartridge cartridge, string ns) {
        var field = typeof(UICartridge).GetField("_namespace",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field.SetValue(cartridge, ns);
    }

    /// <summary>
    /// Creates a test UICartridge with the given slug.
    /// </summary>
    UICartridge CreateTestCartridge(string slug) {
        var cartridge = ScriptableObject.CreateInstance<UICartridge>();
        SetCartridgeSlug(cartridge, slug);
        return cartridge;
    }

    /// <summary>
    /// Creates a test UICartridge with namespace and slug.
    /// </summary>
    UICartridge CreateTestCartridgeWithNamespace(string ns, string slug) {
        var cartridge = ScriptableObject.CreateInstance<UICartridge>();
        SetCartridgeNamespace(cartridge, ns);
        SetCartridgeSlug(cartridge, slug);
        return cartridge;
    }

    /// <summary>
    /// Creates a test UICartridge with a slug and an object entry.
    /// </summary>
    UICartridge CreateTestCartridgeWithObject(string slug, string objectKey, Object objectValue) {
        var cartridge = ScriptableObject.CreateInstance<UICartridge>();
        SetCartridgeSlug(cartridge, slug);
        AddCartridgeObject(cartridge, objectKey, objectValue);
        return cartridge;
    }
}
