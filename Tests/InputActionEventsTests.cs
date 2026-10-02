// InputBridge lives in the OneJS.Runtime.InputSystem child assembly, which
// only compiles when the Input System package is installed and active; these
// tests follow it, under the same two conditions.
#if ENABLE_INPUT_SYSTEM && ONEJS_INPUT_SYSTEM_PACKAGE
using NUnit.Framework;
using OneJS.Input;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OneJS.Tests {
    /// <summary>
    /// `action.on("performed", cb)` in onejs-unity needs C# to report an
    /// action's phases as they happen. A watched action queues each phase as
    /// "handle,code" (0 started, 1 performed, 2 canceled) for one drain a frame.
    /// </summary>
    [TestFixture]
    public class InputActionEventsTests {
        Keyboard _keyboard;
        int _map;
        int _jump;
        InputSettings.BackgroundBehavior _background;
        InputSettings.EditorInputBehaviorInPlayMode _editorInput;

        [SetUp]
        public void SetUp() {
            // A batch-mode editor never has focus, and an unfocused one drops
            // keyboard input by default
            _background = InputSystem.settings.backgroundBehavior;
            _editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _map = InputBridge.CreateActionMap("ActionEventsTest");
            _jump = InputBridge.AddButtonAction(_map, "Jump");
            InputBridge.AddBinding(_jump, "<Keyboard>/space");
            InputBridge.EnableDynamicMap(_map);
            InputBridge.DrainActionEvents();
        }

        [TearDown]
        public void TearDown() {
            InputBridge.UnwatchActionEvents(_jump);
            InputBridge.DisposeDynamicMap(_map);
            InputSystem.RemoveDevice(_keyboard);
            InputSystem.settings.backgroundBehavior = _background;
            InputSystem.settings.editorInputBehaviorInPlayMode = _editorInput;
        }

        void Press() {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Space));
            InputSystem.Update();
            Assert.IsTrue(InputBridge.GetActionPressed(_jump), "the simulated press never reached the action");
        }

        void Release() {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            InputSystem.Update();
        }

        [Test]
        public void WatchedAction_QueuesEachPhaseOnce() {
            InputBridge.WatchActionEvents(_jump);
            InputBridge.WatchActionEvents(_jump);
            Press();
            Release();
            Assert.AreEqual($"{_jump},0;{_jump},1;{_jump},2", InputBridge.DrainActionEvents());
            Assert.AreEqual("", InputBridge.DrainActionEvents(), "a drain empties the queue");
        }

        [Test]
        public void UnwatchedAction_QueuesNothing() {
            Press();
            Release();
            Assert.AreEqual("", InputBridge.DrainActionEvents());

            InputBridge.WatchActionEvents(_jump);
            InputBridge.UnwatchActionEvents(_jump);
            Press();
            Release();
            Assert.AreEqual("", InputBridge.DrainActionEvents());
        }
    }
}
#endif
