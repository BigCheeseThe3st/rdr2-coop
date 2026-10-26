// Which train configuration a real train was built from. The game can't say,
// but it can list the carriage models of a configuration, so the owner's game
// compares a train's carriages with the known configurations. The list comes
// from community research (Nexus mod 10469 "Ambient Train Spawner" and a
// forum post); unknown trains are logged with their carriages so the list can
// grow. With [Gameplay] Trains (0.8.0) it holds all 85 of the game's own
// (trainconfigs.ymt, as alloc8or.re/rdr3/doc/enums/eTrainConfig.txt lists
// them), and a train matching none exactly is copied as the nearest one with
// the same engine (TrainMatch). There's no native to build a train from a list
// of cars, only from a configuration.
using System.Collections.Generic;
using System.Linq;
using RDR2.Native;
using RDR2Coop.Net;

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

        // The rest of trainconfigs.ymt: handcarts, trolleys, mine carts, the
        // ghost train and story trains (Leviticus Cornwall's is winter4).
        static readonly uint[] More =
        {
            0x8EAC625C /* appleseed */, 0x26509FBB /* dummy_engine */, 0x3260CE89 /* engine */, 0x0E62D710 /* ghost_train */,
            0x5AA369CA /* gunslinger4 */, 0x3EDA466D /* handcart */, 0x767DEB32 /* industry2 */, 0xC75AA08C /* minecart */,
            0xBF69518F /* trolley */, 0x487B2BE7 /* winter4 */,
            0x005E03AD, 0x0392C83A, 0x0941ADB7, 0x09B679D6, 0x0CCC2F70, 0x0D03C58D, 0x10461E19, 0x124A1F89,
            0x19A0A288, 0x1C043595, 0x1C9936BB, 0x1EEC5C2A, 0x1EF82A51, 0x29C81ACB, 0x2D1A6F0C, 0x2D3645FA,
            0x35D17C43, 0x4A73E49C, 0x4C9CCB22, 0x592A5CD0, 0x5D9928A4, 0x68CF495F, 0x6CC26E27, 0x6D69A954,
            0x73722125, 0x761CE0AD, 0x7BD58C4D, 0x8864D73A, 0x8D0766BC, 0x90CB53CA, 0x9296570E, 0x96563327,
            0x9897FF51, 0x9CBE6FEC, 0x9E096E46, 0xA3BF0BEB, 0xA8B1CEB7, 0xA91041A2, 0xAA3E691E, 0xAC18A9F4,
            0xAEE0ECF5, 0xC1F1DD80, 0xC732CDC8, 0xCA19C62A, 0xD233B18D, 0xD42DD3EE, 0xD5DF2D82, 0xD8CF6395,
            0xD92B16AE, 0xD93C36C2, 0xDA2EDE2F, 0xDC9DD041, 0xDD920DAF, 0xE0898B89, 0xE16CA3EF, 0xEB8B2439,
            0xEF9FC71D, 0xEFBFBDD8, 0xF19E48CA, 0xF6AA98F4, 0xFAB2FFB9, 0xFAC328F0, 0xFD8810E8,
        };

        public static uint Default => 0x515E31ED;

        static Dictionary<uint, List<uint>> _models;
        // By the cars seen, not the engine's handle: the game reuses handles.
        static readonly Dictionary<string, uint> _byCars = new Dictionary<string, uint>();

        static Dictionary<uint, List<uint>> Models()
        {
            if (_models != null) return _models;
            _models = new Dictionary<uint, List<uint>>();
            var configs = Sync.Trains ? Known.Concat(More) : Known;
            foreach (uint c in configs)
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
            CoopScript.Log($"train configs: {_models.Count} of {configs.Count()} known ones answered");
            return _models;
        }

        // The engine's carriage models (with and without the engine itself,
        // since which way the game counts isn't known) against every config.
        public static uint For(int engine, List<uint> carriages)
        {
            uint engineModel = RDR2.Native.ENTITY.GET_ENTITY_MODEL(engine);
            var withEngine = new List<uint> { engineModel };
            withEngine.AddRange(carriages);
            string key = string.Join(",", withEngine.Select(m => m.ToString("X8")));
            if (_byCars.TryGetValue(key, out var known)) return known;

            uint best = 0; int bestScore = 0;
            if (Sync.Trains) best = TrainMatch.Closest(Models(), new IList<uint>[] { carriages, withEngine }, out bestScore);
            else
                foreach (var kv in Models())
                {
                    foreach (var seq in new[] { carriages, withEngine })
                    {
                        int score = kv.Value.SequenceEqual(seq) ? 1000 : CommonPrefix(kv.Value, seq);
                        if (score > bestScore) { bestScore = score; best = kv.Key; }
                    }
                }
            if (bestScore < TrainMatch.Exact)
            {
                CoopScript.Log($"train {engine}: no exact config for [{key}]; " +
                               (best != 0 ? $"closest {best:X8} ({bestScore} cars alike)" : "nothing close"));
                if (!Sync.Trains && bestScore < 2) best = 0;   // too different: don't fake it
            }
            if (_byCars.Count > 256) _byCars.Clear();
            _byCars[key] = best;
            return best;
        }

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
