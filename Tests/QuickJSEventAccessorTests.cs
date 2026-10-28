using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace OneJS.Tests {
    /// <summary>
    /// A value that keeps its own subscribers, the shape of issue #152's State and
    /// of the Spoke signals its source generator wraps.
    /// </summary>
    public class EventAccessorState<T> {
        readonly List<Action> _subscribers = new List<Action>();
        T _now;

        public EventAccessorState(T initial) { _now = initial; }

        public T Now => _now;
        public int SubscriberCount => _subscribers.Count;

        public void Set(T value) {
            _now = value;
            foreach (var s in _subscribers.ToArray()) s();
        }

        public void Subscribe(Action action) { _subscribers.Add(action); }
        public void Unsubscribe(Action action) { _subscribers.Remove(action); }
    }

    /// <summary>
    /// Issue #152's C# as written: an event whose add and remove hand the delegate
    /// to the state, beside a plain field-like event as the control.
    /// </summary>
    public class EventAccessorGame {
        public static EventAccessorGame Instance;

        readonly EventAccessorState<string[]> _history = new EventAccessorState<string[]>(new string[9]);

        public string[] History => _history.Now;
        public int HistorySubscribers => _history.SubscriberCount;

        public event Action OnHistoryChanged {
            add { _history.Subscribe(value); }
            remove { _history.Unsubscribe(value); }
        }

        public int Score { get; private set; }
        public event Action OnScoreChanged;

        public void Play(string move) {
            var next = (string[])_history.Now.Clone();
            next[0] = move;
            _history.Set(next);
        }

        public void AddScore(int points) {
            Score += points;
            OnScoreChanged?.Invoke();
        }
    }

    /// <summary>
    /// useEventSync subscribes through add_On{Name}Changed. An event with its own
    /// add and remove accessors has to work exactly like a field-like one (#152).
    /// </summary>
    [TestFixture]
    public class QuickJSEventAccessorTests {
        QuickJSContext _ctx;

        [UnitySetUp]
        public IEnumerator SetUp() {
            EventAccessorGame.Instance = new EventAccessorGame();
            _ctx = new QuickJSContext();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown() {
            _ctx?.Dispose();
            _ctx = null;
            EventAccessorGame.Instance = null;
            QuickJSNative.ClearAllHandles();
            yield return null;
        }

        /// <summary>The subscribe and unsubscribe useEventSync does, against one event.</summary>
        void Subscribe(string eventName) {
            _ctx.Eval($@"
                globalThis.__game = CS.OneJS.Tests.EventAccessorGame.Instance;
                globalThis.__fired = 0;
                globalThis.__seen = null;
                globalThis.__handler = () => {{ globalThis.__fired++; globalThis.__seen = String(__game.History.GetValue(0)); }};
                const addFn = __game['add_{eventName}'];
                if (typeof addFn !== 'function') throw new TypeError('no add_{eventName}');
                addFn(__handler);
            ");
        }

        int Fired => int.Parse(_ctx.Eval("String(globalThis.__fired)"));

        [UnityTest]
        public IEnumerator FieldLikeEvent_ReachesTheJsHandler() {
            Subscribe("OnScoreChanged");
            EventAccessorGame.Instance.AddScore(3);
            Assert.AreEqual(1, Fired);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CustomAccessorEvent_ReachesTheJsHandler() {
            Subscribe("OnHistoryChanged");
            Assert.AreEqual(1, EventAccessorGame.Instance.HistorySubscribers, "the add accessor never ran");
            EventAccessorGame.Instance.Play("X");
            Assert.AreEqual(1, Fired, "the state's subscriber did not reach JS");
            Assert.AreEqual("X", _ctx.Eval("globalThis.__seen"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator CustomAccessorEvent_RemoveTakesBackTheSameDelegate() {
            Subscribe("OnHistoryChanged");
            _ctx.Eval("__game['remove_OnHistoryChanged'](__handler)");
            Assert.AreEqual(0, EventAccessorGame.Instance.HistorySubscribers,
                "remove_ handed the state a different delegate than add_ did, so it stayed subscribed");
            EventAccessorGame.Instance.Play("O");
            Assert.AreEqual(0, Fired);
            yield return null;
        }
    }
}
