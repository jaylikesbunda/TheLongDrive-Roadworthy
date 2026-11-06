using System;
using System.Threading;

namespace TldMp.Experimental
{
    // Single owner of the graph, authority and identity stores. Callers must not
    // mutate those stores directly once a coordinator owns them, otherwise the
    // lifecycle invariants below stop holding. Every mutation and read of mutable
    // state runs on the thread that constructed the coordinator.
    //
    // Allocation is fail-closed: an ID is never reused after removal, live and
    // tombstone counts are bounded, and an unexpected partial store update marks
    // the coordinator as requiring a session rekey instead of continuing.
    internal sealed class EntityCoordinator
    {
        readonly EntityGraph graph = new EntityGraph();
        readonly AuthorityRegistry authority = new AuthorityRegistry();
        readonly IdentityMap identity = new IdentityMap();
        readonly int ownerThread;
        readonly int maxEntities, maxTombstones;
        readonly bool isHost;
        int live, tombstones;
        bool requiresRekey;
        Handoff activeHandoff;

        internal EntityCoordinator(bool localIsHost, int entityLimit, int tombstoneLimit)
        {
            if (entityLimit <= 0 || tombstoneLimit <= 0) throw new ArgumentException("Invalid coordinator limits.");
            isHost = localIsHost;
            maxEntities = entityLimit;
            maxTombstones = tombstoneLimit;
            ownerThread = Thread.CurrentThread.ManagedThreadId;
        }

        internal bool IsHost { get { AssertThread(); return isHost; } }
        internal int LiveCount { get { AssertThread(); return live; } }
        internal int TombstoneCount { get { AssertThread(); return tombstones; } }
        internal int EntityLimit { get { AssertThread(); return maxEntities; } }
        internal int TombstoneLimit { get { AssertThread(); return maxTombstones; } }
        internal bool RequiresRekey { get { AssertThread(); return requiresRekey; } }
        internal ulong GraphRevision { get { AssertThread(); return graph.Revision; } }
        internal bool HasActiveHandoff { get { AssertThread(); return activeHandoff != null; } }

        void AssertThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("Experimental session state accessed off its owning thread.");
        }

        // Registers a root entity in all three stores or in none. A tombstoned ID
        // can never be spawned again; session identity is never resurrected.
        internal bool Spawn(ulong id, ulong owner) { return SpawnCore(id, 0, owner); }

        internal bool Spawn(ulong id, ulong parent, ulong owner)
        {
            if (parent == 0) return false;
            return SpawnCore(id, parent, owner);
        }

        bool SpawnCore(ulong id, ulong parent, ulong owner)
        {
            AssertThread();
            if (!isHost || requiresRekey || id == 0 || owner == 0 || live >= maxEntities) return false;
            if (Reserved(id)) return false;
            if (parent != 0 && (parent == id || !graph.Contains(parent))) return false;
            if (!graph.Add(id) || !authority.Add(id, owner) || !identity.Register(id))
            {
                requiresRekey = true;
                return false;
            }
            if (parent != 0 && !graph.Attach(id, parent))
            {
                requiresRekey = true;
                return false;
            }
            live++;
            return true;
        }

        // A removed ID stays reserved in every store; Check Reserved rejects reuse.
        bool Reserved(ulong id)
        {
            return graph.Contains(id) || graph.IsRemoved(id) || authority.Contains(id) ||
                authority.IsRemoved(id) || identity.Knows(id) || identity.IsRemoved(id);
        }

        // Structural re-parenting only; ownership does not move with it.
        internal bool Attach(ulong child, ulong parent)
        {
            AssertThread();
            if (!isHost || requiresRekey || child == 0 || parent == 0) return false;
            return graph.Attach(child, parent);
        }

        internal bool Detach(ulong child)
        {
            AssertThread();
            if (!isHost || requiresRekey) return false;
            return graph.Detach(child);
        }

