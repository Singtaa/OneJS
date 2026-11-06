/*
 * The contract between a WebAssembly module compiled to native code and the
 * QuickJS engine that serves it to JavaScript as `globalThis.__wasm`.
 *
 * QuickJS has no WebAssembly. A module OneJS ships natively (Rapier, for oj's
 * physics) is compiled ahead of time: wasm2c turns the same .wasm a browser
 * runs into C, and wasm2onejs.py writes a table describing it, so the result
 * is one native library per module (onejs_rapier3d) holding that C and this
 * table. quickjs_unity serves any such table through one generic shim
 * (src/quickjs_wasm.c): the module's exports as JavaScript functions, its
 * memory as an ArrayBuffer, its imports called back into JavaScript. The glue
 * that drives the module in a browser then runs unchanged.
 *
 * Nothing here knows QuickJS, so a module library links neither engine, and
 * nothing here knows any one module, so a second module costs a table and no
 * new shim code.
 *
 * Values cross as uint64_t slots: an i32 in the low 32 bits, an f32's bits in
 * the low 32 bits, an f64's bits whole. A signature is one letter per value,
 * the parameters, then '>', then the results: 'i' i32, 'l' i64, 'f' f32,
 * 'd' f64. "if>d" takes an i32 and an f32 and returns an f64.
 */
#ifndef ONEJS_WASM_H
#define ONEJS_WASM_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Bumped whenever this struct or a function's meaning changes. */
#define ONEJS_WASM_ABI 1

/* What call() reports. */
#define ONEJS_WASM_OK 0
#define ONEJS_WASM_TRAPPED 1      /* the module trapped; error says why */
#define ONEJS_WASM_HOST_THREW 2   /* an import threw; the host still holds its exception */

/* Calls one export, on an instance, with its arguments and room for its results. */
typedef void (*onejs_wasm_thunk)(void* instance, const uint64_t* args, uint64_t* results);

/*
 * The host's side of every import: which import (its index in `imports`),
 * its arguments and room for its results. Answers 0, or nonzero when the
 * host's function threw, which unwinds the module back to the call that
 * entered it, as a JavaScript exception thrown through WebAssembly does.
 */
typedef int (*onejs_wasm_import_call)(void* host, uint32_t index, const uint64_t* args, uint64_t* results);

typedef struct {
    const char* module;
    const char* name;
    const char* sig;
} onejs_wasm_import;

typedef struct {
    const char* name;
    const char* sig;
    onejs_wasm_thunk thunk;
} onejs_wasm_export;

typedef struct onejs_wasm_module {
    uint32_t abi;
    /* "rapier3d": what the glue asks for, and the library's name after onejs_. */
    const char* name;
    /* The sha256 of the .wasm this was compiled from, in lower case hex. */
    const char* sha256;
    uint32_t import_count;
    const onejs_wasm_import* imports;
    /* Functions only. The memory is reached through memory() below. */
    uint32_t export_count;
    const onejs_wasm_export* exports;

    /* A new instance whose imports go to call(host, ...), or NULL when it could not be made. */
    void* (*instantiate)(void* host, onejs_wasm_import_call call, char* error, size_t error_size);
    void (*free)(void* instance);
    /* The instance's linear memory as it is now. It moves when it grows. */
    uint8_t* (*memory)(void* instance, uint64_t* size);
    /* Grows the memory by `pages` of 64 KiB. Answers the old size in pages, or UINT64_MAX on failure. */
    uint64_t (*grow)(void* instance, uint64_t pages);
    /* Runs one export, catching its traps. One of the ONEJS_WASM_ codes. */
    int (*call)(void* instance, onejs_wasm_thunk thunk, const uint64_t* args, uint64_t* results,
                char* error, size_t error_size);
} onejs_wasm_module;

#ifdef __cplusplus
}
#endif

#endif /* ONEJS_WASM_H */
