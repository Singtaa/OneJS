/*
 * `globalThis.__wasm`: WebAssembly modules compiled to native code, served to
 * JavaScript in an engine that has no WebAssembly.
 *
 * A module library (wasm/onejs_wasm.h, built by wasm/wasm2onejs.py) describes
 * one module: its exports as thunks, its imports by index, its memory. C#
 * registers each library it finds with qjs_wasm_register, and every context
 * then has
 *
 *     __wasm.has(name, sha256)                   -> boolean
 *     __wasm.instantiate(name, sha256, imports)  -> exports
 *
 * where exports holds a function per export, as WebAssembly.Instance.exports
 * does, and `memory` with `buffer` and `grow`. The glue a module ships for the
 * browser then runs as it is. Nothing here knows any one module.
 *
 * MEMORY THAT MOVES
 *
 * Module libraries are built without guard pages, so no signal handler is
 * installed beside Mono's and IL2CPP's own, and a memory that grows is
 * reallocated. `memory.buffer` is an ArrayBuffer over the bytes as they are
 * now, kept while they stay put, so `buffer === buffer` holds as a browser's
 * does. Whenever control returns to JavaScript (an export returning, an import
 * being called) the memory is compared with that buffer, and a buffer whose
 * bytes have moved is detached before any JavaScript can read through it. A
 * detached buffer is exactly what a browser leaves after `grow`, and is what
 * the glue's views already check for (`byteLength === 0`), so they refresh
 * instead of reading freed memory.
 */
#include <stdio.h>
#include <string.h>
#include "quickjs_compat.h"
#include "quickjs_wasm.h"

#define QJS_WASM_MAX_MODULES 16

static const onejs_wasm_module* g_modules[QJS_WASM_MAX_MODULES];
static int g_module_count = 0;
static JSClassID g_instance_class = 0;

typedef struct {
    const onejs_wasm_module* module;
    void* instance;
    JSContext* ctx;
    /* One function per import, in the module's order. */
    JSValue* imports;
    /* memory.buffer while the bytes stay where they are, else JS_UNDEFINED. */
    JSValue buffer;
    uint8_t* buffer_data;
    uint64_t buffer_size;
} WasmInstance;

int qjs_wasm_register(const onejs_wasm_module* module) {
    if (!module) return -1;
    if (module->abi != ONEJS_WASM_ABI) return -2;
    for (int i = 0; i < g_module_count; i++) {
        if (g_modules[i] == module) return 0;
        if (strcmp(g_modules[i]->name, module->name) == 0) {
            g_modules[i] = module;
            return 0;
        }
    }
    if (g_module_count >= QJS_WASM_MAX_MODULES) return -3;
    g_modules[g_module_count++] = module;
    return 0;
}

int qjs_wasm_unregister(const char* name) {
    if (!name) return -1;
    for (int i = 0; i < g_module_count; i++) {
        if (strcmp(g_modules[i]->name, name) == 0) {
            g_modules[i] = g_modules[--g_module_count];
            g_modules[g_module_count] = NULL;
            return 0;
        }
    }
    return -1;
}

static const onejs_wasm_module* find_module(const char* name) {
    for (int i = 0; i < g_module_count; i++) {
        if (strcmp(g_modules[i]->name, name) == 0) return g_modules[i];
    }
    return NULL;
}

/* The bytes are the module's; nothing to free when a buffer over them goes. */
static void free_nothing(JSRuntime* rt, void* opaque, void* ptr) {
    (void)rt; (void)opaque; (void)ptr;
}

/* Detaches memory.buffer if the bytes under it moved or grew. */
static void check_memory(WasmInstance* w) {
    if (JS_IsUndefined(w->buffer)) return;
    uint64_t size = 0;
    uint8_t* data = w->module->memory(w->instance, &size);
    if (data == w->buffer_data && size == w->buffer_size) return;
    JS_DetachArrayBuffer(w->ctx, w->buffer);
    JS_FreeValue(w->ctx, w->buffer);
    w->buffer = JS_UNDEFINED;
}

