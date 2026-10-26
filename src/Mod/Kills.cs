// Who killed what (0.9.0, [Convenience] Scoreboard or [Games] Hunting): the kills for the scoreboard and the hunting contest.
// Only the game that runs an area (its owner) sees its NPCs die for real, so it alone credits kills there:
// every 500 ms it looks at the dead peds within 150 m (each handle once) and, by their source of death,
// counts its own (Stats kills.human / kills.animal) and sends a partner theirs (their stand-in was the
// inflictor: OwnedWorld.ApplyHit passes it) as a Score notice "kill <human|animal> <model>". A game takes
// such credits only from the owner of the area it is in (Score.MayCredit): anywhere else the peds around
// us are copies, whose deaths mean nothing. Kills are counted as the game attributes them, in missions
// too (there a partner's hits on hostile enemies are made by the host's hand, so they count for the host).
// API for other features (the hunting contest):
//   Kills.Killed (player, human, model): raised on this game for every kill it credits: our own
//     (player == our id; from our own scan, or from the owner's notice) and partners' kills in our area.
//   Stats.Get("kills.human") / ("kills.animal"): our own tally.
// It draws nothing and creates nothing in the world.
using System.Collections.Generic;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    static class Kills
    {
        public delegate void KillHandler(byte player, bool human, uint model);
        public static event KillHandler Killed;

        const int ScanMs = 500;
        const float Range = 150f;
        // Dead peds already looked at; kept while they're still around (the game reuses handles once they're gone).
        static readonly HashSet<int> _judged = new HashSet<int>();
        static long _scanMs;

        // Every tick; works every 500 ms while our game runs the area.
        public static void Tick(ServerLink link, bool iOwn, int me, IDictionary<byte, Puppet> puppets, long now)
        {
            if (!(Extras.Scoreboard || Games.Hunting) || !iOwn || now - _scanMs < ScanMs) return;
            _scanMs = now;
            var around = EntityScan.Peds(new[] { Peds.Position(me) }, Range);
            Dictionary<byte, List<string>> theirs = null;
            foreach (int ped in around)
            {
                if (ped == me || _judged.Contains(ped) || !ENTITY.IS_ENTITY_DEAD(ped)) continue;
                _judged.Add(ped);
                int source = PED.GET_PED_SOURCE_OF_DEATH(ped);
                byte killer = source == me ? link.MyId : PlayerOf(source, puppets);
                if (killer == 0) continue;
                bool human = PED.IS_PED_HUMAN(ped);
                uint model = ENTITY.GET_ENTITY_MODEL(ped);
                if (killer == link.MyId) Stats.Add(human ? "kills.human" : "kills.animal");
                else
                {
                    theirs = theirs ?? new Dictionary<byte, List<string>>();
                    if (!theirs.TryGetValue(killer, out var lines)) theirs[killer] = lines = new List<string>();
                    if (lines.Count < Score.MaxKills) lines.Add(Score.KillLine(human, model));
                }
                Killed?.Invoke(killer, human, model);
            }
            _judged.IntersectWith(around);
            if (theirs != null) foreach (var kv in theirs) link.SendNotice(kv.Key, NoticeKind.Score, string.Join("\n", kv.Value));
        }

        // A "kill" notice for us: taken only from the owner of the area we're in.
        public static void OnCredit(byte from, string text, ServerLink link, byte owner)
        {
            if (!Score.MayCredit(from, owner, link.MyId)) return;
            foreach (var k in Score.ParseKills(text))
            {
                Stats.Add(k.human ? "kills.human" : "kills.animal");
                Killed?.Invoke(link.MyId, k.human, k.model);
            }
        }

        public static void Clear() => _judged.Clear();

        static byte PlayerOf(int handle, IDictionary<byte, Puppet> puppets)
        {
            if (handle != 0) foreach (var kv in puppets) if (kv.Value.Handle == handle) return kv.Key;
            return 0;
        }
    }
}
