using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace OneJS.Tests {
    /// <summary>
    /// A UI event that running JS causes is delivered as a browser delivers it.
    /// el.focus(), el.blur() and el.click() run their handlers there and then,
    /// inside the caller, as WebGL already ran them, and so does the change a C#
    /// value setter JS calls raises. Microtasks wait until the outermost JS has
    /// returned. A text's content changing fires nothing, whoever changed it. (A
    /// value React writes raises no change at all: onejs-react writes it with
    /// SetValueWithoutNotify.)
    ///
    /// Before, the JS the tick ran had every such event dropped, focus included,
    /// and JS that C# called any other way (Eval, GetJSFunction, a C# delegate
    /// holding a JS function) had each one dispatched along with every pending
    /// microtask, in the middle of the call. A React commit made with flushSync
    /// from C# then had React's own scheduler run inside it ("Should not already
    /// be working").
    /// </summary>
    [TestFixture]
    public class EventReentrancyPlaymodeTests {
        GameObject _go;
        UIDocument _doc;
        PanelSettings _panelSettings;
        QuickJSUIBridge _bridge;
        EventReentrancyProbe _probe;

        VisualElement Root => _doc.rootVisualElement;

        // act(what) queues a microtask, does one thing, and returns what ran before
        // it returned, from the handlers and the microtask. A microtask that runs
        // later is counted in __microtasks.
        const string App = @"
            globalThis.__root = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', __rootHandle);
            const UIE = CS.UnityEngine.UIElements;
            const add = (el, name) => { el.name = name; __root.Add(el); return el; };
            const label = add(new UIE.TextElement(), 'label');
            const field = add(new UIE.TextField(), 'field');
            const toggle = add(new UIE.Toggle(), 'toggle');
            const slider = add(new UIE.Slider(), 'slider');
            const button = add(new UIE.Button(), 'button');
            globalThis.__ran = [];
            globalThis.__microtasks = 0;
            const on = (el, type, what) => __eventAPI.addEventListener(el, type, () => { __ran.push(what); });
            // On each element: these were added without the reconciler, so their
            // events do not bubble to __root in JS
            on(label, 'change', 'label change');
            on(field, 'change', 'field change');
            on(toggle, 'change', 'toggle change');
            on(slider, 'change', 'slider change');
            for (const type of ['focus', 'focusin', 'blur', 'focusout', 'click']) on(button, type, type);
            let n = 0;
            const actions = {
                focus: () => button.Focus(),
                // Focused first, with what that ran forgotten: the microtask
                // count still catches one run early
                blur: () => { button.Focus(); __ran = []; button.Blur(); },
                click: () => CS.OneJS.Tests.EventReentrancyProbe.Click(button),
                labelText: () => { label.text = `text ${++n}`; },
                fieldValue: () => { field.value = `value ${++n}`; },
                toggleValue: () => { toggle.value = !toggle.value; },
                sliderValue: () => { slider.value = ++n; },
                // How onejs-react writes a value prop
                fieldValueWithoutNotify: () => field.SetValueWithoutNotify(`quiet ${++n}`),
            };
            globalThis.act = (what) => {
                __ran = [];
                queueMicrotask(() => { __ran.push('microtask'); __microtasks++; });
                actions[what]();
                return __ran.join(', ');
            };
        ";

        [UnitySetUp]
        public IEnumerator SetUp() {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.themeStyleSheet =
                AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");
            _go = new GameObject("EventReentrancyHost");
            _doc = _go.AddComponent<UIDocument>();
            _doc.panelSettings = _panelSettings;
            yield return null;

            _bridge = new QuickJSUIBridge(Root);
            _bridge.Eval($"globalThis.__rootHandle = {QuickJSNative.RegisterObject(Root)}");
            _bridge.Eval(App, "event-reentrancy.js");
            _probe = new EventReentrancyProbe();
            _bridge.Eval($"globalThis.__probe = __csHelpers.wrapObject('OneJS.Tests.EventReentrancyProbe', {QuickJSNative.RegisterObject(_probe)});" +
                "__probe.Act = act;");
            _bridge.Tick();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            _bridge?.Dispose();
            _bridge = null;
            if (_go != null) Object.Destroy(_go);
            if (_panelSettings != null) Object.Destroy(_panelSettings);
            yield return null;
        }

        // What each action runs before it returns: its handlers, and nothing for a
        // text. Never the microtask.
        static readonly (string action, string ran)[] Expected = {
            ("focus", "focus, focusin"),
            ("blur", "blur, focusout"),
            ("click", "click"),
            ("labelText", ""),
            ("fieldValue", "field change"),
            ("toggleValue", "toggle change"),
            ("sliderValue", "slider change"),
            ("fieldValueWithoutNotify", ""),
        };

        static string[] Actions => Array.ConvertAll(Expected, e => e.action);

        [Test]
        public void GetJSFunction([ValueSource(nameof(Actions))] string action) =>
            AssertRan(action, _bridge.GetJSFunction<Func<string, string>>("act")(action));

        [Test]
        public void Eval([ValueSource(nameof(Actions))] string action) =>
            AssertRan(action, _bridge.Eval($"act('{action}')"));

        [Test]
        public void CSharpDelegate([ValueSource(nameof(Actions))] string action) =>
            AssertRan(action, _probe.Act(action));

        // JS the tick runs, as a timer, an effect or React's scheduler does
        [Test]
        public void Tick([ValueSource(nameof(Actions))] string action) {
            _bridge.Eval($"setTimeout(() => {{ globalThis.__result = act('{action}'); }}, 0)");
            var before = Microtasks;
            _bridge.Tick();
            Assert.AreEqual(Ran(action), _bridge.Eval("__result"), $"{action} inside the tick's JS");
            Assert.AreEqual(before + 1, Microtasks, "the microtask ran once the tick's JS had returned");
        }

        // A text changing never reports a change, even when C# changes it
        [Test]
        public void TextSetFromCSharp_ReachesNoHandler() {
            _bridge.Eval("__ran = []");
            Root.Q<TextElement>("label").text = "set by C#";
            Assert.AreEqual("", _bridge.Eval("__ran.join(', ')"));
        }

        // What a user does still reports its change: here, a value set from C#
        // outside any JS, as an input handler sets it
        [Test]
        public void ValueSetOutsideJS_FiresChange() {
            _bridge.Eval("__ran = []");
            Root.Q<TextField>("field").value = "typed";
            Root.Q<Toggle>("toggle").value = !Root.Q<Toggle>("toggle").value;
            Assert.AreEqual("field change, toggle change", _bridge.Eval("__ran.join(', ')"));
        }

        int Microtasks => int.Parse(_bridge.Eval("String(__microtasks)"));

        static string Ran(string action) => Array.Find(Expected, e => e.action == action).ran;

        void AssertRan(string action, string ranInside) {
            Assert.AreEqual(Ran(action), ranInside, $"{action}: what ran inside the call");
            var before = Microtasks;
            _bridge.Tick();
            Assert.AreEqual(before + 1, Microtasks, "the microtask ran once the call had returned");
        }
    }

    /// <summary>Lets JS click an element through C#, and holds a JS function in a C# delegate, for EventReentrancyPlaymodeTests.</summary>
    public class EventReentrancyProbe {
        public Func<string, string> Act;

        public static void Click(VisualElement element) {
            using var click = ClickEvent.GetPooled();
            click.target = element;
            element.SendEvent(click);
        }
    }
}
