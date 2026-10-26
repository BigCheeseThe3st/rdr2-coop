// What partners carry (0.8.0, [Visuals] Carrying): a body or a hogtied person over the shoulder, a
// carcass or a pelt on the shoulder or on their horse. Each game sends the model of what its player
// carries (Net/Carry.cs says where it goes) and each puppet shows one copy of it, for show only.
// The copy is handed to the puppet or its horse with the game's own carry task (TASK_CARRIABLE, the
// way Rockstar's scripts put a carcass on a horse or a captive on a kidnapper's shoulder), so it sits
// where the game puts carried things; if the game won't take it, it's pinned across the shoulders or
// behind the saddle instead. Small and medium pelts stowed on a horse aren't things in the world, only
// item hashes: they go on the puppet's horse the way the game's online stables put a horse's pelts back.
// A failure here stops this feature, never the rest of the tick.
using System;
using System.Collections.Generic;
using RDR2;
using RDR2.Native;
using RDR2Coop.Net;
using Vector3 = RDR2.Math.Vector3;

namespace RDR2Coop.Mod
{
    sealed class Carrying
    {
        const int EveryMs = 250, TakeMs = 2000, LoadMs = 10000;
        const int HidePrompts = 37, NoTakingFromMount = 34;   // _SET_ENTITY_CARRYING_FLAG ids (eCarryingFlag)

        // ---- what we carry, for the partners ----
        static long _readMs;
        static uint _what;
        static byte _where;
        static bool _broken;
        static readonly List<int> _mine = new List<int>();   // the real things we carry
        // Every copy on every puppet: kept out of the NPCs our area sends, and counted as copies for enRage.
        public static readonly HashSet<int> Copies = new HashSet<int>();
        static readonly HashSet<int> PeltHorses = new HashSet<int>();   // partners' stand-in horses we laid a pelt on

        // Our load into the state we send, looked at every 250 ms. mount: the horse we ride; 0 on foot,
        // and then the horse we last rode counts (its load stays on it, and partners may see it waiting).
        public static void Capture(int me, int mount, ref PlayerState s, long now)
        {
            if (!Visuals.Carrying || _broken) return;
            if (now - _readMs >= EveryMs)
            {
                _readMs = now;
                try { Read(me, mount); }
                catch (Exception e) { _broken = true; _mine.Clear(); Note("stopped sending what we carry: " + e.Message); return; }
            }
            s.Carrying = _what;
            s.CarryKind = _where;
        }

        static void Read(int me, int mount)
        {
            _mine.Clear();
            _what = 0;
            _where = Carry.Nothing;
            int held = PED._GET_FIRST_ENTITY_PED_IS_CARRYING(me);
            if (held != 0 && ENTITY.DOES_ENTITY_EXIST(held))
            {
                _mine.Add(held);
                _what = ENTITY.GET_ENTITY_MODEL(held);
                _where = Carry.Shoulder;
                return;
            }
            if (mount == 0) mount = PED._GET_LAST_MOUNT(me);
            if (mount == 0 || !ENTITY.DOES_ENTITY_EXIST(mount)) return;
            // A horse can carry a few things (carcasses, a body, a big pelt): the partners see the biggest.
            var models = new List<uint>();
            var sizes = new List<float>();
            if (PED.IS_PED_CARRYING_SOMETHING(mount))
            {
                int set = ITEMSET.CREATE_ITEMSET(true);
                try
                {
                    PED.FIND_ALL_ATTACHED_CARRIABLE_ENTITIES(mount, set);
                    int n = Math.Min(ITEMSET.GET_ITEMSET_SIZE(set), 8);
                    for (int i = 0; i < n; i++)
                    {
                        int e = MISC._GET_ENTITY_FROM_ITEM(ITEMSET.GET_INDEXED_ITEM_IN_ITEMSET(i, set));
                        if (e == 0 || !ENTITY.DOES_ENTITY_EXIST(e) || Copies.Contains(e)) continue;   // never our copy of a partner's load
                        uint model = ENTITY.GET_ENTITY_MODEL(e);
                        var x = Extent(model);
                        _mine.Add(e);
                        models.Add(model);
                        sizes.Add(x.X * x.Y * x.Z);
                    }
                }
                finally { if (ITEMSET.IS_ITEMSET_VALID(set)) ITEMSET.DESTROY_ITEMSET(set); }
            }
            int best = Carry.Biggest(sizes);
            if (best >= 0) { _what = models[best]; _where = Carry.Horse; return; }
            // Small and medium pelts stowed on a horse (up to three) are item hashes, not things in the world.
            if (PeltHorses.Contains(mount)) return;   // a partner's horse we rode behind them on (ride double): its pelt is our copy of theirs
            for (int i = 0; i < 3 && _what == 0; i++) _what = (uint)PED._GET_PELT_FROM_HORSE(mount, i);
            if (_what != 0) _where = Carry.Horse;
        }

