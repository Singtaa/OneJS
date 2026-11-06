/* globalThis.__wasm: native WebAssembly modules for QuickJS (quickjs_wasm.c). */
#ifndef QUICKJS_WASM_H
#define QUICKJS_WASM_H

#include "quickjs.h"
#include "../wasm/onejs_wasm.h"

/* Makes a module library's table available to every context. 0, or negative when refused (no table, another ABI, too many). */
int qjs_wasm_register(const onejs_wasm_module* module);

/* Forgets a module by name, so later instantiates do not find it; instances already made keep it. 0, or -1 when none had that name. */
int qjs_wasm_unregister(const char* name);

/* Adds __wasm to a context. */
void qjs_wasm_install(JSContext* ctx);

#endif
