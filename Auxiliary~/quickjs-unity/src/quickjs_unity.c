#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include "quickjs.h"

// MARK: Platform

#ifdef _WIN32
    #define QJS_API __declspec(dllexport)
#else
    #define QJS_API __attribute__((visibility("default")))
#endif

// MARK: Constants

#define QJS_MAGIC 0x51534A53u // 'QSJS'
#define QJS_MAX_CALLBACKS 4096
#define QJS_EXCEPTION_BUF_SIZE 2048

// MARK: Error Codes

typedef enum QjsError {
    QJS_OK = 0,
    QJS_ERR_INVALID_CTX = -1,
    QJS_ERR_INVALID_HANDLE = -2,
    QJS_ERR_NOT_FUNCTION = -3,
    QJS_ERR_OUT_OF_MEMORY = -4,
    QJS_ERR_EXCEPTION = -5
} QjsError;

// MARK: Types

typedef struct {
    unsigned int magic;
    JSRuntime* rt;
    JSContext* ctx;
    JSValue callbacks[QJS_MAX_CALLBACKS];
    int callback_next;
    int callback_count;
    int callback_free_head;
} QjsContext;

typedef enum InteropType {
    INTEROP_TYPE_NULL = 0,
    INTEROP_TYPE_BOOL = 1,
    INTEROP_TYPE_INT32 = 2,
    INTEROP_TYPE_DOUBLE = 3,
    INTEROP_TYPE_STRING = 4,
    INTEROP_TYPE_OBJECT_HANDLE = 5,
    INTEROP_TYPE_INT64 = 6,
    INTEROP_TYPE_FLOAT32 = 7,
    INTEROP_TYPE_ARRAY = 8,
    INTEROP_TYPE_JSON_OBJECT = 9,
    INTEROP_TYPE_VECTOR3 = 10,  // Binary packed x,y,z floats
    INTEROP_TYPE_VECTOR4 = 11   // Binary packed x,y,z,w floats (Quaternion, Color)
} InteropType;

typedef struct InteropValue {
    int32_t type;
    int32_t _pad;
    union {
        int32_t i32;
        int32_t b;
        int32_t handle;
        int64_t i64;
        float f32;
        double f64;
        const char* str;
        struct { float x, y, z, w; } vec;  // For Vector3/Vector4
    } v;
    const char* typeHint;
} InteropValue;

typedef enum InteropInvokeCallKind {
    INTEROP_CALL_CTOR = 0,
    INTEROP_CALL_METHOD = 1,
    INTEROP_CALL_GET_PROP = 2,
    INTEROP_CALL_SET_PROP = 3,
    INTEROP_CALL_GET_FIELD = 4,
    INTEROP_CALL_SET_FIELD = 5,
    INTEROP_CALL_TYPE_EXISTS = 6,
    INTEROP_CALL_IS_ENUM_TYPE = 7
} InteropInvokeCallKind;

typedef struct InteropInvokeRequest {
    const char* type_name;
    const char* member_name;
    int32_t call_kind;
    int32_t is_static;
    int32_t target_handle;
    int32_t arg_count;
    InteropValue* args;
} InteropInvokeRequest;

typedef struct InteropInvokeResult {
    InteropValue return_value;
    int32_t error_code;
    const char* error_msg;
} InteropInvokeResult;

// MARK: Callbacks

typedef void (*CsInvokeCallback)(QjsContext* ctx, const InteropInvokeRequest* req, InteropInvokeResult* res);
typedef void (*CsLogCallback)(const char* msg);
typedef void (*CsReleaseHandleCallback)(int handle);

/**
 * Zero-allocation dispatch callback.
 * Called from fixed-arity __zaInvoke functions with stack-allocated args.
 *
 * @param bindingId  Pre-registered binding ID (from interop.bind())
 * @param args       Stack-allocated array of InteropValues
 * @param argCount   Number of arguments (0-8)
 * @param outResult  Output result (caller-allocated)
 */
typedef void (*CsZeroAllocCallback)(int32_t bindingId, const InteropValue* args, int32_t argCount, InteropValue* outResult);

static struct {
    CsInvokeCallback invoke;
    CsLogCallback log;
    CsReleaseHandleCallback release_handle;
    CsZeroAllocCallback zeroalloc;
} g_callbacks = {0};

QJS_API void qjs_set_cs_invoke_callback(CsInvokeCallback cb) { g_callbacks.invoke = cb; }
QJS_API void qjs_set_cs_log_callback(CsLogCallback cb) { g_callbacks.log = cb; }
QJS_API void qjs_set_cs_release_handle_callback(CsReleaseHandleCallback cb) {
    g_callbacks.release_handle = cb;
}
QJS_API void qjs_set_cs_zeroalloc_callback(CsZeroAllocCallback cb) { g_callbacks.zeroalloc = cb; }

// MARK: Utils

static int is_valid(QjsContext* instance) {
    return instance && instance->magic == QJS_MAGIC && instance->rt && instance->ctx;
}

static void copy_cstring(char* dst, int dstSize, const char* src) {
    if (!dst || dstSize <= 0) return;
    if (!src) {
        dst[0] = '\0';
        return;
    }
    strncpy(dst, src, dstSize - 1);
    dst[dstSize - 1] = '\0';
}

static char* strdup_alloc(const char* s) {
    if (!s) return NULL;
    size_t len = strlen(s) + 1;
    char* copy = (char*)malloc(len);
    if (copy) memcpy(copy, s, len);
    return copy;
}

static int get_array_length(JSContext* ctx, JSValueConst arr, uint32_t* outLen) {
    JSValue lenVal = JS_GetPropertyStr(ctx, arr, "length");
    if (JS_IsException(lenVal)) return -1;

    uint32_t len = 0;
    int result = JS_ToUint32(ctx, &len, lenVal);
    JS_FreeValue(ctx, lenVal);

    if (result != 0) return -1;
    *outLen = len;
    return 0;
}

