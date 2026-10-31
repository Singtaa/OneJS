using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests {
    public interface IInterfaceMemberReadOnlyLabel {
        string Label { get; }
        string Describe();
    }

    public interface IInterfaceMemberWritableLabel {
        string Label { get; set; }
        string Describe();
    }

    /// <summary>
    /// Two interfaces that both declare Label and Describe, implemented
    /// explicitly with different results, the shape of VisualElement's
    /// IResolvedStyle.position (get-only) beside ITransform.position.
    /// </summary>
    public class InterfaceMemberTwoLabels : IInterfaceMemberReadOnlyLabel, IInterfaceMemberWritableLabel {
        string _written = "written";

        string IInterfaceMemberReadOnlyLabel.Label => "read-only";
        string IInterfaceMemberReadOnlyLabel.Describe() => "described read-only";
        string IInterfaceMemberWritableLabel.Label { get => _written; set => _written = value; }
        string IInterfaceMemberWritableLabel.Describe() => "described writable";

        public IInterfaceMemberReadOnlyLabel ReadOnly => this;
        public IInterfaceMemberWritableLabel Writable => this;
        public string Written => _written;
    }

    public static class InterfaceMemberTestHelper {
        public static VisualElement element;
        public static VisualElement GetElement() => element;
        public static bool Same(object a, object b) => ReferenceEquals(a, b);
    }

    /// <summary>
    /// A member that two interfaces declare, both implemented explicitly, is
    /// reached through the interface it was reached by, as C# reaches it: JS's
    /// element.transform.position is ITransform's, element.resolvedStyle.position
    /// is IResolvedStyle's. Setting a member on the object itself takes the
    /// declaration that can be written. Discord, 25 Oct: transform.position from
    /// JS threw "Set Method not found for 'position'", because the lookup took
    /// the first interface declaring the name, IResolvedStyle's get-only one.
    /// </summary>
    public class QuickJSInterfaceMemberTests {
        QuickJSContext _ctx;
        PanelHost _panel;

        [UnitySetUp]
        public IEnumerator SetUp() {
            _ctx = new QuickJSContext();
            _panel = PanelHost.Create(200, 200);
            InterfaceMemberTestHelper.element = _panel.AddRect(50, 50);
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            _ctx?.Dispose();
            _ctx = null;
            InterfaceMemberTestHelper.element = null;
            _panel?.Destroy();
            _panel = null;
            QuickJSNative.ClearAllHandles();
            yield return null;
        }

        string Eval(string code) => _ctx.Eval(code);

        [UnityTest]
        public IEnumerator TransformPosition_SetFromJS_ReadsBack() {
            Eval(@"
                var el = CS.OneJS.Tests.InterfaceMemberTestHelper.GetElement();
                el.transform.position = new CS.UnityEngine.Vector3(10, 20, 0);
            ");
            var element = InterfaceMemberTestHelper.element;
            Assert.AreEqual(10f, element.style.translate.value.x.value, "the set reached ITransform.position (style.translate)");
            Assert.AreEqual(20f, element.style.translate.value.y.value);
            yield return null;
            yield return null;
            var read = Eval(@"
                var p = CS.OneJS.Tests.InterfaceMemberTestHelper.GetElement().transform.position;
                [p.x, p.y, p.z].join(',');
            ");
            Assert.AreEqual("10,20,0", read, "transform.position reads back the Vector3");
        }

        [UnityTest]
        public IEnumerator TransformScale_SetFromJS_ReadsBack() {
            Eval(@"
                var el = CS.OneJS.Tests.InterfaceMemberTestHelper.GetElement();
                el.transform.scale = new CS.UnityEngine.Vector3(2, 3, 1);
            ");
            var element = InterfaceMemberTestHelper.element;
            Assert.AreEqual(new Vector3(2, 3, 1), element.style.scale.value.value, "the set reached ITransform.scale (style.scale)");
            yield return null;
            yield return null;
            var read = Eval(@"
                var s = CS.OneJS.Tests.InterfaceMemberTestHelper.GetElement().transform.scale;
                [s.x, s.y, s.z].join(',');
            ");
            Assert.AreEqual("2,3,1", read, "transform.scale reads back the Vector3");
        }

        [UnityTest]
        public IEnumerator TransformRotation_SetFromJS_ReadsBack() {
            Eval(@"
                var el = CS.OneJS.Tests.InterfaceMemberTestHelper.GetElement();
                el.transform.rotation = CS.UnityEngine.Quaternion.Euler(0, 0, 30);
            ");
            var element = InterfaceMemberTestHelper.element;
            Assert.AreEqual(30f, element.style.rotate.value.angle.ToDegrees(), 0.01f, "the set reached ITransform.rotation (style.rotate)");
            yield return null;
            yield return null;
            var read = Eval(@"
                var q = CS.OneJS.Tests.InterfaceMemberTestHelper.GetElement().transform.rotation;
                [q.z, q.w].map(function (v) { return v.toFixed(4); }).join(',');
            ");
            var expected = Quaternion.Euler(0, 0, 30);
            Assert.AreEqual($"{expected.z:F4},{expected.w:F4}", read, "transform.rotation reads back the Quaternion");
        }

        [UnityTest]
        public IEnumerator ResolvedStyle_KeepsItsOwnPositionAndScale() {
            Eval(@"
                var el = CS.OneJS.Tests.InterfaceMemberTestHelper.GetElement();
                el.transform.scale = new CS.UnityEngine.Vector3(2, 2, 1);
            ");
            yield return null;
            yield return null;
            var position = Eval(@"
                var el = CS.OneJS.Tests.InterfaceMemberTestHelper.GetElement();
                el.resolvedStyle.position === CS.UnityEngine.UIElements.Position.Absolute;
            ");
            Assert.AreEqual("true", position, "resolvedStyle.position is IResolvedStyle's Position");
            var scale = Eval(@"
                var el = CS.OneJS.Tests.InterfaceMemberTestHelper.GetElement();
                var s = el.resolvedStyle.scale;
                JSON.stringify(s.value || s);
            ");
            StringAssert.Contains("2", scale, "resolvedStyle.scale is IResolvedStyle's Scale");
        }

        [UnityTest]
        public IEnumerator ExplicitInterfaceMembers_ResolveByTheInterfaceReachedThrough() {
            var result = Eval(@"
                var t = new CS.OneJS.Tests.InterfaceMemberTwoLabels();
                t.Writable.Label = 'changed';
                [
                    t.Writable.Label,
                    t.ReadOnly.Label,
                    t.Written,
                    t.Writable.Describe(),
                    t.ReadOnly.Describe(),
                ].join('|');
            ");
            Assert.AreEqual("changed|read-only|changed|described writable|described read-only", result);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SettingAnAmbiguousMemberOnTheObject_TakesTheWritableOne() {
            var result = Eval(@"
                var t = new CS.OneJS.Tests.InterfaceMemberTwoLabels();
                t.Label = 'set on the object';
                t.Written;
            ");
            Assert.AreEqual("set on the object", result);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AnInterfaceView_IsStableAndIsTheSameObjectInCSharp() {
            var result = Eval(@"
                var t = new CS.OneJS.Tests.InterfaceMemberTwoLabels();
                var w = t.Writable;
                [
                    w === t.Writable,
                    CS.OneJS.Tests.InterfaceMemberTestHelper.Same(t, w),
                    CS.OneJS.Tests.InterfaceMemberTestHelper.Same(w, t.ReadOnly),
                ].join(',');
            ");
            Assert.AreEqual("true,true,true", result);
            yield return null;
        }
    }
}
