using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests {
    /// <summary>
    /// What JS reads from C# for a UnityEngine.Object that C# calls null, and
    /// what a C# exception costs when JS catches it (Singtaa/OneJS#136).
    ///
    /// A destroyed object, like the editor's placeholder for an unassigned
    /// field, is a live C# reference that Unity's overloaded == calls null. The
    /// bridge tested it with the plain reference test and handed JS a proxy, so
    /// `x === null` was false and the first property read threw in C#. That
    /// exception was logged red as it was thrown into JS, even when the JS
    /// caught it, so a script that guarded its read still put an error in front
    /// of whoever pressed Play.
    /// </summary>
    [TestFixture]
    public class UnityNullInteropPlaymodeTests {
        GameObject _go;
        UIDocument _uiDocument;
        PanelSettings _panelSettings;
        QuickJSUIBridge _bridge;

        [UnitySetUp]
        public IEnumerator SetUp() {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.themeStyleSheet =
                AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                    "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");

            _go = new GameObject("UnityNullInteropTestHost");
            _uiDocument = _go.AddComponent<UIDocument>();
            _uiDocument.panelSettings = _panelSettings;
            yield return null;

            _bridge = new QuickJSUIBridge(_uiDocument.rootVisualElement);
            UnityNullFixture.Reset();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            _bridge?.Dispose();
            _bridge = null;
            UnityNullFixture.Reset();
            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_panelSettings != null) UnityEngine.Object.Destroy(_panelSettings);
            QuickJSNative.ClearAllHandles();
            yield return null;
        }

        const string Fixture = "CS.OneJS.Tests.UnityNullFixture";

        [UnityTest]
        public IEnumerator ADestroyedObjectReadFromCSharpIsNullInJs() {
            UnityNullFixture.MakeDead();
            Assert.IsFalse(ReferenceEquals(UnityNullFixture.Dead, null), "The fixture must hold a live reference to a destroyed object.");
            Assert.IsTrue(UnityNullFixture.Dead == null, "Unity must call the fixture's object null.");

            Assert.AreEqual("true", _bridge.Eval($"{Fixture}.Dead === null"), "a property");
            Assert.AreEqual("true", _bridge.Eval($"{Fixture}.DeadField === null"), "a field");
            Assert.AreEqual("true", _bridge.Eval($"{Fixture}.GetDead() === null"), "a method's return value");
            Assert.AreEqual("none", _bridge.Eval($"String({Fixture}.Dead?.name ?? 'none')"), "optional chaining");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ADestroyedObjectPassedToAJsCallbackIsNull() {
            UnityNullFixture.MakeDead();
            _bridge.Eval($"globalThis.__seen = 'not called'; {Fixture}.CallWithDead(function (go) {{ globalThis.__seen = go === null ? 'null' : typeof go }})");
            Assert.AreEqual("null", _bridge.Eval("globalThis.__seen"));
            yield return null;
        }

        // The control: only what Unity calls null turns into null.
        [UnityTest]
        public IEnumerator ALiveObjectStillArrivesAsAProxy() {
            UnityNullFixture.MakeLive("still here");
            Assert.AreEqual("still here", _bridge.Eval($"{Fixture}.Live.name"));
            _bridge.Eval($"globalThis.__seen = ''; {Fixture}.CallWithLive(function (go) {{ globalThis.__seen = go.name }})");
            Assert.AreEqual("still here", _bridge.Eval("globalThis.__seen"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ACSharpExceptionThatJsCatchesLogsNothing() {
            var message = _bridge.Eval($"(function () {{ try {{ return String({Fixture}.Throws) }} catch (e) {{ return e.message }} }})()");
            StringAssert.Contains("InvalidOperationException: thrown on purpose", message,
                "The JS error must still name the C# exception.");
            LogAssert.NoUnexpectedReceived();
            yield return null;
        }

        // Not logging at the throw must not hide a failure nobody caught: it is
        // reported where it surfaces, with the C# exception, stack included, as
        // its inner exception.
        [UnityTest]
        public IEnumerator AnUncaughtCSharpExceptionSurfacesWithItsCSharpStack() {
            JSException thrown = null;
            try {
                _bridge.Eval($"{Fixture}.Throws");
            } catch (JSException e) {
                thrown = e;
            }
            Assert.IsNotNull(thrown, "Eval must throw the uncaught error.");
            StringAssert.Contains("InvalidOperationException: thrown on purpose", thrown.Message);
            Assert.IsInstanceOf<InvalidOperationException>(thrown.InnerException,
                "The report must carry the C# exception it came from.");
            StringAssert.Contains(nameof(UnityNullFixture), thrown.InnerException.StackTrace);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AnUncaughtCSharpExceptionInATimerIsStillLogged() {
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("InvalidOperationException: thrown on purpose"));
            _bridge.Eval($"setTimeout(function () {{ {Fixture}.Throws }}, 0)");
            yield return null;
            _bridge.Tick();
            yield return null;
            _bridge.Tick();
            yield return null;
        }
    }

    /// <summary>C# state the tests above read from JS.</summary>
    public static class UnityNullFixture {
        public static GameObject DeadField;
        public static GameObject Dead => DeadField;
        public static GameObject Live { get; private set; }

        public static GameObject GetDead() => DeadField;
        public static void CallWithDead(Action<GameObject> callback) => callback(DeadField);
        public static void CallWithLive(Action<GameObject> callback) => callback(Live);

        public static string Throws => throw new InvalidOperationException("thrown on purpose");

        public static void MakeDead() {
            DeadField = new GameObject("UnityNullFixture dead");
            UnityEngine.Object.DestroyImmediate(DeadField);
        }

        public static void MakeLive(string name) {
            Live = new GameObject(name);
        }

        public static void Reset() {
            DeadField = null;
            if (Live != null) UnityEngine.Object.Destroy(Live);
            Live = null;
        }
    }
}
