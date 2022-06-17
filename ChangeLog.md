## [2022-06-08] v1.1.1 - Flipbook and more Tailwind support

### Newly Added:

* Flipbook Visual Element
* Negative value support for Tailwind

### Minor Bug fixes:

* opacity bugfix
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