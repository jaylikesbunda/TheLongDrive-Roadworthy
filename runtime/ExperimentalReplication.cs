using System;
using System.Collections.Generic;
using System.Globalization;

namespace TldMp.Experimental
{
    // Phase 7 generic entity replication core.
    //
    // One record shape and one writer-policy table for every replicated category,
    // so the per-category authority models already in this folder can be mapped
    // onto a single gate instead of each carrying its own:
    //
    //   * VehicleAuthority / AuthorityRegistry  (runtime/ExperimentalBackend.cs,
    //     runtime/ExperimentalAuthority.cs)  -> EntityKind.Car, AuthorityPolicy.CarOwner
    //   * ItemAuthority                         (runtime/ExperimentalItems.cs)
    //     -> EntityKind.Item / EntityKind.Cargo, AuthorityPolicy.CarOwner
    //   * BreakableAuthority                    (runtime/ExperimentalBreakables.cs)
    //     -> EntityKind.Gun / Damage / Prop, AuthorityPolicy.FirstWriter
    //   * Players/seat data                     (docs/MULTIPLAYER_PLAYER_AUTHORITY_INVENTORY.md
    //     P1/P2/P3)                            -> EntityKind.Player, AuthorityPolicy.OwnerPlayer
    //   * POI doors / time / weather            (docs/MULTIPLAYER_PARITY_MATRIX.md)
    //     -> EntityKind.Door / Poi / Weather, AuthorityPolicy.HostOnly
    //   * wildlife / AI                         (docs/MULTIPLAYER_ITEM_AUTHORITY_INVENTORY.md §7)
    //     -> EntityKind.Creature, AuthorityPolicy.LeasedInterest
    //
    // This file is the record/policy layer only. It is pure: no Unity type, no game
    // call, no transport, no thread. It does not replace or re-implement the
    // authorities above and nothing in the game consumes it yet.
    //
    // Policy table (EntityPolicy.PolicyFor):
    //
    //   EntityKind   AuthorityPolicy   writer
    //   Player       OwnerPlayer       the player the record belongs to
    //   Car          CarOwner          the car's owner
    //   Item         CarOwner          the car owner, or the recorded owner when loose
    //   Cargo        CarOwner          same as Item (see the loose-item note below)
    //   Creature     LeasedInterest   the current lease owner (the recorded owner)
    //   Door         HostOnly         the host, and only the host
    //   Poi          HostOnly         the host, and only the host
    //   Weather      HostOnly         the host, and only the host
    //   Gun          FirstWriter      the recorded (first) writer
    //   Damage       FirstWriter      the recorded (first) writer
    //   Prop         FirstWriter      the recorded (first) writer
    //   Unknown      None             nobody: every write is refused
    //
    // Item/Cargo loose-item choice: one EntityKind maps to exactly one policy, so
    // Item and Cargo both use CarOwner. The recorded Owner is written by the caller
    // at Register/Transfer time (the host's item gate decides whether the item is a
    // fitted/stored group member or a loose one). With a car-owner resolver
    // installed, a resolver result of 0 means "this item has no car", and the gate
    // then falls back to the recorded owner, i.e. the first-writer/proximity owner
    // a loose item needs. Without a resolver the recorded Owner is the car owner.
    // A resolver result and the recorded owner may disagree; the resolver wins for
    // the write gate and Owner keeps tracking the last explicit Transfer.
    //
    // The registry answers three questions and nothing else:
    //   * may this authenticated peer write this entity under this epoch now,
    //   * who owns it and what is the newest accepted state and revision,
    //   * is it finished, so it can never be registered or written again.

    // Kind of replicated entity. The numeric value is local to this layer and is
    // NOT a native message type; the caller maps native ids and native message
    // types to a kind at the seam. Member order is the policy table order.
    internal enum EntityKind : byte
    {
        Unknown = 0,
        Player = 1,
        Car = 2,
        Item = 3,
        Cargo = 4,
        Creature = 5,
        Door = 6,
        Gun = 7,
        Damage = 8,
        Weather = 9,
        Prop = 10,
        Poi = 11
    }

    // Who is allowed to write one entity. None is the fail-closed default: an
    // unmodelled kind has no writer at all rather than a guessed one.
    internal enum AuthorityPolicy : byte
    {
        None = 0,
        OwnerPlayer = 1,
        CarOwner = 2,
        HostOnly = 3,
        LeasedInterest = 4,
        FirstWriter = 5
    }

