using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Tests for the atob/btoa (Base64) API implementation.
/// </summary>
[TestFixture]
public class QuickJSBase64Tests {
    GameObject _go;
    UIDocument _uiDocument;
    PanelSettings _panelSettings;
    QuickJSUIBridge _bridge;

    [UnitySetUp]
    public IEnumerator SetUp() {
        // Create PanelSettings at runtime
        _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        _panelSettings.themeStyleSheet =
            AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");

        // Create GameObject with UIDocument
        _go = new GameObject("Base64TestHost");
        _uiDocument = _go.AddComponent<UIDocument>();
        _uiDocument.panelSettings = _panelSettings;

        // Wait a frame for UIDocument to initialize
        yield return null;

        var root = _uiDocument.rootVisualElement;
        _bridge = new QuickJSUIBridge(root);

        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown() {
        _bridge?.Dispose();
        _bridge = null;

        if (_go != null) Object.Destroy(_go);
        if (_panelSettings != null) Object.Destroy(_panelSettings);

        QuickJSNative.ClearAllHandles();
        yield return null;
    }

    // MARK: Existence Tests

    [UnityTest]
    public IEnumerator Btoa_GlobalExists() {
        var result = _bridge.Eval("typeof btoa");
        Assert.AreEqual("function", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Atob_GlobalExists() {
        var result = _bridge.Eval("typeof atob");
        Assert.AreEqual("function", result);
        yield return null;
    }

    // MARK: btoa Tests

    [UnityTest]
    public IEnumerator Btoa_EmptyString_Works() {
        var result = _bridge.Eval("btoa('')");
        Assert.AreEqual("", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Btoa_SimpleString_Works() {
        var result = _bridge.Eval("btoa('Hello')");
        Assert.AreEqual("SGVsbG8=", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Btoa_HelloWorld_Works() {
        var result = _bridge.Eval("btoa('Hello, World!')");
        Assert.AreEqual("SGVsbG8sIFdvcmxkIQ==", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Btoa_SingleChar_Works() {
        var result = _bridge.Eval("btoa('A')");
        Assert.AreEqual("QQ==", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Btoa_TwoChars_Works() {
        var result = _bridge.Eval("btoa('AB')");
        Assert.AreEqual("QUI=", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Btoa_ThreeChars_Works() {
        var result = _bridge.Eval("btoa('ABC')");
        Assert.AreEqual("QUJD", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Btoa_Numbers_Works() {
        var result = _bridge.Eval("btoa('12345')");
        Assert.AreEqual("MTIzNDU=", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Btoa_BinaryData_Works() {
        // Test with binary-like data (Latin1 characters)
        // \x00\x01\x02\xff = 4 bytes, encodes to AAEC/w==
        var result = _bridge.Eval("btoa('\\x00\\x01\\x02\\xff')");
        Assert.AreEqual("AAEC/w==", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Btoa_ConvertsNumberToString() {
        var result = _bridge.Eval("btoa(123)");
        Assert.AreEqual("MTIz", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Btoa_NullArgument_Throws() {
        var result = _bridge.Eval(@"
            try {
                btoa(null);
                'no error';
            } catch (e) {
                'error';
            }
        ");
        Assert.AreEqual("error", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Btoa_UndefinedArgument_Throws() {
        var result = _bridge.Eval(@"
            try {
                btoa(undefined);
                'no error';
            } catch (e) {
                'error';
            }
        ");
        Assert.AreEqual("error", result);
        yield return null;
    }

    // MARK: atob Tests

    [UnityTest]
    public IEnumerator Atob_EmptyString_Works() {
        var result = _bridge.Eval("atob('')");
        Assert.AreEqual("", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Atob_SimpleString_Works() {
        var result = _bridge.Eval("atob('SGVsbG8=')");
        Assert.AreEqual("Hello", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Atob_HelloWorld_Works() {
        var result = _bridge.Eval("atob('SGVsbG8sIFdvcmxkIQ==')");
        Assert.AreEqual("Hello, World!", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Atob_NoPadding_Works() {
        var result = _bridge.Eval("atob('QUJD')");
        Assert.AreEqual("ABC", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Atob_SinglePadding_Works() {
        var result = _bridge.Eval("atob('QUI=')");
        Assert.AreEqual("AB", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Atob_DoublePadding_Works() {
        var result = _bridge.Eval("atob('QQ==')");
        Assert.AreEqual("A", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Atob_WithoutPadding_Works() {
        // atob should handle missing padding
        var result = _bridge.Eval("atob('SGVsbG8')");
        Assert.AreEqual("Hello", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Atob_InvalidLength_Throws() {
        var result = _bridge.Eval(@"
            try {
                atob('A');
                'no error';
            } catch (e) {
                'error';
            }
        ");
        Assert.AreEqual("error", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Atob_InvalidCharacter_Throws() {
        var result = _bridge.Eval(@"
            try {
                atob('!!!invalid!!!');
                'no error';
            } catch (e) {
                'error';
            }
        ");
        Assert.AreEqual("error", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Atob_NullArgument_Throws() {
        var result = _bridge.Eval(@"
            try {
                atob(null);
                'no error';
            } catch (e) {
                'error';
            }
        ");
        Assert.AreEqual("error", result);
        yield return null;
    }

    // MARK: Round-trip Tests

    [UnityTest]
    public IEnumerator RoundTrip_SimpleString_Works() {
        var result = _bridge.Eval("atob(btoa('Hello, World!'))");
        Assert.AreEqual("Hello, World!", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator RoundTrip_EmptyString_Works() {
        var result = _bridge.Eval("atob(btoa(''))");
        Assert.AreEqual("", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator RoundTrip_AllPrintableAscii_Works() {
        var result = _bridge.Eval(@"
            var original = '';
            for (var i = 32; i < 127; i++) {
                original += String.fromCharCode(i);
            }
            atob(btoa(original)) === original ? 'match' : 'no match';
        ");
        Assert.AreEqual("match", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator RoundTrip_Latin1Range_Works() {
        var result = _bridge.Eval(@"
            var original = '';
            for (var i = 0; i < 256; i++) {
                original += String.fromCharCode(i);
            }
            atob(btoa(original)) === original ? 'match' : 'no match';
        ");
        Assert.AreEqual("match", result);
        yield return null;
    }

    // MARK: Practical Use Cases

    [UnityTest]
    public IEnumerator Base64_JsonRoundTrip_Works() {
        var result = _bridge.Eval(@"
            var obj = { name: 'Alice', score: 100 };
            var encoded = btoa(JSON.stringify(obj));
            var decoded = JSON.parse(atob(encoded));
            decoded.name + ':' + decoded.score;
        ");
        Assert.AreEqual("Alice:100", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Base64_JwtLikePayload_Works() {
        // Simulate decoding a JWT-like base64 payload
        var result = _bridge.Eval(@"
            var payload = btoa(JSON.stringify({ sub: '1234567890', name: 'John Doe' }));
            var decoded = JSON.parse(atob(payload));
            decoded.sub + '|' + decoded.name;
        ");
        Assert.AreEqual("1234567890|John Doe", result);
        yield return null;
    }
}