static void format_exception(JSContext* ctx, JSValue exc, char* outBuf, int outBufSize) {
    if (outBufSize <= 0) return;
    outBuf[0] = '\0';

    const char* msg = JS_ToCString(ctx, exc);
    JSValue stack = JS_GetPropertyStr(ctx, exc, "stack");
    const char* stackStr = NULL;

    if (!JS_IsUndefined(stack) && !JS_IsNull(stack)) {
        stackStr = JS_ToCString(ctx, stack);
    }

    // Combine message and stack trace for complete error info
    if (msg && stackStr && strlen(stackStr) > 0) {
        // Format: "ErrorMessage\n    at location..."
        int msgLen = strlen(msg);
        int stackLen = strlen(stackStr);
        if (msgLen + 1 + stackLen + 1 <= outBufSize) {
            snprintf(outBuf, outBufSize, "%s\n%s", msg, stackStr);
        } else {
            // Buffer too small, prioritize message
            copy_cstring(outBuf, outBufSize, msg);
        }
    } else if (msg) {
        copy_cstring(outBuf, outBufSize, msg);
    } else if (stackStr && strlen(stackStr) > 0) {
        copy_cstring(outBuf, outBufSize, stackStr);
    } else {
        copy_cstring(outBuf, outBufSize, "Unknown JS exception");
    }

    if (stackStr) JS_FreeCString(ctx, stackStr);
    if (msg) JS_FreeCString(ctx, msg);
    JS_FreeValue(ctx, stack);
}

// MARK: JSON Helper

// Serialize any JS value to JSON string using JSON.stringify
static char* js_value_to_json(JSContext* ctx, JSValueConst v) {
    JSValue global = JS_GetGlobalObject(ctx);
    JSValue json = JS_GetPropertyStr(ctx, global, "JSON");
    JSValue stringify = JS_GetPropertyStr(ctx, json, "stringify");

    JSValue strResult = JS_Call(ctx, stringify, json, 1, &v);

    JS_FreeValue(ctx, stringify);
    JS_FreeValue(ctx, json);
    JS_FreeValue(ctx, global);

    if (JS_IsException(strResult)) {
        JS_FreeValue(ctx, strResult);
        return NULL;
    }

    const char* s = JS_ToCString(ctx, strResult);
    JS_FreeValue(ctx, strResult);

    if (s) {
        char* copy = strdup_alloc(s);
        JS_FreeCString(ctx, s);
        return copy;
    }
    return NULL;
}

// MARK: Vector Detection

// Try to read a float property, returns 1 on success
static int try_get_float_prop(JSContext* ctx, JSValueConst obj, const char* name, float* out) {
    JSValue val = JS_GetPropertyStr(ctx, obj, name);
    if (JS_IsUndefined(val)) {
        JS_FreeValue(ctx, val);
        return 0;
    }
    
    double d;
    int ok = JS_ToFloat64(ctx, &d, val) == 0;
    JS_FreeValue(ctx, val);
    
    if (ok) *out = (float)d;
    return ok;
}

// Detect {x, y, z} pattern and pack as Vector3
static int try_convert_vector3(JSContext* ctx, JSValueConst v, InteropValue* out) {
    float x, y, z;
    
    if (!try_get_float_prop(ctx, v, "x", &x)) return 0;
    if (!try_get_float_prop(ctx, v, "y", &y)) return 0;
    if (!try_get_float_prop(ctx, v, "z", &z)) return 0;
    
    // Check if it also has 'w' - if so, it's a Vector4/Quaternion
    float w;
    if (try_get_float_prop(ctx, v, "w", &w)) {
        out->type = INTEROP_TYPE_VECTOR4;
        out->v.vec.x = x;
        out->v.vec.y = y;
        out->v.vec.z = z;
        out->v.vec.w = w;
        return 1;
    }
    
    out->type = INTEROP_TYPE_VECTOR3;
    out->v.vec.x = x;
    out->v.vec.y = y;
    out->v.vec.z = z;
    out->v.vec.w = 0;
    return 1;
}

// Detect {r, g, b, a} pattern for Color
static int try_convert_color(JSContext* ctx, JSValueConst v, InteropValue* out) {
    float r, g, b, a = 1.0f;
    
    if (!try_get_float_prop(ctx, v, "r", &r)) return 0;
    if (!try_get_float_prop(ctx, v, "g", &g)) return 0;
    if (!try_get_float_prop(ctx, v, "b", &b)) return 0;
    try_get_float_prop(ctx, v, "a", &a);  // Optional, defaults to 1
    
    out->type = INTEROP_TYPE_VECTOR4;  // Color uses same layout as Vector4
    out->v.vec.x = r;
    out->v.vec.y = g;
    out->v.vec.z = b;
    out->v.vec.w = a;
    out->typeHint = strdup_alloc("color");  // Hint to C# this is a Color
    return 1;
}

// MARK: Interop Conv

static int try_convert_array(JSContext* ctx, JSValueConst v, InteropValue* out) {
    if (!JS_IsArray(ctx, v)) return 0;

    uint32_t len = 0;
    get_array_length(ctx, v, &len);

    out->type = INTEROP_TYPE_ARRAY;
    out->v.i32 = (int32_t)len;
    return 1;
}

static int try_convert_handle(JSContext* ctx, JSValueConst v, InteropValue* out) {
    JSValue handleVal = JS_GetPropertyStr(ctx, v, "__csHandle");
    if (JS_IsUndefined(handleVal) || JS_IsNull(handleVal)) {
        JS_FreeValue(ctx, handleVal);
        return 0;
    }

    int32_t handle;
    if (JS_ToInt32(ctx, &handle, handleVal) != 0) {
        JS_FreeValue(ctx, handleVal);
        return 0;
    }

    out->type = INTEROP_TYPE_OBJECT_HANDLE;
    out->v.handle = handle;
    JS_FreeValue(ctx, handleVal);
    return 1;
}

