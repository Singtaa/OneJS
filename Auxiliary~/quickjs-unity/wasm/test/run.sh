#!/bin/bash
# Checks the whole wasm pipeline on this machine: build-module.sh turns
# probe.wasm into a module library for this host, and module-smoke.py runs it
# through the engine OneJS ships for this host (Plugins/), outside Unity. Then
# the same for probe-wrong.wasm, whose add subtracts, which the smoke has to
# fail: a smoke that passes it is measuring nothing. Exits non-zero on either.
#
#     wasm/test/run.sh
#
# On macOS both architectures run when Rosetta is there. On Linux, run it where
# gcc builds for the shipped glibc floor (CI's ubuntu:22.04 container).
set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PLUGINS="$(cd "$DIR/../../../../Plugins" && pwd)"
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT

case "$(uname -s)" in
    Darwin)
        PLATFORM=macos
        ENGINE="$PLUGINS/macOS/libquickjs_unity.dylib"
        LIB="macOS/libonejs_%s.dylib"
        RUNNERS="python3"
        arch -x86_64 /usr/bin/true 2> /dev/null && RUNNERS="$RUNNERS|arch -x86_64 /usr/bin/python3"
        ;;
    Linux)
        PLATFORM=linux
        export ONEJS_IN_CONTAINER=1
        ENGINE="$PLUGINS/Linux/x64/libquickjs_unity.so"
        LIB="Linux/x64/libonejs_%s.so"
        RUNNERS="python3"
        ;;
    *) echo "macOS or Linux only"; exit 2 ;;
esac

"$DIR/../build-module.sh" "$DIR/probe.wasm" probe "$OUT" "$PLATFORM" > "$OUT/build.log" 2>&1 || { cat "$OUT/build.log"; exit 1; }
"$DIR/../build-module.sh" "$DIR/probe-wrong.wasm" probewrong "$OUT" "$PLATFORM" > "$OUT/build-wrong.log" 2>&1 || { cat "$OUT/build-wrong.log"; exit 1; }

STATUS=0
IFS='|' read -ra EACH <<< "$RUNNERS"
for RUN in "${EACH[@]}"; do
    echo "=== $RUN: probe ==="
    # shellcheck disable=SC2059
    $RUN "$DIR/module-smoke.py" "$ENGINE" "$OUT/$(printf "$LIB" probe)" probe "$DIR/probe.wasm" || STATUS=1
    echo "=== $RUN: probe-wrong, which must fail ==="
    set +e
    # shellcheck disable=SC2059
    $RUN "$DIR/module-smoke.py" "$ENGINE" "$OUT/$(printf "$LIB" probewrong)" probewrong "$DIR/probe-wrong.wasm" > "$OUT/wrong.txt"
    WRONG=$?
    set -e
    if [ "$WRONG" -eq 1 ] && grep -q "FAIL  an export" "$OUT/wrong.txt"; then
        echo "ok    the smoke failed the module that answers wrong"
    else
        cat "$OUT/wrong.txt"
        echo "FAIL  the smoke did not fail the module that answers wrong (exit $WRONG)"
        STATUS=1
    fi
done
[ "$STATUS" -eq 0 ] && echo "PASS" || echo "FAIL"
exit "$STATUS"
