using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Tests for the URL and URLSearchParams API implementation.
/// </summary>
[TestFixture]
public class QuickJSURLTests {
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
        _go = new GameObject("URLTestHost");
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
    public IEnumerator URL_GlobalExists() {
        var result = _bridge.Eval("typeof URL");
        Assert.AreEqual("function", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URLSearchParams_GlobalExists() {
        var result = _bridge.Eval("typeof URLSearchParams");
        Assert.AreEqual("function", result);
        yield return null;
    }

    // MARK: URLSearchParams Constructor Tests

    [UnityTest]
    public IEnumerator URLSearchParams_EmptyConstructor_Works() {
        var result = _bridge.Eval("new URLSearchParams().toString()");
        Assert.AreEqual("", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URLSearchParams_StringConstructor_Works() {
        var result = _bridge.Eval("new URLSearchParams('foo=1&bar=2').toString()");
        Assert.AreEqual("foo=1&bar=2", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URLSearchParams_StringWithQuestionMark_Works() {
        var result = _bridge.Eval("new URLSearchParams('?foo=1&bar=2').toString()");
        Assert.AreEqual("foo=1&bar=2", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URLSearchParams_ObjectConstructor_Works() {
        var result = _bridge.Eval("new URLSearchParams({ foo: '1', bar: '2' }).get('foo')");
        Assert.AreEqual("1", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URLSearchParams_ArrayConstructor_Works() {
        var result = _bridge.Eval("new URLSearchParams([['foo', '1'], ['bar', '2']]).get('bar')");
        Assert.AreEqual("2", result);
        yield return null;
    }

    // MARK: URLSearchParams Methods

    [UnityTest]
    public IEnumerator URLSearchParams_Get_ReturnsValue() {
        var result = _bridge.Eval("new URLSearchParams('name=Alice').get('name')");
        Assert.AreEqual("Alice", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URLSearchParams_Get_ReturnsNullForMissing() {
        var result = _bridge.Eval("new URLSearchParams('name=Alice').get('age') === null ? 'null' : 'not null'");
        Assert.AreEqual("null", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URLSearchParams_GetAll_ReturnsAllValues() {
        var result = _bridge.Eval("new URLSearchParams('a=1&a=2&a=3').getAll('a').join(',')");
        Assert.AreEqual("1,2,3", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URLSearchParams_Has_ReturnsTrue() {
        var result = _bridge.Eval("new URLSearchParams('key=value').has('key') ? 'yes' : 'no'");
        Assert.AreEqual("yes", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URLSearchParams_Has_ReturnsFalse() {
        var result = _bridge.Eval("new URLSearchParams('key=value').has('missing') ? 'yes' : 'no'");
        Assert.AreEqual("no", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URLSearchParams_Append_AddsValue() {
        var result = _bridge.Eval(@"
            var p = new URLSearchParams('a=1');
            p.append('b', '2');
            p.toString();
        ");
        Assert.AreEqual("a=1&b=2", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URLSearchParams_Append_AllowsDuplicates() {
        var result = _bridge.Eval(@"
            var p = new URLSearchParams('a=1');
            p.append('a', '2');
            p.getAll('a').join(',');
        ");
        Assert.AreEqual("1,2", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URLSearchParams_Set_OverwritesValue() {
        var result = _bridge.Eval(@"
            var p = new URLSearchParams('a=1&a=2');
            p.set('a', '3');
            p.getAll('a').join(',');
        ");
        Assert.AreEqual("3", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URLSearchParams_Delete_RemovesKey() {
        var result = _bridge.Eval(@"
            var p = new URLSearchParams('a=1&b=2');
            p.delete('a');
            p.toString();
        ");
        Assert.AreEqual("b=2", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URLSearchParams_Sort_SortsKeys() {
        var result = _bridge.Eval(@"
            var p = new URLSearchParams('c=3&a=1&b=2');
            p.sort();
            p.toString();
        ");
        Assert.AreEqual("a=1&b=2&c=3", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URLSearchParams_Keys_ReturnsKeys() {
        var result = _bridge.Eval("new URLSearchParams('a=1&b=2').keys().join(',')");
        Assert.AreEqual("a,b", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URLSearchParams_Values_ReturnsValues() {
        var result = _bridge.Eval("new URLSearchParams('a=1&b=2').values().join(',')");
        Assert.AreEqual("1,2", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URLSearchParams_Size_ReturnsCount() {
        var result = _bridge.Eval("new URLSearchParams('a=1&b=2&c=3').size.toString()");
        Assert.AreEqual("3", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URLSearchParams_ForEach_IteratesAll() {
        var result = _bridge.Eval(@"
            var items = [];
            new URLSearchParams('a=1&b=2').forEach(function(v, k) { items.push(k + ':' + v); });
            items.join(',');
        ");
        Assert.AreEqual("a:1,b:2", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URLSearchParams_EncodesSpecialChars() {
        var result = _bridge.Eval("new URLSearchParams({ 'key': 'hello world' }).toString()");
        Assert.AreEqual("key=hello%20world", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URLSearchParams_DecodesSpecialChars() {
        var result = _bridge.Eval("new URLSearchParams('key=hello%20world').get('key')");
        Assert.AreEqual("hello world", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URLSearchParams_DecodesPlus() {
        var result = _bridge.Eval("new URLSearchParams('key=hello+world').get('key')");
        Assert.AreEqual("hello world", result);
        yield return null;
    }

    // MARK: URL Constructor Tests

    [UnityTest]
    public IEnumerator URL_SimpleURL_Parses() {
        var result = _bridge.Eval("new URL('https://example.com').hostname");
        Assert.AreEqual("example.com", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URL_WithPort_ParsesPort() {
        var result = _bridge.Eval("new URL('https://example.com:8080').port");
        Assert.AreEqual("8080", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URL_WithPath_ParsesPath() {
        var result = _bridge.Eval("new URL('https://example.com/path/to/page').pathname");
        Assert.AreEqual("/path/to/page", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URL_WithQuery_ParsesQuery() {
        var result = _bridge.Eval("new URL('https://example.com?foo=bar').search");
        Assert.AreEqual("?foo=bar", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URL_WithHash_ParsesHash() {
        var result = _bridge.Eval("new URL('https://example.com#section').hash");
        Assert.AreEqual("#section", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URL_FullURL_ParsesAllParts() {
        var result = _bridge.Eval(@"
            var u = new URL('https://user:pass@example.com:8080/path?query=1#hash');
            u.protocol + '|' + u.username + '|' + u.password + '|' + u.hostname + '|' + u.port + '|' + u.pathname + '|' + u.search + '|' + u.hash;
        ");
        Assert.AreEqual("https:|user|pass|example.com|8080|/path|?query=1|#hash", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URL_InvalidURL_Throws() {
        var result = _bridge.Eval(@"
            try {
                new URL('not a valid url');
                'no error';
            } catch (e) {
                'error';
            }
        ");
        Assert.AreEqual("error", result);
        yield return null;
    }

    // MARK: URL Properties

    [UnityTest]
    public IEnumerator URL_Host_IncludesPort() {
        var result = _bridge.Eval("new URL('https://example.com:8080').host");
        Assert.AreEqual("example.com:8080", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URL_Host_ExcludesDefaultPort() {
        var result = _bridge.Eval("new URL('https://example.com').host");
        Assert.AreEqual("example.com", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URL_Origin_Works() {
        var result = _bridge.Eval("new URL('https://example.com:8080/path').origin");
        Assert.AreEqual("https://example.com:8080", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URL_Origin_OmitsDefaultPort() {
        var result = _bridge.Eval("new URL('https://example.com:443/path').origin");
        Assert.AreEqual("https://example.com", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URL_Href_ReturnsFullURL() {
        var result = _bridge.Eval("new URL('https://example.com/path?query=1#hash').href");
        Assert.AreEqual("https://example.com/path?query=1#hash", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URL_ToString_EqualsHref() {
        var result = _bridge.Eval(@"
            var u = new URL('https://example.com/path');
            u.toString() === u.href ? 'equal' : 'not equal';
        ");
        Assert.AreEqual("equal", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URL_ToJSON_EqualsHref() {
        var result = _bridge.Eval(@"
            var u = new URL('https://example.com/path');
            u.toJSON() === u.href ? 'equal' : 'not equal';
        ");
        Assert.AreEqual("equal", result);
        yield return null;
    }

    // MARK: URL SearchParams Integration

    [UnityTest]
    public IEnumerator URL_SearchParams_ReturnsURLSearchParams() {
        var result = _bridge.Eval("new URL('https://example.com?foo=bar').searchParams.get('foo')");
        Assert.AreEqual("bar", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URL_SearchParams_ModificationReflectsInHref() {
        var result = _bridge.Eval(@"
            var u = new URL('https://example.com?foo=1');
            u.searchParams.set('foo', '2');
            u.searchParams.append('bar', '3');
            u.href;
        ");
        Assert.AreEqual("https://example.com/?foo=2&bar=3", result);
        yield return null;
    }

    // MARK: URL Setters

    [UnityTest]
    public IEnumerator URL_SetHostname_Works() {
        var result = _bridge.Eval(@"
            var u = new URL('https://example.com');
            u.hostname = 'other.com';
            u.hostname;
        ");
        Assert.AreEqual("other.com", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URL_SetPathname_Works() {
        var result = _bridge.Eval(@"
            var u = new URL('https://example.com/old');
            u.pathname = '/new/path';
            u.pathname;
        ");
        Assert.AreEqual("/new/path", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URL_SetSearch_Works() {
        var result = _bridge.Eval(@"
            var u = new URL('https://example.com');
            u.search = '?new=query';
            u.search;
        ");
        Assert.AreEqual("?new=query", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URL_SetHash_Works() {
        var result = _bridge.Eval(@"
            var u = new URL('https://example.com');
            u.hash = '#newhash';
            u.hash;
        ");
        Assert.AreEqual("#newhash", result);
        yield return null;
    }

    // MARK: URL with Base

    [UnityTest]
    public IEnumerator URL_RelativeWithBase_ResolvesAbsolutePath() {
        var result = _bridge.Eval("new URL('/path', 'https://example.com').href");
        Assert.AreEqual("https://example.com/path", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URL_RelativeWithBase_ResolvesRelativePath() {
        var result = _bridge.Eval("new URL('page.html', 'https://example.com/dir/').href");
        Assert.AreEqual("https://example.com/dir/page.html", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URL_RelativeWithBase_ResolvesQueryOnly() {
        var result = _bridge.Eval("new URL('?query=1', 'https://example.com/path').href");
        Assert.AreEqual("https://example.com/path?query=1", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URL_RelativeWithBase_ResolvesHashOnly() {
        var result = _bridge.Eval("new URL('#hash', 'https://example.com/path').href");
        Assert.AreEqual("https://example.com/path#hash", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URL_AbsoluteIgnoresBase() {
        var result = _bridge.Eval("new URL('https://other.com', 'https://example.com').hostname");
        Assert.AreEqual("other.com", result);
        yield return null;
    }

    // MARK: Protocol Handling

    [UnityTest]
    public IEnumerator URL_HttpProtocol_Works() {
        var result = _bridge.Eval("new URL('http://example.com').protocol");
        Assert.AreEqual("http:", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URL_WsProtocol_Works() {
        var result = _bridge.Eval("new URL('ws://example.com').protocol");
        Assert.AreEqual("ws:", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator URL_WssProtocol_Works() {
        var result = _bridge.Eval("new URL('wss://example.com').protocol");
        Assert.AreEqual("wss:", result);
        yield return null;
    }
}
