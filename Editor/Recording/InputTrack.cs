using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Editor {
    /// <summary>
    /// A scripted sequence of pointer and keyboard input for <see cref="PanelRecorder"/>.
    ///
    /// Actions are authored sequentially: every call appends at the current time and
    /// advances an internal clock, so a track reads in the order it happens.
    ///
    /// <code>
    /// var track = new InputTrack()
    ///     .MoveTo(480, 140, 0.6)
    ///     .Click()
    ///     .Wait(0.4)
    ///     .DragTo(300, 400, 0.8);
    /// </code>
    ///
    /// Input is delivered as genuine UI Toolkit events through
    /// <see cref="VisualElement.SendEvent"/>, so `:hover` and `:active` styling,
    /// focus rings, ScrollView scrolling, and every React handler behave exactly as
    /// they do for a real user. Nothing here simulates state directly.
    ///
    /// Coordinates are panel-space pixels in the recording's capture resolution
    /// (<see cref="PanelRecordingOptions.Width"/> / Height), with the origin at the
    /// top left.
    /// </summary>
    public sealed class InputTrack {
        /// <summary>Time an instantaneous action is given to settle before the next one.</summary>
        const double DefaultSettle = 0.12;

        struct MoveSegment {
            public double Start, End;
            public Vector2 From, To;
        }

        struct Action {
            public double Time;
            public Func<VisualElement, InputTrack, bool> Run; // returns new pressed state
        }

        readonly List<MoveSegment> _moves = new List<MoveSegment>();
        readonly List<Action> _actions = new List<Action>();

        double _authorTime;
        Vector2 _authorPos;
        double _now;
        double _lastPress = double.NegativeInfinity;

        /// <summary>Total length of the track in seconds.</summary>
        public double Duration { get; private set; }

        /// <summary>Pointer position as of the last <see cref="Step"/>, for a cursor overlay.</summary>
        public Vector2 PointerPosition { get; private set; }

        /// <summary>Whether the pointer is currently pressed, as of the last <see cref="Step"/>.</summary>
        public bool PointerIsDown { get; private set; }

        /// <summary>
        /// Seconds since the most recent press, for drawing click feedback. Very
        /// large before the first press. A press lasts only a few frames, so the
        /// overlay animates from this rather than from <see cref="PointerIsDown"/>,
        /// which would flash by too fast to read at normal playback speed.
        /// </summary>
        public double TimeSincePress => _now - _lastPress;

        /// <summary>Starts the pointer at a position without moving or animating there.</summary>
        public InputTrack StartAt(float x, float y) {
            _authorPos = new Vector2(x, y);
            PointerPosition = _authorPos;
            return this;
        }

        /// <summary>Advances the clock without doing anything, to let the UI settle or animate.</summary>
        public InputTrack Wait(double seconds) {
            _authorTime += Math.Max(0.0, seconds);
            Bump(_authorTime);
            return this;
        }

        /// <summary>Glides the pointer to a position over <paramref name="duration"/> seconds.</summary>
        public InputTrack MoveTo(float x, float y, double duration = 0.5) {
            duration = Math.Max(0.0, duration);
            var to = new Vector2(x, y);
            _moves.Add(new MoveSegment {
                Start = _authorTime, End = _authorTime + duration, From = _authorPos, To = to
            });
            _authorPos = to;
            _authorTime += duration;
            Bump(_authorTime);
            return this;
        }

        /// <summary>Presses and releases at the current position.</summary>
        public InputTrack Click(double settle = DefaultSettle) {
            Press();
            Wait(0.06); // brief hold, so the :active state is visible in frame
            Release(settle);
            return this;
        }

        /// <summary>Presses and holds at the current position.</summary>
        public InputTrack Press(double settle = 0.0) {
            var at = _authorTime;
            AddAction(at, (root, t) => {
                Dispatch(root, EventType.MouseDown, t.PointerPosition, Vector2.zero, 1);
                t._lastPress = at;
                return true;
            });
            return Wait(settle);
        }

        /// <summary>Releases at the current position.</summary>
        public InputTrack Release(double settle = DefaultSettle) {
            AddAction(_authorTime, (root, t) => {
                Dispatch(root, EventType.MouseUp, t.PointerPosition, Vector2.zero, 1);
                return false;
            });
            return Wait(settle);
        }

        /// <summary>Presses, glides to the target, and releases. Drives real drag handlers.</summary>
        public InputTrack DragTo(float x, float y, double duration = 0.8) {
            Press();
            Wait(0.08); // let the press register before motion starts
            MoveTo(x, y, duration);
            Release();
            return this;
        }

        /// <summary>
        /// Sends a wheel event at the current position. Positive
        /// <paramref name="deltaY"/> scrolls down, matching DOM convention.
        /// </summary>
        public InputTrack Scroll(float deltaX, float deltaY, double settle = DefaultSettle) {
            AddAction(_authorTime, (root, t) => {
                var e = new Event {
                    type = EventType.ScrollWheel,
                    mousePosition = t.PointerPosition,
                    delta = new Vector2(deltaX, deltaY),
                };
                using (var evt = WheelEvent.GetPooled(e)) root.SendEvent(evt);
                return t.PointerIsDown;
            });
            return Wait(settle);
        }

        /// <summary>
        /// Types into whatever currently has focus, one character per
        /// <paramref name="perChar"/> seconds. Click the field first to focus it.
        /// </summary>
        public InputTrack Type(string text, double perChar = 0.07) {
            if (string.IsNullOrEmpty(text)) return this;
            foreach (var ch in text) {
                var c = ch;
                AddAction(_authorTime, (root, t) => {
                    SendKey(root, c, KeyCode.None);
                    return t.PointerIsDown;
                });
                _authorTime += Math.Max(0.0, perChar);
            }
            Bump(_authorTime);
            return this;
        }

        /// <summary>
        /// Moves focus to the next focusable element, the way a Tab press does.
        ///
        /// Focus movement in UI Toolkit travels on <see cref="NavigationMoveEvent"/>,
        /// not on the Tab key, so <see cref="Key"/> with <c>KeyCode.Tab</c> leaves
        /// focus exactly where it was. Use this instead.
        /// </summary>
        public InputTrack NavigateNext(double settle = DefaultSettle) =>
            Navigate(NavigationMoveEvent.Direction.Next, settle);

        /// <summary>Moves focus to the previous focusable element (Shift+Tab).</summary>
        public InputTrack NavigatePrevious(double settle = DefaultSettle) =>
            Navigate(NavigationMoveEvent.Direction.Previous, settle);

        /// <summary>Moves focus in a direction, for arrow-key and gamepad navigation.</summary>
        public InputTrack Navigate(NavigationMoveEvent.Direction direction, double settle = DefaultSettle) {
            AddAction(_authorTime, (root, t) => {
                using (var evt = NavigationMoveEvent.GetPooled(direction, EventModifiers.None))
                    root.SendEvent(evt);
                return t.PointerIsDown;
            });
            return Wait(settle);
        }

        /// <summary>
        /// Sends a key press to the focused element, for text editing keys like
        /// Return or Backspace. Not for moving focus: see <see cref="NavigateNext"/>.
        /// </summary>
        public InputTrack Key(KeyCode key, double settle = DefaultSettle) {
            AddAction(_authorTime, (root, t) => {
                SendKey(root, '\0', key);
                return t.PointerIsDown;
            });
            return Wait(settle);
        }

        /// <summary>
        /// Delivers everything scheduled in (<paramref name="fromSeconds"/>,
        /// <paramref name="toSeconds"/>] plus the pointer motion for this frame.
        /// Called by <see cref="PanelRecorder"/> once per frame, after the clock is
        /// stepped and before the panel is rendered.
        /// </summary>
        public void Step(VisualElement root, double fromSeconds, double toSeconds) {
            if (root == null) return;
            _now = toSeconds;

            // Motion first: an action at time T must see the pointer already there.
            var target = PositionAt(toSeconds);
            if (target != PointerPosition) {
                var delta = target - PointerPosition;
                PointerPosition = target;
                Dispatch(root, EventType.MouseMove, target, delta, 0);
            }

            for (int i = 0; i < _actions.Count; i++) {
                var a = _actions[i];
                if (a.Time > fromSeconds && a.Time <= toSeconds)
                    PointerIsDown = a.Run(root, this);
            }
        }

        /// <summary>Interpolated pointer position at a given time, eased for natural motion.</summary>
        public Vector2 PositionAt(double seconds) {
            var pos = _moves.Count > 0 ? _moves[0].From : PointerPosition;
            for (int i = 0; i < _moves.Count; i++) {
                var m = _moves[i];
                if (seconds >= m.End) { pos = m.To; continue; }
                if (seconds <= m.Start) break;
                var u = (float)((seconds - m.Start) / (m.End - m.Start));
                pos = Vector2.LerpUnclamped(m.From, m.To, u * u * (3f - 2f * u)); // smoothstep
                break;
            }
            return pos;
        }

        void AddAction(double time, Func<VisualElement, InputTrack, bool> run) {
            _actions.Add(new Action { Time = time, Run = run });
            Bump(time);
        }

        void Bump(double time) {
            if (time > Duration) Duration = time;
        }

        static void Dispatch(VisualElement root, EventType type, Vector2 pos, Vector2 delta, int clickCount) {
            var e = new Event {
                type = type,
                mousePosition = pos,
                delta = delta,
                button = 0,
                clickCount = clickCount,
            };
            // The pointer dispatching strategy picks the target from the position and
            // updates the panel's element-under-pointer, which is what drives :hover.
            switch (type) {
                case EventType.MouseDown:
                    using (var evt = PointerDownEvent.GetPooled(e)) root.SendEvent(evt);
                    break;
                case EventType.MouseUp:
                    using (var evt = PointerUpEvent.GetPooled(e)) root.SendEvent(evt);
                    break;
                default:
                    using (var evt = PointerMoveEvent.GetPooled(e)) root.SendEvent(evt);
                    break;
            }
        }

        static void SendKey(VisualElement root, char character, KeyCode key) {
            // Keyboard events route to the focus controller's focused element, so a
            // field must be focused (click it) before Type() reaches it.
            using (var down = KeyDownEvent.GetPooled(character, key, EventModifiers.None))
                root.SendEvent(down);
            using (var up = KeyUpEvent.GetPooled(character, key, EventModifiers.None))
                root.SendEvent(up);
        }
    }
}
