#include <string.h>
#include <stdlib.h>
#include <stdint.h>
#include "quickjs.h"

#ifdef _WIN32
#define QJS_API __declspec(dllexport)
#else
#define QJS_API __attribute__((visibility("default")))
#endif

// MARK: Types
#define QJS_MAGIC 0x51534A53u  // 'QSJS'

typedef struct {
    unsigned int magic;
    JSRuntime* rt;
    JSContext* ctx;
} QjsContext;

// MARK: Interop

typedef enum InteropType {
    INTEROP_TYPE_NULL = 0,
    INTEROP_TYPE_BOOL = 1,
    INTEROP_TYPE_INT32 = 2,
    INTEROP_TYPE_DOUBLE = 3,
    INTEROP_TYPE_STRING = 4,
    INTEROP_TYPE_OBJECT_HANDLE = 5
} InteropType;

typedef struct InteropValue {
    int32_t type;
    int32_t _pad; // keep union 8-byte aligned for 64-bit interop
    union {
        int32_t   i32;
        int32_t   b;
        int32_t   handle;
        double    f64;
        const char* str; // UTF-8, owned by native side
    } v;
} InteropValue;

typedef enum InteropInvokeCallKind {
    INTEROP_CALL_CTOR = 0,
    INTEROP_CALL_METHOD = 1,
    INTEROP_CALL_GET_PROP = 2,
    INTEROP_CALL_SET_PROP = 3,
    INTEROP_CALL_GET_FIELD = 4,
    INTEROP_CALL_SET_FIELD = 5
} InteropInvokeCallKind;

typedef struct InteropInvokeRequest {
    const char* type_name;    // e.g. "UnityEngine.GameObject"
    const char* member_name;  // e.g. "ctor", "SetActive", "position"
    int32_t     call_kind;    // InteropInvokeCallKind
    int32_t     is_static;    // 0 = instance, 1 = static
    int32_t     target_handle;// 0 or >0 for instance object
    int32_t     arg_count;
    InteropValue* args;       // pointer to arg array
} InteropInvokeRequest;

typedef struct InteropInvokeResult {
    InteropValue return_value; // result or INTEROP_TYPE_NULL
    int32_t      error_code;   // 0 = ok, non-zero = error
    const char* error_msg;    // optional utf-8, can be NULL
} InteropInvokeResult;

typedef void (*CsInvokeCallback)(
    const InteropInvokeRequest* req,
    InteropInvokeResult* res
    );

// MARK: Dispatcher
static CsInvokeCallback g_cs_invoke = NULL;

QJS_API void qjs_set_cs_invoke_callback(CsInvokeCallback cb) {
    g_cs_invoke = cb;
}



typedef void (*CsLogCallback)(const char* msg);

static CsLogCallback g_cs_log = NULL;

QJS_API void qjs_set_cs_log_callback(CsLogCallback cb) {
    g_cs_log = cb;
}

// MARK: Utils
static void copy_cstring(char* dst, int dstSize, const char* src) {
    if (!dst || dstSize <= 0) return;
    if (!src) {
        dst[0] = '\0';
        return;
    }
    strncpy(dst, src, dstSize - 1);
    dst[dstSize - 1] = '\0';
}

static int is_valid(QjsContext* instance) {
    return instance && instance->magic == QJS_MAGIC && instance->rt && instance->ctx;
}

