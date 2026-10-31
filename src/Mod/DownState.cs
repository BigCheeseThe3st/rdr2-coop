// Going down instead of dying while partners are in the session (the author, 26 Oct: the death screen counts as a
// cutscene and the game's respawn looked like an automatic revive; "suppress that until all party members are
// dead"). Host and guests alike: low health puts us on the ground (safe, unable to act) until a partner revives us
// (F7 next to us) or no enemy near has fought for 10 s, then we get up (next to the area's owner when we were in
// their area). If the game kills us anyway (a one-shot kill, a fall, fire), its respawn and death camera are held
// back and we're brought back where we fell, down. Only when every partner is down too, or nobody revives us in
// 3 min, does the game's own death happen (a story mission fails, the game respawns us). The 10 s doesn't count
// while the area's owner is in a cutscene or loading, and a down player whose controls something else hands back
// (the mission party showing us after a cutscene) is put down again.
using RDR2;
using RDR2.Math;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class DownState
    {
        const float DownAt = 0.15f;       // of max health
        const int CalmMs = 10000;
        const int BleedOutMs = 180000;    // down this long without a revive: the game's own death (lawmen never stop; George, 30 Oct)
        const int BringBackForMs = 5000;  // a death we can't undo in this long is let through (never stuck dead)

        public bool IsDown { get; private set; }
        long _calmSince = -1, _lastRagdoll, _lastSay, _deadSince = -1, _clearScreenUntil = -1, _downSince = -1;
        const int ClearScreenMs = 30000;   // after bringing us back from the game's death: its respawn screen is lifted for this long
        bool _wasDead, _returnAfterDeath, _held, _guarding, _clearLogged, _bledOut;

        static Ped Me => Game.Player.Character;
        static int Health => ENTITY.GET_ENTITY_HEALTH(Me.Handle);
        static int MaxHealth => System.Math.Max(1, Me.MaxHealth);

        // Would this hit (from the area owner's NPCs) finish us? Then go down instead.
        public bool WouldFinish(int damage) => !IsDown && Health - damage <= MaxHealth * DownAt;

        public void GoDown(string why)
        {
            if (IsDown) return;
            IsDown = true;
            _calmSince = -1;
            var me = Me;
            ENTITY.SET_ENTITY_HEALTH(me.Handle, System.Math.Max(1, (int)(MaxHealth * 0.1f)), 0);
            ENTITY.SET_ENTITY_INVINCIBLE(me.Handle, true);
            PLAYER.SET_PLAYER_CONTROL(Game.Player.Handle, false, 0, false);
            PED.SET_PED_TO_RAGDOLL(me.Handle, 2000, 2000, 0, false, false, null);
            CoopScript.Log("down: " + why);
            Stats.Add("downs");
        }

        public void GetUp(Vector3? at, string why)
        {
            if (!IsDown) return;
            IsDown = false;
            _downSince = -1;
            var me = Me;
            if (at.HasValue)
            {
                STREAMING.REQUEST_COLLISION_AT_COORD(at.Value);
                ENTITY.SET_ENTITY_COORDS(me.Handle, at.Value, false, false, false, false);
            }
            ENTITY.SET_ENTITY_INVINCIBLE(me.Handle, false);
            ENTITY.SET_ENTITY_HEALTH(me.Handle, MaxHealth / 2, 0);
            PLAYER.SET_PLAYER_CONTROL(Game.Player.Handle, true, 0, false);
            TASK.CLEAR_PED_TASKS(me.Handle, false, false);
            RDR2.UI.Screen.DisplaySubtitle("Co-op: " + why);
            CoopScript.Log("up: " + why);
        }

        // following: we are in someone else's area. ownerPos: where its owner is (null when it's us).
        // combat: an enemy near us fought just now. hold: the owner is in a cutscene or loading, so quiet doesn't
        // mean the fight's over. partners: other players in the session. allOthersDown: every one of them is down.
        public void Tick(bool following, Vector3? ownerPos, bool combat, bool hold, string ownerName, int partners, bool allOthersDown, long now)
        {
            var me = Me;
            bool canRevive = partners > 0 && !allOthersDown && !_bledOut;
            Guard(canRevive);

            if (me.IsDead)
            {
                if (_deadSince < 0) _deadSince = now;
                if (canRevive && now - _deadSince < BringBackForMs) { BringBack(me, now); return; }
                if (canRevive) { Guard(false); CoopScript.Log("down: couldn't bring us back from the game's death in 5 s: its respawn goes ahead"); }
                _wasDead = true;
                return;
            }
            _deadSince = -1;
            if (_wasDead) { _wasDead = false; _bledOut = false; _returnAfterDeath = following; _calmSince = -1; }
            if (now < _clearScreenUntil) ClearDeathScreen();

            if (!IsDown)
            {
                if (partners > 0 && Health <= MaxHealth * DownAt) GoDown("low health");
                else if (_returnAfterDeath && ownerPos.HasValue)
                {
                    if (combat || Held(hold, ownerName)) _calmSince = -1;
                    else if (_calmSince < 0) _calmSince = now;
                    else if (now - _calmSince > CalmMs)
                    {
                        _returnAfterDeath = false;
                        ENTITY.SET_ENTITY_COORDS(me.Handle, Beside(ownerPos.Value), false, false, false, false);
                        RDR2.UI.Screen.DisplaySubtitle($"Co-op: back with {ownerName}");
                    }
                }
                return;
            }

            if (partners == 0) { GetUp(null, "your partners have left"); return; }
            if (_downSince < 0) _downSince = now;
            bool bleedOut = now - _downSince > BleedOutMs;
            if (allOthersDown || bleedOut)
            {
                // Everyone's down, or nobody came: the game's own death (a story mission fails; the game respawns us).
                IsDown = false;
                _downSince = -1;
                _bledOut = bleedOut;
                ENTITY.SET_ENTITY_INVINCIBLE(me.Handle, false);
                PLAYER.SET_PLAYER_CONTROL(Game.Player.Handle, true, 0, false);
                ENTITY.SET_ENTITY_HEALTH(me.Handle, 0, 0);
                CoopScript.Log(bleedOut ? $"down: no revive in {BleedOutMs / 1000} s: the game's own death goes ahead"
                                        : "down: every partner is down too: the game's own death goes ahead");
                return;
            }

            if (now - _lastRagdoll > 1500)
            {
                _lastRagdoll = now;
                PED.SET_PED_TO_RAGDOLL(me.Handle, 2000, 2000, 0, false, false, null);
            }
            // Something handed our controls back (the mission party after a cutscene): still down.
            if (PLAYER.IS_PLAYER_CONTROL_ON(Game.Player.Handle))
            {
                PLAYER.SET_PLAYER_CONTROL(Game.Player.Handle, false, 0, false);
                ENTITY.SET_ENTITY_INVINCIBLE(me.Handle, true);
                CoopScript.Log("down: controls were handed back while down (a cutscene ending?): taken again");
            }
            if (combat || Held(hold, ownerName)) _calmSince = -1;
            else if (_calmSince < 0) _calmSince = now;
            if (_calmSince >= 0 && now - _calmSince > CalmMs)
            {
                GetUp(ownerPos.HasValue ? Beside(ownerPos.Value) : (Vector3?)null, "the fight's over - back on your feet");
                return;
            }
            if (now - _lastSay > 4000)
            {
                _lastSay = now;
                RDR2.UI.Screen.DisplaySubtitle($"Co-op: you're down - a partner can revive you, or you'll get up when the fight's over (you bleed out in {(BleedOutMs - (now - _downSince)) / 1000} s)");
            }
        }

        // While a partner can still revive us, the game's respawn after a death waits and its death camera stays off.
        void Guard(bool on)
        {
            if (on) CAM._SET_START_CINEMATIC_DEATH_CAM(false);
            if (on == _guarding) return;
            _guarding = on;
            MISC.PAUSE_DEATH_ARREST_RESTART(on);
            CoopScript.Log(on ? "down: the game's respawn after a death is held back while a partner can revive us"
                              : "down: the game's respawn after a death is back to normal (no partner who could revive us)");
        }

        // The game killed us although a partner could still revive us: back where we fell, down.
        void BringBack(Ped me, long now)
        {
            uint cause = PED.GET_PED_CAUSE_OF_DEATH(me.Handle);
            PED.RESURRECT_PED(me.Handle);
            if (me.IsDead) return;   // not yet: tried again next tick (BringBackForMs)
            if (CAM.IS_SCREEN_FADED_OUT() || CAM.IS_SCREEN_FADING_OUT()) CAM.DO_SCREEN_FADE_IN(500);
            GRAPHICS.ANIMPOSTFX_STOP_ALL();   // the death screen's colour effect
            _clearScreenUntil = now + ClearScreenMs; _clearLogged = false;
            _deadSince = -1;
            IsDown = false;
            GoDown($"the game killed us (cause {cause:X8}): brought back where we fell instead");
        }

        // The game's death sequence keeps going after we're brought back: it faded to its black respawn screen with
        // the spinning revolver and stayed there, revived or not, and partners saw "in a cutscene" and couldn't revive
        // (a tester's host, 28 Oct). We're alive: no respawn screen.
        void ClearDeathScreen()
        {
            bool loading = DLC.GET_IS_LOADING_SCREEN_ACTIVE(), spinner = HUD.BUSYSPINNER_IS_ON();
            if (!loading && !spinner && !CAM.IS_SCREEN_FADED_OUT() && !CAM.IS_SCREEN_FADING_OUT()) return;
            if (loading) SCRIPTS.SHUTDOWN_LOADING_SCREEN();
            if (spinner) HUD.BUSYSPINNER_OFF();
            CAM.DO_SCREEN_FADE_IN(500);
            if (!_clearLogged) { _clearLogged = true; CoopScript.Log($"down: the game's respawn screen came up after it brought us back (loading screen {loading}, spinner {spinner}): lifted"); }
        }

        // Whether the get-up timer is paused, logged when that changes.
        bool Held(bool hold, string ownerName)
        {
            if (hold != _held) CoopScript.Log(hold ? $"down: get-up timer paused: {ownerName} is in a cutscene or loading" : "down: get-up timer running again: the cutscene or loading is over");
            _held = hold;
            return hold;
        }

        static Vector3 Beside(Vector3 p) => p + new Vector3(2f, 2f, 0f);
    }
}
