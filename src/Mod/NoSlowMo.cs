// No slow motion ([Gameplay] NoSlowMotion, beta 8.5; the testers' request, 13 Nov: slow motion from the weapon wheel,
// Dead Eye and the like unsettles the mod): the game's menu slow-down (weapon wheel, satchel) is switched off and the
// time scale is held at 1 every frame, so no game script can slow our game down. Not in story cutscenes (they keep
// their own timing), under the pause menu or on a loading screen (a time scale of 1 there would run the game under it).
// The game's own frame time is watched against the real one: when it still runs slow, the log says so (a few times).
using RDR2;
using RDR2.Native;

namespace RDR2Coop.Mod
{
    static class NoSlowMo
    {
        public static bool On = true;
        const float SlowBelow = 0.9f;   // game time / real time under this: slow motion
        const int MaxLogged = 6;
        static int _logged;
        static bool _slow, _said;

        public static void Tick()
        {
            if (!On) return;
            if (!_said) { _said = true; CoopScript.Log("no slow motion: the weapon wheel, Dead Eye and the like run at normal speed ([Gameplay] NoSlowMotion=No turns it off)"); }
            if (Cutscenes.Playing || MissionProbe.InMissionScene || HUD.IS_PAUSE_MENU_ACTIVE() || DLC.GET_IS_LOADING_SCREEN_ACTIVE()) { _slow = false; return; }
            HUD._DISABLE_REDUCED_MENU_TIME_SCALE();
            MISC.SET_TIME_SCALE(1f);
            float real = MISC.GET_SYSTEM_TIME_STEP(), game = MISC.GET_FRAME_TIME();
            bool slow = real > 0.0005f && game / real < SlowBelow;
            if (slow && !_slow && _logged < MaxLogged) { _logged++; CoopScript.Log($"slow motion still seen: the game's frame time is x{game / real:0.00} of the real one (something sets the time scale after us)"); }
            _slow = slow;
        }
    }
}