// Check if object has __struct or __type marker (explicit struct from C#)
static int try_convert_struct(JSContext* ctx, JSValueConst v, InteropValue* out) {
    JSValue structVal = JS_GetPropertyStr(ctx, v, "__struct");
    JSValue typeVal = JS_GetPropertyStr(ctx, v, "__type");
    
    int hasMarker = (!JS_IsUndefined(structVal) && !JS_IsNull(structVal)) ||
                    (!JS_IsUndefined(typeVal) && !JS_IsNull(typeVal));
    
    JS_FreeValue(ctx, structVal);
    JS_FreeValue(ctx, typeVal);
    
    if (!hasMarker) return 0;

    char* json = js_value_to_json(ctx, v);
    if (json) {
        out->type = INTEROP_TYPE_STRING;
        out->v.str = json;
        return 1;
    }
    return 0;
}

// Convert plain object - try vector patterns first, then fall back to JSON
static int try_convert_plain_object(JSContext* ctx, JSValueConst v, InteropValue* out) {
    // Skip if it's a function, array, or has special markers
    if (JS_IsFunction(ctx, v)) return 0;
    if (JS_IsArray(ctx, v)) return 0;
    
    // Check for __csHandle (would have been caught earlier, but be safe)
    JSValue handleVal = JS_GetPropertyStr(ctx, v, "__csHandle");
    int hasHandle = !JS_IsUndefined(handleVal) && !JS_IsNull(handleVal);
    JS_FreeValue(ctx, handleVal);
    if (hasHandle) return 0;

    // Try binary vector patterns first (zero-alloc path)
    if (try_convert_vector3(ctx, v, out)) return 1;
    if (try_convert_color(ctx, v, out)) return 1;

    // Fall back to JSON for other objects
    char* json = js_value_to_json(ctx, v);
    if (json) {
        out->type = INTEROP_TYPE_JSON_OBJECT;
        out->v.str = json;
        return 1;
    }
    return 0;
}

static void interop_value_from_js(JSContext* ctx, JSValueConst v, InteropValue* out) {
    out->type = INTEROP_TYPE_NULL;
    out->_pad = 0;
    out->v.i64 = 0;
    out->typeHint = NULL;

    if (JS_IsNull(v) || JS_IsUndefined(v)) {
        return;
    }

    if (JS_IsBool(v)) {
        out->type = INTEROP_TYPE_BOOL;
        out->v.b = JS_ToBool(ctx, v) ? 1 : 0;
        return;
    }

    if (JS_IsNumber(v)) {
        double d;
        JS_ToFloat64(ctx, &d, v);

        if (d == (double)(int32_t)d && d >= INT32_MIN && d <= INT32_MAX) {
            out->type = INTEROP_TYPE_INT32;
            out->v.i32 = (int32_t)d;
        } else {
            out->type = INTEROP_TYPE_DOUBLE;
            out->v.f64 = d;
        }
        return;
    }

    if (JS_IsString(v)) {
        const char* s = JS_ToCString(ctx, v);
        if (s) {
            char* copy = strdup_alloc(s);
            JS_FreeCString(ctx, s);
            if (copy) {
                out->type = INTEROP_TYPE_STRING;
                out->v.str = copy;
            }
        }
        return;
    }

    if (JS_IsObject(v)) {
        if (try_convert_array(ctx, v, out)) return;
        if (try_convert_handle(ctx, v, out)) return;
        if (try_convert_struct(ctx, v, out)) return;
        if (try_convert_plain_object(ctx, v, out)) return;
    }
}

static JSValue interop_value_to_js(JSContext* ctx, const InteropValue* v) {
    switch (v->type) {
    case INTEROP_TYPE_NULL:
        return JS_NULL;
    case INTEROP_TYPE_BOOL:
        return JS_NewBool(ctx, v->v.b != 0);
    case INTEROP_TYPE_INT32:
        return JS_NewInt32(ctx, v->v.i32);
    case INTEROP_TYPE_INT64:
        return JS_NewInt64(ctx, v->v.i64);
    case INTEROP_TYPE_FLOAT32:
        return JS_NewFloat64(ctx, (double)v->v.f32);
    case INTEROP_TYPE_DOUBLE:
        return JS_NewFloat64(ctx, v->v.f64);
    case INTEROP_TYPE_STRING:
        return v->v.str ? JS_NewString(ctx, v->v.str) : JS_NULL;
    case INTEROP_TYPE_OBJECT_HANDLE:
        {
            JSValue obj = JS_NewObject(ctx);
            JS_SetPropertyStr(ctx, obj, "__csHandle", JS_NewInt32(ctx, v->v.handle));
            if (v->typeHint && v->typeHint[0]) {
                JS_SetPropertyStr(ctx, obj, "__csType", JS_NewString(ctx, v->typeHint));
            }
            return obj;
        }
    case INTEROP_TYPE_VECTOR3:
        {
            JSValue obj = JS_NewObject(ctx);
            JS_SetPropertyStr(ctx, obj, "x", JS_NewFloat64(ctx, v->v.vec.x));
            JS_SetPropertyStr(ctx, obj, "y", JS_NewFloat64(ctx, v->v.vec.y));
            JS_SetPropertyStr(ctx, obj, "z", JS_NewFloat64(ctx, v->v.vec.z));
            return obj;
        }
    case INTEROP_TYPE_VECTOR4:
        {
            JSValue obj = JS_NewObject(ctx);
            // Check hint for color vs quaternion
            if (v->typeHint && strcmp(v->typeHint, "color") == 0) {
                JS_SetPropertyStr(ctx, obj, "r", JS_NewFloat64(ctx, v->v.vec.x));
                JS_SetPropertyStr(ctx, obj, "g", JS_NewFloat64(ctx, v->v.vec.y));
                JS_SetPropertyStr(ctx, obj, "b", JS_NewFloat64(ctx, v->v.vec.z));
                JS_SetPropertyStr(ctx, obj, "a", JS_NewFloat64(ctx, v->v.vec.w));
            } else {
                JS_SetPropertyStr(ctx, obj, "x", JS_NewFloat64(ctx, v->v.vec.x));
                JS_SetPropertyStr(ctx, obj, "y", JS_NewFloat64(ctx, v->v.vec.y));
                JS_SetPropertyStr(ctx, obj, "z", JS_NewFloat64(ctx, v->v.vec.z));
                JS_SetPropertyStr(ctx, obj, "w", JS_NewFloat64(ctx, v->v.vec.w));
            }
            return obj;
        }
    case INTEROP_TYPE_ARRAY:
        // For returning arrays, we'd need to serialize elements
        return JS_NULL;
    case INTEROP_TYPE_JSON_OBJECT:
        // This shouldn't happen (JSON_OBJECT is for JS->C# only)
        return v->v.str ? JS_NewString(ctx, v->v.str) : JS_NULL;
    default:
        return JS_NULL;
    }
}

