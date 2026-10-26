// The hunting contest's game side (0.9.0, [Games] Hunting; the rules are src/Net/Hunting.cs). Setup asks the host
// how long (3, 5 or 10 minutes). While a hunt runs, every animal the scoreboard's Kills credits to us (our own shot in
// an area our game runs, or the area owner's credit in a partner's world) goes to the hunt, with a subtitle naming it
// and its points; nothing counts in a story mission or cutscene. It creates nothing and draws nothing in the world:
// the time left and the standings are the contests' panel (top right, under the party list, src/Mod/Contests.cs).
using System;
using System.Linq;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    static class Hunts
    {
        static Hunting _hunt;
        static long _now;
        static bool _hooked;

        public static void Setup(Action<string> done) =>
            PickList.Open("Hunting: how long?", Hunting.Lengths.Select(m => ($"{m} minutes", (Action)(() => done(Hunting.Params(m))))).ToList(),
                          () => done(null), "Most points wins: the bigger the animal, the more");

        public static void View(Contest contest, long now)
        {
            if (!_hooked) { Kills.Killed += OnKill; _hooked = true; }
            _hunt = contest as Hunting;   // null once it's gone: kills stop counting
            _now = now;
        }

        // Kills raises this for every kill our game credits; ours are the ones with our id.
        static void OnKill(byte player, bool human, uint model)
        {
            try
            {
                var hunt = _hunt;
                if (hunt == null || human || player != hunt.Me || MissionProbe.StoryMission || MissionProbe.InMissionScene) return;
                int points = hunt.Killed(model, _now);
                if (points > 0) RDR2.UI.Screen.DisplaySubtitle($"Co-op: {Hunting.AnimalName(model)}, {points} point{(points == 1 ? "" : "s")}");
            }
            catch (Exception e) { CoopScript.Log("hunting kill: " + e.Message); }
        }
    }
}
