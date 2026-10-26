// "Look here" pings (0.9.0, [Convenience] Ping; the format is src/Net/Ping.cs). [Keys] Ping= or the co-op menu's
// "Ping where I'm looking" marks the spot the camera looks at (a ray up to 200 m that stops at the ground, a
// building, a wagon or an object, never at people or horses; nothing in reach: where we stand) for everyone, ours
// included: a map marker named "<name>: look here" and a tall beam on the spot, for 12 s, and partners get a subtitle
// saying how far it is with the HUD's short info sound. One ping per player every 2 s. None are sent or shown in a
// story mission or cutscene or at one of the game's own tables (those showing go when one starts), and every marker
// goes when its 12 s are up, its player leaves, the session ends or the mod stops. Draws nothing on the HUD.
using System;
using System.Collections.Generic;
using System.Linq;
using Keys = System.Windows.Forms.Keys;
using RDR2;
using RDR2.Math;
using RDR2.Native;
using RDR2Coop.Net;
using Screen = RDR2.UI.Screen;

namespace RDR2Coop.Mod
{
    sealed class Pings
    {
        const int CheckMs = 200;
        const int WorldFlags = 1 | 2 | 16;   // map, vehicles, objects (ScriptHookRDR2DotNet's RaycastLOSOptions): no peds, so not us or our horse
        const int ProbeOptions = 7;          // past glass, see-through and no-collision things (its RaycastOptions.Default)
        const int Ready = 2;                 // SHAPETEST_STATUS_RESULTS_READY

        sealed class Mark { public Vec3 At; public long Until; public int Blip; public bool Ours; }

        public Keys Key;   // [Keys] Ping; empty = menu only
        readonly Dictionary<byte, Mark> _marks = new Dictionary<byte, Mark>();   // one per player: a new ping moves it
        readonly PingGate _gate = new PingGate();
        bool _wanted;
        long _checkedMs;
        uint _cylinder;

        public string MenuLabel(ServerLink link) => Extras.Ping && link != null && link.Roster.Count > 1 ? "Ping where I'm looking" : null;

        public void Request() { if (Extras.Ping) _wanted = true; }   // sent from the tick, not inside the key handler or the menu

        public bool OnKey(Keys key)
        {
            if (!Extras.Ping || Key == Keys.None || key != Key) return false;
            _wanted = true;
            return true;
        }

        static bool Off => MissionProbe.StoryMission || MissionProbe.InMissionScene || Activities.Busy;

        // Every tick while connected: our ping if one was asked for; the markers (blips every 200 ms, the beams every frame).
        public void Tick(ServerLink link, Ped me, long now)
        {
            if (_wanted) { _wanted = false; Send(link, me, now); }
            if (_marks.Count == 0) return;
            bool off = Off;
            if (off || now - _checkedMs >= CheckMs)
            {
                _checkedMs = now;
                foreach (var id in _marks.Where(kv => off || now >= kv.Value.Until).Select(kv => kv.Key).ToList()) Forget(id);
                foreach (var kv in _marks)
                {
                    var m = kv.Value;
                    if (m.Blip != 0 && MAP.DOES_BLIP_EXIST(m.Blip)) continue;
                    Blips.Mine.Remove(m.Blip);
                    m.Blip = MAP.BLIP_ADD_FOR_COORDS(MISC.GET_HASH_KEY("BLIP_STYLE_OBJECTIVE"), CoopScript.ToGame(m.At));
                    if (m.Blip == 0) continue;
                    MAP._SET_BLIP_NAME(m.Blip, m.Ours ? "Your ping" : Plain(link.NameOf(kv.Key)) + ": look here");
                    Blips.Mine.Add(m.Blip);   // ours: never shared as a mission objective
                }
            }
            if (_cylinder == 0) _cylinder = MISC.GET_HASH_KEY("MARKERTYPE_CYLINDER");
            foreach (var m in _marks.Values)   // a marker shows for the frame it's drawn in
                GRAPHICS._DRAW_MARKER(_cylinder, m.At.X, m.At.Y, m.At.Z - 0.5f, 0f, 0f, 0f, 0f, 0f, 0f, 0.8f, 0.8f, 12f,
                                      m.Ours ? 235 : 90, m.Ours ? 190 : 170, m.Ours ? 70 : 255, 150, false, false, 2, false, null, null, false);
        }

