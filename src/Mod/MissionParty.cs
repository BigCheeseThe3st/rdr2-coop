// Mission party: the host invites everyone (co-op menu, F10 twice, or automatically when a
// cutscene starts with no party), each player answers Y or N, and members are
// then kept with the host: their screen fades out while the host is in a
// cutscene or a faded transition, and they are put next to the host when it
// ends or whenever the host ends up far away (fast travel, mission skips).
// The mission itself only runs in the host's game; members take part through
// the copied NPCs and vehicles.
using System;
using System.Collections.Generic;
using RDR2;
using RDR2.Math;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class MissionParty
    {
        enum Mode { None, Hosting, Invited, Member }

        const float FarDistance = 250f;
        const int InviteMs = 20000;
        const int AutoInviteEveryMs = 5 * 60 * 1000;
        const int MaxRewardCents = 50000;   // one payment: no story mission pays more than $500 at once

        Mode _mode;
        uint _partyId;
        byte _host;
        readonly HashSet<byte> _members = new HashSet<byte>();
        long _inviteUntil, _lastAutoInvite = -AutoInviteEveryMs, _farSince = -1, _lastSay;
        bool _hidden;          // our screen is faded and we are frozen for the host's cutscene
        bool _hostWasInCutscene;
        int _lastCash = -1;
        readonly Random _rng = new Random();

        public bool IsMember => _mode == Mode.Member;
        public bool IsHosting => _mode == Mode.Hosting;
        public string Status => _mode == Mode.Hosting ? $"hosting a mission party ({_members.Count} joined)"
                              : _mode == Mode.Member ? "in the host's mission party"
                              : _mode == Mode.Invited ? "invited to a mission (Y/N)" : "no mission party";

        // Host (co-op menu, F10 twice): starts a party, or ends the one running.
        public void Toggle(ServerLink link)
        {
            if (!link.IAmHost) { Say("Co-op: only the host starts missions"); return; }
            if (_mode == Mode.Hosting) End(link, "you ended the mission party");
            else Start(link, false);
        }

        void Start(ServerLink link, bool auto)
        {
            _mode = Mode.Hosting;
            _lastCash = MONEY._MONEY_GET_CASH_BALANCE();
            _partyId = (uint)_rng.Next(1, int.MaxValue);
            _members.Clear();
            link.SendParty(new PartyMsg { Kind = PartyKind.Invite, PartyId = _partyId });
            Say(auto ? "Co-op: cutscene started - everyone was invited to join this mission (end it from the co-op menu, F10 twice)"
                     : "Co-op: mission party started - everyone was invited (end it from the co-op menu, F10 twice)");
            CoopScript.Log($"party {_partyId}: invited everyone{(auto ? " (cutscene started)" : "")}");
        }

        void End(ServerLink link, string why)
        {
            if (_mode == Mode.Hosting) link.SendParty(new PartyMsg { Kind = PartyKind.End, PartyId = _partyId });
            Leave();
            _mode = Mode.None;
            Say("Co-op: " + why);
        }

        public void OnKey(System.Windows.Forms.Keys key, ServerLink link)
        {
            if (_mode != Mode.Invited) return;
            if (key == System.Windows.Forms.Keys.Y)
            {
                link.SendParty(new PartyMsg { Kind = PartyKind.Accept, PartyId = _partyId });
                _mode = Mode.Member;
                Say($"Co-op: you joined {link.NameOf(_host)}'s mission");
            }
            else if (key == System.Windows.Forms.Keys.N)
            {
                link.SendParty(new PartyMsg { Kind = PartyKind.Decline, PartyId = _partyId });
                _mode = Mode.None;
                Say("Co-op: you stayed out of the mission");
            }
        }

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
                    _mode = Mode.Invited;
                    _partyId = m.PartyId;
                    _host = from;
                    _inviteUntil = Environment.TickCount + InviteMs;
                    Say($"Co-op: {link.NameOf(from)} is starting a mission - press Y to join, N to stay out");
                    break;
                case PartyKind.Accept when _mode == Mode.Hosting && m.PartyId == _partyId:
                    _members.Add(from);
                    link.SendParty(new PartyMsg { Kind = PartyKind.Members, PartyId = _partyId, Members = new List<byte>(_members) });
                    Say($"Co-op: {link.NameOf(from)} joined the mission");
                    break;
                case PartyKind.Decline when _mode == Mode.Hosting && m.PartyId == _partyId:
                    Say($"Co-op: {link.NameOf(from)} stays out of this mission");
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
                // Money the host makes during the party goes to every member too.
                int cash = MONEY._MONEY_GET_CASH_BALANCE();
                if (_lastCash >= 0 && cash > _lastCash && _members.Count > 0)
                {
                    link.SendParty(new PartyMsg { Kind = PartyKind.Reward, PartyId = _partyId, Amount = cash - _lastCash });
                    CoopScript.Log($"party {_partyId}: shared ${(cash - _lastCash) / 100.0:0.00}");
                }
                _lastCash = cash;
                return;
            }
            if (link.IAmHost)
            {
                // Story missions open with a cutscene: invite everyone then.
                bool cut = Game.InCutscene || Game.IsCutsceneActive || MissionProbe.InMissionScene;
                if (cut && !_hostWasInCutscene && _mode == Mode.None && now - _lastAutoInvite > AutoInviteEveryMs
                    && link.Roster.Count > 1)
                {
                    _lastAutoInvite = now;
                    Start(link, true);
                }
                _hostWasInCutscene = cut;
                if (_mode != Mode.Hosting && _mode != Mode.None) { Leave(); _mode = Mode.None; }
                return;
            }

            if (_mode == Mode.Invited)
            {
                if (Environment.TickCount > _inviteUntil)
                {
                    _mode = Mode.None;
                    Say("Co-op: mission invite expired");
                }
                // Keep the question on screen: other subtitles would replace it.
                else if (now - _lastSay > 2500)
                {
                    _lastSay = now;
                    Say($"Co-op: {link.NameOf(_host)} is starting a mission - press Y to join, N to stay out");
                }
            }
            if (_mode != Mode.Member || hostState == null) return;
            if (link.HostId != _host) { Leave(); _mode = Mode.None; return; }

            var host = hostState.Value;
            bool hostHidden = (host.Flags & (StateFlags.InCutscene | StateFlags.ScreenFaded)) != 0;
            var me = Game.Player.Character;

            if (hostHidden)
            {
                Hide();
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
            if (me.Position.DistanceTo(CoopScript.ToGame(host.Position)) > FarDistance)
            {
                if (_farSince < 0) _farSince = now;
                if (now - _farSince > 3000)
                {
                    _farSince = -1;
                    CAM.DO_SCREEN_FADE_OUT(300);
                    for (int i = 0; i < 20 && !CAM.IS_SCREEN_FADED_OUT(); i++) Script.Yield();
                    MoveTo(host);
                    CAM.DO_SCREEN_FADE_IN(800);
                    Say($"Co-op: brought you back to {link.NameOf(_host)}");
                }
            }
            else _farSince = -1;
        }

        void Hide()
        {
            if (_hidden) return;
            _hidden = true;
            var me = Game.Player.Character;
            CAM.DO_SCREEN_FADE_OUT(500);
            ENTITY.FREEZE_ENTITY_POSITION(me.Handle, true);
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
            CAM.DO_SCREEN_FADE_IN(800);
        }

        public void Leave()
        {
            Show();
            _members.Clear();
            _farSince = -1;
        }

        // Next to the host (a couple of metres behind them), on the ground,
        // horse and all if we ride.
        static void MoveTo(PlayerState host)
        {
            var me = Game.Player.Character;
            double r = host.Heading * Math.PI / 180.0;
            var at = CoopScript.ToGame(host.Position) + new Vector3((float)Math.Sin(r) * 2.5f, (float)-Math.Cos(r) * 2.5f, 0f);
            STREAMING.REQUEST_COLLISION_AT_COORD(at);
            int mover = me.IsOnMount && me.CurrentMount != null ? me.CurrentMount.Handle : me.Handle;
            ENTITY.SET_ENTITY_COORDS(mover, at, false, false, false, false);
            for (int i = 0; i < 60 && !ENTITY.HAS_COLLISION_LOADED_AROUND_ENTITY(mover); i++) Script.Yield();
            CoopScript.Log($"party: moved next to the host at {at}");
        }

        static void Say(string text) => RDR2.UI.Screen.DisplaySubtitle(text);
    }
}
