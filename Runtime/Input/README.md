# Input System Bridge

Exposes Unity's Input System to JavaScript via a static bridge class, providing ergonomic APIs for keyboard, mouse, gamepad (with haptics), and touch input.

## Architecture

```
JavaScript (onejs-unity/input)
    ↓
CS.OneJS.Input.InputBridge (static methods)
    ↓
Unity Input System (Keyboard.current, Mouse.current, Gamepad.current, etc.)
```

## C# API

### InputBridge Static Methods

#### Keyboard
| Method | Returns | Description |
|--------|---------|-------------|
| `GetKeyDown(string keyName)` | `bool` | Key currently held |
| `GetKeyPressed(string keyName)` | `bool` | Key pressed this frame |
| `GetKeyReleased(string keyName)` | `bool` | Key released this frame |
| `GetModifiers()` | `int` | Bit flags: Shift=1, Ctrl=2, Alt=4, Meta=8 |
| `GetAnyKeyDown()` | `bool` | Any key held |
| `GetAnyKeyPressed()` | `bool` | Any key pressed this frame |

#### Mouse
| Method | Returns | Description |
|--------|---------|-------------|
| `GetMousePositionX/Y()` | `float` | Screen position |
| `GetMouseDeltaX/Y()` | `float` | Frame movement |
| `GetScrollX/Y()` | `float` | Scroll wheel delta |
| `GetMouseButtons()` | `int` | Bit flags: Left=1, Right=2, Middle=4, Forward=8, Back=16 |
| `GetMouseButtonsPressed()` | `int` | Buttons pressed this frame |
| `GetMouseButtonsReleased()` | `int` | Buttons released this frame |

#### Gamepad
| Method | Returns | Description |
|--------|---------|-------------|
| `GetGamepadCount()` | `int` | Connected gamepads |
| `IsGamepadConnected(int index)` | `bool` | Check connection |
| `GetLeftStickX/Y(int index)` | `float` | Left stick (-1 to 1) |
| `GetRightStickX/Y(int index)` | `float` | Right stick (-1 to 1) |
| `GetLeftTrigger(int index)` | `float` | Left trigger (0 to 1) |
| `GetRightTrigger(int index)` | `float` | Right trigger (0 to 1) |
| `GetGamepadButtons(int index)` | `int` | Button bit flags |
| `GetGamepadButtonsPressed(int index)` | `int` | Pressed this frame |
| `GetGamepadButtonsReleased(int index)` | `int` | Released this frame |

#### Haptics
| Method | Description |
|--------|-------------|
| `SetRumble(int index, float low, float high, float duration)` | Start rumble |
| `StopRumble(int index)` | Stop rumble |
| `PauseHaptics()` | Pause all haptics |
| `ResumeHaptics()` | Resume all haptics |

#### Touch
| Method | Returns | Description |
|--------|---------|-------------|
| `GetTouchCount()` | `int` | Active touches |
| `GetTouchFingerId(int index)` | `int` | Finger ID |
| `GetTouchPositionX/Y(int index)` | `float` | Touch position |
| `GetTouchDeltaX/Y(int index)` | `float` | Touch movement |
| `GetTouchPhase(int index)` | `int` | 0=Began, 1=Moved, 2=Stationary, 3=Ended, 4=Canceled |

#### InputActions
| Method | Returns | Description |
|--------|---------|-------------|
| `RegisterActionAsset(object asset)` | `int` | Register asset, returns handle |
| `DisposeActionAsset(int handle)` | `void` | Dispose asset |
| `FindAction(int handle, string path)` | `int` | Find action by path |
| `GetActionTriggered(int handle)` | `bool` | Action triggered this frame |
| `GetActionPressed(int handle)` | `bool` | Action currently pressed |
| `GetActionPhase(int handle)` | `int` | Current phase |
| `GetActionValueFloat(int handle)` | `float` | 1D value |
| `GetActionValueVector2X/Y(int handle)` | `float` | 2D value components |
| `EnableActionMap(int handle, string name)` | `void` | Enable action map |
| `DisableActionMap(int handle, string name)` | `void` | Disable action map |

#### Dynamic Actions (JS-defined)
| Method | Returns | Description |
|--------|---------|-------------|
| `CreateActionMap(string name)` | `int` | Create dynamic map |
| `AddButtonAction(int map, string name)` | `int` | Add button action |
| `AddValueAction(int map, string name)` | `int` | Add value action |
| `AddBinding(int action, string path)` | `void` | Add binding path |
| `EnableDynamicMap(int handle)` | `void` | Enable dynamic map |
| `DisableDynamicMap(int handle)` | `void` | Disable dynamic map |
| `DisposeDynamicMap(int handle)` | `void` | Dispose dynamic map |

## Button Bit Flags

### Gamepad Buttons
```
South (A/Cross)      = 1
East (B/Circle)      = 2
West (X/Square)      = 4
North (Y/Triangle)   = 8
Left Shoulder (LB)   = 16
Right Shoulder (RB)  = 32
Left Stick (L3)      = 64
Right Stick (R3)     = 128
Start               = 256
Select              = 512
D-Pad Up            = 1024
D-Pad Down          = 2048
D-Pad Left          = 4096
D-Pad Right         = 8192
```

## Key Name Mapping

The bridge accepts various key name formats:
- Standard names: `Space`, `Enter`, `Escape`, `Tab`, `Backspace`
- Arrow keys: `LeftArrow`, `Up`, `Down`, `Left`, `Right`
- Letters: `A`-`Z` (case insensitive)
- Numbers: `0`-`9`, `Digit0`-`Digit9`
- Numpad: `Numpad0`-`Numpad9`, `NumpadPlus`, `NumpadMinus`, etc.
- Function keys: `F1`-`F12`
- Modifiers: `Shift`, `Ctrl`, `Alt`, `Meta`, `Command`, `Windows`

## Frame State Tracking

The bridge tracks per-frame button/key states to provide accurate `wasPressed` and `wasReleased` detection. State is updated lazily on first access each frame using `Time.frameCount`.

## Handle Management

InputActions use integer handles for C#↔JS references:
- Asset handles: Registered InputActionAsset instances
- Action handles: Individual InputAction references
- Dynamic map handles: JS-created action maps

Handles should be disposed when no longer needed to prevent memory leaks.
