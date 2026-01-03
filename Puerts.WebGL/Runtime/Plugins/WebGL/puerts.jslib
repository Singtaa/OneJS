var exportDLL = {
    InitPuertsWebGL: function () {
        var global = typeof global != 'undefined' ? global : window;
        if (!global.PuertsWebGL) {
            throw new Error('cannot found PuertsWebGL script. please find some way to load puerts-runtime.js');
        }

        // ---------------------------------------------------------------------------  
        // Unity 6 / Emscripten 3.x compatibility  
        // ---------------------------------------------------------------------------  
        if (typeof updateGlobalBufferAndViews === "undefined") {  
            var updateGlobalBufferAndViews = function (buf) {  
                if (typeof updateMemoryViews === "function") {  
                    return updateMemoryViews(buf);  
                }  
                // Fallback – rebuild the typed-array views ourselves.  
                Module.HEAP8  = HEAP8  = new Int8Array(buf);  
                Module.HEAPU8 = HEAPU8 = new Uint8Array(buf);  
                Module.HEAP16 = HEAP16 = new Int16Array(buf);  
                Module.HEAP32 = HEAP32 = new Int32Array(buf);  
                Module.HEAPU16 = HEAPU16 = new Uint16Array(buf);  
                Module.HEAPU32 = HEAPU32 = new Uint32Array(buf);  
                Module.HEAPF32 = HEAPF32 = new Float32Array(buf);  
                Module.HEAPF64 = HEAPF64 = new Float64Array(buf);  
            };  
        }  

        if (typeof UTF16ToString === "undefined") {  
            if (Module && Module.UTF16ToString) {  
                var UTF16ToString = Module.UTF16ToString  
            } else {  
                const decoder = new TextDecoder("utf-16le")  
                var UTF16ToString = function (ptr) {  
                    let end = ptr  
                    while (HEAPU16[end >> 1] !== 0) end += 2  
                    return decoder.decode(HEAPU8.subarray(ptr, end))  
                }  
            }  
        }  
        if (typeof stringToUTF16 === "undefined") {  
            var stringToUTF16 = function (str, outPtr, maxBytes) {  
                const maxChars = maxBytes ? (maxBytes >> 1) - 1 : str.length  
                for (let i = 0; i < Math.min(str.length, maxChars); i++) HEAPU16[(outPtr >> 1) + i] = str.charCodeAt(i)  
                HEAPU16[(outPtr >> 1) + Math.min(str.length, maxChars)] = 0  
            }  
        }  
        if (typeof lengthBytesUTF16 === "undefined") {  
            var lengthBytesUTF16 = str => (str.length << 1) + 2  
        }  
        
        if (typeof _setTempRet0 === "undefined") {  
            var __tempRet0 = 0  
            var _setTempRet0 = v => { __tempRet0 = v | 0 }  
        }  
        if (typeof _getTempRet0 === "undefined") {  
            var _getTempRet0 = () => __tempRet0 | 0  
        }  

        // Unity 6.3+ compatibility: helper to get wasmTable from multiple sources
        var getWasmTable = function() {
            if (typeof wasmTable !== 'undefined') return wasmTable;
            if (Module && Module.wasmTable) return Module.wasmTable;
            if (Module && Module.asm && Module.asm.__indirect_function_table) return Module.asm.__indirect_function_table;
            return null;
        };

        if (typeof addFunction === "undefined") {
            var addFunction = function (jsFunc /*, sig */) {
                const table = getWasmTable();
                if (!table) throw new Error('wasmTable not found for addFunction');
                const idx = table.length;
                table.grow(1);
                table.set(idx, jsFunc);
                return idx;
            }
        }
        if (typeof removeFunction === "undefined") {
            var removeFunction = function (idx) {
                const table = getWasmTable();
                if (!table) return; // silently fail if no table
                // Emscripten no longer shrinks the table – just replace entry with a noop
                table.set(idx, () => {});
            }
        }

        let oldUpdateGlobalBufferAndViews = updateGlobalBufferAndViews;
        updateGlobalBufferAndViews = function (buf) {
            oldUpdateGlobalBufferAndViews(buf);
            global.PuertsWebGL.updateGlobalBufferAndViews(
                HEAP8,
                HEAPU8,
                HEAP32,
                HEAPF32,
                HEAPF64
            );
        }

        // Unity 6.3+ may call updateMemoryViews directly instead of updateGlobalBufferAndViews
        if (typeof updateMemoryViews === "function") {
            let oldUpdateMemoryViews = updateMemoryViews;
            updateMemoryViews = function (buf) {
                oldUpdateMemoryViews(buf);
                global.PuertsWebGL.updateGlobalBufferAndViews(
                    HEAP8,
                    HEAPU8,
                    HEAP32,
                    HEAPF32,
                    HEAPF64
                );
            }
        }

        global.PuertsWebGL.Init({
            UTF8ToString,
            UTF16ToString,
            _malloc,
            _free,
            _setTempRet0,
            stringToUTF8,
            lengthBytesUTF8,
            stringToUTF16,
            lengthBytesUTF16,
            stackAlloc,
            stackSave,
            stackRestore,
            getWasmTableEntry: (typeof getWasmTableEntry != 'undefined') ? getWasmTableEntry : function(funcPtr) {
                var table = getWasmTable();
                if (table) {
                    return table.get(funcPtr);
                }
                throw new Error('wasmTable not found - Unity 6.3 may require WebAssembly.Table to be disabled in Player Settings');
            },
            addFunction,
            removeFunction,
            _CallCSharpFunctionCallback: Module._CallCSharpFunctionCallback,
            _CallCSharpConstructorCallback: Module._CallCSharpConstructorCallback,
            _CallCSharpDestructorCallback: Module._CallCSharpDestructorCallback,
            InjectPapiGLNativeImpl: Module._InjectPapiGLNativeImpl,
            PApiCallbackWithScope: Module._PApiCallbackWithScope,
            PApiConstructorWithScope: Module._PApiConstructorWithScope,
            WasmAdd: Module._WasmAdd,
            IndirectWasmAdd: Module._IndirectWasmAdd,
            GetWasmAddPtr: Module._GetWasmAddPtr,
            
            HEAP8,
            HEAPU8,
            HEAP32,
            HEAPF32,
            HEAPF64,
        });
        global.PuertsWebGL.inited = true;
    },
};


