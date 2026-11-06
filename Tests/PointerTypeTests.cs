// InputBridge lives in the OneJS.Runtime.InputSystem child assembly, which
// only compiles when the Input System package is installed and active; these
// tests follow it, under the same two conditions.
#if ENABLE_INPUT_SYSTEM && ONEJS_INPUT_SYSTEM_PACKAGE
using System.Collections;
using NUnit.Framework;
using OneJS.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace OneJS.Tests {
    /// <summary>
    /// GetPointerType, onejs-unity's input.pointerType: the kind of pointer the last press came
    /// from. A platform that makes a mouse of a touch or a pen (Windows, Unity's own mouse from
    /// touch) presses that mouse too, and a mouse press just after a finger's is that press, so a
    /// tap never reads as a click.
    /// </summary>
    [TestFixture]
    public class PointerTypeTests {
        Mouse _mouse;
        Touchscreen _touchscreen;
        Pen _pen;
        InputSettings.BackgroundBehavior _background;
        InputSettings.EditorInputBehaviorInPlayMode _editorInput;

        [SetUp]
        public void SetUp() {
            _background = InputSystem.settings.backgroundBehavior;
            _editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _mouse = InputSystem.AddDevice<Mouse>();
            _touchscreen = InputSystem.AddDevice<Touchscreen>();
            _pen = InputSystem.AddDevice<Pen>();
            _mouse.MakeCurrent();
            _touchscreen.MakeCurrent();
            _pen.MakeCurrent();
            InputBridge.GetPointerType();
        }

        [TearDown]
        public void TearDown() {
            InputSystem.RemoveDevice(_mouse);
            InputSystem.RemoveDevice(_touchscreen);
            InputSystem.RemoveDevice(_pen);
            InputSystem.settings.backgroundBehavior = _background;
            InputSystem.settings.editorInputBehaviorInPlayMode = _editorInput;
        }

        void Click() {
            InputSystem.QueueStateEvent(_mouse, new MouseState().WithButton(MouseButton.Left));
            InputSystem.Update();
            InputSystem.QueueStateEvent(_mouse, new MouseState());
            InputSystem.Update();
        }

        void Tap() {
            InputSystem.QueueStateEvent(_touchscreen, new TouchState { touchId = 1, phase = TouchPhase.Began, position = new Vector2(10, 10) });
            InputSystem.Update();
            InputSystem.QueueStateEvent(_touchscreen, new TouchState { touchId = 1, phase = TouchPhase.Ended, position = new Vector2(10, 10) });
            InputSystem.Update();
        }

        void Stroke() {
            InputSystem.QueueStateEvent(_pen, new PenState().WithButton(PenButton.Tip));
            InputSystem.Update();
            InputSystem.QueueStateEvent(_pen, new PenState());
            InputSystem.Update();
        }

        [UnityTest]
        public IEnumerator EachPress_SaysWhichPointerItCameFrom() {
            yield return new WaitForSecondsRealtime(0.25f);
            Click();
            Assert.That(InputBridge.GetPointerType(), Is.EqualTo("mouse"));
            Tap();
            Assert.That(InputBridge.GetPointerType(), Is.EqualTo("touch"));
            Stroke();
            Assert.That(InputBridge.GetPointerType(), Is.EqualTo("pen"));
            yield return new WaitForSecondsRealtime(0.25f);
            Click();
            Assert.That(InputBridge.GetPointerType(), Is.EqualTo("mouse"));
            Assert.That(InputBridge.PointerTypes, Is.True);
        }

        [UnityTest]
        public IEnumerator TheMouseAPlatformMakesOfATouch_IsTheTouch() {
            Tap();
            // The same press, as Windows reports it: the mouse goes down with the finger.
            InputSystem.QueueStateEvent(_touchscreen, new TouchState { touchId = 2, phase = TouchPhase.Began, position = new Vector2(20, 20) });
            InputSystem.QueueStateEvent(_mouse, new MouseState().WithButton(MouseButton.Left));
            InputSystem.Update();
            Assert.That(InputBridge.GetPointerType(), Is.EqualTo("touch"), "in the same update");
            InputSystem.QueueStateEvent(_touchscreen, new TouchState { touchId = 2, phase = TouchPhase.Ended, position = new Vector2(20, 20) });
            InputSystem.QueueStateEvent(_mouse, new MouseState());
            InputSystem.Update();
            Click();
            Assert.That(InputBridge.GetPointerType(), Is.EqualTo("touch"), "an update later");
            yield return new WaitForSecondsRealtime(0.25f);
            Click();
            Assert.That(InputBridge.GetPointerType(), Is.EqualTo("mouse"), "a real click, once the finger is long gone");
        }

        [UnityTest]
        public IEnumerator MovingTheMouse_IsNoPress() {
            Tap();
            yield return new WaitForSecondsRealtime(0.25f);
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = new Vector2(300, 200) });
            InputSystem.Update();
            Assert.That(InputBridge.GetPointerType(), Is.EqualTo("touch"));
        }
    }
}
#endif
