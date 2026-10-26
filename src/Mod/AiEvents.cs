// The game's AI event queue (SCRIPT_EVENT_QUEUE_AI), read once a tick for everyone who looks in it
// (Explosions, DoorSync). Each native call is a round trip to the game thread, and the queue is
// fullest in a gunfight, when frames matter most. An event lasts one frame, so the list is new each tick.
using System;
using System.Collections.Generic;
using RDR2.Native;

namespace RDR2Coop.Mod
{
    static class AiEvents
    {
        public const int Queue = 0;   // SCRIPT_EVENT_QUEUE_AI: GET_EVENT_DATA takes it with an index into Now's list
        const int Max = 128;
        static readonly List<uint> Hashes = new List<uint>();
        static long _readMs = -1;

        // This tick's events: each one's hash, by its index in the queue. now: the tick's clock.
        // shortcut: keyed by the tick's millisecond, so two ticks within 1 ms (over 1000 fps) would share a list; key it by a tick count if that ever happens.
        public static List<uint> Now(long now)
        {
            if (now == _readMs) return Hashes;
            _readMs = now;
            Hashes.Clear();
            int n = Math.Min(SCRIPTS.GET_NUMBER_OF_EVENTS(Queue), Max);
            for (int i = 0; i < n; i++) Hashes.Add(SCRIPTS.GET_EVENT_AT_INDEX(Queue, i));
            return Hashes;
        }
    }
}
