## Preliminary V8 build for OneJS (via ClearScript)

> This is work in progress and is not yet ready for normal use. Expect lots of bugs and missing features (compared with Jint). Goal here is to have seamless transition between the 2 backends (Jint and V8).

To Enable V8, use the "Tools/OneJS/Enable V8" menu item. Remember to flush (delete) your ScriptLib folder after pulling the latest changes.

### Some Quick notes about ClearScript

 * Auto conversion between C# and JS types are actually very costly in ClearScript (often times more so than Jint). But as long as you keep them off any hot path, you should be able to reap the full benefits of V8's performance.
 * Jint supports Operator Overloading; ClearScript V8 cannot. So you cannot quickly add 2 vectors together using just a plus sign like you can with Jint.
