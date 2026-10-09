using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// A style key that matches nothing on IStyle used to be dropped without a
    /// trace, so a typo like "colr" produced an element that silently ignored
    /// the style. These pin the replacement behavior: one warning per unknown
    /// key, and no warning at all for keys that resolve.
    /// </summary>
    public class StyleBridgeTests {
        // The warned-key set is static and survives across tests in a domain,
        // so every test uses keys no other test touches.

        [Test]
        public void UnknownKey_WarnsOnce_ThenStaysQuiet() {
            var el = new VisualElement();
            var styles = new Dictionary<string, object> { { "colrForWarnOnceTest", "red" } };

            LogAssert.Expect(LogType.Warning, new Regex("colrForWarnOnceTest"));
            StyleBridge.ApplyStyles(el, styles);
            StyleBridge.ApplyStyles(el, styles);
            StyleBridge.ApplyStyles(new VisualElement(), styles);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void TwoUnknownKeys_EachGetTheirOwnWarning() {
            var el = new VisualElement();

            LogAssert.Expect(LogType.Warning, new Regex("bckgroundColorTypoTest"));
            LogAssert.Expect(LogType.Warning, new Regex("flexGorwTypoTest"));
            StyleBridge.ApplyStyles(el, new Dictionary<string, object> {
                { "bckgroundColorTypoTest", "blue" },
                { "flexGorwTypoTest", 1 },
            });
            LogAssert.NoUnexpectedReceived();
        }

        // unityParagraphSpacing is deliberately a property the fast path does
        // not cover, so this exercises the same reflective branch the warning
        // lives in and proves a resolvable key still applies silently.
        [Test]
        public void KnownReflectiveKey_AppliesWithoutWarning() {
            var el = new VisualElement();
            StyleBridge.ApplyStyles(el, new Dictionary<string, object> {
                { "unityParagraphSpacing", 7 },
            });

            Assert.AreEqual(7f, el.style.unityParagraphSpacing.value.value);
            LogAssert.NoUnexpectedReceived();
        }

        // onejs-react sends a key React removed from a style as null. It must clear
        // the inline value (StyleKeyword.Null), so the sheet's shows again, on the
        // fast path and the reflective one alike: the property's default would be
        // width 0, opacity 0, a transparent background.
        [Test]
        public void NullValue_ClearsTheInlineValue() {
            var el = new VisualElement();
            StyleBridge.ApplyStyles(el, new Dictionary<string, object> {
                { "width", 77 }, { "opacity", 0.9 }, { "backgroundColor", new Color(0, 1, 0) },
                { "display", 1 }, { "unityParagraphSpacing", 7 }, { "letterSpacing", 2 },
            });
            StyleBridge.ApplyStyles(el, new Dictionary<string, object> {
                { "width", null }, { "opacity", null }, { "backgroundColor", null },
                { "display", null }, { "unityParagraphSpacing", null }, { "letterSpacing", null },
            });

            Assert.AreEqual(StyleKeyword.Null, el.style.width.keyword, "width");
            Assert.AreEqual(StyleKeyword.Null, el.style.opacity.keyword, "opacity");
            Assert.AreEqual(StyleKeyword.Null, el.style.backgroundColor.keyword, "backgroundColor");
            Assert.AreEqual(StyleKeyword.Null, el.style.display.keyword, "display");
            Assert.AreEqual(StyleKeyword.Null, el.style.unityParagraphSpacing.keyword, "unityParagraphSpacing");
            Assert.AreEqual(StyleKeyword.Null, el.style.letterSpacing.keyword, "letterSpacing");
            Assert.AreEqual(StyleKeyword.Null, el.style.unityTextGenerator.keyword, "letterSpacing's text generator");
            LogAssert.NoUnexpectedReceived();
        }

        // A filter React animates sends a new string every frame. One it cannot read
        // warns once for the property, not once per value, which would grow the
        // warned set and the console without bound.
        [Test]
        public void UnreadableFilter_WarnsOncePerProperty_NotPerValue() {
            var el = new VisualElement();
            LogAssert.Expect(LogType.Warning, new Regex("filter"));
            for (int i = 0; i < 5; i++)
                StyleBridge.ApplyStyles(el, new Dictionary<string, object> { { "filter", $"nosuch({i}px)" } });
            LogAssert.NoUnexpectedReceived();
        }

        // Clearing a key this Unity does not have (backdropFilter before 6.6) is
        // what setting it is: warned about once, never thrown.
        [Test]
        public void NullValue_ForAnUnknownKey_WarnsLikeAValue() {
            var el = new VisualElement();
            LogAssert.Expect(LogType.Warning, new Regex("clearedUnknownKeyTest"));
            StyleBridge.ApplyStyles(el, new Dictionary<string, object> { { "clearedUnknownKeyTest", "x" } });
            StyleBridge.ApplyStyles(el, new Dictionary<string, object> { { "clearedUnknownKeyTest", null } });
            LogAssert.NoUnexpectedReceived();
        }

        // onejs-react's className update: what went, then what came, in one call.
        // A string array arrives typed; an empty JS array arrives as an untyped list.
        [Test]
        public void UpdateClasses_RemovesThenAdds() {
            var el = new VisualElement();
            StyleBridge.AddClassesBatch(el, new[] { "a", "b", "c" });
            StyleBridge.UpdateClasses(el, new[] { "b", "c" }, new List<object> { "d", "" });
            CollectionAssert.AreEquivalent(new[] { "a", "d" }, el.GetClasses());
            StyleBridge.UpdateClasses(el, new List<object>(), new[] { "e" });
            CollectionAssert.AreEquivalent(new[] { "a", "d", "e" }, el.GetClasses());
            Assert.IsTrue(StyleBridge.UpdatesClasses);
        }
    }
}
