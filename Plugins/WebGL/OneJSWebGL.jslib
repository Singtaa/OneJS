/**
 * OneJS WebGL Bridge
 *
 * This jslib provides the bridge between Unity C# (compiled to WASM) and
 * browser JavaScript. In WebGL builds, JavaScript runs directly in the
 * browser's JS engine (with JIT) rather than in QuickJS compiled to WASM.
 *
 * Target: Unity 6+ (Emscripten 3.1.38+)
 *
 * Architecture:
 * - C# calls qjs_* functions via [DllImport("__Internal")]
 * - jslib implements these functions, delegating to browser JS
 * - JS->C# callbacks use makeDynCall to invoke C# delegates
 *
 * Struct Layouts (WASM32 - all pointers are 4 bytes):
 *
 * InteropType enum (int32):
 *   0=Null, 1=Bool, 2=Int32, 3=Double, 4=String, 5=ObjectHandle,
 *   6=Int64, 7=Float32, 8=Array, 9=JsonObject, 10=Vector3, 11=Vector4
 *
 * InteropInvokeCallKind enum (int32):
 *   0=Ctor, 1=Method, 2=GetProp, 3=SetProp, 4=GetField, 5=SetField,
 *   6=TypeExists, 7=IsEnumType
 *
 * InteropValue (32 bytes, explicit layout):
 *   +0:  type (int32)
 *   +4:  pad (int32)
 *   +8:  union (16 bytes): i32/b/handle/i64/f32/f64/str/vecXYZW
 *   +24: typeHint (pointer)
 *   +28: padding to 32 bytes
 *
 * InteropInvokeRequest (28 bytes, sequential):
 *   +0:  typeName (pointer)
 *   +4:  memberName (pointer)
 *   +8:  callKind (int32)
 *   +12: isStatic (int32)
 *   +16: targetHandle (int32)
 *   +20: argCount (int32)
 *   +24: args (pointer)
 *
 * InteropInvokeResult (40 bytes, sequential):
 *   +0:  returnValue (InteropValue, 32 bytes)
 *   +32: errorCode (int32)
 *   +36: errorMsg (pointer)
 */