static void free_interop_value(InteropValue* v) {
    if (!v) return;
    if ((v->type == INTEROP_TYPE_STRING || v->type == INTEROP_TYPE_JSON_OBJECT) && v->v.str) {
        free((void*)v->v.str);
        v->v.str = NULL;
    }
    if (v->typeHint) {
        free((void*)v->typeHint);
        v->typeHint = NULL;
    }
}

static void free_interop_args(InteropValue* args, int count) {
    if (!args) return;
    for (int i = 0; i < count; i++) {
        free_interop_value(&args[i]);
    }
    free(args);
}

// MARK: JS Functions

static JSValue js_console_log(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv) {
    if (!g_callbacks.log) return JS_UNDEFINED;

    for (int i = 0; i < argc; i++) {
        const char* str = JS_ToCString(ctx, argv[i]);
        if (str) {
            g_callbacks.log(str);
            JS_FreeCString(ctx, str);
        }
    }
    return JS_UNDEFINED;
}

static JSValue js_release_handle(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv) {
    if (argc < 1) return JS_UNDEFINED;

    int32_t handle = 0;
    if (JS_ToInt32(ctx, &handle, argv[0]) != 0) return JS_UNDEFINED;

    if (handle > 0 && g_callbacks.release_handle) {
        g_callbacks.release_handle(handle);
    }
    return JS_UNDEFINED;
}

static JSValue js_register_callback(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv) {
    if (argc < 1 || !JS_IsFunction(ctx, argv[0])) {
        return JS_ThrowTypeError(ctx, "registerCallback: arg must be a function");
    }

    QjsContext* qctx = (QjsContext*)JS_GetContextOpaque(ctx);
    if (!qctx) return JS_ThrowInternalError(ctx, "no context");

    int slot = -1;

    // Try free list first
    if (qctx->callback_free_head >= 0) {
        slot = qctx->callback_free_head;
        // The slot stores next free index as a tagged integer (negative or special)
        // For simplicity, we'll just scan if free list is complex
        qctx->callback_free_head = -1; // Simple: just use one slot from free list
    }

    // Otherwise scan for empty slot
    if (slot < 0) {
        for (int i = 0; i < QJS_MAX_CALLBACKS; i++) {
            int idx = (qctx->callback_next + i) % QJS_MAX_CALLBACKS;
            if (JS_IsUndefined(qctx->callbacks[idx])) {
                slot = idx;
                qctx->callback_next = (idx + 1) % QJS_MAX_CALLBACKS;
                break;
            }
        }
    }

    if (slot < 0) {
        return JS_ThrowInternalError(ctx, "callback table full");
    }

    qctx->callbacks[slot] = JS_DupValue(ctx, argv[0]);
    qctx->callback_count++;

    return JS_NewInt32(ctx, slot);
}

static JSValue js_unregister_callback(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv) {
    if (argc < 1) return JS_FALSE;

    int32_t handle;
    if (JS_ToInt32(ctx, &handle, argv[0]) != 0) return JS_FALSE;

    QjsContext* qctx = (QjsContext*)JS_GetContextOpaque(ctx);
    if (!qctx) return JS_FALSE;

    if (handle < 0 || handle >= QJS_MAX_CALLBACKS) return JS_FALSE;
    if (JS_IsUndefined(qctx->callbacks[handle])) return JS_FALSE;

    JS_FreeValue(ctx, qctx->callbacks[handle]);
    qctx->callbacks[handle] = JS_UNDEFINED;
    qctx->callback_count--;

    return JS_TRUE;
}

