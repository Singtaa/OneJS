using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace OneJS.Input {
    /// <summary>
    /// Bridge class exposing Unity Input System functionality to JavaScript.
    /// All methods are static and designed to be called via the CS proxy.
    /// </summary>
    public static class InputBridge {
        // ============ Pointer Event Control ============

        /// <summary>
        /// Global flag to control PointerMoveEvent dispatching.
        /// When false, QuickJSUIBridge will not dispatch pointermove events to JavaScript.
        /// Set this from JS via: input.setPointerMoveEventsEnabled(false)
        /// This eliminates ~0.6KB/frame GC allocation when using InputReader for mouse input.
        /// </summary>
        public static bool PointerMoveEventsEnabled { get; private set; } = true;

        /// <summary>
        /// Enable or disable PointerMoveEvent dispatching to JavaScript.
        /// When disabled, React's onPointerMove handlers won't fire, but onPointerEnter/Leave still work.
        /// Use this when polling mouse input via InputReader instead of React events.
        /// </summary>
        public static void SetPointerMoveEventsEnabled(bool enabled) {
            PointerMoveEventsEnabled = enabled;
        }

        // Frame tracking for wasPressed/wasReleased
        static int _lastKeyboardFrame = -1;
        static int _lastMouseFrame = -1;
        static int _lastGamepadFrame = -1;

        // Cached button states for this-frame detection
        static readonly HashSet<Key> _keysPressed = new HashSet<Key>();
        static readonly HashSet<Key> _keysReleased = new HashSet<Key>();
        static int _mouseButtonsPressed;
        static int _mouseButtonsReleased;
        static readonly int[] _gamepadButtonsPressed = new int[8];
        static readonly int[] _gamepadButtonsReleased = new int[8];

        // Key name to Key enum mapping
        static readonly Dictionary<string, Key> _keyNameMap = new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase);

        // Gamepad button name to GamepadButton enum mapping
        static readonly Dictionary<string, GamepadButton> _gamepadButtonMap = new Dictionary<string, GamepadButton>(StringComparer.OrdinalIgnoreCase);

        static InputBridge() {
            // Populate key name map with common key names
            foreach (Key key in Enum.GetValues(typeof(Key))) {
                _keyNameMap[key.ToString()] = key;
            }

            // Add common aliases
            _keyNameMap["Space"] = Key.Space;
            _keyNameMap["Enter"] = Key.Enter;
            _keyNameMap["Return"] = Key.Enter;
            _keyNameMap["Escape"] = Key.Escape;
            _keyNameMap["Esc"] = Key.Escape;
            _keyNameMap["Tab"] = Key.Tab;
            _keyNameMap["Backspace"] = Key.Backspace;
            _keyNameMap["Delete"] = Key.Delete;
            _keyNameMap["Insert"] = Key.Insert;
            _keyNameMap["Home"] = Key.Home;
            _keyNameMap["End"] = Key.End;
            _keyNameMap["PageUp"] = Key.PageUp;
            _keyNameMap["PageDown"] = Key.PageDown;
            _keyNameMap["LeftArrow"] = Key.LeftArrow;
            _keyNameMap["RightArrow"] = Key.RightArrow;
            _keyNameMap["UpArrow"] = Key.UpArrow;
            _keyNameMap["DownArrow"] = Key.DownArrow;
            _keyNameMap["Left"] = Key.LeftArrow;
            _keyNameMap["Right"] = Key.RightArrow;
            _keyNameMap["Up"] = Key.UpArrow;
            _keyNameMap["Down"] = Key.DownArrow;

            // Shift/Ctrl/Alt variants
            _keyNameMap["Shift"] = Key.LeftShift;
            _keyNameMap["LeftShift"] = Key.LeftShift;
            _keyNameMap["RightShift"] = Key.RightShift;
            _keyNameMap["Ctrl"] = Key.LeftCtrl;
            _keyNameMap["Control"] = Key.LeftCtrl;
            _keyNameMap["LeftCtrl"] = Key.LeftCtrl;
            _keyNameMap["RightCtrl"] = Key.RightCtrl;
            _keyNameMap["Alt"] = Key.LeftAlt;
            _keyNameMap["LeftAlt"] = Key.LeftAlt;
            _keyNameMap["RightAlt"] = Key.RightAlt;
            _keyNameMap["Meta"] = Key.LeftMeta;
            _keyNameMap["LeftMeta"] = Key.LeftMeta;
            _keyNameMap["RightMeta"] = Key.RightMeta;
            _keyNameMap["Command"] = Key.LeftMeta;
            _keyNameMap["Windows"] = Key.LeftMeta;

            // Letter keys (A-Z)
            for (char c = 'A'; c <= 'Z'; c++) {
                _keyNameMap[c.ToString()] = (Key)Enum.Parse(typeof(Key), c.ToString());
            }

            // Number keys
            for (int i = 0; i <= 9; i++) {
                _keyNameMap[i.ToString()] = (Key)Enum.Parse(typeof(Key), $"Digit{i}");
                _keyNameMap[$"Digit{i}"] = (Key)Enum.Parse(typeof(Key), $"Digit{i}");
            }

            // Function keys
            for (int i = 1; i <= 12; i++) {
                _keyNameMap[$"F{i}"] = (Key)Enum.Parse(typeof(Key), $"F{i}");
            }

            // Gamepad button mapping
            foreach (GamepadButton btn in Enum.GetValues(typeof(GamepadButton))) {
                _gamepadButtonMap[btn.ToString()] = btn;
            }

            // Common aliases for gamepad buttons
            _gamepadButtonMap["A"] = GamepadButton.South;
            _gamepadButtonMap["B"] = GamepadButton.East;
            _gamepadButtonMap["X"] = GamepadButton.West;
            _gamepadButtonMap["Y"] = GamepadButton.North;
            _gamepadButtonMap["Cross"] = GamepadButton.South;
            _gamepadButtonMap["Circle"] = GamepadButton.East;
            _gamepadButtonMap["Square"] = GamepadButton.West;
            _gamepadButtonMap["Triangle"] = GamepadButton.North;
            _gamepadButtonMap["LB"] = GamepadButton.LeftShoulder;
            _gamepadButtonMap["RB"] = GamepadButton.RightShoulder;
            _gamepadButtonMap["LT"] = GamepadButton.LeftTrigger;
            _gamepadButtonMap["RT"] = GamepadButton.RightTrigger;
            _gamepadButtonMap["L3"] = GamepadButton.LeftStick;
            _gamepadButtonMap["R3"] = GamepadButton.RightStick;
            _gamepadButtonMap["Start"] = GamepadButton.Start;
            _gamepadButtonMap["Select"] = GamepadButton.Select;
            _gamepadButtonMap["Back"] = GamepadButton.Select;
            _gamepadButtonMap["Menu"] = GamepadButton.Start;
        }

        static Key ParseKey(string keyName) {
            if (_keyNameMap.TryGetValue(keyName, out var key)) {
                return key;
            }
            Debug.LogWarning($"[InputBridge] Unknown key name: {keyName}");
            return Key.None;
        }

        static GamepadButton ParseGamepadButton(string buttonName) {
            if (_gamepadButtonMap.TryGetValue(buttonName, out var button)) {
                return button;
            }
            Debug.LogWarning($"[InputBridge] Unknown gamepad button: {buttonName}");
            return GamepadButton.South;
        }

        // ============ Keyboard ============

        static void UpdateKeyboardFrame() {
            int frame = Time.frameCount;
            if (_lastKeyboardFrame == frame) return;
            _lastKeyboardFrame = frame;

            _keysPressed.Clear();
            _keysReleased.Clear();

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // Check all keys for press/release this frame
            foreach (var key in keyboard.allKeys) {
                if (key.wasPressedThisFrame) {
                    _keysPressed.Add(key.keyCode);
                }
                if (key.wasReleasedThisFrame) {
                    _keysReleased.Add(key.keyCode);
                }
            }
        }

        /// <summary>
        /// Check if a key is currently held down.
        /// </summary>
        public static bool GetKeyDown(string keyName) {
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;

            var key = ParseKey(keyName);
            if (key == Key.None) return false;

            return keyboard[key].isPressed;
        }

        /// <summary>
        /// Check if a key was pressed this frame.
        /// </summary>
        public static bool GetKeyPressed(string keyName) {
            UpdateKeyboardFrame();
            var key = ParseKey(keyName);
            return _keysPressed.Contains(key);
        }

        /// <summary>
        /// Check if a key was released this frame.
        /// </summary>
        public static bool GetKeyReleased(string keyName) {
            UpdateKeyboardFrame();
            var key = ParseKey(keyName);
            return _keysReleased.Contains(key);
        }

        // ============ Key ID Methods (Zero-Alloc) ============

        /// <summary>
        /// Get the integer ID for a key name. Cache this at init time for zero-alloc polling.
        /// Returns 0 (Key.None) if the key name is not recognized.
        /// </summary>
        public static int GetKeyId(string keyName) {
            return (int)ParseKey(keyName);
        }

        /// <summary>
        /// Check if a key is currently held down using its integer ID.
        /// Use GetKeyId() once at init to get the ID, then use this in hot paths.
        /// </summary>
        public static bool GetKeyDownById(int keyId) {
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;
            if (keyId == 0) return false; // Key.None

            return keyboard[(Key)keyId].isPressed;
        }

        /// <summary>
        /// Check if a key was pressed this frame using its integer ID.
        /// </summary>
        public static bool GetKeyPressedById(int keyId) {
            UpdateKeyboardFrame();
            return _keysPressed.Contains((Key)keyId);
        }

        /// <summary>
        /// Check if a key was released this frame using its integer ID.
        /// </summary>
        public static bool GetKeyReleasedById(int keyId) {
            UpdateKeyboardFrame();
            return _keysReleased.Contains((Key)keyId);
        }

        /// <summary>
        /// Get modifier key states as bit flags.
        /// Bit 0: Shift, Bit 1: Ctrl, Bit 2: Alt, Bit 3: Meta
        /// </summary>
        public static int GetModifiers() {
            var keyboard = Keyboard.current;
            if (keyboard == null) return 0;

            int mods = 0;
            if (keyboard.shiftKey.isPressed) mods |= 1;
            if (keyboard.ctrlKey.isPressed) mods |= 2;
            if (keyboard.altKey.isPressed) mods |= 4;
            if (keyboard.leftMetaKey.isPressed || keyboard.rightMetaKey.isPressed) mods |= 8;
            return mods;
        }

        /// <summary>
        /// Check if any key is currently pressed.
        /// </summary>
        public static bool GetAnyKeyDown() {
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.anyKey.isPressed;
        }

        /// <summary>
        /// Check if any key was pressed this frame.
        /// </summary>
        public static bool GetAnyKeyPressed() {
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.anyKey.wasPressedThisFrame;
        }

        // ============ Mouse ============

        static void UpdateMouseFrame() {
            int frame = Time.frameCount;
            if (_lastMouseFrame == frame) return;
            _lastMouseFrame = frame;

            _mouseButtonsPressed = 0;
            _mouseButtonsReleased = 0;

            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame) _mouseButtonsPressed |= 1;
            if (mouse.rightButton.wasPressedThisFrame) _mouseButtonsPressed |= 2;
            if (mouse.middleButton.wasPressedThisFrame) _mouseButtonsPressed |= 4;
            if (mouse.forwardButton.wasPressedThisFrame) _mouseButtonsPressed |= 8;
            if (mouse.backButton.wasPressedThisFrame) _mouseButtonsPressed |= 16;

            if (mouse.leftButton.wasReleasedThisFrame) _mouseButtonsReleased |= 1;
            if (mouse.rightButton.wasReleasedThisFrame) _mouseButtonsReleased |= 2;
            if (mouse.middleButton.wasReleasedThisFrame) _mouseButtonsReleased |= 4;
            if (mouse.forwardButton.wasReleasedThisFrame) _mouseButtonsReleased |= 8;
            if (mouse.backButton.wasReleasedThisFrame) _mouseButtonsReleased |= 16;
        }

        /// <summary>
        /// Get mouse position in screen coordinates.
        /// </summary>
        public static float GetMousePositionX() {
            var mouse = Mouse.current;
            return mouse?.position.x.ReadValue() ?? 0;
        }

        public static float GetMousePositionY() {
            var mouse = Mouse.current;
            return mouse?.position.y.ReadValue() ?? 0;
        }

        /// <summary>
        /// Get mouse delta (movement since last frame).
        /// </summary>
        public static float GetMouseDeltaX() {
            var mouse = Mouse.current;
            return mouse?.delta.x.ReadValue() ?? 0;
        }

        public static float GetMouseDeltaY() {
            var mouse = Mouse.current;
            return mouse?.delta.y.ReadValue() ?? 0;
        }

        /// <summary>
        /// Get scroll wheel delta.
        /// </summary>
        public static float GetScrollX() {
            var mouse = Mouse.current;
            return mouse?.scroll.x.ReadValue() ?? 0;
        }

        public static float GetScrollY() {
            var mouse = Mouse.current;
            return mouse?.scroll.y.ReadValue() ?? 0;
        }

        /// <summary>
        /// Get current mouse button states as bit flags.
        /// Bit 0: Left, Bit 1: Right, Bit 2: Middle, Bit 3: Forward, Bit 4: Back
        /// </summary>
        public static int GetMouseButtons() {
            var mouse = Mouse.current;
            if (mouse == null) return 0;

            int buttons = 0;
            if (mouse.leftButton.isPressed) buttons |= 1;
            if (mouse.rightButton.isPressed) buttons |= 2;
            if (mouse.middleButton.isPressed) buttons |= 4;
            if (mouse.forwardButton.isPressed) buttons |= 8;
            if (mouse.backButton.isPressed) buttons |= 16;
            return buttons;
        }

        /// <summary>
        /// Get mouse buttons pressed this frame as bit flags.
        /// </summary>
        public static int GetMouseButtonsPressed() {
            UpdateMouseFrame();
            return _mouseButtonsPressed;
        }

        /// <summary>
        /// Get mouse buttons released this frame as bit flags.
        /// </summary>
        public static int GetMouseButtonsReleased() {
            UpdateMouseFrame();
            return _mouseButtonsReleased;
        }

        // ============ Gamepad ============

        static void UpdateGamepadFrame() {
            int frame = Time.frameCount;
            if (_lastGamepadFrame == frame) return;
            _lastGamepadFrame = frame;

            Array.Clear(_gamepadButtonsPressed, 0, _gamepadButtonsPressed.Length);
            Array.Clear(_gamepadButtonsReleased, 0, _gamepadButtonsReleased.Length);

            var gamepads = Gamepad.all;
            for (int i = 0; i < Math.Min(gamepads.Count, 8); i++) {
                var gp = gamepads[i];
                int pressed = 0;
                int released = 0;

                if (gp.buttonSouth.wasPressedThisFrame) pressed |= 1;
                if (gp.buttonEast.wasPressedThisFrame) pressed |= 2;
                if (gp.buttonWest.wasPressedThisFrame) pressed |= 4;
                if (gp.buttonNorth.wasPressedThisFrame) pressed |= 8;
                if (gp.leftShoulder.wasPressedThisFrame) pressed |= 16;
                if (gp.rightShoulder.wasPressedThisFrame) pressed |= 32;
                if (gp.leftStickButton.wasPressedThisFrame) pressed |= 64;
                if (gp.rightStickButton.wasPressedThisFrame) pressed |= 128;
                if (gp.startButton.wasPressedThisFrame) pressed |= 256;
                if (gp.selectButton.wasPressedThisFrame) pressed |= 512;
                if (gp.dpad.up.wasPressedThisFrame) pressed |= 1024;
                if (gp.dpad.down.wasPressedThisFrame) pressed |= 2048;
                if (gp.dpad.left.wasPressedThisFrame) pressed |= 4096;
                if (gp.dpad.right.wasPressedThisFrame) pressed |= 8192;

                if (gp.buttonSouth.wasReleasedThisFrame) released |= 1;
                if (gp.buttonEast.wasReleasedThisFrame) released |= 2;
                if (gp.buttonWest.wasReleasedThisFrame) released |= 4;
                if (gp.buttonNorth.wasReleasedThisFrame) released |= 8;
                if (gp.leftShoulder.wasReleasedThisFrame) released |= 16;
                if (gp.rightShoulder.wasReleasedThisFrame) released |= 32;
                if (gp.leftStickButton.wasReleasedThisFrame) released |= 64;
                if (gp.rightStickButton.wasReleasedThisFrame) released |= 128;
                if (gp.startButton.wasReleasedThisFrame) released |= 256;
                if (gp.selectButton.wasReleasedThisFrame) released |= 512;
                if (gp.dpad.up.wasReleasedThisFrame) released |= 1024;
                if (gp.dpad.down.wasReleasedThisFrame) released |= 2048;
                if (gp.dpad.left.wasReleasedThisFrame) released |= 4096;
                if (gp.dpad.right.wasReleasedThisFrame) released |= 8192;

                _gamepadButtonsPressed[i] = pressed;
                _gamepadButtonsReleased[i] = released;
            }
        }

        /// <summary>
        /// Get number of connected gamepads.
        /// </summary>
        public static int GetGamepadCount() {
            return Gamepad.all.Count;
        }

        /// <summary>
        /// Check if a gamepad is connected at index.
        /// </summary>
        public static bool IsGamepadConnected(int index) {
            return index >= 0 && index < Gamepad.all.Count;
        }

        /// <summary>
        /// Get left stick X value (-1 to 1).
        /// </summary>
        public static float GetLeftStickX(int index) {
            if (index < 0 || index >= Gamepad.all.Count) return 0;
            return Gamepad.all[index].leftStick.x.ReadValue();
        }

        public static float GetLeftStickY(int index) {
            if (index < 0 || index >= Gamepad.all.Count) return 0;
            return Gamepad.all[index].leftStick.y.ReadValue();
        }

        public static float GetRightStickX(int index) {
            if (index < 0 || index >= Gamepad.all.Count) return 0;
            return Gamepad.all[index].rightStick.x.ReadValue();
        }

        public static float GetRightStickY(int index) {
            if (index < 0 || index >= Gamepad.all.Count) return 0;
            return Gamepad.all[index].rightStick.y.ReadValue();
        }

        /// <summary>
        /// Get trigger values (0 to 1).
        /// </summary>
        public static float GetLeftTrigger(int index) {
            if (index < 0 || index >= Gamepad.all.Count) return 0;
            return Gamepad.all[index].leftTrigger.ReadValue();
        }

        public static float GetRightTrigger(int index) {
            if (index < 0 || index >= Gamepad.all.Count) return 0;
            return Gamepad.all[index].rightTrigger.ReadValue();
        }

        /// <summary>
        /// Get current gamepad button states as bit flags.
        /// </summary>
        public static int GetGamepadButtons(int index) {
            if (index < 0 || index >= Gamepad.all.Count) return 0;
            var gp = Gamepad.all[index];

            int buttons = 0;
            if (gp.buttonSouth.isPressed) buttons |= 1;
            if (gp.buttonEast.isPressed) buttons |= 2;
            if (gp.buttonWest.isPressed) buttons |= 4;
            if (gp.buttonNorth.isPressed) buttons |= 8;
            if (gp.leftShoulder.isPressed) buttons |= 16;
            if (gp.rightShoulder.isPressed) buttons |= 32;
            if (gp.leftStickButton.isPressed) buttons |= 64;
            if (gp.rightStickButton.isPressed) buttons |= 128;
            if (gp.startButton.isPressed) buttons |= 256;
            if (gp.selectButton.isPressed) buttons |= 512;
            if (gp.dpad.up.isPressed) buttons |= 1024;
            if (gp.dpad.down.isPressed) buttons |= 2048;
            if (gp.dpad.left.isPressed) buttons |= 4096;
            if (gp.dpad.right.isPressed) buttons |= 8192;
            return buttons;
        }

        /// <summary>
        /// Get gamepad buttons pressed this frame.
        /// </summary>
        public static int GetGamepadButtonsPressed(int index) {
            UpdateGamepadFrame();
            if (index < 0 || index >= 8) return 0;
            return _gamepadButtonsPressed[index];
        }

        /// <summary>
        /// Get gamepad buttons released this frame.
        /// </summary>
        public static int GetGamepadButtonsReleased(int index) {
            UpdateGamepadFrame();
            if (index < 0 || index >= 8) return 0;
            return _gamepadButtonsReleased[index];
        }

        /// <summary>
        /// Check if a specific gamepad button is pressed by name.
        /// </summary>
        public static bool GetGamepadButtonDown(int index, string buttonName) {
            if (index < 0 || index >= Gamepad.all.Count) return false;
            var gp = Gamepad.all[index];
            var button = ParseGamepadButton(buttonName);

            return GetGamepadButtonDownByIdInternal(gp, button);
        }

        // ============ Gamepad Button ID Methods (Zero-Alloc) ============

        /// <summary>
        /// Get the integer ID for a gamepad button name. Cache this at init time for zero-alloc polling.
        /// </summary>
        public static int GetGamepadButtonId(string buttonName) {
            return (int)ParseGamepadButton(buttonName);
        }

        /// <summary>
        /// Check if a specific gamepad button is pressed by ID.
        /// Use GetGamepadButtonId() once at init to get the ID, then use this in hot paths.
        /// </summary>
        public static bool GetGamepadButtonDownById(int index, int buttonId) {
            if (index < 0 || index >= Gamepad.all.Count) return false;
            return GetGamepadButtonDownByIdInternal(Gamepad.all[index], (GamepadButton)buttonId);
        }

        static bool GetGamepadButtonDownByIdInternal(Gamepad gp, GamepadButton button) {
            return button switch {
                GamepadButton.South => gp.buttonSouth.isPressed,
                GamepadButton.East => gp.buttonEast.isPressed,
                GamepadButton.West => gp.buttonWest.isPressed,
                GamepadButton.North => gp.buttonNorth.isPressed,
                GamepadButton.LeftShoulder => gp.leftShoulder.isPressed,
                GamepadButton.RightShoulder => gp.rightShoulder.isPressed,
                GamepadButton.LeftStick => gp.leftStickButton.isPressed,
                GamepadButton.RightStick => gp.rightStickButton.isPressed,
                GamepadButton.Start => gp.startButton.isPressed,
                GamepadButton.Select => gp.selectButton.isPressed,
                GamepadButton.DpadUp => gp.dpad.up.isPressed,
                GamepadButton.DpadDown => gp.dpad.down.isPressed,
                GamepadButton.DpadLeft => gp.dpad.left.isPressed,
                GamepadButton.DpadRight => gp.dpad.right.isPressed,
                _ => false
            };
        }

        // ============ Haptics ============

        /// <summary>
        /// Set gamepad rumble.
        /// </summary>
        /// <param name="index">Gamepad index</param>
        /// <param name="lowFreq">Low frequency motor (0-1)</param>
        /// <param name="highFreq">High frequency motor (0-1)</param>
        /// <param name="duration">Duration in seconds (0 = indefinite)</param>
        public static void SetRumble(int index, float lowFreq, float highFreq, float duration) {
            if (index < 0 || index >= Gamepad.all.Count) return;
            var gp = Gamepad.all[index];

            gp.SetMotorSpeeds(lowFreq, highFreq);

            if (duration > 0) {
                // Schedule stop - requires coroutine helper
                // For now, caller should manage duration
            }
        }

        /// <summary>
        /// Stop gamepad rumble.
        /// </summary>
        public static void StopRumble(int index) {
            if (index < 0 || index >= Gamepad.all.Count) return;
            Gamepad.all[index].SetMotorSpeeds(0, 0);
        }

        /// <summary>
        /// Pause haptics on all gamepads.
        /// </summary>
        public static void PauseHaptics() {
            foreach (var gp in Gamepad.all) {
                gp.PauseHaptics();
            }
        }

        /// <summary>
        /// Resume haptics on all gamepads.
        /// </summary>
        public static void ResumeHaptics() {
            foreach (var gp in Gamepad.all) {
                gp.ResumeHaptics();
            }
        }

        // ============ Touch ============

        /// <summary>
        /// Get number of active touches.
        /// </summary>
        public static int GetTouchCount() {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null) return 0;

            int count = 0;
            foreach (var touch in touchscreen.touches) {
                if (touch.isInProgress) count++;
            }
            return count;
        }

        // Helper to get touch at index (caches active touches)
        static UnityEngine.InputSystem.Controls.TouchControl GetTouchAt(int touchIndex) {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null) return null;

            int currentIndex = 0;
            foreach (var touch in touchscreen.touches) {
                if (!touch.isInProgress) continue;
                if (currentIndex == touchIndex) return touch;
                currentIndex++;
            }
            return null;
        }

        /// <summary>
        /// Get touch finger ID at index.
        /// </summary>
        public static int GetTouchFingerId(int touchIndex) {
            var touch = GetTouchAt(touchIndex);
            return touch?.touchId.ReadValue() ?? -1;
        }

        /// <summary>
        /// Get touch position X at index.
        /// </summary>
        public static float GetTouchPositionX(int touchIndex) {
            var touch = GetTouchAt(touchIndex);
            return touch?.position.x.ReadValue() ?? 0;
        }

        /// <summary>
        /// Get touch position Y at index.
        /// </summary>
        public static float GetTouchPositionY(int touchIndex) {
            var touch = GetTouchAt(touchIndex);
            return touch?.position.y.ReadValue() ?? 0;
        }

        /// <summary>
        /// Get touch delta X at index.
        /// </summary>
        public static float GetTouchDeltaX(int touchIndex) {
            var touch = GetTouchAt(touchIndex);
            return touch?.delta.x.ReadValue() ?? 0;
        }

        /// <summary>
        /// Get touch delta Y at index.
        /// </summary>
        public static float GetTouchDeltaY(int touchIndex) {
            var touch = GetTouchAt(touchIndex);
            return touch?.delta.y.ReadValue() ?? 0;
        }

        /// <summary>
        /// Get touch phase at index (0=began, 1=moved, 2=stationary, 3=ended, 4=canceled).
        /// </summary>
        public static int GetTouchPhase(int touchIndex) {
            var touch = GetTouchAt(touchIndex);
            if (touch == null) return -1;

            var phase = touch.phase.ReadValue();
            return phase switch {
                UnityEngine.InputSystem.TouchPhase.Began => 0,
                UnityEngine.InputSystem.TouchPhase.Moved => 1,
                UnityEngine.InputSystem.TouchPhase.Stationary => 2,
                UnityEngine.InputSystem.TouchPhase.Ended => 3,
                UnityEngine.InputSystem.TouchPhase.Canceled => 4,
                _ => 0
            };
        }

        // ============ InputActions (Asset-based) ============

        static int _nextAssetHandle = 1;
        static int _nextActionHandle = 1;
        static readonly Dictionary<int, InputActionAsset> _assetHandles = new Dictionary<int, InputActionAsset>();
        static readonly Dictionary<int, InputAction> _actionHandles = new Dictionary<int, InputAction>();
        static readonly object _lock = new object();

        /// <summary>
        /// Register an InputActionAsset and return a handle.
        /// </summary>
        public static int RegisterActionAsset(InputActionAsset asset) {
            if (asset == null) {
                Debug.LogWarning("[InputBridge] Cannot register null InputActionAsset");
                return -1;
            }

            lock (_lock) {
                int handle = _nextAssetHandle++;
                _assetHandles[handle] = asset;

                // Enable all action maps by default
                asset.Enable();

                return handle;
            }
        }

        /// <summary>
        /// Dispose an InputActionAsset handle.
        /// </summary>
        public static void DisposeActionAsset(int handle) {
            lock (_lock) {
                if (_assetHandles.TryGetValue(handle, out var asset)) {
                    asset.Disable();
                    _assetHandles.Remove(handle);
                }
            }
        }

        /// <summary>
        /// Find an action by path (e.g., "Player/Jump") and return a handle.
        /// </summary>
        public static int FindAction(int assetHandle, string actionPath) {
            lock (_lock) {
                if (!_assetHandles.TryGetValue(assetHandle, out var asset)) {
                    return -1;
                }

                var action = asset.FindAction(actionPath);
                if (action == null) {
                    Debug.LogWarning($"[InputBridge] Action '{actionPath}' not found in asset");
                    return -1;
                }

                int handle = _nextActionHandle++;
                _actionHandles[handle] = action;
                return handle;
            }
        }

        /// <summary>
        /// Check if an action was triggered this frame.
        /// </summary>
        public static bool GetActionTriggered(int actionHandle) {
            lock (_lock) {
                if (!_actionHandles.TryGetValue(actionHandle, out var action)) {
                    return false;
                }
                return action.triggered;
            }
        }

        /// <summary>
        /// Check if an action is currently pressed.
        /// </summary>
        public static bool GetActionPressed(int actionHandle) {
            lock (_lock) {
                if (!_actionHandles.TryGetValue(actionHandle, out var action)) {
                    return false;
                }
                return action.IsPressed();
            }
        }

        /// <summary>
        /// Get action phase (0=Disabled, 1=Waiting, 2=Started, 3=Performed, 4=Canceled).
        /// </summary>
        public static int GetActionPhase(int actionHandle) {
            lock (_lock) {
                if (!_actionHandles.TryGetValue(actionHandle, out var action)) {
                    return 0;
                }
                return (int)action.phase;
            }
        }

        /// <summary>
        /// Read action value as float.
        /// </summary>
        public static float GetActionValueFloat(int actionHandle) {
            lock (_lock) {
                if (!_actionHandles.TryGetValue(actionHandle, out var action)) {
                    return 0;
                }
                return action.ReadValue<float>();
            }
        }

        /// <summary>
        /// Read action value as Vector2.
        /// </summary>
        public static float GetActionValueVector2X(int actionHandle) {
            lock (_lock) {
                if (!_actionHandles.TryGetValue(actionHandle, out var action)) {
                    return 0;
                }
                return action.ReadValue<Vector2>().x;
            }
        }

        public static float GetActionValueVector2Y(int actionHandle) {
            lock (_lock) {
                if (!_actionHandles.TryGetValue(actionHandle, out var action)) {
                    return 0;
                }
                return action.ReadValue<Vector2>().y;
            }
        }

        /// <summary>
        /// Enable an action map by name.
        /// </summary>
        public static void EnableActionMap(int assetHandle, string mapName) {
            lock (_lock) {
                if (!_assetHandles.TryGetValue(assetHandle, out var asset)) return;
                var map = asset.FindActionMap(mapName);
                map?.Enable();
            }
        }

        /// <summary>
        /// Disable an action map by name.
        /// </summary>
        public static void DisableActionMap(int assetHandle, string mapName) {
            lock (_lock) {
                if (!_assetHandles.TryGetValue(assetHandle, out var asset)) return;
                var map = asset.FindActionMap(mapName);
                map?.Disable();
            }
        }

        // ============ Dynamic Actions (JS-defined) ============

        static int _nextDynamicMapHandle = 1;
        static readonly Dictionary<int, InputActionMap> _dynamicMaps = new Dictionary<int, InputActionMap>();

        /// <summary>
        /// Create a new dynamic action map.
        /// </summary>
        public static int CreateActionMap(string name) {
            lock (_lock) {
                var map = new InputActionMap(name);
                int handle = _nextDynamicMapHandle++;
                _dynamicMaps[handle] = map;
                return handle;
            }
        }

        /// <summary>
        /// Add a button action to a dynamic map.
        /// </summary>
        public static int AddButtonAction(int mapHandle, string name) {
            lock (_lock) {
                if (!_dynamicMaps.TryGetValue(mapHandle, out var map)) return -1;

                var action = map.AddAction(name, InputActionType.Button);
                int handle = _nextActionHandle++;
                _actionHandles[handle] = action;
                return handle;
            }
        }

        /// <summary>
        /// Add a value (axis) action to a dynamic map.
        /// </summary>
        public static int AddValueAction(int mapHandle, string name) {
            lock (_lock) {
                if (!_dynamicMaps.TryGetValue(mapHandle, out var map)) return -1;

                var action = map.AddAction(name, InputActionType.Value);
                int handle = _nextActionHandle++;
                _actionHandles[handle] = action;
                return handle;
            }
        }

        /// <summary>
        /// Add a binding to an action.
        /// </summary>
        public static void AddBinding(int actionHandle, string path) {
            lock (_lock) {
                if (!_actionHandles.TryGetValue(actionHandle, out var action)) return;
                action.AddBinding(path);
            }
        }

        /// <summary>
        /// Enable a dynamic action map.
        /// </summary>
        public static void EnableDynamicMap(int mapHandle) {
            lock (_lock) {
                if (_dynamicMaps.TryGetValue(mapHandle, out var map)) {
                    map.Enable();
                }
            }
        }

        /// <summary>
        /// Disable a dynamic action map.
        /// </summary>
        public static void DisableDynamicMap(int mapHandle) {
            lock (_lock) {
                if (_dynamicMaps.TryGetValue(mapHandle, out var map)) {
                    map.Disable();
                }
            }
        }

        /// <summary>
        /// Dispose a dynamic action map.
        /// </summary>
        public static void DisposeDynamicMap(int mapHandle) {
            lock (_lock) {
                if (_dynamicMaps.TryGetValue(mapHandle, out var map)) {
                    map.Disable();
                    map.Dispose();
                    _dynamicMaps.Remove(mapHandle);
                }
            }
        }

        // ============ Zero-Alloc Bindings ============

        static bool _bindingsRegistered = false;
        static ZeroAllocInputBindings _bindingIds;

        static void InitializeZeroAllocBindings() {
            if (_bindingsRegistered) return;
            _bindingsRegistered = true;

            // Keyboard - string-based (for compatibility)
            _bindingIds.getKeyDown = QuickJSNative.Bind<string, bool>(GetKeyDown);
            _bindingIds.getKeyPressed = QuickJSNative.Bind<string, bool>(GetKeyPressed);
            _bindingIds.getKeyReleased = QuickJSNative.Bind<string, bool>(GetKeyReleased);

            // Keyboard - ID-based (zero-alloc hot path)
            _bindingIds.getKeyId = QuickJSNative.Bind<string, int>(GetKeyId);
            _bindingIds.getKeyDownById = QuickJSNative.Bind<int, bool>(GetKeyDownById);
            _bindingIds.getKeyPressedById = QuickJSNative.Bind<int, bool>(GetKeyPressedById);
            _bindingIds.getKeyReleasedById = QuickJSNative.Bind<int, bool>(GetKeyReleasedById);

            // Mouse (0 args -> float/int)
            _bindingIds.getMouseButtons = QuickJSNative.Bind(GetMouseButtons);
            _bindingIds.getMousePositionX = QuickJSNative.Bind(GetMousePositionX);
            _bindingIds.getMousePositionY = QuickJSNative.Bind(GetMousePositionY);
            _bindingIds.getMouseDeltaX = QuickJSNative.Bind(GetMouseDeltaX);
            _bindingIds.getMouseDeltaY = QuickJSNative.Bind(GetMouseDeltaY);
            _bindingIds.getScrollX = QuickJSNative.Bind(GetScrollX);
            _bindingIds.getScrollY = QuickJSNative.Bind(GetScrollY);

            // Gamepad - string-based (for compatibility)
            _bindingIds.getGamepadButtonDown = QuickJSNative.Bind<int, string, bool>(GetGamepadButtonDown);

            // Gamepad - ID-based (zero-alloc hot path)
            _bindingIds.getGamepadButtonId = QuickJSNative.Bind<string, int>(GetGamepadButtonId);
            _bindingIds.getGamepadButtonDownById = QuickJSNative.Bind<int, int, bool>(GetGamepadButtonDownById);

            // Gamepad sticks/triggers (already zero-alloc - no strings)
            _bindingIds.getLeftStickX = QuickJSNative.Bind<int, float>(GetLeftStickX);
            _bindingIds.getLeftStickY = QuickJSNative.Bind<int, float>(GetLeftStickY);
            _bindingIds.getRightStickX = QuickJSNative.Bind<int, float>(GetRightStickX);
            _bindingIds.getRightStickY = QuickJSNative.Bind<int, float>(GetRightStickY);
            _bindingIds.getLeftTrigger = QuickJSNative.Bind<int, float>(GetLeftTrigger);
            _bindingIds.getRightTrigger = QuickJSNative.Bind<int, float>(GetRightTrigger);
        }

        /// <summary>
        /// Get binding IDs for zero-allocation input polling.
        /// These IDs can be used with __zaInvokeN functions to avoid
        /// type resolution overhead on every call.
        /// </summary>
        public static ZeroAllocInputBindings GetZeroAllocBindingIds() {
            if (!_bindingsRegistered) {
                InitializeZeroAllocBindings();
            }
            return _bindingIds;
        }

        public struct ZeroAllocInputBindings {
            // Keyboard - string-based (for compatibility)
            public int getKeyDown;
            public int getKeyPressed;
            public int getKeyReleased;

            // Keyboard - ID-based (zero-alloc hot path)
            public int getKeyId;
            public int getKeyDownById;
            public int getKeyPressedById;
            public int getKeyReleasedById;

            // Mouse
            public int getMouseButtons;
            public int getMousePositionX;
            public int getMousePositionY;
            public int getMouseDeltaX;
            public int getMouseDeltaY;
            public int getScrollX;
            public int getScrollY;

            // Gamepad - string-based (for compatibility)
            public int getGamepadButtonDown;

            // Gamepad - ID-based (zero-alloc hot path)
            public int getGamepadButtonId;
            public int getGamepadButtonDownById;

            // Gamepad sticks/triggers
            public int getLeftStickX;
            public int getLeftStickY;
            public int getRightStickX;
            public int getRightStickY;
            public int getLeftTrigger;
            public int getRightTrigger;
        }
    }
}
