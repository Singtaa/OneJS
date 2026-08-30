// InputBridge lives in the OneJS.Runtime.InputSystem child assembly, which
// only compiles when the Input System package is installed; these tests
// follow it.
#if ENABLE_INPUT_SYSTEM
using System;
using System.Reflection;
using NUnit.Framework;
using OneJS.Input;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// GetKeyDown here always returned held state, the opposite of what the
    /// name means in Unity's own vocabulary, and the fix retired the name
    /// rather than repurposing it (a semantic swap would silently break
    /// every existing caller). These pin the deprecation: the old names
    /// still exist, carry [Obsolete] pointing at the replacements, and the
    /// replacements exist. If someone deletes an alias outright, or strips
    /// the attribute, this goes red before a consumer's project does.
    /// </summary>
    public class InputBridgeNamingTests {
        static void AssertDeprecatedAlias(string oldName, string replacement, Type[] signature) {
            var t = typeof(InputBridge);
            var oldMethod = t.GetMethod(oldName, signature);
            Assert.IsNotNull(oldMethod, $"{oldName} must keep existing as a deprecated alias.");

            var obsolete = oldMethod.GetCustomAttribute<ObsoleteAttribute>();
            Assert.IsNotNull(obsolete, $"{oldName} must carry [Obsolete].");
            StringAssert.Contains(replacement, obsolete.Message,
                $"{oldName}'s deprecation message must name {replacement}.");

            Assert.IsNotNull(t.GetMethod(replacement, signature),
                $"{replacement} must exist with the same signature as {oldName}.");
        }

        [Test]
        public void GetKeyDown_IsADeprecatedAliasOf_GetKeyHeld() {
            AssertDeprecatedAlias("GetKeyDown", "GetKeyHeld", new[] { typeof(string) });
        }

        [Test]
        public void GetKeyDownById_IsADeprecatedAliasOf_GetKeyHeldById() {
            AssertDeprecatedAlias("GetKeyDownById", "GetKeyHeldById", new[] { typeof(int) });
        }

        // The edge-triggered pair already used the Input System's own
        // vocabulary and must stay undecorated.
        [Test]
        public void GetKeyPressed_And_GetKeyReleased_AreNotDeprecated() {
            foreach (var name in new[] { "GetKeyPressed", "GetKeyReleased" }) {
                var m = typeof(InputBridge).GetMethod(name, new[] { typeof(string) });
                Assert.IsNotNull(m, $"{name} must exist.");
                Assert.IsNull(m.GetCustomAttribute<ObsoleteAttribute>(), $"{name} must not be deprecated.");
            }
        }
    }
}
#endif