static JSValue js_cs_invoke(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv) {
    if (!g_callbacks.invoke) {
        return JS_ThrowInternalError(ctx, "invoke callback not set");
    }

    if (argc < 5) {
        return JS_ThrowTypeError(ctx, "cs_invoke requires 5+ args");
    }

    const char* type_name = NULL;
    const char* member_name = NULL;
    InteropValue* args = NULL;
    int arg_count = 0;
    JSValue result = JS_UNDEFINED;

    type_name = JS_ToCString(ctx, argv[0]);
    member_name = JS_ToCString(ctx, argv[1]);

    if (!type_name) {
        result = JS_ThrowTypeError(ctx, "typeName must be a string");
        goto cleanup;
    }

    int32_t call_kind = 0, is_static = 0, target_handle = 0;
    if (JS_ToInt32(ctx, &call_kind, argv[2]) != 0 || JS_ToInt32(ctx, &is_static, argv[3]) != 0 ||
        JS_ToInt32(ctx, &target_handle, argv[4]) != 0) {
        result = JS_ThrowTypeError(ctx, "callKind/isStatic/targetHandle must be ints");
        goto cleanup;
    }

    if (argc > 5 && !JS_IsUndefined(argv[5]) && !JS_IsNull(argv[5])) {
        if (!JS_IsArray(ctx, argv[5])) {
            result = JS_ThrowTypeError(ctx, "args must be an array");
            goto cleanup;
        }

        uint32_t len = 0;
        if (get_array_length(ctx, argv[5], &len) != 0) {
            result = JS_ThrowInternalError(ctx, "failed to get args length");
            goto cleanup;
        }

        arg_count = (int)len;
        if (arg_count > 0) {
            args = (InteropValue*)calloc(arg_count, sizeof(InteropValue));
            if (!args) {
                result = JS_ThrowInternalError(ctx, "out of memory");
                goto cleanup;
            }

            for (int i = 0; i < arg_count; i++) {
                JSValue item = JS_GetPropertyUint32(ctx, argv[5], (uint32_t)i);
                interop_value_from_js(ctx, item, &args[i]);
                JS_FreeValue(ctx, item);
            }
        }
    }

    InteropInvokeRequest req = {
        .type_name = type_name,
        .member_name = member_name,
        .call_kind = call_kind,
        .is_static = is_static,
        .target_handle = target_handle,
        .arg_count = arg_count,
        .args = args
    };

    InteropInvokeResult res = {0};
    res.return_value.type = INTEROP_TYPE_NULL;

    QjsContext* qctx = (QjsContext*)JS_GetContextOpaque(ctx);
    g_callbacks.invoke(qctx, &req, &res);

    if (res.error_code != 0) {
        const char* msg = res.error_msg ? res.error_msg : "C# invoke error";
        result = JS_ThrowInternalError(ctx, "%s", msg);
        goto cleanup;
    }

    result = interop_value_to_js(ctx, &res.return_value);

    if (res.return_value.type == INTEROP_TYPE_STRING && res.return_value.v.str) {
        free((void*)res.return_value.v.str);
    }
    if (res.return_value.typeHint) {
        free((void*)res.return_value.typeHint);
    }

cleanup:
    if (type_name) JS_FreeCString(ctx, type_name);
    if (member_name) JS_FreeCString(ctx, member_name);
    free_interop_args(args, arg_count);
    return result;
}

// MARK: Zero-Alloc Invoke
//
// These functions provide zero-allocation C# interop by:
// 1. Using stack-allocated InteropValue arrays (no malloc)
// 2. Taking a pre-registered bindingId instead of type/member strings
// 3. Converting JS args inline without intermediate allocations
//
// Usage from JS:
//   const result = __zaInvoke3(bindingId, arg0, arg1, arg2)
//
// The bindingId is obtained from C# via interop.bind() which returns
// a numeric ID that maps to a pre-registered delegate.

/**
 * Convert JS value to InteropValue without allocation.
 * Unlike interop_value_from_js, this does NOT allocate strings - it stores
 * a pointer to QuickJS's internal string which remains valid until the
 * next JS operation. Caller must use the value before returning to JS.
 *
 * @param ctx   JS context
 * @param v     JS value to convert
 * @param out   Output InteropValue (stack-allocated by caller)
 */
static void interop_value_from_js_noalloc(JSContext* ctx, JSValueConst v, InteropValue* out) {
    out->type = INTEROP_TYPE_NULL;
    out->_pad = 0;
    out->v.i64 = 0;
    out->typeHint = NULL;

    if (JS_IsNull(v) || JS_IsUndefined(v)) {
        return;
    }

    if (JS_IsBool(v)) {
        out->type = INTEROP_TYPE_BOOL;
        out->v.b = JS_ToBool(ctx, v) ? 1 : 0;
        return;
    }

    if (JS_IsNumber(v)) {
        double d;
        JS_ToFloat64(ctx, &d, v);

        if (d == (double)(int32_t)d && d >= INT32_MIN && d <= INT32_MAX) {
            out->type = INTEROP_TYPE_INT32;
            out->v.i32 = (int32_t)d;
        } else {
            out->type = INTEROP_TYPE_DOUBLE;
            out->v.f64 = d;
        }
        return;
    }

    // For strings, we get a pointer to QuickJS's internal string.
    // This is NOT allocated - it's valid until we call another JS function.
    // The caller must copy if they need it beyond the callback.
    if (JS_IsString(v)) {
        const char* s = JS_ToCString(ctx, v);
        if (s) {
            out->type = INTEROP_TYPE_STRING;
            out->v.str = s;  // Note: This will be freed after the call
        }
        return;
    }

    // For objects with __csHandle, extract the handle
    if (JS_IsObject(v)) {
        JSValue handleVal = JS_GetPropertyStr(ctx, v, "__csHandle");
        if (!JS_IsUndefined(handleVal) && !JS_IsNull(handleVal)) {
            int32_t handle;
            if (JS_ToInt32(ctx, &handle, handleVal) == 0) {
                out->type = INTEROP_TYPE_OBJECT_HANDLE;
                out->v.handle = handle;
            }
            JS_FreeValue(ctx, handleVal);
            return;
        }
        JS_FreeValue(ctx, handleVal);

        // Try vector patterns (no allocation - just copies floats)
        if (try_convert_vector3(ctx, v, out)) return;
        if (try_convert_color(ctx, v, out)) return;

        // For complex objects, fall back to null (user should use regular invoke)
        // We don't want to allocate JSON strings in the zero-alloc path
    }
}

/**
 * Free string references from interop_value_from_js_noalloc.
 * Must be called for each InteropValue that might contain a string pointer.
 */
static void interop_value_free_string_ref(JSContext* ctx, InteropValue* v) {
    if (v->type == INTEROP_TYPE_STRING && v->v.str) {
        JS_FreeCString(ctx, v->v.str);
        v->v.str = NULL;
    }
}

// Zero-arg invoke
static JSValue js_za_invoke0(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv) {
    if (!g_callbacks.zeroalloc) {
        return JS_ThrowInternalError(ctx, "zeroalloc callback not set");
    }
    if (argc < 1) {
        return JS_ThrowTypeError(ctx, "__zaInvoke0 requires bindingId");
    }

    int32_t bindingId;
    if (JS_ToInt32(ctx, &bindingId, argv[0]) != 0) {
        return JS_ThrowTypeError(ctx, "bindingId must be an integer");
    }

    InteropValue result = {0};
    g_callbacks.zeroalloc(bindingId, NULL, 0, &result);
    return interop_value_to_js(ctx, &result);
}

