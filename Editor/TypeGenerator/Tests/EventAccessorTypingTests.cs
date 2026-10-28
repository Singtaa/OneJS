using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace OneJS.Editor.TypeGenerator.Tests {
    /// <summary>
    /// Issue #152's C#: an event whose add and remove forward to a state, as
    /// Spoke2OneJS generates, beside a field-like event.
    /// </summary>
    public class EventAccessorTypingFixture {
        readonly List<Action> _subscribers = new List<Action>();

        public string[] History => new string[9];

        public event Action OnHistoryChanged {
            add { _subscribers.Add(value); }
            remove { _subscribers.Remove(value); }
        }

        public int Score { get; private set; }
        public event Action OnScoreChanged;

        void Touch() => OnScoreChanged?.Invoke();
    }

    /// <summary>
    /// useEventSync(game, "History") needs the History property and
    /// add_OnHistoryChanged in the generated types. An event with its own
    /// accessors has to be declared exactly as a field-like one is (#152).
    /// </summary>
    [TestFixture]
    public class EventAccessorTypingTests {
        static string Declaration(string content, string member) {
            var line = Regex.Match(content, @"^\s*public " + Regex.Escape(member) + @"\(handler: [^)]*\): void;", RegexOptions.Multiline);
            return line.Success ? line.Value.Trim() : null;
        }

        [Test]
        public void CustomAccessorEvent_IsDeclaredLikeAFieldLikeOne() {
            var content = TypeGenerator.GenerateToResult(typeof(EventAccessorTypingFixture)).Content;

            StringAssert.Contains("History", content);
            var add = Declaration(content, "add_OnHistoryChanged");
            var remove = Declaration(content, "remove_OnHistoryChanged");
            var controlAdd = Declaration(content, "add_OnScoreChanged");
            Assert.IsNotNull(add, "no add_OnHistoryChanged in:\n" + content);
            Assert.IsNotNull(remove, "no remove_OnHistoryChanged in:\n" + content);
            Assert.IsNotNull(controlAdd, "no add_OnScoreChanged in:\n" + content);
            Assert.AreEqual(controlAdd.Replace("OnScoreChanged", "OnHistoryChanged"), add);
        }
    }
}