static void interop_value_from_js(JSContext* ctx, JSValueConst v, InteropValue* out) {
    out->type = INTEROP_TYPE_NULL;
    out->_pad = 0;
    out->v.i32 = 0;

    if (JS_IsNull(v) || JS_IsUndefined(v)) {
        out->type = INTEROP_TYPE_NULL;
        return;
    }

    if (JS_IsBool(v)) {
        int b = JS_ToBool(ctx, v);
        out->type = INTEROP_TYPE_BOOL;
        out->v.b = b ? 1 : 0;
        return;
    }

    if (JS_IsNumber(v)) {
        int32_t i;
        if (JS_ToInt32(ctx, &i, v) == 0) {
            out->type = INTEROP_TYPE_INT32;
            out->v.i32 = i;
        }
        else {
            double d;
            JS_ToFloat64(ctx, &d, v);
            out->type = INTEROP_TYPE_DOUBLE;
            out->v.f64 = d;
        }
        return;
    }

    if (JS_IsString(v)) {
        const char* s = JS_ToCString(ctx, v);
        if (s) {
            size_t len = strlen(s);
            char* copy = (char*)malloc(len + 1);
            if (copy) {
                memcpy(copy, s, len + 1);
                out->type = INTEROP_TYPE_STRING;
                out->v.str = copy;
            }
            else {
                out->type = INTEROP_TYPE_NULL;
            }
            JS_FreeCString(ctx, s);
        }
        return;
    }

    if (JS_IsObject(v)) {
        // Try to read __csHandle for object handles
        JSValue handleVal = JS_GetPropertyStr(ctx, v, "__csHandle");
        if (!JS_IsUndefined(handleVal) && !JS_IsNull(handleVal)) {
            int32_t handle;
            if (JS_ToInt32(ctx, &handle, handleVal) == 0) {
                out->type = INTEROP_TYPE_OBJECT_HANDLE;
                out->v.handle = handle;
                JS_FreeValue(ctx, handleVal);
                return;
            }
        }
        JS_FreeValue(ctx, handleVal);
        // For now, unknown objects are treated as null
        out->type = INTEROP_TYPE_NULL;
        return;
    }

    // Fallback
    out->type = INTEROP_TYPE_NULL;
}

static JSValue interop_value_to_js(JSContext* ctx, const InteropValue* v) {
    switch (v->type) {
    case INTEROP_TYPE_NULL:
        return JS_NULL;
    case INTEROP_TYPE_BOOL:
        return JS_NewBool(ctx, v->v.b != 0);
    case INTEROP_TYPE_INT32:
        return JS_NewInt32(ctx, v->v.i32);
    case INTEROP_TYPE_DOUBLE:
        return JS_NewFloat64(ctx, v->v.f64);
    case INTEROP_TYPE_STRING:
        if (v->v.str) {
            return JS_NewString(ctx, v->v.str);
        }
        else {
            return JS_NULL;
        }
    case INTEROP_TYPE_OBJECT_HANDLE: {
        JSValue obj = JS_NewObject(ctx);
        JS_SetPropertyStr(ctx, obj, "__csHandle", JS_NewInt32(ctx, v->v.handle));
        return obj;
    }
    default:
        return JS_UNDEFINED;
    }
}

// MARK: js_cs_invoke

static int get_array_length(JSContext* ctx, JSValueConst arr, uint32_t* outLen) {
    JSValue lenVal = JS_GetPropertyStr(ctx, arr, "length");
    if (JS_IsException(lenVal)) {
        return -1;
    }

    uint32_t len = 0;
    if (JS_ToUint32(ctx, &len, lenVal) != 0) {
        JS_FreeValue(ctx, lenVal);
        return -1;
    }

    JS_FreeValue(ctx, lenVal);
    *outLen = len;
    return 0;
}

