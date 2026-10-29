// Helpers shared by the partner puppet and the guest's copies of the host's
// NPCs: spawning, steering towards network positions, and a damage trap that
// reports hits instead of letting the local game decide who dies.
using System.Collections.Generic;
using RDR2;
using RDR2.Math;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    static class Peds
    {
        public const float TeleportDistance = 8f;   // further than this: snap, don't walk
        public static int RetaskEveryMs = 150;      // re-issuing a task every frame resets its animation

        // Model streaming is asynchronous; ask and check, giving up after a
        // short wait so a bad model can't hang the game tick.
        public static bool Load(uint model)
        {
            if (model == 0 || !new Model(model).IsInCdImage) return false;
            if (!STREAMING.IS_MODEL_A_PED(model) && !STREAMING.IS_MODEL_A_VEHICLE(model)) return false;   // no props from the wire
            if (STREAMING.HAS_MODEL_LOADED(model)) return true;
            STREAMING.REQUEST_MODEL(model, false);
            for (int i = 0; i < 20 && !STREAMING.HAS_MODEL_LOADED(model); i++) Script.Yield();
            return STREAMING.HAS_MODEL_LOADED(model);
        }

        // A ped that only does what we tell it. 0 if the model isn't available.
        // Every ped the mod made (copies, stand-ins, their horses, wagon drivers, carried bodies, ambushers): mission
        // entities like the game's own script peds, so MirrorWorld's hiding of a guest's own script people must skip them.
        public static readonly HashSet<int> Ours = new HashSet<int>();

        public static int Spawn(uint model, Vector3 at, float heading)
        {
            if (!STREAMING.IS_MODEL_A_PED(model) || !Load(model)) return 0;
            int ped = PED.CREATE_PED(model, at, heading, false, false, false, false);
            STREAMING.SET_MODEL_AS_NO_LONGER_NEEDED(model);
            if (ped == 0) return 0;
            PED._SET_RANDOM_OUTFIT_VARIATION(ped, true);   // script peds are invisible without an outfit
            ENTITY.SET_ENTITY_AS_MISSION_ENTITY(ped, true, true);
            ENTITY.SET_ENTITY_LOAD_COLLISION_FLAG(ped, true);
            PED.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS(ped, true);
            PED.SET_PED_KEEP_TASK(ped, true);
            Ours.Add(ped);
            if (Ours.Count > 4096) Ours.RemoveWhere(h => !Exists(h));   // handles of peds gone some other way
            return ped;
        }

        public static bool Exists(int ped) => ped != 0 && ENTITY.DOES_ENTITY_EXIST(ped);

        public static unsafe uint CurrentWeapon(int ped)
        {
            uint hash = 0;
            return WEAPON.GET_CURRENT_PED_WEAPON(ped, &hash, false, 0, false) ? hash : 0;
        }

        public static Vector3 Position(int ped) => ENTITY.GET_ENTITY_COORDS(ped, true, true);

        public static void Delete(int ped)
        {
            Ours.Remove(ped);
            if (Exists(ped)) Entity.FromHandle(ped)?.Delete();
        }

        // Walks, runs or rides `ped` towards a networked position. Horses use
        // wider gait bands. Returns the new last-task time.
        // standing: whether the ped already has its standing order (callers
        // reset lastTaskMs to 0 after giving it any other task).
        public static long Steer(int ped, Vec3 position, Vec3 velocity, float heading, bool horse, long lastTaskMs, ref bool standing, long now)
        {
            var target = CoopScript.ToGame(position);
            float gap = Position(ped).DistanceTo(target);
            float speed = velocity.Length;
            if (lastTaskMs == 0) standing = false;

            // Far behind: jump there where nobody sees it; in view, run to catch up
            // (unless it's hopelessly far).
            float snapAt = TeleportDistance * (horse ? 2f : 1f);
            if (gap > snapAt && (gap > snapAt * 4f || !ENTITY.IS_ENTITY_ON_SCREEN(ped)))
            {
                ENTITY._SET_ENTITY_COORDS_AND_HEADING(ped, target, heading, false, false, false);
                standing = false;
                return 0;
            }
            if (now - lastTaskMs < RetaskEveryMs) return lastTaskMs;

            if (speed < 0.3f && gap < (horse ? 1f : 0.6f))
            {
                // One standing order, then leave it be so its idle animations
                // play; turn (walking round, not spinning) only when the
                // owner's has turned.
                float turn = ((heading - ENTITY.GET_ENTITY_HEADING(ped)) % 360f + 540f) % 360f - 180f;
                if (System.Math.Abs(turn) > 30f) TASK.TASK_ACHIEVE_HEADING(ped, heading, 1500);
                else if (!standing) TASK.TASK_STAND_STILL(ped, -1);
                else return lastTaskMs;
                standing = true;
                return now;
            }

            // Aim a little ahead so the ped doesn't stop and start between
            // snapshots. Blend 1 = walk, 2 = run/canter, 3 = sprint/gallop.
            var ahead = CoopScript.ToGame(position + velocity * (horse ? 0.6f : 0.5f));
            float blend = gap > snapAt ? 3f
                        : horse ? (speed > 9f ? 3f : speed > 3.5f ? 2f : 1f)
                                : (speed > 5.5f ? 3f : speed > 2.2f ? 2f : 1f);
            TASK.TASK_GO_STRAIGHT_TO_COORD(ped, ahead, blend, -1, heading, horse ? 0.5f : 0.2f, 0);
            standing = false;
            return now;
        }

        // The vehicle a ped sits in, or 0.
        public static int VehicleOf(int ped) =>
            Exists(ped) && PED.IS_PED_IN_ANY_VEHICLE(ped, false) ? PED.GET_VEHICLE_PED_IS_IN(ped, false) : 0;

        // Seat index of `ped` in `vehicle` (-1 = driver), or sbyte.MinValue.
        public static sbyte SeatOf(int vehicle, int ped)
        {
            int n = VEHICLE.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS(vehicle);
            for (int seat = -1; seat < n; seat++)
                if (VEHICLE.GET_PED_IN_VEHICLE_SEAT(vehicle, seat) == ped) return (sbyte)seat;
            return sbyte.MinValue;
        }

        // Puts `ped` in a seat unless it is already there; false while the seat is taken.
        public static bool SitIn(int ped, int vehicle, int seat)
        {
            if (!Exists(ped) || !Exists(vehicle)) return false;
            if (seat < -1 || seat >= VEHICLE.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS(vehicle)) return false;   // a seat from the network it hasn't got
            int there = VEHICLE.GET_PED_IN_VEHICLE_SEAT(vehicle, seat);
            if (there == ped) return true;
            if (there != 0) return false;
            PED.SET_PED_INTO_VEHICLE(ped, vehicle, seat);
            return true;
        }

        // Damage trap: a huge health pool so the local game never kills the
        // ped; each tick the lost health is read, reported and refilled.
        public const int TrapHealth = 10000;

        public static void ArmTrap(int ped)
        {
            ENTITY.SET_ENTITY_INVINCIBLE(ped, false);
            ENTITY.SET_ENTITY_CAN_BE_DAMAGED(ped, true);
            PED.SET_PED_MAX_HEALTH(ped, TrapHealth);
            ENTITY.SET_ENTITY_HEALTH(ped, TrapHealth, 0);
        }

        // Damage dealt by a ped since the last check (0 if none). `attacker`
        // restricts it to one ped; `ignore` skips one ped (friendly fire).
        public static unsafe int TakeTrapped(int ped, int attacker, int ignore, out int bone)
        {
            bone = 0;
            int health = ENTITY.GET_ENTITY_HEALTH(ped);
            if (health >= TrapHealth) return 0;

            bool byPed = attacker != 0
                ? ENTITY.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY(ped, attacker, true, true)
                : ENTITY.HAS_ENTITY_BEEN_DAMAGED_BY_ANY_PED(ped);
            if (ignore != 0 && ENTITY.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY(ped, ignore, true, true)) byPed = false;

            int b = 0;
            if (PED.GET_PED_LAST_DAMAGE_BONE(ped, &b)) bone = b;

            ENTITY.SET_ENTITY_HEALTH(ped, TrapHealth, 0);
            ENTITY.CLEAR_ENTITY_LAST_DAMAGE_ENTITY(ped);
            PED.CLEAR_PED_LAST_DAMAGE_BONE(ped);
            return byPed ? TrapHealth - health : 0;
        }
    }
}
