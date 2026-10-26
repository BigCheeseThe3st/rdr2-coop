// Time of day and weather follow the host: the host's game sends its clock and
// weather every few seconds; a guest sets its clock to match (the game clock is
// the same everywhere) and takes the host's weather while it shows the host's
// area (weather is regional: off on their own, guests keep their own).
using System;
using System.Globalization;
using RDR2.Native;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class EnvSync
    {
        const int SendEveryMs = 5000;
        const int ClockSlackS = 90;   // closer than this: leave the clock alone (no jumps for a few seconds' lag)

        long _lastSentMs = -SendEveryMs;
        bool _loggedClock, _loggedWeather;

        public unsafe void HostTick(ServerLink link, long now)
        {
            if (now - _lastSentMs < SendEveryMs) return;
            _lastSentMs = now;
            uint w1 = 0, w2 = 0;
            float mix = 0f;
            MISC.GET_CURR_WEATHER_STATE(&w1, &w2, &mix);
            link.SendNotice(Protocol.Broadcast, NoticeKind.Clock, string.Join(" ",
                CLOCK.GET_CLOCK_HOURS(), CLOCK.GET_CLOCK_MINUTES(), CLOCK.GET_CLOCK_SECONDS(),
                w1, w2, mix.ToString("0.###", CultureInfo.InvariantCulture)));
        }

        // From the host. inHostsArea: we're showing the host's area (their weather is ours).
        public void OnClock(string text, bool inHostsArea)
        {
            if (Activities.Busy) return;   // mini-games run the clock their own way (slower): it catches up after
            var f = (text ?? "").Split(' ');
            if (f.Length < 6 || !int.TryParse(f[0], out int h) || !int.TryParse(f[1], out int m) || !int.TryParse(f[2], out int s)
                || h < 0 || h > 23 || m < 0 || m > 59 || s < 0 || s > 59) return;
            int theirs = h * 3600 + m * 60 + s;
            int ours = CLOCK.GET_CLOCK_HOURS() * 3600 + CLOCK.GET_CLOCK_MINUTES() * 60 + CLOCK.GET_CLOCK_SECONDS();
            int diff = Math.Abs(theirs - ours);
            if (Math.Min(diff, 86400 - diff) > ClockSlackS)
            {
                CLOCK.SET_CLOCK_TIME(h, m, s);
                if (!_loggedClock) { _loggedClock = true; CoopScript.Log($"clock set to the host's: {h:00}:{m:00}"); }
            }

            if (!inHostsArea || !uint.TryParse(f[3], out uint w1) || !uint.TryParse(f[4], out uint w2)
                || !float.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float mix)) return;
            MISC.SET_CURR_WEATHER_STATE(w1, w2, Math.Max(0f, Math.Min(1f, mix)), true);
            if (!_loggedWeather) { _loggedWeather = true; CoopScript.Log($"weather follows the host's ({w1:X8} -> {w2:X8})"); }
        }
    }
}
