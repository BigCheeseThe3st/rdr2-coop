// enRage (with its unofficial co-op patch) in the host's game talks to the
// other players through us: its news and grudge notices are queued in the
// script domain and sent to the right player, and a guest's F11 asks the
// host's enRage which nemeses hold a grudge against them.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using RDR2.UI;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class EnrageLink
    {
        const string QueueKey = "rdr2coop.noticeQueue", ListKey = "rdr2coop.nemesesFor";
        const int PanelMs = 12000;

        readonly ConcurrentQueue<string[]> _queue;
        readonly FarEncounter _far = new FarEncounter();
        string[] _panel;
        long _panelUntil;
        Dictionary<ushort, string> _names = new Dictionary<ushort, string>();
        byte _namesFrom;

        public EnrageLink()
        {
            _queue = AppDomain.CurrentDomain.GetData(QueueKey) as ConcurrentQueue<string[]> ?? new ConcurrentQueue<string[]>();
            AppDomain.CurrentDomain.SetData(QueueKey, _queue);
        }

        // enRage runs in this game (its own F11 menu is then the one to use).
        // Set by enRage itself (its copy check) when it runs in this loader.
        public static bool EnrageHere => !StoppedForGuest && AppDomain.CurrentDomain.GetData("enRAGE.active") != null;

        /// <summary>enRage is installed here but stopped while we're a guest (EnrageGuest): F11 asks the host's.</summary>
        public static bool StoppedForGuest { get; set; }

        // Name tags over the copies of the area owner's named NPCs.
        public void DrawNames(byte owner, Func<ushort, int> handleOf)
        {
            if (owner != _namesFrom || _names.Count == 0) return;
            var me = RDR2.Game.Player.Character.Position;
            foreach (var kv in _names)
            {
                int h = handleOf(kv.Key);
                if (h == 0) continue;
                var at = Peds.Position(h);
                if (at.DistanceTo(me) > 30f || !RDR2.Native.ENTITY.IS_ENTITY_ON_SCREEN(h)) continue;
                DrawTag(kv.Value, at + new RDR2.Math.Vector3(0f, 0f, 1.1f));
            }
        }

        static unsafe void DrawTag(string text, RDR2.Math.Vector3 at)
        {
            float x = 0f, y = 0f;
            if (!RDR2.Native.GRAPHICS.GET_SCREEN_COORD_FROM_WORLD_COORD(at, &x, &y)) return;
            Ui.Text(text, x - text.Length * 0.0028f, y, 0.32f, Color.FromArgb(235, 240, 225, 200));   // roughly centred over them
        }

        public void Tick(ServerLink link, bool downed, long now)
        {
            while (_queue.TryDequeue(out var n))
            {
                if (n == null || n.Length < 2) continue;
                var kind = n.Length > 2 && n[2] == "encounter" ? NoticeKind.Encounter : NoticeKind.Text;
                if (n[0] == "*") { link.SendNotice(Protocol.Broadcast, kind, n[1]); continue; }
                var to = link.Roster.FirstOrDefault(e => e.Name == n[0]);
                if (to.Id != 0) link.SendNotice(to.Id, kind, n[1]);
            }
            _far.Tick(link, downed, now);
            if (_panel != null && now < _panelUntil && !Chat.Typing) Draw(_panel);   // chat types in the same place
        }

        public void OnNotice(byte from, NoticeMsg m, ServerLink link, long now)
        {
            switch (m.Kind)
            {
                case NoticeKind.Text:
                    if (!TableGames.Hold(m.Text)) Screen.DisplaySubtitle(m.Text);
                    CoopScript.Log("enRage: " + m.Text);
                    break;
                case NoticeKind.NemesesRequest:   // we host: ask our enRage
                    var list = AppDomain.CurrentDomain.GetData(ListKey) as Func<string, string[]>;
                    string[] lines = list == null ? new[] { "enRage (with the co-op patch) isn't running in the host's game." }
                                                  : list(link.NameOf(from));
                    if (lines.Length == 0) lines = new[] { "No nemesis holds a grudge against you yet." };
                    link.SendNotice(from, NoticeKind.Nemeses, string.Join("\n", lines));
                    break;
                case NoticeKind.Encounter when from == link.HostId:   // a nemesis comes for us out here
                    _far.OnPlan(from, m.Text, link, now);
                    break;
                case NoticeKind.EncounterResult:                      // we host: tell our enRage
                    (AppDomain.CurrentDomain.GetData("rdr2coop.encounterResult") as Action<string, string>)?.Invoke(link.NameOf(from), m.Text);
                    break;
                case NoticeKind.Names:
                    _namesFrom = from;
                    _names = (m.Text ?? "").Split('\n').Where(l => l.Contains("="))
                        .Select(l => (ok: ushort.TryParse(l.Substring(0, l.IndexOf('=')), out ushort id), id, name: l.Substring(l.IndexOf('=') + 1)))
                        .Where(t => t.ok).GroupBy(t => t.id).ToDictionary(g => g.Key, g => g.First().name);
                    break;
                case NoticeKind.Nemeses when from == link.HostId:
                    _panel = new[] { "Your nemeses (in " + link.NameOf(from) + "'s enRage world)" }.Concat(m.Text.Split('\n')).ToArray();
                    _panelUntil = now + PanelMs;
                    break;
            }
        }

        public void Clear() => _far.Clear();

        // Guest's key: ask the host's enRage. False when enRage runs here.
        public bool RequestList(ServerLink link)
        {
            if (EnrageHere || link.IAmHost) return false;
            link.SendNotice(link.HostId, NoticeKind.NemesesRequest, "");
            return true;
        }

        static void Draw(string[] lines) =>
            Ui.Panel(lines.Select((l, i) => (l, i == 0 ? Color.FromArgb(255, 200, 40, 40) : Color.White)).ToList(), 560f);
    }

