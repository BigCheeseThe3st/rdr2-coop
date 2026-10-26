// Finds the people and vehicles near some points. ScriptHookRDR2's pool
// lists (World.GetAllPeds/GetAllVehicles) fail at random and then return
// nothing for long stretches, so this asks the game itself: a sphere volume
// around each point and the entities inside it, merged with the pool list
// whenever that does work.
using System.Collections.Generic;
using RDR2;
using RDR2.Math;
using RDR2.Native;

namespace RDR2Coop.Mod
{
    static class EntityScan
    {
        // _GET_ENTITIES_IN_VOLUME entity types.
        public const int PedType = 1, VehicleType = 2;

        public static List<int> Peds(IEnumerable<Vector3> points, float radius) => Scan(points, radius, PedType);
        public static List<int> Vehicles(IEnumerable<Vector3> points, float radius) => Scan(points, radius, VehicleType);

        static List<int> Scan(IEnumerable<Vector3> points, float radius, int type)
        {
            var found = new HashSet<int>();
            foreach (var p in points) InVolume(p, radius, type, found);
            // Whatever the pool list adds (when it works) is welcome too.
            if (type == PedType) foreach (var x in World.GetAllPeds()) found.Add(x.Handle);
            else foreach (var x in World.GetAllVehicles()) found.Add(x.Handle);
            found.Remove(0);
            return new List<int>(found);
        }

        static void InVolume(Vector3 centre, float radius, int type, HashSet<int> into)
        {
            float d = radius * 2f;   // volume scale is the full size, not the radius
            int volume = VOLUME.CREATE_VOLUME_SPHERE(centre, new Vector3(0f, 0f, 0f), new Vector3(d, d, d));
            int set = ITEMSET.CREATE_ITEMSET(true);
            try
            {
                ENTITY._GET_ENTITIES_IN_VOLUME(volume, set, type);
                int n = ITEMSET.GET_ITEMSET_SIZE(set);
                for (int i = 0; i < n; i++) into.Add(ITEMSET.GET_INDEXED_ITEM_IN_ITEMSET(i, set));
            }
            finally
            {
                if (ITEMSET.IS_ITEMSET_VALID(set)) ITEMSET.DESTROY_ITEMSET(set);
                VOLUME.DELETE_VOLUME(volume);
            }
        }
    }
}
