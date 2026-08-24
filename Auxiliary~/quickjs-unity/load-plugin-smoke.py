#!/usr/bin/env python3
"""Load this platform's shipped native plugin and check its ABI against the C#.

check-plugin-deps.py reads dependency lists without opening anything, which is
what lets one Linux runner check all six binaries at once. The cost of that
reach is that it cannot tell whether a binary actually loads and runs. This is
the other half: on a runner matching the binary's own platform it opens the
shipped file, calls qjs_abi_version, and compares the answer with the
ExpectedAbiVersion constant the C# runtime enforces at context creation.

That covers the defect the static guard structurally cannot see: a binary that
parses fine but is stale, so its ABI disagrees with the C# side. Today that
surfaces only at runtime, as QuickJSContext telling the user to restart the
editor, which sends them looking in the wrong place when the real cause is a
platform whose binary was never rebuilt.

Run with no arguments to test this platform's shipped binary:

    python3 Auxiliary~/quickjs-unity/load-plugin-smoke.py

Pass a path to test a specific file instead. That is how the failing case gets
demonstrated: point it at a known-bad binary and watch it go red.

Exit code 0 if the library loads and its ABI matches, 1 otherwise.
"""

import ctypes
import platform
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

# Only the three desktop platforms can be loaded by a CI runner. Android and iOS
# binaries are covered by check-plugin-deps.py alone; nothing can dlopen them
# here, which is a real limit of this job rather than an oversight.
PLUGIN_FOR_PLATFORM = {
    "win32": "Windows/x64/quickjs_unity.dll",
    "darwin": "macOS/libquickjs_unity.dylib",
    "linux": "Linux/x64/libquickjs_unity.so",
}

# The C# side of the handshake. Matched across all of Runtime/ rather than one
# hardcoded file so that moving the constant does not silently disable the check.
ABI_CONSTANT = re.compile(r"const\s+int\s+ExpectedAbiVersion\s*=\s*(\d+)\s*;")


def expected_abi_version():
    """Read ExpectedAbiVersion out of the C# runtime.

    Errors if the constant is not found exactly once. A check that cannot find
    its input has to fail: a silent no-match would report green forever, which
    is the only failure mode here that actually matters.
    """
    hits = []
    for source in sorted((ROOT / "Runtime").rglob("*.cs")):
        for match in ABI_CONSTANT.finditer(source.read_text(encoding="utf-8", errors="replace")):
            hits.append((source.relative_to(ROOT), int(match.group(1))))

    if len(hits) != 1:
        where = ", ".join(f"{path} = {value}" for path, value in hits) or "nowhere"
        raise LookupError(
            f"expected exactly one ExpectedAbiVersion declaration under Runtime/, found {len(hits)} ({where}). "
            "The constant moved or was duplicated; update ABI_CONSTANT in this script."
        )
    return hits[0]


def main():
    argv = sys.argv[1:]
    if len(argv) > 1:
        print(f"usage: {Path(__file__).name} [path-to-native-plugin]")
        return 1

    key = "linux" if sys.platform.startswith("linux") else sys.platform
    relative = PLUGIN_FOR_PLATFORM.get(key)
    if argv:
        path, label = Path(argv[0]), argv[0]
    elif relative is None:
        print(f"FAIL  no shipped plugin mapped for platform {sys.platform!r}")
        return 1
    else:
        path, label = ROOT / "Plugins" / relative, relative

    host = f"{sys.platform}/{platform.machine()}"

    try:
        source, expected = expected_abi_version()
    except LookupError as error:
        print(f"FAIL  {label}: {error}")
        return 1

    if not path.exists():
        print(f"FAIL  {label}: missing")
        print(f"\nNo binary at {path}.")
        return 1

    try:
        library = ctypes.CDLL(str(path))
    except OSError as error:
        print(f"FAIL  {label}: will not load on {host}")
        print(f"\nThe OS refused the library: {error}\n")
        print("A shipped plugin that will not open on its own platform reaches users")
        print("as a bare DllNotFoundException naming only the top-level library.")
        print("Check the architecture it was built for and its dependency list")
        print("(Auxiliary~/quickjs-unity/check-plugin-deps.py reports the latter).")
        return 1

    try:
        symbol = library.qjs_abi_version
    except AttributeError:
        print(f"FAIL  {label}: loaded on {host} but exports no qjs_abi_version")
        print("\nThe library opened but is missing the ABI entry point, so it is either")
        print("far older than this runtime or was built from different source.")
        return 1

    symbol.argtypes = []
    symbol.restype = ctypes.c_int
    actual = symbol()

    if actual != expected:
        print(f"FAIL  {label}: loaded on {host}, qjs_abi_version() = {actual}, {source} expects {expected}")
        print(f"\nThis binary is out of step with the C# runtime. QuickJSContext refuses")
        print("to start on that mismatch and tells the user to restart the editor, which")
        print("is misleading when the real cause is a binary that was never rebuilt.")
        print(f"Rebuild the {key} plugin from the current source, or correct")
        print(f"ExpectedAbiVersion in {source} if the ABI genuinely changed.")
        return 1

    print(f"ok    {label}: loaded on {host}, qjs_abi_version() = {actual}, {source} expects {expected}")
    print(f"\nThe shipped {key} plugin loads and its ABI matches the C# runtime.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