        // The game writes each corner as a script vector, x, y and z in 8-byte slots (24 bytes): a 16-byte
        // Vector3 there would have the stack written past it.
        static unsafe Vector3 Extent(uint model)
        {
            float* lo = stackalloc float[6];
            float* hi = stackalloc float[6];
            MISC.GET_MODEL_DIMENSIONS(model, (Vector3*)lo, (Vector3*)hi);
            return new Vector3(Math.Abs(hi[0] - lo[0]), Math.Abs(hi[2] - lo[2]), Math.Abs(hi[4] - lo[4]));
        }

        // Keeps what we carry, and our copies of what the partners carry, out of the NPCs our area
        // sends: the partners see those on the stand-ins already.
        public static void Skip(HashSet<int> skip)
        {
            skip.UnionWith(_mine);
            skip.UnionWith(Copies);
        }

        // ---- what a partner carries, on their puppet ----
        uint _show;           // a model, or (on a horse) a pelt's item hash; 0 = nothing
        byte _on;             // Carry.Shoulder or Carry.Horse
        int _carrier;         // the puppet, or its horse
        int _copy;            // our copy: a ped (body, carcass) or an object (pelt)
        bool _pelt;           // a pelt laid on the horse instead of a copy
        bool _asked, _held, _pinned, _stop, _requested;
        int _made;
        long _sinceMs, _nextMs;
        static readonly HashSet<string> Logged = new HashSet<string>();

        // From the puppet loop, every tick; the work itself at most every 250 ms.
        public void Show(Puppet p, PlayerState s, long now)
        {
            byte on = Visuals.Carrying ? Carry.Where(s) : Carry.Nothing;
            int carrier = on == Carry.Shoulder ? p.Handle : on == Carry.Horse ? p.HorseHandle : 0;
            uint show = carrier != 0 ? s.Carrying : 0;
            try
            {
                if (show != _show || on != _on || carrier != _carrier)
                {
                    Delete();   // put down, swapped, or moved between shoulder and horse: start again
                    _show = show;
                    _on = on;
                    _carrier = carrier;
                    _sinceMs = now;
                }
                if (_show == 0 || _stop || _pelt || now < _nextMs) return;
                _nextMs = now + EveryMs;
                if (_copy == 0) Make(p.Handle, now);
                else Hold(now);
            }
            catch (Exception e) { _stop = true; Note("stopped showing a load after an error: " + e.Message); }
        }

        // puppet: the stand-in itself (the rider, when the load goes on its horse).
        void Make(int puppet, long now)
        {
            uint m = _show;
            if (!Peds.Exists(_carrier)) return;
            if (!new Model(m).IsInCdImage)
            {
                // Not a model: on a horse it can be a stowed pelt, if the game knows it as one (the
                // butcher's own check: a valid item key, then a pelt).
                if (_on == Carry.Horse && ITEMDATABASE._ITEMDATABASE_IS_KEY_VALID(m, 0) && INVENTORY._INVENTORY_GET_INVENTORY_ITEM_IS_ANIMAL_PELT(m))
                {
                    PED._SET_PELT_FOR_HORSE(_carrier, (int)m);
                    PeltHorses.Add(_carrier);
                    _pelt = true;
                    Note($"pelt {m:X8} put on a partner's horse");
                }
                else Stop($"{m:X8} is neither a model nor a pelt; not shown");
                return;
            }
            // Things you can carry only: people and animals, or a small object (no vehicles, nothing
            // bigger than a bison's pelt).
            bool ped = STREAMING.IS_MODEL_A_PED(m);
            if (!ped && STREAMING.IS_MODEL_A_VEHICLE(m)) { Stop($"{m:X8} is a vehicle; not shown"); return; }
            if (!STREAMING.HAS_MODEL_LOADED(m))
            {
                STREAMING.REQUEST_MODEL(m, false);
                _requested = true;
                if (now - _sinceMs > LoadMs) Stop($"model {m:X8} didn't load in 10 s");
                return;
            }
            if (!ped)
            {
                // The one object made from a partner's word: only a model the game itself carries (Rockstar's
                // scripts ask the same of a loaded model before making it carriable), so no other prop is created.
                if (!ENTITY._IS_CARRIABLE_MODEL(m)) { Stop($"{m:X8} isn't a carriable model; not shown"); return; }
                var x = Extent(m);
                if (Math.Max(x.X, Math.Max(x.Y, x.Z)) > 4f) { Stop($"{m:X8} is too big to carry; not shown"); return; }
            }
            if (++_made > 3) { Stop($"copies of {m:X8} keep vanishing"); return; }
            var at = Peds.Position(_carrier);
            _copy = ped ? Peds.Spawn(m, at, 0f) : OBJECT.CREATE_OBJECT(m, at, false, false, true, false, true);
            if (!ped) STREAMING.SET_MODEL_AS_NO_LONGER_NEEDED(m);
            _requested = false;
            if (_copy == 0) { Stop($"couldn't make a copy of {m:X8}"); return; }
            Copies.Add(_copy);
            ENTITY.SET_ENTITY_VISIBLE(_copy, false);   // shown once it's on them, not while it drops into place
            if (ped)
            {
                // A body or a carcass: dead, quiet, and nobody aims at it.
                AUDIO.DISABLE_PED_PAIN_AUDIO(_copy, true);
                PED.SET_PED_CAN_BE_TARGETTED(_copy, false);
                ENTITY.SET_ENTITY_HEALTH(_copy, 0, 0);
            }
            else
            {
                ENTITY.SET_ENTITY_AS_MISSION_ENTITY(_copy, true, true);
                TASK._MAKE_OBJECT_CARRIABLE(_copy);
            }
            ENTITY.SET_ENTITY_INVINCIBLE(_copy, true);
            ENTITY.SET_ENTITY_CAN_BE_DAMAGED(_copy, false);
            ENTITY.SET_ENTITY_NO_COLLISION_ENTITY(_copy, _carrier, false);
            if (puppet != _carrier) ENTITY.SET_ENTITY_NO_COLLISION_ENTITY(_copy, puppet, false);   // on the horse: nor with the rider
            // For show only: nothing to loot or skin, and no prompt to take it.
            ENTITY._SET_ENTITY_FULLY_LOOTED(_copy, true);
            ENTITY._SET_ENTITY_CARRYING_FLAG(_copy, HidePrompts, true);
            if (_on == Carry.Horse) ENTITY._SET_ENTITY_CARRYING_FLAG(_carrier, NoTakingFromMount, true);
        }

