// Dynamite, fire bottles and explosive ammo reach the partners' games (0.8.0, [Gameplay] Explosions);
// before, an explosion only happened in the thrower's game. Ours come from the game's event queue
// (EVENT_ENTITY_EXPLOSION says who set one off, with which weapon and where) and go out as an
// Explosion notice. A partner's is made again here: owned by their stand-in, when our game runs the
// area they are in, so our NPCs react to it and blame them (the hurt comes as before, in the hits from
// their game's copies, and its fire burns here); otherwise only to see and hear, since the people
// around it here are copies the owner's game drives (their damage trap passes on only what we do to
// them). Players never hurt each other with it: we, our horse and the stand-ins near a partner's
// blast are fire- and explosion-proof for a moment, and in a story mission so are our allies and the
// mission's vehicles (hurting those fails it for everyone, as OwnedWorld.ApplyHit knows for guests' shots).
using System;
using System.Collections.Generic;
using System.Linq;
using RDR2;
using RDR2.Math;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class Explosions
    {
        const uint ExplosionEvent = 0xF79F5B8B;   // EVENT_ENTITY_EXPLOSION: 0 who, 2 weapon, 3-5 where (femga's rdr3_discoveries, AI/EVENTS)
        const float ShowWithin = 300f;            // further from us: outside the world our game has loaded
        const float Hazard = 15f;                 // near enough to a blast to be hurt by it, with room to spare
        const int ShieldMs = 1500;
        // Flame, explosion and smoke proof (femga's rdr3_discoveries, AI/ENTITY_PROOFS): 70, which no story
        // script sets, so finding 70 later means it is still ours to undo.
        const int ShieldBits = 2 | 4 | 64;

        struct Seen { public byte From; public int Kind, Other; public Vec3 At; public uint Weapon; public long Ms; }

        readonly List<Seen> _ours = new List<Seen>();       // ours, until the game shows which kind each is
        readonly List<Seen> _sent = new List<Seen>();       // ours sent in the last second: one blast goes out once
        readonly Queue<Seen> _theirs = new Queue<Seen>();   // partners', made on the next tick
        readonly BlastGate _outGate = new BlastGate();
        readonly Dictionary<byte, BlastGate> _inGates = new Dictionary<byte, BlastGate>();
        readonly Dictionary<int, long> _shielded = new Dictionary<int, long>();   // entity -> proof until
        long _lastNoteMs = -10000;

        // Every tick, after the puppets are placed. owner: whose game runs our area.
        public void Tick(ServerLink link, Ped me, byte owner, IDictionary<byte, Puppet> puppets, Func<byte, PlayerState?> stateOf, long now)
        {
            Unshield(now, false);
            if (!Sync.Explosions) return;
            if (link.Roster.Count > 1) ReadEvents(me.Handle, now);
            SendOurs(link, now);   // also when they just left: what's waiting goes now, never late
            while (_theirs.Count > 0) Make(_theirs.Dequeue(), me, owner, link.MyId, puppets, stateOf, now);
        }

        // Every frame, not throttled: the game keeps an event for one frame only.
        unsafe void ReadEvents(int me, long now)
        {
            var events = AiEvents.Now(now);
            ulong* data = stackalloc ulong[6];
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i] != ExplosionEvent || !SCRIPTS.GET_EVENT_DATA(AiEvents.Queue, i, data, 6)) continue;
                if (unchecked((int)data[0]) != me) continue;   // someone else's: the game that runs them deals with it
                var at = new Vec3(*(float*)&data[3], *(float*)&data[4], *(float*)&data[5]);   // a float sits in the low half of its slot
                if (_ours.Count >= 8 || _ours.Concat(_sent).Any(s => now - s.Ms < 300 && Vec3.Distance(s.At, at) < 1.5f)) continue;   // the same blast again
                _ours.Add(new Seen { At = at, Weapon = unchecked((uint)data[2]), Ms = now, Other = -1 });
            }
        }

        // The kind is the one the game shows there (it can take a frame or two). If none ever showed,
        // thrown dynamite and fire bottles go by the weapon; another kind there (a wagon or barrel it
        // set off) stays ours alone.
        void SendOurs(ServerLink link, long now)
        {
            _sent.RemoveAll(s => now - s.Ms > 1000);
            for (int i = _ours.Count - 1; i >= 0; i--)
            {
                var s = _ours[i];
                s.Kind = KindAt(CoopScript.ToGame(s.At), out int other);
                if (other >= 0) s.Other = other;
                if (s.Kind < 0 && now - s.Ms < 600) { _ours[i] = s; continue; }   // not showing yet: look again next frame
                _ours.RemoveAt(i);
                if (s.Kind < 0 && s.Other < 0)
                    s.Kind = s.Weapon == MISC.GET_HASH_KEY("WEAPON_THROWN_DYNAMITE") ? 25 : s.Weapon == MISC.GET_HASH_KEY("WEAPON_THROWN_MOLOTOV") ? 2 : -1;
                if (s.Kind < 0) { Note($"explosions: ours (weapon {s.Weapon:X8}, tag {s.Other}) isn't a kind we pass on", now); continue; }
                if (!_outGate.Allow(now)) continue;
                _sent.Add(s);
                link.SendNotice(Protocol.Broadcast, NoticeKind.Explosion, Blast.Write(s.Kind, s.At));
                Note($"explosions: sent our {Blast.Name(s.Kind)} at {s.At}", now);
            }
        }

        // One of our kinds exploding at `at` (the event's own spot, so 1 m), or -1; other: any other kind there.
        static int KindAt(Vector3 at, out int other)
        {
            other = -1;
            for (int k = 0; k <= 35; k++)   // EXP_TAG_GRENADE .. EXP_TAG_POISON_BOTTLE
            {
                if (!FIRE.IS_EXPLOSION_IN_SPHERE(k, at, 1f)) continue;
                if (Blast.Kinds.ContainsKey(k)) return k;
                if (other < 0) other = k;
            }
            return -1;
        }

        // From the network (inside ServerLink.Poll): checked here, made on the tick, which knows whose area this is.
        public void OnNotice(byte from, string text, long now)
        {
            if (!Sync.Explosions) return;
            if (!Blast.Read(text, out int kind, out var at)) { Note($"explosions: an unreadable notice from player {from}", now); return; }
            if (!_inGates.TryGetValue(from, out var gate)) _inGates[from] = gate = new BlastGate();
            if (gate.Allow(now) && _theirs.Count < 16) _theirs.Enqueue(new Seen { From = from, Kind = kind, At = at, Ms = now });
        }

        void Make(Seen b, Ped me, byte owner, byte myId, IDictionary<byte, Puppet> puppets, Func<byte, PlayerState?> stateOf, long now)
        {
            var st = stateOf(b.From);
            // Stale, or further from them than they could throw or shoot.
            if (now - b.Ms > 2000 || st == null || Vec3.Distance(st.Value.Position, b.At) > Blast.MaxFromThrower) return;
            var at = CoopScript.ToGame(b.At);
            if (me.Position.DistanceTo(at) > ShowWithin || MissionProbe.InMissionScene) return;   // never into a cutscene
            puppets.TryGetValue(b.From, out var thrower);
            bool real = owner == myId;
            // Our game runs our area but they aren't in it: the people there are ours, not copies, and theirs made it already.
            if (real && (st.Value.Following != myId || thrower == null || !thrower.Exists)) return;
            long until = now + ShieldMs;
            Shield(me.Handle, at, until);
            if (me.IsOnMount && me.CurrentMount != null) Shield(me.CurrentMount.Handle, at, until);
            var horse = Game.Player.ActiveHorse;
            if (horse != null) Shield(horse.Handle, at, until);
            if (real)
            {
                uint players = MISC.GET_HASH_KEY("PLAYER");
                foreach (var p in puppets.Values.Where(x => x.Exists))
                {
                    // The fire it leaves outlasts the shield: stand-ins take nothing from the PLAYER group (players
                    // and stand-ins), so only our NPCs' hits reach their players.
                    ENTITY.SET_ENTITY_CAN_BE_DAMAGED_BY_RELATIONSHIP_GROUP(p.Handle, false, players);
                    Shield(p.Handle, at, until);
                }
                if (MissionProbe.StoryMission) ShieldAllies(at, me.Handle, thrower.Handle, until);
                // No damage of its own: their copies of our people took the blast in their game, and those hits
                // land here (as with owners on 0.7.x). Twice would kill the wounded; CoopScript.OnHit skips the burns.
                FIRE.ADD_OWNED_EXPLOSION(thrower.Handle, at, b.Kind, 0f, true, false, 1f);
            }
            else FIRE.ADD_EXPLOSION(at, b.Kind, 1f, true, false, 1f);
            Note($"explosions: {Blast.Name(b.Kind)} from player {b.From} at {b.At}, " + (real ? "for real (our area)" : "to see and hear"), now);
        }

        // Story mission: people and horses near it that aren't against us or the thrower are allies,
        // and the mission's own vehicles count too.
        void ShieldAllies(Vector3 at, int me, int thrower, long until)
        {
            var near = new[] { at };
            foreach (int h in EntityScan.Peds(near, Hazard))
            {
                if (!Peds.Exists(h) || Peds.Position(h).DistanceTo(at) >= Hazard || PED.IS_PED_A_PLAYER(h) || ENTITY.IS_ENTITY_DEAD(h)) continue;
                if (!PED.IS_PED_HUMAN(h) && !PED._IS_THIS_MODEL_A_HORSE(ENTITY.GET_ENTITY_MODEL(h))) continue;   // wild animals are fair game
                if (PED.GET_RELATIONSHIP_BETWEEN_PEDS(h, me) >= 4 || PED.IS_PED_IN_COMBAT(h, me) || PED.IS_PED_IN_COMBAT(h, thrower)) continue;   // 4 dislike, 5 hate: enemies too
                Shield(h, at, until);
            }
            foreach (int v in EntityScan.Vehicles(near, Hazard))
                if (Peds.Exists(v) && Peds.Position(v).DistanceTo(at) < Hazard && ENTITY.IS_ENTITY_A_MISSION_ENTITY(v)) Shield(v, at, until);
        }

        // Fire- and explosion-proof until `until`, if near the blast and without proofs of its own.
        void Shield(int e, Vector3 at, long until)
        {
            if (!Peds.Exists(e) || Peds.Position(e).DistanceTo(at) >= Hazard) return;
            if (_shielded.ContainsKey(e)) { _shielded[e] = until; return; }
            if (ENTITY._GET_ENTITY_PROOFS(e) != 0) return;   // a mission's own proofs: leave them be
            ENTITY.SET_ENTITY_PROOFS(e, ShieldBits, false);
            _shielded[e] = until;
        }

        // Back to no proofs, unless something else (a mission) changed them meanwhile: then they're its.
        void Unshield(long now, bool all)
        {
            if (_shielded.Count == 0) return;
            foreach (int e in _shielded.Where(kv => all || now >= kv.Value).Select(kv => kv.Key).ToList())
            {
                if (Peds.Exists(e) && ENTITY._GET_ENTITY_PROOFS(e) == ShieldBits) ENTITY.SET_ENTITY_PROOFS(e, 0, false);
                _shielded.Remove(e);
            }
        }

        void Note(string line, long now)
        {
            if (now - _lastNoteMs < 2000) return;   // a burst of explosive rounds logs once
            _lastNoteMs = now;
            CoopScript.Log(line);
        }

        // Leaving the session, or the mod stopping: nothing keeps our shield.
        public void Clear()
        {
            Unshield(0, true);
            _ours.Clear();
            _sent.Clear();
            _theirs.Clear();
            _inGates.Clear();
        }
    }
}