        void Send(ServerLink link, Ped me, long now)
        {
            if (!Extras.Ping || link.State != LinkState.Connected) return;
            if (link.Roster.Count < 2) { Say("Co-op: nobody here to see a ping"); return; }
            if (Off) { Say("Co-op: no pings in a story mission or while you play"); return; }
            if (!_gate.Allow(link.MyId, now)) { Say("Co-op: one ping every 2 seconds"); return; }
            var at = LookingAt(me);
            link.SendNotice(Protocol.Broadcast, NoticeKind.Ping, PingMsg.Write(at));
            Stats.Add("pings");
            Put(link.MyId, at, true, now);
            Say($"Co-op: you pinged a spot {Vec3.Distance(CoopScript.ToNet(me.Position), at):0} m away");
        }

        // Where the camera's view meets the world within 200 m, or where we stand.
        static unsafe Vec3 LookingAt(Ped me)
        {
            var mine = CoopScript.ToNet(me.Position);
            var cam = CAM.GET_GAMEPLAY_CAM_COORD();
            var to = Aim.Along(CoopScript.ToNet(cam), CoopScript.ToNet(CAM.GET_GAMEPLAY_CAM_ROT(2)), PingMsg.Reach);
            int probe = SHAPETEST.START_EXPENSIVE_SYNCHRONOUS_SHAPE_TEST_LOS_PROBE(cam.X, cam.Y, cam.Z, to.X, to.Y, to.Z, WorldFlags,
                                                                                Peds.VehicleOf(me.Handle), ProbeOptions);
            ulong* hit = stackalloc ulong[1];    // a script BOOL is 4 bytes, not C#'s one
            float* end = stackalloc float[6];    // script vectors: x, y and z in 8-byte slots (24 bytes)
            float* normal = stackalloc float[6];
            int* entity = stackalloc int[2];
            hit[0] = 0;
            if (probe == 0 || SHAPETEST.GET_SHAPE_TEST_RESULT(probe, (bool*)hit, (Vector3*)end, (Vector3*)normal, entity) != Ready || hit[0] == 0) return mine;
            var at = new Vec3(end[0], end[2], end[4]);
            return Vec3.Distance(at, CoopScript.ToNet(cam)) <= PingMsg.Reach + 1f ? at : mine;   // NaN fails too
        }

        // A partner's ping: rate-limited, checked, then on our map and in the world.
        public void OnNotice(byte from, string text, ServerLink link, long now)
        {
            if (!Extras.Ping || from == link.MyId || !link.Roster.Exists(e => e.Id == from) || Off || !_gate.Allow(from, now)) return;
            var me = Game.Player.Character;
            if (me == null || !me.Exists()) return;
            var mine = CoopScript.ToNet(me.Position);
            if (!PingMsg.Read(text, mine, out var at)) return;
            Put(from, at, false, now);
            Say(PingMsg.Line(link.NameOf(from), Vec3.Distance(mine, at)));
            AUDIO.PLAY_SOUND_FRONTEND("INFO", "HUD_SHOP_SOUNDSET", true, 0);   // the shop menu's short info sound, as the game's butcher plays it
        }

        void Put(byte from, Vec3 at, bool ours, long now)
        {
            Forget(from);
            _marks[from] = new Mark { At = at, Until = now + PingMsg.ShowMs, Ours = ours };
            _checkedMs = now - CheckMs;   // its blip on the next tick
        }

        // Someone left: their ping goes.
        public void OnRoster(ICollection<byte> ids)
        {
            foreach (var id in _marks.Keys.Where(id => !ids.Contains(id)).ToList()) Forget(id);
        }

        // The session is over or the mod stops: every marker goes.
        public void Clear()
        {
            foreach (var id in _marks.Keys.ToList()) Forget(id);
            _gate.Clear();
            _wanted = false;
        }

        void Forget(byte id)
        {
            if (!_marks.TryGetValue(id, out var m)) return;
            Blips.Remove(ref m.Blip);
            _marks.Remove(id);
        }

        static string Plain(string s) => new string((s ?? "").Where(c => c != '~' && c != '<' && c != '>').ToArray());   // names come from strangers
        static void Say(string text) { if (!TableGames.Hold(text)) Screen.DisplaySubtitle(text); }
    }
}
