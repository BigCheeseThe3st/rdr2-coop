// Another player as seen in our world: a ped we spawn and steer, plus a horse
// when they ride. While our game runs the area they are in, it also catches
// hits from our NPCs so their own character can take them.
using RDR2;
using RDR2.Math;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class Puppet
    {
        public uint PedModel { get; private set; }
        public int Handle => _ped;
        public int HorseHandle => _horse;
        public bool HasWaitingHorse => !_wasMounted && Peds.Exists(_horse);   // their horse stands where they got off
        public uint HorseModel => _horseModel;
        int _ped;
        int _horse;
        uint _horseModel;
        int _blip;
        long _lastTaskMs;
        bool _standing, _horseStanding;
        bool _wasMounted;
        bool _catchHits;
        readonly PuppetAim _aim = new PuppetAim();   // their aim and shots (AimShots.cs)
        public readonly Carrying Carried = new Carrying();   // what they carry (Carrying.cs)

        public bool Exists => Peds.Exists(_ped);
        public Vector3 Position => Peds.Position(_ped);

        public static Puppet Spawn(uint model, uint fallbackModel, Vector3 at, float heading, string name)
        {
            uint requested = model;   // compared next tick: the fallback must not look like a change
            int ped = Peds.Spawn(model, at, heading);
            if (ped == 0 && model != fallbackModel)
            {
                CoopScript.Log($"model {model:X8} not available, using {fallbackModel:X8}");
                model = fallbackModel;
                ped = Peds.Spawn(model, at, heading);
            }
            if (ped == 0) return null;

            ENTITY.SET_ENTITY_INVINCIBLE(ped, true);
            ENTITY.SET_ENTITY_CAN_BE_DAMAGED(ped, false);
            PED.SET_PED_CAN_BE_TARGETTED(ped, false);
            PED.SET_PED_RELATIONSHIP_GROUP_HASH(ped, MISC.GET_HASH_KEY("PLAYER"));

            int blip = MAP.BLIP_ADD_FOR_ENTITY(MISC.GET_HASH_KEY("BLIP_STYLE_FRIENDLY"), ped);
            if (blip != 0) MAP._SET_BLIP_NAME(blip, name);
            Blips.Note(blip);

            return new Puppet { PedModel = requested, _ped = ped, _blip = blip };
        }

        // The weapon in their hand (0 or unarmed: empty hands). Given with no ammo:
        // it's for show, and the stand-in ignores threats anyway (Peds.Spawn).
        // False while the weapon's model is still loading.
        public bool ShowWeapon(uint weapon)
        {
            uint unarmed = MISC.GET_HASH_KEY("WEAPON_UNARMED");
            if (weapon == 0 || weapon == unarmed || !WEAPON.IS_WEAPON_VALID(weapon))
            {
                WEAPON.SET_CURRENT_PED_WEAPON(_ped, unarmed, true, 0, false, false);
                return true;
            }
            if (!WEAPON._HAS_WEAPON_ASSET_LOADED(weapon)) { WEAPON._REQUEST_WEAPON_ASSET(weapon, 31, false); return false; }
            if (!WEAPON.HAS_PED_GOT_WEAPON(_ped, weapon, 0, false))
                WEAPON.GIVE_WEAPON_TO_PED(_ped, weapon, 0, true, false, 0, false, 0.5f, 1f, 0, false, 0f, false);
            WEAPON.SET_CURRENT_PED_WEAPON(_ped, weapon, true, 0, false, false);
            return true;
        }

        // On while our game runs the area this player is in: our NPCs' hits on
        // the puppet are caught and sent to them. Off: invincible stand-in.
        public void SetCatchHits(bool on)
        {
            if (on == _catchHits || !Exists) return;
            _catchHits = on;
            if (on) Peds.ArmTrap(_ped);
            else
            {
                ENTITY.SET_ENTITY_INVINCIBLE(_ped, true);
                ENTITY.SET_ENTITY_CAN_BE_DAMAGED(_ped, false);
            }
        }

        long _lastRagdollMs;
        readonly Stance _stance = new Stance();   // their crouch, jumps, climbs and falls ([Visuals] Stance)
        readonly Emotes.Act _act = new Emotes.Act();   // the scenario it plays for its player

        // Seated behind a rider (ride double): no orders, only the flash and bang of their shots.
        public void ShowShots(PlayerState s) { if (Exists) _aim.ShowShots(_ped, s); }

        // Their emote (EmoteMenu.cs; the animation is loaded), as a secondary upper-body task: following, walking and
        // riding carry on under it, so Follow needn't know. Not while it lies down or sits in a vehicle, and never on an
        // animal stand-in ([Coop] AppearAs= can be any ped model): the gestures are for a human skeleton.
        public void PlayEmote(string dict, string clip, int ms)
        {
            if (!Exists || !PED.IS_PED_HUMAN(_ped) || PED.IS_PED_RAGDOLL(_ped) || PED.IS_PED_IN_ANY_VEHICLE(_ped, false)) return;
            TASK.TASK_PLAY_ANIM(_ped, dict, clip, 8f, -8f, ms, EmoteList.Flags, 0f, false, 0, false, "", false);
        }

        // vehicle: the local vehicle (real or copy) they sit in, 0 = none.
        public void Follow(PlayerState s, long now, int vehicle)
        {
            if (!Exists) return;
            if (WaitingHorses.TimeUp(_horse)) DeleteHorse();   // their horse waited ten minutes unridden
            _aim.ShowShots(_ped, s);
            if ((s.Flags & StateFlags.Downed) != 0)
            {
                // Down in their game: lie where they are.
                if (Position.DistanceTo(CoopScript.ToGame(s.Position)) > 3f)
                    ENTITY.SET_ENTITY_COORDS(_ped, CoopScript.ToGame(s.Position), false, false, false, false);
                if (now - _lastRagdollMs > 1500)
                {
                    _lastRagdollMs = now;
                    PED.SET_PED_TO_RAGDOLL(_ped, 2000, 2000, 0, false, false, null);
                }
                return;
            }
            if (vehicle != 0)
            {
                if (_wasMounted) Dismount();
                Peds.SitIn(_ped, vehicle, s.Seat);
                return;
            }
            if (PED.IS_PED_IN_ANY_VEHICLE(_ped, false))
            {
                TASK.CLEAR_PED_TASKS_IMMEDIATELY(_ped, false, true);
                _lastTaskMs = 0;
            }
            if (s.Mounted) { FollowMounted(s, now); return; }
            if (_wasMounted) Dismount();
            // One order at a time on foot: their scenario (they stand still by it), else a move that has
            // started, else their aim, else plain following. Skipping stance and aim meanwhile is safe: both reset after a pause.
            if (Visuals.Emotes && Emotes.Play(_ped, s, now, _act)) { _lastTaskMs = 0; return; }   // sitting, leaning...: not steered meanwhile
            if (_stance.Show(_ped, s, now)) { _lastTaskMs = now; _standing = false; }   // a move plays: Steer only snaps it if it falls far behind
            else if (_aim.OnFoot(_ped, s, now, ref _lastTaskMs)) return;   // aiming: its order walks it (a move that started plays out first)
            _lastTaskMs = Peds.Steer(_ped, s.Position, s.Velocity, s.Heading, false, _lastTaskMs, ref _standing, now);
        }

        // Damage our NPCs did to the puppet since the last call (our own shots
        // don't count), for that player to take.
        public int TakeHits(int myPed, out int bone)
        {
            bone = 0;
            return _catchHits && Exists ? Peds.TakeTrapped(_ped, 0, myPed, out bone) : 0;
        }

        void FollowMounted(PlayerState s, long now)
        {
            if (!Peds.Exists(_horse) || _horseModel != s.MountModel || !WaitingHorses.CanCarry(_horse, s.MountPosition))
            {
                DeleteHorse();
                _horse = Peds.Spawn(s.MountModel, CoopScript.ToGame(s.MountPosition), s.MountHeading);
                if (_horse == 0) return;
                _horseModel = s.MountModel;
                ENTITY.SET_ENTITY_INVINCIBLE(_horse, true);
                ENTITY.SET_ENTITY_CAN_BE_DAMAGED(_horse, false);
                _lastTaskMs = 0;
            }

            if (!_wasMounted || !PED.IS_PED_ON_MOUNT(_ped))
            {
                if (WaitingHorses.Take(_horse)) _lastTaskMs = 0;   // back on the horse that waited: steer it afresh
                PED.SET_PED_ONTO_MOUNT(_ped, _horse, -1, true);
                _wasMounted = true;
            }
            _lastTaskMs = Peds.Steer(_horse, s.MountPosition, s.Velocity, s.MountHeading, true, _lastTaskMs, ref _horseStanding, now);
        }

        void Dismount()
        {
            _wasMounted = false;
            bool waits = Visuals.Horses && Peds.Exists(_horse);   // their horse stays where they left it
            if (waits) WaitingHorses.Leave(_ped, _horse);
            TASK.CLEAR_PED_TASKS_IMMEDIATELY(_ped, false, true);
            if (!waits) DeleteHorse();
            _lastTaskMs = 0;
        }

        void DeleteHorse()
        {
            WaitingHorses.Take(_horse);
            RideDouble.LetGo(_horse);   // never with our player still sitting on it
            Peds.Delete(_horse);
            _horse = 0;
            _horseModel = 0;
        }

        public unsafe void Delete()
        {
            Carried.Delete();
            if (_blip != 0 && MAP.DOES_BLIP_EXIST(_blip))
            {
                int blip = _blip;
                MAP.REMOVE_BLIP(&blip);
            }
            DeleteHorse();
            Peds.Delete(_ped);
            _ped = 0;
            _blip = 0;
        }
    }
}
