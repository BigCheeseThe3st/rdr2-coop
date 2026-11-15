// Testing aid ([Keys] SoftsaveProbe=): a round trip through the game's own menu-less restore, the one mission replays
// and checkpoint retries use (decompiled 1491.50: startup_sp func_15 copies a softsave slot back into the live save
// globals and restarts main and the update scripts). It takes a fresh slot-1 snapshot of THIS game, copies the live
// save globals into slot 1 with one harmless marker changed (the slot's spawn x, Global_40.f_9.f_7 = Global_56, +40 m),
// and triggers the restore. Logs every step to scripts\RDR2Coop.log ("softsave probe: ..."). If Global_56 comes back
// as the marker, the script data we put in the slot is what the game restored: a guest can be given the host's story
// this way, with no Load Game. Free roam only; nothing from another save is used.
using System;
using RDR2;
using RDR2.Native;
using Keys = System.Windows.Forms.Keys;

namespace RDR2Coop.Mod
{
    sealed class SoftsaveProbe
    {
        public Keys Key = Keys.None, DumpKey = Keys.None, InjectKey = Keys.None;
        static readonly string DumpFile = System.IO.Path.Combine("scripts", "RDR2Coop.worlddump.bin");
        // Kept from OUR game when another world is put in (block 0 = Global_40 offsets): the spawn/clock/bounty fields,
        // the horse, outfits and hair, honor and cores, items, cheats and the save checksum to the block's end.
        static readonly int[,] KeepOwn = { { 9, 30 }, { 1095, 3142 }, { 7157, 599 }, { 11095, 71 }, { 11943, 12066 - 11943 } };
        const int KeepBlock = 6;   // Global_26796 (777): outfits, all ours
        ulong[] _inject;
        public static bool Health;   // set after a run: the next press only reports

        // The nine save-global blocks: live start, softsave staging array base, size (slots). Slot s of block k is at
        // base + 1 + s * size; the live blocks are back to back from Global_40 to Global_36308.
        static readonly int[,] Blocks =
        {
            { 40, 2621440, 12066 }, { 12106, 2645573, 5398 }, { 17504, 2656370, 3206 }, { 20710, 2662783, 2408 },
            { 23118, 2667600, 1769 }, { 24887, 2671139, 1909 }, { 26796, 2674958, 777 }, { 27573, 2676513, 4501 }, { 32074, 2685516, 4234 },
        };
        const int Flags = 1572864 + 3, Restart = 43889, MissionRunning = 1572887 + 12, SpawnX = 56, Honor = 11170, Camp = 4324, Model = 79;
        static readonly uint MainHash = MISC.GET_HASH_KEY("main"), StartupHash = MISC.GET_HASH_KEY("startup_sp");

        int _step;   // 0 idle, 1 snapshot pending, 2 restore running
        long _stepMs, _lastLogMs;
        float _marker;
        int _frames;
        bool _sawMainGone;

        public unsafe bool OnKey(Keys k, long now)
        {
            if (DumpKey != Keys.None && k == DumpKey) { Dump(); return true; }
            if (InjectKey != Keys.None && k == InjectKey)
            {
                try
                {
                    var bytes = System.IO.File.ReadAllBytes(DumpFile);
                    if (bytes.Length != 8 * Total()) { Say($"dump is {bytes.Length} bytes, expected {8 * Total()}"); return true; }
                    _inject = new ulong[Total()];
                    Buffer.BlockCopy(bytes, 0, _inject, 0, bytes.Length);
                    if (_inject[79 - 40] != (ulong)(uint)MissionProbe.GInt(Model) && (_inject[79 - 40] & 0xffffffff) != ((ulong)(uint)MissionProbe.GInt(Model)))
                        Log($"note: the dump's player model {_inject[79 - 40] & 0xffffffff:X8} differs from ours {(uint)MissionProbe.GInt(Model):X8}");
                    Log($"injecting {DumpFile}: dump camp {(int)(uint)_inject[4324 - 40]}, our camp {MissionProbe.GInt(Camp)}");
                }
                catch (Exception e) { Say("no dump to inject: " + e.Message); return true; }
                k = Key;   // then the same restore as the round trip, with the dump's world in slot 1
            }
            if (Key == Keys.None || k != Key) return false;
            if (_step != 0) { Log("already running"); return true; }
            if ((MissionProbe.GInt(Flags) & 32) != 0 && (MissionProbe.GInt(21) & 4096) == 0)
            {   // main waits for Global_21 4096 (startup_sp's spawn step sets it when the restart has bit 4096): release it
                *(int*)Ptr(21) |= 4096;
                Log("main was waiting for Global_21 4096: set it");
                return true;
            }
            if (Health || (MissionProbe.GInt(Flags) & 32) != 0) { Log("health: " + State() + " | " + Threads() + $" | player control {PLAYER.IS_PLAYER_CONTROL_ON(PLAYER.PLAYER_ID())}, frozen?, loading {STREAMING.IS_LOAD_SCENE_ACTIVE()}"); Health = false; return true; }
            if (MissionProbe.GInt(MissionRunning) != -1) { Say("a mission is running: free roam only"); return true; }
            int s0 = MISC._GET_STATUS_OF_SAVEGAME_OPERATION(0), s1 = MISC._GET_STATUS_OF_SAVEGAME_OPERATION(1);
            if (s0 == 1 || s1 == 1) { Say($"a savegame operation is busy ({s0}/{s1})"); return true; }
            if (Ptr(2633507) == null || Ptr(40) + 12066 != Ptr(12106)) { Say("save globals aren't where the 1491.50 scripts put them: stopped"); return true; }
            Log("starting. Before: " + State());
            CAM.DO_SCREEN_FADE_OUT(0);
            bool q = MISC._QUEUE_SAVEGAME_OPERATION(1);
            Log($"queued slot-1 snapshot: {q}");
            _step = 1; _stepMs = now; _frames = 0;
            return true;
        }

