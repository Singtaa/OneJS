using NUnit.Framework;
using UnityEngine.TestTools;
using OneJS;

namespace OneJS.Tests.Editor {
    /// <summary>
    /// What a data struct looks like from JavaScript.
    ///
    /// A struct with no instance methods is handed to JS as JSON rather than as a
    /// handle, so this text is the whole of its surface over there: a member missing
    /// here is undefined in a game, with nothing to look at that says why.
    ///
    /// From a report: a computed property was missing unless the struct also had a
    /// method. The method was never the point. A struct with any method is not
    /// serialized at all, so adding one moved the type onto the handle path, whose
    /// members resolve through general reflection and never had the bug.
    /// </summary>
    public class StructSerializationTests {
        struct WithComputed {
            public int a;
            public int b;
            public int Sum => a + b;
        }

        /// <summary>The reported shape: a property whose name is the field's, capitalised.</summary>
        struct NameAndName {
            public string name;
            public string Name => name == null ? "" : name.ToUpperInvariant();
        }

        struct Writable {
            public int a;
            public int Doubled { get; set; }
        }

        struct Throws {
            public int a;
            public string Bad => throw new System.InvalidOperationException("nope");
        }

        struct OnlyComputed {
            public int Answer => 42;
        }

        static string Json<T>(T value) where T : struct => QuickJSNative.SerializeStruct(value);

        [Test]
        public void ComputedPropertyIsSerialized() {
            // The defect: a getter with no setter was dropped, so JS saw a.b and no Sum.
            var json = Json(new WithComputed { a = 2, b = 3 });
            StringAssert.Contains("\"Sum\":5", json);
            StringAssert.Contains("\"a\":2", json);
        }

        [Test]
        public void ComputedPropertySurvivesAFieldOfTheSameNameInAnotherCase() {
            // The exact reported struct. Members differing only by case used to be
            // treated as one, so Name lost to name and never reached JS at all.
            var json = Json(new NameAndName { name = "abc" });
            StringAssert.Contains("\"name\":\"abc\"", json);
            StringAssert.Contains("\"Name\":\"ABC\"", json);
        }

        [Test]
        public void AWritablePropertyStillRoundTrips() {
            var json = Json(new Writable { a = 1, Doubled = 8 });
            StringAssert.Contains("\"Doubled\":8", json);

            var back = (Writable)QuickJSNative.DeserializeStruct(json, typeof(Writable));
            Assert.AreEqual(1, back.a);
            Assert.AreEqual(8, back.Doubled, "a property with a setter must still be written back");
        }

        [Test]
        public void AReadOnlyPropertyComesBackWithoutBeingWritten() {
            // Serializing a computed member means the JSON carries something with
            // nowhere to go. Reading it back must ignore it rather than throw.
            var json = Json(new WithComputed { a = 4, b = 6 });
            var back = (WithComputed)QuickJSNative.DeserializeStruct(json, typeof(WithComputed));
            Assert.AreEqual(4, back.a);
            Assert.AreEqual(6, back.b);
            Assert.AreEqual(10, back.Sum, "Sum is computed, so it follows the fields it is computed from");
        }

        [Test]
        public void OneThrowingPropertyDoesNotTakeTheStructWithIt() {
            // A computed property runs arbitrary code. The rest of the struct is
            // still worth having.
            LogAssert.ignoreFailingMessages = true;
            var json = Json(new Throws { a = 7 });
            LogAssert.ignoreFailingMessages = false;
            StringAssert.Contains("\"a\":7", json);
            Assert.IsNotNull(json);
        }

        [Test]
        public void AStructThatIsNothingButComputedMembersStillSerializes() {
            var json = Json(new OnlyComputed());
            StringAssert.Contains("\"Answer\":42", json);
        }
    }
}
