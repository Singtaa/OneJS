#!/usr/bin/env python3
"""Verify every shipped native plugin loads with nothing but OS-provided libraries.

The binaries under Plugins/ are committed build artifacts. Only the Linux one is
rebuilt by CI, so for every other platform the committed file is what users get,
and nothing else in the repo ever opens it. A dynamic dependency picked up from
the build machine's toolchain therefore ships unnoticed and fails at load time on
a clean machine, where the OS reports only that the top-level library is missing.

That is exactly how v3.2.0 shipped a Windows DLL importing libwinpthread-1.dll
(MinGW's posix thread model, satisfying quickjs.c's pthread use): every Windows
user got "DllNotFoundException: quickjs_unity" with no mention of the real cause.

This reads the dependency list straight out of each container format (PE imports,
ELF DT_NEEDED, Mach-O LC_LOAD_DYLIB) with no toolchain and no third-party module,
so it runs anywhere, on any host, against every platform's binary at once.

    python3 Auxiliary~/quickjs-unity/check-plugin-deps.py

Exit code 0 if every dependency is allowed, 1 otherwise.
"""

import re
import struct
import sys
from pathlib import Path

PLUGINS = Path(__file__).resolve().parents[2] / "Plugins"

# Per-platform allowlists. A name here is a promise that the library is present
# on a clean end-user machine: part of the OS, or of a runtime the platform
# guarantees. Everything else has to be linked statically instead.
#
# Windows: UCRT (api-ms-win-crt-*) ships with Windows 10 and is serviced by
# Windows Update, so it needs no redistributable. The MSVC runtime (VCRUNTIME*,
# MSVCP*) does need one and is deliberately absent.
TARGETS = [
    ("Windows/x64/quickjs_unity.dll", [
        r"kernel32\.dll",
        r"api-ms-win-crt-[a-z0-9-]+\.dll",
    ]),
    ("macOS/libquickjs_unity.dylib", [
        r"/usr/lib/libSystem\.B\.dylib",
    ]),
    ("Linux/x64/libquickjs_unity.so", [
        r"libc\.so\.6",
        r"libm\.so\.6",
        r"libdl\.so\.2",
        r"libpthread\.so\.0",
        r"ld-linux-x86-64\.so\.2",
    ]),
] + [
    (f"Android/{abi}/libquickjs_unity.so", [
        r"libc\.so",
        r"libm\.so",
        r"libdl\.so",
        r"liblog\.so",
    ])
    for abi in ("arm64-v8a", "armeabi-v7a", "x86_64")
]

# iOS ships a static archive (linked into the IL2CPP binary) and WebGL a .jslib,
# so neither has a dependency list to read.


