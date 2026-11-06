#!/usr/bin/env python3
"""
Runs a native module library built from probe.wasm through the OneJS engine,
outside Unity, and checks every answer.

    python3 module-smoke.py <engine library> <module library> <name> <module.wasm>

Loads both libraries, registers the module's table with the engine as C# does,
makes a context and instantiates the module through __wasm with the sha256 of
<module.wasm>. Each check prints ok or FAIL with what came back. Exits 0 when
every check passed, 1 when any failed, 2 on bad arguments or a library that
will not load, and 3 when the run compared nothing.
"""

import ctypes
import hashlib
import json
import sys

# What probe.wasm answers, check by check.
CHECKS = [
    ("an export", "e.add(2, 3)", 5),
    ("an i32 wraps and comes back signed", "e.add(2147483647, 1)", -2147483648),
    ("an import into JavaScript", "e.callLog(4) + ':' + logged.join()", "41:4"),
    ("an i64 as a BigInt", "String(e.wide(9007199254740993n))", "9007199254740994"),
    ("f64 and f32", "e.mulf(1.5, 2.5)", 3.75),
    ("memory written by JavaScript, read by the module",
     "(new Uint8Array(e.memory.buffer)[100] = 77, e.load(100))", 77),
    ("memory written by the module, read by JavaScript",
     "(e.store(200, 9), new Uint8Array(e.memory.buffer)[200])", 9),
    ("the same buffer while memory stays put", "e.memory.buffer === e.memory.buffer", True),
    ("a trap is a RuntimeError",
     "(() => { try { e.oob(); return 'no trap' } catch (x) { return String(x).includes('RuntimeError') } })()", True),
    ("the instance works after a trap", "e.add(1, 1)", 2),
    ("growth detaches the old buffer and keeps the bytes",
     "(() => { const b = e.memory.buffer; const old = e.grow(1); "
     "return [old, b.byteLength, e.memory.buffer.byteLength, new Uint8Array(e.memory.buffer)[100]].join() })()",
     "1,0,131072,77"),
]


def main():
    if len(sys.argv) != 5:
        print(__doc__.strip())
        return 2
    engine_path, module_path, name, wasm_path = sys.argv[1:]
    try:
        engine = ctypes.CDLL(engine_path)
        module = ctypes.CDLL(module_path)
        with open(wasm_path, "rb") as f:
            sha = hashlib.sha256(f.read()).hexdigest()
    except OSError as e:
        print(f"FAIL  cannot load: {e}")
        return 2

    table = getattr(module, f"onejs_wasm_{name}", None)
    if table is None:
        print(f"FAIL  {module_path} has no onejs_wasm_{name}")
        return 2
    table.restype = ctypes.c_void_p
    engine.qjs_register_wasm_module.argtypes = [ctypes.c_void_p]
    registered = engine.qjs_register_wasm_module(table())
    if registered != 0:
        print(f"FAIL  the engine refused the module ({registered})")
        return 1

    engine.qjs_create.restype = ctypes.c_void_p
    engine.qjs_eval.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_char_p, ctypes.c_int,
                                ctypes.c_char_p, ctypes.c_int]
    engine.qjs_destroy.argtypes = [ctypes.c_void_p]
    ctx = engine.qjs_create()
    buf = ctypes.create_string_buffer(64 * 1024)

    def evaluate(code):
        rc = engine.qjs_eval(ctx, code.encode(), b"module-smoke.js", 0, buf, len(buf))
        return rc, buf.value.decode("utf-8", "replace")

    rc, text = evaluate(
        "globalThis.logged = []; globalThis.e = __wasm.instantiate("
        f"{json.dumps(name)}, {json.dumps(sha)}, {{ env: {{ log: (x) => (logged.push(x), x * 10) }} }}); 'made'")
    if rc != 0:
        print(f"FAIL  instantiate: {text}")
        engine.qjs_destroy(ctx)
        return 1

    compared = failed = 0
    for label, expression, expected in CHECKS:
        rc, text = evaluate(f"JSON.stringify({expression})")
        got = json.loads(text) if rc == 0 else f"threw {text}"
        compared += 1
        if got == expected:
            print(f"ok    {label}")
        else:
            failed += 1
            print(f"FAIL  {label}: {got!r}, expected {expected!r}")
    engine.qjs_destroy(ctx)

    if compared == 0:
        print("Nothing compared.")
        return 3
    print(f"{compared - failed}/{compared} passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
