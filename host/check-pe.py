#!/usr/bin/env python3
# Self-test for the built loader: python3 check-pe.py <RDR2Coop.asi>
# Checks, from the PE headers alone (no pefile): x64 DLL; it imports only Windows system DLLs (ScriptHookRDR2.dll
# and hostfxr.dll are resolved by hand at run time, so they must NOT be in the import table, else the game's .asi
# loader would fail to load us before ScriptHookRDR2 is up); exports only RDR2Coop_worldGetAll (the SEH pool guard
# NativeMemory.cs P/Invokes) and mingw CRT housekeeping (zig's linker has no --exclude-all-symbols; a leaked
# ScriptHook-style export would mean a mistake); and every mangled name, the hostfxr
# entry points and the managed type name are present as strings. Exit 1 with the first failure.
import struct, sys

MANGLED = [
    "?scriptRegister@@YAXPEAUHINSTANCE__@@P6AXXZ@Z", "?scriptUnregister@@YAXPEAUHINSTANCE__@@@Z", "?scriptWait@@YAXK@Z",
    "?keyboardHandlerRegister@@YAXP6AXKGEHHHH@Z@Z", "?keyboardHandlerUnregister@@YAXP6AXKGEHHHH@Z@Z",
    "?nativeInit@@YAX_K@Z", "?nativePush64@@YAX_K@Z", "?nativeCall@@YAPEA_KXZ", "?getGlobalPtr@@YAPEA_KH@Z",
    "?worldGetAllPeds@@YAHPEAHH@Z", "?worldGetAllVehicles@@YAHPEAHH@Z", "?worldGetAllObjects@@YAHPEAHH@Z",
    "?worldGetAllPickups@@YAHPEAHH@Z", "?getScriptHandleBaseAddress@@YAPEAEH@Z", "?getGameVersion@@YA?AW4eGameVersion@@XZ",
]
HOSTFXR = ["hostfxr_initialize_for_runtime_config", "hostfxr_get_runtime_delegate", "hostfxr_close", "hostfxr_set_error_writer"]
ALLOWED_IMPORT_PREFIXES = ("kernel32.dll", "user32.dll", "api-ms-win-crt-")

def fail(msg):
    print("check-pe FAILED: " + msg); sys.exit(1)

data = open(sys.argv[1], "rb").read()
if data[:2] != b"MZ": fail("not an MZ file")
pe = struct.unpack_from("<I", data, 0x3C)[0]
if data[pe:pe + 4] != b"PE\0\0": fail("no PE signature")
machine, nsections, _, _, _, optsize, chars = struct.unpack_from("<HHIIIHH", data, pe + 4)
if machine != 0x8664: fail("machine 0x%x is not x64" % machine)
if not chars & 0x2000: fail("not a DLL")
opt = pe + 24
if struct.unpack_from("<H", data, opt)[0] != 0x20B: fail("not PE32+")
ndirs = struct.unpack_from("<I", data, opt + 108)[0]
dirs = [struct.unpack_from("<II", data, opt + 112 + 8 * i) for i in range(ndirs)]
sections = [struct.unpack_from("<8sIIII", data, opt + optsize + 40 * i) for i in range(nsections)]

def rva(r):   # file offset of a virtual address
    for _, vsize, vaddr, rawsize, rawptr in sections:
        if vaddr <= r < vaddr + max(vsize, rawsize): return rawptr + r - vaddr
    fail("rva 0x%x outside every section" % r)

def cstr(off): return data[off:data.index(b"\0", off)].decode("ascii")

exports, (exp_rva, exp_size) = [], dirs[0]
if exp_size:
    e = rva(exp_rva); nnames, names = struct.unpack_from("<I", data, e + 24)[0], rva(struct.unpack_from("<I", data, e + 32)[0])
    exports = [cstr(rva(struct.unpack_from("<I", data, names + 4 * i)[0])) for i in range(nnames)]
bad = [x for x in exports if x.startswith("?") or "hostfxr" in x]
if bad: fail("unexpected exports %s" % bad)
if "RDR2Coop_worldGetAll" not in exports: fail("RDR2Coop_worldGetAll not exported (the pool guard NativeMemory.cs calls)")
imports, p = [], rva(dirs[1][0])
while struct.unpack_from("<I", data, p + 12)[0]:
    imports.append(cstr(rva(struct.unpack_from("<I", data, p + 12)[0]))); p += 20
bad = [d for d in imports if not d.lower().startswith(ALLOWED_IMPORT_PREFIXES)]
if bad: fail("unexpected imports %s (ScriptHookRDR2/hostfxr are resolved at run time)" % bad)
for s in MANGLED + HOSTFXR:
    if s.encode() not in data: fail("string missing: " + s)
if "RDR2Coop.Core.Host, RDR2Coop.Core".encode("utf-16-le") not in data: fail("managed type name missing")
print("check-pe ok: x64 DLL; imports %s; exports %s; %d mangled names + hostfxr + managed type name present"
      % (", ".join(sorted(set(d.split("-l1")[0] for d in imports))), exports, len(MANGLED)))
