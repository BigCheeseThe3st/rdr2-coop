// Trains people drive (0.8.0, [Gameplay] Trains). The game that runs an area
// sends its trains' positions and speeds and the copies keep that speed
// (MirrorWorld), so a train its own player drives is followed already. When a
// partner takes the driving seat of their copy of one of our trains, our real
// one keeps their speed the same way, and everyone else's copies follow it:
// like a wagon they drive. A train only goes along its track, so it's steered
// by speed alone, never pushed like a wagon (VehicleSteer).
using System;
using System.Collections.Generic;
using RDR2.Math;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    static class TrainDrive
    {
        const float MaxSpeed = 30f;   // the game's top train speed: numbers from the network never go past it

        public static bool IsTrain(int vehicle) => VEHICLE.IS_THIS_MODEL_A_TRAIN(ENTITY.GET_ENTITY_MODEL(vehicle));

        // `speed` along the train's own direction (signed), a little more or
        // less to close a gap to `target` along the track.
        public static void Follow(int train, Vector3 target, float speed)
        {
            float ahead = Vector3.Dot(target - Peds.Position(train), ENTITY.GET_ENTITY_FORWARD_VECTOR(train));
            float v = Clamp(speed + Clamp(ahead * 0.3f, 4f), MaxSpeed);
            VEHICLE.SET_TRAIN_SPEED(train, v);
            VEHICLE.SET_TRAIN_CRUISE_SPEED(train, v);
        }

        static readonly HashSet<int> _logged = new HashSet<int>();

        // A partner's stand-in sits in the driving seat of one of our real
        // trains: it goes where and as fast as their copy goes. Not while our
        // story mission runs: its trains do what the mission says.
        public static void FollowDriver(int train, PlayerState s)
        {
            if (MissionProbe.StoryMission) return;
            if (_logged.Count > 64) _logged.Clear();
            if (_logged.Add(train)) CoopScript.Log($"train {train}: a partner drives it");
            var velocity = CoopScript.ToGame(s.Velocity);
            Follow(train, CoopScript.ToGame(s.MountPosition), Vector3.Dot(velocity, ENTITY.GET_ENTITY_FORWARD_VECTOR(train)));
        }

        // Everyone out of a copy train before it goes: whether deleting a train
        // takes the people in it along isn't known, and a stand-in must stay.
        public static void EmptySeats(int train)
        {
            for (int seat = -1; seat < VEHICLE.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS(train); seat++)
            {
                int ped = VEHICLE.GET_PED_IN_VEHICLE_SEAT(train, seat);
                if (ped != 0) TASK.CLEAR_PED_TASKS_IMMEDIATELY(ped, false, true);
            }
        }

        static float Clamp(float v, float max) => Math.Max(-max, Math.Min(max, v));
    }
}
