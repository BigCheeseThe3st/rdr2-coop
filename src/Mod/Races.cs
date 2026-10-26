// The horse race's game side (0.9.0, [Games] Race; the rules are src/Net/Race.cs). Setup takes the host's map
// waypoint as the finish (none set: it says so and opens nothing; a race to where they stand would be won at GO)
// and asks how (any way, or on horseback only). While a race runs: the finish on the map (a blip named "Race
// finish") and in the world (a gold ring of a cylinder marker, the 15 m finish zone), and every 200 ms where we
// are and whether we're on a horse, for the race's own finish check. The blip and marker go while we're in a
// story mission (we're still watched, to be ranked when we come out) and when the race is over or called off
// (Contests ticks View with null).
using System;
using System.Collections.Generic;
using RDR2;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    static class Races
    {
        const int CheckMs = 200;
        static int _blip;
        static uint _cylinder;
        static long _checkedMs;

        public static void Setup(Action<string> done)
        {
            if (!MAP.IS_WAYPOINT_ACTIVE()) { RDR2.UI.Screen.DisplaySubtitle("Co-op: set a waypoint on the map first: the race is to it"); done(null); return; }
            var w = MAP._GET_WAYPOINT_COORDS();   // its z is always 0: the height map gives the ground's (as Waypoints does)
            How(new Vec3(w.X, w.Y, PATHFIND.GET_APPROX_FLOOR_FOR_POINT(w.X, w.Y)), done);
        }

        static void How(Vec3 finish, Action<string> done) =>
            PickList.Open("Race: how?", new List<(string, Action)> { ("Any way you like", () => done(Race.Params(finish, false))),
                                                                     ("On horseback only", () => done(Race.Params(finish, true))) }, () => done(null));

        public static void View(Contest contest, long now)
        {
            var race = contest as Race;
            if (race == null || !race.Running) { Blips.Remove(ref _blip); return; }
            bool mission = MissionProbe.StoryMission;   // nothing of ours shows in a story mission; we still watch where we are
            if (now - _checkedMs >= CheckMs)
            {
                _checkedMs = now;
                if (mission) Blips.Remove(ref _blip);
                else if (_blip == 0 || !MAP.DOES_BLIP_EXIST(_blip))
                {
                    _blip = MAP.BLIP_ADD_FOR_COORDS(MISC.GET_HASH_KEY("BLIP_STYLE_OBJECTIVE"), CoopScript.ToGame(race.Finish));
                    if (_blip != 0)
                    {
                        MAP._SET_BLIP_NAME(_blip, "Race finish");
                        Blips.Mine.Add(_blip);   // ours: never shared as a mission objective
                        CoopScript.Log($"race finish at {race.Finish}" + (race.HorseOnly ? " (on horseback only)" : ""));
                    }
                }
                var me = Game.Player.Character;
                if (me != null && me.Exists()) race.Observe(CoopScript.ToNet(me.Position), PED.IS_PED_ON_MOUNT(me.Handle), now);
            }
            if (mission || MissionProbe.InMissionScene) return;
            if (_cylinder == 0) _cylinder = MISC.GET_HASH_KEY("MARKERTYPE_CYLINDER");
            var f = race.Finish;
            GRAPHICS._DRAW_MARKER(_cylinder, f.X, f.Y, f.Z - 1f, 0f, 0f, 0f, 0f, 0f, 0f, 2f * Race.FinishM, 2f * Race.FinishM, 4f,
                                  235, 190, 70, 110, false, false, 2, false, null, null, false);
        }
    }
}
