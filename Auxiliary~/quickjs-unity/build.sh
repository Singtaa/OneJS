#!/bin/bash
cd quickjs && make libquickjs.a && cd ..

clang -dynamiclib -O2 \
    -I./quickjs \
    -o libquickjs_unity.dylib \
    src/quickjs_unity.c \
    quickjs/libquickjs.a

cp libquickjs_unity.dylib ../../Plugins/macOS

echo "DONE. Generated libquickjs_unity.dylib under Plugins/macOS"