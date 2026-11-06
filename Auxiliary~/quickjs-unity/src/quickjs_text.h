/* TextDecoder and TextEncoder, UTF-8, for QuickJS (quickjs_text.c). */
#ifndef QUICKJS_TEXT_H
#define QUICKJS_TEXT_H

#include "quickjs.h"

/* Adds TextDecoder and TextEncoder to a context's globals. */
void qjs_text_install(JSContext* ctx);

#endif
