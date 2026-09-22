# Input

`InputBridge` exposes Unity's Input System to JavaScript. Every method is static
and reached from JS by name through the CS proxy, so nothing in C# calls most of
it and the linker cannot see any of it being used.

OneJS does not install the Input System package. A project that wants
`InputBridge` installs `com.unity.inputsystem` itself; Unity's 3D templates
already include it.

## Why this folder is its own assembly

`OneJS.Runtime.InputSystem.asmdef` compiles only when two things hold, and both
are needed:

- `ENABLE_INPUT_SYSTEM`: Player Settings select the Input System as an active
  input handler.
- `ONEJS_INPUT_SYSTEM_PACKAGE`: the package is installed. The asmdef defines it
  through `versionDefines`, because `ENABLE_INPUT_SYSTEM` comes from the Player
  Settings and not from the package, so a project can select the Input System
  with the package removed. The first condition alone does not compile there.

Everywhere else the assembly is simply not built and `InputBridge` does not
exist.

That is deliberate. `InputBridge` uses Input System types in its method
*signatures*, not only in its bodies: `RegisterActionAsset(InputActionAsset)`
cannot be stubbed without the package. The alternative was a second copy of a
64-method surface guarded by `#if`, which would have to be kept in step by hand
forever, and would still be unable to mirror those methods.

Without the package a game reads input through `setInputBackend` instead. That
seam already exists for the OneJS Play container, which feeds input from browser
events and never touches this class.

## What stays behind

`PointerEvents` lives in `Runtime/`, not here. `QuickJSUIBridge` reads it on
every pointer move, and it has nothing to do with the Input System: it is a
switch for how much this bridge talks to JS. `InputBridge` forwards to it so the
JS API is unchanged.

## What this does not change

UI Toolkit pointer events do not come from this class, or from the Input System.
Clicks, focus and hover reach a runtime panel through Unity's own input path.
Verified both ways: with the package absent, clicking Wordle's on-screen keyboard
still puts a letter on the board.

## Keyboard and gamepad navigation

`JSRunner` creates an EventSystem at play start so a runtime panel receives
navigation events. `OneJS.Runtime` does not reference the Input System, so the
`InputSystemUIInputModule` for that EventSystem is added from here:
`InputSystemUIModule` sets `JSRunner.AddInputSystemModule` at
`SubsystemRegistration`. Where this assembly is not built, `JSRunner` adds the
legacy `StandaloneInputModule` instead. When Player Settings select only the
Input System and the package is absent, there is no module to add, so it creates
no EventSystem and logs a warning naming the package.

`JSRunnerEventSystemPlaymodeTests` pins which module each configuration gets.
