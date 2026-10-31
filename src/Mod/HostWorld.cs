// The host's world for guests (0.9.0-beta.9, [Gameplay] HostWorld; the format and checks: src/Net/WorldShare.cs, the
// guest's saves: src/Net/SaveGuard.cs). Host: sends its story globals to a guest who asks, then streams what changes
// twice a second, with sums every 5 s so lost changes get resent. Guest: once in someone else's session, backs up its
// saves and its own story, takes the host's story through the game's own menu-less restore (the mission
// replay/checkpoint path: proven 28 Oct with the 100% save's world in George's game, his own cash, clothes and map
// kept), sends the "spawn done" signal the restart waits for, is put next to the host, then writes the stream straight
// into its live globals. Its own saves are guarded the whole time (_guard set = we're in someone else's story). Leaving
// the session, losing the connection or the host changing takes our own story back the same way; the guard only ends
// after that restore really finished. Ticked every frame whatever the connection or role (CoopScript.HostWorldTick).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RDR2;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class HostWorld
    {
        public static bool On = true;
        public static bool Swapping { get; private set; }   // the restore is running: the screen is ours (black); others keep off

        const int Flags = 1572864 + 3, Restart = 43889, MissionRunning = 1572887 + 12, Camp = 4324, Model = 79, Spawned = 21;
        // Story flow: the save keeps a copy at Global_40.f_40 (243 slots), the running scripts use Global_1879534.f_7301;
        // the game copies one to the other at save (flow_controller func_1492) and load (startup_sp func_128).
        const int FlowSave = 80, FlowLive = 1879534 + 7301, FlowSlots = 243;
        const int JoinAfterMs = 8000, AskEveryMs = 4000, MissEveryMs = 2000, GuardEveryMs = 2000, SwapTimeoutMs = 90000, RetryLeaveMs = 15000;
        // A dropped connection waits this long before our own story comes back: a guest whose link blipped went to their
        // own story and straight back into the host's, two swaps a time (a tester, 30 Oct, three times in half an hour).
        const int DropGraceMs = 30000;
        // A guest's own player (cash, horse, items, clothes: WorldLayout.Own) goes into own-world.bin every CheckpointMs while
        // in the host's story, and the game saves their own story as soon as it's back (George, 30 Oct: what a guest buys in
        // the host's game should reach their own save by itself). After a crash the checkpoint can be brought back (asked).
        const int CheckpointMs = 120000, AutosaveAfterMs = 3000;
        const uint AutosaveType = 0xF4AE69EC;   // SAVEGAMETYPE_SP_AUTOSAVE, as the game's own autosaves (flow_controller, long_update)
        long _lastCheckpointMs, _autosaveAt = -1, _askRecoverMs = -1, _lastRecoverSay;
        bool _ownFromFile;   // the leave restores own-world.bin's own player as it is (the checkpoint), not the live one
        long _outSinceMs = -1;
        static readonly string Root = Path.Combine("RDR2Coop", "backup");
        static readonly string OwnFile = Path.Combine(Root, "own-world.bin");
        // Written when we start taking our own story back, deleted once it's back in the game: a mod that starts again
        // before then (the loader starts it twice when a save loads, 30 Oct: the second start only ended the guard and
        // left a guest in the host's story with saving unguarded) finishes the job.
        static readonly string LeaveFile = Path.Combine(Root, "leave.pending");

        // host
        readonly Dictionary<byte, Queue<string>> _outgoing = new Dictionary<byte, Queue<string>>();
        readonly Dictionary<byte, (uint id, List<string> pieces)> _transfers = new Dictionary<byte, (uint, List<string>)>();
        readonly HashSet<byte> _guests = new HashSet<byte>();
        ulong[] _sent, _scratch;
        uint _seq, _transfer;
        long _lastDiffMs, _lastSumsMs, _lastPieceMs;

        // guest
        enum G { Off, Asking, Swapping, Joined, LeavePending, Leaving }
        G _g = G.Off;
        readonly WorldStateAssembler _asm = new WorldStateAssembler();
        ulong[] _world, _pending;      // our view of the host's world; a world waiting to be swapped in
        bool _pendingOwn, _reasking;   // the pending world is our own (leaving); asking again while already in the host's story
        SaveGuard _guard;              // set = we're (or may be) in someone else's story: saves guarded
        long _connectedMs = -1, _lastAskMs, _lastMissMs, _lastGuardMs, _lastChunkAskMs, _leaveFailedMs = -RetryLeaveMs;
        uint _lastSumsSeq = uint.MaxValue, _lastDiffSeq;
        int _swapStep, _leftRepeats;
        long _swapMs;
        byte _host;
        readonly Func<byte, PlayerState?> _stateOf;

        public HostWorld(Func<byte, PlayerState?> stateOf) { _stateOf = stateOf; }

        public bool InHostStory => _guard != null;

        /// <summary>Every frame, connected or not, host or guest.</summary>
        public void Tick(ServerLink link, long now)
        {
            if (_g == G.Swapping || _g == G.Leaving) { SwapTick(link, now); return; }
            GuardTick(now);
            CheckpointTick(now);
            AutosaveTick(now);
            if (_askRecoverMs >= 0) AskRecover(now);
            bool connected = link != null && link.State == LinkState.Connected;
            if (connected && _leftRepeats > 0 && now - _lastAskMs >= 1000) { _leftRepeats--; _lastAskMs = now; link.SendNotice(_host, NoticeKind.WorldShare, "left"); }
            if (connected && link.IAmHost && !InHostStory && _g == G.Off) { HostTick(link, now); return; }
            GuestTick(link, connected && !link.IAmHost && link.Roster.Count > 1, now);
        }

        // ---- host ----------------------------------------------------------------------------------------------

        void HostTick(ServerLink link, long now)
        {
            if (_guests.Count == 0 && _outgoing.Count == 0) { _sent = null; return; }
            if (now - _lastPieceMs >= 100)   // full transfers, paced
            {
                _lastPieceMs = now;
                foreach (var kv in _outgoing.ToList())
                {
                    for (int i = 0; i < WorldShare.PiecesPerTick && kv.Value.Count > 0; i++) link.SendNotice(kv.Key, NoticeKind.WorldShare, kv.Value.Dequeue());
                    if (kv.Value.Count == 0) _outgoing.Remove(kv.Key);
                }
            }
            if (_guests.Count == 0 || !Ready()) return;
            if (now - _lastDiffMs >= WorldShare.SendEveryMs)
            {
                _lastDiffMs = now;
                if (_scratch == null) _scratch = new ulong[WorldLayout.Total];
                ReadLive(_scratch);
                if (_sent != null)
                {
                    var lines = WorldShare.Diff(++_seq, _sent, _scratch);
                    foreach (byte g in _guests) foreach (var l in lines) link.SendNotice(g, NoticeKind.WorldShare, l);
                    if (lines.Count > 8) CoopScript.Log($"host world: {lines.Count} change lines this time");
                }
                var t = _sent ?? new ulong[WorldLayout.Total];   // reuse the two buffers (no 290 KB garbage twice a second)
                _sent = _scratch; _scratch = t;
            }
            if (now - _lastSumsMs >= WorldShare.SumsEveryMs && _sent != null)
            {
                _lastSumsMs = now;
                foreach (byte g in _guests) foreach (var l in WorldShare.Sums(_seq, _sent)) link.SendNotice(g, NoticeKind.WorldShare, l);
            }
        }

        void HostNotice(byte from, string text, ServerLink link)
        {
            if (InHostStory || _g != G.Off) return;   // we're in someone else's story ourselves: never serve it on
            var f = text.Split(' ');
            switch (f[0])
            {
                case "want":
                    if (!Ready()) { CoopScript.Log($"host world: {link.NameOf(from)} asked, but this game's globals don't match the 1491.50 layout"); return; }
                    var live = new ulong[WorldLayout.Total];
                    ReadLive(live);
                    var pieces = WorldShare.Pieces(++_transfer, WorldShare.Pack(live), (uint)MissionProbe.GInt(Model));
                    _transfers[from] = (_transfer, pieces);
                    _outgoing[from] = new Queue<string>(pieces);
                    _guests.Add(from);
                    _sent = null;   // the next diff starts from the next read
                    CoopScript.Log($"host world: sending our world to {link.NameOf(from)} ({pieces.Count} pieces)");
                    break;
                case "miss":
                    if (f.Length < 3 || !_transfers.TryGetValue(from, out var tr) || f[1] != tr.id.ToString()) return;
                    if (!_outgoing.TryGetValue(from, out var q)) _outgoing[from] = q = new Queue<string>();
                    foreach (var p in f.Skip(2).Take(256)) if (int.TryParse(p, out int i) && i >= 0 && i < tr.pieces.Count) q.Enqueue(tr.pieces[i]);
                    break;
                case "chunk":
                    if (_sent != null && _guests.Contains(from) && f.Length == 2 && int.TryParse(f[1], out int c) && c >= 0 && c < WorldLayout.Chunks)
                        foreach (var l in WorldShare.Chunk(_seq, _sent, c)) link.SendNotice(from, NoticeKind.WorldShare, l);
                    break;
                case "left":
                    if (_guests.Remove(from)) CoopScript.Log($"host world: {link.NameOf(from)} is back in their own world");
                    _transfers.Remove(from); _outgoing.Remove(from);
                    break;
            }
        }

        public void OnRosterLeft(byte id) { _guests.Remove(id); _outgoing.Remove(id); _transfers.Remove(id); }

        // ---- guest ---------------------------------------------------------------------------------------------

        void GuardTick(long now)
        {
            if (_guard == null || now - _lastGuardMs < GuardEveryMs) return;
            _lastGuardMs = now;
            try { foreach (var d in _guard.Check()) CoopScript.Log("host world: save guard: " + d); }
            catch (Exception e) { CoopScript.Log("host world: save guard (will retry): " + e.Message); }
        }

        void GuestTick(ServerLink link, bool inSession, long now)
        {
            bool want = On && inSession;
            if (_g == G.LeavePending)
            {
                if (now - _leaveFailedMs >= RetryLeaveMs && Idle()) BeginSwap(link, now);
                return;
            }
            // Leaving: out of the host's session for DropGraceMs (the saves stay guarded meanwhile), or a new host.
            if (InHostStory && !want)
            {
                if (_outSinceMs < 0) { _outSinceMs = now; CoopScript.Log($"host world: out of the host's session: staying in their story for {DropGraceMs / 1000} s in case it's a blip"); }
                if (now - _outSinceMs >= DropGraceMs) { _outSinceMs = -1; BeginLeave("out of the host's session"); }
                return;
            }
            if (_outSinceMs >= 0)
            {
                _outSinceMs = -1;
                if (InHostStory && link.HostId == _host) { Reask(link, "back in the host's session"); return; }   // whatever changed meanwhile, in one catch-up
            }
            if (InHostStory && link.HostId != _host) { BeginLeave("the host changed"); return; }
            if (!want)
            {
                _connectedMs = -1;
                if (_g == G.Asking) { _g = G.Off; _pending = null; _asm.Reset(); }
                return;
            }
            if (_connectedMs < 0) _connectedMs = now;
            if (_g == G.Off && !InHostStory && now - _connectedMs > JoinAfterMs && Idle())
            {
                if (!Ready()) { CoopScript.Log("host world: this game's globals don't match the 1491.50 layout: staying in our own world"); On = false; return; }
                StartAsking(link.HostId);
                CoopScript.Log($"host world: asking {link.NameOf(_host)} for their world");
            }
            if (_g == G.Asking)
            {
                if (link.HostId != _host) StartAsking(link.HostId);   // the host changed while we asked (not in their story yet)
                if (now - _lastAskMs >= AskEveryMs && !_asm.Started) { _lastAskMs = now; link.SendNotice(_host, NoticeKind.WorldShare, "want"); }
                if (_asm.Started && now - _lastMissMs >= MissEveryMs)
                {
                    _lastMissMs = now;
                    var miss = _asm.Missing();
                    if (miss.Count > 0) link.SendNotice(_host, NoticeKind.WorldShare, "miss " + _asm.Id + " " + string.Join(" ", miss.Take(120)));
                }
                // Not while the checkpoint question is up: joining writes own-world.bin, the very checkpoint a Y brings back
                // (reznov pressed Y a minute after joining Weise and got the join-time snapshot instead, 30 Oct; Doc the same
                // with Eva, 31 Oct). Nor before our own story's autosave: Doc rejoined 1 s after taking it back, so the game
                // saved the host's story instead and the guard threw that away.
                if (_pending != null && Idle() && _askRecoverMs < 0 && _autosaveAt < 0) BeginSwap(link, now);
            }
        }

        void StartAsking(byte host)
        {
            _host = host; _asm.Reset(); _pending = null; _lastAskMs = 0; _lastDiffSeq = 0; _lastSumsSeq = uint.MaxValue;
            _reasking = InHostStory; _g = G.Asking;
        }

        void GuestNotice(byte from, string text, ServerLink link, long now)
        {
            if (from != link.HostId || !On || from != _host) return;
            if (text.StartsWith("full "))
            {
                if (_g != G.Asking || _pending != null) return;   // late resent pieces after the world is complete
                var w = _asm.Add(text);
                if (w == null) return;
                if (_asm.Model != (uint)MissionProbe.GInt(Model))
                {
                    CoopScript.Log($"host world: the host plays another character ({_asm.Model:X8}, we're {(uint)MissionProbe.GInt(Model):X8}): staying in our own world");
                    CoopScript.Say($"Co-op: {link.NameOf(from)} plays a different character, so you stay in your own story for now");
                    On = false;
                    if (InHostStory) BeginLeave("the host plays another character"); else { _g = G.Off; _pending = null; }
                    return;
                }
                _pending = w; _pendingOwn = false;
                CoopScript.Log($"host world: {link.NameOf(from)}'s world is here (camp {(int)(uint)w[Camp - WorldLayout.First]}, ours {MissionProbe.GInt(Camp)})");
                return;
            }
            if (_g == G.Off && !InHostStory && text.StartsWith("sums ")) { link.SendNotice(from, NoticeKind.WorldShare, "left"); return; }   // the host thinks we joined
            if (_g != G.Joined || _world == null) return;
            if (text.StartsWith("diff "))
            {
                var f = text.Split(' ');
                if (uint.TryParse(f[1], out uint seq) && seq < _lastDiffSeq) return;   // an older change arriving late: the sums fix anything it held
                _lastDiffSeq = Math.Max(_lastDiffSeq, seq);
                int campWas = (int)(uint)_world[Camp - WorldLayout.First];
                var changed = WorldShare.Apply(text, _world);
                if (changed == null) return;
                WriteLive(changed);
                if ((int)(uint)_world[Camp - WorldLayout.First] != campWas) Reask(link, "the host's camp moved");
            }
            else if (text.StartsWith("sums "))
            {
                var f = text.Split(' ');
                if (!uint.TryParse(f[1], out uint seq)) return;
                var bad = WorldShare.Mismatches(text, _world);
                if (bad == null || bad.Count == 0) return;
                if (seq == _lastSumsSeq && now - _lastChunkAskMs < 1000) return;
                _lastSumsSeq = seq; _lastChunkAskMs = now;
                foreach (int c in bad.Take(8)) link.SendNotice(from, NoticeKind.WorldShare, "chunk " + c);
            }
            else if (text == "reswap") Reask(link, "the host's camp moved");
        }

        void Reask(ServerLink link, string why)
        {
            if (_g != G.Joined) return;
            CoopScript.Log($"host world: {why}: taking their world again");
            StartAsking(_host);
        }

        public void OnNotice(byte from, string text, ServerLink link, long now)
        {
            if (string.IsNullOrEmpty(text) || text.Length > NoticeMsg.MaxBytes) return;
            if (link.IAmHost) HostNotice(from, text, link); else GuestNotice(from, text, link, now);
        }

        // Start-up: a join that didn't end (Insert, a crash, the game quit). Same game process -> our own world back.
        // A new process: the game loaded the newest save; if the guard finds the host's world written there, that's what
        // is loaded now: take our own world back too. Otherwise just end the guard.
        public void AtStart(long now)
        {
            var pending = SaveGuard.Pending(Root);
            if (pending == null) return;
            DateTime started;
            try { started = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(); } catch (Exception) { started = DateTime.MaxValue; }
            DateTime marked = File.GetLastWriteTimeUtc(Path.Combine(Root, SaveGuard.Marker));
            _guard = pending;
            bool sameProcess = started < marked;
            List<string> moved = new List<string>();
            try { moved = pending.Check(true); foreach (var d in moved) CoopScript.Log("host world: after the last session: " + d); }
            catch (Exception e) { CoopScript.Log("host world: save check after the last session (will retry): " + e.Message); }
            bool leaving = File.Exists(LeaveFile);
            if ((sameProcess || moved.Count > 0 || leaving) && File.Exists(OwnFile))
            {
                CoopScript.Log(leaving ? "host world: taking our own story back didn't finish before the mod restarted: doing it now"
                             : sameProcess ? "host world: the mod restarted while in the host's story: taking our own story back"
                                           : "host world: the game loaded a save of the host's story after the last session: taking our own story back");
                BeginLeave(null);
                return;
            }
            EndGuard("the last session ended in the host's story; your own saves are back");
            // The game loaded our own save from before the session: what we had in the host's story (the last checkpoint)
            // isn't in it. Offered back, not forced: the save loaded may not be the one we left from.
            if (File.Exists(OwnFile) && CheckpointDiffers()) { _askRecoverMs = now; CoopScript.Log("host world: the last session's money, horse and items (its checkpoint) aren't in this save: asking (Y/N)"); }
        }

        // Every CheckpointMs in the host's story: our own player as it is now, into own-world.bin (written aside, then moved).
        void CheckpointTick(long now)
        {
            if (!InHostStory || _g != G.Joined || now - _lastCheckpointMs < CheckpointMs || !File.Exists(OwnFile)) return;
            _lastCheckpointMs = now;
            try
            {
                var own = ReadOwnFile();
                var live = new ulong[WorldLayout.Total];
                ReadLive(live);
                int changed = 0;
                var slots = new List<string>();
                for (int i = 0; i < own.Length; i++)
                    if (WorldLayout.IsOwn(WorldLayout.First + i) && own[i] != live[i])
                    {
                        own[i] = live[i];
                        if (++changed <= 8) slots.Add("Global_" + (WorldLayout.First + i));
                    }
                if (changed == 0) return;
                var bytes = new byte[own.Length * 8];
                Buffer.BlockCopy(own, 0, bytes, 0, bytes.Length);
                File.WriteAllBytes(OwnFile + ".new", bytes);
                File.Copy(OwnFile + ".new", OwnFile, true);
                File.Delete(OwnFile + ".new");
                CoopScript.Log($"host world: checkpoint: your own player ({changed} values changed: {string.Join(", ", slots)}{(changed > 8 ? "..." : "")}) kept for your own story");
            }
            catch (Exception e) { CoopScript.Log("host world: checkpoint failed: " + e.Message); }
        }

        static ulong[] ReadOwnFile()
        {
            var bytes = File.ReadAllBytes(OwnFile);
            if (bytes.Length != WorldLayout.Total * 8) throw new InvalidDataException($"{bytes.Length} bytes");
            var own = new ulong[WorldLayout.Total];
            Buffer.BlockCopy(bytes, 0, own, 0, bytes.Length);
            return own;
        }

        // Whether own-world.bin's own player differs from the one loaded now (nothing to bring back otherwise).
        static bool CheckpointDiffers()
        {
            try
            {
                var own = ReadOwnFile();
                var live = new ulong[WorldLayout.Total];
                ReadLive(live);
                for (int i = 0; i < own.Length; i++) if (WorldLayout.IsOwn(WorldLayout.First + i) && own[i] != live[i]) return true;
            }
            catch (Exception e) { CoopScript.Log("host world: couldn't read the last checkpoint: " + e.Message); }
            return false;
        }

        // Our own story is back: the game saves it, as its own autosave does, so what we bought in the host's story is in it.
        void AutosaveTick(long now)
        {
            if (_autosaveAt < 0 || now < _autosaveAt || Swapping) return;
            _autosaveAt = -1;
            bool ok = SAVE.SAVEGAME_SAVE_SP(AutosaveType);
            CoopScript.Log($"host world: saved your own story (autosave {(ok ? "started" : "refused by the game")})");
            if (ok) CoopScript.Say("Co-op: your own story saved, with what you bought in the host's story");
        }

        void AskRecover(long now)
        {
            if (now - _askRecoverMs > 120000)
            {
                _askRecoverMs = -1;
                // Aside, as a decline keeps it: the next join would write over it.
                try { File.Move(OwnFile, OwnFile + ".unanswered-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")); } catch (Exception) { }
                CoopScript.Log("host world: no answer about the last session's checkpoint: left as it is (kept aside in RDR2Coop\\backup)");
                return;
            }
            if (now - _lastRecoverSay > 3000) { _lastRecoverSay = now; CoopScript.Say("Co-op: you left the host's story without saving. Bring back your money, horse and items from it? Y / N"); }
        }

        /// <summary>Y or N while the checkpoint question is up. True when the key was ours.</summary>
        public bool OnKey(System.Windows.Forms.Keys key)
        {
            if (_askRecoverMs < 0 || (key != System.Windows.Forms.Keys.Y && key != System.Windows.Forms.Keys.N)) return false;
            _askRecoverMs = -1;
            if (key == System.Windows.Forms.Keys.N)
            {
                try { File.Move(OwnFile, OwnFile + ".declined-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")); } catch (Exception) { }
                CoopScript.Log("host world: checkpoint declined (kept aside in RDR2Coop\\backup)");
                CoopScript.Say("Co-op: left as it is");
                return true;
            }
            CoopScript.Log("host world: bringing back the last session's checkpoint");
            _ownFromFile = true;
            BeginLeave(null);
            return true;
        }

        void EndGuard(string say)
        {
            try
            {
                foreach (var d in _guard.End(Root)) CoopScript.Log("host world: save guard: " + d);
                _guard = null;
                if (say != null) CoopScript.Log("host world: " + say);
            }
            catch (Exception e) { CoopScript.Log("host world: ending the save guard failed (will retry): " + e.Message); }
        }

        void BeginLeave(string why)
        {
            if (why != null) CoopScript.Log($"host world: {why}: taking our own story back");
            _world = null; _reasking = false; _asm.Reset();
            try
            {
                var bytes = File.ReadAllBytes(OwnFile);
                if (bytes.Length != WorldLayout.Total * 8) throw new InvalidDataException($"{bytes.Length} bytes");
                var own = new ulong[WorldLayout.Total];
                Buffer.BlockCopy(bytes, 0, own, 0, bytes.Length);
                _pending = own; _pendingOwn = true; _g = G.LeavePending;
                try { File.WriteAllText(LeaveFile, DateTime.UtcNow.ToString("o")); } catch (Exception e) { CoopScript.Log("host world: couldn't mark the leave as pending: " + e.Message); }
            }
            catch (Exception e)
            {
                CoopScript.Log("host world: couldn't read our own story (" + e.Message + "): saves stay guarded until the game restarts");
                CoopScript.Say("Co-op: your own story couldn't be put back; your saves are kept safe. Restart the game to get it back.");
                _g = G.Off;   // the guard stays on: nothing of the host's world reaches the saves
            }
        }

        /// <summary>The mod stopping (Insert) mid-restore: never leave the player frozen behind a black screen.</summary>
        public unsafe void OnAbort()
        {
            if (!Swapping) return;
            if ((MissionProbe.GInt(Flags) & 32) != 0) *(int*)Ptr(Spawned) |= 4096;
            *(int*)Ptr(Flags) &= ~4096;
            CAM.DO_SCREEN_FADE_IN(500);
            Swapping = false;
        }

        // ---- the swap: the game's own menu-less restore with a world of our choosing in softsave slot 1 ----------

        unsafe void BeginSwap(ServerLink link, long now)
        {
            if (!_pendingOwn && !InHostStory)
            {
                try
                {
                    string profile = ProfileFolder();
                    if (profile == null) { CoopScript.Log("host world: no save folder found: not joining (your saves couldn't be guarded)"); _pending = null; _g = G.Off; On = false; return; }
                    Directory.CreateDirectory(Root);
                    var own = new ulong[WorldLayout.Total];
                    ReadLive(own);
                    var bytes = new byte[own.Length * 8];
                    Buffer.BlockCopy(own, 0, bytes, 0, bytes.Length);
                    File.WriteAllBytes(OwnFile, bytes);
                    _guard = SaveGuard.Begin(profile, Root, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
                    CoopScript.Log($"host world: saves in {profile} backed up to {_guard.Backup}; own story kept in {OwnFile}");
                }
                catch (Exception e) { CoopScript.Log("host world: backup failed (" + e.Message + "): not joining"); _pending = null; _g = G.Off; On = false; return; }
            }
            CoopScript.Say(_pendingOwn ? "Co-op: back to your own story..." : $"Co-op: joining {link?.NameOf(_host)}'s story...");
            CAM.DO_SCREEN_FADE_OUT(500);
            MISC._QUEUE_SAVEGAME_OPERATION(1);
            _swapStep = 1; _swapMs = now; Swapping = true;
            _g = _pendingOwn ? G.Leaving : G.Swapping;
        }

        unsafe void SwapTick(ServerLink link, long now)
        {
            GuardTick(now);
            long t = now - _swapMs;
            switch (_swapStep)
            {
                case 1:   // the fresh slot-1 snapshot, then the world into slot 1 (always our own player in it), then the restart
                    int st = MISC._GET_STATUS_OF_SAVEGAME_OPERATION(1);
                    if (st == 1 && t < 10000) return;
                    var mine = new ulong[WorldLayout.Total];
                    ReadLive(mine);
                    var world = _ownFromFile ? _pending : WorldLayout.Merge(_pending, mine);   // the checkpoint's own player as it is
                    _ownFromFile = false;
                    int at = 0;
                    for (int k = 0; k < WorldLayout.Blocks.GetLength(0); k++)
                    {
                        long* dst = Ptr(WorldLayout.Blocks[k, 1] + 1 + WorldLayout.Blocks[k, 2]);
                        int n = WorldLayout.Blocks[k, 2];
                        if (dst == null) { CoopScript.Log("host world: softsave slot missing: stopped"); Failed(now); return; }
                        fixed (ulong* src = &world[at]) Buffer.MemoryCopy(src, dst, 8L * n, 8L * n);
                        at += n;
                    }
                    *(int*)Ptr(Flags) |= 8 | 32 | 4096;   // restart from slot 1, rerun the spawn step; never 2 (a mission retry)
                    BRAIN.DISABLE_SCRIPT_BRAIN_SET(1);
                    BRAIN.DISABLE_SCRIPT_BRAIN_SET(2);
                    SCRIPTS._0x11B0A0B282FA9B10(false);
                    *(int*)Ptr(Restart) = 1;
                    _world = _pendingOwn ? null : world;
                    CoopScript.Log($"host world: restore started (snapshot status {st}, {(_pendingOwn ? "our own story" : "the host's story")})");
                    _swapStep = 2; _swapMs = now;
                    return;
                case 2:   // the restart: main waits for the spawn step's signal; send it once the scripts are back
                    bool restarting = (MissionProbe.GInt(Flags) & 32) != 0;
                    if (restarting && t > 3000 && (MissionProbe.GInt(Spawned) & 4096) == 0 && SCRIPTS.GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH(MISC.GET_HASH_KEY("main")) > 0)
                    {
                        *(int*)Ptr(Spawned) |= 4096;
                        CoopScript.Log($"host world: sent the spawn-done signal after {t} ms");
                    }
                    if (restarting && t < SwapTimeoutMs) return;
                    if (restarting) { CoopScript.Log("host world: the restore didn't finish in 90 s"); Failed(now); return; }
                    *(int*)Ptr(Flags) &= ~4096;
                    CopyFlowToLive();
                    CoopScript.Log($"host world: restore finished in {t} ms (camp {MissionProbe.GInt(Camp)})");
                    if (!_pendingOwn && _stateOf(_host) is PlayerState host) MissionParty.MoveTo(host);
                    CAM.DO_SCREEN_FADE_IN(800);
                    Swapping = false;
                    _pending = null;
                    if (_pendingOwn)
                    {
                        _leftRepeats = 3;   // tell the host a few times (UDP)
                        try { File.Delete(LeaveFile); } catch (Exception) { }
                        if (_guard != null) EndGuard(null);
                        _autosaveAt = now + AutosaveAfterMs;   // the game saves our own story, with what we had in the host's
                        CoopScript.Say("Co-op: back in your own story");
                        _g = G.Off;
                    }
                    else
                    {
                        CoopScript.Say(_reasking ? "Co-op: caught up with the host's story" : $"Co-op: you're in {link?.NameOf(_host)}'s story");
                        _reasking = false;
                        _g = G.Joined;
                    }
                    return;
            }
        }

        // A restore that didn't run: undo what we set, fade in, keep the state we started from (a leave is retried,
        // the guard stays on either way).
        unsafe void Failed(long now)
        {
            *(int*)Ptr(Restart) = 0;
            *(int*)Ptr(Flags) &= ~(8 | 32 | 4096);
            BRAIN.ENABLE_SCRIPT_BRAIN_SET(1);
            BRAIN.ENABLE_SCRIPT_BRAIN_SET(2);
            SCRIPTS._0x11B0A0B282FA9B10(true);
            CAM.DO_SCREEN_FADE_IN(500);
            Swapping = false;
            if (_pendingOwn) { _g = G.LeavePending; _leaveFailedMs = now; CoopScript.Log("host world: will try taking our own story back again"); }
            else { _g = InHostStory && _reasking ? G.Joined : G.Off; _pending = null; if (_g == G.Off && InHostStory && !_reasking) EndGuard(null); }
        }

        // ---- helpers -------------------------------------------------------------------------------------------

        static bool Idle() =>
            MissionProbe.GInt(MissionRunning) == -1 && !MissionProbe.InMissionScene && !CAM.IS_SCREEN_FADED_OUT() && !CAM.IS_SCREEN_FADING_OUT()
            && Game.Player.Character.Exists() && !ENTITY.IS_ENTITY_DEAD(Game.Player.Character.Handle)
            && MISC._GET_STATUS_OF_SAVEGAME_OPERATION(0) != 1 && MISC._GET_STATUS_OF_SAVEGAME_OPERATION(1) != 1;

        static unsafe bool Ready() => MissionProbe.GlobalsOk && Ptr(2633507) != null && Ptr(40) + 12066 == Ptr(12106) && Ptr(FlowLive) != null;

        // The story globals as they'd be saved now: the live story flow copied into its save place first (as the game
        // does before every save), so the flow is current.
        static unsafe void ReadLive(ulong[] into)
        {
            long* p = Ptr(WorldLayout.First);
            fixed (ulong* d = into) Buffer.MemoryCopy(p, d, 8L * into.Length, 8L * into.Length);
            long* flow = Ptr(FlowLive);
            if (flow != null) for (int i = 0; i < FlowSlots; i++) into[FlowSave - WorldLayout.First + i] = (ulong)flow[i];
        }

        static unsafe void CopyFlowToLive()   // after a restore the game copies it itself; harmless to be sure
        {
            long* flow = Ptr(FlowLive), save = Ptr(FlowSave);
            if (flow != null && save != null) Buffer.MemoryCopy(save, flow, 8L * FlowSlots, 8L * FlowSlots);
        }

        unsafe void WriteLive(List<int> globals)
        {
            long* p = Ptr(WorldLayout.First), flow = Ptr(FlowLive);
            foreach (int g in globals)
            {
                long v = (long)_world[g - WorldLayout.First];
                p[g - WorldLayout.First] = v;
                if (flow != null && g >= FlowSave && g < FlowSave + FlowSlots) flow[g - FlowSave] = v;   // the running scripts read the flow here
            }
        }

        // The profile the running game writes to: newest save written since the game started, else the newest at all.
        static string ProfileFolder()
        {
            try
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string profiles = Path.Combine(docs, "Rockstar Games", "Red Dead Redemption 2", "Profiles");
                // Why not, in the log (a tester's game found none, 30 Oct, and said nothing more).
                if (string.IsNullOrEmpty(docs)) { CoopScript.Log("host world: Windows gave no Documents folder"); return null; }
                if (!Directory.Exists(profiles)) { CoopScript.Log("host world: no Rockstar Games\\Red Dead Redemption 2\\Profiles in Documents"); return null; }
                var all = Directory.GetDirectories(profiles);
                var dirs = all.Where(d => Directory.GetFiles(d, "SRDR3*").Length > 0)
                              .Select(d => (d, t: Directory.GetFiles(d, "SRDR3*").Max(f => File.GetLastWriteTimeUtc(f)))).OrderByDescending(x => x.t).ToList();
                if (dirs.Count == 0) { CoopScript.Log($"host world: {all.Length} profile folder(s), none with a save (SRDR3...) in it"); return null; }
                DateTime started = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
                var since = dirs.Where(x => x.t >= started).ToList();
                if (since.Count > 1) { CoopScript.Log("host world: more than one profile written since the game started: not sure which is yours"); return null; }
                return (since.Count == 1 ? since[0] : dirs[0]).d;
            }
            catch (Exception e) { CoopScript.Log("host world: looking for the save folder failed: " + e.GetType().Name + ": " + e.Message); return null; }
        }

        static unsafe long* Ptr(int global) => (long*)Game.GetGlobalPtr(global).MemoryAddress;
    }
}
