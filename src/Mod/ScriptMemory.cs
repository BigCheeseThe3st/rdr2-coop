// Another running game script's static locals (0.9.0-beta.8.4), for steering the game's own poker. The loader
// (host/RDR2Coop.cpp) finds the game's script thread array in memory and reads or writes a script's statics inside
// __try. Before trusting it, this checks it once against the game's own thread iterator: every thread id the game
// lists must be in the array with the same script hash, or the feature stays off (logged). Only the .NET 9 build has
// the loader; the ScriptHookRDR2DotNet build answers "not available".
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using RDR2.Native;

namespace RDR2Coop.Mod
{
    static class ScriptMemory
    {
        public const int NotRunning = 0;
        static int _state;   // 0 not checked, 1 ok, -1 off
        public static bool Ready => _state == 1;
        /// <summary>The game folder (RDR2.exe and the loader).</summary>
        public static string GameDir() { try { return Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName); } catch (Exception) { return "."; } }

#if NET48
        public static bool Check(string gameDir) { if (_state == 0) { _state = -1; CoopScript.Log("script memory: needs the mod's own loader (the .NET 9 build)"); } return false; }
        public static int Read(uint script, int index, ulong[] buf) => -1;
        public static int Write(uint script, int index, ulong[] buf) => -1;
        public static int Read(uint script, int index, ulong[] buf, int offset, int count) => -1;
        public static int Info(uint script, out int size, out int sp) { size = sp = 0; return -1; }
        public static int Threads(uint[] ids, uint[] hashes) => -1;
#else
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] unsafe delegate int ThreadsFn(uint* ids, uint* hashes, int max);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] unsafe delegate int StaticsFn(uint hash, int index, int count, ulong* buf, int write);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int UseFn(int which);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] unsafe delegate int InfoFn(uint hash, uint* size, uint* sp, uint* fp);
        static InfoFn _info;   // null with a loader older than 0.9.0-beta.8.4
        static ThreadsFn _threads;
        static UseFn _use;
        static StaticsFn _statics;

        /// <summary>Once: is the loader's thread array the game's? Call from the script tick.</summary>
        public static unsafe bool Check(string gameDir)
        {
            if (_state != 0) return _state == 1;
            _state = -1;
            try
            {
                IntPtr lib = NativeLibrary.Load(Path.Combine(gameDir, "RDR2Coop.asi"));
                if (!NativeLibrary.TryGetExport(lib, "RDR2Coop_scriptThreads", out IntPtr t) || !NativeLibrary.TryGetExport(lib, "RDR2Coop_scriptStatics", out IntPtr st)
                    || !NativeLibrary.TryGetExport(lib, "RDR2Coop_scriptUse", out IntPtr u))
                { CoopScript.Log("script memory: the loader is older than this mod (no script exports): off"); return false; }
                _threads = Marshal.GetDelegateForFunctionPointer<ThreadsFn>(t);
                _statics = Marshal.GetDelegateForFunctionPointer<StaticsFn>(st);
                _use = Marshal.GetDelegateForFunctionPointer<UseFn>(u);
                if (NativeLibrary.TryGetExport(lib, "RDR2Coop_scriptInfo", out IntPtr inf)) _info = Marshal.GetDelegateForFunctionPointer<InfoFn>(inf);
                // The game's own list: every thread id and its script hash.
                var game = new Dictionary<uint, uint>();
                SCRIPTS.SCRIPT_THREAD_ITERATOR_RESET();
                for (int id; (id = SCRIPTS.SCRIPT_THREAD_ITERATOR_GET_NEXT_THREAD_ID()) != 0 && game.Count < 1024;) game[(uint)id] = SCRIPTS._GET_HASH_OF_THREAD(id);
                int candidates = _use(-1);
                if (candidates < 0) { CoopScript.Log("script memory: the loader found no script thread array: off (its log says why)"); return false; }
                var ids = new uint[1024];
                var hashes = new uint[1024];
                var tried = new List<string>();
                for (int c = 0; c < candidates; c++)   // the candidate whose threads are exactly the game's
                {
                    _use(c);
                    int n;
                    fixed (uint* pi = ids) fixed (uint* ph = hashes) n = _threads(pi, ph, ids.Length);
                    int wrong = 0;
                    var mine = new Dictionary<uint, uint>();
                    for (int i = 0; i < Math.Max(0, n); i++) mine[ids[i]] = hashes[i];
                    foreach (var kv in game) if (!mine.TryGetValue(kv.Key, out uint h) || h != kv.Value) wrong++;
                    tried.Add($"#{c}: {n} read, {wrong} of {game.Count} differ");
                    if (n > 0 && game.Count > 0 && wrong == 0)
                    {
                        _state = 1;
                        CoopScript.Log($"script memory: ready (candidate {c + 1} of {candidates}: {game.Count} script threads, all match)");
                        return true;
                    }
                }
                CoopScript.Log("script memory: no candidate array matches the game's threads (" + string.Join("; ", tried) + "): off");
                return false;
            }
            catch (Exception e) { CoopScript.Log("script memory: " + e.Message + ": off"); return false; }
        }

        /// <summary>Statics index.. of the running script (its name hash) into buf. The thread id (> 0), 0 not running, < 0 an error.</summary>
        public static unsafe int Read(uint script, int index, ulong[] buf)
        {
            if (_state != 1 || buf.Length == 0) return -1;
            fixed (ulong* p = buf) return _statics(script, index, buf.Length, p, 0);
        }

        /// <summary>Slots index..index+count-1 into buf[offset..]: the thread id (> 0), 0 not running, < 0 an error.</summary>
        public static unsafe int Read(uint script, int index, ulong[] buf, int offset, int count)
        {
            if (_state != 1 || count <= 0 || offset < 0 || offset + count > buf.Length) return -1;
            fixed (ulong* p = &buf[offset]) return _statics(script, index, count, p, 0);
        }

        /// <summary>The running script threads (ids and name hashes) into the arrays: how many, or < 0 an error.</summary>
        public static unsafe int Threads(uint[] ids, uint[] hashes)
        {
            if (_state != 1) return -1;
            fixed (uint* pi = ids) fixed (uint* ph = hashes) return _threads(pi, ph, Math.Min(ids.Length, hashes.Length));
        }

        /// <summary>The running script's stack size and stack pointer in slots (statics at the bottom, so [0, sp) is
        /// in use): the thread id (> 0), 0 not running, < 0 an error or a loader without it.</summary>
        public static unsafe int Info(uint script, out int size, out int sp)
        {
            size = sp = 0;
            if (_state != 1 || _info == null) return -1;
            uint z, s, f;
            int r = _info(script, &z, &s, &f);
            size = (int)z; sp = (int)s;
            return r;
        }

        /// <summary>Writes buf over statics index.. of the running script. Only for slots known to hold plain values.</summary>
        public static unsafe int Write(uint script, int index, ulong[] buf)
        {
            if (_state != 1 || buf.Length == 0) return -1;
            fixed (ulong* p = buf) return _statics(script, index, buf.Length, p, 1);
        }
#endif
    }
}
