// RDR2Coop.asi: the loader. ScriptHookRDR2 loads it like any .asi; it registers one script, starts
// .NET 9 from RDR2Coop\runtime on the script fiber, loads RDR2Coop\RDR2Coop.Core.dll and hands it
// every tick and key press. Nothing here may take the game down: each failure is one line in
// scripts\RDR2Coop.Host.log and the loader goes idle. Contract with the managed side: api/Core/Host.cs (Init/Tick/Key).
// Built with zig (clang, mingw target): ScriptHookRDR2's C++ exports are therefore resolved by their
// MSVC-mangled names instead of being linked.
#include <windows.h>
#include <cstdint>
#include <cstdarg>
#include <cstdio>
#include <cwchar>
#include <string>

#ifndef RDR2COOP_VERSION
#define RDR2COOP_VERSION "dev"
#endif

namespace {

std::wstring g_base;     // the game folder (where this .asi is), with its trailing backslash
std::wstring g_logPath;

void Log(const char* fmt, ...)
{
    FILE* f = _wfopen(g_logPath.c_str(), L"a");
    if (!f) return;
    SYSTEMTIME t; GetLocalTime(&t);
    fprintf(f, "[%02u:%02u:%02u] ", t.wHour, t.wMinute, t.wSecond);
    va_list a; va_start(a, fmt); vfprintf(f, fmt, a); va_end(a);
    fputc('\n', f);
    fclose(f);
}

// ---- ScriptHookRDR2 (signatures from its SDK's main.h) ----
typedef void (*scriptRegister_t)(HMODULE, void (*)());
typedef void (*scriptUnregister_t)(HMODULE);
typedef void (*scriptWait_t)(DWORD);
typedef void (*keyboardHandler_t)(DWORD key, WORD repeats, BYTE scanCode, BOOL isExtended, BOOL isWithAlt, BOOL wasDownBefore, BOOL isUpNow);
typedef void (*keyboardHandlerRegister_t)(keyboardHandler_t);

scriptRegister_t scriptRegister;
scriptUnregister_t scriptUnregister;
scriptWait_t scriptWait;
keyboardHandlerRegister_t keyboardHandlerRegister, keyboardHandlerUnregister;

// The exports RDR2Coop.Core P/Invokes by name (NativeFunc.cs, NativeMemory.cs). Checked up front so a
// ScriptHookRDR2 build that lacks one is a clear log line at startup, not an exception mid-game.
const char* const kManagedExports[] = {
    "?nativeInit@@YAX_K@Z", "?nativePush64@@YAX_K@Z", "?nativeCall@@YAPEA_KXZ", "?getGlobalPtr@@YAPEA_KH@Z",
    "?getScriptHandleBaseAddress@@YAPEAEH@Z", "?getGameVersion@@YA?AW4eGameVersion@@XZ",
};

// The pool lists go through RDR2Coop_worldGetAll below instead: ScriptHookRDR2 faults at random while
// walking a pool (Halen84/ScriptHookRDR2DotNet-V2 issue 2), and on .NET 9 an access violation in native
// code can't be caught by managed code (the process just exits), so the guard has to be SEH, here.
typedef int (*worldGetAll_t)(int* arr, int arrSize);
const char* const kPoolExports[] = {
    "?worldGetAllPeds@@YAHPEAHH@Z", "?worldGetAllVehicles@@YAHPEAHH@Z", "?worldGetAllObjects@@YAHPEAHH@Z",
    "?worldGetAllPickups@@YAHPEAHH@Z",
};
worldGetAll_t g_worldGetAll[4];

template <class T> bool Resolve(HMODULE sh, const char* name, T& fn)
{
    fn = (T)GetProcAddress(sh, name);
    if (!fn) Log("ScriptHookRDR2.dll has no export %s", name);
    return fn != nullptr;
}

// ---- hostfxr (the entry points we use, from dotnet/runtime's hostfxr.h and coreclr_delegates.h) ----
typedef int (*hostfxr_initialize_for_runtime_config_fn)(const wchar_t* runtime_config_path, const void* parameters, void** host_context_handle);
typedef int (*hostfxr_get_runtime_delegate_fn)(void* host_context_handle, int type, void** delegate);
typedef int (*hostfxr_close_fn)(void* host_context_handle);
typedef void (*hostfxr_error_writer_fn)(const wchar_t* message);
typedef hostfxr_error_writer_fn (*hostfxr_set_error_writer_fn)(hostfxr_error_writer_fn);
typedef int (*load_assembly_and_get_function_pointer_fn)(const wchar_t* assembly_path, const wchar_t* type_name, const wchar_t* method_name, const wchar_t* delegate_type_name, void* reserved, void** delegate);
const int hdt_load_assembly_and_get_function_pointer = 5;
const wchar_t* const UNMANAGEDCALLERSONLY_METHOD = (const wchar_t*)-1;

// ---- the managed side: RDR2Coop.Core.dll, static class RDR2Coop.Core.Host, [UnmanagedCallersOnly] ----
typedef int (*managed_init_fn)(const wchar_t* gameDir);                              // 0 = running; anything else = stay idle
typedef void (*managed_tick_fn)();
typedef void (*managed_key_fn)(unsigned int key, int down, int ctrl, int shift, int alt);
managed_init_fn g_init;
managed_tick_fn g_tick;
managed_key_fn g_key;
bool g_ready;            // Init returned 0 and the script loop is running: only then do keys go through

// runtime\host\fxr\<version>\ : whichever version make-runtime.sh put there (highest name if several).
std::wstring FxrDir()
{
    std::wstring root = g_base + L"RDR2Coop\\runtime\\host\\fxr\\", best;
    WIN32_FIND_DATAW fd;
    HANDLE h = FindFirstFileW((root + L"*").c_str(), &fd);
    if (h == INVALID_HANDLE_VALUE) return best;
    do {
        if ((fd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) && fd.cFileName[0] != L'.' && wcscmp(fd.cFileName, best.c_str()) > 0)
            best = fd.cFileName;
    } while (FindNextFileW(h, &fd));
    FindClose(h);
    return best.empty() ? best : root + best + L"\\";
}

const char* HostfxrHint(int rc)
{
    switch ((unsigned)rc) {
        case 0x80008093: return " (the runtimeconfig is self-contained; it must name the Microsoft.NETCore.App framework)";
        case 0x80008096: return " (Microsoft.NETCore.App.deps.json / .runtimeconfig.json missing from runtime\\shared)";
        default: return "";
    }
}

// Starts .NET from RDR2Coop\runtime and fetches the three entry points. False = logged, stay idle.
bool StartManaged()
{
    std::wstring dir = g_base + L"RDR2Coop\\";
    if (GetFileAttributesW((dir + L"host-trace.txt").c_str()) != INVALID_FILE_ATTRIBUTES) {   // support switch: hostfxr's own trace
        SetEnvironmentVariableW(L"COREHOST_TRACE", L"1");
        SetEnvironmentVariableW(L"COREHOST_TRACEFILE", (dir + L"host-trace.log").c_str());
    }
    std::wstring fxrDir = FxrDir();
    if (fxrDir.empty()) { Log("no .NET runtime: expected RDR2Coop\\runtime\\host\\fxr\\<version>\\hostfxr.dll in the game folder (is the RDR2Coop folder unpacked next to RDR2.exe?)"); return false; }
    HMODULE fxr = LoadLibraryW((fxrDir + L"hostfxr.dll").c_str());
    if (!fxr) { Log("%lshostfxr.dll failed to load (error %lu)", fxrDir.c_str(), GetLastError()); return false; }
    auto init = (hostfxr_initialize_for_runtime_config_fn)GetProcAddress(fxr, "hostfxr_initialize_for_runtime_config");
    auto getDelegate = (hostfxr_get_runtime_delegate_fn)GetProcAddress(fxr, "hostfxr_get_runtime_delegate");
    auto close = (hostfxr_close_fn)GetProcAddress(fxr, "hostfxr_close");
    auto setErrorWriter = (hostfxr_set_error_writer_fn)GetProcAddress(fxr, "hostfxr_set_error_writer");
    if (!init || !getDelegate || !close) { Log("hostfxr.dll lacks the expected exports"); return false; }
    if (setErrorWriter) setErrorWriter([](const wchar_t* m) { Log("hostfxr: %ls", m); });

    std::wstring runtimeRoot = dir + L"runtime", config = dir + L"RDR2Coop.Core.runtimeconfig.json", core = dir + L"RDR2Coop.Core.dll";
    struct { size_t size; const wchar_t* host_path; const wchar_t* dotnet_root; } params = { sizeof(params), nullptr, runtimeRoot.c_str() };
    void* ctx = nullptr;
    int rc = init(config.c_str(), &params, &ctx);
    if (rc != 0 || !ctx) { Log("hostfxr_initialize_for_runtime_config(%ls) failed: 0x%x%s", config.c_str(), rc, HostfxrHint(rc)); return false; }
    void* loadPtr = nullptr;
    rc = getDelegate(ctx, hdt_load_assembly_and_get_function_pointer, &loadPtr);
    close(ctx);   // the runtime stays up; the handle only carried the configuration
    if (rc != 0 || !loadPtr) { Log("hostfxr_get_runtime_delegate failed: 0x%x%s", rc, HostfxrHint(rc)); return false; }
    auto load = (load_assembly_and_get_function_pointer_fn)loadPtr;

    struct { const wchar_t* name; void** fn; } entries[] = { { L"Init", (void**)&g_init }, { L"Tick", (void**)&g_tick }, { L"Key", (void**)&g_key } };
    for (auto& e : entries) {
        rc = load(core.c_str(), L"RDR2Coop.Core.Host, RDR2Coop.Core", e.name, UNMANAGEDCALLERSONLY_METHOD, nullptr, e.fn);
        if (rc != 0 || !*e.fn) { Log("%ls: entry point RDR2Coop.Core.Host.%ls not found: 0x%x", core.c_str(), e.name, rc); return false; }
    }
    return true;
}

[[noreturn]] void Idle(const char* why)
{
    Log("%s: co-op is off for this game session", why);
    for (;;) scriptWait(1000);
}

// All managed code runs on ONE fiber of ours, made once per process. .NET ties the game's main thread to the stack it
// first ran on, and ScriptHookRDR2 gives this script a NEW fiber (a new stack) whenever a save loads: entering .NET from
// that new stack crashed the game (27 Oct, "loading my save crashed the game"), and before that, starting .NET again
// there failed (0x1, co-op off). Our fiber outlives the game's: each call switches to it and back.
enum class Call { Start, Init, Tick };
void* g_managedFiber;
void* g_backTo;
Call g_call;
int g_result;

// Keys come from the game's window procedure, outside any fiber of ours: queued, handed over before the next tick.
struct KeyEvent { uint32_t key; int down, ctrl, shift, alt; };
KeyEvent g_keys[64];
int g_keyCount;

void CALLBACK ManagedFiber(void*)
{
    for (;;) {
        switch (g_call) {
        case Call::Start: g_result = StartManaged() ? 0 : 1; break;
        case Call::Init: g_result = g_init(g_base.c_str()); break;
        case Call::Tick:
            for (int i = 0; i < g_keyCount; i++) g_key(g_keys[i].key, g_keys[i].down, g_keys[i].ctrl, g_keys[i].shift, g_keys[i].alt);
            g_keyCount = 0;
            g_tick();
            break;
        }
        SwitchToFiber(g_backTo);
    }
}

int OnManagedFiber(Call c)
{
    if (!g_managedFiber) {
        g_managedFiber = CreateFiberEx(0, 16 * 1024 * 1024, FIBER_FLAG_FLOAT_SWITCH, ManagedFiber, nullptr);   // .NET wants a big stack
        if (!g_managedFiber) { Log("couldn't make the loader's fiber (error %lu)", GetLastError()); return -1; }
    }
    g_call = c;
    g_backTo = GetCurrentFiber();
    SwitchToFiber(g_managedFiber);
    return g_result;
}

// The script ScriptHookRDR2 runs for us, on the fiber it made (a new one after a save loads: a new call of this).
// Same loop as SHDN's DllMain.cpp: a new fiber means the game reloaded its scripts, so the managed side is initialised again.
void ScriptMain()
{
    void* fiber = GetCurrentFiber();
    static bool started = false;
    if (!started) {
        if (OnManagedFiber(Call::Start) != 0) Idle("loader idle");
        started = true;
        Log(".NET runtime up");
    } else {
        Log("game restarted its scripts (a save loaded?): initialising the managed side again");
    }
    for (;;) {
        g_keyCount = 0;   // keys from before the (re)start are stale
        int rc = OnManagedFiber(Call::Init);
        if (rc != 0) { Log("RDR2Coop.Core Init returned %d (its own log says why)", rc); Idle("loader idle"); }
        g_ready = true;
        while (GetCurrentFiber() == fiber) { OnManagedFiber(Call::Tick); scriptWait(0); }
        g_ready = false;
        fiber = GetCurrentFiber();
        Log("game reloaded its scripts (new fiber): initialising the managed side again");
    }
}

// ScriptHookRDR2 calls this from the game's window procedure, on the main thread but not on our fiber: queued.
void KeyboardMessage(DWORD key, WORD, BYTE, BOOL, BOOL isWithAlt, BOOL, BOOL isUpNow)
{
    if (!g_ready || g_keyCount >= 64) return;
    g_keys[g_keyCount++] = { (uint32_t)key, !isUpNow, (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0, (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0, isWithAlt != FALSE };
}

} // namespace

// kind: 0 peds, 1 vehicles, 2 objects, 3 pickups (NativeMemory.cs). -1 = no such export. The __try below is SHDN's
// guard, but it catches nothing in this build (zig, windows-gnu; see Peek below): ScriptHookRDR2's own pool walk
// is trusted, as it has been in every release.
extern "C" __declspec(dllexport) int RDR2Coop_worldGetAll(int kind, int* arr, int arrSize)
{
    if (kind < 0 || kind > 3 || !g_worldGetAll[kind]) return -1;
    __try { return g_worldGetAll[kind](arr, arrSize); }
    __except (EXCEPTION_EXECUTE_HANDLER) { return -1; }
}

// ---- other scripts' statics (0.9.0-beta.8.4: steering the game's own poker) ----
// The game keeps its script threads in an array (rage's sm_Threads: data pointer at +0, two u16 counts at +8 and
// +0xA). Each thread: id +0x08, the script's name hash +0x0C, state +0x10 (2 = killed), stack size in 8-byte slots
// +0x58, stack +0x6B8; static N is slot N of the stack. Layout and the two code patterns that find the array come
// from public RDR2 research (scrDbg, RDR-Classes, HorseMenu, emcifuntik/rdr2scripthook all agree; our own code).
// RDR2.exe is encrypted on disk, so the patterns are searched in memory, once, on the first call (a script tick).
// Every read and write of the game's memory goes through Peek/Poke (the kernel copies it), so a wrong guess or a
// script that just ended fails the call instead of taking the game down. __try/__except does NOT work in this build
// (zig, x86_64-windows-gnu): tested under Wine on 27 Oct, an unhandled page fault; a mission's end crashed the game.
// The managed side checks the array against the game's own thread iterator before it trusts it (ScriptMemory.cs).
namespace {
struct ThreadArray { uint8_t** data; uint16_t a, b; };
ThreadArray* g_threads = nullptr;
int g_threadScan = 0;   // 0 not searched yet, 1 candidates found, -1 none
ThreadArray* g_candidates[32];   // every plausible array the patterns point at: the managed side picks the one the game's
int g_candidateCount = 0;        // own thread iterator agrees with (26 Oct: the two patterns' first matches differed)

struct Pattern { uint8_t b[24]; bool any[24]; int n; };

Pattern ParsePattern(const char* s)
{
    Pattern p = {};
    while (*s && p.n < 24) {
        while (*s == ' ') s++;
        if (!*s) break;
        if (*s == '?') { p.any[p.n++] = true; s++; continue; }
        unsigned v = 0;
        sscanf(s, "%2x", &v);
        p.b[p.n++] = (uint8_t)v;
        s += 2;
    }
    return p;
}

// The matches (at most max) in the main module's executable sections (committed, readable pages only).
int FindPattern(const Pattern& p, uint8_t** out, int max)
{
    int found = 0;
    auto base = (uint8_t*)GetModuleHandleW(nullptr);
    auto nt = (IMAGE_NT_HEADERS64*)(base + ((IMAGE_DOS_HEADER*)base)->e_lfanew);
    auto sec = IMAGE_FIRST_SECTION(nt);
    for (int i = 0; i < nt->FileHeader.NumberOfSections; i++, sec++) {
        if (!(sec->Characteristics & IMAGE_SCN_MEM_EXECUTE)) continue;
        uint8_t* at = base + sec->VirtualAddress;
        uint8_t* end = at + sec->Misc.VirtualSize;
        while (at < end) {
            MEMORY_BASIC_INFORMATION mbi;
            if (!VirtualQuery(at, &mbi, sizeof(mbi))) break;
            uint8_t* regionEnd = (uint8_t*)mbi.BaseAddress + mbi.RegionSize;
            if (regionEnd > end) regionEnd = end;
            bool readable = mbi.State == MEM_COMMIT && !(mbi.Protect & (PAGE_GUARD | PAGE_NOACCESS))
                && (mbi.Protect & (PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_READONLY | PAGE_READWRITE | PAGE_EXECUTE_WRITECOPY | PAGE_WRITECOPY));
            if (readable)
                for (uint8_t* q = at; q + p.n <= regionEnd; q++) {
                    int k = 0;
                    while (k < p.n && (p.any[k] || q[k] == p.b[k])) k++;
                    if (k == p.n) { out[found++] = q; if (found == max) return found; }
                }
            at = regionEnd;
        }
    }
    return found;
}

uint8_t* Rip(uint8_t* at, int disp) { return at ? at + disp + 4 + *(int32_t*)(at + disp) : nullptr; }

bool Peek(const void* at, void* out, size_t n) { SIZE_T got = 0; return ReadProcessMemory(GetCurrentProcess(), at, out, n, &got) && got == n; }
bool Poke(void* at, const void* in, size_t n) { SIZE_T put = 0; return WriteProcessMemory(GetCurrentProcess(), at, in, n, &put) && put == n; }
template <class T> bool Peek(const void* at, T& v) { return Peek(at, &v, sizeof v); }

// The array's count and data pointer, read safely. False if unreadable.
bool ArrayHead(ThreadArray* t, uint8_t**& data, uint16_t& n)
{
    ThreadArray copy;
    if (!t || !Peek(t, copy)) return false;
    data = copy.data;
    n = copy.a < copy.b ? copy.a : copy.b;
    return true;
}

// A thread's id, name hash and state, read safely. False if unreadable.
struct ThreadHead { uint32_t id, hash, state; };
bool HeadOf(uint8_t* t, ThreadHead& h)
{
    uint32_t v[3];
    if (!t || !Peek(t + 0x08, v, sizeof v)) return false;
    h = { v[0], v[1], v[2] };
    return true;
}

// The thread pointer at index i of the array, or null.
uint8_t* ThreadAt(uint8_t** data, uint16_t i)
{
    uint8_t* t = nullptr;
    return Peek(data + i, t) ? t : nullptr;
}

// A plausible thread array: a data pointer, 1 to 1024 threads, the first entries readable thread objects.
bool Plausible(ThreadArray* c)
{
    uint8_t** data; uint16_t n;
    if (!ArrayHead(c, data, n) || !data || n == 0 || n > 1024) return false;
    for (uint16_t i = 0; i < n && i < 8; i++) {
        uint8_t* th = ThreadAt(data, i);
        ThreadHead h;
        if (th && !HeadOf(th, h)) return false;
    }
    return true;
}

void SearchThreads()
{
    uint8_t* hits[32];
    int n = 0;
    const char* patterns[] = { "48 8D 0D ? ? ? ? E8 ? ? ? ? EB 0B 8B 0D", "48 8B 05 ? ? ? ? FF C2" };
    for (const char* pat : patterns) {
        int got = FindPattern(ParsePattern(pat), hits, 32);   // reads only committed, readable code pages
        for (int i = 0; i < got && g_candidateCount < 32; i++) {
            auto c = (ThreadArray*)Rip(hits[i], 3);
            bool dup = false;
            for (int k = 0; k < g_candidateCount; k++) dup = dup || g_candidates[k] == c;
            if (!dup && Plausible(c)) g_candidates[g_candidateCount++] = c;
        }
        n += got;
    }
    if (g_candidateCount == 0) { Log("script threads: no plausible thread array in this game build (%d pattern matches): script memory off", n); g_threadScan = -1; return; }
    g_threads = g_candidates[0];
    g_threadScan = 1;
    Log("script threads: %d candidate array(s) from %d pattern matches; the mod checks which is the game's", g_candidateCount, n);
}

// The running (not killed) thread of the script with this name hash, or null; its head in h.
uint8_t* FindThread(uint32_t hash, ThreadHead& h)
{
    uint8_t** data; uint16_t n;
    if (!ArrayHead(g_threads, data, n)) return nullptr;
    for (uint16_t i = 0; i < n && i < 1024; i++) {
        uint8_t* t = ThreadAt(data, i);
        if (t && HeadOf(t, h) && h.id && h.hash == hash && h.state != 2) return t;
    }
    return nullptr;
}
} // namespace

// How many candidate arrays there are, and choose one (which < count) for the calls below. -1: none.
extern "C" __declspec(dllexport) int RDR2Coop_scriptUse(int which)
{
    if (g_threadScan == 0) SearchThreads();
    if (g_threadScan != 1) return -1;
    if (which >= 0 && which < g_candidateCount) g_threads = g_candidates[which];
    return g_candidateCount;
}

// The running script threads as the game's array has them: ids and name hashes, max at most. Returns how many
// (-1: the array wasn't found in this build, -2: it can't be read). The managed side compares this with the
// game's own thread iterator before using RDR2Coop_scriptStatics.
extern "C" __declspec(dllexport) int RDR2Coop_scriptThreads(uint32_t* ids, uint32_t* hashes, int max)
{
    if (g_threadScan == 0) SearchThreads();
    if (g_threadScan != 1 || max <= 0) return -1;
    uint8_t** data; uint16_t n;
    if (!ArrayHead(g_threads, data, n)) return -2;
    int out = 0;
    for (uint16_t i = 0; i < n && i < 1024 && out < max; i++) {
        ThreadHead h;
        uint8_t* t = ThreadAt(data, i);
        if (!t || !HeadOf(t, h) || !h.id || h.state == 2) continue;
        ids[out] = h.id;
        hashes[out] = h.hash;
        out++;
    }
    return out;
}

// Statics index..index+count-1 of the running script with this name hash, read into buf (write = 0) or written from
// it (write = 1). Returns the thread's id (> 0), 0 when no such script runs, -1 no thread array, -3 out of its
// stack, -4 unreadable (e.g. the script just ended). Call it from the script tick only (the script engine's thread).
extern "C" __declspec(dllexport) int RDR2Coop_scriptStatics(uint32_t hash, int index, int count, uint64_t* buf, int write)
{
    if (g_threadScan == 0) SearchThreads();
    if (g_threadScan != 1) return -1;
    if (index < 0 || count <= 0 || count > 16384 || !buf) return -3;   // one call reads a whole script (poker_sp: 4844 slots)
    ThreadHead h;
    uint8_t* t = FindThread(hash, h);
    if (!t) return 0;
    uint64_t* stack = nullptr;
    uint32_t size = 0;
    if (!Peek(t + 0x6B8, stack) || !Peek(t + 0x58, size)) return -4;
    if (!stack || (uint32_t)index + (uint32_t)count > size) return -3;
    bool ok = write ? Poke(stack + index, buf, (size_t)count * 8) : Peek(stack + index, buf, (size_t)count * 8);
    return ok ? (int)h.id : -4;
}

// The running script's stack size and, at its last yield, its stack and frame pointers (slots; the context after
// the thread id: +0x14 ip, +0x18 frame, +0x1C stack, as in the game's scrThread). Statics sit at the bottom, so
// [0, sp) is everything in use. Returns the thread id (> 0), 0 not running, -1 no thread array, -4 unreadable.
extern "C" __declspec(dllexport) int RDR2Coop_scriptInfo(uint32_t hash, uint32_t* size, uint32_t* sp, uint32_t* fp)
{
    if (g_threadScan == 0) SearchThreads();
    if (g_threadScan != 1 || !size || !sp || !fp) return -1;
    ThreadHead h;
    uint8_t* t = FindThread(hash, h);
    if (!t) return 0;
    uint32_t ctx[2];
    if (!Peek(t + 0x58, *size) || !Peek(t + 0x18, ctx, sizeof ctx)) return -4;
    *fp = ctx[0];
    *sp = ctx[1];
    return (int)h.id;
}

BOOL APIENTRY DllMain(HMODULE h, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_DETACH) {
        if (scriptUnregister) scriptUnregister(h);
        if (keyboardHandlerUnregister) keyboardHandlerUnregister(KeyboardMessage);
        return TRUE;
    }
    if (reason != DLL_PROCESS_ATTACH) return TRUE;
    DisableThreadLibraryCalls(h);

    wchar_t path[1024];
    GetModuleFileNameW(h, path, 1024);
    g_base = path;
    g_base.erase(g_base.find_last_of(L"\\/") + 1);
    CreateDirectoryW((g_base + L"scripts").c_str(), nullptr);   // the ini and the logs live there; a fresh install may not have it
    g_logPath = g_base + L"scripts\\RDR2Coop.Host.log";
    if (FILE* f = _wfopen(g_logPath.c_str(), L"w")) fclose(f);   // one log per game start
    Log("RDR2Coop.asi " RDR2COOP_VERSION " loaded from %ls", g_base.c_str());

    // Not loaded yet when the .asi loader reaches us (SHDN gets it through its import table); load it ourselves,
    // by full path so a bare name never falls through to a copy somewhere on PATH.
    HMODULE sh = GetModuleHandleA("ScriptHookRDR2.dll");
    if (!sh) sh = LoadLibraryW((g_base + L"ScriptHookRDR2.dll").c_str());
    if (!sh) { Log("ScriptHookRDR2.dll is not installed or failed to load (error %lu): loader idle", GetLastError()); return TRUE; }

    bool ok = Resolve(sh, "?scriptRegister@@YAXPEAUHINSTANCE__@@P6AXXZ@Z", scriptRegister);
    ok = Resolve(sh, "?scriptUnregister@@YAXPEAUHINSTANCE__@@@Z", scriptUnregister) && ok;
    ok = Resolve(sh, "?scriptWait@@YAXK@Z", scriptWait) && ok;
    ok = Resolve(sh, "?keyboardHandlerRegister@@YAXP6AXKGEHHHH@Z@Z", keyboardHandlerRegister) && ok;
    ok = Resolve(sh, "?keyboardHandlerUnregister@@YAXP6AXKGEHHHH@Z@Z", keyboardHandlerUnregister) && ok;
    for (const char* name : kManagedExports) {
        void* fn;
        ok = Resolve(sh, name, fn) && ok;
    }
    for (int i = 0; i < 4; i++) ok = Resolve(sh, kPoolExports[i], g_worldGetAll[i]) && ok;
    if (!ok) { Log("this ScriptHookRDR2 build lacks exports the mod needs: loader idle"); scriptRegister = nullptr; scriptUnregister = nullptr; keyboardHandlerUnregister = nullptr; return TRUE; }

    scriptRegister(h, ScriptMain);
    keyboardHandlerRegister(KeyboardMessage);
    Log("script registered with ScriptHookRDR2");
    return TRUE;
}