        public unsafe void Tick(long now)
        {
            if (_step == 0) return;
            _frames++;
            if (_step == 1)
            {
                int st = MISC._GET_STATUS_OF_SAVEGAME_OPERATION(1);
                if (st == 1 && now - _stepMs < 10000) return;
                Log($"snapshot status {st} after {_frames} frames, {now - _stepMs} ms");
                if (st == 2 || st == 1) { Log("snapshot failed: stopped"); CAM.DO_SCREEN_FADE_IN(500); _step = 0; return; }
                for (int k = 0; k < Blocks.GetLength(0); k++)
                {
                    long* src = Ptr(Blocks[k, 0]), dst = Ptr(Blocks[k, 1] + 1 + Blocks[k, 2]);
                    if (src == null || dst == null) { Log($"block {k}: no address: stopped"); CAM.DO_SCREEN_FADE_IN(500); _step = 0; return; }
                    Buffer.MemoryCopy(src, dst, 8L * Blocks[k, 2], 8L * Blocks[k, 2]);
                }
                if (_inject != null)
                {
                    int at = 0;
                    for (int k = 0; k < Blocks.GetLength(0); k++)
                    {
                        long* dst = Ptr(Blocks[k, 1] + 1 + Blocks[k, 2]);
                        if (k != KeepBlock)
                        {
                            var keep = new ulong[Blocks[k, 2]];
                            if (k == 0) for (int r = 0; r < KeepOwn.GetLength(0); r++) for (int j = 0; j < KeepOwn[r, 1]; j++) keep[KeepOwn[r, 0] + j] = (ulong)dst[KeepOwn[r, 0] + j];
                            for (int j = 0; j < Blocks[k, 2]; j++) dst[j] = (long)_inject[at + j];
                            if (k == 0) for (int r = 0; r < KeepOwn.GetLength(0); r++) for (int j = 0; j < KeepOwn[r, 1]; j++) dst[KeepOwn[r, 0] + j] = (long)keep[KeepOwn[r, 0] + j];
                        }
                        at += Blocks[k, 2];
                    }
                    Log("slot 1 now holds the dump's world with our own player data kept");
                    _inject = null;
                }
                int slot1 = Blocks[0, 1] + 1 + Blocks[0, 2];   // Global_2633507: slot 1 of block 0 (= Global_40's layout)
                float x = Game.Player.Character.Position.X;
                _marker = x + 40f;
                *(float*)Ptr(slot1 + 9 + 7) = _marker;          // f_9.f_7.x: the slot's spawn point
                *(int*)Ptr(slot1 + 9) = 5;                      // f_9: save type 5 (checkpoint)
                *(int*)Ptr(slot1 + 9 + 20) = LAW.GET_BOUNTY(PLAYER.PLAYER_ID());
                Log($"copied 9 blocks live -> slot 1; marker spawn x {_marker:0.00} (live x {x:0.00}, live Global_56 {GFloat(SpawnX):0.00})");
                *(int*)Ptr(Flags) |= 8 | 32 | 4096;             // restart (32) from slot 1 (8), rerun the spawn step (4096: it releases main); never 2 (mission retry)
                BRAIN.DISABLE_SCRIPT_BRAIN_SET(1);
                BRAIN.DISABLE_SCRIPT_BRAIN_SET(2);
                SCRIPTS._0x11B0A0B282FA9B10(false);
                *(int*)Ptr(Restart) = 1;
                Log($"triggered: flags {MissionProbe.GInt(Flags)}, Global_43889 {MissionProbe.GInt(Restart)}");
                _step = 2; _stepMs = now; _lastLogMs = 0; _sawMainGone = false;
                return;
            }
            // Restore running: watch startup_sp's state, main coming and going, the restart bit.
            int mains = SCRIPTS.GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH(MainHash);
            if (mains == 0) _sawMainGone = true;
            bool restarting = (MissionProbe.GInt(Flags) & 32) != 0;
            if (now - _lastLogMs >= 250)
            {
                _lastLogMs = now;
                Log($"t+{now - _stepMs} ms: startup_sp state {StartupState()}, main threads {mains}, restart bit {restarting}, Global_43889 {MissionProbe.GInt(Restart)}, faded out {CAM.IS_SCREEN_FADED_OUT()}");
            }
            if ((_sawMainGone && mains > 0 && !restarting && now - _stepMs > 1500) || now - _stepMs > 30000)
            {
                bool done = now - _stepMs <= 30000;
                CAM.DO_SCREEN_FADE_IN(800);
                float g56 = GFloat(SpawnX);
                Log((done ? "restore finished" : "TIMED OUT after 30 s") + $" in {now - _stepMs} ms. After: " + State() +
                    $" | live Global_56 {g56:0.00} vs marker {_marker:0.00}: " + (Math.Abs(g56 - _marker) < 0.01f ? "MARKER SURVIVED (our slot data was restored)" : "marker gone"));
                Say(done ? (Math.Abs(g56 - _marker) < 0.01f ? "Co-op test: softsave round trip worked, marker survived" : "Co-op test: softsave round trip done, marker gone")
                         : "Co-op test: softsave round trip timed out");
                *(int*)Ptr(Flags) &= ~4096;   // the 4096 restart leaves its bit set
                _step = 0;
                Health = true;
                Log("threads now: " + Threads());
            }
        }

