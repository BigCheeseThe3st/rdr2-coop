// How a player stands and moves, for their stand-in in the others' games (0.8.0, [Visuals] Stance):
// crouching, jumps, climbs and vaults, swimming, falls and being knocked over. We send our own ped's
// pose with our position; a partner's puppet crouches when they do, makes the move they make
// (StanceTracker decides when) and swims after them instead of drowning. Nothing is created here,
// so there is nothing to clean up: the settings go with the puppet.
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class Stance
    {
        const PoseFlags Moves = PoseFlags.Jumping | PoseFlags.Climbing | PoseFlags.Falling | PoseFlags.Ragdoll;

        // ---- our own pose, sent with every state ----
        static long _readMs = -1000;
        static PoseFlags _pose;

        // Read every 100 ms: a jump lasts most of a second, so none is missed.
        public static PoseFlags Read(int ped, long now)
        {
            if (!Visuals.Stance) return PoseFlags.None;
            if (now - _readMs < 100) return _pose;
            _readMs = now;
            var f = PoseFlags.None;
            if (PED.GET_PED_CROUCH_MOVEMENT(ped)) f |= PoseFlags.Crouching;
            if (PED.IS_PED_JUMPING(ped)) f |= PoseFlags.Jumping;
            if (PED.IS_PED_CLIMBING(ped) || PED.IS_PED_VAULTING(ped) || PED._IS_PED_CLIMBING_LADDER(ped)) f |= PoseFlags.Climbing;
            if (PED.IS_PED_SWIMMING(ped)) f |= PoseFlags.Swimming;
            if (PED.IS_PED_FALLING(ped)) f |= PoseFlags.Falling;
            if (PED.IS_PED_RAGDOLL(ped)) f |= PoseFlags.Ragdoll;
            return _pose = f;
        }

        // ---- a partner's puppet ----
        readonly StanceTracker _moves = new StanceTracker();
        bool _ready, _crouched;
        long _crouchMs;

        // Every tick while the puppet is on foot. True while a move plays: steering then only snaps it.
        public bool Show(int ped, PlayerState s, long now)
        {
            if (!Visuals.Stance) return false;
            if (!_ready)
            {
                _ready = true;
                PED.SET_PED_CONFIG_FLAG(ped, 265, false);   // PCF_DrownsInWater: it swims after them
                PED.SET_PED_CONFIG_FLAG(ped, 266, false);   // PCF_DiesInstantlyWhenSwimming
            }
            // Crouched when they are; once a second, check it still is (a dismount or a teleport stands it up).
            bool crouch = (s.Pose & PoseFlags.Crouching) != 0;
            if (crouch != _crouched || now - _crouchMs > 1000)
            {
                if (crouch != _crouched || PED.GET_PED_CROUCH_MOVEMENT(ped) != crouch) PED._SET_PED_CROUCH_MOVEMENT(ped, crouch, 0, false);
                _crouched = crouch;
                _crouchMs = now;
            }
            var them = CoopScript.ToGame(s.Position);
            bool near = (s.Pose & Moves) != 0 && Peds.Position(ped).DistanceTo(them) < 3f;   // only read while they make a move
            var step = _moves.Update(s.Pose, near, now);
            if ((step & StanceStep.Jump) != 0) TASK.TASK_JUMP(ped, true);
            if ((step & StanceStep.Climb) != 0) TASK.TASK_CLIMB(ped, true);   // climbs or vaults the nearest thing: what they climbed
            if ((step & StanceStep.Ragdoll) != 0) PED.SET_PED_TO_RAGDOLL(ped, 1000, StanceTracker.RagdollMs, 0, false, false, null);
            // Still below the wall they climbed, or up where they jumped or fell from: put it beside them.
            if ((step & StanceStep.CheckLevel) != 0 && StanceTracker.OffLevel(CoopScript.ToNet(Peds.Position(ped)), s.Position))
                ENTITY._SET_ENTITY_COORDS_AND_HEADING(ped, them, s.Heading, false, false, false);
            return (step & StanceStep.Hold) != 0;
        }
    }
}
