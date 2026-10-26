// Makes a local vehicle follow a networked one smoothly: its body follows the
// path by velocity every frame, while a driving order keeps the horses' legs
// (or a boat's engine) going. Used for copies of another game's vehicles and,
// in the game that runs an area, for its real vehicle while another player
// drives it.
using RDR2.Math;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    static class VehicleSteer
    {
        public const int DriveEveryMs = 400;   // how often the order is checked
        const int ReissueMs = 2000;            // how often a running order is replaced
        static readonly uint DriveTaskHash = RDR2.Native.MISC.GET_HASH_KEY("SCRIPT_TASK_VEHICLE_DRIVE_TO_COORD");

        // target/velocity/yaw: where the vehicle should be. driver: who holds
        // the reins (0 = leave the AI alone this frame).
        public static void Glide(int veh, uint model, Vector3 target, Vector3 current, Vector3 velocity, float yaw,
                                 bool onGround, bool draft, int driver, ref long lastDriveMs, long now)
        {
            // The body: the wanted velocity plus a firm pull onto the position,
            // capped; height left to gravity on land.
            var cur = ENTITY.GET_ENTITY_VELOCITY(veh, 0);
            var pull = (target - current) * 1.5f;
            var want = new Vector3(velocity.X + pull.X, velocity.Y + pull.Y, 0f);
            float max = new Vector3(velocity.X, velocity.Y, 0f).Length() + 5f;
            if (want.Length() > max) want = want.Normalized * max;
            ENTITY.SET_ENTITY_VELOCITY(veh, new Vector3(want.X, want.Y, onGround ? cur.Z : velocity.Z + pull.Z));

            // Heading eased towards the wanted one; pitch and roll stay with physics.
            var rot = ENTITY.GET_ENTITY_ROTATION(veh, 2);
            ENTITY.SET_ENTITY_ROTATION(veh, rot.X, rot.Y, Interpolator.LerpAngle(rot.Z, yaw, 0.1f), 2, true);

            // The horses: their legs only move while someone drives, so the
            // driver gets a driving order at the wanted speed along the path.
            // The body still follows the exact path: the velocity above is set
            // every frame, after the AI's own.
            if (!draft || now - lastDriveMs < DriveEveryMs) return;
            float speed = new Vector3(velocity.X, velocity.Y, 0f).Length();
            VEHICLE._SET_DRAFT_VEHICLE_DESIRED_SPEED(veh, speed);
            if (driver == 0) { lastDriveMs = now; return; }
            // A new driving order restarts the task, which then sits in "waiting
            // to start" for a moment; re-issued too often it never runs and the
            // horses stand still. So: a new order every 2 s (aimed ~4 s ahead),
            // sooner only when none is running.
            int status = TASK.GET_SCRIPT_TASK_STATUS(driver, DriveTaskHash, true);
            bool running = status == 0 || status == 1;   // waiting to start / performing
            if (running && now - lastDriveMs < ReissueMs) return;
            lastDriveMs = now;
            if (speed < 0.5f) TASK.TASK_VEHICLE_DRIVE_TO_COORD(driver, veh, current, 0f, 0UL, model, MirrorWorld.DriveMode, 3f, 0f);
            else TASK.TASK_VEHICLE_DRIVE_TO_COORD(driver, veh, target + velocity * 4f, speed, 0UL, model, MirrorWorld.DriveMode, 1f, 0f);

            if (!LastLog.TryGetValue(veh, out var last) || now - last > 5000)
            {
                LastLog[veh] = now;
                CoopScript.Log($"steer {veh}: driver {driver} in seat {VEHICLE.GET_PED_IN_VEHICLE_SEAT(veh, -1) == driver}, " +
                    $"task status {TASK.GET_SCRIPT_TASK_STATUS(driver, MISC.GET_HASH_KEY("SCRIPT_TASK_VEHICLE_DRIVE_TO_COORD"), true)}, " +
                    $"wanted {speed:0.0} m/s, draft desired {VEHICLE._GET_DRAFT_VEHICLE_DESIRED_SPEED(veh):0.0}, actual {ENTITY.GET_ENTITY_SPEED(veh):0.0}, " +
                    $"driver group {PED.GET_PED_RELATIONSHIP_GROUP_HASH(driver):X8}, mission {ENTITY.IS_ENTITY_A_MISSION_ENTITY(veh)}");
            }
        }

        static readonly System.Collections.Generic.Dictionary<int, long> LastLog = new System.Collections.Generic.Dictionary<int, long>();
    }
}
