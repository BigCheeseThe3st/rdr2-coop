// The quick-draw duel's game side (0.9.0, [Games] Duel; the rules are src/Net/Duel.cs). Setup lists the partners
// whose stand-in stands within 20 m of us (none: it says so and opens nothing), the host picks one, and the pause
// before DRAW (1-3 s) is picked here. While we duel, from GO until our DRAW, aim and attack (on foot, on a horse,
// in a wagon, and melee) are off every frame: the controls the story's own duels turn off. Every frame, a shot of
// ours that leaves while we lock on to or free-aim at the other's stand-in is our shot (before DRAW: too soon);
// their stand-in can be locked on to while we duel (it can't otherwise). Cues are subtitles ("Holster and face
// <name>", "DRAW!" with the shop's short sound, the verdict); the panel is the framework's, top right under the
// party list: "DUEL vs <name>: ...". Losing to a shot knocks us over for 3 s (a ragdoll; not on a horse or in a
// vehicle). Nothing runs in a story mission or cutscene or at a table. CoopScript hands us the stand-ins.
using System;
using System.Collections.Generic;
using System.Linq;
using RDR2;
using RDR2.Native;
using RDR2Coop.Net;
using Screen = RDR2.UI.Screen;

namespace RDR2Coop.Mod
{
    static class Duels
    {
        // INPUT_AIM, INPUT_ATTACK, INPUT_ATTACK2, INPUT_MELEE_ATTACK, INPUT_HORSE_AIM, INPUT_HORSE_ATTACK, INPUT_VEH_AIM, INPUT_VEH_ATTACK (joaat of the names)
        static readonly uint[] Guns = { 0xF84FA74F, 0x07CE1E61, 0x0283C582, 0xB2F377E8, 0x61470051, 0x60C81CDE, 0xD7CAFCEF, 0xF4330038 };
        const int LookMs = 200;
        static readonly Random Rng = new Random();
        static int _standIn;   // the other's stand-in, made targetable while we duel (0: none)
        static long _lookedMs = -LookMs;
        static bool _wasShooting, _logged;

        public static void Setup(Action<string> done, ServerLink link, Func<byte, int> standIn)
        {
            var me = Game.Player.Character;
            if (me == null || !me.Exists()) { done(null); return; }
            var rows = new List<(string, Action)>();
            foreach (var e in link.Roster)
            {
                int ped = e.Id == link.MyId ? 0 : standIn(e.Id);
                if (ped == 0 || Peds.Position(ped).DistanceTo(me.Position) > Duel.RangeM) continue;
                byte id = e.Id;
                rows.Add((e.Name, () => done(Duel.Params(link.MyId, id, Rng.Next(Duel.MinPauseMs, Duel.MaxPauseMs + 1)))));
            }
            if (rows.Count == 0) { Screen.DisplaySubtitle($"Co-op: stand within {Duel.RangeM:0} m of a partner to duel them"); done(null); return; }
            PickList.Open("Duel who?", rows, () => done(null), "Face them, hands off your gun until DRAW!");
        }

        public static void View(Contest contest, long now, Func<byte, int> standIn)
        {
            var duel = contest as Duel;
            if (duel == null) { Release(); _logged = false; return; }
            bool fall = duel.TakeFall();   // now or never: not after a cutscene that came between
            if (MissionProbe.StoryMission || MissionProbe.InMissionScene || Activities.Busy) { Release(); return; }
            string cue = duel.Cue(now);
            if (cue != null) { Screen.DisplaySubtitle(new string(cue.Where(c => c != '~' && c != '<' && c != '>').ToArray())); if (cue == Duel.DrawCue) AUDIO.PLAY_SOUND_FRONTEND("INFO", "HUD_SHOP_SOUNDSET", true, 0); }   // names come from strangers: no ~ < >
            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;
            if (fall && !me.IsOnMount && !PED.IS_PED_IN_ANY_VEHICLE(me.Handle, false)) PED.SET_PED_TO_RAGDOLL(me.Handle, 3000, 3000, 0, false, false, null);
            if (!duel.Dueling) { Release(); return; }
            if (!duel.Drawn(now)) foreach (uint input in Guns) PAD.DISABLE_CONTROL_ACTION(0, input, false);
            if (now - _lookedMs >= LookMs)
            {
                _lookedMs = now;
                int ped = standIn(duel.Foe);
                if (ped != _standIn) { Release(); _standIn = ped; if (ped != 0) PED.SET_PED_CAN_BE_TARGETTED(ped, true); }
            }
            bool shooting = PED.IS_PED_SHOOTING(me.Handle);   // true only around the frame a shot leaves
            if (shooting && !_wasShooting && Peds.Exists(_standIn))
            {
                int player = Game.Player.Handle;
                if (PLAYER.IS_PLAYER_TARGETTING_ENTITY(player, _standIn, false) || PLAYER.IS_PLAYER_FREE_AIMING_AT_ENTITY(player, _standIn))
                {
                    duel.Shot(now);
                    if (!_logged) { _logged = true; CoopScript.Log($"duel: our shot at {duel.NameOf(duel.Foe)}" + (duel.Drawn(now) ? "" : ", before DRAW")); }
                }
            }
            _wasShooting = shooting;
        }

        static void Release()
        {
            if (Peds.Exists(_standIn)) PED.SET_PED_CAN_BE_TARGETTED(_standIn, false);   // as Puppet.Spawn leaves it
            _standIn = 0;
            _wasShooting = false;
        }
    }
}