static JSValue js_cs_invoke(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv) {
    if (!g_cs_invoke) {
        return JS_ThrowInternalError(ctx, "C# invoke callback not set");
    }

    if (argc < 5) {
        return JS_ThrowTypeError(ctx, "__cs_invoke expects at least 5 arguments");
    }

    const char* type_name = JS_ToCString(ctx, argv[0]);
    const char* member_name = JS_ToCString(ctx, argv[1]);
    int32_t call_kind = 0;
    int32_t is_static = 0;
    int32_t target_handle = 0;

    if (!type_name || !member_name) {
        if (type_name) JS_FreeCString(ctx, type_name);
        if (member_name) JS_FreeCString(ctx, member_name);
        return JS_ThrowTypeError(ctx, "type_name/member_name must be strings");
    }

    if (JS_ToInt32(ctx, &call_kind, argv[2]) != 0 ||
        JS_ToInt32(ctx, &is_static, argv[3]) != 0 ||
        JS_ToInt32(ctx, &target_handle, argv[4]) != 0) {

        JS_FreeCString(ctx, type_name);
        JS_FreeCString(ctx, member_name);
        return JS_ThrowTypeError(ctx, "callKind/isStatic/targetHandle must be ints");
    }

    // Args array is optional 6th parameter
    JSValue argsArray = JS_UNDEFINED;
    if (argc > 5) {
        argsArray = argv[5];
    }

    int arg_count = 0;
    InteropValue* args = NULL;

    if (!JS_IsUndefined(argsArray) && !JS_IsNull(argsArray)) {
        if (!JS_IsArray(ctx, argsArray)) {
            JS_FreeCString(ctx, type_name);
            JS_FreeCString(ctx, member_name);
            return JS_ThrowTypeError(ctx, "args must be an array");
        }

        uint32_t len = 0;
        if (get_array_length(ctx, argsArray, &len) != 0) {
            JS_FreeCString(ctx, type_name);
            JS_FreeCString(ctx, member_name);
            return JS_ThrowInternalError(ctx, "failed to get args length");
        }

        arg_count = (int)len;
        if (arg_count > 0) {
            args = (InteropValue*)calloc(arg_count, sizeof(InteropValue));
            if (!args) {
                JS_FreeCString(ctx, type_name);
                JS_FreeCString(ctx, member_name);
                return JS_ThrowInternalError(ctx, "out of memory");
            }

            for (int i = 0; i < arg_count; i++) {
                JSValue item = JS_GetPropertyUint32(ctx, argsArray, (uint32_t)i);
                interop_value_from_js(ctx, item, &args[i]);
                JS_FreeValue(ctx, item);
            }
        }
    }

    InteropInvokeRequest req;
    req.type_name = type_name;
    req.member_name = member_name;
    req.call_kind = call_kind;
    req.is_static = is_static;
    req.target_handle = target_handle;
    req.arg_count = arg_count;
    req.args = args;

    InteropInvokeResult res;
    memset(&res, 0, sizeof(res));
    res.return_value.type = INTEROP_TYPE_NULL;
    res.return_value._pad = 0;

    g_cs_invoke(&req, &res);

    // Clean up argument strings (InteropValue STRING type)
    if (args) {
        for (int i = 0; i < arg_count; i++) {
            if (args[i].type == INTEROP_TYPE_STRING && args[i].v.str) {
                free((void*)args[i].v.str);
                args[i].v.str = NULL;
            }
        }
        free(args);
    }

    JS_FreeCString(ctx, type_name);
    JS_FreeCString(ctx, member_name);

    if (res.error_code != 0) {
        const char* msg = res.error_msg ? res.error_msg : "C# invoke error";
        return JS_ThrowInternalError(ctx, "%s", msg);
    }

    JSValue ret = interop_value_to_js(ctx, &res.return_value);

    // NOTE: we do not own res.error_msg nor res.return_value.v.str here;
    // C# should not hand us pointers with longer lifetime requirements yet.

    return ret;
}

static void qjs_init_cs_bridge(JSContext* ctx) {
    JSValue global_obj = JS_GetGlobalObject(ctx);

    // __cs_invoke raw function
    JSValue invokeFunc = JS_NewCFunction(ctx, js_cs_invoke, "__cs_invoke", 6);
    JS_SetPropertyStr(ctx, global_obj, "__cs_invoke", invokeFunc);

    // __cs object with .invoke method
    JSValue csObj = JS_NewObject(ctx);
    JSValue csInvoke = JS_NewCFunction(ctx, js_cs_invoke, "invoke", 6);
    JS_SetPropertyStr(ctx, csObj, "invoke", csInvoke);
    JS_SetPropertyStr(ctx, global_obj, "__cs", csObj);

    JS_FreeValue(ctx, global_obj);
}

