// Story cutscenes on guests' screens (0.9.0-beta.8.4, [Gameplay] Cutscenes; the notice: src/Net/Cutscene.cs). Host:
// while a story cutscene plays (MissionProbe), says once a second which one, how far it has got, and which of our NPCs,
// horses, wagons and props play which part (the names come from the mission map, src/Net/MissionMap.cs, asked of the
// running scene). Guest in the mission party: plays the same anim scene in their own game, with the host's stand-in as
// the host's character and their copies in the other parts (parts nobody is given, the scene makes itself), our own
// character hidden; the copies and stand-in are left to the scene meanwhile (Scripted), and the scene's speed follows
// the host's. If it won't load or start, MissionParty's black screen covers it as before.
using System;
using System.Collections.Generic;
using System.Linq;
using RDR2;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class Cutscenes
    {
        public static bool On = true;
        /// <summary>Copies and stand-ins a cutscene is playing here: MirrorWorld and Puppet leave them alone.</summary>
        public static readonly HashSet<int> Scripted = new HashSet<int>();
        public static bool Playing => _scene != 0 && _started;
        /// <summary>Host: everything in the story cutscene playing here (OwnedWorld sends them first).</summary>
        public static readonly HashSet<int> HostParts = new HashSet<int>();

        static readonly string[] PlayerParts = { "ARTHUR", "Arthur", "player_zero", "player", "JOHN", "John", "player_three" };
        const int LoadMs = 8000;

        // Host
        long _lastSentMs;
        string _sentLabel, _list;
        ulong[] _mem;
        uint _listHash;
        int _listSlot = -1;   // where the mission keeps the playing list (the slot after it: queued phases)
        readonly List<string> _castNames = new List<string>();   // the stage engine's cast registry: the names its scenes use
        readonly ulong[] _phaseBuf = new ulong[53];

        // Guest
        CutsceneMsg _last;
        long _lastAtMs;
        static int _scene;
        static bool _started;
        static string _label;
        long _createdMs;
        bool _hidMe;
        string _failedLabel, _playing;
        readonly HashSet<string> _requested = new HashSet<string>();
        long _endedMs = -1;
        float _rate = 1f;

        public void HostTick(ServerLink link, OwnedWorld owned, string mission, bool hosting, long now)
        {
            int scene = MissionProbe.Scene;
            string label = MissionProbe.InMissionScene ? MissionProbe.SceneLabel : null;
            if (!On || !hosting || link.Roster.Count < 2) label = null;
            if (label == null)
            {
                HostParts.Clear();
                if (_sentLabel != null) { link.SendNotice(Protocol.Broadcast, NoticeKind.Cutscene, new CutsceneMsg { Label = _sentLabel, End = true }.Write()); _sentLabel = null; }
                return;
            }
            if (label == _sentLabel && now - _lastSentMs < CutsceneMsg.SendEveryMs) return;
            _lastSentMs = now;
            if (label != _sentLabel) _list = PlaybackList(mission, scene, label) ?? "NormalStart";
            var m = new CutsceneMsg { Label = label, List = _list, Time = ANIMSCENE._GET_ANIM_SCENE_TIME(scene) };
            ReadPhases(scene, label, m);
            unsafe
            {
                var pos = new RDR2.Math.Vector3(); var rot = new RDR2.Math.Vector3();
                ANIMSCENE.GET_ANIM_SCENE_ORIGIN(scene, &pos, &rot, 2);
                m.Origin = CoopScript.ToNet(pos); m.Rotation = CoopScript.ToNet(rot);
            }
            int me = PLAYER.PLAYER_PED_ID();
            var unsent = new List<string>();
            var names = PlayerParts.Concat(_castNames).Concat(MissionMap.Slots(mission)?.SelectMany(s => s.Names) ?? Enumerable.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var n in names)
            {
                int h = ANIMSCENE._GET_ANIM_SCENE_PED(scene, n, false);
                if (h <= 0) h = ANIMSCENE._GET_ANIM_SCENE_VEHICLE(scene, n, false);
                if (h <= 0) h = ANIMSCENE._GET_ANIM_SCENE_OBJECT(scene, n, false);
                if (h <= 0) continue;
                if (h != me) HostParts.Add(h);
                if (h == me) m.Parts.Add(new KeyValuePair<string, ushort>(n, CutsceneMsg.Host));
                else if (owned.IdOf(h) is ushort id && id != 0) m.Parts.Add(new KeyValuePair<string, ushort>(n, id));
                else unsent.Add($"{n} ({ENTITY.GET_ENTITY_MODEL(h):X8}, not shared)");
            }
            if (label != _sentLabel) CoopScript.Log($"cutscene: sending {label} (playback {_list}) to the party ({m.Parts.Count} parts: {string.Join(", ", m.Parts.Select(p => p.Key))}" +
                                                    (unsent.Count > 0 ? "; in it but not shared: " + string.Join(", ", unsent) : "") + ")");
            _sentLabel = label;
            link.SendNotice(Protocol.Broadcast, NoticeKind.Cutscene, m.Write());
        }

        // The playback list the mission chose ("NormalStart", "MultiStart"): the stage engine keeps its scene as
        // { label (4 slots), handle, second handle, playback list (8 slots) } in the mission's statics (seen in
        // mudtown3 at slot 9546); found by the handle with the label 4 slots before it. Null when not found (other builds, no script memory).
        string PlaybackList(string mission, int scene, string label)
        {
            _listSlot = -1;
            _castNames.Clear();
            if (mission == null || !ScriptMemory.Check(ScriptMemory.GameDir())) return null;
            uint hash = MISC.GET_HASH_KEY(mission);
            if (ScriptMemory.Info(hash, out int size, out int sp) <= 0) return null;
            int count = Math.Min(sp > 0 && sp <= size ? sp : size, 32768);
            if (_mem == null || _mem.Length < count) _mem = new ulong[count];
            for (int at = 0; at < count; at += 16384)
                if (ScriptMemory.Read(hash, at, _mem, at, Math.Min(16384, count - at)) <= 0) return null;
            string dict = "cutscene@" + label;
            for (int i = 4; i + 10 < count; i++)
                if ((uint)_mem[i] == (uint)scene && Text(_mem, i - 4, 4) is string d && (d.Equals(label, StringComparison.OrdinalIgnoreCase) || d.Equals(dict, StringComparison.OrdinalIgnoreCase)))
                {
                    string list = Text(_mem, i + 2, 8);
                    CoopScript.Log($"cutscene: {mission} keeps {dict} at slot {i}, playback list '{list}'");
                    _listHash = hash; _listSlot = i + 2;
                    ReadCast(i - 4 - 800 + 13, count);
                    return list.Length > 0 && list.All(c => c == '_' || c == '-' || char.IsLetterOrDigit(c)) ? list : null;   // e.g. 2-IfMUD2NotComplete
                }
            CoopScript.Log($"cutscene: {label}'s playback list not found in {mission}'s memory: NormalStart");
            return null;
        }

        // The stage engine loads queued playback lists (4 names of 9 slots each, 16 slots after the list slot) and
        // writes the one it sets over the list slot; the scene goes on with the list it was made with (sent as List)
        // and the others go out as Next, for a guest whose list ends first. Only names our own scene has.
        void ReadPhases(int scene, string label, CutsceneMsg m)
        {
            if (_listSlot < 0) return;
            if (ScriptMemory.Read(_listHash, _listSlot - 6, _phaseBuf, 0, _phaseBuf.Length) <= 0) { _listSlot = -1; return; }
            string at = Text(_phaseBuf, 0, 4);
            if (!at.Equals(label, StringComparison.OrdinalIgnoreCase) && !at.Equals("cutscene@" + label, StringComparison.OrdinalIgnoreCase)) { _listSlot = -1; return; }
            string list = Text(_phaseBuf, 6, 8);   // the list slot now: the scene still plays the list it was made with (_list)
            if (list != _list && list.Length > 0 && ANIMSCENE._DOES_ANIM_SCENE_PLAY_LIST_EXIST(scene, list)) m.Next.Add(list);
            m.List = _list;
            for (int k = 0; k < 4; k++)
            {
                string n = Text(_phaseBuf, 6 + 16 + 1 + 9 * k, 8);
                if (n.Length > 0 && n != _list && !m.Next.Contains(n) && ANIMSCENE._DOES_ANIM_SCENE_PLAY_LIST_EXIST(scene, n)) m.Next.Add(n);
            }
        }

        // The stage engine's cast registry, 13 slots into its block (the label is at 800): 65 entries of 12 slots,
        // the entity then its scene name (mudtown3: Arthur, Uncle, WAGON03X, MaryBeth, Horse_01^1, p_door33x...).
        void ReadCast(int at, int count)
        {
            _castNames.Clear();
            if (at < 0 || at + 1 + 65 * 12 > count || _mem[at] != 65) return;
            for (int k = 0; k < 65; k++)
            {
                string n = Text(_mem, at + 1 + 12 * k + 1, 4);
                if (n.Length > 0 && n.Length <= 48 && n.All(c => c == '_' || c == '^' || char.IsLetterOrDigit(c))) _castNames.Add(n);
            }
            CoopScript.Log($"cutscene: the mission's cast list: {string.Join(", ", _castNames)}");
        }

        static string Text(ulong[] mem, int at, int slots)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < slots * 8; i++)
            {
                byte b = (byte)(mem[at + i / 8] >> (8 * (i % 8)));
                if (b == 0) break;
                sb.Append((char)b);
            }
            return sb.ToString();
        }

        public void OnNotice(byte from, string text, ServerLink link, long now)
        {
            if (from != link.HostId) return;
            var m = CutsceneMsg.Read(text);
            if (m == null) return;
            _last = m; _lastAtMs = now;
        }

        /// <summary>Guest side, every tick. member: we're in the host's mission party and close by.</summary>
        public void GuestTick(bool member, MirrorWorld mirror, Func<byte, int> standIn, byte host, long now)
        {
            bool want = On && member && _last != null && !_last.End && now - _lastAtMs < CutsceneMsg.StaleMs;
            if ((_scene != 0 || _hidMe) && (!want || _label != _last.Label)) Stop(want ? "the host moved on to another cutscene" : "the host's cutscene is over");
            if (!want) { _failedLabel = null; return; }
            if (_failedLabel == _last.Label) return;
            if (_scene == 0)
            {
                _label = _last.Label;
                _scene = ANIMSCENE._CREATE_ANIM_SCENE("cutscene@" + _label, 256 | 2048, _last.List, false, true);
                if (_scene == 0 || !ANIMSCENE.DOES_ANIM_SCENE_EXIST(_scene)) { Fail("couldn't make it"); return; }
                if (_last.Origin.X != 0 || _last.Origin.Y != 0)   // 0 0 0: the scene's own place
                    ANIMSCENE.SET_ANIM_SCENE_ORIGIN(_scene, CoopScript.ToGame(_last.Origin), CoopScript.ToGame(_last.Rotation), 2);
                var given = new List<string>();
                foreach (var p in _last.Parts)
                {
                    int h = p.Value == CutsceneMsg.Host ? standIn(host) : mirror.HandleOf(p.Value);
                    if (h == 0) h = mirror.VehicleHandle(p.Value);
                    if (h == 0) continue;
                    ANIMSCENE.SET_ANIM_SCENE_ENTITY(_scene, p.Key, h, 0);
                    Scripted.Add(h);
                    given.Add(p.Key);
                }
                ANIMSCENE.LOAD_ANIM_SCENE(_scene);
                _createdMs = now; _started = false; _rate = 1f; _playing = _last.List; _requested.Clear(); _endedMs = -1;
                CoopScript.Log($"cutscene: playing the host's {_label} here, parts given: {(given.Count == 0 ? "none" : string.Join(", ", given))} (of {_last.Parts.Count})");
                return;
            }
            if (!ANIMSCENE.DOES_ANIM_SCENE_EXIST(_scene)) { Fail("it went away"); return; }
            float hostTime = _last.Time + (now - _lastAtMs) / 1000f;
            if (!_started)
            {
                if (!ANIMSCENE.IS_ANIM_SCENE_LOADED(_scene, true, false)) { if (now - _createdMs > LoadMs) Fail("it didn't load in time"); return; }
                int me = PLAYER.PLAYER_PED_ID();
                ENTITY.SET_ENTITY_VISIBLE(me, false);   // the host's stand-in plays the host's part; we watch
                ENTITY.SET_ENTITY_COLLISION(me, false, false);
                _hidMe = true;
                ANIMSCENE.START_ANIM_SCENE(_scene);
                _started = true;
                CoopScript.Log($"cutscene: {_label} started here {now - _createdMs} ms after the notice, the host at {hostTime:0.0} s");
                return;
            }
            // Load what the host's mission has queued. The list the scene was made with plays to its end (a mission
            // rewrites its list slot as bookkeeping: MUD3_INT is made with its 77 s "2-IfMUD2NotComplete" and the slot
            // says "NormalStart", 5.7 s, from 1 s on); only when ours ends first do we go on to the host's current one.
            foreach (var n in _last.Next.Concat(new[] { _last.List }))
                if (n != _playing && _requested.Add(n)) ANIMSCENE.REQUEST_ANIM_SCENE_PLAY_LIST(_scene, n);
            if (ANIMSCENE.IS_ANIM_SCENE_FINISHED(_scene, false) || ANIMSCENE.HAS_ANIM_SCENE_EXITED(_scene, false))
            {
                string next = _last.Next.Concat(new[] { _last.List }).FirstOrDefault(n => n != _playing && ANIMSCENE._IS_ANIM_SCENE_PLAYBACK_LIST_PHASE_LOADED(_scene, n));
                if (next != null && _endedMs < 0)
                {
                    ANIMSCENE.SET_ANIM_SCENE_PLAY_LIST(_scene, next, true);
                    CoopScript.Log($"cutscene: {_label} '{_playing}' over here, on to '{next}' (the host at {hostTime:0.0} s)");
                    _playing = next;
                    _endedMs = now;   // once: if it's still over after this, the black screen
                    return;
                }
                if (_endedMs < 0) _endedMs = now;   // the host's next list may still be on its way
                if (now - _endedMs > 3000)
                    Fail($"it ended at {ANIMSCENE._GET_ANIM_SCENE_TIME(_scene):0.0} s ('{_playing}'), the host's is at {hostTime:0.0} s ('{_last.List}')");
                return;
            }
            float rate = CutsceneMsg.Rate(hostTime, ANIMSCENE._GET_ANIM_SCENE_TIME(_scene));
            if (Math.Abs(rate - _rate) > 0.05f) { ANIMSCENE.SET_ANIM_SCENE_RATE(_scene, rate); _rate = rate; }
        }

        void Fail(string why)
        {
            CoopScript.Log($"cutscene: the host's {_label} can't play here ({why}): black screen instead");
            string label = _label;
            Stop(null);
            _failedLabel = label;
        }

        public void Stop(string why)
        {
            if (why != null && _scene != 0) CoopScript.Log($"cutscene: {_label} stopped here ({why})");
            if (_scene != 0 && ANIMSCENE.DOES_ANIM_SCENE_EXIST(_scene)) { ANIMSCENE.ABORT_ANIM_SCENE(_scene, false); ANIMSCENE._DELETE_ANIM_SCENE(_scene); }
            _scene = 0; _started = false; _label = null;
            Scripted.Clear();
            if (_hidMe)
            {
                int me = PLAYER.PLAYER_PED_ID();
                ENTITY.SET_ENTITY_VISIBLE(me, true);
                ENTITY.SET_ENTITY_COLLISION(me, true, true);
                _hidMe = false;
            }
        }
    }
}
