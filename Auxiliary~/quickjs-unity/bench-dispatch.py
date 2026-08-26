#!/usr/bin/env python3
"""Benchmark __dispatchEvent on the real QuickJS engine, not on V8.

The event hot path only matters because QuickJS has no JIT, so measuring it in
Node proves nothing: V8 sinks the allocation this is about and reports a number
that would send you the wrong way. This drives the shipped native plugin
directly through ctypes, the same binary the editor loads, so the interpreter
being measured is the one that ships.

It needs no Unity editor, which is the other reason it exists: the profiler is
the right instrument for a GC-alloc figure under a live drag, and it is also
unavailable to anything headless.

    python3 Auxiliary~/quickjs-unity/bench-dispatch.py

Prints per-dispatch cost for the current implementation against the one it
replaced, over three shapes of dispatch, plus the correctness checks that any
change here has to keep passing. Exit code 0 if those checks pass, 1 otherwise.
"""

import ctypes
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

PLUGIN_FOR_PLATFORM = {
    "win32": "Windows/x64/quickjs_unity.dll",
    "darwin": "macOS/libquickjs_unity.dylib",
    "linux": "Linux/x64/libquickjs_unity.so",
}

# Deep enough to be a real UI, shallow enough that the walk is not the whole
# measurement. A button inside a row inside a list inside a panel is about this.
DEPTH = 8
ITERATIONS = 200_000
# Best of N, not the mean. Everything interfering with a run makes it slower and
# nothing makes it faster, so the minimum estimates the real cost while the mean
# estimates how busy the machine was. A first pass here, with an editor running,
# put one figure at 2875 ns and then at 4585 ns.
REPEATS = 5

ENV = """
// The parts of the bootstrap __dispatchEvent touches, and nothing else.
const __eventHandlers = new Map();
const __parentMap = new Map();
const __nonBubblingEvents = new Set(["focus", "blur", "pointerenter", "pointerleave"]);
globalThis.console = { error() {}, log() {} };

const DEPTH = %(depth)d;
// handle 1 is the target, DEPTH is the outermost ancestor
for (let h = 1; h < DEPTH; h++) __parentMap.set(h, h + 1);

function listen(handle, type, fn) {
    let byType = __eventHandlers.get(handle);
    if (!byType) { byType = new Map(); __eventHandlers.set(handle, byType); }
    let set = byType.get(type);
    if (!set) { set = new Set(); byType.set(type, set); }
    set.add(fn);
}
function clearListeners() { __eventHandlers.clear(); }

// A pointermove payload, shaped like the one the bridge sends.
const DATA = { x: 12.5, y: 40.25, button: 0, pointerId: 1, ctrlKey: false, shiftKey: false };
"""

BEFORE = """
function dispatchBefore(elementHandle, eventType, eventData) {
    const event = {
        type: eventType,
        target: elementHandle,
        currentTarget: elementHandle,
        ...eventData,
        preventDefault() { this.defaultPrevented = true; },
        stopPropagation() { this.propagationStopped = true; },
        defaultPrevented: false,
        propagationStopped: false
    };

    const targetHandlers = __eventHandlers.get(elementHandle);
    if (targetHandlers) {
        const callbacks = targetHandlers.get(eventType);
        if (callbacks && callbacks.size > 0) {
            for (const cb of callbacks) {
                try { cb(event); } catch (e) { console.error("Event handler error:", e); }
            }
        }
    }

    if (!event.propagationStopped && !__nonBubblingEvents.has(eventType)) {
        let currentHandle = __parentMap.get(elementHandle);
        while (currentHandle != null && currentHandle > 0) {
            event.currentTarget = currentHandle;
            const parentHandlers = __eventHandlers.get(currentHandle);
            if (parentHandlers) {
                const callbacks = parentHandlers.get(eventType);
                if (callbacks && callbacks.size > 0) {
                    for (const cb of callbacks) {
                        try { cb(event); } catch (e) { console.error("Event handler error:", e); }
                    }
                }
            }
            if (event.propagationStopped) break;
            currentHandle = __parentMap.get(currentHandle);
        }
    }

    return (event.propagationStopped ? 1 : 0) | (event.defaultPrevented ? 2 : 0);
}
"""


