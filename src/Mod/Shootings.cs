// The shooting contest's game side (0.9.0, [Games] Shooting; the rules are src/Net/Shooting.cs). Setup asks the
// host how many bottles and how long, and takes where they stand and face. At GO our own game stands the bottles
// (beer bottles: the model the story's bottle-shooting stranger mission sets up and checks for broken) on the
// ground ahead of that spot, without waiting on the model (it loads over a few ticks, 10 s at most), and every
// 200 ms counts ours that are broken (gone, broken, dead or moved more than 1 m) for the contest to report. They
// are ours alone: never networked, so a partner's stand-in shoots at nothing we can see. A story mission, a
// cutscene or one of the game's tables takes them away for the rest of the contest (the score we had stays).
// They're all deleted when the contest ends or is called off, when its host leaves, when the session ends and
// when the mod stops (Contests ticks View with null on every one of those). The panel is the framework's.
using System;
using System.Collections.Generic;
using System.Linq;
using RDR2;
using RDR2.Math;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    static class Shootings
    {
        const int CheckMs = 200, LoadMs = 10_000;
        const float MovedM = 1f;
        static readonly string[] Models = { "p_bottlebeer01x", "p_bottlejd01x" };   // the first the game has
        static readonly List<(int handle, Vector3 at)> Bottles = new List<(int, Vector3)>();
        static bool[] _broken;
        static uint _model;
        static long _checkedMs, _loadMs = -1;
        static bool _made, _gone, _requested;

        public static void Setup(Action<string> done) =>
            PickList.Open("Shooting: how many bottles?", Shooting.Counts.Select(n => ($"{n} bottles", (Action)(() => How(n, done)))).ToList(), () => done(null));

        static void How(int count, Action<string> done) =>
            PickList.Open("Shooting: how long?", Shooting.Times.Select(s => ($"{s} seconds", (Action)(() => Here(count, s, done)))).ToList(), () => done(null));

        static void Here(int count, int seconds, Action<string> done)
        {
            var me = Game.Player.Character;
            if (me == null || !me.Exists()) { done(null); return; }
            done(Shooting.Params(CoopScript.ToNet(me.Position), me.Heading, new Random().Next(), count, seconds));
        }

        public static void View(Contest contest, long now)
        {
            var shoot = contest as Shooting;
            if (shoot == null || !shoot.Running) { Clear(); return; }
            if (_gone || now - _checkedMs < CheckMs) return;
            _checkedMs = now;
            if (MissionProbe.StoryMission || MissionProbe.InMissionScene || Activities.Busy)
            {
                Clear();
                _gone = true;   // not again this contest: a bottle made now would stand where we may not be
                CoopScript.Log("shooting: bottles taken away (a mission, a cutscene or a table)");
                return;
            }
            if (!_made) { Make(shoot, now); return; }
            int broken = 0;
            for (int i = 0; i < Bottles.Count; i++)
            {
                if (Bottles[i].handle != 0 && !ENTITY.DOES_ENTITY_EXIST(Bottles[i].handle)) { Bottles[i] = (0, Bottles[i].at); _broken[i] = true; }   // gone: the game may give its handle to something else, which Clear must never delete
                if (_broken[i] || (_broken[i] = Broken(Bottles[i].handle, Bottles[i].at))) broken++;
            }
            shoot.Observe(broken, now);
        }

        static unsafe void Make(Shooting shoot, long now)
        {
            if (_loadMs < 0)
            {
                _loadMs = now;
                _model = Models.Select(m => MISC.GET_HASH_KEY(m)).FirstOrDefault(m => new Model(m).IsInCdImage);
            }
            if (_model == 0 || now - _loadMs > LoadMs) { Stop($"no bottle model {(_model == 0 ? "in this game" : "loaded in 10 s")}"); return; }
            if (!STREAMING.HAS_MODEL_LOADED(_model)) { STREAMING.REQUEST_MODEL(_model, false); _requested = true; return; }
            foreach (var spot in shoot.Spots())
            {
                float z = spot.Z;
                if (!MISC.GET_GROUND_Z_FOR_3D_COORD(spot.X, spot.Y, spot.Z + 10f, &z, false)) z = spot.Z;
                int h = OBJECT.CREATE_OBJECT(_model, spot.X, spot.Y, z, false, false, false, false, true);   // not networked, still until hit (as the story's)
                if (h == 0) continue;
                OBJECT.PLACE_OBJECT_ON_GROUND_PROPERLY(h, false);
                Bottles.Add((h, ENTITY.GET_ENTITY_COORDS(h, true, true)));
            }
            STREAMING.SET_MODEL_AS_NO_LONGER_NEEDED(_model);
            _requested = false;
            _made = true;
            _broken = new bool[Bottles.Count];
            CoopScript.Log($"shooting: {Bottles.Count} of {shoot.Count} bottles stood up");
        }

        static bool Broken(int h, Vector3 at) =>
            !ENTITY.DOES_ENTITY_EXIST(h) || OBJECT.HAS_OBJECT_BEEN_BROKEN((ulong)h) || ENTITY.IS_ENTITY_DEAD(h)
            || Vector3.Distance(ENTITY.GET_ENTITY_COORDS(h, true, true), at) > MovedM;

        static void Stop(string why)
        {
            Clear();
            _gone = true;
            CoopScript.Log("shooting: " + why);
            RDR2.UI.Screen.DisplaySubtitle("Co-op: your bottles couldn't be set up, so you can't score this time");
        }

        // Every bottle of ours goes; ready for the next contest.
        static void Clear()
        {
            foreach (var b in Bottles) Peds.Delete(b.handle);
            Bottles.Clear();
            if (_requested) STREAMING.SET_MODEL_AS_NO_LONGER_NEEDED(_model);
            _broken = null;
            _made = _gone = _requested = false;
            _loadMs = -1;
        }
    }
}
