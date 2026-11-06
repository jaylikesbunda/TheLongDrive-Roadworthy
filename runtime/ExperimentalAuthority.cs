using System;
using System.Collections.Generic;

namespace TldMp.Experimental
{
    // Host-side bookkeeping only. It does not touch the game or send messages.
    // IDs and epochs are session-scoped; no entity may be resurrected after removal.
    internal sealed class AuthorityRegistry
    {
        internal sealed class Entry
        {
            internal readonly ulong Id;
            internal ulong Owner;
            internal ulong Epoch;
            internal bool Removed;
            internal ulong FrozenBy;
            internal Entry(ulong id, ulong owner) { Id = id; Owner = owner; Epoch = 1; }
        }

        readonly Dictionary<ulong, Entry> entries = new Dictionary<ulong, Entry>();
        internal int Count { get { return entries.Count; } }
        internal bool Contains(ulong id) { Entry e; return entries.TryGetValue(id, out e) && !e.Removed; }
        internal bool IsRemoved(ulong id) { Entry e; return entries.TryGetValue(id, out e) && e.Removed; }
        internal bool Add(ulong id, ulong owner)
        {
            if (id == 0 || owner == 0 || entries.ContainsKey(id)) return false;
            entries.Add(id, new Entry(id, owner));
            return true;
        }
        internal bool Accepts(ulong id, ulong sender, ulong epoch)
        {
            Entry e;
            return sender != 0 && entries.TryGetValue(id, out e) && !e.Removed && e.FrozenBy == 0 && e.Owner == sender && e.Epoch == epoch;
        }
        internal bool TryEpoch(ulong id, ulong owner, out ulong epoch)
        {
            epoch = 0;
            Entry e;
            if (owner == 0 || !entries.TryGetValue(id, out e) || e.Removed || e.Owner != owner) return false;
            epoch = e.Epoch;
            return true;
        }
        // Read-only owner lookup for callers that must reproduce the current
        // (owner, epoch) pair, e.g. to build an expected-epoch transfer.
        internal bool TryOwner(ulong id, out ulong owner, out ulong epoch)
        {
            owner = 0; epoch = 0;
            Entry e;
            if (!entries.TryGetValue(id, out e) || e.Removed) return false;
            owner = e.Owner;
            epoch = e.Epoch;
            return true;
        }
        // Caller must validate the claim and recipient readiness. The expected epoch
        // makes repeated or concurrent decisions fail rather than transferring twice.
        internal bool Transfer(ulong id, ulong expectedEpoch, ulong newOwner, out ulong epoch)
        {
            epoch = 0;
            Entry e;
            if (newOwner == 0 || !entries.TryGetValue(id, out e) || e.Removed || e.FrozenBy != 0 ||
                e.Epoch != expectedEpoch || e.Owner == newOwner || e.Epoch == UInt64.MaxValue) return false;
            e.Owner = newOwner;
            epoch = ++e.Epoch;
            return true;
        }
        internal bool Remove(ulong id, ulong expectedEpoch)
        {
            Entry e;
            if (!entries.TryGetValue(id, out e) || e.Removed || e.FrozenBy != 0 || e.Epoch != expectedEpoch) return false;
            e.Removed = true;
            return true;
        }

        // All-or-nothing transfer for a caller-supplied, already validated graph.
        // Epochs are per entity; callers must obtain a stable graph snapshot before
        // invoking this method and must not infer readiness from this bookkeeping.
        internal bool TransferGroup(ulong[] ids, ulong[] epochs, ulong oldOwner, ulong newOwner)
        {
            if (ids == null || epochs == null || ids.Length == 0 || ids.Length != epochs.Length ||
                oldOwner == 0 || newOwner == 0 || oldOwner == newOwner) return false;
            var seen = new HashSet<ulong>();
            for (int i = 0; i < ids.Length; i++)
            {
                Entry e;
                if (!seen.Add(ids[i]) || !entries.TryGetValue(ids[i], out e) || e.Removed ||
                    e.Owner != oldOwner || e.Epoch != epochs[i] || e.Epoch == UInt64.MaxValue || e.FrozenBy != 0) return false;
            }
            for (int i = 0; i < ids.Length; i++)
            {
                Entry e = entries[ids[i]];
                e.Owner = newOwner;
                e.Epoch++;
            }
            return true;
        }

        internal bool FreezeGroup(ulong[] ids, ulong[] epochs, ulong owner, ulong transaction)
        {
            if (ids == null || epochs == null || ids.Length == 0 || ids.Length != epochs.Length || owner == 0 || transaction == 0) return false;
            var seen = new HashSet<ulong>();
            for (int i = 0; i < ids.Length; i++)
            {
                Entry e;
                if (!seen.Add(ids[i]) || !entries.TryGetValue(ids[i], out e) || e.Removed || e.FrozenBy != 0 || e.Owner != owner || e.Epoch != epochs[i]) return false;
            }
            for (int i = 0; i < ids.Length; i++) entries[ids[i]].FrozenBy = transaction;
            return true;
        }
        internal void UnfreezeGroup(ulong[] ids, ulong transaction)
        {
            if (ids == null || transaction == 0) return;
            for (int i = 0; i < ids.Length; i++) { Entry e; if (entries.TryGetValue(ids[i], out e) && e.FrozenBy == transaction) e.FrozenBy = 0; }
        }
        internal bool CommitFrozenGroup(ulong[] ids, ulong[] epochs, ulong oldOwner, ulong newOwner, ulong transaction)
        {
            if (ids == null || epochs == null || ids.Length == 0 || ids.Length != epochs.Length || transaction == 0 || newOwner == 0 || oldOwner == 0 || newOwner == oldOwner) return false;
            var seen = new HashSet<ulong>();
            for (int i = 0; i < ids.Length; i++)
            {
                Entry e;
                if (!seen.Add(ids[i]) || !entries.TryGetValue(ids[i], out e) || e.Removed || e.FrozenBy != transaction || e.Owner != oldOwner || e.Epoch != epochs[i] || e.Epoch == UInt64.MaxValue) return false;
            }
            for (int i = 0; i < ids.Length; i++) { Entry e = entries[ids[i]]; e.Owner = newOwner; e.Epoch++; e.FrozenBy = 0; }
            return true;
        }
    }
}
