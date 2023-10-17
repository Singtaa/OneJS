> This is work in progress and is not yet ready for normal use. Expect lots of bugs and missing parity with Jint.

### Preliminary V8 build for OneJS (via ClearScript)

To Enable V8, use the "Tools/OneJS/Enable V8" menu item. Remember to flush (delete) your ScriptLib folder after pulling the latest changes.

Auto conversion between C# and JS types are very costly in ClearScript (often times more so than Jint). But as long as you keep them off any hot path, you should be able to reap the full benefits of V8's performance.
