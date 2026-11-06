/*
 * Where Bellard's QuickJS (every .sh build) and quickjs-ng (the MSVC build)
 * differ in the API this plugin's own C uses. quickjs-ng alone defines
 * QJS_VERSION_MAJOR.
 */
#ifndef QUICKJS_COMPAT_H
#define QUICKJS_COMPAT_H

#include "quickjs.h"

#ifdef QJS_VERSION_MAJOR
#define QJS_NEW_CLASS_ID(rt, pclass_id) JS_NewClassID((rt), (pclass_id))
#else
#define QJS_NEW_CLASS_ID(rt, pclass_id) ((void)(rt), JS_NewClassID(pclass_id))
#endif

#endif
