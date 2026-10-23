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
        public static bool EnrageHere => AppDomain.CurrentDomain.GetData("enRAGE.active") != null;

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
            new TextElement(text, new PointF(x * 1280f, y * 720f), 0.32f, Color.FromArgb(235, 240, 225, 200), Alignment.Center, true, true).Draw();
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
            if (_panel != null && now < _panelUntil) Draw(_panel);
        }

        public void OnNotice(byte from, NoticeMsg m, ServerLink link, long now)
        {
            switch (m.Kind)
            {
                case NoticeKind.Text:
                    Screen.DisplaySubtitle(m.Text);
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

        static void Draw(string[] lines)
        {
            const float x = 40f, y = 60f, line = 22f;
            new ContainerElement(new PointF(x - 12f, y - 10f), new SizeF(560f, line * lines.Length + 20f), Color.FromArgb(170, 0, 0, 0)).ScaledDraw();
            for (int i = 0; i < lines.Length; i++)
                new TextElement(lines[i], new PointF(x, y + i * line), i == 0 ? 0.42f : 0.36f,
                                i == 0 ? Color.FromArgb(255, 200, 40, 40) : Color.White).ScaledDraw();
        }
    }
}
