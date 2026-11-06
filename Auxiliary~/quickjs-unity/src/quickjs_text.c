/*
 * TextDecoder and TextEncoder for QuickJS, UTF-8 only, as the encoding
 * standard's defaults.
 *
 * QuickJS has neither, and code written for the browser reaches for them
 * first: wasm-bindgen's glue makes a TextDecoder when it loads, so Rapier's
 * failed in OneJS before anything ran. In C rather than in the bootstrap, so a
 * string out of WebAssembly memory is one crossing and not a loop in the
 * interpreter.
 *
 *     new TextDecoder(label = "utf-8", { fatal, ignoreBOM }).decode(bytes)
 *     new TextEncoder().encode(string)                 -> Uint8Array
 *     new TextEncoder().encodeInto(string, uint8Array) -> { read, written }
 *
 * Other encodings are refused at construction with a RangeError, as a browser
 * refuses a label it does not know. `stream` is not supported.
 */
#include <string.h>
#include "quickjs_compat.h"
#include "quickjs_text.h"

static JSClassID g_decoder_class = 0;

typedef struct {
    int fatal;
    int ignore_bom;
} Decoder;

static void decoder_finalizer(JSRuntime* rt, JSValue val) {
    js_free_rt(rt, JS_GetOpaque(val, g_decoder_class));
}

static JSClassDef g_decoder_class_def = { "TextDecoder", .finalizer = decoder_finalizer };

/* Strict UTF-8: no overlongs, no surrogates, nothing past U+10FFFF, nothing cut short. */
static int valid_utf8(const uint8_t* p, size_t n) {
    size_t i = 0;
    while (i < n) {
        uint8_t c = p[i];
        if (c < 0x80) { i++; continue; }
        size_t len;
        uint32_t min;
        uint32_t cp;
        if ((c & 0xE0) == 0xC0) { len = 2; min = 0x80; cp = c & 0x1F; }
        else if ((c & 0xF0) == 0xE0) { len = 3; min = 0x800; cp = c & 0x0F; }
        else if ((c & 0xF8) == 0xF0) { len = 4; min = 0x10000; cp = c & 0x07; }
        else return 0;
        if (i + len > n) return 0;
        for (size_t k = 1; k < len; k++) {
            if ((p[i + k] & 0xC0) != 0x80) return 0;
            cp = (cp << 6) | (p[i + k] & 0x3F);
        }
        if (cp < min || cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF)) return 0;
        i += len;
    }
    return 1;
}

/* The bytes of an ArrayBuffer, a typed array or a DataView. NULL with an exception pending otherwise. */
static const uint8_t* bytes_of(JSContext* ctx, JSValueConst input, size_t* length) {
    size_t size = 0;
    uint8_t* whole = JS_GetArrayBuffer(ctx, &size, input);
    if (whole) {
        *length = size;
        return whole;
    }
    JS_FreeValue(ctx, JS_GetException(ctx));
    JSValue buffer = JS_GetPropertyStr(ctx, input, "buffer");
    uint8_t* data = JS_GetArrayBuffer(ctx, &size, buffer);
    JS_FreeValue(ctx, buffer);
    if (!data) {
        JS_FreeValue(ctx, JS_GetException(ctx));
        JS_ThrowTypeError(ctx, "TextDecoder.decode takes an ArrayBuffer, a typed array or a DataView");
        return NULL;
    }
    uint32_t offset = 0, count = 0;
    JSValue o = JS_GetPropertyStr(ctx, input, "byteOffset");
    JSValue l = JS_GetPropertyStr(ctx, input, "byteLength");
    int bad = JS_ToUint32(ctx, &offset, o) || JS_ToUint32(ctx, &count, l);
    JS_FreeValue(ctx, o);
    JS_FreeValue(ctx, l);
    if (bad) return NULL;
    if ((size_t)offset + count > size) {
        JS_ThrowRangeError(ctx, "TextDecoder.decode: a view past the end of its buffer");
        return NULL;
    }
    *length = count;
    return data + offset;
}

