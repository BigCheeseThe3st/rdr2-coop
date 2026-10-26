// Their horse waits where they leave it (0.8.0, Visuals.Horses). When a partner gets
// off, the stand-in horse under their puppet stays standing there instead of going:
// no task, invincible as before, and nobody else can get on it or be kicked by it.
// They get back on it when they mount the same breed close by; it goes when they ride
// another horse, after ten minutes unridden, or with their puppet (out of range, left).
using System;
using System.Collections.Generic;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    static class WaitingHorses
    {
        const float ReuseRange = 30f;            // they get on further from it than this: another horse
        const int KeepMs = 10 * 60 * 1000;
        static readonly Dictionary<int, int> Waiting = new Dictionary<int, int>();   // horse -> when it was left (TickCount)

        // Partners' horses standing unridden: never NPCs to share, whoever's area they're in.
        public static IEnumerable<int> All => Waiting.Keys;

        // They got off: the puppet comes off the way the story scripts take a rider off; the horse stays.
        public static void Leave(int rider, int horse)
        {
            if (PED.IS_PED_ON_MOUNT(rider)) PED._REMOVE_PED_FROM_MOUNT(rider, true, false);
            TASK.CLEAR_PED_TASKS(horse, true, false);   // stops where it is
            Lock(horse, true);
            Waiting[horse] = Environment.TickCount;
        }

        // They get on a horse again: false when this one waits too far from where they mount.
        public static bool CanCarry(int horse, Vec3 mountAt) =>
            !Waiting.ContainsKey(horse) || Peds.Position(horse).DistanceTo(CoopScript.ToGame(mountAt)) <= ReuseRange;

        // It stops waiting: ridden again, or about to go. False if it wasn't waiting.
        public static bool Take(int horse)
        {
            if (!Waiting.Remove(horse)) return false;
            if (Peds.Exists(horse)) Lock(horse, false);
            return true;
        }

        public static bool TimeUp(int horse) => Waiting.TryGetValue(horse, out int at) && Environment.TickCount - at > KeepMs;

        // Ped config flags (ePedScriptConfigFlags) as the story scripts set them on horses:
        // 136 can't be mounted, 412 no horse prompts, 419 no mount prompt; 471 never kicks.
        static void Lock(int horse, bool on)
        {
            PED.SET_PED_CONFIG_FLAG(horse, 136, on);
            PED.SET_PED_CONFIG_FLAG(horse, 412, on);
            PED.SET_PED_CONFIG_FLAG(horse, 419, on);
            PED.SET_PED_CONFIG_FLAG(horse, 471, true);
        }
    }
}
