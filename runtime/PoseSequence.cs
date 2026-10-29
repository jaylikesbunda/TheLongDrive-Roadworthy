using System;
using System.Collections.Generic;

namespace TldMp
{
    // Position and velocity updates are latest-value state: an older one arriving
    // after a newer one (queues, retransmits, slow frames, receive backlog) used to
    // be applied as current and threw a passenger's car, and their view, seconds
    // back along the road. Every such message now carries a 4-byte sequence
    // trailer added at the transport and removed before the game sees it; the
    // receiver drops anything not newer than what it already applied for the same
    // sender, type and object. All peers run the same build (join fingerprint).
    // Messages the game sends reliably carry sequence 0 and are always applied:
    // they are one-off state (a part attaching to its car after an item-data
    // request) that may legitimately arrive after newer unreliable motion.
    internal static class PoseSequence
    {
        internal const int TrailerBytes = 4;
        internal const uint Unordered = 0;

        // playerPos, itemPosGlobal, itemPosLocal, velocity, carVelocity, carPosGlobal.
        internal static bool Stamped(byte type)
        {
            switch (type)
            {
                case 10: case 16: case 17: case 19: case 20: case 39: case LiveUsablePolicy.Type: return true;
                default: return false;
            }
        }

        internal static byte[] Stamp(byte[] bytes, uint sequence)
        {
            if (bytes == null || bytes.Length == 0 || !Stamped(bytes[0])) return bytes;
            var stamped = new byte[bytes.Length + TrailerBytes];
            Buffer.BlockCopy(bytes, 0, stamped, 0, bytes.Length);
            int at = bytes.Length;
            stamped[at] = (byte)sequence;
            stamped[at + 1] = (byte)(sequence >> 8);
            stamped[at + 2] = (byte)(sequence >> 16);
            stamped[at + 3] = (byte)(sequence >> 24);
            return stamped;
        }

        // Unstamped types pass through unchanged (stamped=false).
        internal static bool TryUnstamp(byte[] bytes, out byte[] payload, out uint sequence, out bool stamped)
        {
            payload = bytes;
            sequence = 0;
            stamped = false;
            if (bytes == null || bytes.Length == 0 || !Stamped(bytes[0])) return true;
            if (bytes.Length <= TrailerBytes) return false;
            int at = bytes.Length - TrailerBytes;
            sequence = (uint)(bytes[at] | bytes[at + 1] << 8 | bytes[at + 2] << 16 | bytes[at + 3] << 24);
            payload = new byte[at];
            Buffer.BlockCopy(bytes, 0, payload, 0, at);
            stamped = true;
            return true;
        }

        // Ordering key inside one sender: message type plus the object it describes
        // (a player position describes the sender's own player).
        internal static ulong Key(byte[] payload)
        {
            if (payload == null || payload.Length == 0) return 0;
            ulong live; int turn;
            if (LiveUsablePolicy.TryRead(payload, out live, out turn)) return live;
            ulong item = payload[0] != 10 && payload.Length >= 5 ? BitConverter.ToUInt32(payload, 1) : 0u;
            // World and car-relative poses of one item share one order, so a late
            // world pose never overrides a newer car-relative one (CarCargo).
            byte type = payload[0] == 17 ? (byte)16 : payload[0];
            return (ulong)type << 32 | item;
        }

        // Serial-number comparison, safe across the 32-bit wrap.
        internal static bool Newer(uint candidate, uint newest)
        {
            return (int)(candidate - newest) > 0;
        }
    }

    // Sender side: strictly increasing, and seeded from the clock so a restarted
    // game never sends numbers below the ones its previous session used.
    internal sealed class PoseSequenceCounter
    {
        uint last;
        bool started;

        internal uint Next(uint clockMs)
        {
            last = !started || PoseSequence.Newer(clockMs, last) ? clockMs : last + 1;
            if (last == PoseSequence.Unordered) last = 1;
            started = true;
            return last;
        }
    }

    // Maps a sender's send clock (the sequence, in milliseconds) onto the local
    // clock. Poses used to be timed by arrival: after a hitch a burst of queued
    // poses arrived in one frame with one timestamp, and interpolation picked a
    // pose from seconds earlier (the passenger's car jumped hundreds of metres
    // back, then forward). Timed by send clock, a burst keeps its real spacing.
    // Offset is the smallest arrival-minus-send seen (the fastest delivery), let
    // rise slowly for clock drift; lateness is how much later than that a pose came.
    internal sealed class SenderClock
    {
        internal const double RelaxPerSecond = 0.002;

        sealed class State
        {
            internal uint LastMs;
            internal double LastSeconds, Offset, LastLocal;
        }

        readonly Dictionary<ulong, State> states = new Dictionary<ulong, State>();

        internal double Map(ulong sender, uint senderMs, double localNow, out double lateness)
        {
            State s;
            if (!states.TryGetValue(sender, out s))
            {
                s = new State { LastMs = senderMs, LastSeconds = 0, Offset = localNow, LastLocal = localNow };
                states[sender] = s;
                lateness = 0;
                return localNow;
            }
            double senderSeconds = s.LastSeconds + (int)(senderMs - s.LastMs) / 1000.0;
            if (PoseSequence.Newer(senderMs, s.LastMs))
            {
                s.LastMs = senderMs;
                s.LastSeconds = senderSeconds;
            }
            double relaxed = s.Offset + RelaxPerSecond * Math.Max(0, localNow - s.LastLocal);
            double candidate = localNow - senderSeconds;
            s.Offset = candidate < relaxed ? candidate : relaxed;
            s.LastLocal = localNow;
            double mapped = senderSeconds + s.Offset;
            lateness = localNow - mapped;
            return mapped;
        }

        internal void Forget(ulong sender) { states.Remove(sender); }

        internal void Clear() { states.Clear(); }
    }

    // Receiver side: newest sequence applied per sender and key.
    internal sealed class PoseSequenceFilter
    {
        internal const int MaxKeysPerSender = 8192;
        readonly Dictionary<ulong, Dictionary<ulong, uint>> newest = new Dictionary<ulong, Dictionary<ulong, uint>>();
        internal long Passed, Stale;

        internal bool Accept(ulong sender, ulong key, uint sequence)
        {
            if (sequence == PoseSequence.Unordered)
            {
                Passed++;
                return true;
            }
            Dictionary<ulong, uint> keys;
            if (!newest.TryGetValue(sender, out keys))
            {
                keys = new Dictionary<ulong, uint>();
                newest[sender] = keys;
            }
            uint last;
            if (keys.TryGetValue(key, out last) && !PoseSequence.Newer(sequence, last))
            {
                Stale++;
                return false;
            }
            if (keys.Count >= MaxKeysPerSender && !keys.ContainsKey(key)) keys.Clear();
            keys[key] = sequence;
            Passed++;
            return true;
        }

        internal void Forget(ulong sender) { newest.Remove(sender); }

        internal void Clear() { newest.Clear(); }
    }
}
