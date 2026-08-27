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
    }
}
