## [2023-09-09] v1.6.7

* Auto-reimport uss after generation (Tailwind)
* useEventfulState() forceUpdate() regression fix

## [2023-08-31] v1.6.6 - Stability and Misc Additions

* Updated Jint to [beta-2050]
* jsx-runtime support
* OnPreprocessBuild now accommodates when there's no scene listed in the build settings
* Added comps sample scene
* Added GradientTextureFill Job example
* Added onEngineDestroy
* Added webapi.getImage()
* Added OnInitOptions to ScriptEngine
* Dom.addEventListener now go through all loaded assemblies
* Fixed regression in InitAllUIElementEvents()
* Added TagTypeResolver to ScriptEngine
* JSON module import
* Added default tasks.json for vscode
* Added EventfulPropertyAttribute and corresponding source generator
* Added ValueTuple check in TSDefConverter

## [2023-07-03] v1.6.4 - Comp Library and More

* Added a new Component Library under ScriptLib/onejs/comps. These are inspired by HeadlessUI.
  * Please see the v1.6 Docs for more information.
* ScriptLib folder now will contain the .ts files. This is to make it easier to refer to the TS implementation instead of the compiled JS.

## [2023-05-30] v1.6.1 - Major Tailwind Rework

* Support for Tailwind compiler has landed!
* Documentation here: https://onejs.com/docs/v1.6/tailwind

## [2023-04-23] v1.5.9 - Quite a bit of QoL stuff, Tweaks, and Bugfixes

* Added more CLR exception handling logging
* VisualElementExts ForceUpdate()
* Preact <-> Jint Cyclic ref error fix
* Added array type mapping for TSDefConverter
* Added public properties for ScriptEngine option fields
* Added _logRedundantErrors to ScriptEngine
* BuildProcessor bugfix for Cloudbuild @mattehr
* Improved addEventListener and removeEventListener to allow multiple callbacks on the same event
* Jint Engine tweak: MemberNameComparer = StringComparer.Ordinal

* Color processor fix
* More fields added to Image tag (jsx def)
* Added unitySliceScale support (new in Unity 2022.2)
* [StyleProcessor] Check for all falsy values instead of just null
* forceUpdate() in useEventfulState
* Added ref support for styled comp
* Added memo to preact/compat

## [2023-03-04] v1.5.7 - Minor Tweaks and Bugfixes

* Better VectorAPI version check
* .boxedValue alternative (for Unity 2021.3)
* Auto-create directory for player workingDir

## [2023-02-18] v1.5.5 - Internal Code Improvements

Online documentation was revamped! Check it out at https://onejs.com/ There is now a new comprehensive Tutorial101.

* Cloud Build support (some internal changes to Bundler)
* Runtime USS errors are now properly handled (No longer need to reload scene)
* Offline Doumentation.pdf revamped
* Many minior tweaks and bugfixes

## [2023-01-26] v1.5.1 - Getting ready for VanillaBox

* New custom Editors for ScriptEngine, Bundler, and LiveReload. Removed dependency on NaughtyAttributes.
* Some internal changes done to support the launch of VanillaBox
* Removed lib.dom.d.ts from the default Typescript typings
* Made WorkingDir more configurable per ScriptEngine. This also makes having multiple ScriptEngines easier.
* Replaced FrontLoader and ImageLoader with the `resource` global variable
* Moved OneJSBuildProcessor functionality to Bundler
* Removed `Extract ScriptLib On Start` option as this is replaced by the ScriptsBundle

## [2023-01-04] v1.4.5 Misc

* TSDefConverter now supports Jint syntax for events ("add_" and "remove_").
* Added a new Sample (CharManSample)

## [2022-12-25] v1.4.4 Misc

* Bugfix for `clearInterval()` not working inside the queued action
* `classnames` added
* VectorAPI check for sample scene

## [2022-12-15] v1.4.3 Built-in Styled Components and Emotion APIs

* Implemented `onejs/styled` which includes both styled-components and emotion APIs.
* `backgroundImage` Style prop now also accepts Sprite objects in addition to string paths and Texture objects.
* Reverted default Trickledown.
* ScriptEngine.Objects list will now accept any UnityEngine.Object, not just MonoBehaviours.

## [2022-11-08] v1.3.5 - useEventfulState()

* Implemented useEventfulState()
* Fixed inconsistent timing between performance.now() and requestAnimationFrame()
* Keep existing unity-* classnames intact for stock controls
* Added IsByRef check in TSDef Converter
* Updated Preact Signals to latest version
* Path Resolver tweak

## [2022-10-13] v1.3.3a - Bundler Changes

* Added user-defined sub-directories for the bundler to ignore during runtime updates

## [2022-10-08] v1.3.3 - Async/await support

* Async/await are now supported in OneJS scripts
* Preact Signals are also now fully supported

* Changed QueuedActions to use PriorityQueue
* Added RadioButton and RadioButtonGroup TS Defs and sample usage
* Fixed issue with relative path loading same modules more than once
* Fixed OneJSBuildProcessor+OnPreprocessBuild _ignoreList issue
* NetSync: Have Server also Broadcast
* Tailwind ParseColor bugfix @LordXyroz

## [2022-09-15] v1.3.1a - ExCSS.Unity fix

* Patched ExCSS.Unity.dll so it doesn't cause conflicts with other Unity packages

## [2022-09-13] v1.3.1 - Minor Bug Fixes

* Fixed turning Live Reload off for Standalone builds
* Add Navigation Events to the TS definitions
* Better UglifyJS error handling
* Adds support for chaining pseudo selectors @Walrusking16
* Tag lookup fix, Dom compliance and ListView example @Sciumo

## [2022-09-01] v1.3.0 - Runtime CSS

You are now able to load CSS strings at runtime via `document.addRuntimeCSS()`. See https://onejs.com/runtimecss for
more information.

* Runtime CSS
* Updated Jint to latest
* Copy to Clipboard for TSDefConverter (credit to @Sciumo)
* Added node_modules to JSPathResolver
* _engine.ResetConstraints() in Update Loop
* Action queues without coroutines
* setInterval

## [2022-07-10] v1.2.1 - Minor features and bugfixes

* Implemented onValueChanged for UI Toolkit controls
* Fixed __listeners Linux slowdown
* GameObject Extensions AddComp(), GetComp(), and FindType() fixes
* UIElementsNativeModule handling for 2022.2 and later

## [2022-06-24] v1.2.0 - WorkingDir Rework

You are now able to keep all your scripts under `{ProjectDir}/OneJS`. And the scripts will be automatically bundled
into`{persistentDataPath}/OneJS` for Standalone builds.

* Added a Bundler component that is responsible for extracting scripts.
* Added OneJSBuildProcessor ScriptableObject that is responsible for packaging scripts (for Standalone builds).
    * This is generally automatic as it uses OnPreprocessBuild
    * It also provides glob ignore patterns for things you don't want to include in the bundle.
* Added `[DefaultExecutionOrder]` for various components.
* Added an extra option (`Poll Standalone Screen`) on the Tailwind component to allow you to also watch for screen
  changes for standalone builds.

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

* Completely reworked Live Reload's File watching mechanism to conserve more CPU cycles. Previously it was using
  FileSystemWatcher (poor performance when watching directories recursively).
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