        // A round after the copy appears (dead by then) the game is asked to carry it; if it hasn't
        // within 2 s, or drops it later, we pin it ourselves.
        void Hold(long now)
        {
            if (!ENTITY.DOES_ENTITY_EXIST(_copy) || !Peds.Exists(_carrier)) { RemoveCopy(); return; }
            if (ENTITY.IS_ENTITY_ATTACHED_TO_ENTITY(_copy, _carrier) || PED._GET_CARRIER_AS_PED(_copy) == _carrier)
            {
                if (!_held && !_pinned)
                {
                    _held = true;
                    ENTITY.SET_ENTITY_VISIBLE(_copy, true);
                    Note($"{_show:X8} carried the game's way on a partner's {Place}");
                }
                return;
            }
            if (!_asked)
            {
                _asked = true;
                _sinceMs = now;
                uint config = ENTITY._GET_OPTIMAL_CARRY_CONFIG(_copy, 0);
                if (config != 0) { TASK.TASK_CARRIABLE(_copy, config, _carrier, 0, 0); return; }
            }
            else if (!_held && !_pinned && now - _sinceMs < TakeMs) return;
            Pin();
        }

        // shortcut: the pin (across the shoulders, behind the saddle) is a guess, used only when the game won't carry the copy; tune it from a screenshot if the log says "pinned".
        void Pin()
        {
            bool ped = ENTITY.IS_ENTITY_A_PED(_copy), human = ped && PED.IS_PED_HUMAN(_copy);
            // Lying across: people stand along z, animals run along y.
            float roll = human ? 90f : 0f, yaw = ped && !human ? 90f : 0f, back = _on == Carry.Horse ? -0.45f : 0f;
            ENTITY.ATTACH_ENTITY_TO_ENTITY(_copy, _carrier, 0, 0f, back, 0.5f, 0f, roll, yaw, false, false, false, ped, 2, true, false, false);
            ENTITY.SET_ENTITY_COLLISION(_copy, false, false);   // ours to hold: it touches nothing
            ENTITY.SET_ENTITY_VISIBLE(_copy, true);
            if (!_pinned) Note($"{_show:X8} pinned on a partner's {Place} (the game didn't carry it{(_held ? " for long" : "")})");
            _pinned = true;
            _held = false;
        }

        string Place => _on == Carry.Horse ? "horse" : "shoulder";

        unsafe void RemoveCopy()
        {
            if (_copy != 0 && ENTITY.DOES_ENTITY_EXIST(_copy))
            {
                int h = _copy;
                if (_held) ENTITY._DELETE_CARRIABLE(&h);   // carried by the game: let go of it the game's way
                Peds.Delete(_copy);
            }
            Copies.Remove(_copy);
            _copy = 0;
            _asked = _held = _pinned = false;
        }

        // Everything this puppet's load put in our world (Puppet.Delete calls this too).
        public void Delete()
        {
            RemoveCopy();
            if (_pelt) { PeltHorses.Remove(_carrier); if (Peds.Exists(_carrier)) PED._CLEAR_PELT_FROM_HORSE(_carrier, (int)_show); }
            if (_requested) STREAMING.SET_MODEL_AS_NO_LONGER_NEEDED(_show);
            _show = 0;
            _on = Carry.Nothing;
            _carrier = 0;
            _pelt = _stop = _requested = false;
            _made = 0;
        }

        void Stop(string why) { _stop = true; Note(why); }

        static void Note(string what)
        {
            if (Logged.Count < 100 && Logged.Add(what)) CoopScript.Log("carrying: " + what);
        }
    }
}