// 1-arg invoke
static JSValue js_za_invoke1(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv) {
    if (!g_callbacks.zeroalloc) {
        return JS_ThrowInternalError(ctx, "zeroalloc callback not set");
    }
    if (argc < 2) {
        return JS_ThrowTypeError(ctx, "__zaInvoke1 requires bindingId + 1 arg");
    }

    int32_t bindingId;
    if (JS_ToInt32(ctx, &bindingId, argv[0]) != 0) {
        return JS_ThrowTypeError(ctx, "bindingId must be an integer");
    }

    InteropValue args[1];
    interop_value_from_js_noalloc(ctx, argv[1], &args[0]);

    InteropValue result = {0};
    g_callbacks.zeroalloc(bindingId, args, 1, &result);

    interop_value_free_string_ref(ctx, &args[0]);
    return interop_value_to_js(ctx, &result);
}

// 2-arg invoke
static JSValue js_za_invoke2(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv) {
    if (!g_callbacks.zeroalloc) {
        return JS_ThrowInternalError(ctx, "zeroalloc callback not set");
    }
    if (argc < 3) {
        return JS_ThrowTypeError(ctx, "__zaInvoke2 requires bindingId + 2 args");
    }

    int32_t bindingId;
    if (JS_ToInt32(ctx, &bindingId, argv[0]) != 0) {
        return JS_ThrowTypeError(ctx, "bindingId must be an integer");
    }

    InteropValue args[2];
    interop_value_from_js_noalloc(ctx, argv[1], &args[0]);
    interop_value_from_js_noalloc(ctx, argv[2], &args[1]);

    InteropValue result = {0};
    g_callbacks.zeroalloc(bindingId, args, 2, &result);

    interop_value_free_string_ref(ctx, &args[0]);
    interop_value_free_string_ref(ctx, &args[1]);
    return interop_value_to_js(ctx, &result);
}

// 3-arg invoke
static JSValue js_za_invoke3(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv) {
    if (!g_callbacks.zeroalloc) {
        return JS_ThrowInternalError(ctx, "zeroalloc callback not set");
    }
    if (argc < 4) {
        return JS_ThrowTypeError(ctx, "__zaInvoke3 requires bindingId + 3 args");
    }

    int32_t bindingId;
    if (JS_ToInt32(ctx, &bindingId, argv[0]) != 0) {
        return JS_ThrowTypeError(ctx, "bindingId must be an integer");
    }

    InteropValue args[3];
    interop_value_from_js_noalloc(ctx, argv[1], &args[0]);
    interop_value_from_js_noalloc(ctx, argv[2], &args[1]);
    interop_value_from_js_noalloc(ctx, argv[3], &args[2]);

    InteropValue result = {0};
    g_callbacks.zeroalloc(bindingId, args, 3, &result);

    for (int i = 0; i < 3; i++) interop_value_free_string_ref(ctx, &args[i]);
    return interop_value_to_js(ctx, &result);
}

// 4-arg invoke
static JSValue js_za_invoke4(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv) {
    if (!g_callbacks.zeroalloc) {
        return JS_ThrowInternalError(ctx, "zeroalloc callback not set");
    }
    if (argc < 5) {
        return JS_ThrowTypeError(ctx, "__zaInvoke4 requires bindingId + 4 args");
    }

    int32_t bindingId;
    if (JS_ToInt32(ctx, &bindingId, argv[0]) != 0) {
        return JS_ThrowTypeError(ctx, "bindingId must be an integer");
    }

    InteropValue args[4];
    for (int i = 0; i < 4; i++) {
        interop_value_from_js_noalloc(ctx, argv[1 + i], &args[i]);
    }

    InteropValue result = {0};
    g_callbacks.zeroalloc(bindingId, args, 4, &result);

    for (int i = 0; i < 4; i++) interop_value_free_string_ref(ctx, &args[i]);
    return interop_value_to_js(ctx, &result);
}

// 5-arg invoke
static JSValue js_za_invoke5(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv) {
    if (!g_callbacks.zeroalloc) {
        return JS_ThrowInternalError(ctx, "zeroalloc callback not set");
    }
    if (argc < 6) {
        return JS_ThrowTypeError(ctx, "__zaInvoke5 requires bindingId + 5 args");
    }

    int32_t bindingId;
    if (JS_ToInt32(ctx, &bindingId, argv[0]) != 0) {
        return JS_ThrowTypeError(ctx, "bindingId must be an integer");
    }

    InteropValue args[5];
    for (int i = 0; i < 5; i++) {
        interop_value_from_js_noalloc(ctx, argv[1 + i], &args[i]);
    }

    InteropValue result = {0};
    g_callbacks.zeroalloc(bindingId, args, 5, &result);

    for (int i = 0; i < 5; i++) interop_value_free_string_ref(ctx, &args[i]);
    return interop_value_to_js(ctx, &result);
}

// 6-arg invoke
static JSValue js_za_invoke6(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv) {
    if (!g_callbacks.zeroalloc) {
        return JS_ThrowInternalError(ctx, "zeroalloc callback not set");
    }
    if (argc < 7) {
        return JS_ThrowTypeError(ctx, "__zaInvoke6 requires bindingId + 6 args");
    }

    int32_t bindingId;
    if (JS_ToInt32(ctx, &bindingId, argv[0]) != 0) {
        return JS_ThrowTypeError(ctx, "bindingId must be an integer");
    }

    InteropValue args[6];
    for (int i = 0; i < 6; i++) {
        interop_value_from_js_noalloc(ctx, argv[1 + i], &args[i]);
    }

    InteropValue result = {0};
    g_callbacks.zeroalloc(bindingId, args, 6, &result);

    for (int i = 0; i < 6; i++) interop_value_free_string_ref(ctx, &args[i]);
    return interop_value_to_js(ctx, &result);
}

