// In-game text chat (0.8.0, [Convenience] Chat): the chat key ([Keys] Chat=, T: nothing in story mode
// uses it, it is Red Dead Online's chat key) opens the game's keyboard, and what you type goes to
// everyone in the session. The last few messages show for 10 s with the sender's name, in a panel
// on the left. The text rules (cleaning, wrapping, the flood cap) are in src/Net/Chat.cs.
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using RDR2.Native;
using RDR2.UI;
using RDR2Coop.Net;
using Keys = System.Windows.Forms.Keys;

namespace RDR2Coop.Mod
{
    sealed class Chat
    {
        // Where the panel's first line sits on Ui.Panel's 1280x720 layout. Left column, top to bottom: the
        // co-op menu and enRage's lists (y 50 to ~380 at their longest), chat (380 to 510: a header and
        // ChatLog.MaxLines lines), the game's radar (from ~515). While typing it moves up to the co-op menu's
        // place (closed then), clear of the game's keyboard in the middle of the screen.
        public const float Top = 390f, TypingTop = 60f;
        const int KeyboardDefault = 0;   // KTEXTTYPE_DEFAULT

        public Keys Key = Keys.T;
        readonly ChatLog _log = new ChatLog();
        static bool _typing;
        bool _shown;

        // The keyboard is open: the panel sits where enRage's lists go, so those wait.
        public static bool Typing => _typing;
        long _typingSince, _lastSent = -1000;

        // True when the key was chat's: its key opening the keyboard, or anything typed into that keyboard
        // (so typing "y" or F7 there doesn't answer a party invite or revive anyone).
        public bool OnKey(Keys key, ServerLink link, bool menuOpen, long now)
        {
            if (_typing) return true;
            if (!Extras.Chat || key != Key || link.State != LinkState.Connected || menuOpen || MissionProbe.InMissionScene
                || CAM.IS_SCREEN_FADED_OUT() || MISC.UPDATE_ONSCREEN_KEYBOARD() == 0) return false;   // 0: another keyboard is open
            if (now - _lastSent < 1000) { Screen.DisplaySubtitle("Co-op: one chat message a second"); return true; }
            MISC.DISPLAY_ONSCREEN_KEYBOARD(KeyboardDefault, "", "", "", "", "", "", ChatLog.MaxChars + 1);   // + 1 like ScriptHookRDR2DotNet's GetUserInput
            _typing = true;
            _shown = false;
            _typingSince = now;
            return true;
        }

        // Every tick: our keyboard while it's open, and the panel while there's something to show.
        public void Tick(ServerLink link, long now)
        {
            if (!Extras.Chat) return;
            if (_typing)
            {
                int status = MISC.UPDATE_ONSCREEN_KEYBOARD();
                if (status == 0) _shown = true;
                else if (status == 1) { _typing = false; Send(link, MISC.GET_ONSCREEN_KEYBOARD_RESULT(), now); }   // the result reads only in this tick
                else if (status == 2) _typing = false;   // Esc
                else if (now - _typingSince > 1000)
                {
                    _typing = false;   // it never opened, or something else closed it
                    CoopScript.Log($"chat: the game's keyboard closed (status {status}, {(_shown ? "after typing" : "never opened")})");
                    if (!_shown) Screen.DisplaySubtitle("Co-op: the game's keyboard didn't open for chat");
                }
            }
            var lines = _log.Visible(now, _typing);
            if (!_typing && lines.Count == 0) return;
            var panel = new List<(string, Color)>
            {
                (_typing ? "Chat to everyone: type, then Enter (Esc cancels)" : $"Chat   ({Key} to talk)", Color.FromArgb(255, 200, 40, 40))
            };
            panel.AddRange(lines.Select(l => (l, Color.White)));
            Ui.Panel(panel, 640f, y: _typing ? TypingTop : Top);
        }

        void Send(ServerLink link, string typed, long now)
        {
            string text = ChatLog.Clean(typed);
            if (text.Length == 0 || link.State != LinkState.Connected) return;
            link.SendNotice(Protocol.Broadcast, NoticeKind.Chat, text);
            _lastSent = now;
            Show(link.NameOf(link.MyId), text, now);
        }

        // A partner's message (the relay stamps who sent it).
        public void OnNotice(byte from, NoticeMsg n, ServerLink link, long now)
        {
            if (Extras.Chat && _log.Allow(from, now)) Show(link.NameOf(from), n.Text, now);
        }

        void Show(string name, string text, long now)
        {
            string line = _log.Add(name, text, now);
            if (line != null) CoopScript.Log("chat " + line);
        }

        // Leaving the session, or the mod stopping: our keyboard closes and the messages go.
        public void Clear()
        {
            if (_typing) MISC.CANCEL_ONSCREEN_KEYBOARD();
            _typing = false;
            _log.Clear();
        }
    }
}
