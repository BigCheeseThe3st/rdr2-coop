// Shared map waypoints (0.8.0, [Convenience] Waypoint). Twice a second we look at our
// own map waypoint and tell the others when it's set, moved or cleared (and every 10 s
// again, for lost packets and late joiners). Each partner's waypoint is a named marker on
// our map, and the host's is also our GPS route while we have no waypoint of our own.
// Scripts can't place the player's waypoint in RDR2, so that route is a GPS route we draw
// and remove ourselves: a waypoint the player set is never touched.
using System;
using System.Collections.Generic;
using System.Linq;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class Waypoints
    {
        const int CheckEveryMs = 500, ResendMs = 10000;
        // The game's own look for a posse leader's waypoint: the waypoint flag in its own colour.
        const string Style = "BLIP_STYLE_GANG_WAYPOINT", RouteColor = "COLOR_GANG_WAYPOINT";

        sealed class Mark { public Vec3 At; public int Blip; public bool Moved; }

        readonly Dictionary<byte, Mark> _marks = new Dictionary<byte, Mark>();
        long _lastCheckMs = -CheckEveryMs, _sentMs = -ResendMs;
        string _sent = "";
        Vec3? _route;   // where the GPS route we drew goes; null = none of ours

        public void Tick(ServerLink link, long now)
        {
            if (!Extras.Waypoint || now - _lastCheckMs < CheckEveryMs) return;
            _lastCheckMs = now;

            bool own = MAP.IS_WAYPOINT_ACTIVE();
            string text = "";
            if (own)
            {
                var w = MAP._GET_WAYPOINT_COORDS();   // its z is always 0: the height map gives the ground's
                text = WaypointNotice.Encode(new Vec3(w.X, w.Y, PATHFIND.GET_APPROX_FLOOR_FOR_POINT(w.X, w.Y)));
            }
            if (text != _sent || now - _sentMs >= ResendMs)
            {
                if (text != _sent) CoopScript.Log(own ? "our waypoint: " + text : "our waypoint cleared");
                _sent = text;
                _sentMs = now;
                link.SendNotice(Protocol.Broadcast, NoticeKind.Waypoint, text);
            }

            foreach (var kv in _marks)
            {
                var m = kv.Value;
                if (m.Blip == 0 || !MAP.DOES_BLIP_EXIST(m.Blip))
                {
                    Blips.Mine.Remove(m.Blip);
                    m.Blip = MAP.BLIP_ADD_FOR_COORDS(MISC.GET_HASH_KEY(Style), CoopScript.ToGame(m.At));
                    if (m.Blip == 0) continue;
                    // Names come from strangers: no ~ < > (the game's text markup).
                    string name = new string(link.NameOf(kv.Key).Where(c => c != '~' && c != '<' && c != '>').ToArray()) + "'s waypoint";
                    MAP._SET_BLIP_NAME(m.Blip, name);
                    Blips.Mine.Add(m.Blip);   // ours: never shared as a mission objective
                    CoopScript.Log($"{name} on our map at {m.At}");
                }
                else if (m.Moved) MAP.SET_BLIP_COORDS(m.Blip, CoopScript.ToGame(m.At));
                m.Moved = false;
            }

            var want = WaypointNotice.Follow(link.IAmHost, _marks.TryGetValue(link.HostId, out var host) ? host.At : (Vec3?)null,
                                             own, MissionProbe.StoryMission);
            if (Nullable.Equals(want, _route)) return;
            ClearRoute();
            if (want == null) return;
            var to = want.Value;
            MAP.START_GPS_MULTI_ROUTE(MISC.GET_HASH_KEY(RouteColor), true, true);
            MAP.ADD_POINT_TO_GPS_MULTI_ROUTE(to.X, to.Y, to.Z, false);
            MAP.SET_GPS_MULTI_ROUTE_RENDER(true);
            _route = want;
            CoopScript.Log($"GPS route to {link.NameOf(link.HostId)}'s waypoint at {to}");
        }

        // A partner's waypoint, or (empty text) they cleared it. The markers are made in Tick,
        // so a flood of notices costs no game calls.
        public void OnNotice(byte from, string text, ServerLink link)
        {
            if (!Extras.Waypoint || !link.Roster.Exists(e => e.Id == from) || !WaypointNotice.TryParse(text, out var at)) return;
            if (at == null) { Forget(from); return; }
            if (!_marks.TryGetValue(from, out var m)) _marks[from] = new Mark { At = at.Value };
            else if (!m.At.Equals(at.Value)) { m.At = at.Value; m.Moved = true; }
        }

        // Someone joined or left: the markers of those who left go, and newcomers get ours.
        public void OnRoster(ICollection<byte> ids)
        {
            foreach (var id in _marks.Keys.Where(id => !ids.Contains(id)).ToList()) Forget(id);
            _sentMs = -ResendMs;
        }

        // The session is over or the mod stops: every marker and our route go.
        public void Clear()
        {
            foreach (var id in _marks.Keys.ToList()) Forget(id);
            ClearRoute();
        }

        void Forget(byte id)
        {
            if (!_marks.TryGetValue(id, out var m)) return;
            Blips.Remove(ref m.Blip);
            _marks.Remove(id);
        }

        void ClearRoute()
        {
            if (_route == null) return;
            MAP.CLEAR_GPS_MULTI_ROUTE();
            _route = null;
            CoopScript.Log("GPS route to the host's waypoint removed");
        }
    }
}