        // Removes a leaf from all three stores. Parents must be detached first so
        // no live child can reference a tombstone. Tombstone exhaustion is a
        // session-ending condition, not a reason to forget the ID.
        internal bool Remove(ulong id, ulong expectedEpoch)
        {
            AssertThread();
            if (!isHost || requiresRekey || id == 0) return false;
            if (tombstones >= maxTombstones) { requiresRekey = true; return false; }
            if (!graph.Contains(id) || !identity.Knows(id) || graph.HasChildren(id)) return false;
            if (!authority.Remove(id, expectedEpoch)) return false;
            if (!graph.RemoveLeaf(id) || !identity.Remove(id))
            {
                requiresRekey = true;
                return false;
            }
            live--;
            tombstones++;
            return true;
        }

        internal bool TryEpoch(ulong id, ulong owner, out ulong epoch)
        {
            AssertThread();
            if (requiresRekey) { epoch = 0; return false; }
            return authority.TryEpoch(id, owner, out epoch);
        }

        internal bool Accepts(ulong id, ulong sender, ulong epoch)
        {
            AssertThread();
            if (requiresRekey) return false;
            return authority.Accepts(id, sender, epoch);
        }

        internal bool TryOwner(ulong id, out ulong owner, out ulong epoch)
        {
            AssertThread();
            owner = 0; epoch = 0;
            if (requiresRekey) return false;
            return authority.TryOwner(id, out owner, out epoch);
        }

        // Single-entity ownership move. The expected epoch makes a stale caller
        // fail instead of moving a car the host has already re-granted.
        internal bool Transfer(ulong id, ulong expectedEpoch, ulong newOwner, out ulong epoch)
        {
            AssertThread();
            epoch = 0;
            if (!isHost || requiresRekey || id == 0 || expectedEpoch == 0 || newOwner == 0) return false;
            if (!graph.Contains(id) || !identity.Knows(id)) return false;
            return authority.Transfer(id, expectedEpoch, newOwner, out epoch);
        }

        // All-or-nothing ownership move for a root and its whole graph group. The
        // caller passes the graph revision its epoch snapshot came from; a
        // structural change in between refuses the transfer instead of splitting
        // the group across two owners.
        internal bool TransferGroup(ulong root, ulong graphRevision, ulong oldOwner, ulong newOwner, int maxEntities)
        {
            AssertThread();
            if (!isHost || requiresRekey || root == 0 || oldOwner == 0 || newOwner == 0 || oldOwner == newOwner) return false;
            if (graphRevision != graph.Revision) return false;
            int limit = maxEntities < 1 ? 1 : maxEntities;
            ulong[] group;
            if (!graph.TryGroup(root, limit, out group)) return false;
            var epochs = new ulong[group.Length];
            for (int i = 0; i < group.Length; i++)
                if (!authority.TryEpoch(group[i], oldOwner, out epochs[i])) return false;
            if (graphRevision != graph.Revision) return false;
            return authority.TransferGroup(group, epochs, oldOwner, newOwner);
        }

        // Snapshot of a root's graph group with the current graph revision, for
        // callers that need both without touching the stores.
        internal bool TryGroup(ulong root, int limit, out ulong[] ids, out ulong revision)
        {
            AssertThread();
            ids = null; revision = 0;
            if (requiresRekey) return false;
            if (!graph.TryGroup(root, limit, out ids)) { ids = null; return false; }
            revision = graph.Revision;
            return true;
        }

        // Local bindings are per machine and are not host-owned. The network ID
        // must still be registered; binding never creates identity.
        internal bool BindLocal(ulong networkId, uint localId, out ulong generation)
        {
            AssertThread();
            generation = 0;
            if (requiresRekey) return false;
            return identity.Bind(networkId, localId, out generation);
        }

        internal bool UnbindLocal(ulong networkId, uint expectedLocal, ulong expectedGeneration)
        {
            AssertThread();
            if (requiresRekey) return false;
            return identity.Unbind(networkId, expectedLocal, expectedGeneration);
        }

        internal bool TryLocal(ulong networkId, out uint localId)
        {
            AssertThread();
            return identity.TryLocal(networkId, out localId);
        }

        internal bool TryNetwork(uint localId, out ulong networkId)
        {
            AssertThread();
            return identity.TryNetwork(localId, out networkId);
        }

