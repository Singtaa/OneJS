// InputBridge lives in the OneJS.Runtime.InputSystem child assembly, which
// only compiles when the Input System package is installed and active; these
// tests follow it, under the same two conditions.
#if ENABLE_INPUT_SYSTEM && ONEJS_INPUT_SYSTEM_PACKAGE
using System.Collections;
using NUnit.Framework;
using OneJS.Input;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace OneJS.Tests {
    /// <summary>
    /// GetKeysPressed lists the keys that went down this frame in the order
    /// they went down. GetKeyPressed says each one went down and nothing about
    /// which came first, so letters typed inside one slow frame reached a game
    /// in whatever order it asked about them (onejs-play#4).
    /// </summary>
    [TestFixture]
    public class KeysPressedOrderTests {
        Keyboard _keyboard;
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
            InputBridge.GetKeysPressed();
        }

        [TearDown]
        public void TearDown() {
            InputSystem.RemoveDevice(_keyboard);
            InputSystem.settings.backgroundBehavior = _background;
            InputSystem.settings.editorInputBehaviorInPlayMode = _editorInput;
        }

        // Each key goes down in its own event, holding the ones before it, and
        // all of them reach the Input System in one update, so in one frame
        void TypeInOneFrame(params Key[] keys) {
            for (int i = 0; i < keys.Length; i++) {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys[..(i + 1)]));
            }
            InputSystem.Update();
        }

        void ReleaseAll() {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            InputSystem.Update();
        }

        [UnityTest]
        public IEnumerator TwoKeysInOneFrame_KeepTheOrderTheyWentDownIn() {
            yield return null;
            TypeInOneFrame(Key.R, Key.C);
            Assert.IsTrue(InputBridge.GetKeyPressed("C") && InputBridge.GetKeyPressed("R"),
                "the simulated presses never reached the keyboard");
            Assert.AreEqual("R,C", InputBridge.GetKeysPressed());
            ReleaseAll();

            yield return null;
            TypeInOneFrame(Key.C, Key.R, Key.A, Key.N, Key.E);
            Assert.AreEqual("C,R,A,N,E", InputBridge.GetKeysPressed());
            ReleaseAll();
        }

        [UnityTest]
        public IEnumerator AKeyPressedTwiceInOneFrame_IsListedTwice() {
            yield return null;
            TypeInOneFrame(Key.E);
            ReleaseAll();
            TypeInOneFrame(Key.E);
            Assert.AreEqual("E,E", InputBridge.GetKeysPressed());
            ReleaseAll();
        }

        [UnityTest]
        public IEnumerator TheNextFrame_StartsEmpty_AndAHeldKeyIsNotPressedAgain() {
            yield return null;
            TypeInOneFrame(Key.C);
            Assert.AreEqual("C", InputBridge.GetKeysPressed());

            yield return null;
            Assert.AreEqual("", InputBridge.GetKeysPressed(), "last frame's presses carried over");
            TypeInOneFrame(Key.C, Key.R);
            Assert.AreEqual("R", InputBridge.GetKeysPressed(), "C was already down, so only R went down");
            ReleaseAll();
        }
    }
}
#endif
