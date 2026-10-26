// The "going to the host" prompt (the author, 25 Oct: "mission party by default but you have
// to decline a teleport with a 30 second timer"). A party member far from the host isn't moved
// at once: for AskMs the screen says "going to <host> in N s, press N to stay"; N holds the
// move off for StayMs, Y goes now, silence goes when the timer runs out. Pure: no game calls.
namespace RDR2Coop.Net
{
    public sealed class FollowPrompt
    {
        public const int AskMs = 30_000, StayMs = 5 * 60_000;

        long _askedAt = -1, _stayUntil = -1;

        public bool Asking => _askedAt >= 0;
        public bool Staying(long now) => now < _stayUntil;

        /// <summary>Seconds left on the question, for the subtitle.</summary>
        public int SecondsLeft(long now) => (int)((AskMs - (now - _askedAt) + 999) / 1000);

        /// <summary>Each tick while a move is wanted: true when it's time to go. Not wanted: the question is dropped.</summary>
        public bool Tick(bool wanted, long now)
        {
            if (!wanted) { _askedAt = -1; return false; }
            if (Staying(now)) return false;
            if (_askedAt < 0) { _askedAt = now; return false; }
            if (now - _askedAt < AskMs) return false;
            _askedAt = -1;
            return true;
        }

        /// <summary>N: stay where I am for StayMs. True when there was a question to answer.</summary>
        public bool Decline(long now)
        {
            if (!Asking) return false;
            _askedAt = -1; _stayUntil = now + StayMs;
            return true;
        }

        /// <summary>Y: go now. True when there was a question to answer.</summary>
        public bool Accept()
        {
            if (!Asking) return false;
            _askedAt = -1; _stayUntil = -1;
            return true;
        }

        public void Reset() { _askedAt = -1; _stayUntil = -1; }
    }
}