        static int Total() { int n = 0; for (int k = 0; k < Blocks.GetLength(0); k++) n += Blocks[k, 2]; return n; }

        unsafe void Dump()
        {
            var all = new byte[8 * Total()];
            int at = 0;
            for (int k = 0; k < Blocks.GetLength(0); k++)
            {
                long* src = Ptr(Blocks[k, 0]);
                if (src == null) { Say("no address for block " + k); return; }
                fixed (byte* d = &all[at]) Buffer.MemoryCopy(src, d, 8L * Blocks[k, 2], 8L * Blocks[k, 2]);
                at += 8 * Blocks[k, 2];
            }
            System.IO.File.WriteAllBytes(DumpFile, all);
            Say($"world dumped ({all.Length} bytes) to {DumpFile}");
            Log("dump state: " + State());
        }

        static string Threads()
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var n in new[] { "main", "long_update", "medium_update", "short_update", "flow_controller", "player_horse", "startup_sp" })
                parts.Add(n + " " + SCRIPTS.GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH(MISC.GET_HASH_KEY(n)));
            return string.Join(", ", parts);
        }

        static string State()
        {
            // plus a story fingerprint: how many of the mission-completion block's slots are set
            int set = 0; for (int i = 12106; i < 17504; i++) if (MissionProbe.GInt(i) != 0) set++;
            var p = Game.Player.Character.Position;
            return $"cash {MONEY._MONEY_GET_CASH_BALANCE()}, honor {MissionProbe.GInt(Honor)}, bounty {LAW.GET_BOUNTY(PLAYER.PLAYER_ID())}, pos {p.X:0.0},{p.Y:0.0},{p.Z:0.0}, " +
                   $"camp {MissionProbe.GInt(Camp)}, model {MissionProbe.GInt(Model)}, engine slot0 {MISC._0x7CF96F1250EF3221(0)} slot1 {MISC._0x7CF96F1250EF3221(1)}, " +
                   $"flags {MissionProbe.GInt(Flags)}, Global_43889 {MissionProbe.GInt(Restart)}, main threads {SCRIPTS.GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH(MainHash)}, " +
                   $"startup_sp state {StartupState()}, Global_56 {GFloat(SpawnX):0.00}, weapon {Peds.CurrentWeapon(Game.Player.Character.Handle):X8}, Global_12106 block non-zero slots {set}, sFlow f_40 {MissionProbe.GInt(80)}";
        }

        static string StartupState()
        {
            var buf = new ulong[1];
            return ScriptMemory.Read(StartupHash, 18, buf, 0, 1) > 0 ? ((int)(uint)buf[0]).ToString() : "?";
        }

        static unsafe long* Ptr(int global) => (long*)Game.GetGlobalPtr(global).MemoryAddress;
        static unsafe float GFloat(int global) { var p = (float*)Game.GetGlobalPtr(global).MemoryAddress; return p == null ? float.NaN : *p; }
        static void Log(string s) => CoopScript.Log("softsave probe: " + s);
        static void Say(string s) { Log(s); CoopScript.Say("Co-op test: " + s); }
    }
}
