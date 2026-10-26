// Crimes count for the player who did them (0.8.0, switch [Gameplay] Law).
// A partner's shot at one of our NPCs is applied by our game (OwnedWorld.ApplyHit)
// with their stand-in as the culprit. The game books crimes against players only, so
// on its own it charges nobody, or wrongly us: for a moment after such a hit we keep
// those crimes off our own player, unless we fired or punched ourselves. A witnessed
// murder or attack on a lawman is sent to the partner (NoticeKind.Law) and their own
// game charges them with it (LAW::_REPORT_CRIME, as Rockstar's scripts report the
// player's crimes), so the wanted level and bounty are theirs, in that region; the
// lawmen here who saw it go after their stand-in, so their copies shoot at them.
// Story missions run their own law: nothing happens in them.
using System.Collections.Generic;
using System.Linq;
using RDR2;
using RDR2.Native;
using RDR2.UI;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class LawSync
    {
        sealed class Victim
        {
            public byte By;              // the partner whose hit landed last
            public bool Human, Lawman, Provoked, Settled;
            public long LastHitMs;
        }

        const int HeldMs = 1500;         // a hit's crime registers within a frame or two (the murder as the NPC dies)
        const int SettleMs = 2500;       // still alive this long after the last hit: wounded (an attack, if a lawman)
        const int KeepMs = 30000;        // dying of their wounds this long after still counts as theirs
        const float WitnessRange = 40f;
        const float NoticeRange = 300f;  // a crime said to be somewhere we aren't is a bad notice

        readonly Dictionary<int, Victim> _victims = new Dictionary<int, Victim>();
        readonly LawGate _gate = new LawGate();
        IDictionary<byte, Puppet> _followers = new Dictionary<byte, Puppet>();
        long _lastGuestHitMs = -60000, _lastOwnMs = -60000, _lastCheckMs, _lastRefusedMs = -60000, _lastHeldLogMs = -60000;
        bool _holding;
        int _heldSeen;

        // Owner side, just before a partner's hit is applied: who it was and whether the NPC already fought us.
        public void BeforeGuestHit(byte by, int ped, long now)
        {
            if (!Sync.Law || MissionProbe.StoryMission || !Peds.Exists(ped) || ENTITY.IS_ENTITY_DEAD(ped)) return;
            _lastGuestHitMs = now;
            if (_victims.TryGetValue(ped, out var v)) { v.By = by; v.LastHitMs = now; return; }
            if (_victims.Count >= 32) return;   // a massacre: the first 32 are plenty
            _victims[ped] = new Victim
            {
                By = by, LastHitMs = now, Human = PED.IS_PED_HUMAN(ped), Lawman = IsLawman(ped),
                Provoked = !OwnedWorld.IsHostile(ped, PLAYER.PLAYER_PED_ID(), _followers.Values),
            };
        }

        // Owner side, every frame while our game runs the area.
        public void OwnerTick(ServerLink link, IDictionary<byte, Puppet> followers, long now)
        {
            _followers = followers;
            if (!Sync.Law || followers.Count == 0) return;
            int me = PLAYER.PLAYER_PED_ID();
            if (PED.IS_PED_SHOOTING(me) || PED.IS_PED_IN_MELEE_COMBAT(me)) _lastOwnMs = now;
            // Our own shot or punch in the same moment: whatever it did is ours to answer for.
            bool hold = now - _lastGuestHitMs < HeldMs && now - _lastOwnMs > HeldMs;
            if (hold)
                foreach (uint crime in LawRules.FromHits) LAW.SUPPRESS_CRIME_THIS_FRAME(PLAYER.PLAYER_ID(), crime, 0, 0, -1);   // each frame, as the game wants it
            else if (_holding) LogHeld(now);
            _holding = hold;

            if (now - _lastCheckMs < 200 || _victims.Count == 0) return;
            _lastCheckMs = now;
            foreach (var kv in _victims.ToList())
            {
                int ped = kv.Key;
                var v = kv.Value;
                long since = now - v.LastHitMs;
                bool gone = !Peds.Exists(ped), dead = !gone && ENTITY.IS_ENTITY_DEAD(ped);
                if (gone || dead || since > KeepMs) _victims.Remove(ped);
                if (gone || since > KeepMs) continue;
                if (dead) Resolve(link, ped, v, true, followers);
                else if (!v.Settled && since >= SettleMs) { v.Settled = true; Resolve(link, ped, v, false, followers); }
            }
        }

        void Resolve(ServerLink link, int ped, Victim v, bool dead, IDictionary<byte, Puppet> followers)
        {
            uint crime = LawRules.For(v.Human, v.Lawman, dead, v.Provoked, MissionProbe.StoryMission);
            if (crime == 0 || !followers.TryGetValue(v.By, out var culprit) || !culprit.Exists) return;   // no crime even if seen, or they've left
            string who = Plain(link.NameOf(v.By));
            uint model = ENTITY.GET_ENTITY_MODEL(ped);
            int source = dead ? PED.GET_PED_SOURCE_OF_DEATH(ped) : 0;
            if (source != culprit.Handle && source != 0)
            {
                CoopScript.Log($"law: {model:X8} died by {source}, not {who}'s stand-in {culprit.Handle}: not theirs");
                return;
            }
            var lawmen = new List<int>();
            if (v.Lawman && !dead) lawmen.Add(ped);   // he saw it himself
            if (!Witnesses(ped, culprit, followers.Values, lawmen) && lawmen.Count == 0)
            {
                CoopScript.Log($"law: {who} {(dead ? "killed" : "wounded")} {model:X8} and nobody saw it: no crime");
                return;
            }
            int sent = 0;
            foreach (int cop in lawmen)
                if (!PED.IS_PED_IN_COMBAT(cop, culprit.Handle)) { TASK.TASK_COMBAT_PED(cop, culprit.Handle, 0, 16); sent++; }
            link.SendNotice(v.By, NoticeKind.Law, LawNotice.Format(crime, CoopScript.ToNet(Peds.Position(ped))));
            CoopScript.Log($"law: {who} was seen {LawRules.Name(crime)} ({model:X8}): told their game; {lawmen.Count} lawmen saw it, {sent} newly after them");
            Say($"Co-op: {who} was seen {LawRules.Name(crime)}" + (lawmen.Count > 0 ? ": the law is after them" : ""));
        }

        // Anyone near the culprit or the victim (not us, a stand-in or an outlaw) with a
        // clear view of the culprit; the lawmen among them go in `lawmen`.
        static bool Witnesses(int victim, Puppet culprit, IEnumerable<Puppet> standIns, List<int> lawmen)
        {
            int me = PLAYER.PLAYER_PED_ID();
            var points = new[] { culprit.Position, Peds.Position(victim) };
            var skip = new HashSet<int> { victim, me };
            foreach (var p in standIns) { skip.Add(p.Handle); skip.Add(p.HorseHandle); }
            bool any = false;
            int looks = 0;
            foreach (int h in EntityScan.Peds(points, WitnessRange))
            {
                if (skip.Contains(h) || !Peds.Exists(h)) continue;
                var at = Peds.Position(h);
                if (at.DistanceTo(points[0]) > WitnessRange && at.DistanceTo(points[1]) > WitnessRange) continue;
                if (ENTITY.IS_ENTITY_DEAD(h) || !PED.IS_PED_HUMAN(h)) continue;
                bool lawman = IsLawman(h);
                if (!lawman && PED.GET_RELATIONSHIP_BETWEEN_PEDS(h, me) == 5) continue;   // outlaws who hate us (5) don't report crimes
                if (++looks > 24) break;   // enough sight checks for one crime
                if (!ENTITY.HAS_ENTITY_CLEAR_LOS_TO_ENTITY(h, culprit.Handle, 17)) continue;
                any = true;
                if (lawman) lawmen.Add(h);
            }
            return any;
        }

        // The game counts the crimes it held back: the log then shows whether a partner's
        // hits would have been charged to us.
        void LogHeld(long now)
        {
            if (now - _lastHeldLogMs < 10000) return;   // a long gun fight: one line per 10 s at most
            int player = PLAYER.PLAYER_ID(), held = 0;
            foreach (uint crime in LawRules.FromHits) held += LAW.NUM_CRIMES_SUPPRESSED(player, crime);
            if (held > _heldSeen)
            {
                _lastHeldLogMs = now;
                CoopScript.Log($"law: the game held back {held - _heldSeen} crime(s) a partner's hit would have charged to you");
            }
            _heldSeen = held;
        }

        // Guest side: the game that runs our area saw us commit a crime there.
        public void OnNotice(byte from, string text, ServerLink link, byte owner, long now)
        {
            if (!Sync.Law) return;
            var me = Game.Player.Character;
            bool known = LawNotice.TryParse(text, out uint crime, out var at);
            string why = from != owner || owner == link.MyId ? "not from the game running our area"
                : !known ? "not a crime we know"
                : me == null || !me.Exists() || me.IsDead ? "we're dead"
                : MissionProbe.StoryMission ? "our own story mission runs its law"
                : me.Position.DistanceTo(CoopScript.ToGame(at)) > NoticeRange ? "it's somewhere we aren't"
                : !_gate.Allow(now) ? "too many this minute" : null;
            string who = Plain(link.NameOf(from));
            if (why != null)
            {
                if (now - _lastRefusedMs > 10000) { _lastRefusedMs = now; CoopScript.Log($"law: ignored a crime from {who} ({why})"); }
                return;
            }
            int bounty = LAW.GET_BOUNTY(PLAYER.PLAYER_ID());
            LAW._REPORT_CRIME(PLAYER.PLAYER_ID(), crime, 0, 0, true);   // as Rockstar's scripts do: the game's own bounty, where we are
            CoopScript.Log($"law: {who}'s world saw us {LawRules.Name(crime)}: reported to our game (bounty before {bounty})");
            Say($"Co-op: you were seen {LawRules.Name(crime)} in {who}'s world: the bounty is yours");
        }

        public void Clear()
        {
            _victims.Clear();
            _followers = new Dictionary<byte, Puppet>();
        }

        static bool IsLawman(int ped)
        {
            uint group = PED.GET_PED_RELATIONSHIP_GROUP_DEFAULT_HASH(ped);
            return group == LawRules.RelCop || group == LawRules.RelGuamaLaw;
        }

        static string Plain(string s) => new string((s ?? "").Where(c => c != '~' && c != '<' && c != '>').ToArray());   // names come from strangers

        static void Say(string text) { if (!TableGames.Hold(text)) Screen.DisplaySubtitle(text); }   // waits while we play at a card table
    }
}
