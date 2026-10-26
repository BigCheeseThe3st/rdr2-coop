// Guests' own story missions are blocked (the author, 25 Oct: "guests own mission activations
// are just straight up blocked"). Only the host's story is the shared one, and a guest placed
// next to the host at a mission marker would otherwise start their own copy of the same
// mission on top of it (two sets of NPCs, bug #4). While we're a guest, the start point of
// every entry in the story scripts' mission table is moved far below the map, so the game's
// mission launcher never finds the player at one; the real points come back when we host
// again, leave the server, or the mod is reloaded. [Gameplay] OwnMissionsAsGuest=Yes keeps
// a guest's own missions. The table is the one MissionMarkers reads (checked against this
// game build by MissionProbe); nothing else in it is touched.
using System.Collections.Generic;
using RDR2;
using RDR2Coop.Net;

namespace RDR2Coop.Mod
{
    sealed class MissionBlock
    {
        const float Away = -9000f;      // z well under the map
        const int RescanMs = 2000;      // the scripts rewrite an entry when a mission becomes available

        readonly Dictionary<int, (float x, float y, float z)> _saved = new Dictionary<int, (float, float, float)>();
        bool _on, _warned;
        long _lastScan;

        public bool On => _on;

        // Every tick: block while we're a guest, restore as soon as we're not.
        public void Tick(bool guest, long now)
        {
            if (!MissionProbe.GlobalsOk)
            {
                if (guest && !_warned) { _warned = true; CoopScript.Log("own missions NOT blocked as a guest: the script globals don't match this game build, so a mission started next to the host runs twice"); }
                return;
            }
            if (guest) { if (!_on || now - _lastScan > RescanMs) { _lastScan = now; Block(); } }
            else if (_on) Restore();
        }

        unsafe void Block()
        {
            int count = System.Math.Max(0, System.Math.Min(MissionProbe.GInt(MissionMarkers.Table), 128));
            int moved = 0;
            for (int i = 0; i < count; i++)
            {
                int b = MissionMarkers.Table + 1 + MissionMarkers.EntrySize * i + MissionMarkers.StartField;
                float z = MissionProbe.GFloat(b + 2);
                if (z == Away) continue;   // already ours
                _saved[i] = (MissionProbe.GFloat(b), MissionProbe.GFloat(b + 1), z);   // the game's current point (it may have been rewritten)
                Set(b, 0f); Set(b + 1, 0f); Set(b + 2, Away);
                moved++;
            }
            if (!_on || moved > 0) CoopScript.Log($"own missions blocked as a guest ({moved} start points moved, {_saved.Count} held)");
            _on = true;
        }

        public unsafe void Restore()
        {
            foreach (var kv in _saved)
            {
                int b = MissionMarkers.Table + 1 + MissionMarkers.EntrySize * kv.Key + MissionMarkers.StartField;
                if (MissionProbe.GFloat(b + 2) != Away) continue;   // the game rewrote it meanwhile: its value wins
                Set(b, kv.Value.x); Set(b + 1, kv.Value.y); Set(b + 2, kv.Value.z);
            }
            if (_on) CoopScript.Log($"own missions back ({_saved.Count} start points restored)");
            _saved.Clear();
            _on = false;
        }

        static unsafe void Set(int global, float value)
        {
            var p = (float*)Game.GetGlobalPtr(global).MemoryAddress;
            if (p != null) *p = value;
        }
    }
}
