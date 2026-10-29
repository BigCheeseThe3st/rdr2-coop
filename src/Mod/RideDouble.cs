// Ride double ([Convenience] RideDouble): sit behind a partner on their horse. Next to their horse
// (3 m, them in the saddle) the game's mount key (F) or [Keys] RideDouble puts us in the seat behind
// the rider. In our game their horse is their stand-in's (Puppet), so we send RidingWith = them
// (Riding.AsPassenger) and their game sits our stand-in behind them on their real horse (Carry).
// The game's dismount key or RideDouble again gets us off, and so does their getting off or leaving.
// Seats as Rockstar's own scripts use them: -1 is the rider, 0 the seat behind (SET_PED_ONTO_MOUNT);
// a ped comes off with _REMOVE_PED_FROM_MOUNT (TASK_DISMOUNT_ANIMAL differs in later ScriptHookRDR2DotNet builds).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using RDR2.Native;
using RDR2Coop.Net;
using Screen = RDR2.UI.Screen;

namespace RDR2Coop.Mod
{
    sealed class RideDouble
    {
        const int Seat = 0;                      // behind the rider
        const float Reach = 3f;
        const uint MountKey = 0xCEFD9220;        // INPUT_ENTER: mount (F)
        const uint DismountKey = 0xCBDB82A8;     // INPUT_HORSE_EXIT

        public Keys Key = Keys.None;             // [Keys] RideDouble
        public static byte With { get; private set; }   // whose horse we sit on, 0 = nobody's
        int _horse;                              // their stand-in's horse, in our game
        string _withName = "";
        byte _near;                              // a partner whose horse we could get on now (looked for every 200 ms)
        int _nearHorse;
        bool _takeMountKey, _keyPressed, _seated;
        long _lastLook, _onSince, _offAt;
        // Partners whose stand-in we sat behind a rider here (on our horse, or another stand-in's), and when.
        readonly Dictionary<byte, (int horse, long at)> _carried = new Dictionary<byte, (int horse, long at)>();

        public bool OnKey(Keys k)
        {
            if (!Extras.RideDouble || Key == Keys.None || k != Key) return false;
            _keyPressed = true;   // acted on in the tick
            return true;
        }

        // Our own seat, every tick. puppets: the other players' stand-ins here; stateOf: their latest states.
        public void Tick(int me, bool down, IDictionary<byte, Puppet> puppets, Func<byte, PlayerState> stateOf, ServerLink link, long now)
        {
            if (!Extras.RideDouble) return;
            if (With != 0) { WhileRiding(me, down, puppets, stateOf, now); return; }
            if (now - _lastLook >= 200 || _keyPressed) { _lastLook = now; Look(me, down, puppets, stateOf); }
            bool go = _keyPressed;
            _keyPressed = false;
            if (_near == 0)
            {
                if (go) Screen.DisplaySubtitle("Co-op: stand next to a partner on their horse to ride with them");
                return;
            }
            if (_takeMountKey)
            {
                PAD.DISABLE_CONTROL_ACTION(0, MountKey, false);   // F by their horse sits us behind them, never pulls them off
                go |= PAD.IS_DISABLED_CONTROL_JUST_PRESSED(0, MountKey);
            }
            if (!go || !Peds.Exists(_nearHorse)) return;   // (gone since the last look: the next one finds out)
            TASK.CLEAR_PED_TASKS_IMMEDIATELY(me, false, true);
            PED.SET_PED_ONTO_MOUNT(me, _nearHorse, Seat, true);
            With = _near; _horse = _nearHorse; _near = 0; _withName = Clean(link.NameOf(With));
            _onSince = _lastLook = now; _seated = false; _offAt = 0;
            Screen.DisplaySubtitle($"Co-op: riding with {_withName} (dismount to get off)");
            CoopScript.Log($"ride double: behind {_withName}");
        }

        // The nearest partner's horse we could get on: them in the saddle, the seat behind free, us on foot
        // with empty arms (carrying something, F stows it on their horse as usual).
        void Look(int me, bool down, IDictionary<byte, Puppet> puppets, Func<byte, PlayerState> stateOf)
        {
            _near = 0;
            if (down || MissionProbe.InMissionScene || PED.IS_PED_ON_MOUNT(me) || PED.IS_PED_IN_ANY_VEHICLE(me, false)
                || ENTITY.IS_ENTITY_DEAD(me) || PED.IS_PED_CARRYING_SOMETHING(me)) return;
            var at = Peds.Position(me);
            float best = Reach;
            foreach (var kv in puppets)
            {
                int horse = kv.Value.HorseHandle;
                if (horse == 0 || !Riding.CanCarry(stateOf(kv.Key)) || PED.GET_MOUNT(kv.Value.Handle) != horse || !PED._IS_MOUNT_SEAT_FREE(horse, Seat)) continue;
                float d = Peds.Position(horse).DistanceTo(at);
                if (d > best) continue;
                best = d; _near = kv.Key; _nearHorse = horse;
            }
            if (_near == 0) return;
            // Our own horse nearer still: F mounts it as usual (RideDouble's key still works).
            int mine = PLAYER._GET_SADDLE_HORSE_FOR_PLAYER(PLAYER.PLAYER_ID());
            _takeMountKey = !(Peds.Exists(mine) && Peds.Position(mine).DistanceTo(at) < best);
        }

