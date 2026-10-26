// FakePlayer's "emotes" mode (0.9.0 emote menu): an emote from the list every 6 s, in turn, printed as it
// goes. One round of the list it stands 3 m from you, facing you; the next it walks its circle round you
// (the stand-in should keep walking and gesture on top), and so on. Your own emotes print as they arrive.
using System;
using RDR2Coop.Net;

static class EmoteMode
{
    const int EveryMs = 6000;
    static int _sent = -1, _round = -1;
    static Vec3 _at;

    public static void Tick(ServerLink link, ref PlayerState s, Vec3 you, long now)
    {
        int n = (int)(now / EveryMs), round = n / EmoteList.All.Length;
        if (round != _round) { _round = round; _at = new Vec3(you.X + 3, you.Y, you.Z); }   // stays put while you move
        bool stand = round % 2 == 0;
        if (stand)
        {
            s.Position = _at;
            s.Velocity = new Vec3(0, 0, 0);
            s.Heading = 90;   // facing you
        }
        if (n == _sent) return;
        _sent = n;
        int i = n % EmoteList.All.Length;
        link.SendNotice(Protocol.Broadcast, NoticeKind.Emote, EmoteList.Line(i));
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} emote {i}: {EmoteList.All[i].Label} ({(stand ? "standing by you" : "walking round you")})");
    }
}