static JSValue decoder_new(JSContext* ctx, JSValueConst new_target, int argc, JSValueConst* argv) {
    if (argc > 0 && !JS_IsUndefined(argv[0])) {
        const char* label = JS_ToCString(ctx, argv[0]);
        if (!label) return JS_EXCEPTION;
        int ok = strcmp(label, "utf-8") == 0 || strcmp(label, "utf8") == 0 || strcmp(label, "UTF-8") == 0
            || strcmp(label, "unicode-1-1-utf-8") == 0;
        JS_FreeCString(ctx, label);
        if (!ok) return JS_ThrowRangeError(ctx, "TextDecoder: only utf-8 is supported here");
    }
    Decoder* d = (Decoder*)js_mallocz(ctx, sizeof(Decoder));
    if (!d) return JS_EXCEPTION;
    if (argc > 1 && JS_IsObject(argv[1])) {
        JSValue fatal = JS_GetPropertyStr(ctx, argv[1], "fatal");
        JSValue bom = JS_GetPropertyStr(ctx, argv[1], "ignoreBOM");
        d->fatal = JS_ToBool(ctx, fatal);
        d->ignore_bom = JS_ToBool(ctx, bom);
        JS_FreeValue(ctx, fatal);
        JS_FreeValue(ctx, bom);
    }
    JSValue proto = JS_GetPropertyStr(ctx, new_target, "prototype");
    JSValue obj = JS_NewObjectProtoClass(ctx, proto, g_decoder_class);
    JS_FreeValue(ctx, proto);
    if (JS_IsException(obj)) {
        js_free(ctx, d);
        return obj;
    }
    JS_SetOpaque(obj, d);
    return obj;
}

static JSValue decoder_decode(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv) {
    Decoder* d = (Decoder*)JS_GetOpaque2(ctx, this_val, g_decoder_class);
    if (!d) return JS_EXCEPTION;
    if (argc == 0 || JS_IsUndefined(argv[0])) return JS_NewStringLen(ctx, "", 0);
    size_t n = 0;
    const uint8_t* p = bytes_of(ctx, argv[0], &n);
    if (!p) return JS_EXCEPTION;
    if (!d->ignore_bom && n >= 3 && p[0] == 0xEF && p[1] == 0xBB && p[2] == 0xBF) {
        p += 3;
        n -= 3;
    }
    if (d->fatal && !valid_utf8(p, n)) return JS_ThrowTypeError(ctx, "TextDecoder: the data is not valid utf-8");
    return JS_NewStringLen(ctx, (const char*)p, n);
}

static JSValue decoder_encoding(JSContext* ctx, JSValueConst this_val) {
    (void)this_val;
    return JS_NewString(ctx, "utf-8");
}

static JSValue decoder_fatal(JSContext* ctx, JSValueConst this_val) {
    Decoder* d = (Decoder*)JS_GetOpaque2(ctx, this_val, g_decoder_class);
    return d ? JS_NewBool(ctx, d->fatal) : JS_EXCEPTION;
}

static JSValue decoder_ignore_bom(JSContext* ctx, JSValueConst this_val) {
    Decoder* d = (Decoder*)JS_GetOpaque2(ctx, this_val, g_decoder_class);
    return d ? JS_NewBool(ctx, d->ignore_bom) : JS_EXCEPTION;
}

static const JSCFunctionListEntry g_decoder_proto[] = {
    JS_CFUNC_DEF("decode", 1, decoder_decode),
    JS_CGETSET_DEF("encoding", decoder_encoding, NULL),
    JS_CGETSET_DEF("fatal", decoder_fatal, NULL),
    JS_CGETSET_DEF("ignoreBOM", decoder_ignore_bom, NULL),
};

static JSValue new_uint8_array(JSContext* ctx, const uint8_t* bytes, size_t n) {
    JSValue buffer = JS_NewArrayBufferCopy(ctx, bytes, n);
    if (JS_IsException(buffer)) return buffer;
    JSValue global = JS_GetGlobalObject(ctx);
    JSValue ctor = JS_GetPropertyStr(ctx, global, "Uint8Array");
    JS_FreeValue(ctx, global);
    JSValue array = JS_CallConstructor(ctx, ctor, 1, (JSValueConst*)&buffer);
    JS_FreeValue(ctx, ctor);
    JS_FreeValue(ctx, buffer);
    return array;
}

static JSValue encoder_new(JSContext* ctx, JSValueConst new_target, int argc, JSValueConst* argv) {
    (void)argc; (void)argv;
    JSValue proto = JS_GetPropertyStr(ctx, new_target, "prototype");
    JSValue obj = JS_NewObjectProto(ctx, proto);
    JS_FreeValue(ctx, proto);
    return obj;
}

