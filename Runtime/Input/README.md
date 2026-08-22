# Input

`InputBridge` exposes Unity's Input System to JavaScript. Every method is static
and reached from JS by name through the CS proxy, so nothing in C# calls most of
it and the linker cannot see any of it being used.

## Why this folder is its own assembly

`OneJS.Runtime.InputSystem.asmdef` carries
`"defineConstraints": ["ENABLE_INPUT_SYSTEM"]`, so it compiles only in projects
where the Input System is an active input handler. Everywhere else the assembly
is simply not built and `InputBridge` does not exist.

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

`JSRunner` still references the Input System, guarded by `ENABLE_INPUT_SYSTEM`,
to attach an `InputSystemUIInputModule` for keyboard and gamepad navigation. The
asmdef reference has to stay for that; Unity tolerates it going unresolved when
the package is absent, which is what lets a project drop it entirely.