def pe_imports(data):
    pe = struct.unpack_from("<I", data, 0x3C)[0]
    if data[pe:pe + 4] != b"PE\0\0":
        raise ValueError("not a PE file")
    nsec = struct.unpack_from("<H", data, pe + 6)[0]
    opt = pe + 24
    magic = struct.unpack_from("<H", data, opt)[0]
    dirs = opt + (112 if magic == 0x20B else 96)
    sections = [
        struct.unpack_from("<IIII", data, opt + struct.unpack_from("<H", data, pe + 20)[0] + 40 * i + 8)
        for i in range(nsec)
    ]

    def to_offset(rva):
        for vsize, vaddr, rsize, raddr in sections:
            if vaddr <= rva < vaddr + max(vsize, rsize):
                return raddr + (rva - vaddr)
        raise ValueError(f"RVA {rva:#x} is outside every section")

    def cstring(offset):
        return data[offset:data.index(b"\0", offset)].decode("ascii")

    names = []
    for base, stride, name_field in ((dirs + 8, 20, 12), (dirs + 13 * 8, 32, 4)):  # imports, delay imports
        rva = struct.unpack_from("<I", data, base)[0]
        if not rva:
            continue
        entry = to_offset(rva)
        while any(struct.unpack_from("<I", data, entry + 4 * i)[0] for i in range(stride // 4)):
            name_rva = struct.unpack_from("<I", data, entry + name_field)[0]
            names.append(cstring(to_offset(name_rva)))
            entry += stride
    return names


def elf_needed(data):
    if data[:4] != b"\x7fELF":
        raise ValueError("not an ELF file")
    is64, little = data[4] == 2, data[5] == 1
    end = "<" if little else ">"
    shoff, shentsize, shnum = (
        struct.unpack_from(end + "Q", data, 0x28)[0] if is64 else struct.unpack_from(end + "I", data, 0x20)[0],
        struct.unpack_from(end + "H", data, 0x3A if is64 else 0x2E)[0],
        struct.unpack_from(end + "H", data, 0x3C if is64 else 0x30)[0],
    )

    dynamic = dynstr = None
    for i in range(shnum):
        sh = shoff + shentsize * i
        sh_type = struct.unpack_from(end + "I", data, sh + 4)[0]
        offset, size = (
            struct.unpack_from(end + "QQ", data, sh + 0x18) if is64
            else struct.unpack_from(end + "II", data, sh + 0x10)
        )
        if sh_type == 6:  # SHT_DYNAMIC
            dynamic = (offset, size)
        elif sh_type == 3 and dynstr is None and struct.unpack_from(end + "I", data, sh)[0]:  # SHT_STRTAB
            dynstr_candidate = (offset, size)
            # .dynstr is the string table the SHT_DYNAMIC section links to; resolve below.
            dynstr = dynstr or dynstr_candidate
    if dynamic is None:
        return []

    # DT_STRTAB is a virtual address, so map it back through the program headers.
    phoff = struct.unpack_from(end + "Q", data, 0x20)[0] if is64 else struct.unpack_from(end + "I", data, 0x1C)[0]
    phentsize = struct.unpack_from(end + "H", data, 0x36 if is64 else 0x2A)[0]
    phnum = struct.unpack_from(end + "H", data, 0x38 if is64 else 0x2C)[0]
    loads = []
    for i in range(phnum):
        ph = phoff + phentsize * i
        if struct.unpack_from(end + "I", data, ph)[0] != 1:  # PT_LOAD
            continue
        if is64:
            p_offset, p_vaddr = struct.unpack_from(end + "QQ", data, ph + 0x08)
            p_filesz = struct.unpack_from(end + "Q", data, ph + 0x20)[0]
        else:
            p_offset, p_vaddr = struct.unpack_from(end + "II", data, ph + 0x04)
            p_filesz = struct.unpack_from(end + "I", data, ph + 0x10)[0]
        loads.append((p_vaddr, p_offset, p_filesz))

    def to_offset(vaddr):
        for base, offset, size in loads:
            if base <= vaddr < base + size:
                return offset + (vaddr - base)
        raise ValueError(f"vaddr {vaddr:#x} is in no PT_LOAD segment")

    fmt, step = (end + "qQ", 16) if is64 else (end + "iI", 8)
    offset, size = dynamic
    needed, strtab = [], None
    for pos in range(offset, offset + size, step):
        tag, value = struct.unpack_from(fmt, data, pos)
        if tag == 0:  # DT_NULL
            break
        if tag == 1:  # DT_NEEDED, value is an offset into DT_STRTAB
            needed.append(value)
        elif tag == 5:  # DT_STRTAB
            strtab = value
    base = to_offset(strtab)
    return [data[base + n:data.index(b"\0", base + n)].decode("ascii") for n in needed]


def macho_dylibs(data):
    slices = []
    magic = struct.unpack_from(">I", data, 0)[0]
    if magic in (0xCAFEBABE, 0xCAFEBABF):  # fat binary
        count = struct.unpack_from(">I", data, 4)[0]
        width = 20 if magic == 0xCAFEBABE else 32
        for i in range(count):
            entry = 8 + width * i
            offset = (struct.unpack_from(">I", data, entry + 8)[0] if magic == 0xCAFEBABE
                      else struct.unpack_from(">Q", data, entry + 8)[0])
            slices.append(offset)
    else:
        slices.append(0)

    names = []
    for start in slices:
        magic = struct.unpack_from("<I", data, start)[0]
        if magic not in (0xFEEDFACE, 0xFEEDFACF):
            raise ValueError("not a Mach-O file")
        is64 = magic == 0xFEEDFACF
        ncmds = struct.unpack_from("<I", data, start + 16)[0]
        cmd = start + (32 if is64 else 28)
        for _ in range(ncmds):
            cmd_type, cmd_size = struct.unpack_from("<II", data, cmd)
            if cmd_type in (0x0C, 0x8000_0018, 0x1F):  # LOAD_DYLIB, LOAD_WEAK_DYLIB, REEXPORT_DYLIB
                name_offset = struct.unpack_from("<I", data, cmd + 8)[0]
                base = cmd + name_offset
                names.append(data[base:data.index(b"\0", base)].decode("ascii"))
            cmd += cmd_size
    return names


def dependencies(path, data):
    if path.suffix == ".dll":
        return pe_imports(data)
    if path.suffix == ".dylib":
        return macho_dylibs(data)
    return elf_needed(data)


def main():
    failures, checked = [], 0
    for relative, allowed in TARGETS:
        path = PLUGINS / relative
        if not path.exists():
            failures.append(f"{relative}: missing")
            continue
        patterns = [re.compile(p + "$", re.IGNORECASE) for p in allowed]
        try:
            deps = dependencies(path, path.read_bytes())
        except Exception as error:  # a malformed binary is itself a failure
            failures.append(f"{relative}: could not read dependencies ({error})")
            continue
        checked += 1
        stray = sorted({d for d in deps if not any(p.match(d) for p in patterns)})
        if stray:
            failures.append(f"{relative}: unexpected dependency {', '.join(stray)}")
        print(f"{'FAIL' if stray else 'ok  '}  {relative}: {', '.join(sorted(set(deps))) or 'none'}")

    if failures:
        print("\nEvery dependency has to be present on a clean end-user machine.")
        print("Link the rest statically (see the -static note in build-windows.sh),")
        print("or add it to this script's allowlist with a reason.\n")
        for failure in failures:
            print(f"  {failure}")
        return 1
    print(f"\n{checked} native plugins depend only on OS-provided libraries.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