static JSValue encoder_encode(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv) {
    (void)this_val;
    if (argc == 0 || JS_IsUndefined(argv[0])) return new_uint8_array(ctx, NULL, 0);
    size_t n = 0;
    // cesu8 off: a lone surrogate becomes U+FFFD's bytes, as the standard says.
    const char* s = JS_ToCStringLen2(ctx, &n, argv[0], 0);
    if (!s) return JS_EXCEPTION;
    JSValue array = new_uint8_array(ctx, (const uint8_t*)s, n);
    JS_FreeCString(ctx, s);
    return array;
}

/* How many UTF-16 units a whole UTF-8 sequence starting with this byte stands for. */
static uint32_t utf16_units(uint8_t lead) {
    return (lead & 0xF8) == 0xF0 ? 2 : 1;
}

static uint32_t utf8_length(uint8_t lead) {
    if (lead < 0x80) return 1;
    if ((lead & 0xE0) == 0xC0) return 2;
    if ((lead & 0xF0) == 0xE0) return 3;
    return 4;
}

static JSValue encoder_encode_into(JSContext* ctx, JSValueConst this_val, int argc, JSValueConst* argv) {
    (void)this_val;
    if (argc < 2) return JS_ThrowTypeError(ctx, "TextEncoder.encodeInto(string, uint8Array)");
    size_t n = 0;
    const char* s = JS_ToCStringLen2(ctx, &n, argv[0], 0);
    if (!s) return JS_EXCEPTION;
    size_t room = 0;
    uint8_t* out = (uint8_t*)bytes_of(ctx, argv[1], &room);
    if (!out) {
        JS_FreeCString(ctx, s);
        return JS_EXCEPTION;
    }
    // Whole characters only, as the standard does when the room runs out.
    size_t written = 0;
    uint32_t read = 0;
    while (written < n) {
        uint32_t len = utf8_length((uint8_t)s[written]);
        if (written + len > room) break;
        read += utf16_units((uint8_t)s[written]);
        written += len;
    }
    memcpy(out, s, written);
    JS_FreeCString(ctx, s);
    JSValue result = JS_NewObject(ctx);
    JS_SetPropertyStr(ctx, result, "read", JS_NewUint32(ctx, read));
    JS_SetPropertyStr(ctx, result, "written", JS_NewUint32(ctx, (uint32_t)written));
    return result;
}

static JSValue encoder_encoding(JSContext* ctx, JSValueConst this_val) {
    (void)this_val;
    return JS_NewString(ctx, "utf-8");
}

static const JSCFunctionListEntry g_encoder_proto[] = {
    JS_CFUNC_DEF("encode", 1, encoder_encode),
    JS_CFUNC_DEF("encodeInto", 2, encoder_encode_into),
    JS_CGETSET_DEF("encoding", encoder_encoding, NULL),
};

void qjs_text_install(JSContext* ctx) {
    JSRuntime* rt = JS_GetRuntime(ctx);
    if (g_decoder_class == 0) QJS_NEW_CLASS_ID(rt, &g_decoder_class);
    if (!JS_IsRegisteredClass(rt, g_decoder_class)) JS_NewClass(rt, g_decoder_class, &g_decoder_class_def);

    JSValue global = JS_GetGlobalObject(ctx);

    JSValue decoder_proto = JS_NewObject(ctx);
    JS_SetPropertyFunctionList(ctx, decoder_proto, g_decoder_proto, sizeof(g_decoder_proto) / sizeof(g_decoder_proto[0]));
    JSValue decoder = JS_NewCFunction2(ctx, decoder_new, "TextDecoder", 0, JS_CFUNC_constructor, 0);
    JS_SetConstructor(ctx, decoder, decoder_proto);
    JS_SetClassProto(ctx, g_decoder_class, decoder_proto);
    JS_SetPropertyStr(ctx, global, "TextDecoder", decoder);

    JSValue encoder_proto = JS_NewObject(ctx);
    JS_SetPropertyFunctionList(ctx, encoder_proto, g_encoder_proto, sizeof(g_encoder_proto) / sizeof(g_encoder_proto[0]));
    JSValue encoder = JS_NewCFunction2(ctx, encoder_new, "TextEncoder", 0, JS_CFUNC_constructor, 0);
    JS_SetConstructor(ctx, encoder, encoder_proto);
    JS_FreeValue(ctx, encoder_proto);
    JS_SetPropertyStr(ctx, global, "TextEncoder", encoder);

    JS_FreeValue(ctx, global);
}
