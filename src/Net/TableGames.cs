// Card and table games (0.8.0, [Gameplay] MiniGames), the parts that need no game
// (src/Mod/TableGames.cs has the game side): what a player at a table tells the others,
// and when a partner's stand-in sits with them. The word goes in an Activity notice
// (NoticeKind.Activity): one activity's name ("poker", "blackjack", "dominoes" or
// "fillet" here; other activities have their own names) while it lasts, "" when it's
// over. It goes again every few seconds, and a game not heard of for a while counts as
// over, so a lost packet can't leave anyone sitting at a table in someone else's game.
using System.Collections.Generic;
using System.Linq;

namespace RDR2Coop.Net
{
    public static class TableRules
    {
        public static readonly string[] Games = { "poker", "blackjack", "dominoes", "fillet" };
        public const int RefreshMs = 5000, ForgetMs = 15000;

        // What to send now, or null: our game when it starts and every RefreshMs while it lasts, "" once when it ends.
        // sent = null makes it go again at once (someone new joined).
        public static string Due(string playing, ref string sent, ref long sentMs, long now)
        {
            if (playing == sent && (playing == null || now - sentMs < RefreshMs)) return null;
            sent = playing;
            sentMs = now;
            return playing ?? "";
        }

        // The table game a partner's notice names, or null (it's over, or it's another activity).
        // Only these words are ever used, never the text itself: it comes from strangers.
        public static string Game(string text) => Games.FirstOrDefault(g => g == text);

        // For F10 (and the party list): " (playing poker)", or "" when they aren't at a table.
        public static string Label(string game) =>
            game == null ? "" : game == "fillet" ? " (playing five finger fillet)" : " (playing " + game + ")";

        // A partner's stand-in sits while they play at a table, on foot (free: not mounted, in a vehicle
        // or down) and still, once it has reached them; sitting, it stays put until they get up or go.
        public static bool Sits(bool atTable, bool free, float speed, float gap, bool sitting) =>
            atTable && free && speed <= 0.6f && (sitting || gap < 3f);
    }

    // Which partners play at a table, from their Activity notices.
    public sealed class TablePlayers
    {
        readonly Dictionary<byte, (string game, long heard)> _at = new Dictionary<byte, (string, long)>();

        // False when it only repeats the table game they already play (it goes every 5 s): nothing new to show.
        public bool Heard(byte id, string text, long now)
        {
            string game = TableRules.Game(text);
            bool again = game != null && Of(id, now) == game;
            if (game == null) _at.Remove(id);
            else _at[id] = (game, now);
            return !again;
        }

        public string Of(byte id, long now) => _at.TryGetValue(id, out var a) && now - a.heard <= TableRules.ForgetMs ? a.game : null;

        public void KeepOnly(ICollection<byte> ids)
        {
            foreach (var id in _at.Keys.Where(k => !ids.Contains(k)).ToList()) _at.Remove(id);
        }

        public void Clear() => _at.Clear();
    }
}