static void instance_finalizer(JSRuntime* rt, JSValue val) {
    WasmInstance* w = (WasmInstance*)JS_GetOpaque(val, g_instance_class);
    if (!w) return;
    if (w->instance) w->module->free(w->instance);
    if (w->imports) {
        for (uint32_t i = 0; i < w->module->import_count; i++) JS_FreeValueRT(rt, w->imports[i]);
        js_free_rt(rt, w->imports);
    }
    JS_FreeValueRT(rt, w->buffer);
    js_free_rt(rt, w);
}

static void instance_mark(JSRuntime* rt, JSValueConst val, JS_MarkFunc* mark) {
    WasmInstance* w = (WasmInstance*)JS_GetOpaque(val, g_instance_class);
    if (!w) return;
    if (w->imports) {
        for (uint32_t i = 0; i < w->module->import_count; i++) JS_MarkValue(rt, w->imports[i], mark);
    }
    JS_MarkValue(rt, w->buffer, mark);
}

static JSClassDef g_instance_class_def = {
    "WebAssemblyInstance",
    .finalizer = instance_finalizer,
    .gc_mark = instance_mark,
};

/* A value, as WebAssembly's ToWebAssemblyValue makes it, into a slot. */
static int to_slot(JSContext* ctx, char type, JSValueConst value, uint64_t* slot) {
    if (type == 'i') {
        int32_t v;
        if (JS_ToInt32(ctx, &v, value)) return -1;
        *slot = (uint32_t)v;
    } else if (type == 'l') {
        int64_t v;
        if (JS_ToBigInt64(ctx, &v, value)) return -1;
        *slot = (uint64_t)v;
    } else if (type == 'f') {
        double d;
        if (JS_ToFloat64(ctx, &d, value)) return -1;
        float f = (float)d;
        uint32_t bits;
        memcpy(&bits, &f, 4);
        *slot = bits;
    } else {
        double d;
        if (JS_ToFloat64(ctx, &d, value)) return -1;
        memcpy(slot, &d, 8);
    }
    return 0;
}

/* A slot, as WebAssembly's ToJSValue makes it: an i32 comes back signed. */
static JSValue from_slot(JSContext* ctx, char type, uint64_t slot) {
    if (type == 'i') return JS_NewInt32(ctx, (int32_t)(uint32_t)slot);
    if (type == 'l') return JS_NewBigInt64(ctx, (int64_t)slot);
    if (type == 'f') {
        uint32_t bits = (uint32_t)slot;
        float f;
        memcpy(&f, &bits, 4);
        return JS_NewFloat64(ctx, (double)f);
    }
    double d;
    memcpy(&d, &slot, 8);
    return JS_NewFloat64(ctx, d);
}

static JSValue export_call(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv,
                           int magic, JSValue* data) {
    (void)this_val;
    WasmInstance* w = (WasmInstance*)JS_GetOpaque(data[0], g_instance_class);
    if (!w || !w->instance) return JS_ThrowTypeError(ctx, "that WebAssembly instance is gone");
    const onejs_wasm_export* e = &w->module->exports[magic];
    uint64_t args[32];
    uint64_t results[4] = {0};
    const char* sig = e->sig;
    int n = 0;
    for (; sig[n] != '>'; n++) {
        if (n >= 32) return JS_ThrowRangeError(ctx, "%s takes more than 32 arguments", e->name);
        if (to_slot(ctx, sig[n], n < argc ? argv[n] : JS_UNDEFINED, &args[n])) return JS_EXCEPTION;
    }
    char error[128] = {0};
    int code = w->module->call(w->instance, e->thunk, args, results, error, sizeof(error));
    check_memory(w);
    if (code == ONEJS_WASM_HOST_THREW) return JS_EXCEPTION;
    if (code != ONEJS_WASM_OK) {
        return JS_ThrowInternalError(ctx, "RuntimeError: %s (in %s.%s)", error, w->module->name, e->name);
    }
    const char* result = sig + n + 1;
    return *result ? from_slot(ctx, *result, results[0]) : JS_UNDEFINED;
}