    // Why a state write was refused. Accepted is reported on success so a caller
    // can log one field for both outcomes. HostUnset and UnknownKind are both
    // fail-closed outcomes of an incomplete configuration, not of a bad sender.
    internal enum RejectReason : byte
    {
        Accepted = 0,
        Invalid = 1,
        UnknownKind = 2,
        UnknownEntity = 3,
        Removed = 4,
        NotOwner = 5,
        EpochMismatch = 6,
        StaleRevision = 7,
        StateTooLarge = 8,
        HostUnset = 9
    }

    // The single kind -> policy table. Pure, allocation-free and total: every
    // EntityKind value, including undefined ones cast from a byte, has an answer.
    internal static class EntityPolicy
    {
        internal static AuthorityPolicy PolicyFor(EntityKind kind)
        {
            switch (kind)
            {
                case EntityKind.Player: return AuthorityPolicy.OwnerPlayer;
                case EntityKind.Car: return AuthorityPolicy.CarOwner;
                case EntityKind.Item: return AuthorityPolicy.CarOwner;
                case EntityKind.Cargo: return AuthorityPolicy.CarOwner;
                case EntityKind.Creature: return AuthorityPolicy.LeasedInterest;
                case EntityKind.Door: return AuthorityPolicy.HostOnly;
                case EntityKind.Poi: return AuthorityPolicy.HostOnly;
                case EntityKind.Weather: return AuthorityPolicy.HostOnly;
                case EntityKind.Gun: return AuthorityPolicy.FirstWriter;
                case EntityKind.Damage: return AuthorityPolicy.FirstWriter;
                case EntityKind.Prop: return AuthorityPolicy.FirstWriter;
                default: return AuthorityPolicy.None;
            }
        }

        // A kind is modelled when the table gives it a writer.
        internal static bool Known(EntityKind kind) { return PolicyFor(kind) != AuthorityPolicy.None; }
    }

    // Read-only snapshot handed out by ReplicationRegistry.TryGet. State aliases
    // the registry's newest accepted buffer (a null State means no state has been
    // accepted yet) and must be treated as read-only: the registry never mutates an
    // accepted buffer, it replaces it on the next accepted write.
    internal struct ReplicationRecord
    {
        internal ulong NetworkId;
        internal ulong Owner;
        internal ulong Epoch;
        internal ulong Revision;
        internal EntityKind Kind;
        internal byte[] State;
        internal bool Removed;
    }

    // Bounded generic registry: one owner+epoch per entity, one strictly
    // increasing revision per (entity, epoch), one tombstone per removed id.
    //
    //   * one writer per entity. The policy table names it and the authenticated
    //     sender must be that writer; a non-writer is refused, never queued.
    //   * every ownership change carries a new epoch and restarts the revision
    //     stream, so an older (owner, epoch) pair can never write again.
    //   * removal is final. The id moves to the tombstone store and is never
    //     registered again; a full tombstone store refuses the removal (the entity
    //     stays live) rather than forgetting that it died.
    //   * a full entity store refuses a registration instead of evicting: nothing
    //     is guessed, so a cap breach is a visible refusal.
    //   * state is bounded per entity and copied on accept, so a caller cannot
    //     mutate a stored buffer afterwards.
    //
    // Hot path: TryAcceptState is two dictionary lookups, one switch, one length
    // check and one bounded array copy. No delegate unless a car-owner resolver is
    // installed, no allocation when the state length is unchanged in practice
    // (the copy is per accepted write and bounded by maxStateBytes).
    internal sealed class ReplicationRegistry
    {
        internal const int DefaultMaxEntities = 4096;
        internal const int DefaultMaxTombstones = 4096;
        internal const int DefaultMaxStateBytes = 512;

        sealed class Entry
        {
            internal ulong Owner;
            internal ulong Epoch;
            internal ulong Revision;
            internal readonly EntityKind Kind;
            internal byte[] State;

            internal Entry(ulong owner, EntityKind kind, byte[] state)
            {
                Owner = owner;
                Epoch = 1;
                Kind = kind;
                State = state;
            }
        }

