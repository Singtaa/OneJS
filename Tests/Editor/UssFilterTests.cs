using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OneJS.CustomStyleSheets;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// How an inline filter resolves a function name when reflection cannot find
    /// Unity's own table (a stripped player without OneJS's link.xml, or a Unity
    /// that renamed it): by FilterFunctionType's member names, which must give
    /// exactly what Unity's table gives, and say once that it is doing so.
    /// </summary>
    public class UssFilterTests {
        // Every function this Unity has, as USS writes it, and names that only a
        // loose match (the kebab-to-Pascal guess this once was) would take.
        static IEnumerable<string> Names() {
            foreach (var member in System.Enum.GetNames(typeof(FilterFunctionType))) {
                if (member is "None" or "Custom" or "Count") continue;
                yield return Regex.Replace(member, "(?<=.)([A-Z])", "-$1").ToLowerInvariant();
            }
            foreach (var odd in new[] { "BLUR", "Hue-Rotate", "hue--rotate", "blur-", "huerotate", "none", "custom", "count" })
                yield return odd;
        }

        static string Resolve(string name) => UssFilter.TryFunctionType(name, out var type) ? type.ToString() : "-";

        [Test]
        public void WithoutUnitysTable_NamesResolveAsUnitysTableResolvesThem() {
            var names = Names().ToList();
            var expected = names.Select(Resolve).ToList();
            Assert.That(expected.Count(e => e != "-"), Is.GreaterThanOrEqualTo(8), "precondition: Unity's table resolves this Unity's functions");
            using (UssFilter.WithoutUnityTableForTests()) {
                LogAssert.Expect(LogType.Warning, new Regex("filter function table"));
                CollectionAssert.AreEqual(expected, names.Select(Resolve).ToList(), string.Join(", ", names));
            }
        }

        [Test]
        public void WithoutUnitysTable_InlineFiltersStillParse_AndSayWhyOnce() {
            using (UssFilter.WithoutUnityTableForTests()) {
                LogAssert.Expect(LogType.Warning, new Regex("filter function table"));
                Assert.IsTrue(UssFilter.TryParse("blur(4px) hue-rotate(90deg)", out var filters, out var error), error);
                Assert.AreEqual(new[] { FilterFunctionType.Blur, FilterFunctionType.HueRotate }, filters.value.Select(f => f.type).ToArray());
                Assert.IsTrue(UssFilter.TryParse("grayscale(50%)", out _, out error), error);
                LogAssert.NoUnexpectedReceived();
            }
        }
    }
}
