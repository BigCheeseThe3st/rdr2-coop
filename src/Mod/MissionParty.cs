// Mission party: on by default (the author, 25 Oct: "you are in one by default"). The host's
// game starts it as soon as a second player is on the server, and everyone who joins is in;
// the host can switch it off and on from the co-op menu (F10 twice). Members
// are kept with the host: their screen goes black while the host is in a cutscene or a faded
// transition (drawn by the mod with a line saying why, bug #23: the game's own fade hid any text), and they are put next to the host when it ends or whenever the host ends up far
// away (fast travel, mission skips). A member who is far from the host when that would happen
// is asked first (FollowPrompt: 30 s, N stays put for 5 minutes, Y goes now); one close by is
// in the host's world already and just follows. The mission itself only runs in the host's
// game; members take part through the copied NPCs and vehicles.
using System;
using System.Collections.Generic;
using System.Drawing;
using RDR2;
using RDR2.Math;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class MissionParty
    {
        enum Mode { None, Hosting, Member }

        const float FarDistance = Authority.LeaveRadius;   // past this we're out of the host's world anyway
        const int MaxRewardCents = 50000;   // one payment: no story mission pays more than $500 at once

        Mode _mode;
        uint _partyId;
        byte _host;
        readonly HashSet<byte> _members = new HashSet<byte>();
        long _lastSay;
        bool _hostOff;         // the host switched the party off: don't start it again until they switch it on
        readonly FollowPrompt _prompt = new FollowPrompt();
        bool _hidden;          // our screen is faded and we are frozen for the host's cutscene
        int _lastCash = -1;
        readonly Random _rng = new Random();

        public bool IsMember => _mode == Mode.Member;
        public bool IsHosting => _mode == Mode.Hosting;
        public string Status => _mode == Mode.Hosting ? $"mission party on ({_members.Count} with you)"
                              : _mode == Mode.Member ? (_prompt.Asking ? "asked to go to the host (Y/N)" : "in the host's mission party")
                              : _hostOff ? "mission party off" : "no mission party";

        // Host (co-op menu, F10 twice): switches the party off (nobody follows you) or back on.
        public void Toggle(ServerLink link)
        {
            if (!link.IAmHost) { Say("Co-op: only the host starts missions"); return; }
            if (_mode == Mode.Hosting) { _hostOff = true; End(link, "mission party off: nobody follows you now"); }
            else { _hostOff = false; Start(link, false); }
        }

        void Start(ServerLink link, bool auto)
        {
            _mode = Mode.Hosting;
            _lastCash = MONEY._MONEY_GET_CASH_BALANCE();
            Money.Given = 0;   // what the mod paid before the party is in _lastCash already
            _partyId = (uint)_rng.Next(1, int.MaxValue);
            _members.Clear();
            link.SendParty(new PartyMsg { Kind = PartyKind.Invite, PartyId = _partyId });
            Say(auto ? "Co-op: mission party on - everyone follows you into missions (switch it off from the co-op menu, F10 twice)"
                     : "Co-op: mission party on - everyone follows you into missions");
            CoopScript.Log($"party {_partyId}: on{(auto ? " (a second player is here)" : "")}");
        }

        void End(ServerLink link, string why)
        {
            if (_mode == Mode.Hosting) link.SendParty(new PartyMsg { Kind = PartyKind.End, PartyId = _partyId });
            Leave();
            _mode = Mode.None;
            Say("Co-op: " + why);
        }

        // Y/N while "going to the host in N s" is on screen.
        public void OnKey(System.Windows.Forms.Keys key, ServerLink link)
        {
            if (_mode != Mode.Member) return;
            if (key == System.Windows.Forms.Keys.N && _prompt.Decline(Environment.TickCount))
            {
                link.SendParty(new PartyMsg { Kind = PartyKind.Decline, PartyId = _partyId });
                Say($"Co-op: staying here; you're asked again in {FollowPrompt.StayMs / 60000} minutes (or when {link.NameOf(_host)} is far again)");
            }
            else if (key == System.Windows.Forms.Keys.Y && _prompt.Accept()) _goNow = true;
        }
        bool _goNow;

        // Host: someone joined the server while a party runs: ask them too.
        public void OnRoster(ServerLink link)
        {
            if (_mode == Mode.Hosting) link.SendParty(new PartyMsg { Kind = PartyKind.Invite, PartyId = _partyId });
        }

        public void OnMessage(byte from, PartyMsg m, ServerLink link)
        {
            switch (m.Kind)
            {
                case PartyKind.Invite when from == link.HostId:
                    CoopScript.Log($"party {m.PartyId}: invited by {link.NameOf(from)}");
                    if (_mode == Mode.Member && m.PartyId == _partyId) break;   // already in
                    _mode = Mode.Member;   // in by default: the prompt asks before any move while far away
                    _partyId = m.PartyId;
                    _host = from;
                    _prompt.Reset();
                    link.SendParty(new PartyMsg { Kind = PartyKind.Accept, PartyId = _partyId });
                    Say($"Co-op: you're in {link.NameOf(from)}'s mission party (you're asked before being moved to them from far away)");
                    break;
                case PartyKind.Accept when _mode == Mode.Hosting && m.PartyId == _partyId:
                    _members.Add(from);
                    link.SendParty(new PartyMsg { Kind = PartyKind.Members, PartyId = _partyId, Members = new List<byte>(_members) });
                    Say($"Co-op: {link.NameOf(from)} joined the mission");
                    break;
                case PartyKind.Decline when _mode == Mode.Hosting && m.PartyId == _partyId:
                    Say($"Co-op: {link.NameOf(from)} stays where they are for now");
                    break;
                case PartyKind.Reward when _mode == Mode.Member && from == _host && m.PartyId == _partyId && m.Amount > 0:
                    int amount = Math.Min(m.Amount, MaxRewardCents);
                    if ((long)MONEY._MONEY_GET_CASH_BALANCE() + amount > int.MaxValue / 2) break;   // never near an overflow
                    MONEY._MONEY_INCREMENT_CASH_BALANCE(amount, MISC.GET_HASH_KEY("ADD_REASON_DEFAULT"));
                    Say($"Co-op: mission money shared: +${amount / 100.0:0.00}");
                    CoopScript.Log($"party {_partyId}: received ${amount / 100.0:0.00}");
                    break;
                case PartyKind.End when from == _host && m.PartyId == _partyId:
                    Leave();
                    _mode = Mode.None;
                    Say($"Co-op: {link.NameOf(from)} ended the mission party");
                    break;
            }
        }

        // hostState: the host's latest state (null if we have none yet).
        public void Tick(ServerLink link, PlayerState? hostState, long now)
        {
            if (_mode == Mode.Hosting)
            {
                if (!link.IAmHost) { End(link, "you're no longer the host"); return; }
                // Money the host makes in a story mission goes to every member too (not free-roam cash: the party is always on).
                // Not what the mod paid us (gifts, a gift back, the mod's tables): a Give the balance doesn't show yet counts next tick
                // (shortcut: cash spent in the same tick as a Give stays counted as mod money, so that much mission money isn't shared).
                int cash = MONEY._MONEY_GET_CASH_BALANCE(), rise = cash - _lastCash, share = rise - Money.Given;
                Money.Given = Math.Max(0, Money.Given - Math.Max(0, rise));
                if (_lastCash >= 0 && share > 0 && _members.Count > 0 && !Activities.Busy && MissionProbe.StoryMission)   // card winnings (the game's tables or the mod's) aren't mission money
                {
                    link.SendParty(new PartyMsg { Kind = PartyKind.Reward, PartyId = _partyId, Amount = share });
                    CoopScript.Log($"party {_partyId}: shared ${share / 100.0:0.00}");
                }
                _lastCash = cash;
                return;
            }
            if (link.IAmHost)
            {
                if (_mode == Mode.None && !_hostOff && link.Roster.Count > 1) Start(link, true);   // on by default once someone else is here
                if (_mode != Mode.Hosting && _mode != Mode.None) { Leave(); _mode = Mode.None; }
                return;
            }

            if (_mode != Mode.Member || hostState == null) return;
            if (link.HostId != _host) { Leave(); _mode = Mode.None; return; }
            if (Activities.Busy) { Show(); _prompt.Tick(false, now); return; }   // in a mini-game or activity: no fades or moves till it's over

            var host = hostState.Value;
            // A host who's down or dead isn't in a cutscene, whatever their screen shows: we stay free to revive them.
            bool hostHidden = (host.Flags & (StateFlags.InCutscene | StateFlags.ScreenFaded)) != 0 && (host.Flags & (StateFlags.Downed | StateFlags.Dead)) == 0;
            var me = Game.Player.Character;
            bool far = me.Position.DistanceTo(CoopScript.ToGame(host.Position)) > FarDistance;

            // Far from the host and about to be faded out or moved: ask first. Close by, we're in their world already.
            bool wanted = far && !_hidden, go = _prompt.Tick(wanted, now) || (_goNow && wanted);
            _goNow = false;
            if (wanted && !go)
            {
                if (_prompt.Asking && now - _lastSay > 2500)   // keep the question on screen: other subtitles would replace it
                {
                    _lastSay = now;
                    Say($"Co-op: going to {link.NameOf(_host)} in {_prompt.SecondsLeft(now)} s - press N to stay here, Y to go now");
                }
                return;
            }

            if (hostHidden)
            {
                Hide();
                DrawWaiting(link.NameOf(_host), (host.Flags & StateFlags.InCutscene) != 0);
                if (now - _lastSay > 4000) { _lastSay = now; Say($"Co-op: {link.NameOf(_host)} is in a cutscene"); }
                return;
            }
            if (_hidden)
            {
                // The cutscene or transition is over: join the host, then show.
                if (me.Position.DistanceTo(CoopScript.ToGame(host.Position)) > 20f) MoveTo(host);
                Show();
                return;
            }
            if (far)
            {
                CAM.DO_SCREEN_FADE_OUT(300);
                for (int i = 0; i < 20 && !CAM.IS_SCREEN_FADED_OUT(); i++) Script.Yield();
                MoveTo(host);
                CAM.DO_SCREEN_FADE_IN(800);
                Say($"Co-op: brought you back to {link.NameOf(_host)}");
            }
        }

        void Hide()
        {
            if (_hidden) return;
            _hidden = true;
            var me = Game.Player.Character;
            ENTITY.FREEZE_ENTITY_POSITION(me.Handle, true);   // the black screen is drawn each tick (DrawWaiting), not the game's fade
            CoopScript.Log("mission party: host in a cutscene or loading: screen black with a message, us frozen");
            ENTITY.SET_ENTITY_INVINCIBLE(me.Handle, true);
            PLAYER.SET_PLAYER_CONTROL(Game.Player.Handle, false, 0, false);
        }

        void Show()
        {
            if (!_hidden) return;
            _hidden = false;
            var me = Game.Player.Character;
            ENTITY.FREEZE_ENTITY_POSITION(me.Handle, false);
            ENTITY.SET_ENTITY_INVINCIBLE(me.Handle, false);
            PLAYER.SET_PLAYER_CONTROL(Game.Player.Handle, true, 0, false);
            CAM.DO_SCREEN_FADE_IN(800);   // only does anything if the game itself faded us meanwhile
            CoopScript.Log("mission party: the host's cutscene or loading is over: screen back, us free");
        }

        // Our own black screen with a line on top, every tick while hidden: the game's screen fade
        // covers script text, so players saw plain black and thought the game had hung.
        static void DrawWaiting(string host, bool cutscene)
        {
            GRAPHICS.DRAW_RECT(0.5f, 0.5f, 1f, 1f, 0, 0, 0, 255, true, false);
            string line = cutscene ? $"{host} is in a cutscene" : $"{host} is loading";
            const string next = "you'll join them when it ends (the cutscene plays in their game)";
            Ui.Text(line, 0.5f - line.Length * 0.00875f * 0.55f, 0.45f, 0.55f, Color.FromArgb(255, 235, 225, 200));
            Ui.Text(next, 0.5f - next.Length * 0.00875f * 0.4f, 0.5f, 0.4f, Color.FromArgb(220, 200, 190, 170));
        }

        public void Leave()
        {
            Show();
            _members.Clear();
            _prompt.Reset();
        }

        // Next to the host (a couple of metres behind them), on the ground,
        // horse and all if we ride.
        static void MoveTo(PlayerState host)
        {
            var me = Game.Player.Character;
            double r = host.Heading * Math.PI / 180.0;
            var at = CoopScript.ToGame(host.Position) + new Vector3((float)Math.Sin(r) * 2.5f, (float)-Math.Cos(r) * 2.5f, 0f);
            STREAMING.REQUEST_COLLISION_AT_COORD(at);
            int mover = RideDouble.Mover(me);
            ENTITY.SET_ENTITY_COORDS(mover, at, false, false, false, false);
            for (int i = 0; i < 60 && !ENTITY.HAS_COLLISION_LOADED_AROUND_ENTITY(mover); i++) Script.Yield();
            CoopScript.Log($"party: moved next to the host at {at}");
        }

        static void Say(string text) { if (!TableGames.Hold(text)) RDR2.UI.Screen.DisplaySubtitle(text); }
    }
}
