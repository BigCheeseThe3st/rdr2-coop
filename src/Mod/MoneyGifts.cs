// Giving a partner money (0.9.0, [Convenience] GiveMoney; the messages and their rules are src/Net/Gift.cs). The
// co-op menu's "Give money to a partner" picks who, then asks how many dollars (the game's keyboard): the cash is
// taken from ours at once (never more than we have, nor more than $10,000) and their game adds it and answers (ours
// sends it again every 2 s until then). No answer within 10 s, or the session ends first: it comes back to us. Both
// sides get a subtitle; the scoreboard's stats count money.given and money.received (cents). Nothing is given or
// taken in a story mission or cutscene or at one of the game's own tables: a gift that arrives then goes
// unanswered, so its giver gets it back. All cash moves go through Money. Draws nothing and creates nothing.
using System;
using System.Collections.Generic;
using System.Linq;
using RDR2Coop.Net;
using Screen = RDR2.UI.Screen;

namespace RDR2Coop.Mod
{
    sealed class MoneyGifts
    {
        readonly GiftLedger _ledger = new GiftLedger();
        readonly Dictionary<byte, string> _names = new Dictionary<byte, string>();   // who our waiting gifts went to, for the subtitles
        long _now;

        public string MenuLabel(ServerLink link) => Extras.GiveMoney && link != null && link.Roster.Count > 1 ? "Give money to a partner" : null;

        static bool Off => MissionProbe.StoryMission || MissionProbe.InMissionScene || Activities.Busy;

        public void MenuAct(ServerLink link)
        {
            if (!Extras.GiveMoney || link == null) return;
            if (Off) { Say("Co-op: no giving money in a story mission or while you play"); return; }
            var rows = link.Roster.Where(e => e.Id != link.MyId)
                                  .Select(e => (label: Plain(e.Name), act: (Action)(() => AskAmount(link, e.Id, Plain(e.Name))))).ToList();
            PickList.Open("Give money to", rows, null, "Your cash: " + Money.Dollars(Money.Cents));
        }

        void AskAmount(ServerLink link, byte to, string name) =>
            PickList.AskFor($"Give {name} how many dollars?", $"You have {Money.Dollars(Money.Cents)}; $10,000 at most. Type it, then Enter (Esc: none)",
                            true, 8, text => Give(link, to, name, text));

        // The keyboard's answer, on a later tick.
        void Give(ServerLink link, byte to, string name, string text)
        {
            if (text == null) return;   // cancelled
            int cents = PickList.Cents(text);
            if (cents <= 0) { Say("Co-op: that isn't an amount of dollars"); return; }
            if (cents > Money.MaxCents) { Say("Co-op: $10,000 at most"); return; }
            if (link.State != LinkState.Connected || !link.Roster.Exists(e => e.Id == to)) { Say($"Co-op: {name} isn't here any more"); return; }
            if (Off) { Say("Co-op: no giving money in a story mission or while you play"); return; }
            if (!Money.Take(cents)) { Say($"Co-op: you don't have {Money.Dollars(cents)}"); return; }
            link.SendNotice(to, NoticeKind.Money, _ledger.Give(to, cents, _now));
            _names[to] = name;
            Say($"Co-op: giving {name} {Money.Dollars(cents)}...");
            CoopScript.Log($"gave {name} {Money.Dollars(cents)}; waiting for their game to take it");
        }

        // Every tick while connected: gifts of ours not answered yet go again every 2 s; nobody took them in 10 s: back.
        public void Tick(ServerLink link, long now)
        {
            _now = now;
            if (_ledger.Waiting == 0) return;
            foreach (var (to, text) in _ledger.Resend(now)) link.SendNotice(to, NoticeKind.Money, text);
            foreach (var g in _ledger.Overdue(now)) Back(g.to, g.cents, "no answer in 10 s");
        }

        // A Money notice: a partner's gift to us, or their answer to ours.
        public void OnNotice(byte from, string text, ServerLink link, long now)
        {
            _now = now;
            if (!Extras.GiveMoney || from == link.MyId || !link.Roster.Exists(e => e.Id == from)) return;
            string name = Plain(link.NameOf(from));
            var (pay, reply, settled) = _ledger.Hear(from, text, now, cents =>
            {
                if (!Off && Money.Give(cents)) return true;
                CoopScript.Log($"{name}'s {Money.Dollars(cents)} not taken (busy, or the game refused): it goes back to them");
                return false;
            });
            if (settled > 0)
            {
                Stats.Add("money.given", settled);
                Say($"Co-op: {name} got your {Money.Dollars(settled)}");
                CoopScript.Log($"{name} took our {Money.Dollars(settled)}");
            }
            if (reply != null) link.SendNotice(from, NoticeKind.Money, reply);   // a repeat of one we took: answered again
            if (pay <= 0) return;
            Stats.Add("money.received", pay);
            Say($"Co-op: {name} gave you {Money.Dollars(pay)}");
            CoopScript.Log($"{name} gave us {Money.Dollars(pay)}");
        }

        // The session is over or the mod stops: what's still waiting comes back to us now.
        public void Clear()
        {
            foreach (var g in _ledger.Clear()) Back(g.to, g.cents, "the session ended");
            _names.Clear();
        }

        void Back(byte to, int cents, string why)
        {
            string name = _names.TryGetValue(to, out var n) ? n : "your partner";
            bool ok = Money.Give(cents);
            CoopScript.Log($"our {Money.Dollars(cents)} for {name} ({why}): " + (ok ? "back with us" : "the game refused it back"));
            if (ok) Say($"Co-op: {name} didn't take your {Money.Dollars(cents)}: it's back with you");
        }

        static string Plain(string s) => new string((s ?? "").Where(c => c != '~' && c != '<' && c != '>').ToArray());   // names come from strangers
        static void Say(string text) { if (!TableGames.Hold(text)) Screen.DisplaySubtitle(text); }
    }
}
