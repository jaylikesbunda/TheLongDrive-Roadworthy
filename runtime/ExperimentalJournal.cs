using System;
using System.Collections.Generic;

namespace TldMp.Experimental
{
    // Events recorded after a snapshot barrier, owned by one session thread.
    // An overflow invalidates the entire journal: caller must restart the join.
    internal sealed class SnapshotJournal
    {
        readonly int maxEntries, maxBytes;
        readonly List<byte[]> events = new List<byte[]>();
        ulong revision;
        int bytes;
        bool failed;
        internal SnapshotJournal(ulong snapshotRevision, int entryLimit, int byteLimit)
        {
            if (snapshotRevision == 0 || entryLimit <= 0 || byteLimit <= 0) throw new ArgumentException("Invalid journal limits.");
            revision = snapshotRevision; maxEntries = entryLimit; maxBytes = byteLimit;
        }
        internal bool Append(ulong nextRevision, byte[] payload)
        {
            if (failed) return false;
            if (payload == null || payload.Length == 0 || revision == ulong.MaxValue || nextRevision != revision + 1)
            {
                failed = true; events.Clear(); bytes = 0; return false;
            }
            if (events.Count >= maxEntries || payload.Length > maxBytes - bytes)
            {
                failed = true; events.Clear(); bytes = 0; return false;
            }
            events.Add((byte[])payload.Clone()); bytes += payload.Length; revision = nextRevision;
            return true;
        }
        internal bool TryCopy(out ulong finalRevision, out byte[][] result)
        {
            finalRevision = 0; result = null;
            if (failed) return false;
            result = new byte[events.Count][];
            for (int i = 0; i < events.Count; i++) result[i] = (byte[])events[i].Clone();
            finalRevision = revision; return true;
        }
    }
}