        // A tombstone keeps the epoch the entity died under and its kind, so a
        // late event can be classified without the live entry.
        struct Tombstone
        {
            internal ulong Epoch;
            internal EntityKind Kind;

            internal Tombstone(ulong epoch, EntityKind kind)
            {
                Epoch = epoch;
                Kind = kind;
            }
        }

        readonly Dictionary<ulong, Entry> entities = new Dictionary<ulong, Entry>();
        readonly Dictionary<ulong, Tombstone> tombstones = new Dictionary<ulong, Tombstone>();
        readonly int maxEntities, maxTombstones, maxStateBytes;
        ulong host;
        Func<ulong, ulong> carOwnerResolver;
        int registered, transferred, forgotten, accepted;
        int invalid, unknownKind, unknownEntity, removed, notOwner, epochMismatch, staleRevision, stateTooLarge, hostUnset, full;

        internal ReplicationRegistry(int maxEntities, int maxTombstones, int maxStateBytes)
        {
            if (maxEntities <= 0 || maxTombstones <= 0 || maxStateBytes <= 0)
                throw new ArgumentException("Invalid replication registry limits.");
            this.maxEntities = maxEntities;
            this.maxTombstones = maxTombstones;
            this.maxStateBytes = maxStateBytes;
        }

        internal int Count { get { return entities.Count; } }
        internal int TombstoneCount { get { return tombstones.Count; } }
        internal int MaxEntities { get { return maxEntities; } }
        internal int MaxTombstones { get { return maxTombstones; } }
        internal int MaxStateBytes { get { return maxStateBytes; } }
        internal ulong Host { get { return host; } }
        internal int Registered { get { return registered; } }
        internal int Accepted { get { return accepted; } }
        internal int Transferred { get { return transferred; } }

        // HostOnly needs a host. 0 means "no host configured", which refuses every
        // HostOnly write (HostUnset) instead of accepting the sender by default.
        internal void SetHost(ulong host)
        {
            this.host = host;
        }

        // Optional CarOwner lookup: entity id -> the owner of the car that holds it,
        // 0 when it holds none. null (the default) means the record's own Owner
        // field is the car owner. The resolver may be replaced at any time; it is
        // never called for a kind whose policy is not CarOwner.
        internal void SetCarOwnerResolver(Func<ulong, ulong> resolver)
        {
            carOwnerResolver = resolver;
        }

        // Creates a new entity. The owner is recorded, not validated against the
        // policy (the policy is enforced on every write), so a caller can register a
        // HostOnly entity before the host is known; such an entity simply refuses
        // writes until SetHost names its owner. A null state is allowed and means
        // "no state accepted yet". Never resurrects a tombstoned id and never evicts.
        internal bool Register(ulong id, EntityKind kind, ulong owner, byte[] state)
        {
            if (id == 0 || owner == 0 || !EntityPolicy.Known(kind)) { invalid++; return false; }
            if (state != null && state.Length > maxStateBytes) { stateTooLarge++; return false; }
            if (entities.ContainsKey(id) || tombstones.ContainsKey(id)) return false;
            if (entities.Count >= maxEntities) { full++; return false; }
            entities.Add(id, new Entry(owner, kind, Copy(state)));
            registered++;
            return true;
        }

        // The expected epoch makes a repeated or concurrent transfer fail rather
        // than moving the entity twice. The new owner starts a fresh revision
        // stream, so its revisions are not constrained by the previous owner's.
        internal bool Transfer(ulong id, ulong expectedEpoch, ulong newOwner, out ulong epoch)
        {
            epoch = 0;
            if (expectedEpoch == 0 || newOwner == 0) return false;
            Entry entry;
            if (!entities.TryGetValue(id, out entry)) return false;
            if (entry.Epoch != expectedEpoch || entry.Owner == newOwner || entry.Epoch == UInt64.MaxValue) return false;
            entry.Owner = newOwner;
            entry.Epoch++;
            entry.Revision = 0;
            epoch = entry.Epoch;
            transferred++;
            return true;
        }

        // Removal is final. Without a tombstone the id could be registered again, so
        // a full tombstone store refuses the removal and leaves the entity live.
        internal bool Remove(ulong id, ulong expectedEpoch)
        {
            if (expectedEpoch == 0) return false;
            Entry entry;
            if (!entities.TryGetValue(id, out entry)) return false;
            if (entry.Epoch != expectedEpoch) return false;
            if (tombstones.Count >= maxTombstones) { full++; return false; }
            entities.Remove(id);
            tombstones.Add(id, new Tombstone(entry.Epoch, entry.Kind));
            return true;
        }