def after_source(bootstrap_text):
    """The current implementation, lifted out of the bootstrap rather than copied.

    Copying it here would let the two drift, and a benchmark measuring a stale
    copy of the thing it is named after is worse than no benchmark.
    """
    start = bootstrap_text.index("function __eventPreventDefault")
    end = bootstrap_text.index("globalThis.__dispatchEventFast")
    body = bootstrap_text[start:end]
    return body.replace("globalThis.__dispatchEvent =", "var dispatchAfter =")


CHECKS = """
// Behaviour has to be identical, so both run every check.
function checkOne(dispatch) {
    const out = [];
    clearListeners();

    // 1. no listener anywhere: returns 0, and nothing throws
    out.push(dispatch(1, "pointermove", DATA) === 0);

    // 2. target listener sees type, target and payload, read while it runs
    let seen = null, seenCurrent = null;
    listen(1, "click", (e) => { seen = e; seenCurrent = e.currentTarget; });
    dispatch(1, "click", DATA);
    out.push(seen !== null && seen.type === "click" && seen.target === 1 &&
             seen.x === 12.5 && seenCurrent === 1);

    // 3. the event survives the dispatch. This is the acceptance criterion the
    //    issue is explicit about: no pooling, so a handler may keep it.
    const retained = seen;
    dispatch(1, "click", { x: 999, y: 999 });
    out.push(retained.x === 12.5 && retained.type === "click");

    // 4. bubbling reaches an ancestor, and currentTarget follows the walk
    clearListeners();
    let atAncestor = null;
    listen(DEPTH, "click", (e) => { atAncestor = e.currentTarget; });
    dispatch(1, "click", DATA);
    out.push(atAncestor === DEPTH);

    // 5. stopPropagation blocks the ancestor and is reported in bit 0
    clearListeners();
    let reached = false;
    listen(1, "click", (e) => { e.stopPropagation(); });
    listen(DEPTH, "click", () => { reached = true; });
    out.push(dispatch(1, "click", DATA) === 1 && reached === false);

    // 6. preventDefault is reported in bit 1, and both flags combine
    clearListeners();
    listen(1, "click", (e) => { e.preventDefault(); });
    out.push(dispatch(1, "click", DATA) === 2);
    clearListeners();
    listen(1, "click", (e) => { e.preventDefault(); e.stopPropagation(); });
    out.push(dispatch(1, "click", DATA) === 3);

    // 7. a non-bubbling type never reaches an ancestor
    clearListeners();
    let blurSeen = false;
    listen(DEPTH, "blur", () => { blurSeen = true; });
    dispatch(1, "blur", DATA);
    out.push(blurSeen === false);

    // 8. a throwing handler does not stop the ones after it, or the bubble
    clearListeners();
    let after = false, ancestor = false;
    listen(1, "click", () => { throw new Error("boom"); });
    listen(1, "click", () => { after = true; });
    listen(DEPTH, "click", () => { ancestor = true; });
    dispatch(1, "click", DATA);
    out.push(after === true && ancestor === true);

    clearListeners();
    return out;
}

/**
 * The one place the two deliberately disagree, kept out of the checks above so
 * that a real difference is not reported as a broken benchmark.
 *
 * The old version assigned currentTarget once per ancestor while walking, even
 * for ancestors with no handler, so an event retained past its dispatch ended
 * up reporting the outermost element in the chain: a value no handler ever saw
 * and nothing ever set on purpose. The current one assigns it only where a
 * handler is about to read it, so a retained event still says what the last
 * handler that ran was looking at.
 */
function retainedCurrentTarget(dispatch) {
    clearListeners();
    let seen = null;
    listen(1, "click", (e) => { seen = e; });
    dispatch(1, "click", DATA);
    clearListeners();
    return seen.currentTarget;
}
"""

