using NUnit.Framework;
using UnityEngine.UIElements;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// The C# half of keeping component state across a hot reload (#155): JsHost asks the old
    /// context for its state, carries the string past the context's end, gives it to the new one
    /// before the bundle runs, and asks the new one what it kept. The app here stands in for
    /// onejs-react, whose half keep-state.test.tsx covers.
    /// </summary>
    public class JsHostKeepStateTests {
        // Keeps `counter` and `text` the way onejs-react keeps hook state: hands them over as a
        // JSON string through __bridge.ReloadSnapshot, and reads __onejsReloadSnapshot back.
        const string App = @"
globalThis.__onejsCaptureState = function () {
    __bridge.ReloadSnapshot = JSON.stringify({ counter: globalThis.counter, text: globalThis.text });
    return 1;
};
var kept = typeof globalThis.__onejsReloadSnapshot === 'string' ? JSON.parse(globalThis.__onejsReloadSnapshot) : null;
delete globalThis.__onejsReloadSnapshot;
globalThis.counter = kept ? kept.counter : 0;
globalThis.text = kept ? kept.text : '';
globalThis.__onejsTakeReloadReport = function () { return kept ? 'kept counter ' + kept.counter : ''; };
";

        // Sets `text` to everything a string literal can trip on: both quotes, a backslash,
        // newlines, a line separator and a NUL, written as JS escapes.
        const string SetAwkwardText = @"text = '\'single\' ""double"" back\\slash \n new\r\nline \u2028 sep \u0000 nul'";

        JsHost _host;
        VisualElement _root;

        [SetUp]
        public void SetUp() {
            _host = new JsHost("JsHostKeepStateTests");
            _root = new VisualElement();
            _host.Create(_root, null);
            _host.Run(App, "app.js");
        }

        [TearDown]
        public void TearDown() => _host.Dispose();

        [Test]
        public void ARecreateThatKeepsStateHandsItToTheNewContext() {
            _host.Bridge.Eval("counter = 7; " + SetAwkwardText);
            Assert.AreEqual(52, int.Parse(_host.Bridge.Eval("String(text.length)")), "the test's own string is what it means to be");
            var expected = _host.Bridge.Eval("JSON.stringify(text)");
            _host.Recreate(_root, null, App, "app.js", keepState: true);
            Assert.AreEqual("7", _host.Bridge.Eval("String(counter)"));
            Assert.AreEqual(expected, _host.Bridge.Eval("JSON.stringify(text)"));
            Assert.AreEqual("kept counter 7", _host.LastReloadReport);
        }

        [Test]
        public void ARecreateThatDoesNotKeepStateStartsFresh() {
            _host.Bridge.Eval("counter = 7");
            _host.Recreate(_root, null, App, "app.js");
            Assert.AreEqual("0", _host.Bridge.Eval("String(counter)"));
            Assert.IsNull(_host.LastReloadReport);
        }

        [Test]
        public void AnAppWithNothingToKeepReloadsAsBefore() {
            _host.Recreate(_root, null, "globalThis.counter = 1;", "plain.js", keepState: true);
            _host.Recreate(_root, null, "globalThis.counter = 2;", "plain.js", keepState: true);
            Assert.AreEqual("2", _host.Bridge.Eval("String(counter)"));
            Assert.AreEqual("", _host.LastReloadReport);
        }
    }
}
