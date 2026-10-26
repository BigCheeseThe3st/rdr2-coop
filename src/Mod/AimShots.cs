// Partners aim and fire (0.8.0, [Visuals] Aiming). We send whether we aim, where at
// (what we're locked on to or aiming at, else a point 50 m along the camera) and a
// count of our shots. Their stand-in raises its gun to that point, walking too, and
// shows each shot as a muzzle flash and a gunshot at its gun. It never fires: it has
// no ammo and only gets aim orders that don't shoot, so no bullet or damage comes from it.
using RDR2;
using RDR2.Math;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    // Our side: what goes into the state we send.
    static class AimSender
    {
        static byte _shots;
        static bool _wasShooting;

        // Every frame: IS_PED_SHOOTING is true only around the frame a shot leaves.
        public static void CountShots(int me)
        {
            if (!Visuals.Aiming) return;
            bool shooting = PED.IS_PED_SHOOTING(me);
            if (shooting && !_wasShooting) _shots++;
            _wasShooting = shooting;
        }

        // Once Weapon is filled in.
        public static unsafe void Capture(ref PlayerState s)
        {
            if (!Visuals.Aiming) return;
            s.Shots = _shots;
            if (s.Weapon == 0) return;   // aiming empty-handed only picks someone to talk to
            int player = Game.Player.Handle;
            bool locked = PLAYER.IS_PLAYER_TARGETTING_ANYTHING(player);
            if (!locked && !PLAYER.IS_PLAYER_FREE_AIMING(player)) return;
            s.Pose |= PoseFlags.Aiming;
            int target = 0;
            bool on = locked ? PLAYER.GET_PLAYER_TARGET_ENTITY(player, &target) : PLAYER.GET_ENTITY_PLAYER_IS_FREE_AIMING_AT(player, &target);
            s.AimAt = on && Peds.Exists(target) ? CoopScript.ToNet(Peds.Position(target))
                    : Aim.Along(CoopScript.ToNet(CAM.GET_GAMEPLAY_CAM_COORD()), CoopScript.ToNet(CAM.GET_GAMEPLAY_CAM_ROT(2)));
            // The "target" can be our own horse or wagon (aiming from the saddle or a seat): that's a point on
            // ourselves, which partners' games refuse ("the aim point is on the stand-in"). Aim along the camera then.
            if (Vec3.Distance(s.AimAt, s.Position) < 1.5f)
                s.AimAt = Aim.Along(CoopScript.ToNet(CAM.GET_GAMEPLAY_CAM_COORD()), CoopScript.ToNet(CAM.GET_GAMEPLAY_CAM_ROT(2)));
        }
    }

    // Their side, one per stand-in.
    sealed class PuppetAim
    {
        // TASK_GO_TO_COORD_WHILE_AIMING_AT_COORD by hash: ScriptHookRDR2DotNet declares its floats as integers.
        const ulong GoWhileAiming = 0x11315AB3385B8AC0;
        const uint BurstFire = 0xD6FF6D61;   // FIRING_PATTERN_BURST_FIRE, what Rockstar passes; unused, it never shoots
        // Rockstar's random encounters play their distant carbine shot from this set.
        const string ShotSounds = "REFC_Sounds", ShotSound = "CarbineShotDistant";
        static bool _soundsAsked;   // PREPARE_SOUNDSET called since the last Release

        int _lastShots = -1;
        bool _aiming, _still;   // it has our aim order; the standing one
        Vec3 _aimedAt;
        long _seenMs, _nextMs;

        // On foot: true while it aims (its aim order then also walks it, in place of Steer's).
        public bool OnFoot(int ped, PlayerState s, long now, ref long lastTaskMs)
        {
            if (now - _seenMs > 250) Stop(ref lastTaskMs);   // it rode, sat or lay down meanwhile: start afresh
            _seenMs = now;
            if (!Visuals.Aiming || (s.Pose & PoseFlags.Aiming) == 0 || s.Weapon == 0) return Stop(ref lastTaskMs);
            if (now < _nextMs) return _aiming;
            _nextMs = now + Peds.RetaskEveryMs;

            var from = CoopScript.ToNet(Peds.Position(ped));
            float gap = Vec3.Distance(from, s.Position);
            uint held = Peds.CurrentWeapon(ped);   // what ShowWeapon gave it (checked), not the hash off the wire
            // Far behind: Steer catches it up first. Guns and bows only, not a lasso or dynamite.
            // Whatever gun the stand-in ended up holding aims (the game sometimes keeps the previous one in hand for a
            // while after a weapon change, or hands out a base model for a unique variant); only no gun at all stops it.
            if (held != s.Weapon && held != _mismatchLogged) { _mismatchLogged = held; CoopScript.Log($"aiming with the stand-in's {held:X8} while they hold {s.Weapon:X8}"); }
            string why = gap > Peds.TeleportDistance ? "the stand-in is far behind" : Vec3.Distance(from, s.AimAt) < 1f ? "the aim point is on the stand-in"
                       : held == 0 || !(WEAPON.IS_WEAPON_A_GUN(held) || WEAPON.IS_WEAPON_BOW(held)) ? $"the stand-in holds {held:X8}, not a gun or bow" : null;
            if (why != null)
            {
                if (s.Weapon != _whyLogged) { _whyLogged = s.Weapon; CoopScript.Log($"no aim shown for weapon {s.Weapon:X8}: {why}"); }   // diagnostics (0.9.0), once per weapon
                return Stop(ref lastTaskMs);
            }

            bool still = s.Velocity.Length < 0.3f && gap < 0.6f;
            if (still)
            {
                if (_aiming && _still && !Aim.Moved(from, _aimedAt, s.AimAt)) return true;
                TASK.TASK_AIM_GUN_AT_COORD(ped, s.AimAt.X, s.AimAt.Y, s.AimAt.Z, -1, false, false);
            }
            else
            {
                // As Steer walks it: to a little ahead of them, walking or jogging (nobody sprints aiming).
                var to = s.Position + s.Velocity * 0.5f;
                Function.Call(GoWhileAiming, ped, to.X, to.Y, to.Z, s.AimAt.X, s.AimAt.Y, s.AimAt.Z, s.Velocity.Length > 2.2f ? 2f : 1f,
                              false, 0.2f, 0.2f, false, 0, false, BurstFire, 0);
            }
            _aiming = true;
            _still = still;
            _aimedAt = s.AimAt;
            lastTaskMs = now;
            return true;
        }

        uint _whyLogged, _mismatchLogged;

        bool Stop(ref long lastTaskMs)
        {
            if (_aiming) { _aiming = false; lastTaskMs = 0; }   // Steer then gives its own order at once, ending the aim
            return false;
        }

        // Each new shot of theirs with a gun: a muzzle flash at the gun in its hand and a gunshot.
        public void ShowShots(int ped, PlayerState s)
        {
            if (!Visuals.Aiming) return;
            int fired = Shots.NewShots(_lastShots, s.Shots);
            _lastShots = s.Shots;
            // Loaded while they aim, so their first shot is heard.
            if (!_soundsAsked && (s.Pose & PoseFlags.Aiming) != 0) { _soundsAsked = true; AUDIO.PREPARE_SOUNDSET(ShotSounds, false); }
            if (fired == 0) return;
            uint held = Peds.CurrentWeapon(ped);
            if (held == 0 || !WEAPON.IS_WEAPON_A_GUN(held) || WEAPON.IS_WEAPON_BOW(held)) return;

            int gun = WEAPON.GET_CURRENT_PED_WEAPON_ENTITY_INDEX(ped, 0);
            int muzzle = Peds.Exists(gun) ? ENTITY.GET_ENTITY_BONE_INDEX_BY_NAME(gun, "Gun_Muzzle") : -1;
            if (muzzle >= 0)
            {
                // On the gun, unturned: it points the way the barrel does (the gun's X axis).
                var at = ENTITY.GET_OFFSET_FROM_ENTITY_GIVEN_WORLD_COORDS(gun, ENTITY.GET_WORLD_POSITION_OF_ENTITY_BONE(gun, muzzle));
                GRAPHICS.USE_PARTICLE_FX_ASSET("core");
                GRAPHICS.START_PARTICLE_FX_NON_LOOPED_ON_ENTITY(Flash(held), gun, at, new Vector3(), 1f, false, false, false);
            }
            _soundsAsked = true;
            // shortcut: one sound per name plays at a time, so fast fire drops some; play them by sound id if testers notice.
            if (AUDIO.PREPARE_SOUNDSET(ShotSounds, false)) AUDIO.PLAY_SOUND_FROM_ENTITY(ShotSound, muzzle >= 0 ? gun : ped, ShotSounds, false, 0, 0);
        }

        // The game's own muzzle flashes ("core" effects).
        static string Flash(uint weapon) =>
            WEAPON.IS_WEAPON_SHOTGUN(weapon) ? "muz_shotgun"
            : WEAPON.IS_WEAPON_REVOLVER(weapon) || WEAPON.IS_WEAPON_PISTOL(weapon) ? "muz_revolver" : "muz_rifle";

        // When the session ends or the mod stops.
        public static void Release()
        {
            if (_soundsAsked) AUDIO._RELEASE_SOUNDSET(ShotSounds);
            _soundsAsked = false;
        }
    }
}