BENCH = """
function bench(dispatch, setup, iterations) {
    clearListeners();
    setup();
    // Warm the shapes without timing them. There is no JIT to warm, but the
    // first pass still pays for hidden class creation and Map growth.
    for (let i = 0; i < 2000; i++) dispatch(1, "pointermove", DATA);
    const t0 = Date.now();
    for (let i = 0; i < iterations; i++) dispatch(1, "pointermove", DATA);
    const ms = Date.now() - t0;
    clearListeners();
    return ms;
}

let sink = 0;
const SCENARIOS = {
    // The case the change is for: nothing on the path listens for this type.
    // Every pointermove over unhandled chrome looks like this.
    "no listener": () => {},
    // The common case, and the one that must not regress.
    "listener on target": () => listen(1, "pointermove", (e) => { sink += e.x; }),
    // The full walk, with the only listener at the far end.
    "listener on root": () => listen(DEPTH, "pointermove", (e) => { sink += e.x; }),
};

function bestOf(dispatch, setup, iterations, repeats) {
    let best = Infinity;
    for (let r = 0; r < repeats; r++) {
        const ms = bench(dispatch, setup, iterations);
        if (ms < best) best = ms;
    }
    return best;
}

const results = {};
for (const [name, setup] of Object.entries(SCENARIOS)) {
    results[name] = {
        before: bestOf(dispatchBefore, setup, ITERATIONS, REPEATS),
        after: bestOf(dispatchAfter, setup, ITERATIONS, REPEATS),
    };
}
JSON.stringify({
    checks: { before: checkOne(dispatchBefore), after: checkOne(dispatchAfter) },
    retained: { before: retainedCurrentTarget(dispatchBefore),
                after: retainedCurrentTarget(dispatchAfter) },
    ms: results,
    iterations: ITERATIONS,
});
"""


def main():
    plugin = PLUGIN_FOR_PLATFORM.get(sys.platform)
    if plugin is None:
        print(f"no plugin for {sys.platform}")
        return 1
    lib = ctypes.CDLL(str(ROOT / "Plugins" / plugin))
    lib.qjs_create.restype = ctypes.c_void_p
    lib.qjs_destroy.argtypes = [ctypes.c_void_p]
    lib.qjs_eval.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_char_p,
                             ctypes.c_int, ctypes.c_void_p, ctypes.c_int]
    lib.qjs_eval.restype = ctypes.c_int

    bootstrap = (ROOT / "Resources/OneJS/QuickJSBootstrap.js.txt").read_text(
        encoding="utf-8", errors="replace")

    program = "\n".join([
        ENV % {"depth": DEPTH},
        f"const ITERATIONS = {ITERATIONS}; const REPEATS = {REPEATS};",
        BEFORE,
        after_source(bootstrap),
        CHECKS,
        BENCH,
    ])

    ctx = lib.qjs_create()
    if not ctx:
        print("qjs_create failed")
        return 1
    buf = ctypes.create_string_buffer(1 << 16)
    rc = lib.qjs_eval(ctx, program.encode("utf-8"), b"bench-dispatch.js", 0, buf, len(buf))
    out = buf.value.decode("utf-8", errors="replace")
    lib.qjs_destroy(ctx)

    if rc != 0:
        print(f"eval failed rc={rc}: {out}")
        return 1

    data = json.loads(out)
    ok = all(data["checks"]["before"]) and all(data["checks"]["after"])
    n = data["iterations"]

    print(f"QuickJS {sys.platform}, {n:,} dispatches per figure, "
          f"best of {REPEATS}, chain depth {DEPTH}\n")
    print(f"{'scenario':22} {'before':>10} {'after':>10} {'change':>10}")
    for name, r in data["ms"].items():
        b = r["before"] / n * 1e6
        a = r["after"] / n * 1e6
        delta = (a - b) / b * 100 if b else 0
        print(f"{name:22} {b:8.0f} ns {a:8.0f} ns {delta:+9.1f}%")

    print()
    for side in ("before", "after"):
        results = data["checks"][side]
        failed = [i + 1 for i, v in enumerate(results) if not v]
        print(f"checks {side:6} {sum(results)}/{len(results)}"
              + (f"  FAILED: {failed}" if failed else ""))

    r = data["retained"]
    print("\nretained event currentTarget, no ancestor handler:"
          f" before {r['before']}, after {r['after']} (target is 1)")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