[
    "GetLibVersion",
    "GetApiLevel",
    "GetLibBackend",
    "CreateJSEngine",
    "CreateJSEngineWithExternalEnv",
    "DestroyJSEngine",
    "SetGlobalFunction",
    "GetLastExceptionInfo",
    "LowMemoryNotification",
    "IdleNotificationDeadline",
    "RequestMinorGarbageCollectionForTesting",
    "RequestFullGarbageCollectionForTesting",
    "SetGeneralDestructor",
    "Eval",
    "ClearModuleCache",
    "GetModuleExecutor",
    "GetJSObjectValueGetter",
    "GetJSStackTrace",
    "_RegisterClass",
    "RegisterStruct",
    "RegisterFunction",
    "RegisterProperty",
    "ReturnClass",
    "ReturnObject",
    "ReturnNumber",
    "ReturnString",
    "ReturnBigInt",
    "ReturnBoolean",
    "ReturnDate",
    "ReturnNull",
    "ReturnFunction",
    "ReturnJSObject",
    "ReturnArrayBuffer",
    "ReturnCSharpFunctionCallback",
    "ReturnCSharpFunctionCallback2",
    // "GetArgumentType",
//    "GetArgumentValue",
    // "GetJsValueType",
    "GetTypeIdFromValue",
    // "GetNumberFromValue",
    // "GetDateFromValue",
    // "GetStringFromValue",
    // "GetBooleanFromValue",
    // "ValueIsBigInt",
    // "GetBigIntFromValue",
    // "GetObjectFromValue",
    // "GetFunctionFromValue",
    // "GetJSObjectFromValue",
    // "GetArrayBufferFromValue",
    "SetNumberToOutValue",
    "SetDateToOutValue",
    "SetStringToOutValue",
    "SetBooleanToOutValue",
    "SetBigIntToOutValue",
    "SetObjectToOutValue",
    "SetNullToOutValue",
    "SetArrayBufferToOutValue",
    "ThrowException",
    "PushNullForJSFunction",
    "PushDateForJSFunction",
    "PushBooleanForJSFunction",
    "PushBigIntForJSFunction",
    "PushStringForJSFunction",
    "__PushStringForJSFunction",
    "PushNumberForJSFunction",
    "PushObjectForJSFunction",
    "PushJSFunctionForJSFunction",
    "PushJSObjectForJSFunction",
    "PushArrayBufferForJSFunction",
    "SetPushJSFunctionArgumentsCallback",
    "InvokeJSFunction",
    "GetFunctionLastExceptionInfo",
    "ReleaseJSFunction",
    "ReleaseJSObject",
    "GetResultType",
    "GetNumberFromResult",
    "GetDateFromResult",
    "GetStringFromResult",
    "GetBooleanFromResult",
    "ResultIsBigInt",
    "GetBigIntFromResult",
    "GetObjectFromResult",
    "GetTypeIdFromResult",
    "GetFunctionFromResult",
    "GetJSObjectFromResult",
    "GetArrayBufferFromResult",
    "ResetResult",
    "CreateInspector",
    "DestroyInspector",
    "InspectorTick",
    "LogicTick",
    "SetLogCallback",
    "GetWebGLFFIApi",
    "GetWebGLPapiEnvRef",
    "GetQjsFFIApi", // declare for compile
    "GetQjsPapiEnvRef", // declare for compile
    "GetRegsterApi",
    "pesapi_alloc_property_descriptors",
    "pesapi_define_class",
    "pesapi_get_class_data",
    "pesapi_on_class_not_found",
    "pesapi_set_method_info",
    "pesapi_set_property_info",
    "pesapi_trace_native_object_lifecycle",
    "pesapi_create_array_js",
    "pesapi_create_object_js",
    "pesapi_create_function_js",
    "pesapi_create_class_js",
    "pesapi_get_array_length_js",
    "pesapi_native_object_to_value_js",
    "pesapi_throw_by_string_js",
    "pesapi_open_scope_placement_js",
    "pesapi_has_caught_js",
    "pesapi_get_exception_as_string_js",
    "pesapi_close_scope_placement_js",
    "pesapi_create_value_ref_js",
    "pesapi_release_value_ref_js",
    "pesapi_get_value_from_ref_js",
    "pesapi_get_property_js",
    "pesapi_set_property_js",
    "pesapi_get_private_js",
    "pesapi_set_private_js",
    "pesapi_get_property_uint32_js",
    "pesapi_set_property_uint32_js",
    "pesapi_call_function_js",
    "pesapi_eval_js",
    "pesapi_global_js",
    "pesapi_set_env_private_js"
].forEach(function (methodName) {

    exportDLL[methodName] = new Function(
        "var global = typeof global != 'undefined' ? global : window; " +
        "if (!global.PuertsWebGL) throw new Error('cannot found PuertsWebGL script. please find some way to load puerts-runtime.js');" +
        "if (!global.PuertsWebGL.inited) throw new Error('please use Puerts.WebGL.MainEnv.Get() to create JsEnv'); " +
        "if (global.PuertsWebGL.debug) console.log('WebGL DLL:" + methodName + "'); "+
        "return global.PuertsWebGL['" + methodName + "'].apply(this, arguments)"
    );
})

mergeInto(LibraryManager.library, exportDLL);