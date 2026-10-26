// RDR2Coop.asi: the loader. ScriptHookRDR2 loads it like any .asi; it registers one script, starts
// .NET 9 from RDR2Coop\runtime on the script fiber, loads RDR2Coop\RDR2Coop.Core.dll and hands it
// every tick and key press. Nothing here may take the game down: each failure is one line in
// scripts\RDR2Coop.Host.log and the loader goes idle. Contract with the managed side: api/Core/Host.cs (Init/Tick/Key).
// Built with zig (clang, mingw target): ScriptHookRDR2's C++ exports are therefore resolved by their
// MSVC-mangled names instead of being linked.
#include <windows.h>
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

// The script ScriptHookRDR2 runs for us, on the fiber it made for this thread. Same loop as SHDN's
// DllMain.cpp: a new fiber means the game reloaded its scripts, so the managed side is initialised again.
void ScriptMain()
{
    void* fiber = GetCurrentFiber();
    if (!StartManaged()) Idle("loader idle");
    Log(".NET runtime up");
    for (;;) {
        int rc = g_init(g_base.c_str());
        if (rc != 0) { Log("RDR2Coop.Core Init returned %d (its own log says why)", rc); Idle("loader idle"); }
        g_ready = true;
        while (GetCurrentFiber() == fiber) { g_tick(); scriptWait(0); }
        g_ready = false;
        fiber = GetCurrentFiber();
        Log("game reloaded its scripts (new fiber): initialising the managed side again");
    }
}

// ScriptHookRDR2 calls this from the game's window procedure, on the same thread the fibers run on.
void KeyboardMessage(DWORD key, WORD, BYTE, BOOL, BOOL isWithAlt, BOOL, BOOL isUpNow)
{
    if (!g_ready) return;
    g_key(key, !isUpNow, (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0, (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0, isWithAlt != FALSE);
}

} // namespace

// kind: 0 peds, 1 vehicles, 2 objects, 3 pickups (NativeMemory.cs). -1 = the pool walk faulted (or no such
// export); the managed side treats that as an empty list, as SHDN's catch did on .NET Framework.
extern "C" __declspec(dllexport) int RDR2Coop_worldGetAll(int kind, int* arr, int arrSize)
{
    if (kind < 0 || kind > 3 || !g_worldGetAll[kind]) return -1;
    __try { return g_worldGetAll[kind](arr, arrSize); }
    __except (EXCEPTION_EXECUTE_HANDLER) { return -1; }
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