#if !NET48
    // enRage off while we're a guest (George, 31 Oct: "off whenever a guest"). A guest's enRage runs its own world and
    // starts its own fights next to them; shown the host's world, those people are hidden and attacked unseen (George's
    // O'Driscolls and Lemoyne Raiders, enRage's log, 31 Oct). The loader stops just enRage (on its way out it saves and
    // despawns its people) and starts it again, co-op patch and all, once we've been no guest for a while.
    static class EnrageGuest
    {
        const int BackAfterMs = 30000;   // a host's drop or a reshuffle of the room shouldn't restart enRage
        static string _file;             // enRage's file while we have it stopped
        static long _notGuestSince = -1;

        public static void Tick(bool guest, long now)
        {
            var domain = RDR2DN.ScriptDomain.CurrentDomain;
            if (domain == null) return;
            if (guest)
            {
                _notGuestSince = -1;
                if (_file != null) return;
                var s = domain.RunningScripts.FirstOrDefault(x => x.IsRunning && x.FileName != null
                    && System.IO.Path.GetFileName(x.FileName).Equals("NemesisMod.dll", StringComparison.OrdinalIgnoreCase));
                if (s == null) return;
                _file = s.FileName;
                EnrageLink.StoppedForGuest = true;
                domain.AbortScripts(_file);
                CoopScript.Log("enRage: stopped while we're a guest (its own fights would start next to us, hidden in the host's world); back when we host or leave");
                CoopScript.Say("Co-op: enRage is off while you're a guest (it runs on the host). It comes back when you host or leave.");
                return;
            }
            if (_file == null) return;
            if (_notGuestSince < 0) { _notGuestSince = now; return; }
            if (now - _notGuestSince < BackAfterMs) return;
            string file = _file;
            _file = null;
            EnrageLink.StoppedForGuest = false;
            domain.StartScripts(file);
            CoopScript.Log("enRage: started again (no longer a guest)");
        }
    }
#endif
}
