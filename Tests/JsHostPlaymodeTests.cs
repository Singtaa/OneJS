using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests {
    /// <summary>What a bundle's onPlay and onStop did, in order.</summary>
    public static class LifecycleLog {
        public static readonly List<string> Entries = new List<string>();
        public static void Add(string entry) => Entries.Add(entry);
        public static string Read() => string.Join(",", Entries);
    }

    /// <summary>
    /// JSRunner and JSPad run their apps through one JsHost, so every rebuild
    /// calls onStop before the context goes and onPlay after the new one runs,
    /// and both components give an app the same globals.
    /// </summary>
    [TestFixture]
    public class JsHostPlaymodeTests {
        const string Bundle = @"
            var __exports = (function () {
                return {
                    onPlay: function () { CS.OneJS.Tests.LifecycleLog.Add('play:' + __isPlaying) },
                    onStop: function () { CS.OneJS.Tests.LifecycleLog.Add('stop') },
                }
            })();";

        [SetUp]
        public void SetUp() => LifecycleLog.Entries.Clear();

        [TearDown]
        public void TearDown() => QuickJSNative.ClearAllHandles();

        [Test]
        public void Recreate_StopsTheRunningAppBeforeStartingTheNextOne() {
            var host = new JsHost("Test");
            var root = new VisualElement();
            try {
                host.Create(root, Application.persistentDataPath);
                host.Run(Bundle, "app.js");
                host.InvokeOnPlay();

                host.Recreate(root, Application.persistentDataPath, Bundle, "app.js",
                    afterOnStop: () => LifecycleLog.Add("cleanup"));
                host.Stop();

                Assert.AreEqual("play:true,stop,cleanup,play:true,stop", LifecycleLog.Read());
                Assert.IsNull(host.Bridge);
            } finally {
                host.Dispose();
            }
        }

        [Test]
        public void Tick_RunsTheAppsFrameCallbacks() {
            var host = new JsHost("Test");
            try {
                host.Create(new VisualElement(), Application.persistentDataPath);
                host.Run("requestAnimationFrame(function () { CS.OneJS.Tests.LifecycleLog.Add('frame') })", "app.js");
                host.Tick();
                Assert.AreEqual("frame", LifecycleLog.Read());
            } finally {
                host.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator JSPad_Reenabled_CallsOnStopThenOnPlay() {
            var go = new GameObject("JSPadHost");
            go.SetActive(false);
            var pad = go.AddComponent<JSPad>();
            var compress = typeof(JSPad).GetMethod("CompressString", BindingFlags.NonPublic | BindingFlags.Static);
            typeof(JSPad).GetField("_compressedBundle", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(pad, compress.Invoke(null, new object[] { Bundle }));
            try {
                go.SetActive(true);
                yield return null; // Start runs the bundle
                Assert.AreEqual("play:true", LifecycleLog.Read(), "JSPad sets __isPlaying and calls onPlay");

                go.SetActive(false);
                go.SetActive(true);
                yield return null;
                Assert.AreEqual("play:true,stop,play:true", LifecycleLog.Read());
            } finally {
                Object.DestroyImmediate(go);
            }
        }
    }
}
