// Mini-games and activities (0.8.0, [Gameplay] MiniGames), the parts the tests can check:
// what we're doing (held a moment, so a script restarting between rounds isn't leaving), the
// line partners are shown, and our shot count with what a fishing rod "fires" left out.
using System.Linq;
using System.Text.RegularExpressions;

namespace RDR2Coop.Net
{
    public sealed class ActivityState
    {
        public const int HoldMs = 5000;   // gone this long: it's over

        public string Doing { get; private set; } = "";   // "is fishing"..., "" = nothing
        long _seenMs;

        // seen: what the script running now says we do ("" or null: none). True when Doing changed.
        public bool Update(string seen, long now)
        {
            if (!string.IsNullOrEmpty(seen))
            {
                _seenMs = now;
                if (seen == Doing) return false;
                Doing = seen;
                return true;
            }
            if (Doing.Length == 0 || now - _seenMs < HoldMs) return false;
            Doing = "";
            return true;
        }
    }

    public static class ActivityText
    {
        public const int MaxLength = 60;

        // The line comes from another player: the game's ~x~ codes and any ~ < > come out.
        public static string Clean(string text)
        {
            string s = Regex.Replace(text ?? "", "~[^~]*~", "");
            s = new string(s.Where(c => c != '~' && c != '<' && c != '>' && !char.IsControl(c)).ToArray()).Trim();
            return s.Length > MaxLength ? s.Substring(0, MaxLength) : s;
        }

        // What a partner's notice says they do: the table games send just the game's name (TableRules).
        public static string Of(string text)
        {
            string game = TableRules.Game(text);
            return game == null ? Clean(text) : "is playing " + (game == "fillet" ? "five finger fillet" : game);
        }
    }

    // A fishing rod is held like a gun: readying a cast reads as aiming and a cast can count
    // as a shot. Partners' stand-ins hold our rod, so with it in hand we send no aim and
    // keep its "shots" out of the counter (it carries on from there with real ones).
    public sealed class RodShots
    {
        byte _last, _hidden;   // the counter as last read; how many of those the rod made

        public void Apply(ref PlayerState s, bool rod)
        {
            if (rod)
            {
                s.Pose &= ~PoseFlags.Aiming;
                _hidden += (byte)(s.Shots - _last);
            }
            _last = s.Shots;
            s.Shots -= _hidden;   // wraps like the counter does
        }
    }
}
