// One of each person where bubbles overlap (the rules: src/Net/Doubles.cs). Every 2 s we tell linked players which
// of the people we send are which companions (the gang, by the game's companion registry Global_1360165). Twice a
// second, if a linked player outranks us (the host, then the lowest id), we hide our own gang member whose copy from
// that player's world is here, and our other script-made people with a copy of the same model on the same spot;
// they come back 2 s after the copy goes, or when we unlink. Hidden people are never sent (OwnedWorld.Skip).
using System;
using System.Collections.Generic;
using System.Linq;
using RDR2;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class DoublesSync
    {
        const int Registry = 1360165, Entry = 1157, ScanRadius = 150, DecideEveryMs = 500;
        readonly Doubles _rules = new Doubles();
        readonly Dictionary<byte, Dictionary<int, ushort>> _keysFrom = new Dictionary<byte, Dictionary<int, ushort>>();
        readonly HashSet<int> _hidden = new HashSet<int>();
        long _lastSendMs, _lastDecideMs;
        const int FadeMs = 800;   // people fade out and back in rather than popping (George: "I saw Dutch jump back in")
        readonly Dictionary<int, long> _fadeIn = new Dictionary<int, long>(), _fadeOut = new Dictionary<int, long>();

        public IEnumerable<int> Hidden => _hidden;

        public void OnNotice(byte from, string text)
        {
            var k = EntityKeys.Read(text);
            if (k != null) _keysFrom[from] = k;
        }

        public void Tick(ServerLink link, OwnedWorld owned, ICollection<byte> linked, Func<byte, MirrorWorld> mirrorOf, Vec3 myPos, long now)
        {
            if (!MissionProbe.GlobalsOk) return;   // the registry's place is from the 1491.50 scripts
            if (_fadeIn.Count > 0 || _fadeOut.Count > 0) Fades(now);
            bool send = now - _lastSendMs >= EntityKeys.SendEveryMs && linked.Count > 0;
            if (!send && now - _lastDecideMs < DecideEveryMs) return;
            var own = OwnCompanions();
            if (send)
            {
                _lastSendMs = now;
                var keys = own.Where(kv => !_hidden.Contains(kv.Value)).Select(kv => new KeyValuePair<int, ushort>(kv.Key, owned.IdOf(kv.Value))).Where(kv => kv.Value != 0).ToList();
                if (keys.Count > 0) foreach (byte id in linked) link.SendNotice(id, NoticeKind.EntityKeys, EntityKeys.Write(keys));
            }
            if (now - _lastDecideMs < DecideEveryMs) return;
            _lastDecideMs = now;

            var higher = linked.Where(id => Bubbles.Outranks(id, link.MyId, link.HostId)).ToList();
            var copied = new HashSet<int>();
            var copies = new List<CopyPed>();
            foreach (byte id in higher)
            {
                var m = mirrorOf(id);
                if (_keysFrom.TryGetValue(id, out var keys)) foreach (var kv in keys) if (m.HandleOf(kv.Value) != 0) copied.Add(kv.Key);
                copies.AddRange(m.CopyModels().Select(c => new CopyPed { Model = c.model, Position = CoopScript.ToNet(c.pos) }));
            }
            var ownScript = new List<LocalPed>();
            if (copies.Count > 0)
            {
                var me = Game.Player.Character;
                foreach (int h in EntityScan.Peds(new[] { me.Position }, ScanRadius))
                {
                    if (h == me.Handle || !Peds.Exists(h) || PED.IS_PED_A_PLAYER(h) || MirrorWorld.IsCopyGroup(h) || OwnedWorld.Skip.Contains(h) || Peds.Ours.Contains(h)) continue;
                    if (!ENTITY.IS_ENTITY_A_MISSION_ENTITY(h) || ENTITY.IS_ENTITY_DEAD(h)) continue;
                    ownScript.Add(new LocalPed { Handle = h, Model = ENTITY.GET_ENTITY_MODEL(h), Position = CoopScript.ToNet(Peds.Position(h)) });
                }
            }
            var hide = _rules.Decide(own, copied, ownScript, copies, now);
            if (MissionProbe.StoryMission) hide.Clear();   // our own mission's people stay ours, whatever the copies
            else hide.RemoveWhere(KeepShown);
            int newly = 0, back = 0;
            var names = new List<string>();
            foreach (int h in hide)
            {
                if (_hidden.Add(h) && Peds.Exists(h))
                {
                    Hide(h, now); newly++;
                    int c = own.FirstOrDefault(kv => kv.Value == h).Key;
                    names.Add(own.ContainsValue(h) ? $"companion {c} ({ENTITY.GET_ENTITY_MODEL(h):X8})" : $"{ENTITY.GET_ENTITY_MODEL(h):X8} on the copy's spot");
                }
                else if (Peds.Exists(h) && ENTITY.IS_ENTITY_VISIBLE(h) && !_fadeOut.ContainsKey(h)) Hide(h, now);   // its own script showed it again
            }
            foreach (int h in _hidden.Where(h => !hide.Contains(h)).ToList()) { _hidden.Remove(h); if (Peds.Exists(h)) { Show(h, now); back++; } }
            if (newly > 0 || back > 0) CoopScript.Log($"doubles: hid {newly}, showed {back} of our own people (the higher-priority game's copies stand in; {_hidden.Count} hidden)" +
                                                      (names.Count > 0 ? ": " + string.Join(", ", names) : ""));
        }

        /// <summary>Everyone we hid back (unlinked, bubbles off, the mod stopping).</summary>
        public void ShowAll()
        {
            foreach (int h in _hidden) if (Peds.Exists(h)) { Show(h, 0); ENTITY.RESET_ENTITY_ALPHA(h); }
            _fadeIn.Clear(); _fadeOut.Clear();
            if (_hidden.Count > 0) CoopScript.Log($"doubles: {_hidden.Count} of our own people shown again");
            _hidden.Clear();
            _rules.Clear();
            _keysFrom.Clear();
        }

        public void Forget(byte id) => _keysFrom.Remove(id);

        static Dictionary<int, int> OwnCompanions()
        {
            var d = new Dictionary<int, int>();
            for (int i = 0; i < EntityKeys.Companions; i++)
            {
                int h = MissionProbe.GInt(Registry + 1 + i * Entry);
                if (h > 0 && Peds.Exists(h) && !PED.IS_PED_A_PLAYER(h)) d[i] = h;
            }
            return d;
        }

        // Never hidden: our horses, whoever rides with us or is carried by us, the mission's cast and our group (a gang
        // member riding along on an errand): ours to play with, whatever the higher game shows.
        static bool KeepShown(int h)
        {
            var me = Game.Player.Character;
            int meH = me.Handle, veh = Peds.VehicleOf(meH);
            if (h == (Game.Player.ActiveHorse?.Handle ?? 0) || h == PED._GET_LAST_MOUNT(meH) || (me.IsOnMount && me.CurrentMount != null && h == me.CurrentMount.Handle)) return true;
            if (veh != 0 && Peds.VehicleOf(h) == veh) return true;
            if (PED._GET_CARRIER_AS_PED(h) == meH || MissionRoles.Known(h)) return true;
            return PED.IS_PED_GROUP_MEMBER(h, PLAYER.GET_PLAYER_GROUP(PLAYER.PLAYER_ID()), false);
        }

        // Out of the way at once (no collision, still), then faded to nothing and made invisible.
        void Hide(int h, long now)
        {
            ENTITY.SET_ENTITY_COLLISION(h, false, false);
            ENTITY.FREEZE_ENTITY_POSITION(h, true);
            _fadeIn.Remove(h);
            _fadeOut[h] = now;
        }

        // Back where they were, faded in from nothing.
        void Show(int h, long now)
        {
            _fadeOut.Remove(h);
            ENTITY.FREEZE_ENTITY_POSITION(h, false);
            ENTITY.SET_ENTITY_COLLISION(h, true, true);
            ENTITY.SET_ENTITY_ALPHA(h, 0, false);
            ENTITY.SET_ENTITY_VISIBLE(h, true);
            if (now > 0) _fadeIn[h] = now;
        }

        void Fades(long now)
        {
            foreach (var kv in _fadeOut.ToList())
            {
                if (!Peds.Exists(kv.Key)) { _fadeOut.Remove(kv.Key); continue; }
                long t = now - kv.Value;
                if (t >= FadeMs) { ENTITY.SET_ENTITY_VISIBLE(kv.Key, false); ENTITY.RESET_ENTITY_ALPHA(kv.Key); _fadeOut.Remove(kv.Key); }
                else ENTITY.SET_ENTITY_ALPHA(kv.Key, (int)(255 - 255 * t / FadeMs), false);
            }
            foreach (var kv in _fadeIn.ToList())
            {
                if (!Peds.Exists(kv.Key)) { _fadeIn.Remove(kv.Key); continue; }
                long t = now - kv.Value;
                if (t >= FadeMs) { ENTITY.RESET_ENTITY_ALPHA(kv.Key); _fadeIn.Remove(kv.Key); }
                else ENTITY.SET_ENTITY_ALPHA(kv.Key, (int)(255 * t / FadeMs), false);
            }
        }
    }
}