/* An import, called from the module: into the JavaScript function it was given. */
static int import_call(void* host, uint32_t index, const uint64_t* args, uint64_t* results) {
    WasmInstance* w = (WasmInstance*)host;
    JSContext* ctx = w->ctx;
    const onejs_wasm_import* imp = &w->module->imports[index];
    check_memory(w);
    JSValue argv[32];
    int n = 0;
    for (; imp->sig[n] != '>'; n++) {
        if (n >= 32) {
            JS_ThrowRangeError(ctx, "%s takes more than 32 arguments", imp->name);
            for (int k = 0; k < n; k++) JS_FreeValue(ctx, argv[k]);
            return 1;
        }
        argv[n] = from_slot(ctx, imp->sig[n], args[n]);
    }
    JSValue ret = JS_Call(ctx, w->imports[index], JS_UNDEFINED, n, argv);
    for (int k = 0; k < n; k++) JS_FreeValue(ctx, argv[k]);
    if (JS_IsException(ret)) return 1;
    const char* result = imp->sig + n + 1;
    int failed = *result ? to_slot(ctx, *result, ret, &results[0]) : 0;
    JS_FreeValue(ctx, ret);
    return failed ? 1 : 0;
}

static JSValue memory_buffer(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv,
                             int magic, JSValue* data) {
    (void)this_val; (void)argc; (void)argv; (void)magic;
    WasmInstance* w = (WasmInstance*)JS_GetOpaque(data[0], g_instance_class);
    if (!w || !w->instance) return JS_ThrowTypeError(ctx, "that WebAssembly instance is gone");
    check_memory(w);
    if (JS_IsUndefined(w->buffer)) {
        uint64_t size = 0;
        uint8_t* bytes = w->module->memory(w->instance, &size);
        w->buffer = JS_NewArrayBuffer(ctx, bytes, (size_t)size, free_nothing, NULL, 0);
        if (JS_IsException(w->buffer)) {
            w->buffer = JS_UNDEFINED;
            return JS_EXCEPTION;
        }
        w->buffer_data = bytes;
        w->buffer_size = size;
    }
    return JS_DupValue(ctx, w->buffer);
}

static JSValue memory_grow(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv,
                           int magic, JSValue* data) {
    (void)this_val; (void)magic;
    WasmInstance* w = (WasmInstance*)JS_GetOpaque(data[0], g_instance_class);
    if (!w || !w->instance) return JS_ThrowTypeError(ctx, "that WebAssembly instance is gone");
    uint32_t pages = 0;
    if (argc > 0 && JS_ToUint32(ctx, &pages, argv[0])) return JS_EXCEPTION;
    uint64_t old = w->module->grow(w->instance, pages);
    check_memory(w);
    if (old == UINT64_MAX) return JS_ThrowRangeError(ctx, "WebAssembly.Memory.grow(): could not grow by %u pages", pages);
    return JS_NewFloat64(ctx, (double)old);
}

static JSValue wasm_has(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv) {
    (void)this_val;
    if (argc < 2) return JS_FALSE;
    const char* name = JS_ToCString(ctx, argv[0]);
    const char* sha = JS_ToCString(ctx, argv[1]);
    const onejs_wasm_module* m = name ? find_module(name) : NULL;
    int has = m && sha && strcmp(m->sha256, sha) == 0;
    JS_FreeCString(ctx, name);
    JS_FreeCString(ctx, sha);
    return JS_NewBool(ctx, has);
}

