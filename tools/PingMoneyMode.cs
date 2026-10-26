// FakePlayer's pings and money (0.9.0): `partner ping` pings a spot near you every 5 s (30 m out, a quarter turn
// round you each time: your map and the world show it for 12 s, with a subtitle and a sound); `partner money` gives
// you $1 every 10 s (a subtitle, and $1 more in your cash) and takes your gifts (co-op menu > Give money to a
// partner > FakePartner), answering each (a repeat again), like a real partner's game. Both print what your game sends. Compiled
// into FakePlayer.exe only (tools/*.cs); the game never sees this file.
using System;
using System.Diagnostics;
using RDR2Coop.Net;

static class PingMoneyMode
{
    static readonly GiftLedger Ledger = new GiftLedger();
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static bool _hooked;
    static long _lastMs = -100000;
    static int _n;

    public static void Tick(ServerLink link, string mode, byte you, Vec3? at, long now)
    {
        if (!_hooked)
        {
            _hooked = true;
            link.NoticeReceived += (from, n) =>
            {
                if (n.Kind != NoticeKind.Money) return;
                var (pay, reply, settled) = Ledger.Hear(from, n.Text, Clock.ElapsedMilliseconds);
                string stamp = DateTime.Now.ToString("HH:mm:ss");
                if (settled > 0) Console.WriteLine($"{stamp} money: you took my ${settled / 100.0:0.00}");
                if (reply != null) link.SendNotice(from, NoticeKind.Money, reply);
                if (pay <= 0) return;
                Console.WriteLine($"{stamp} money: you gave me ${pay / 100.0:0.00}; answered '{reply}'");
            };
        }
        foreach (var (to, text) in Ledger.Resend(Clock.ElapsedMilliseconds)) link.SendNotice(to, NoticeKind.Money, text);
        foreach (var g in Ledger.Overdue(Clock.ElapsedMilliseconds))
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} money: no answer to my ${g.cents / 100.0:0.00} in 10 s (your game would give it back)");
        if (you == 0 || at == null) return;
        if (mode == "ping" && now - _lastMs >= 5000)
        {
            _lastMs = now;
            double a = _n++ * Math.PI / 2;
            var spot = new Vec3(at.Value.X + 30f * (float)Math.Cos(a), at.Value.Y + 30f * (float)Math.Sin(a), at.Value.Z);
            link.SendNotice(Protocol.Broadcast, NoticeKind.Ping, PingMsg.Write(spot));
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} ping: {PingMsg.Write(spot)} (30 m " + new[] { "east", "north", "west", "south" }[(_n - 1) % 4] + " of you)");
        }
        if (mode == "money" && now - _lastMs >= 10000)
        {
            _lastMs = now;
            string give = Ledger.Give(you, 100, Clock.ElapsedMilliseconds);
            link.SendNotice(you, NoticeKind.Money, give);
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} money: gave you $1.00 ('{give}')");
        }
    }
}
