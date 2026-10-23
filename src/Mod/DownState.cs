// Going down instead of dying while in someone else's area. The game that runs
// the area keeps going; we lie on the ground (safe, unable to act) until a
// partner revives us (F7 next to us) or no enemy near has fought for 10 s, and
// then get up next to the area's owner. RDR2's own death never fires, so no
// doctor's bill and no respawn somewhere else.
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

        public bool IsDown { get; private set; }
        long _calmSince = -1, _lastRagdoll, _lastSay;
        bool _wasDead, _returnAfterDeath;

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
        }

        public void GetUp(Vector3? at, string why)
        {
            if (!IsDown) return;
            IsDown = false;
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

        // following: we are in someone else's area. ownerPos: where its owner is.
        // combat: an enemy near us fought in the last snapshot.
        public void Tick(bool following, Vector3? ownerPos, bool combat, string ownerName, long now)
        {
            var me = Me;
            // A death we couldn't catch (a fall, fire): once back up, rejoin the owner when it's calm.
            if (me.IsDead) { _wasDead = true; return; }
            if (_wasDead) { _wasDead = false; _returnAfterDeath = following; _calmSince = -1; }

            if (!IsDown)
            {
                if (following && Health <= MaxHealth * DownAt) GoDown("low health");
                else if (_returnAfterDeath && ownerPos.HasValue)
                {
                    if (combat) _calmSince = -1;
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

            if (!following) { GetUp(null, "you're in your own area again"); return; }

            if (now - _lastRagdoll > 1500)
            {
                _lastRagdoll = now;
                PED.SET_PED_TO_RAGDOLL(me.Handle, 2000, 2000, 0, false, false, null);
            }
            if (combat) _calmSince = -1;
            else if (_calmSince < 0) _calmSince = now;
            if (_calmSince >= 0 && now - _calmSince > CalmMs)
            {
                GetUp(ownerPos.HasValue ? Beside(ownerPos.Value) : (Vector3?)null, "the fight's over - back on your feet");
                return;
            }
            if (now - _lastSay > 4000)
            {
                _lastSay = now;
                RDR2.UI.Screen.DisplaySubtitle("Co-op: you're down - a partner can revive you, or you'll get up when the fight's over");
            }
        }

        static Vector3 Beside(Vector3 p) => p + new Vector3(2f, 2f, 0f);
    }
}
