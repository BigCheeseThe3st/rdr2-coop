// Map markers on copies: whatever the owner's game marks (mission characters,
// targets, enemies, mission wagons) gets a marker on the copy here too.
using System.Collections.Generic;
using RDR2.Native;

namespace RDR2Coop.Mod
{
    static class Blips
    {
        static bool _loggedHandle;
        public static readonly HashSet<int> Mine = new HashSet<int>();   // our own markers (partners far away): never objectives

        // Largest marker handle we've been given: tells the objective scan
        // which handle range markers live in.
        public static int MaxHandle { get; private set; }

        public static void Note(int handle)
        {
            if (handle <= MaxHandle) return;
            MaxHandle = handle;
            if (!_loggedHandle) { _loggedHandle = true; CoopScript.Log($"first marker handle: {handle} (0x{handle:X})"); }
        }

        public static void Sync(ref int blip, int entity, bool wanted, string style)
        {
            bool has = blip != 0 && MAP.DOES_BLIP_EXIST(blip);
            if (wanted && !has)
            {
                blip = MAP.BLIP_ADD_FOR_ENTITY(MISC.GET_HASH_KEY(style), entity);
                Note(blip);
            }
            else if (!wanted && has) Remove(ref blip);
        }

        public static unsafe void Remove(ref int blip)
        {
            if (blip != 0 && MAP.DOES_BLIP_EXIST(blip)) { int b = blip; MAP.REMOVE_BLIP(&b); }
            Mine.Remove(blip);
            blip = 0;
        }
    }
}
