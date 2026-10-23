// Which train configuration a real train was built from. The game can't say,
// but it can list the carriage models of a configuration, so the owner's game
// compares a train's carriages with the known configurations. The list comes
// from community research (Nexus mod 10469 "Ambient Train Spawner" and a
// forum post); unknown trains are logged with their carriages so the list can
// grow.
using System.Collections.Generic;
using System.Linq;
using RDR2.Native;

namespace RDR2Coop.Mod
{
    static class TrainConfigs
    {
        static readonly uint[] Known =
        {
            0x31656D23,   // bounty train
            0x57C209C4,   // mixed with armoured car
            0x515E31ED,   // standard passenger
            0x98427740,   // fast passenger
            0x998A0CBC,   // standard private
            0x0660E567,   // very crowded passenger
            0x41436136,   // long passenger/freight
            0x3ADC4DA9,   // sleeper private
            0x25E5D8FF,   // heavy money train
            0xCD2C7CA1,   // winter private passenger
            MISC.GET_HASH_KEY("bountyhunter_config"),
            MISC.GET_HASH_KEY("gunslinger3_config"),
        };

        public static uint Default => 0x515E31ED;

        static Dictionary<uint, List<uint>> _models;
        static readonly Dictionary<int, uint> _byTrain = new Dictionary<int, uint>();
        static readonly HashSet<string> _logged = new HashSet<string>();

        static Dictionary<uint, List<uint>> Models()
        {
            if (_models != null) return _models;
            _models = new Dictionary<uint, List<uint>>();
            foreach (uint c in Known)
            {
                var list = new List<uint>();
                for (int i = 0; i < 64; i++)
                {
                    uint m = VEHICLE._GET_TRAIN_MODEL_FROM_TRAIN_CONFIG_BY_CAR_INDEX(c, i);
                    if (m == 0) break;
                    list.Add(m);
                }
                if (list.Count > 0) _models[c] = list;
            }
            CoopScript.Log($"train configs: {_models.Count} of {Known.Length} known ones answered");
            return _models;
        }

        // The engine's carriage models (with and without the engine itself,
        // since which way the game counts isn't known) against every config.
        public static uint For(int engine, List<uint> carriages)
        {
            if (_byTrain.TryGetValue(engine, out var known)) return known;
            uint engineModel = RDR2.Native.ENTITY.GET_ENTITY_MODEL(engine);
            var withEngine = new List<uint> { engineModel };
            withEngine.AddRange(carriages);

            uint best = 0; int bestScore = 0;
            foreach (var kv in Models())
            {
                foreach (var seq in new[] { carriages, withEngine })
                {
                    int score = kv.Value.SequenceEqual(seq) ? 1000 : CommonPrefix(kv.Value, seq);
                    if (score > bestScore) { bestScore = score; best = kv.Key; }
                }
            }
            if (bestScore < 1000)
            {
                string key = string.Join(",", withEngine.Select(m => m.ToString("X8")));
                if (_logged.Add(key))
                    CoopScript.Log($"train {engine}: no exact config for [{key}]; " +
                                   (best != 0 ? $"closest {best:X8} ({bestScore} cars alike)" : "nothing close"));
                if (bestScore < 2) best = 0;   // too different: don't fake it
            }
            _byTrain[engine] = best;
            return best;
        }

        public static void Forget(int engine) => _byTrain.Remove(engine);

        // A mission train is only built once every car's model is loaded.
        // False if the config lists no cars or a model won't load in time.
        public static bool LoadCars(uint config)
        {
            var cars = new List<uint>();
            for (int i = 0; i < 64; i++)
            {
                uint m = VEHICLE._GET_TRAIN_MODEL_FROM_TRAIN_CONFIG_BY_CAR_INDEX(config, i);
                if (m == 0) break;
                cars.Add(m);
            }
            if (cars.Count == 0) { CoopScript.Log($"train config {config:X8}: the game lists no cars for it"); return false; }
            // Train cars stream slowly and don't all answer the usual "is it in
            // the game files" check, so: request them all, then wait up to ~3 s.
            var distinct = cars.Distinct().ToList();
            foreach (uint m in distinct) STREAMING.REQUEST_MODEL(m, false);
            for (int i = 0; i < 180 && !distinct.All(STREAMING.HAS_MODEL_LOADED); i++) RDR2.Script.Yield();
            var missing = distinct.Where(m => !STREAMING.HAS_MODEL_LOADED(m)).ToList();
            if (missing.Count > 0)
            {
                CoopScript.Log($"train config {config:X8}: {missing.Count} of {distinct.Count} car models not loaded yet ({string.Join(",", missing.Select(m => m.ToString("X8")))})");
                return false;
            }
            return true;
        }

        // Builds a mission train (models loaded first); 0 on failure.
        public static int Create(uint config, RDR2.Math.Vector3 at, bool direction, bool passengers)
        {
            if (!LoadCars(config)) return 0;
            int train = VEHICLE._CREATE_MISSION_TRAIN(config, at, direction, passengers, true, false);
            if (train == 0) CoopScript.Log($"train config {config:X8}: _CREATE_MISSION_TRAIN returned nothing at {at}");
            return train;
        }

        static int CommonPrefix(List<uint> a, List<uint> b)
        {
            int n = 0;
            while (n < a.Count && n < b.Count && a[n] == b[n]) n++;
            return n;
        }
    }
}