        // Starts the single in-flight ownership transfer for a graph root. Both
        // peers must already be authenticated; the digest is the source's applied
        // final state and is required to bind the target's acknowledgement later.
        internal bool TryBeginHandoff(ulong transactionId, ulong root, ulong from, ulong to,
            PeerSession sourceSession, PeerSession targetSession, byte[] sourceDigest, double expiresAt,
            out Handoff handoff)
        {
            AssertThread();
            handoff = null;
            if (!isHost || requiresRekey || activeHandoff != null || root == 0 || from == 0 || to == 0 || from == to ||
                sourceDigest == null || sourceDigest.Length != 32 ||
                sourceSession == null || targetSession == null ||
                sourceSession.Phase != SessionPhase.Ready || targetSession.Phase != SessionPhase.Ready ||
                sourceSession.Peer != from || targetSession.Peer != to) return false;
            ulong[] group;
            if (!graph.TryGroup(root, maxEntities, out group)) return false;
            var epochs = new ulong[group.Length];
            for (int i = 0; i < group.Length; i++)
                if (!authority.TryEpoch(group[i], from, out epochs[i])) return false;
            try
            {
                handoff = new Handoff(transactionId, root, graph.Revision, group, epochs, from, to, maxEntities,
                    sourceSession, targetSession, sourceDigest, expiresAt);
            }
            catch (ArgumentException) { return false; }
            activeHandoff = handoff;
            return true;
        }

        internal bool PrepareHandoff(Handoff handoff, ulong peer, ulong nonce)
        {
            AssertThread();
            if (!isHost || handoff == null || !ReferenceEquals(handoff, activeHandoff)) return false;
            bool prepared = handoff.PreparedBy(peer, nonce, handoff.Transaction, graph, authority);
            if (handoff.IsFinished) activeHandoff = null;
            return prepared;
        }

        internal bool ReadyHandoff(Handoff handoff, ulong peer, ulong nonce, byte[] appliedDigest, double now)
        {
            AssertThread();
            if (!isHost || handoff == null || !ReferenceEquals(handoff, activeHandoff)) return false;
            bool ready = handoff.ReadyAt(peer, nonce, handoff.Transaction, appliedDigest, now);
            if (handoff.IsFinished) activeHandoff = null;
            return ready;
        }

        // Host tick: expire the in-flight transfer so a missing peer cannot leave
        // the source frozen. Returns true when this call aborted a transfer.
        internal bool TickHandoff(double now)
        {
            AssertThread();
            if (activeHandoff == null) return false;
            bool expired = activeHandoff.Tick(now);
            if (activeHandoff.IsFinished) activeHandoff = null;
            return expired;
        }

        internal bool CommitHandoff(Handoff handoff, double now)
        {
            AssertThread();
            if (!isHost || handoff == null || !ReferenceEquals(handoff, activeHandoff)) return false;
            bool committed = handoff.Commit(graph, authority, now);
            if (committed || handoff.IsFinished) activeHandoff = null;
            return committed;
        }

        internal bool AbortHandoff(Handoff handoff)
        {
            AssertThread();
            if (handoff == null || !ReferenceEquals(handoff, activeHandoff)) return false;
            handoff.Abort();
            activeHandoff = null;
            return true;
        }

        // True only when every live entity exists in all three stores, every
        // tombstone exists in all three, parents are live and caps are respected.
        internal bool Verify()
        {
            AssertThread();
            ulong[] ids = graph.CopyIds();
            int liveSeen = 0, removedSeen = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                ulong id = ids[i];
                bool graphLive = graph.Contains(id), graphRemoved = graph.IsRemoved(id);
                bool authorityLive = authority.Contains(id), authorityRemoved = authority.IsRemoved(id);
                bool identityLive = identity.Knows(id), identityRemoved = identity.IsRemoved(id);
                if (graphLive)
                {
                    if (!authorityLive || !identityLive || graphRemoved || authorityRemoved || identityRemoved) return false;
                    liveSeen++;
                    ulong parent = graph.ParentOf(id);
                    if (parent != 0 && !graph.Contains(parent)) return false;
                }
                else if (graphRemoved)
                {
                    if (!authorityRemoved || !identityRemoved || authorityLive || identityLive) return false;
                    removedSeen++;
                }
                else return false;
            }
            return liveSeen == live && removedSeen == tombstones && live <= maxEntities && tombstones <= maxTombstones;
        }
    }
}
