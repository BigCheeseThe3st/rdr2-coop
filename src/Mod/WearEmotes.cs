// Wear and Emotes (0.8.0, [Visuals] in RDR2Coop.ini). How wet and how dirty our clothes are, and
// the scenario we play (a bench, a chair, a rail, the campfire, the bedroll...), go out with our
// position; a partner's stand-in gets their wet or muddy clothes and plays their scenario where
// they are, the way NPC copies do in MirrorWorld. A lost hat needs nothing here: the hat is a
// piece of the outfit Looks.Capture sends (again within 2 s of a change), and the stand-in is
// dressed again from scratch.
using System.Collections.Generic;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    static class Wear
    {
        // _GET_PED_DIRT_LEVEL(ped, composite layer): 0..1, blood included as far as the game counts it.
        // ScriptHookRDR2DotNet 2.2.0 only has it unnamed.
        const ulong GetPedDirtLevel = 0x0105FEE8F9091255;
        static float _wet;
        static byte _dirt;
        static long _readMs = -1;

        // Ours, read twice a second. Nothing reads how wet the game has made us: being in water up
        // to the waist wets us to the waist, swimming all over, then it dries (WearLevels.Wet). Rain
        // isn't counted: it falls on the stand-in too.
        public static void Ours(int ped, long now, ref PlayerState s)
        {
            if (_readMs < 0 || now - _readMs >= 500)
            {
                float water = PED.IS_PED_SWIMMING(ped) ? 1f : ENTITY.GET_ENTITY_SUBMERGED_LEVEL(ped);
                _wet = WearLevels.Wet(_wet, water, _readMs < 0 ? 0 : now - _readMs);
                _dirt = WearLevels.ToByte(Function.Call<float>(GetPedDirtLevel, ped, true));
                _readMs = now;
            }
            s.Wet = WearLevels.ToByte(_wet);
            s.Dirt = _dirt;
        }

        sealed class Shown { public int Ped; public uint Look; public byte Wet, Dirt; public long Ms; }
        static readonly Dictionary<byte, Shown> _shown = new Dictionary<byte, Shown>();

        // Theirs on their stand-in (id: the player), at most once a second and only on a real change.
        // look: the look last put on it; a new stand-in or new clothes start clean and dry, so they
        // get it all again a second later, once dressed. 0 from players before 0.8.0: nothing changes.
        public static void Show(byte id, int ped, uint look, PlayerState s, long now)
        {
            if (!_shown.TryGetValue(id, out var w) || w.Ped != ped || w.Look != look)
                _shown[id] = w = new Shown { Ped = ped, Look = look, Ms = now };
            if (now - w.Ms < 1000) return;
            w.Ms = now;
            if (WearLevels.Wetter(s.Wet, ref w.Wet))
            {
                // shortcut: the scale is a guess (height in metres up from the feet, amount 0..1); check it in game.
                PED.SET_PED_WETNESS_HEIGHT(ped, s.Wet / 255f * 2f);
                PED._0xF9CFF5BB70E8A2CB(ped, s.Wet / 255f);   // _SET_PED_WETNESS_AMOUNT, used with the height in R*'s scripts
                CoopScript.Log($"player {id}'s stand-in: wet {s.Wet * 100 / 255}%");
            }
            if (WearLevels.DirtChanged(s.Dirt, ref w.Dirt))
            {
                PED._SET_PED_DIRT_CLEANED(ped, s.Dirt / 255f, -1, true, true);   // sets the dirt level; R*'s baths pass 0
                if (s.Dirt < WearLevels.Step) { PED.CLEAR_PED_ENV_DIRT(ped); PED.CLEAR_PED_BLOOD_DAMAGE(ped); }   // they bathed: mud and blood off too
                CoopScript.Log($"player {id}'s stand-in: dirt {s.Dirt * 100 / 255}%");
            }
        }
    }

    static class Emotes
    {
        static uint _ours;
        static long _readMs = -1;

        // The scenario we play, 0 = none, read four times a second the way OwnedWorld reads NPCs'.
        public static uint Ours(int ped, long now)
        {
            if (_readMs >= 0 && now - _readMs < 250) return _ours;
            _readMs = now;
            return _ours = OwnedWorld.ActionOf(ped, out uint scenario) == EntityAction.Scenario ? scenario : 0;
        }

        // One per stand-in: the scenario it plays and when that last changed.
        public sealed class Act { public uint Scenario; public long Ms = -1000; }

        // Their scenario on the stand-in, in place where they are, as MirrorWorld.ActOut does. True
        // while it plays, so it isn't steered; it stands up when they stop, or walk off.
        public static bool Play(int ped, PlayerState s, long now, Act act)
        {
            if (s.Scenario == 0 && act.Scenario == 0) return false;   // the usual case, and players before 0.8.0
            if (now - act.Ms < 500) return act.Scenario != 0;         // two changes a second at most, however their game flickers
            var at = CoopScript.ToGame(s.Position);
            uint want = EmoteRules.Want(s.Scenario, s.Velocity.Length, Peds.Position(ped).DistanceTo(at));
            if (want == act.Scenario) return want != 0;
            if (act.Scenario != 0) TASK.CLEAR_PED_TASKS(ped, false, false);   // stand up
            act.Scenario = want;
            act.Ms = now;
            if (want == 0) return false;
            ENTITY._SET_ENTITY_COORDS_AND_HEADING(ped, at, s.Heading, false, false, false);
            TASK.TASK_START_SCENARIO_IN_PLACE_HASH(ped, want, -1, true, 0, s.Heading, false);
            CoopScript.Log($"a stand-in plays scenario {want:X8}");
            return true;
        }
    }
}
