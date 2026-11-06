using System;

namespace TldMp.Experimental
{
    // Host session thread only. Snapshot data and acknowledgements must be
    // authenticated/validated by the caller; this class does not move physics.
    //
    // Two forms exist. The legacy constructor adopts no peer session and binds no
    // digest; it exists for isolated tests. The bound form requires both peer
    // sessions, an authenticated nonce per step, a 32-byte digest of the source's
    // applied final state, and a deadline after which the transfer aborts and
    // unfreezes instead of leaving authority stranded.
    internal sealed class Handoff
    {
        readonly ulong transaction, root, graphRevision, oldOwner, newOwner;
        readonly ulong[] ids, epochs;
        readonly int limit;
        readonly PeerSession sourceSession, targetSession;
        readonly byte[] sourceDigest;
        readonly double deadline;
        bool sourceReady, targetReady, finished;
        AuthorityRegistry frozenAuthority;

        internal Handoff(ulong transactionId, ulong rootId, ulong revision, ulong[] group, ulong[] ownerEpochs,
            ulong from, ulong to, int maxEntities)
            : this(transactionId, rootId, revision, group, ownerEpochs, from, to, maxEntities, null, null, null, 0) { }

        internal Handoff(ulong transactionId, ulong rootId, ulong revision, ulong[] group, ulong[] ownerEpochs,
            ulong from, ulong to, int maxEntities, PeerSession source, PeerSession target,
            byte[] expectedDigest, double expiresAt)
        {
            if (transactionId == 0 || rootId == 0 || group == null || ownerEpochs == null || group.Length == 0 ||
                group.Length != ownerEpochs.Length || maxEntities < group.Length || from == 0 || to == 0 || from == to)
                throw new ArgumentException("Invalid handoff snapshot.");
            if (expectedDigest != null && expectedDigest.Length != 32)
                throw new ArgumentException("A handoff digest must be 32 bytes.");
            if (Double.IsNaN(expiresAt) || Double.IsInfinity(expiresAt))
                throw new ArgumentException("Invalid handoff deadline.");
            if ((source == null) != (target == null))
                throw new ArgumentException("Both peer sessions are required.");
            transaction = transactionId; root = rootId; graphRevision = revision; oldOwner = from; newOwner = to; limit = maxEntities;
            ids = (ulong[])group.Clone(); epochs = (ulong[])ownerEpochs.Clone();
            sourceSession = source; targetSession = target; deadline = expiresAt;
            sourceDigest = expectedDigest == null ? null : (byte[])expectedDigest.Clone();
        }

        internal bool IsFinished { get { return finished; } }
        internal ulong Transaction { get { return transaction; } }

        // Call only after validating and recording the source's final state and
        // stopping its simulator. Freeze rejects late old-owner motion at host.
        internal bool PreparedBy(ulong peer, ulong transactionId, EntityGraph graph, AuthorityRegistry authority)
        {
            return PreparedByCore(peer, 0, false, transactionId, graph, authority);
        }

        internal bool PreparedBy(ulong peer, ulong nonce, ulong transactionId, EntityGraph graph, AuthorityRegistry authority)
        {
            return PreparedByCore(peer, nonce, true, transactionId, graph, authority);
        }

        bool PreparedByCore(ulong peer, ulong nonce, bool authenticated, ulong transactionId,
            EntityGraph graph, AuthorityRegistry authority)
        {
            if (finished || peer != oldOwner || transactionId != transaction || authority == null) return false;
            if (!Authenticated(sourceSession, peer, nonce, authenticated)) return false;
            if (sourceReady) return ReferenceEquals(authority, frozenAuthority);
            if (!GraphTransfer.Matches(graph, root, graphRevision, ids, limit)) return false;
            if (!authority.FreezeGroup(ids, epochs, oldOwner, transaction)) return false;
            frozenAuthority = authority; sourceReady = true; return true;
        }

        internal bool ReadyAt(ulong peer, ulong transactionId)
        {
            return ReadyAtCore(peer, 0, false, transactionId, null, 0);
        }

        // The target must echo the digest of the snapshot it actually applied.
        // A mismatched or missing digest leaves the transfer unready so the host
        // can retry or abort; it never commits unseen state.
        internal bool ReadyAt(ulong peer, ulong nonce, ulong transactionId, byte[] appliedDigest, double now)
        {
            return ReadyAtCore(peer, nonce, true, transactionId, appliedDigest, now);
        }

        bool ReadyAtCore(ulong peer, ulong nonce, bool authenticated, ulong transactionId, byte[] appliedDigest, double now)
        {
            if (finished || peer != newOwner || transactionId != transaction || !sourceReady) return false;
            if (!Authenticated(targetSession, peer, nonce, authenticated)) return false;
            if (ExpiredAt(now)) { Abort(); return false; }
            if (sourceDigest != null)
            {
                if (appliedDigest == null || appliedDigest.Length != sourceDigest.Length || !Equal(sourceDigest, appliedDigest)) return false;
            }
            else if (appliedDigest != null) return false;
            targetReady = true; return true;
        }

        internal bool Commit(EntityGraph graph, AuthorityRegistry authority)
        {
            return Commit(graph, authority, 0);
        }

        internal bool Commit(EntityGraph graph, AuthorityRegistry authority, double now)
        {
            if (finished || !sourceReady || !targetReady || !ReferenceEquals(authority, frozenAuthority)) return false;
            if (ExpiredAt(now)) { Abort(); return false; }
            finished = true;
            bool ok = false;
            try
            {
                ok = GraphTransfer.Matches(graph, root, graphRevision, ids, limit) &&
                    authority.CommitFrozenGroup(ids, epochs, oldOwner, newOwner, transaction);
                return ok;
            }
            finally { if (!ok) authority.UnfreezeGroup(ids, transaction); }
        }

        // Host tick: expire an overdue transfer so a missing target cannot leave
        // the source frozen forever. Returns true when this call aborted it.
        internal bool Tick(double now)
        {
            if (finished || !ExpiredAt(now)) return false;
            Abort();
            return true;
        }

        internal void Abort()
        {
            if (finished) return;
            finished = true;
            if (frozenAuthority != null) frozenAuthority.UnfreezeGroup(ids, transaction);
        }

        static bool ValidTime(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }

        bool ExpiredAt(double now)
        {
            return deadline > 0 && (!ValidTime(now) || now >= deadline);
        }

        // A bound transfer accepts only the authenticated overload; an unbound
        // transfer accepts only the legacy one, so a caller cannot claim proof it
        // does not have or silently skip authentication.
        static bool Authenticated(PeerSession session, ulong peer, ulong nonce, bool supplied)
        {
            if (session == null) return !supplied;
            return supplied && session.Accepts(peer, nonce);
        }

        static bool Equal(byte[] expected, byte[] actual)
        {
            int difference = 0;
            for (int i = 0; i < expected.Length; i++) difference |= expected[i] ^ actual[i];
            return difference == 0;
        }
    }
}
