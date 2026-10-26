// Giving money to a partner (0.9.0, [Convenience] GiveMoney; the game side is src/Mod/MoneyGifts.cs). The
// giver's cash is taken first, then a Money notice goes to that player alone (again every 2 s until answered: a
// notice is one UDP packet); their game adds it and answers, and a gift nobody answers within 10 s goes back to the
// giver. Money notices, first token the verb:
//   give <cents> <nonce>   giver -> receiver: 1 cent to $10,000; the nonce (1-16 letters and digits) names the gift
//   ack <nonce>            receiver -> giver: taken (sent again for every repeat of a gift already taken: a lost
//                          answer would otherwise give the money back while the receiver keeps it too)
// A receiver takes a nonce from a sender once and at most 5 gifts a minute from one sender; anything else goes
// unanswered, so its giver gets it back. More than 5 Money notices a second from one player: the rest are dropped.
// Pure, so the formats and the nonce and answer rules are tested without the game.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RDR2Coop.Net
{
    public sealed class GiftLedger
    {
        public const int MaxCents = 10_000_00;   // as Money.MaxCents: one move, at most
        public const int AckMs = 10000, ResendMs = 2000, PerMinute = 5, PerSecond = 5;
        const int SeenMs = 600000;               // how long a taken nonce is remembered (a repeat is a duplicate or a replay)

        sealed class Out { public byte To; public int Cents; public long Ms, SentMs; public string Text; }

        readonly Dictionary<string, Out> _out = new Dictionary<string, Out>(StringComparer.Ordinal);    // ours, waiting for an answer
        readonly Dictionary<string, long> _seen = new Dictionary<string, long>(StringComparer.Ordinal); // "sender nonce" -> when taken
        readonly Dictionary<byte, List<long>> _taken = new Dictionary<byte, List<long>>();             // gifts taken per sender
        readonly Dictionary<byte, (long since, int n)> _rate = new Dictionary<byte, (long, int)>();      // notices per sender this second
        readonly Random _rng;

        public GiftLedger(Random rng = null) { _rng = rng ?? new Random(); }

        public int Waiting => _out.Count;

        /// <summary>The notice for a gift to `to` (their cash already taken from ours), or null when it isn't an amount.</summary>
        public string Give(byte to, int cents, long now)
        {
            if (cents <= 0 || cents > MaxCents) return null;
            string nonce;
            do nonce = _rng.Next(1, int.MaxValue).ToString("x", CultureInfo.InvariantCulture); while (_out.ContainsKey(nonce));
            string text = "give " + cents.ToString(CultureInfo.InvariantCulture) + " " + nonce;
            _out[nonce] = new Out { To = to, Cents = cents, Ms = now, SentMs = now, Text = text };
            return text;
        }

        /// <summary>Our gifts still waiting for an answer whose last send was 2 s ago or more: send them again.</summary>
        public List<(byte to, string text)> Resend(long now)
        {
            var again = new List<(byte, string)>();
            foreach (var o in _out.Values.Where(o => now - o.SentMs >= ResendMs && now - o.Ms < AckMs)) { o.SentMs = now; again.Add((o.To, o.Text)); }
            return again;
        }

        /// <summary>A Money notice from a player in the roster. A gift: take(cents) adds it to our cash (false: it isn't
        /// taken now, and a repeat may be); pay > 0 is what was taken. Whenever reply isn't null it goes back to them
        /// alone: a new gift taken, or a repeat of one taken before. An answer: settled > 0 is the gift of ours they
        /// took. Anything else: all zero, no reply (they get it back). take null: always taken (the tests, FakePlayer).</summary>
        public (int pay, string reply, int settled) Hear(byte from, string text, long now, Func<int, bool> take = null)
        {
            (long since, int n) r = _rate.TryGetValue(from, out var x) && now - x.since < 1000 ? x : (now, 0);
            _rate[from] = (r.since, r.n + 1);
            if (r.n >= PerSecond) return (0, null, 0);
            var f = (text ?? "").Split(' ');
            if (f.Length >= 2 && f[0] == "ack")
            {
                if (!_out.TryGetValue(f[1], out var o) || o.To != from) return (0, null, 0);   // not ours, not theirs, or already back with us
                _out.Remove(f[1]);
                return (0, null, o.Cents);
            }
            if (f.Length < 3 || f[0] != "give" || !Nonce(f[2]) || !int.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out int cents)
                || cents <= 0 || cents > MaxCents) return (0, null, 0);
            foreach (var old in _seen.Where(kv => now - kv.Value >= SeenMs).Select(kv => kv.Key).ToList()) _seen.Remove(old);
            string key = from + " " + f[2];
            if (_seen.ContainsKey(key)) return (0, "ack " + f[2], 0);   // taken already: our answer got lost
            if (!_taken.TryGetValue(from, out var times)) _taken[from] = times = new List<long>();
            times.RemoveAll(t => now - t >= 60000);
            if (times.Count >= PerMinute || take != null && !take(cents)) return (0, null, 0);
            times.Add(now);
            _seen[key] = now;
            return (cents, "ack " + f[2], 0);
        }

        /// <summary>Our gifts nobody answered within 10 s: they go back to us (a late answer is then ignored).</summary>
        public List<(byte to, int cents)> Overdue(long now) => TakeOut(o => now - o.Ms >= AckMs);

        /// <summary>The session is over: every gift still waiting goes back to us, and what we heard is forgotten.</summary>
        public List<(byte to, int cents)> Clear()
        {
            _seen.Clear();
            _taken.Clear();
            _rate.Clear();
            return TakeOut(o => true);
        }

        List<(byte to, int cents)> TakeOut(Func<Out, bool> which)
        {
            var back = new List<(byte, int)>();
            if (_out.Count == 0) return back;
            foreach (var kv in _out.Where(kv => which(kv.Value)).ToList()) { _out.Remove(kv.Key); back.Add((kv.Value.To, kv.Value.Cents)); }
            return back;
        }

        static bool Nonce(string s) => s.Length >= 1 && s.Length <= 16 && s.All(c => c < 128 && char.IsLetterOrDigit(c));
    }
}