        // Behind someone. RideDouble's key gets us off at once; the game's dismount key after a moment, if the
        // game hasn't let us down itself by then; their getting off, going down or leaving at once.
        void WhileRiding(int me, bool down, IDictionary<byte, Puppet> puppets, Func<byte, PlayerState> stateOf, long now)
        {
            if (_keyPressed) { _keyPressed = false; Off(me); }
            else if (_offAt == 0 && (PAD.IS_CONTROL_JUST_PRESSED(0, DismountKey) || PAD.IS_DISABLED_CONTROL_JUST_PRESSED(0, DismountKey))) _offAt = now + 1500;
            else if (_offAt != 0 && now >= _offAt) { _offAt = 0; Off(me); }
            if (now - _lastLook < 200) return;
            _lastLook = now;
            if (PED.GET_MOUNT(me) != _horse)
            {
                if (now - _onSince < 1000) return;   // still getting on
                if (!_seated) Screen.DisplaySubtitle($"Co-op: couldn't get on {_withName}'s horse");
                CoopScript.Log($"ride double: off {_withName}'s horse" + (_seated ? "" : " (never got on)"));
                With = 0; _horse = 0; _offAt = 0;
                return;
            }
            _seated = true;
            if (down || MissionProbe.InMissionScene || !puppets.TryGetValue(With, out var p) || p.HorseHandle != _horse || !Riding.CanCarry(stateOf(With)))
                Off(me);   // the next look ends the ride
        }

        // A partner's game says they sit behind st.RidingWith: put their stand-in in the seat behind that rider
        // here (on our own horse when it's us), or take it down once they've got off. True while it sits
        // there: nothing else may steer it then.
        public bool Carry(byte id, Puppet p, PlayerState st, byte myId, Puppet riderPuppet, long now)
        {
            if (!Extras.RideDouble) return false;
            _carried.TryGetValue(id, out var c);
            if (st.RidingWith == 0 && c.horse == 0) return false;                    // the usual case costs nothing
            if (st.RidingWith != 0 && c.horse != 0 && now - c.at < 250) return true;  // sitting there: looked at 4 times a second
            if (!p.Exists) return false;
            int horse = st.RidingWith == 0 ? 0 : st.RidingWith == myId ? MyMount() : riderPuppet?.HorseHandle ?? 0;
            if (WaitingHorses.All.Contains(horse)) horse = 0;   // the rider got off and left it waiting (Visuals.Horses)
            int on = PED.GET_MOUNT(p.Handle);
            if (horse != 0 && on == horse) { _carried[id] = (horse, now); return true; }
            _carried.Remove(id);
            if (c.horse != 0 && on == c.horse) PED._REMOVE_PED_FROM_MOUNT(p.Handle, true, false);   // off the last one: Follow walks it on
            // Onto the horse they sit on now, unless its seat is taken or they're nowhere near it (a stale or bad state).
            if (horse == 0 || !PED._IS_MOUNT_SEAT_FREE(horse, Seat) || Peds.Position(horse).DistanceTo(CoopScript.ToGame(st.Position)) > 10f) return false;
            TASK.CLEAR_PED_TASKS_IMMEDIATELY(p.Handle, false, true);
            PED.SET_PED_ONTO_MOUNT(p.Handle, horse, Seat, true);
            _carried[id] = (horse, now);
            return true;
        }

        // A stand-in's horse is about to be deleted (they got off, left or changed horse): get us off it first.
        public static void LetGo(int horse)
        {
            if (!Extras.RideDouble || horse == 0) return;
            int me = PLAYER.PLAYER_PED_ID();
            if (PED.IS_PED_ON_MOUNT(me) && PED.GET_MOUNT(me) == horse) PED._REMOVE_PED_FROM_MOUNT(me, true, false);
        }

        // What moves when co-op takes us somewhere (F9, the party fetching us, the host joining a guest): our
        // horse with us on it, else us. Behind a partner we get off first: that horse is their stand-in's, which
        // goes straight back to them, and would take us with it.
        public static int Mover(RDR2.Ped me)
        {
            if (!me.IsOnMount || me.CurrentMount == null) return me.Handle;
            if (With == 0) return me.CurrentMount.Handle;
            PED._REMOVE_PED_FROM_MOUNT(me.Handle, true, false);   // at once, as Rockstar's scripts do right before moving a rider
            return me.Handle;
        }

        // What we send while behind someone: on foot, RidingWith set.
        public void Fill(ref PlayerState s) { if (With != 0) s = Riding.AsPassenger(s, With); }

        // Session over: the stand-ins and their horses are gone (LetGo got us off ours first).
        public void Clear() { With = 0; _horse = 0; _near = 0; _offAt = 0; _carried.Clear(); }

        static int MyMount() { int me = PLAYER.PLAYER_PED_ID(); return PED.IS_PED_ON_MOUNT(me) ? PED.GET_MOUNT(me) : 0; }

        static void Off(int me) { if (PED.IS_PED_ON_MOUNT(me)) PED._REMOVE_PED_FROM_MOUNT(me, true, false); }

        // Names come from strangers: no game markup in what we draw.
        static string Clean(string name) => new string(name.Where(ch => ch != '~' && ch != '<' && ch != '>').ToArray());
    }
}