// MARK: Console.log

// Console.log implementation: logs only the first argument for now.
static JSValue js_console_log(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv) {
    if (!g_cs_log) {
        return JS_UNDEFINED;
    }

    if (argc > 0) {
        const char* str = JS_ToCString(ctx, argv[0]);
        if (str) {
            g_cs_log(str);
            JS_FreeCString(ctx, str);
        }
    }
    else {
        g_cs_log("");
    }

    return JS_UNDEFINED;
}

static void qjs_init_console(JSContext* ctx) {
    JSValue global_obj = JS_GetGlobalObject(ctx);
    JSValue console = JS_NewObject(ctx);

    JSValue log_fn = JS_NewCFunction(ctx, js_console_log, "log", 1);
    JS_SetPropertyStr(ctx, console, "log", log_fn);

    JS_SetPropertyStr(ctx, global_obj, "console", console);
    JS_FreeValue(ctx, global_obj);
}

// MARK: Lifecycle
QJS_API QjsContext* qjs_create() {
    JSRuntime* rt = JS_NewRuntime();
    if (!rt) return NULL;

    JSContext* ctx = JS_NewContext(rt);
    if (!ctx) {
        JS_FreeRuntime(rt);
        return NULL;
    }

    qjs_init_console(ctx);
    qjs_init_cs_bridge(ctx);

    QjsContext* wrapper = (QjsContext*)malloc(sizeof(QjsContext));
    if (!wrapper) {
        JS_FreeContext(ctx);
        JS_FreeRuntime(rt);
        return NULL;
    }

    wrapper->magic = QJS_MAGIC;
    wrapper->rt = rt;
    wrapper->ctx = ctx;
    return wrapper;
}

QJS_API void qjs_destroy(QjsContext* instance) {
    if (!is_valid(instance)) return;

    JSContext* ctx = instance->ctx;
    JSRuntime* rt = instance->rt;

    // Mark as dead *before* touching QuickJS, so a second call is a no-op
    instance->magic = 0;
    instance->ctx = NULL;
    instance->rt = NULL;

    JS_FreeContext(ctx);
    JS_FreeRuntime(rt);
    free(instance);
}

// MARK: Eval
// Returns 0 on success, non-zero on error.
QJS_API int qjs_eval(
    QjsContext* instance,
    const char* code,
    const char* filename,
    int evalFlags,
    char* outBuf,
    int outBufSize
) {
    if (!is_valid(instance) || !code) {
        copy_cstring(outBuf, outBufSize, "Invalid context or code");
        return -1;
    }

    JSContext* ctx = instance->ctx;
    const char* fname = filename ? filename : "<input>";

    JSValue val = JS_Eval(ctx, code, strlen(code), fname, evalFlags);
    if (JS_IsException(val)) {
        JSValue exc = JS_GetException(ctx);
        const char* msg = JS_ToCString(ctx, exc);
        copy_cstring(outBuf, outBufSize, msg ? msg : "Unknown JS exception");
        if (msg) JS_FreeCString(ctx, msg);
        JS_FreeValue(ctx, exc);
        JS_FreeValue(ctx, val);
        return -1;
    }

    const char* str = JS_ToCString(ctx, val);
    if (str) {
        copy_cstring(outBuf, outBufSize, str);
        JS_FreeCString(ctx, str);
    }
    else {
        copy_cstring(outBuf, outBufSize, "");
    }

    JS_FreeValue(ctx, val);
    return 0;
}

// MARK: GC
QJS_API void qjs_run_gc(QjsContext* instance) {
    if (!is_valid(instance)) return;
    JS_RunGC(instance->rt);
}
