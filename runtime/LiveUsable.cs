using System;
using System.Collections.Generic;

namespace TldMp
{
    // Live doors, windows, mirrors, seats and switches. The game sends a usable
    // (msgType.usable, 40) reliably on every change, so dragging a door queues a
    // reliable message per frame and the other players see it move late and in
    // steps. The message is absolute state (rotations, slide, turn state, tuning),
    // so while the turn state is unchanged each update goes unreliably as latest
    // value per usable (coalesced and sequence-ordered); a turn-state change (a
    // switch, a key) keeps the game's reliable send, and QuietSeconds after the
    // last change the final state is sent reliably once. Unity-free.
    internal static class LiveUsablePolicy
    {
        internal const byte Type = 40;
        internal const double QuietSeconds = 0.3;
        internal const int MaxEntries = 4096;

        // item: bool(0) uint id, int usable, float xRot, float yRot, float slide, int turn, float tuned
        // poi:  bool(1) int gen, Vector3d poi, int usable, float, float, float, int turn, float
        const int ItemLength = 30, ItemIdentityEnd = 10, ItemTurn = 22;
        const int PoiLength = 54, PoiIdentityEnd = 34, PoiTurn = 46;

        internal static bool TryRead(byte[] b, out ulong key, out int turnState)
        {
            key = 0;
            turnState = 0;
            if (b == null || b.Length < 2 || b[0] != Type) return false;
            int identityEnd, turn;
            if (b[1] == 0 && b.Length == ItemLength) { identityEnd = ItemIdentityEnd; turn = ItemTurn; }
            else if (b[1] != 0 && b.Length == PoiLength) { identityEnd = PoiIdentityEnd; turn = PoiTurn; }
            else return false;
            key = IdentityKey(b, identityEnd);
            turnState = BitConverter.ToInt32(b, turn);
            return true;
        }

        // FNV-1a over the identity bytes, tagged so it never equals a
        // StablePackets.StreamKey ((type << 32) | id).
        internal static ulong IdentityKey(byte[] b, int identityEnd)
        {
            ulong hash = 14695981039346656037UL;
            for (int i = 1; i < identityEnd && i < b.Length; i++)
            {
                hash ^= b[i];
                unchecked { hash *= 1099511628211UL; }
            }
            return 0x8000000000000000UL | ((ulong)Type << 48) | (hash & 0xFFFFFFFFFFFFUL);
        }
    }

    internal sealed class LiveUsableSender
    {
        sealed class Entry
        {
            internal byte[] Last;
            internal int Turn;
            internal double ChangedAt;
            internal bool SettlePending;
        }

        readonly Dictionary<ulong, Dictionary<ulong, Entry>> peers = new Dictionary<ulong, Dictionary<ulong, Entry>>();
        internal long Live, Reliable, Settled;

        // True: send this message live (unreliable, coalesced with liveKey).
        // False: send it as the game would (reliably).
        internal bool Offer(ulong peer, byte[] bytes, double now, out ulong liveKey)
        {
            int turn;
            if (!LiveUsablePolicy.TryRead(bytes, out liveKey, out turn)) { Reliable++; return false; }
            Dictionary<ulong, Entry> entries;
            if (!peers.TryGetValue(peer, out entries)) { entries = new Dictionary<ulong, Entry>(); peers[peer] = entries; }
            Entry e;
            if (!entries.TryGetValue(liveKey, out e))
            {
                if (entries.Count >= LiveUsablePolicy.MaxEntries) entries.Clear();
                entries[liveKey] = new Entry { Last = bytes, Turn = turn, ChangedAt = now, SettlePending = false };
                Reliable++;
                return false;
            }
            e.Last = bytes;
            e.ChangedAt = now;
            if (e.Turn != turn)
            {
                e.Turn = turn;
                e.SettlePending = false;
                Reliable++;
                return false;
            }
            e.SettlePending = true;
            Live++;
            return true;
        }

        // Final states that have been still for QuietSeconds, to send reliably.
        internal void TakeSettles(double now, List<KeyValuePair<ulong, byte[]>> due)
        {
            foreach (var peer in peers)
                foreach (var e in peer.Value.Values)
                    if (e.SettlePending && now - e.ChangedAt >= LiveUsablePolicy.QuietSeconds)
                    {
                        e.SettlePending = false;
                        due.Add(new KeyValuePair<ulong, byte[]>(peer.Key, e.Last));
                        Settled++;
                    }
        }

        internal void Drop(ulong peer) { peers.Remove(peer); }

        internal void Clear() { peers.Clear(); }

        internal string TakeSummary()
        {
            string text = "liveUsables{live=" + Live + " reliable=" + Reliable + " settled=" + Settled + "}";
            Live = Reliable = Settled = 0;
            return text;
        }
    }
}