// 7-arg invoke
static JSValue js_za_invoke7(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv) {
    if (!g_callbacks.zeroalloc) {
        return JS_ThrowInternalError(ctx, "zeroalloc callback not set");
    }
    if (argc < 8) {
        return JS_ThrowTypeError(ctx, "__zaInvoke7 requires bindingId + 7 args");
    }

    int32_t bindingId;
    if (JS_ToInt32(ctx, &bindingId, argv[0]) != 0) {
        return JS_ThrowTypeError(ctx, "bindingId must be an integer");
    }

    InteropValue args[7];
    for (int i = 0; i < 7; i++) {
        interop_value_from_js_noalloc(ctx, argv[1 + i], &args[i]);
    }

    InteropValue result = {0};
    g_callbacks.zeroalloc(bindingId, args, 7, &result);

    for (int i = 0; i < 7; i++) interop_value_free_string_ref(ctx, &args[i]);
    return interop_value_to_js(ctx, &result);
}

// 8-arg invoke
static JSValue js_za_invoke8(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv) {
    if (!g_callbacks.zeroalloc) {
        return JS_ThrowInternalError(ctx, "zeroalloc callback not set");
    }
    if (argc < 9) {
        return JS_ThrowTypeError(ctx, "__zaInvoke8 requires bindingId + 8 args");
    }

    int32_t bindingId;
    if (JS_ToInt32(ctx, &bindingId, argv[0]) != 0) {
        return JS_ThrowTypeError(ctx, "bindingId must be an integer");
    }

    InteropValue args[8];
    for (int i = 0; i < 8; i++) {
        interop_value_from_js_noalloc(ctx, argv[1 + i], &args[i]);
    }

    InteropValue result = {0};
    g_callbacks.zeroalloc(bindingId, args, 8, &result);

    for (int i = 0; i < 8; i++) interop_value_free_string_ref(ctx, &args[i]);
    return interop_value_to_js(ctx, &result);
}

static void qjs_init_zeroalloc(JSContext* ctx) {
    JSValue global = JS_GetGlobalObject(ctx);

    JS_SetPropertyStr(ctx, global, "__zaInvoke0", JS_NewCFunction(ctx, js_za_invoke0, "__zaInvoke0", 1));
    JS_SetPropertyStr(ctx, global, "__zaInvoke1", JS_NewCFunction(ctx, js_za_invoke1, "__zaInvoke1", 2));
    JS_SetPropertyStr(ctx, global, "__zaInvoke2", JS_NewCFunction(ctx, js_za_invoke2, "__zaInvoke2", 3));
    JS_SetPropertyStr(ctx, global, "__zaInvoke3", JS_NewCFunction(ctx, js_za_invoke3, "__zaInvoke3", 4));
    JS_SetPropertyStr(ctx, global, "__zaInvoke4", JS_NewCFunction(ctx, js_za_invoke4, "__zaInvoke4", 5));
    JS_SetPropertyStr(ctx, global, "__zaInvoke5", JS_NewCFunction(ctx, js_za_invoke5, "__zaInvoke5", 6));
    JS_SetPropertyStr(ctx, global, "__zaInvoke6", JS_NewCFunction(ctx, js_za_invoke6, "__zaInvoke6", 7));
    JS_SetPropertyStr(ctx, global, "__zaInvoke7", JS_NewCFunction(ctx, js_za_invoke7, "__zaInvoke7", 8));
    JS_SetPropertyStr(ctx, global, "__zaInvoke8", JS_NewCFunction(ctx, js_za_invoke8, "__zaInvoke8", 9));

    JS_FreeValue(ctx, global);
}

// MARK: Init

static void qjs_init_callbacks(QjsContext* qctx) {
    for (int i = 0; i < QJS_MAX_CALLBACKS; i++) {
        qctx->callbacks[i] = JS_UNDEFINED;
    }
    qctx->callback_next = 0;
    qctx->callback_count = 0;
    qctx->callback_free_head = -1;

    JSContext* ctx = qctx->ctx;
    JSValue global = JS_GetGlobalObject(ctx);

    JS_SetPropertyStr(ctx, global, "__registerCallback",
                      JS_NewCFunction(ctx, js_register_callback, "__registerCallback", 1));
    JS_SetPropertyStr(ctx, global, "__unregisterCallback",
                      JS_NewCFunction(ctx, js_unregister_callback, "__unregisterCallback", 1));

    JS_FreeValue(ctx, global);
}

static void qjs_cleanup_callbacks(QjsContext* qctx) {
    if (!qctx || !qctx->ctx) return;

    for (int i = 0; i < QJS_MAX_CALLBACKS; i++) {
        if (!JS_IsUndefined(qctx->callbacks[i])) {
            JS_FreeValue(qctx->ctx, qctx->callbacks[i]);
            qctx->callbacks[i] = JS_UNDEFINED;
        }
    }
    qctx->callback_count = 0;
    qctx->callback_free_head = -1;
}

static void qjs_init_console(JSContext* ctx) {
    JSValue global_obj = JS_GetGlobalObject(ctx);
    JSValue console = JS_NewObject(ctx);
    JSValue log_fn = JS_NewCFunction(ctx, js_console_log, "log", 1);

    JS_SetPropertyStr(ctx, console, "log", JS_DupValue(ctx, log_fn));
    JS_SetPropertyStr(ctx, console, "warn", JS_DupValue(ctx, log_fn));
    JS_SetPropertyStr(ctx, console, "error", JS_DupValue(ctx, log_fn));
    JS_SetPropertyStr(ctx, console, "info", log_fn);

    JS_SetPropertyStr(ctx, global_obj, "console", console);
    JS_FreeValue(ctx, global_obj);
}

