## [2022-06-24] v1.2.0 - WorkingDir Rework

You are now able to keep all your scripts under `{ProjectDir}/OneJS`. And the scripts will be automatically bundled into`{persistentDataPath}/OneJS` for Standalone builds.

* Added a Bundler component that is responsible for extracting scripts.
* Added OneJSBuildProcessor ScriptableObject that is responsible for packaging scripts (for Standalone builds).
  * This is generally automatic as it uses OnPreprocessBuild
  * It also provides glob ignore patterns for things you don't want to include in the bundle.
* Added `[DefaultExecutionOrder]` for various components.
* Added an extra option (`Poll Standalone Screen`) on the Tailwind component to allow you to also watch for screen changes for standalone builds.

## [2022-06-19] v1.1.2 - Bugfixes

* Fixed various preact cyclic reference errors
* Fixed preact diff bug (missing parentNode)
* Fixed Tailwind StyleScale regression in 2021.3

## [2022-06-08] v1.1.1 - Flipbook and more Tailwind support

### Newly Added:

* Flipbook Visual Element
* Negative value support for Tailwind

### Minor Bug fixes:

* Opacity bugfix
* Preact useContext bugfix
* Preact nested children bugfix

## [2022-05-26] v1.1.0 - Tailwind and Multi-Device Live Reload

### New Features:

* Tailwind
* Multi-Device Live Reload
* USS transition support in JSX

### Other Notables:

* Completely reworked Live Reload's File watching mechanism to conserve more CPU cycles. Previously it was using  FileSystemWatcher (poor performance when watching directories recursively).
* New GradientRect control (allows linear gradients with 4 corners, demo'ed in the fortnite ui sample)

### Minor Bug fixes:

* Fixed Double to Single casting error during Dom.setAttribute
* Fixed object[] casting during Dom.setAttribute
* Fixed unityTextAlign style Enum bug
* Fixed overflow style Enum bug
* Fixed a bunch of setStyleList bugs

## [2022-05-16] v1.0.0 - Initial Release

* Full Preact Integration with 1-to-1 interop with UI Toolkit
* Live Reload
* C# to TS Def converter