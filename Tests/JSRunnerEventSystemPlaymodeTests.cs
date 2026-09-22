using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
#if !ENABLE_LEGACY_INPUT_MANAGER && !(ENABLE_INPUT_SYSTEM && ONEJS_INPUT_SYSTEM_PACKAGE)
using System.Text.RegularExpressions;
using UnityEngine.TestTools;
#endif

namespace OneJS.Tests {
    /// <summary>
    /// JSRunner creates an EventSystem at play start so runtime panels get keyboard and
    /// gamepad navigation. The Input System module is added from another assembly, which
    /// exists only where the package is installed and active, so these pin what each input
    /// configuration actually ends up with rather than trusting that seam to be wired.
    /// </summary>
    [TestFixture]
    public class JSRunnerEventSystemPlaymodeTests {
        GameObject _host;

        [SetUp]
        public void SetUp() => DestroyEventSystems();

        [TearDown]
        public void TearDown() {
            if (_host != null) Object.DestroyImmediate(_host);
            DestroyEventSystems();
        }

        [Test]
        public void EnsureEventSystem_AddsTheModuleForTheActiveInputBackend() {
            // Inactive, so Awake and OnEnable do not start a real app.
            _host = new GameObject("JSRunnerHost");
            _host.SetActive(false);
            var runner = _host.AddComponent<JSRunner>();
#if !ENABLE_LEGACY_INPUT_MANAGER && !(ENABLE_INPUT_SYSTEM && ONEJS_INPUT_SYSTEM_PACKAGE)
            LogAssert.Expect(LogType.Warning, new Regex("com.unity.inputsystem package is not installed"));
#endif
            typeof(JSRunner).GetMethod("EnsureEventSystem", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(runner, null);

            var eventSystem = Object.FindAnyObjectByType<EventSystem>();
#if ENABLE_INPUT_SYSTEM && ONEJS_INPUT_SYSTEM_PACKAGE
            Assert.IsNotNull(eventSystem, "JSRunner should create an EventSystem.");
            var module = eventSystem.GetComponent<BaseInputModule>();
            Assert.AreEqual("UnityEngine.InputSystem.UI.InputSystemUIInputModule", module?.GetType().FullName);
            Assert.AreEqual(false, Get(module, "deselectOnBackgroundClick"),
                "Clicking a non-focusable area must keep the panel selected.");
            var bindings = (IEnumerable)Get(Get(Get(module, "submit"), "action"), "bindings");
            var paths = bindings.Cast<object>().Select(b => (string)Get(b, "path")).ToList();
            CollectionAssert.Contains(paths, "<Keyboard>/space", "Space should submit, as Enter does.");
#elif ENABLE_LEGACY_INPUT_MANAGER
            Assert.IsNotNull(eventSystem, "JSRunner should create an EventSystem.");
            Assert.IsInstanceOf<StandaloneInputModule>(eventSystem.GetComponent<BaseInputModule>());
#else
            // Player Settings select the Input System but the package is absent: there is no
            // module to give an EventSystem, so none is created and the project is told why.
            Assert.IsNull(eventSystem, "An EventSystem with no input module should not be created.");
#endif
        }

        static object Get(object target, string property) =>
            target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance).GetValue(target);

        static void DestroyEventSystems() {
            foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                Object.DestroyImmediate(es.gameObject);
        }
    }
}
