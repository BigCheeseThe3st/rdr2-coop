// Bubbles' crowd rule (src/Net/Bubbles.cs): one crowd where two players' worlds overlap, and one set of trains.
// Near a linked player who outranks us (the host, then the lowest id), our game stops spawning ambient people,
// animals and traffic there and retires its own off-screen ambient people (never one on screen, fighting, fleeing,
// tied, dead, a card player, or within 50 m of anyone); their game's people show here as copies instead. How spawning
// stops: by default the whole-map density zero the follower always used, while that player is within 200 m;
// [Testing] BubbleVolumes=Yes instead puts the game's own spawn/avoid/scenario block (as missions use: abigail2_1_intro)
// on a sphere round that player, moved with them, so our own spawning goes on elsewhere (untested in game: hence the
// switch). Trains: while any linked player outranks us, our game runs no random trains and removes its own
// off-screen, empty ones; theirs show as copies.
using System;
using System.Collections.Generic;
using System.Linq;
using RDR2;
using RDR2.Math;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class BubbleCrowd
    {
        public static bool Volumes;   // [Testing] BubbleVolumes
        const float VolumeRadius = 120f;
        readonly Dictionary<byte, (int volume, ulong block)> _volumes = new Dictionary<byte, (int, ulong)>();
        bool _trainsOff;
        readonly HashSet<int> _stuckTrains = new HashSet<int>();
        long _lastRetireMs, _lastTrainMs, _lastMoveMs, _retireLogMs;

        public void Tick(byte myId, Vec3 myPos, byte host, IDictionary<byte, OtherPlayer> linked, ICollection<int> keep, IList<Vec3> players, long now)
        {
            var higher = linked.Where(kv => !kv.Value.Away && Bubbles.Outranks(kv.Key, myId, host)).ToDictionary(kv => kv.Key, kv => kv.Value);

            // Trains: the highest linked game runs them.
            if (higher.Count > 0)
            {
                if (!_trainsOff) { VEHICLE.SET_RANDOM_TRAINS(false); _trainsOff = true; CoopScript.Log("bubbles: our random trains off (a linked game outranks us; its trains show here)"); }
                if (now - _lastTrainMs > 5000) { _lastTrainMs = now; RetireTrains(keep, players); }
            }
            else if (_trainsOff) { VEHICLE.SET_RANDOM_TRAINS(true); _trainsOff = false; CoopScript.Log("bubbles: our random trains back on"); }

            // Spawning.
            if (Volumes) VolumesTick(higher, now);
            else if (Bubbles.BlockSpawning(myId, myPos, host, linked)) MirrorWorld.SuppressAmbient();   // every frame
            if (higher.Count == 0 || now - _lastRetireMs < 1000) return;
            _lastRetireMs = now;
            var centres = higher.Values.Where(o => Vec3.Distance(o.Position, myPos) <= Bubbles.SpawnBlock + VolumeRadius).Select(o => o.Position).ToList();
            if (centres.Count == 0) return;
            int retired = 0;
            var me = Game.Player.Character;
            foreach (int h in EntityScan.Peds(new[] { me.Position }, Bubbles.SpawnBlock))
            {
                if (keep.Contains(h) || OwnedWorld.Skip.Contains(h) || MirrorWorld.IsCopyGroup(h) || Peds.Ours.Contains(h) || !Peds.Exists(h)) continue;
                if (PED.IS_PED_A_PLAYER(h) || ENTITY.IS_ENTITY_A_MISSION_ENTITY(h) || ENTITY.IS_ENTITY_ON_SCREEN(h) || ENTITY.IS_ENTITY_DEAD(h)) continue;
                if (PED.IS_PED_IN_COMBAT(h, 0) || PED.IS_PED_FLEEING(h) || PED.IS_PED_HOGTIED(h) || PED.IS_PED_LASSOED(h) || TableGames.CardPlayer(h)) continue;
                if (PED._GET_VEHICLE_DRAFT_HORSE_IS_ATTACHED_TO(h) != 0 || MissionRoles.Known(h) || Cutscenes.HostParts.Contains(h)) continue;
                var at = CoopScript.ToNet(Peds.Position(h));
                if (players.Any(p => Vec3.Distance(p, at) < 50f)) continue;
                if (!centres.Any(c => Vec3.Distance(c, at) <= (Volumes ? VolumeRadius : Bubbles.SpawnBlock))) continue;   // only where their crowd stands in
                Peds.Delete(h);
                retired++;
            }
            if (retired > 0 && now - _retireLogMs > 30000) { _retireLogMs = now; CoopScript.Log($"bubbles: retired {retired} of our own ambient people off screen near a higher-priority player"); }
        }

        void VolumesTick(Dictionary<byte, OtherPlayer> higher, long now)
        {
            foreach (var id in _volumes.Keys.Where(id => !higher.ContainsKey(id)).ToList()) RemoveVolume(id);
            bool move = now - _lastMoveMs >= 500;
            if (move) _lastMoveMs = now;
            foreach (var kv in higher)
            {
                var at = CoopScript.ToGame(kv.Value.Position);
                if (!_volumes.TryGetValue(kv.Key, out var v) || !VOLUME.DOES_VOLUME_EXIST(v.volume))
                {
                    RemoveVolume(kv.Key);   // what's left of a volume the game dropped
                    int vol = VOLUME.CREATE_VOLUME_SPHERE(at, new Vector3(0f, 0f, 0f), new Vector3(2 * VolumeRadius, 2 * VolumeRadius, 2 * VolumeRadius));
                    if (vol == 0) { CoopScript.Log("bubbles: couldn't make a spawn block volume: whole-map suppression instead"); foreach (var id in _volumes.Keys.ToList()) RemoveVolume(id); Volumes = false; return; }
                    POPULATION._ADD_AMBIENT_SPAWN_RESTRICTION(vol, 0, 0, 0, uint.MaxValue, uint.MaxValue, 0);
                    POPULATION._ADD_AMBIENT_AVOIDANCE_RESTRICTION(vol, 0, 0, 0, uint.MaxValue, uint.MaxValue, 2);
                    ulong block = PED._ADD_SCENARIO_BLOCKING_VOLUME(vol, false, 15);
                    _volumes[kv.Key] = (vol, block);
                    CoopScript.Log($"bubbles: spawn block round player {kv.Key} ({VolumeRadius:0} m)");
                }
                else if (move) VOLUME.SET_VOLUME_COORDS(v.volume, at);
            }
        }

        void RemoveVolume(byte id)
        {
            if (!_volumes.TryGetValue(id, out var v)) return;
            if (v.block != 0) PED.REMOVE_SCENARIO_BLOCKING_AREA(v.block, false);   // even when the volume itself is gone
            if (VOLUME.DOES_VOLUME_EXIST(v.volume))
            {
                POPULATION._REMOVE_AMBIENT_SPAWN_RESTRICTION(v.volume);
                POPULATION._REMOVE_AMBIENT_AVOIDANCE_RESTRICTION(v.volume);
                VOLUME.DELETE_VOLUME(v.volume);
            }
            _volumes.Remove(id);
        }

        // keep: our character, the stand-ins and their horses (whatever they ride stays); players: everyone's positions.
        unsafe void RetireTrains(ICollection<int> keep, IList<Vec3> players)
        {
            var me = Game.Player.Character;
            var ridden = new HashSet<int>(keep.Where(h => h != 0 && Peds.Exists(h)).Select(Peds.VehicleOf).Where(v => v != 0));
            int removed = 0;
            var trains = EntityScan.Vehicles(new[] { me.Position }, 600f).Where(v => Peds.Exists(v) && VEHICLE.IS_THIS_MODEL_A_TRAIN(ENTITY.GET_ENTITY_MODEL(v))).ToList();
            var carriages = new HashSet<int>();
            foreach (int v in trains)
                for (int i = 0, n = VEHICLE._GET_TRAIN_CARRIAGE_TRAILER_NUMBER(v); i < n; i++) { int c = VEHICLE.GET_TRAIN_CARRIAGE(v, i); if (c != 0 && c != v) carriages.Add(c); }
            foreach (int v in trains)   // engines only: deleting one removes its whole train
            {
                if (carriages.Contains(v) || OwnedWorld.Skip.Contains(v) || ENTITY.IS_ENTITY_A_MISSION_ENTITY(v) || ENTITY.IS_ENTITY_ON_SCREEN(v)) continue;
                int driver = VEHICLE.GET_PED_IN_VEHICLE_SEAT(v, -1);
                if (driver != 0 && PED.IS_PED_A_PLAYER(driver)) continue;
                var parts = new List<int> { v };
                for (int i = 0, n = VEHICLE._GET_TRAIN_CARRIAGE_TRAILER_NUMBER(v); i < n; i++) { int c = VEHICLE.GET_TRAIN_CARRIAGE(v, i); if (c != 0) parts.Add(c); }
                // Anyone riding it, or standing on or by it (a flatcar, the platform), keeps it.
                if (parts.Any(c => ridden.Contains(c) || ENTITY.IS_ENTITY_ON_SCREEN(c) || players.Any(p => Vec3.Distance(p, CoopScript.ToNet(Peds.Position(c))) < 60f))) continue;
                if (_stuckTrains.Contains(v)) continue;
                int h = v;
                ENTITY.SET_ENTITY_AS_MISSION_ENTITY(h, true, true);
                VEHICLE.DELETE_MISSION_TRAIN(&h);
                if (!Peds.Exists(v)) { removed++; continue; }
                _stuckTrains.Add(v);   // the game wouldn't let it go: not tried again
                CoopScript.Log($"bubbles: couldn't remove our train {v} (model {ENTITY.GET_ENTITY_MODEL(v):X8}, mission entity {ENTITY.IS_ENTITY_A_MISSION_ENTITY(v)}, {parts.Count - 1} carriages): left as it is");
            }
            _stuckTrains.RemoveWhere(t => !Peds.Exists(t));
            if (removed > 0) CoopScript.Log($"bubbles: removed {removed} of our own trains off screen (a linked game outranks us: its trains show here)");
        }

        /// <summary>Unlinked, bubbles off or the mod stopping: our spawning and trains back as they were.</summary>
        public void Clear()
        {
            foreach (var id in _volumes.Keys.ToList()) RemoveVolume(id);
            if (_trainsOff) { VEHICLE.SET_RANDOM_TRAINS(true); _trainsOff = false; }
        }
    }
}