        // Receive-side gate. sender is the authenticated transport peer, epoch the
        // pair the event was produced under, revision the per-(entity, epoch)
        // watermark. Accepted means "deliver this state"; the caller then decides
        // how to apply it. The order of refusals is: caller input, tombstone, unknown
        // id, unmodelled kind (defensive), writer, epoch, revision.
        internal bool TryAcceptState(ulong id, ulong sender, ulong epoch, ulong revision, byte[] state, out RejectReason reason)
        {
            if (id == 0 || sender == 0 || epoch == 0 || revision == 0 || state == null)
            {
                reason = RejectReason.Invalid;
                invalid++;
                return false;
            }
            if (state.Length > maxStateBytes)
            {
                reason = RejectReason.StateTooLarge;
                stateTooLarge++;
                return false;
            }
            if (tombstones.ContainsKey(id))
            {
                reason = RejectReason.Removed;
                removed++;
                return false;
            }

            Entry entry;
            if (!entities.TryGetValue(id, out entry))
            {
                reason = RejectReason.UnknownEntity;
                unknownEntity++;
                return false;
            }

            AuthorityPolicy policy = EntityPolicy.PolicyFor(entry.Kind);
            if (policy == AuthorityPolicy.None)
            {
                reason = RejectReason.UnknownKind;
                unknownKind++;
                return false;
            }

            if (policy == AuthorityPolicy.HostOnly)
            {
                // An unset host is not "everyone": it refuses.
                if (host == 0)
                {
                    reason = RejectReason.HostUnset;
                    hostUnset++;
                    return false;
                }
                if (entry.Owner != host || sender != host)
                {
                    reason = RejectReason.NotOwner;
                    notOwner++;
                    return false;
                }
            }
            else if (policy == AuthorityPolicy.CarOwner)
            {
                // With a resolver the car decides; 0 means "no car", i.e. a loose
                // item, which falls back to the recorded (first-writer) owner.
                ulong car = carOwnerResolver != null ? carOwnerResolver(id) : 0;
                ulong writer = car != 0 ? car : entry.Owner;
                if (writer == 0 || sender != writer)
                {
                    reason = RejectReason.NotOwner;
                    notOwner++;
                    return false;
                }
            }
            else
            {
                // OwnerPlayer, LeasedInterest and FirstWriter all reduce to "the
                // recorded owner is the only writer". They differ in who sets that
                // owner (the entity's own peer, host interest leasing, the first
                // accepted write) and in how often Transfer is expected.
                if (entry.Owner == 0 || sender != entry.Owner)
                {
                    reason = RejectReason.NotOwner;
                    notOwner++;
                    return false;
                }
            }

            if (entry.Epoch != epoch)
            {
                reason = RejectReason.EpochMismatch;
                epochMismatch++;
                return false;
            }
            if (revision <= entry.Revision)
            {
                // Strictly increasing: an equal revision is a replay, a lower one is
                // stale. Neither may overwrite the accepted state.
                reason = RejectReason.StaleRevision;
                staleRevision++;
                return false;
            }

            entry.Revision = revision;
            entry.State = Copy(state);
            accepted++;
            reason = RejectReason.Accepted;
            return true;
        }

        // Read-only snapshot. Returns true for a live entity and for a tombstone
        // (Removed = true, State null); the gate itself never uses this.
        internal bool TryGet(ulong id, out ReplicationRecord record)
        {
            record = new ReplicationRecord();
            if (id == 0) return false;
            Entry entry;
            if (entities.TryGetValue(id, out entry))
            {
                record.NetworkId = id;
                record.Owner = entry.Owner;
                record.Epoch = entry.Epoch;
                record.Revision = entry.Revision;
                record.Kind = entry.Kind;
                record.State = entry.State;
                record.Removed = false;
                return true;
            }
            Tombstone tombstone;
            if (tombstones.TryGetValue(id, out tombstone))
            {
                record.NetworkId = id;
                record.Epoch = tombstone.Epoch;
                record.Kind = tombstone.Kind;
                record.Removed = true;
                return true;
            }
            return false;
        }

