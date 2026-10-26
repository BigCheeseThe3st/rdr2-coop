// When a copy couldn't be made (the game refused to create the ped or vehicle, or its model was slow to load), the
// model gets another try after a wait instead of being given up for the session: a busy town fills the game's limit
// on people for a while, and blacklisting the model left everyone using it invisible (Big Kitty's log: Valentine's
// townsfolk, beta.7 and beta.8). Pure, so it can be tested.
using System.Collections.Generic;

namespace RDR2Coop.Net
{
    public sealed class RetryBook
    {
        readonly Dictionary<uint, long> _until = new Dictionary<uint, long>();
        readonly Dictionary<uint, long> _logged = new Dictionary<uint, long>();

        /// <summary>Whether this model may be tried now (also forgets a wait that's over).</summary>
        public bool CanTry(uint model, long now)
        {
            if (!_until.TryGetValue(model, out long until)) return true;
            if (now < until) return false;
            _until.Remove(model);
            return true;
        }

        /// <summary>A failed try: not again for waitMs. True when it's worth a log line (once a minute per model).</summary>
        public bool Failed(uint model, long now, int waitMs)
        {
            _until[model] = now + waitMs;
            if (_logged.TryGetValue(model, out long at) && now - at < 60000) return false;
            _logged[model] = now;
            return true;
        }

        public void Clear() { _until.Clear(); _logged.Clear(); }
    }
}