static JSValue wasm_instantiate(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv) {
    (void)this_val;
    if (argc < 3) return JS_ThrowTypeError(ctx, "__wasm.instantiate(name, sha256, imports)");
    const char* name = JS_ToCString(ctx, argv[0]);
    if (!name) return JS_EXCEPTION;
    const char* sha = JS_ToCString(ctx, argv[1]);
    if (!sha) {
        JS_FreeCString(ctx, name);
        return JS_EXCEPTION;
    }
    const onejs_wasm_module* m = find_module(name);
    if (!m || strcmp(m->sha256, sha) != 0) {
        // Both hashes named: a glue moved to a new module while an old library
        // is linked fails here, plainly, rather than somewhere inside the glue.
        JSValue err = m
            ? JS_ThrowTypeError(ctx, "the native %s module is %s, and this glue was made for %s", name, m->sha256, sha)
            : JS_ThrowTypeError(ctx, "no native %s module is linked into this build", name);
        JS_FreeCString(ctx, name);
        JS_FreeCString(ctx, sha);
        return err;
    }
    JS_FreeCString(ctx, name);
    JS_FreeCString(ctx, sha);

    WasmInstance* w = (WasmInstance*)js_mallocz(ctx, sizeof(WasmInstance));
    if (!w) return JS_EXCEPTION;
    w->module = m;
    w->ctx = ctx;
    w->buffer = JS_UNDEFINED;
    JSValue holder = JS_NewObjectClass(ctx, (int)g_instance_class);
    if (JS_IsException(holder)) {
        js_free(ctx, w);
        return JS_EXCEPTION;
    }
    JS_SetOpaque(holder, w);

    // Every import resolved before the module starts, since starting it may call them.
    w->imports = (JSValue*)js_mallocz(ctx, sizeof(JSValue) * (m->import_count ? m->import_count : 1));
    if (!w->imports) {
        JS_FreeValue(ctx, holder);
        return JS_EXCEPTION;
    }
    for (uint32_t i = 0; i < m->import_count; i++) w->imports[i] = JS_UNDEFINED;
    for (uint32_t i = 0; i < m->import_count; i++) {
        JSValue mod = JS_GetPropertyStr(ctx, argv[2], m->imports[i].module);
        JSValue fn = JS_IsObject(mod) ? JS_GetPropertyStr(ctx, mod, m->imports[i].name) : JS_UNDEFINED;
        JS_FreeValue(ctx, mod);
        if (!JS_IsFunction(ctx, fn)) {
            JS_FreeValue(ctx, fn);
            JS_FreeValue(ctx, holder);
            return JS_ThrowTypeError(ctx, "LinkError: import %s.%s is not a function", m->imports[i].module, m->imports[i].name);
        }
        w->imports[i] = fn;
    }

    char error[128] = {0};
    w->instance = m->instantiate(w, import_call, error, sizeof(error));
    if (!w->instance) {
        JS_FreeValue(ctx, holder);
        if (JS_HasException(ctx)) return JS_EXCEPTION;
        return JS_ThrowInternalError(ctx, "RuntimeError: %s (starting %s)", error, m->name);
    }

    JSValue exports = JS_NewObject(ctx);
    for (uint32_t i = 0; i < m->export_count; i++) {
        JSValue fn = JS_NewCFunctionData(ctx, export_call, 0, (int)i, 1, &holder);
        JS_DefinePropertyValueStr(ctx, exports, m->exports[i].name, fn, JS_PROP_ENUMERABLE);
    }
    JSValue memory = JS_NewObject(ctx);
    JSAtom buffer_atom = JS_NewAtom(ctx, "buffer");
    JS_DefinePropertyGetSet(ctx, memory, buffer_atom,
                            JS_NewCFunctionData(ctx, memory_buffer, 0, 0, 1, &holder), JS_UNDEFINED,
                            JS_PROP_ENUMERABLE);
    JS_FreeAtom(ctx, buffer_atom);
    JS_SetPropertyStr(ctx, memory, "grow", JS_NewCFunctionData(ctx, memory_grow, 1, 0, 1, &holder));
    JS_DefinePropertyValueStr(ctx, exports, "memory", memory, JS_PROP_ENUMERABLE);
    JS_FreeValue(ctx, holder);
    return exports;
}

void qjs_wasm_install(JSContext* ctx) {
    JSRuntime* rt = JS_GetRuntime(ctx);
    if (g_instance_class == 0) QJS_NEW_CLASS_ID(rt, &g_instance_class);
    if (!JS_IsRegisteredClass(rt, g_instance_class)) JS_NewClass(rt, g_instance_class, &g_instance_class_def);

    JSValue global = JS_GetGlobalObject(ctx);
    JSValue wasm = JS_NewObject(ctx);
    JS_SetPropertyStr(ctx, wasm, "has", JS_NewCFunction(ctx, wasm_has, "has", 2));
    JS_SetPropertyStr(ctx, wasm, "instantiate", JS_NewCFunction(ctx, wasm_instantiate, "instantiate", 3));
    JS_SetPropertyStr(ctx, global, "__wasm", wasm);
    JS_FreeValue(ctx, global);
}
