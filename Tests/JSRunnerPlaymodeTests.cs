using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace OneJS.Tests {
    /// <summary>
    /// PlayMode tests for JSRunner. A runner's project comes from its Panel Settings,
    /// and a real project needs a saved scene, so these cover what a runner does
    /// without one.
    /// </summary>
    [TestFixture]
    public class JSRunnerPlaymodeTests {
        GameObject _host;
        int _warnings;

        void Count(string message, string stackTrace, LogType type) {
            if (type == LogType.Warning && message.Contains("has no Panel Settings, so it runs nothing")) _warnings++;
        }

        [SetUp]
        public void SetUp() {
            _warnings = 0;
            Application.logMessageReceived += Count;
        }

        [TearDown]
        public void TearDown() {
            Application.logMessageReceived -= Count;
            if (_host != null) Object.DestroyImmediate(_host);
            foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                Object.DestroyImmediate(es.gameObject);
        }

        // Entering Play mode creates no Panel Settings (it did nothing at all from
        // February to October 2026 while the README said it did), so a runner
        // without one must say so, once, rather than retry silently every frame.
        [UnityTest]
        public IEnumerator NoPanelSettings_WarnsOnce() {
            _host = new GameObject("RunnerWithoutPanelSettings");
            _host.AddComponent<JSRunner>();
            for (int i = 0; i < 10; i++) yield return null;
            Assert.AreEqual(1, _warnings, "a runner without Panel Settings should warn exactly once");
        }
    }
}
