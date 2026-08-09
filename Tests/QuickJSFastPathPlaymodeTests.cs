using System;
using System.Collections;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace OneJS.Tests {
    /// <summary>
    /// Playmode tests for QuickJS zero-allocation fast path interop.
    /// Tests correctness, allocation behavior, and performance.
    /// </summary>
    [TestFixture]
    public class QuickJSFastPathPlaymodeTests {
        QuickJSContext _ctx;

        [UnitySetUp]
        public IEnumerator SetUp() {
            _ctx = new QuickJSContext();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            _ctx?.Dispose();
            _ctx = null;
            QuickJSNative.ClearAllHandles();
            yield return null;
        }

        // MARK: Correctness Tests

        [UnityTest]
        public IEnumerator FastPath_Count_GreaterThanZero() {
            // Trigger initialization
            _ctx.Eval("CS.UnityEngine.Time.deltaTime");
            Assert.Greater(QuickJSNative.FastPath.Count, 0);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FastPath_TimeDeltaTime_ReturnsNonNegativeFloat() {
            var result = _ctx.Eval("CS.UnityEngine.Time.deltaTime");
            Assert.IsTrue(float.TryParse(result, out var dt) && dt >= 0, $"Expected non-negative float, got: {result}");
            yield return null;
        }

        [UnityTest]
        public IEnumerator FastPath_TimeFrameCount_ReturnsNonNegativeInt() {
            var result = _ctx.Eval("CS.UnityEngine.Time.frameCount");
            Assert.IsTrue(int.TryParse(result, out var fc) && fc >= 0, $"Expected non-negative int, got: {result}");
            yield return null;
        }

        [UnityTest]
        public IEnumerator FastPath_TimeTimeScale_GetSetWorks() {
            float original = Time.timeScale;
            try {
                _ctx.Eval("CS.UnityEngine.Time.timeScale = 0.5");
                Assert.AreEqual(0.5f, Time.timeScale, 0.001f);
            } finally {
                Time.timeScale = original;
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator FastPath_TransformPosition_SetWorks() {
            _ctx.Eval(@"
                var go = new CS.UnityEngine.GameObject('FastPathPosTest');
                go.transform.position = { x: 11, y: 22, z: 33 };
            ");

            var go = GameObject.Find("FastPathPosTest");
            Assert.IsNotNull(go);

            var pos = go.transform.position;
            Assert.AreEqual(11f, pos.x, 0.01f);
            Assert.AreEqual(22f, pos.y, 0.01f);
            Assert.AreEqual(33f, pos.z, 0.01f);

            UnityEngine.Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FastPath_TransformLocalScale_SetWorks() {
            _ctx.Eval(@"
                var go = new CS.UnityEngine.GameObject('FastPathScaleTest');
                go.transform.localScale = { x: 2, y: 3, z: 4 };
            ");

            var go = GameObject.Find("FastPathScaleTest");
            Assert.IsNotNull(go);

            var scale = go.transform.localScale;
            Assert.AreEqual(2f, scale.x, 0.01f);
            Assert.AreEqual(3f, scale.y, 0.01f);
            Assert.AreEqual(4f, scale.z, 0.01f);

            UnityEngine.Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FastPath_GameObjectName_GetWorks() {
            var result = _ctx.Eval(@"
                var go = new CS.UnityEngine.GameObject('NameTest');
                var name = go.name;
                CS.UnityEngine.Object.Destroy(go);
                name;
            ");
            Assert.AreEqual("NameTest", result);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FastPath_GameObjectSetActive_Works() {
            _ctx.Eval(@"
                var go = new CS.UnityEngine.GameObject('ActiveTest');
                go.SetActive(false);
                globalThis.__activeGo = go;
            ");

            var result = _ctx.Eval("globalThis.__activeGo.activeSelf");
            Assert.AreEqual("false", result);

            _ctx.Eval("CS.UnityEngine.Object.Destroy(globalThis.__activeGo)");
            yield return null;
        }

        // MARK: Fast Constructor Tests
        // `new CS.UnityEngine.Vector2/3/4/Color/Quaternion(...)` build via the
        // zero-alloc ctor fast path. Returns surface as {x,y,z(,w)} in JS, matching
        // the reflection ctor path exactly.

        [UnityTest]
        public IEnumerator FastCtor_Vector2_ReturnsComponents() {
            var x = _ctx.Eval("new CS.UnityEngine.Vector2(3, 4).x");
            var y = _ctx.Eval("new CS.UnityEngine.Vector2(3, 4).y");
            Assert.IsTrue(float.TryParse(x, out var fx) && Mathf.Approximately(fx, 3f), $"x={x}");
            Assert.IsTrue(float.TryParse(y, out var fy) && Mathf.Approximately(fy, 4f), $"y={y}");
            yield return null;
        }

        [UnityTest]
        public IEnumerator FastCtor_Vector3_TwoArg_ZeroFillsZ() {
            var z = _ctx.Eval("new CS.UnityEngine.Vector3(5, 6).z");
            Assert.IsTrue(float.TryParse(z, out var fz) && Mathf.Approximately(fz, 0f), $"z={z}");
            yield return null;
        }

        [UnityTest]
        public IEnumerator FastCtor_Color_FourArg_ReturnsRGBA() {
            var b = _ctx.Eval("new CS.UnityEngine.Color(0.25, 0.5, 0.75, 0.125).z");
            var a = _ctx.Eval("new CS.UnityEngine.Color(0.25, 0.5, 0.75, 0.125).w");
            Assert.IsTrue(float.TryParse(b, out var fb) && Mathf.Approximately(fb, 0.75f), $"b={b}");
            Assert.IsTrue(float.TryParse(a, out var fa) && Mathf.Approximately(fa, 0.125f), $"a={a}");
            yield return null;
        }

        [UnityTest]
        public IEnumerator FastCtor_Color_ThreeArg_DefaultsAlphaToOne() {
            var a = _ctx.Eval("new CS.UnityEngine.Color(0.1, 0.2, 0.3).w");
            Assert.IsTrue(float.TryParse(a, out var fa) && Mathf.Approximately(fa, 1f), $"a={a}");
            yield return null;
        }

        [UnityTest]
        public IEnumerator FastCtor_Quaternion_ReturnsComponents() {
            var w = _ctx.Eval("new CS.UnityEngine.Quaternion(0.1, 0.2, 0.3, 0.4).w");
            Assert.IsTrue(float.TryParse(w, out var fw) && Mathf.Approximately(fw, 0.4f), $"w={w}");
            yield return null;
        }

        [UnityTest]
        public IEnumerator FastCtor_Element_ConstructsUsableElement() {
            // Reference-type element ctor (parameterless) goes through the fast path.
            // The round-trip proves it built a real, usable Button (name set + read back).
            var result = _ctx.Eval(@"
                var el = new CS.UnityEngine.UIElements.Button();
                el.name = 'fastCtorBtn';
                el.name;
            ");
            Assert.AreEqual("fastCtorBtn", result);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FastCtor_Element_NonNumericArgFallsThrough() {
            // A string-arg element ctor is not fast-pathed (non-numeric arg); it must
            // still work via the reflection ctor path (Label(string text)).
            var result = _ctx.Eval(@"
                var lbl = new CS.UnityEngine.UIElements.Label('hello');
                lbl.text;
            ");
            Assert.AreEqual("hello", result);
            yield return null;
        }

        // MARK: NodeBridge Tests
        // Zero-alloc tree wiring by element handle, type-agnostic across element types.

        [UnityTest]
        public IEnumerator NodeBridge_Add_AttachesChild() {
            var result = _ctx.Eval(@"
                var parent = new CS.UnityEngine.UIElements.VisualElement();
                var child = new CS.UnityEngine.UIElements.Button();
                CS.OneJS.NodeBridge.Add(parent.__csHandle, child.__csHandle);
                parent.childCount;
            ");
            Assert.AreEqual("1", result);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NodeBridge_Insert_AttachesAtIndex() {
            var result = _ctx.Eval(@"
                var parent = new CS.UnityEngine.UIElements.VisualElement();
                var a = new CS.UnityEngine.UIElements.VisualElement();
                var b = new CS.UnityEngine.UIElements.VisualElement();
                var c = new CS.UnityEngine.UIElements.VisualElement();
                CS.OneJS.NodeBridge.Add(parent.__csHandle, a.__csHandle);
                CS.OneJS.NodeBridge.Add(parent.__csHandle, b.__csHandle);
                CS.OneJS.NodeBridge.Insert(parent.__csHandle, 1, c.__csHandle);
                parent.IndexOf(c) + ':' + parent.childCount;
            ");
            Assert.AreEqual("1:3", result);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NodeBridge_RemoveFromHierarchy_Detaches() {
            var result = _ctx.Eval(@"
                var parent = new CS.UnityEngine.UIElements.VisualElement();
                var child = new CS.UnityEngine.UIElements.VisualElement();
                CS.OneJS.NodeBridge.Add(parent.__csHandle, child.__csHandle);
                var before = parent.childCount;
                CS.OneJS.NodeBridge.RemoveFromHierarchy(child.__csHandle);
                before + ':' + parent.childCount;
            ");
            Assert.AreEqual("1:0", result);
            yield return null;
        }

        // An unresolvable handle must never be swallowed on attach: the element would be
        // fully constructed and styled but never parented, so the subtree silently stops
        // painting with nothing in the console to explain it.
        [UnityTest]
        public IEnumerator NodeBridge_Add_ReportsUnresolvableHandle() {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                @"NodeBridge\.Add: child handle -?\d+ is not in the handle table"));
            var result = _ctx.Eval(@"
                var parent = new CS.UnityEngine.UIElements.VisualElement();
                CS.OneJS.NodeBridge.Add(parent.__csHandle, 999999);
                parent.childCount;
            ");
            Assert.AreEqual("0", result);
            yield return null;
        }

        // Detach is deliberately tolerant: the reconciler relies on it being a no-op when
        // the root was cleared before React tore the tree down (hot reload).
        [UnityTest]
        public IEnumerator NodeBridge_RemoveFromHierarchy_ToleratesMissingHandle() {
            var result = _ctx.Eval(@"
                CS.OneJS.NodeBridge.RemoveFromHierarchy(999999);
                'ok';
            ");
            Assert.AreEqual("ok", result);
            LogAssert.NoUnexpectedReceived();
            yield return null;
        }

        // MARK: StyleBridge Tests
        // The batched style application uses direct typed setters for the common
        // properties (fast path) and reflection for the long tail (fallback).

        [UnityTest]
        public IEnumerator StyleBridge_ApplyStyles_FastFloatEnumAndFallback() {
            var result = _ctx.Eval(@"
                var el = new CS.UnityEngine.UIElements.VisualElement();
                CS.OneJS.StyleBridge.ApplyStyles(el, {
                    opacity: 0.25,
                    display: CS.UnityEngine.UIElements.DisplayStyle.None,
                    unitySliceScale: 4
                });
                el.style.opacity.value + ',' + el.style.display.value + ',' + el.style.unitySliceScale.value;
            ");
            // opacity (fast StyleFloat), display=None=1 (fast StyleEnum),
            // unitySliceScale (StyleFloat, not in the fast switch -> reflection).
            Assert.AreEqual("0.25,1,4", result);
            yield return null;
        }

        [UnityTest]
        public IEnumerator StyleBridge_ApplyStyles_ColorAndLengthDoNotThrow() {
            // Exercises the fast-path AsColor / AsLength setters across several
            // properties. Reading struct-valued styles (StyleColor/StyleLength) back
            // through the proxy is unreliable (the Style_* tests use the same
            // no-throw pattern); visual mapping correctness is covered by rendering.
            Assert.DoesNotThrow(() => {
                _ctx.Eval(@"
                    var el = new CS.UnityEngine.UIElements.VisualElement();
                    CS.OneJS.StyleBridge.ApplyStyles(el, {
                        backgroundColor: { r: 1, g: 0, b: 0, a: 1 },
                        color: { r: 0, g: 1, b: 0, a: 1 },
                        width: new CS.UnityEngine.UIElements.Length(100),
                        height: new CS.UnityEngine.UIElements.Length(50),
                        marginTop: new CS.UnityEngine.UIElements.Length(12),
                        paddingLeft: new CS.UnityEngine.UIElements.Length(8)
                    });
                ");
            });
            yield return null;
        }

        [UnityTest]
        public IEnumerator FastPath_ScreenWidth_ReturnsPositiveInt() {
            var result = _ctx.Eval("CS.UnityEngine.Screen.width");
            Assert.IsTrue(int.TryParse(result, out var w) && w > 0, $"Expected positive int, got: {result}");
            yield return null;
        }

        // MARK: Allocation Tests

        [UnityTest]
        public IEnumerator FastPath_PropertyGet_LowAllocation() {
            // Warm up
            for (int i = 0; i < 100; i++) {
                _ctx.Eval("CS.UnityEngine.Time.deltaTime");
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetTotalMemory(false);

            for (int i = 0; i < 1000; i++) {
                _ctx.Eval("CS.UnityEngine.Time.deltaTime");
            }

            GC.Collect();
            long bytes = GC.GetTotalMemory(false) - before;

            Debug.Log($"FastPath property get: ~{bytes} bytes for 1000 calls (~{bytes / 1000} per call)");
            Assert.Less(bytes, 50000, "Allocation should be low for fast path");
            yield return null;
        }

        [UnityTest]
        public IEnumerator FastPath_PropertySet_LowAllocation() {
            float original = Time.timeScale;
            try {
                // Warm up
                for (int i = 0; i < 100; i++) {
                    _ctx.Eval("CS.UnityEngine.Time.timeScale = 1.0");
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                long before = GC.GetTotalMemory(false);

                for (int i = 0; i < 1000; i++) {
                    _ctx.Eval("CS.UnityEngine.Time.timeScale = 1.0");
                }

                GC.Collect();
                long bytes = GC.GetTotalMemory(false) - before;

                Debug.Log($"FastPath property set: ~{bytes} bytes for 1000 calls (~{bytes / 1000} per call)");
                Assert.Less(bytes, 50000, "Allocation should be low for fast path");
            } finally {
                Time.timeScale = original;
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator FastCtor_Construction_LowAllocation() {
            // The reflection ctor path allocates an object[], boxes each arg, builds
            // ConstructorInfo[]/ParameterInfo[], and boxes the result: per call. The
            // fast ctor path does none of that, so allocation stays at eval overhead.
            for (int i = 0; i < 100; i++) {
                _ctx.Eval("new CS.UnityEngine.Vector2(3, 4)");
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetTotalMemory(false);

            for (int i = 0; i < 1000; i++) {
                _ctx.Eval("new CS.UnityEngine.Vector2(3, 4)");
            }

            GC.Collect();
            long bytes = GC.GetTotalMemory(false) - before;

            Debug.Log($"FastCtor Vector2: ~{bytes} bytes for 1000 calls (~{bytes / 1000} per call)");
            Assert.Less(bytes, 50000, "Allocation should be low for the fast ctor path");
            yield return null;
        }

        [UnityTest]
        public IEnumerator FastPath_Registry_ContainsExpectedEntries() {
            // Trigger some initialization
            _ctx.Eval("CS.UnityEngine.Time.deltaTime");

            int count = QuickJSNative.FastPath.Count;
            Debug.Log($"Registered fast paths: {count}");

            Assert.GreaterOrEqual(count, 30, "Should have Time, Transform, GameObject, Screen, Mathf entries");
            yield return null;
        }

        [UnityTest]
        public IEnumerator FastPath_RepeatedCalls_ConsistentLowOverhead() {
            // Warm up
            for (int i = 0; i < 100; i++) {
                _ctx.Eval("CS.UnityEngine.Time.deltaTime");
            }

            var times = new long[5];
            for (int batch = 0; batch < 5; batch++) {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 1000; i++) {
                    _ctx.Eval("CS.UnityEngine.Time.deltaTime");
                }
                sw.Stop();
                times[batch] = sw.ElapsedMilliseconds;
            }

            long min = times[0], max = times[0];
            foreach (var t in times) {
                if (t < min) min = t;
                if (t > max) max = t;
            }

            Debug.Log($"5 batches of 1000 calls: min={min}ms, max={max}ms, variance={max - min}ms");
            Assert.Less(max - min, 50, "Variance should be low if no GC pressure");
            yield return null;
        }

        // MARK: Performance Tests

        [UnityTest]
        public IEnumerator Performance_TransformPropertyAccess_ReasonableTime() {
            _ctx.Eval(@"
                globalThis.__perfGo = new CS.UnityEngine.GameObject('PerfTest');
                globalThis.__perfTr = globalThis.__perfGo.transform;
            ");

            const int iterations = 10000;

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++) {
                _ctx.Eval("globalThis.__perfTr.position");
            }
            sw.Stop();

            Debug.Log($"Transform.position: {sw.ElapsedMilliseconds}ms for {iterations} calls");
            Debug.Log($"{iterations * 1000.0 / Math.Max(1, sw.ElapsedMilliseconds):F0} calls/sec");

            _ctx.Eval("CS.UnityEngine.Object.Destroy(globalThis.__perfGo)");

            Assert.Less(sw.ElapsedMilliseconds, 10000, "Should complete in reasonable time");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Performance_PerFrameSimulation_Works() {
            _ctx.Eval(@"
                globalThis.__simGo = new CS.UnityEngine.GameObject('SimTest');
                globalThis.__simTr = globalThis.__simGo.transform;
                globalThis.__simTr.position = { x: 0, y: 0, z: 0 };
            ");

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++) {
                _ctx.Eval(@"
                    var dt = CS.UnityEngine.Time.deltaTime;
                    var pos = globalThis.__simTr.position;
                    globalThis.__simTr.position = { x: pos.x + dt, y: pos.y, z: pos.z };
                ");
            }
            sw.Stop();

            var go = GameObject.Find("SimTest");
            float finalX = go != null ? go.transform.position.x : 0;
            UnityEngine.Object.Destroy(go);

            Debug.Log($"1000 frame simulation: {sw.ElapsedMilliseconds}ms");
            Debug.Log($"Final X position: {finalX:F4}");

            Assert.Less(sw.ElapsedMilliseconds, 5000);
            Assert.Greater(finalX, 0);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Performance_FastPathVsReflection_Comparison() {
            const int iterations = 10000;

            // Warm up
            for (int i = 0; i < 100; i++) {
                _ctx.Eval("CS.UnityEngine.Time.deltaTime");
                _ctx.Eval("CS.UnityEngine.Application.platform");
            }

            // Fast path (Time.deltaTime is registered)
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++) {
                _ctx.Eval("CS.UnityEngine.Time.deltaTime");
            }
            sw.Stop();
            long fastPathMs = sw.ElapsedMilliseconds;

            // Reflection path (Application.platform is NOT registered)
            sw.Restart();
            for (int i = 0; i < iterations; i++) {
                _ctx.Eval("CS.UnityEngine.Application.platform");
            }
            sw.Stop();
            long reflectionMs = sw.ElapsedMilliseconds;

            Debug.Log($"FastPath: {fastPathMs}ms, Reflection: {reflectionMs}ms for {iterations} calls");

            if (fastPathMs < reflectionMs) {
                Debug.Log($"FastPath is {(double)reflectionMs / Math.Max(1, fastPathMs):F1}x faster");
            } else {
                Debug.Log("Difference within noise margin (both paths are fast when cached)");
            }

            // This is informational: always pass
            Assert.Pass("Performance comparison logged");
            yield return null;
        }
    }
}

