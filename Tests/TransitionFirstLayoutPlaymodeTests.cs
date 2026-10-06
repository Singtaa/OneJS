using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests {
    /// <summary>
    /// A USS transition runs between two resolved styles, so a class that is
    /// already on an element at its first style resolution is its starting
    /// style and nothing animates (#133). The Transitions section of the
    /// styling docs says so and gives the fix, starting the change from the
    /// element's first geometrychanged; these pin both halves against a real
    /// panel, driven from JS the way an app drives it.
    /// </summary>
    [TestFixture]
    public class TransitionFirstLayoutPlaymodeTests {
        const string Uss = @"
.map { position: absolute; width: 10px; height: 10px; translate: 0 0;
       transition-property: translate; transition-duration: 10s; }
.map--far { translate: 600px 600px; }";

        PanelHost _host;
        QuickJSUIBridge _bridge;

        [UnitySetUp]
        public IEnumerator SetUp() {
            _host = PanelHost.Create(64, 64, "TransitionFirstLayoutHost");
            yield return null;

            _bridge = new QuickJSUIBridge(_host.root);
            var rootHandle = QuickJSNative.RegisterObject(_host.root);
            _bridge.Eval($"globalThis.__root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {rootHandle})");
            Assert.IsTrue(_bridge.CompileStyleSheet(Uss, "transition-first-layout"));
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            _bridge?.Dispose();
            _bridge = null;
            _host?.Destroy();
            _host = null;
            QuickJSNative.ClearAllHandles();
            yield return null;
        }

        [UnityTest]
        public IEnumerator AClassAddedBeforeTheFirstLayoutLandsOnTheEndState() {
            _bridge.Eval(@"
                const map = new CS.UnityEngine.UIElements.VisualElement()
                map.AddToClassList('map')
                __root.Add(map)
                map.AddToClassList('map--far')
            ");
            yield return Frames(5);

            Assert.AreEqual(600f, Map().resolvedStyle.translate.x, 0.5f,
                "a class present at the first style resolution is the starting style, so it jumps");
        }

        [UnityTest]
        public IEnumerator AClassAddedFromTheFirstGeometryChangedAnimates() {
            _bridge.Eval(@"
                const map = new CS.UnityEngine.UIElements.VisualElement()
                map.AddToClassList('map')
                const start = () => {
                    __eventAPI.removeEventListener(map, 'geometrychanged', start)
                    map.AddToClassList('map--far')
                }
                __eventAPI.addEventListener(map, 'geometrychanged', start)
                __root.Add(map)
            ");
            yield return Frames(5);

            var map = Map();
            Assert.IsTrue(map.ClassListContains("map--far"), "geometrychanged never reached JS");
            var x = map.resolvedStyle.translate.x;
            Assert.That(x, Is.LessThan(300f), "a 10 s transition should have barely started");
            Assert.That(x, Is.GreaterThanOrEqualTo(0f));
        }

        VisualElement Map() {
            Assert.AreEqual(1, _host.root.childCount);
            return _host.root[0];
        }

        IEnumerator Frames(int n) {
            for (var i = 0; i < n; i++) {
                _bridge.Tick();
                yield return null;
            }
        }
    }
}