var OneJSWebGLLib = {

    // =========================================================================
    // Dependencies - Shared State
    // =========================================================================

    $OneJS: {
        // Runtime state
        initialized: false,
        contextPtr: 0,

        // JS->C# delegate callbacks, registered via the global __registerCallback
        // (WebGL counterpart of the native callback table in quickjs_unity.c)
        callbackRegistry: null,   // Map<int, function>
        nextCallbackId: 1,

        // Callback function pointers (set by C#)
        callbacks: {
            log: null,
            invoke: null,
            releaseHandle: null,
            zeroalloc: null
        },

        // Struct sizes (WASM32)
        SIZEOF_INTEROP_VALUE: 32,
        SIZEOF_INTEROP_REQUEST: 28,
        SIZEOF_INTEROP_RESULT: 40,

        // InteropType enum values
        TYPE_NULL: 0,
        TYPE_BOOL: 1,
        TYPE_INT32: 2,
        TYPE_DOUBLE: 3,
        TYPE_STRING: 4,
        TYPE_OBJECT_HANDLE: 5,
        TYPE_INT64: 6,
        TYPE_FLOAT32: 7,
        TYPE_ARRAY: 8,
        TYPE_JSON_OBJECT: 9,
        TYPE_VECTOR3: 10,
        TYPE_VECTOR4: 11,

        // Initialize the runtime
        init: function() {
            if (OneJS.initialized) return;
            OneJS.initialized = true;

            // Global error handler for debugging
            window.onerror = function(message, source, lineno, colno, error) {
                console.error("[OneJS] Global error:", message, "at", source, lineno + ":" + colno);
                if (error && error.stack) {
                    console.error("[OneJS] Stack:", error.stack);
                }
                return false; // Don't prevent default handling
            };

            window.onunhandledrejection = function(event) {
                console.error("[OneJS] Unhandled promise rejection:", event.reason);
            };

            // Callback registry so the bootstrap's __resolveValue can convert JS
            // functions into { __csCallbackHandle: N } markers (same contract as the
            // native __registerCallback provided by quickjs_unity.c). The bootstrap
            // runs in the page's global scope, so these must be globals.
            OneJS.callbackRegistry = new Map();
            var global = typeof window !== "undefined" ? window : globalThis;
            global.__registerCallback = function(fn) {
                if (typeof fn !== "function") return -1;
                var id = OneJS.nextCallbackId++;
                OneJS.callbackRegistry.set(id, fn);
                return id;
            };
            global.__unregisterCallback = function(id) {
                if (OneJS.callbackRegistry) OneJS.callbackRegistry.delete(id);
            };
        },

        // =====================================================================
        // Marshal JS value to InteropValue on WASM heap
        // Returns the allocated pointer (caller must free)
        // =====================================================================
        marshalValue: function(value, valuePtr) {
            // Zero out the struct first
            for (var i = 0; i < 32; i += 4) {
                HEAP32[(valuePtr + i) >> 2] = 0;
            }

            if (value === null || value === undefined) {
                HEAP32[valuePtr >> 2] = OneJS.TYPE_NULL;
                return;
            }

            var type = typeof value;

            if (type === "boolean") {
                HEAP32[valuePtr >> 2] = OneJS.TYPE_BOOL;
                HEAP32[(valuePtr + 8) >> 2] = value ? 1 : 0;
            }
            else if (type === "number") {
                if (Number.isInteger(value) && value >= -2147483648 && value <= 2147483647) {
                    HEAP32[valuePtr >> 2] = OneJS.TYPE_INT32;
                    HEAP32[(valuePtr + 8) >> 2] = value;
                } else {
                    HEAP32[valuePtr >> 2] = OneJS.TYPE_DOUBLE;
                    HEAPF64[(valuePtr + 8) >> 3] = value;
                }
            }
            else if (type === "string") {
                HEAP32[valuePtr >> 2] = OneJS.TYPE_STRING;
                var strLen = lengthBytesUTF8(value) + 1;
                var strPtr = _malloc(strLen);
                stringToUTF8(value, strPtr, strLen);
                HEAPU32[(valuePtr + 8) >> 2] = strPtr;
            }
            else if (type === "object") {
                // Check for C# object handle
                if (value.__csHandle !== undefined && value.__csHandle !== 0) {
                    HEAP32[valuePtr >> 2] = OneJS.TYPE_OBJECT_HANDLE;
                    HEAP32[(valuePtr + 8) >> 2] = value.__csHandle;
                }
                // Serialized C# struct (__type marker) or type reference object.
                // Must be checked before the vector duck-typing: a JSON-round-tripped
                // struct like UIElements.Translate has x/y/z members that are nested
                // structs (Length), and the HEAPF32 writes below would mangle them
                // into NaN. C# rebuilds these from JSON via DeserializeFromDict.
                else if (value.__type !== undefined || value.__csTypeRef) {
                    var json = JSON.stringify(value);
                    HEAP32[valuePtr >> 2] = OneJS.TYPE_JSON_OBJECT;
                    var jsonLen = lengthBytesUTF8(json) + 1;
                    var jsonPtr = _malloc(jsonLen);
                    stringToUTF8(json, jsonPtr, jsonLen);
                    HEAPU32[(valuePtr + 8) >> 2] = jsonPtr;
                }
                // Check for Vector3-like {x, y, z} - numeric members only; objects
                // with non-numeric x/y/z fall through to the generic JSON branch
                else if (typeof value.x === "number" && typeof value.y === "number" && typeof value.z === "number" && value.w === undefined) {
                    HEAP32[valuePtr >> 2] = OneJS.TYPE_VECTOR3;
                    HEAPF32[(valuePtr + 8) >> 2] = value.x;
                    HEAPF32[(valuePtr + 12) >> 2] = value.y;
                    HEAPF32[(valuePtr + 16) >> 2] = value.z;
                }
                // Check for Vector4-like {x, y, z, w}
                else if (typeof value.x === "number" && typeof value.y === "number" && typeof value.z === "number" && typeof value.w === "number") {
                    HEAP32[valuePtr >> 2] = OneJS.TYPE_VECTOR4;
                    HEAPF32[(valuePtr + 8) >> 2] = value.x;
                    HEAPF32[(valuePtr + 12) >> 2] = value.y;
                    HEAPF32[(valuePtr + 16) >> 2] = value.z;
                    HEAPF32[(valuePtr + 20) >> 2] = value.w;
                }
                // Check for Color-like {r, g, b, a}
                else if (typeof value.r === "number" && typeof value.g === "number" && typeof value.b === "number") {
                    HEAP32[valuePtr >> 2] = OneJS.TYPE_VECTOR4;
                    HEAPF32[(valuePtr + 8) >> 2] = value.r;
                    HEAPF32[(valuePtr + 12) >> 2] = value.g;
                    HEAPF32[(valuePtr + 16) >> 2] = value.b;
                    HEAPF32[(valuePtr + 20) >> 2] = typeof value.a === "number" ? value.a : 1.0;
                    // Set typeHint to "color"
                    var hintPtr = _malloc(6);
                    stringToUTF8("color", hintPtr, 6);
                    HEAPU32[(valuePtr + 24) >> 2] = hintPtr;
                }
                // Generic object - serialize as JSON
                else {
                    try {
                        var json = JSON.stringify(value);
                        HEAP32[valuePtr >> 2] = OneJS.TYPE_JSON_OBJECT;
                        var jsonLen = lengthBytesUTF8(json) + 1;
                        var jsonPtr = _malloc(jsonLen);
                        stringToUTF8(json, jsonPtr, jsonLen);
                        HEAPU32[(valuePtr + 8) >> 2] = jsonPtr;
                    } catch (e) {
                        // Can't serialize - treat as null
                        HEAP32[valuePtr >> 2] = OneJS.TYPE_NULL;
                    }
                }
            }
            else {
                if (type === "function") {
                    // Functions must be converted to { __csCallbackHandle: N } by the
                    // bootstrap's __resolveValue before crossing the bridge - a bare
                    // function here means a path bypassed it
                    console.error("[OneJS] marshalValue: cannot marshal a JS function directly; " +
                        "it should have been registered via __registerCallback. Marshaling as null.");
                }
                HEAP32[valuePtr >> 2] = OneJS.TYPE_NULL;
            }
        },

        // =====================================================================
        // Unmarshal InteropValue from WASM heap to JS value
        // =====================================================================
        unmarshalValue: function(valuePtr) {
            var type = HEAP32[valuePtr >> 2];

            switch (type) {
                case OneJS.TYPE_NULL:
                    return null;

                case OneJS.TYPE_BOOL:
                    return HEAP32[(valuePtr + 8) >> 2] !== 0;

                case OneJS.TYPE_INT32:
                    return HEAP32[(valuePtr + 8) >> 2];

                case OneJS.TYPE_INT64:
                    // Read as two 32-bit values and combine
                    var lo = HEAPU32[(valuePtr + 8) >> 2];
                    var hi = HEAP32[(valuePtr + 12) >> 2];
                    return hi * 0x100000000 + lo;

                case OneJS.TYPE_FLOAT32:
                    return HEAPF32[(valuePtr + 8) >> 2];

                case OneJS.TYPE_DOUBLE:
                    return HEAPF64[(valuePtr + 8) >> 3];

                case OneJS.TYPE_STRING:
                    var strPtr = HEAPU32[(valuePtr + 8) >> 2];
                    return strPtr ? UTF8ToString(strPtr) : null;

                case OneJS.TYPE_OBJECT_HANDLE:
                    var handle = HEAP32[(valuePtr + 8) >> 2];
                    var typeHintPtr = HEAPU32[(valuePtr + 24) >> 2];
                    var typeHint = typeHintPtr ? UTF8ToString(typeHintPtr) : "";
                    // Return object with handle - will be wrapped by bootstrap
                    return { __csHandle: handle, __csType: typeHint };

                case OneJS.TYPE_VECTOR3:
                    return {
                        x: HEAPF32[(valuePtr + 8) >> 2],
                        y: HEAPF32[(valuePtr + 12) >> 2],
                        z: HEAPF32[(valuePtr + 16) >> 2]
                    };

                case OneJS.TYPE_VECTOR4:
                    var hintPtr = HEAPU32[(valuePtr + 24) >> 2];
                    var hint = hintPtr ? UTF8ToString(hintPtr) : "";
                    if (hint === "color") {
                        return {
                            r: HEAPF32[(valuePtr + 8) >> 2],
                            g: HEAPF32[(valuePtr + 12) >> 2],
                            b: HEAPF32[(valuePtr + 16) >> 2],
                            a: HEAPF32[(valuePtr + 20) >> 2]
                        };
                    }
                    return {
                        x: HEAPF32[(valuePtr + 8) >> 2],
                        y: HEAPF32[(valuePtr + 12) >> 2],
                        z: HEAPF32[(valuePtr + 16) >> 2],
                        w: HEAPF32[(valuePtr + 20) >> 2]
                    };

                case OneJS.TYPE_JSON_OBJECT:
                    var jsonPtr = HEAPU32[(valuePtr + 8) >> 2];
                    if (jsonPtr) {
                        try {
                            return JSON.parse(UTF8ToString(jsonPtr));
                        } catch (e) {
                            return null;
                        }
                    }
                    return null;

                default:
                    return null;
            }
        },

        // =====================================================================
        // Free any allocated memory in an InteropValue
        // =====================================================================
        freeValueMemory: function(valuePtr) {
            var type = HEAP32[valuePtr >> 2];
            if (type === OneJS.TYPE_STRING || type === OneJS.TYPE_JSON_OBJECT) {
                var strPtr = HEAPU32[(valuePtr + 8) >> 2];
                if (strPtr) _free(strPtr);
            }
            var hintPtr = HEAPU32[(valuePtr + 24) >> 2];
            if (hintPtr) _free(hintPtr);
        },

        // =====================================================================
        // Main invoke function - called from JS to invoke C# methods
        // =====================================================================
        invokeCs: function(typeName, memberName, callKind, isStatic, targetHandle, args) {
            if (!OneJS.callbacks.invoke) {
                console.error("[OneJS] Invoke callback not set");
                return null;
            }

            // Allocate request struct
            var reqPtr = _malloc(OneJS.SIZEOF_INTEROP_REQUEST);

            // Allocate and write typeName string
            var typeNameLen = lengthBytesUTF8(typeName) + 1;
            var typeNamePtr = _malloc(typeNameLen);
            stringToUTF8(typeName, typeNamePtr, typeNameLen);

            // Allocate and write memberName string
            var memberNameLen = lengthBytesUTF8(memberName) + 1;
            var memberNamePtr = _malloc(memberNameLen);
            stringToUTF8(memberName, memberNamePtr, memberNameLen);

            // Allocate args array
            var argCount = args ? args.length : 0;
            var argsPtr = 0;
            if (argCount > 0) {
                argsPtr = _malloc(argCount * OneJS.SIZEOF_INTEROP_VALUE);
                for (var i = 0; i < argCount; i++) {
                    OneJS.marshalValue(args[i], argsPtr + i * OneJS.SIZEOF_INTEROP_VALUE);
                }
            }

            // Fill request struct
            HEAPU32[(reqPtr + 0) >> 2] = typeNamePtr;    // typeName
            HEAPU32[(reqPtr + 4) >> 2] = memberNamePtr;  // memberName
            HEAP32[(reqPtr + 8) >> 2] = callKind;        // callKind
            HEAP32[(reqPtr + 12) >> 2] = isStatic;       // isStatic
            HEAP32[(reqPtr + 16) >> 2] = targetHandle;   // targetHandle
            HEAP32[(reqPtr + 20) >> 2] = argCount;       // argCount
            HEAPU32[(reqPtr + 24) >> 2] = argsPtr;       // args

            // Allocate result struct
            var resPtr = _malloc(OneJS.SIZEOF_INTEROP_RESULT);
            for (var i = 0; i < OneJS.SIZEOF_INTEROP_RESULT; i += 4) {
                HEAP32[(resPtr + i) >> 2] = 0;
            }

            // Call C# dispatch function
            // Signature: void(IntPtr ctx, InteropInvokeRequest*, InteropInvokeResult*)
            {{{ makeDynCall("viii", "OneJS.callbacks.invoke") }}}(
                OneJS.contextPtr,
                reqPtr,
                resPtr
            );

            // Read result
            var errorCode = HEAP32[(resPtr + 32) >> 2];
            var result = null;

            if (errorCode === 0) {
                result = OneJS.unmarshalValue(resPtr);
            } else {
                var errorMsgPtr = HEAPU32[(resPtr + 36) >> 2];
                if (errorMsgPtr) {
                    console.error("[OneJS] C# invoke error:", UTF8ToString(errorMsgPtr));
                }
            }

            // Free allocated memory
            _free(typeNamePtr);
            _free(memberNamePtr);
            if (argsPtr) {
                for (var i = 0; i < argCount; i++) {
                    OneJS.freeValueMemory(argsPtr + i * OneJS.SIZEOF_INTEROP_VALUE);
                }
                _free(argsPtr);
            }

            // Free result string memory if any (C# allocated, we free)
            var resultType = HEAP32[resPtr >> 2];
            if (resultType === OneJS.TYPE_STRING) {
                var strPtr = HEAPU32[(resPtr + 8) >> 2];
                if (strPtr) _free(strPtr);
            }
            var typeHintPtr = HEAPU32[(resPtr + 24) >> 2];
            if (typeHintPtr) _free(typeHintPtr);

            _free(reqPtr);
            _free(resPtr);

            return result;
        },

        // Called from JS to release a C# handle
        releaseHandle: function(handle) {
            if (!OneJS.callbacks.releaseHandle || !handle) return;
            {{{ makeDynCall("vi", "OneJS.callbacks.releaseHandle") }}}(handle);
        }
    },

    // =========================================================================
    // Context Management
    // =========================================================================

    qjs_create__deps: ["$OneJS"],
    qjs_create: function() {
        OneJS.init();
        OneJS.contextPtr = 1; // Dummy context pointer
        return 1;
    },

    qjs_destroy__deps: ["$OneJS"],
    qjs_destroy: function(ctx) {
        OneJS.contextPtr = 0;
        // Drop all registered callbacks (mirrors C#'s ClearDelegateCache). Keep
        // nextCallbackId monotonic so stale C#-side delegate caches can never
        // alias a handle from a new context.
        if (OneJS.callbackRegistry) OneJS.callbackRegistry.clear();
    },

    // =========================================================================
    // Evaluation
    // =========================================================================

    qjs_eval__deps: ["$OneJS"],
    qjs_eval: function(ctx, codePtr, filenamePtr, flags, outBuf, outBufSize) {
        var code = UTF8ToString(codePtr);
        var filename = UTF8ToString(filenamePtr);

        try {
            var result = (0, eval)(code);

            var resultStr = "";
            if (result !== undefined && result !== null) {
                if (typeof result === "object") {
                    try {
                        resultStr = JSON.stringify(result);
                    } catch (e) {
                        resultStr = String(result);
                    }
                } else {
                    resultStr = String(result);
                }
            }

            stringToUTF8(resultStr, outBuf, outBufSize);
            return 0;

        } catch (e) {
            var errorMsg = e.message || String(e);
            if (e.stack) {
                console.error("[OneJS] JS Error in " + filename + ":", e.stack);
            }
            if (filename && filename !== "<input>") {
                errorMsg = filename + ": " + errorMsg;
            }
            stringToUTF8(errorMsg, outBuf, outBufSize);
            return 1;
        }
    },

    // =========================================================================
    // Job Queue / Promises
    // =========================================================================

    qjs_execute_pending_jobs__deps: ["$OneJS"],
    qjs_execute_pending_jobs: function(ctx) {
        return 0;
    },

    // =========================================================================
    // Garbage Collection
    // =========================================================================

    qjs_run_gc__deps: ["$OneJS"],
    qjs_run_gc: function(ctx) {
        // No-op
    },

    // =========================================================================
    // Callback Registration
    // =========================================================================

    qjs_set_cs_log_callback__deps: ["$OneJS"],
    qjs_set_cs_log_callback: function(callbackPtr) {
        OneJS.callbacks.log = callbackPtr;
    },

    qjs_set_cs_invoke_callback__deps: ["$OneJS"],
    qjs_set_cs_invoke_callback: function(callbackPtr) {
        OneJS.callbacks.invoke = callbackPtr;

        // Wire up __cs_invoke for bootstrap.js
        var global = typeof window !== "undefined" ? window : globalThis;
        global.__cs_invoke = OneJS.invokeCs;
        global.__releaseHandle = OneJS.releaseHandle;
    },

    qjs_set_cs_release_handle_callback__deps: ["$OneJS"],
    qjs_set_cs_release_handle_callback: function(callbackPtr) {
        OneJS.callbacks.releaseHandle = callbackPtr;

        var global = typeof window !== "undefined" ? window : globalThis;
        global.__releaseHandle = OneJS.releaseHandle;
    },

    // Zero-alloc fast-path callback registration.
    //
    // On native, the QuickJS runtime exposes __zaInvoke0..N JS functions that
    // route through this callback. We replicate the same surface on WebGL so
    // code that uses the fast path (onejs-unity GPU compute, input reader)
    // works without changes - and so the reconciler can opt-in later.
    //
    // The shims marshal args directly into the WASM heap, call the C# zero-
    // alloc dispatcher via dynCall, and unmarshal the result. Allocation is
    // per-call (via _malloc/_free) - slower than native's stack-allocated
    // path but still far cheaper than __cs.invoke since there's no JSON
    // marshalling or method-resolution on the C# side.
    qjs_set_cs_zeroalloc_callback__deps: ["$OneJS"],
    qjs_set_cs_zeroalloc_callback: function(callbackPtr) {
        OneJS.callbacks.zeroalloc = callbackPtr;

        var invoke = function(bindingId, args) {
            if (!OneJS.callbacks.zeroalloc) return null;

            var argCount = args.length;
            var argsPtr = argCount > 0 ? _malloc(argCount * OneJS.SIZEOF_INTEROP_VALUE) : 0;
            var resultPtr = _malloc(OneJS.SIZEOF_INTEROP_VALUE);

            // Zero result struct
            for (var i = 0; i < OneJS.SIZEOF_INTEROP_VALUE; i += 4) {
                HEAP32[(resultPtr + i) >> 2] = 0;
            }

            for (var i = 0; i < argCount; i++) {
                OneJS.marshalValue(args[i], argsPtr + i * OneJS.SIZEOF_INTEROP_VALUE);
            }

            // Signature: void(int bindingId, InteropValue* args, int argCount, InteropValue* outResult)
            {{{ makeDynCall("viiii", "OneJS.callbacks.zeroalloc") }}}(
                bindingId, argsPtr, argCount, resultPtr
            );

            var result = OneJS.unmarshalValue(resultPtr);

            if (argsPtr) {
                for (var i = 0; i < argCount; i++) {
                    OneJS.freeValueMemory(argsPtr + i * OneJS.SIZEOF_INTEROP_VALUE);
                }
                _free(argsPtr);
            }
            // Free strings/JSON allocated by C# in the result (mirrors invokeCs)
            var rt = HEAP32[resultPtr >> 2];
            if (rt === OneJS.TYPE_STRING || rt === OneJS.TYPE_JSON_OBJECT) {
                var sp = HEAPU32[(resultPtr + 8) >> 2];
                if (sp) _free(sp);
            }
            var hp = HEAPU32[(resultPtr + 24) >> 2];
            if (hp) _free(hp);
            _free(resultPtr);

            return result;
        };

        var g = typeof window !== "undefined" ? window : globalThis;
        // One implementation services all arities - JS doesn't enforce param
        // counts, and the variadic form keeps the shim small.
        g.__zaInvoke0 = function(bindingId) { return invoke(bindingId, []); };
        g.__zaInvoke1 = function(bindingId, a0) { return invoke(bindingId, [a0]); };
        g.__zaInvoke2 = function(bindingId, a0, a1) { return invoke(bindingId, [a0, a1]); };
        g.__zaInvoke3 = function(bindingId, a0, a1, a2) { return invoke(bindingId, [a0, a1, a2]); };
        g.__zaInvoke4 = function(bindingId, a0, a1, a2, a3) { return invoke(bindingId, [a0, a1, a2, a3]); };
        g.__zaInvoke5 = function(bindingId, a0, a1, a2, a3, a4) { return invoke(bindingId, [a0, a1, a2, a3, a4]); };
        g.__zaInvoke6 = function(bindingId, a0, a1, a2, a3, a4, a5) { return invoke(bindingId, [a0, a1, a2, a3, a4, a5]); };
        g.__zaInvoke7 = function(bindingId, a0, a1, a2, a3, a4, a5, a6) { return invoke(bindingId, [a0, a1, a2, a3, a4, a5, a6]); };
        g.__zaInvoke8 = function(bindingId, a0, a1, a2, a3, a4, a5, a6, a7) { return invoke(bindingId, [a0, a1, a2, a3, a4, a5, a6, a7]); };
    },

    // =========================================================================
    // Callback Invocation (C# -> JS)
    // =========================================================================

    qjs_invoke_callback__deps: ["$OneJS"],
    qjs_invoke_callback: function(ctx, callbackHandle, argsPtr, argCount, outResultPtr) {
        // Invokes a JS callback registered via __registerCallback. This is how every
        // C#-side delegate wrapper (CreateDelegateWrapper) and the lifecycle path
        // (onPlay/onStop) call back into JS. RAF/timer callbacks never come through
        // here on WebGL (those use native browser APIs).
        var fn = OneJS.callbackRegistry ? OneJS.callbackRegistry.get(callbackHandle) : undefined;
        if (!fn) {
            // Stale handle: log rather than fail, so a C# event raise chain isn't aborted
            console.error("[OneJS] qjs_invoke_callback: no callback registered for handle " + callbackHandle);
            return 0;
        }

        // Argument memory is owned by the C# caller (InvokeCallback/NoAlloc allocate
        // and free it) - must not be freed here.
        var args = new Array(argCount);
        for (var i = 0; i < argCount; i++) {
            args[i] = OneJS.unmarshalValue(argsPtr + i * OneJS.SIZEOF_INTEROP_VALUE);
        }

        try {
            var result = fn.apply(null, args);
            // outResultPtr is null for the InvokeCallbackNoAlloc family
            if (outResultPtr) OneJS.marshalValue(result, outResultPtr);
            return 0;
        } catch (e) {
            // Contain handler errors: a non-zero return throws inside the C# raise,
            // which would abort remaining C# listeners on the same event
            console.error("[OneJS] Callback error:", e && e.stack ? e.stack : e);
            return 0;
        }
    },

    // =========================================================================
    // Fast Event Dispatch (WebGL optimization)
    // =========================================================================

    qjs_dispatch_event__deps: ["$OneJS"],
    qjs_dispatch_event: function(elementHandle, eventTypePtr, eventDataPtr) {
        // Fast path for event dispatch - avoids eval overhead. Returns the suppression-flags
        // bitmask from __dispatchEvent (bit0=propagationStopped, bit1=defaultPrevented), or 0.
        var eventType = UTF8ToString(eventTypePtr);
        var eventDataJson = UTF8ToString(eventDataPtr);

        try {
            var eventData = eventDataJson ? JSON.parse(eventDataJson) : {};
            if (typeof __dispatchEvent === "function") {
                return __dispatchEvent(elementHandle, eventType, eventData) | 0;
            }
        } catch (e) {
            console.error("[OneJS] Event dispatch error:", e);
        }
        return 0;
    }
};

autoAddDeps(OneJSWebGLLib, "$OneJS");
mergeInto(LibraryManager.library, OneJSWebGLLib);