static void qjs_init_cs_bridge(JSContext* ctx) {
    JSValue global_obj = JS_GetGlobalObject(ctx);
    JS_SetPropertyStr(ctx, global_obj, "__cs_invoke", JS_NewCFunction(ctx, js_cs_invoke, "__cs_invoke", 6));
    JS_FreeValue(ctx, global_obj);
}

static void qjs_init_release_handle(JSContext* ctx) {
    JSValue global_obj = JS_GetGlobalObject(ctx);
    JS_SetPropertyStr(ctx, global_obj, "__releaseHandle",
                      JS_NewCFunction(ctx, js_release_handle, "__releaseHandle", 1));
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

    QjsContext* wrapper = (QjsContext*)malloc(sizeof(QjsContext));
    if (!wrapper) {
        JS_FreeContext(ctx);
        JS_FreeRuntime(rt);
        return NULL;
    }

    wrapper->magic = QJS_MAGIC;
    wrapper->rt = rt;
    wrapper->ctx = ctx;

    JS_SetContextOpaque(ctx, wrapper);

    qjs_init_console(ctx);
    qjs_init_cs_bridge(ctx);
    qjs_init_release_handle(ctx);
    qjs_init_callbacks(wrapper);
    qjs_init_zeroalloc(ctx);

    return wrapper;
}

QJS_API void qjs_destroy(QjsContext* instance) {
    if (!is_valid(instance)) return;

    JSContext* ctx = instance->ctx;
    JSRuntime* rt = instance->rt;

    instance->magic = 0;
    qjs_cleanup_callbacks(instance);

    instance->ctx = NULL;
    instance->rt = NULL;

    JS_FreeContext(ctx);
    JS_FreeRuntime(rt);
    free(instance);
}

// MARK: Public API

QJS_API int qjs_eval(QjsContext* instance, const char* code, const char* filename, int evalFlags,
                     char* outBuf, int outBufSize) {
    if (!is_valid(instance) || !code) {
        copy_cstring(outBuf, outBufSize, "Invalid context or code");
        return QJS_ERR_INVALID_CTX;
    }

    JSContext* ctx = instance->ctx;
    const char* fname = filename ? filename : "<input>";

    JSValue val = JS_Eval(ctx, code, strlen(code), fname, evalFlags);
    if (JS_IsException(val)) {
        JSValue exc = JS_GetException(ctx);
        format_exception(ctx, exc, outBuf, outBufSize);
        JS_FreeValue(ctx, exc);
        JS_FreeValue(ctx, val);
        return QJS_ERR_EXCEPTION;
    }

    const char* str = JS_ToCString(ctx, val);
    copy_cstring(outBuf, outBufSize, str ? str : "");
    if (str) JS_FreeCString(ctx, str);

    JS_FreeValue(ctx, val);
    return QJS_OK;
}

QJS_API int qjs_invoke_callback(QjsContext* instance, int callbackHandle, InteropValue* args, int argCount,
                                InteropValue* outResult) {
    if (!is_valid(instance)) return QJS_ERR_INVALID_CTX;
    if (callbackHandle < 0 || callbackHandle >= QJS_MAX_CALLBACKS) return QJS_ERR_INVALID_HANDLE;

    JSContext* ctx = instance->ctx;
    JSValue func = instance->callbacks[callbackHandle];

    if (JS_IsUndefined(func) || !JS_IsFunction(ctx, func)) return QJS_ERR_NOT_FUNCTION;

    JSValue* jsArgs = NULL;
    if (argCount > 0 && args) {
        jsArgs = (JSValue*)malloc(sizeof(JSValue) * argCount);
        if (!jsArgs) return QJS_ERR_OUT_OF_MEMORY;

        for (int i = 0; i < argCount; i++) {
            jsArgs[i] = interop_value_to_js(ctx, &args[i]);
        }
    }

    JSValue result = JS_Call(ctx, func, JS_UNDEFINED, argCount, jsArgs);

    if (jsArgs) {
        for (int i = 0; i < argCount; i++) {
            JS_FreeValue(ctx, jsArgs[i]);
        }
        free(jsArgs);
    }

    if (JS_IsException(result)) {
        JSValue exc = JS_GetException(ctx);
        if (g_callbacks.log) {
            char errBuf[QJS_EXCEPTION_BUF_SIZE];
            format_exception(ctx, exc, errBuf, sizeof(errBuf));
            g_callbacks.log(errBuf);
        }
        JS_FreeValue(ctx, exc);
        JS_FreeValue(ctx, result);

        if (outResult) outResult->type = INTEROP_TYPE_NULL;
        return QJS_ERR_EXCEPTION;
    }

    if (outResult) {
        interop_value_from_js(ctx, result, outResult);
    }

    JS_FreeValue(ctx, result);
    return QJS_OK;
}

QJS_API void qjs_run_gc(QjsContext* instance) {
    if (!is_valid(instance)) return;
    JS_RunGC(instance->rt);
}

// Execute all pending jobs (Promise callbacks, microtasks)
// Returns: number of jobs executed, or -1 on error
QJS_API int qjs_execute_pending_jobs(QjsContext* instance) {
    if (!is_valid(instance)) return -1;

    int total = 0;
    JSContext* job_ctx = NULL;
    while (1) {
        int ret = JS_ExecutePendingJob(instance->rt, &job_ctx);
        if (ret < 0) {
            // Error occurred
            JSContext* err_ctx = job_ctx ? job_ctx : instance->ctx;
            JSValue exc = JS_GetException(err_ctx);
            if (g_callbacks.log) {
                char errBuf[QJS_EXCEPTION_BUF_SIZE];
                format_exception(err_ctx, exc, errBuf, sizeof(errBuf));
                g_callbacks.log(errBuf);
            }
            JS_FreeValue(err_ctx, exc);
            return -1;
        }
        if (ret == 0) {
            // No more jobs
            break;
        }
        total++;
    }
    return total;
}