        internal bool IsRemoved(ulong id)
        {
            return id != 0 && tombstones.ContainsKey(id);
        }

        // A departed peer keeps nothing it could still write. Every live entity it
        // owns moves to the host with a new epoch and a fresh revision stream; when
        // there is no host to hand them to (host unset, or the host itself is the
        // departing peer) they expire into the tombstone store. Expiry is skipped
        // when that store is full: the entry stays live but ownerless in practice,
        // so no sender can write it, and a later Remove can still finish it.
        internal void ForgetPeer(ulong peer)
        {
            if (peer == 0 || entities.Count == 0) return;
            var ids = new List<ulong>();
            foreach (var pair in entities)
            {
                if (pair.Value.Owner == peer) ids.Add(pair.Key);
            }
            for (int i = 0; i < ids.Count; i++)
            {
                Entry entry = entities[ids[i]];
                if (host != 0 && host != peer && entry.Epoch != UInt64.MaxValue)
                {
                    entry.Owner = host;
                    entry.Epoch++;
                    entry.Revision = 0;
                    forgotten++;
                    continue;
                }
                if (tombstones.Count >= maxTombstones) { full++; continue; }
                entities.Remove(ids[i]);
                tombstones.Add(ids[i], new Tombstone(entry.Epoch, entry.Kind));
                forgotten++;
            }
        }

        // One line for a log or a test failure. Every counter mirrors one outcome;
        // transferred/forgotten/registered count registrations, not writes.
        internal string Summary()
        {
            return "entities=" + entities.Count.ToString(CultureInfo.InvariantCulture) +
                " tombstones=" + tombstones.Count.ToString(CultureInfo.InvariantCulture) +
                " host=" + host.ToString(CultureInfo.InvariantCulture) +
                " registered=" + registered.ToString(CultureInfo.InvariantCulture) +
                " transferred=" + transferred.ToString(CultureInfo.InvariantCulture) +
                " forgotten=" + forgotten.ToString(CultureInfo.InvariantCulture) +
                " accepted=" + accepted.ToString(CultureInfo.InvariantCulture) +
                " invalid=" + invalid.ToString(CultureInfo.InvariantCulture) +
                " unknownKind=" + unknownKind.ToString(CultureInfo.InvariantCulture) +
                " unknownEntity=" + unknownEntity.ToString(CultureInfo.InvariantCulture) +
                " removed=" + removed.ToString(CultureInfo.InvariantCulture) +
                " notOwner=" + notOwner.ToString(CultureInfo.InvariantCulture) +
                " epochMismatch=" + epochMismatch.ToString(CultureInfo.InvariantCulture) +
                " staleRevision=" + staleRevision.ToString(CultureInfo.InvariantCulture) +
                " stateTooLarge=" + stateTooLarge.ToString(CultureInfo.InvariantCulture) +
                " hostUnset=" + hostUnset.ToString(CultureInfo.InvariantCulture) +
                " full=" + full.ToString(CultureInfo.InvariantCulture);
        }

        // Invariants only: both stores inside their caps, no id live and tombstoned
        // at once, every record has an owner, a non-zero epoch, a modelled kind and a
        // state inside the bound.
        internal bool Verify()
        {
            if (entities.Count > maxEntities || tombstones.Count > maxTombstones) return false;
            foreach (var pair in entities)
            {
                Entry entry = pair.Value;
                if (pair.Key == 0 || entry.Owner == 0 || entry.Epoch == 0) return false;
                if (!EntityPolicy.Known(entry.Kind)) return false;
                if (entry.State != null && entry.State.Length > maxStateBytes) return false;
                if (tombstones.ContainsKey(pair.Key)) return false;
            }
            foreach (var pair in tombstones)
            {
                if (pair.Key == 0 || pair.Value.Epoch == 0) return false;
                if (!EntityPolicy.Known(pair.Value.Kind)) return false;
                if (entities.ContainsKey(pair.Key)) return false;
            }
            return true;
        }

        // Stored state is a copy, so a caller cannot rewrite an accepted buffer after
        // the fact. Bounded by the length the caller already checked.
        static byte[] Copy(byte[] state)
        {
            return state == null ? null : (byte[])state.Clone();
        }
    }
}
