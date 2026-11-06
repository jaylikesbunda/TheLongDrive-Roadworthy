using System;
using System.Collections.Generic;
using System.Globalization;

namespace TldMp.Experimental
{
    // Phase 6 breakable/prop authority core
    // (docs/MULTIPLAYER_WORLD_AUTHORITY_INVENTORY.md §5 "Breakables, props and
    // damage" and §6.3 "Breakables and props").
    //
    // Pure bookkeeping: no Unity types, no game call, no transport and no live
    // wiring. It answers three questions and nothing else:
    //
    //   * may this authenticated peer write this world prop entity now,
    //   * who owns it after that write and under which epoch,
    //   * is the entity finished, so it can never be written or claimed again.
    //
    // The native payload is never decoded beyond its validated entity id, so a
    // caller can replay the untouched native message on the accepted path.
    //
    // Rules, one per failure mode the inventory lists in §5.4:
    //
    //   * one owner per entity. A write from another peer is refused unless the
    //     owner's lease is stale (nearest-simulating-peer shape, §6.3). The host
    //     needs no lease: its write is authoritative and takes the entity with a
    //     new epoch (§5.1 role split, §6.4 rule 2).
    //   * an unknown id is a first-writer claim, not a rejection: the prop exists
    //     because the host manifest created it (§1.3) and no peer had written to
    //     it yet. A full registry refuses with Full instead of evicting.
    //   * broken is terminal. removeItem (36) is the only kind that ends an entity;
    //     a repeated remove, or any later damage/paint/push, is AlreadyBroken. A
    //     finished entity becomes a tombstone while the tombstone store has room,
    //     and otherwise stays registered but broken. Either way it never returns.
    //   * every take-over carries a new epoch, so an older (owner, epoch) pair can
    //     never write again (inventory §6.4 rule 2).
    //
    // Hot path: AllowWrite is one dictionary lookup plus one dictionary insert for
    // a first-writer claim, no per-event list, no delegate, no native call.

    // The native message families that mutate a world prop. The numeric value is
    // the native message type and is the only part of this enum that must never
    // change; 29/32/33/36 are the four types the inventory records as ungated
    // (§5.2).
    internal enum BreakableKind : byte
    {
        Unknown = 0,
        Damage = 33,   // breakable: health/shootHealth/explosionHealth assignment
        Remove = 36,   // removeItem: unconditional local Destroy
        Paint = 32,    // color: spray paint
        Push = 29      // push: local impulse
    }

    internal enum BreakableDecision : byte
    {
        Accepted = 0,
        OwnerMismatch = 1,
        AlreadyBroken = 2,
        UnknownEntity = 3,
        Full = 4,
        Invalid = 5
    }

    internal sealed class BreakableAuthority
    {
        sealed class Entry
        {
            internal ulong Owner;
            internal ulong Epoch;
            internal bool Broken;
            internal double LastSeen;

            internal Entry(ulong owner, double now)
            {
                Owner = owner;
                Epoch = 1;
                LastSeen = now;
            }
        }

        readonly Dictionary<uint, Entry> entities = new Dictionary<uint, Entry>();
        // id -> epoch it was tombstoned under. A tombstone is never removed: it is
        // the only thing that stops a destroyed prop from being claimed again.
        readonly Dictionary<uint, ulong> tombstones = new Dictionary<uint, ulong>();
        readonly ulong host;
        readonly int maxEntities, maxTombstones;
        readonly double leaseSeconds;
        int accepted, ownerMismatch, alreadyBroken, unknown, full;

        internal BreakableAuthority(ulong host, int maxEntities, int maxTombstones, double leaseSeconds)
        {
            if (host == 0) throw new ArgumentException("A breakable authority needs a host peer.");
            if (maxEntities <= 0 || maxTombstones <= 0) throw new ArgumentException("Invalid breakable authority limits.");
            if (!(leaseSeconds > 0)) throw new ArgumentException("A breakable lease must be positive.");
            this.host = host;
            this.maxEntities = maxEntities;
            this.maxTombstones = maxTombstones;
            this.leaseSeconds = leaseSeconds;
        }

        internal ulong Host { get { return host; } }
        internal int Count { get { return entities.Count; } }
        internal int TombstoneCount { get { return tombstones.Count; } }

        // Receive-side decision for one native breakable/prop message. sender is the
        // authenticated transport peer, entity the validated native object id, now
        // the caller's monotonic clock in seconds. Invalid caller input, a finished
        // entity, a live lease held by another peer and a full registry are the only
        // refusals; everything else is accepted and updates the registry.
        internal BreakableDecision AllowWrite(ulong authenticatedSender, uint entity, BreakableKind kind, double now)
        {
            if (authenticatedSender == 0 || entity == 0 || !Known(kind)) return BreakableDecision.Invalid;
            if (Double.IsNaN(now) || Double.IsInfinity(now)) return BreakableDecision.Invalid;
            if (tombstones.ContainsKey(entity)) { alreadyBroken++; return BreakableDecision.AlreadyBroken; }

            Entry entry;
            if (!entities.TryGetValue(entity, out entry))
            {
                // First-writer claim: the entity is registered to whoever writes it
                // first, host or guest. The claim is refused, never evicted, when the
                // bounded registry is full.
                if (entities.Count >= maxEntities) { full++; return BreakableDecision.Full; }
                unknown++;
                entry = new Entry(authenticatedSender, now);
                entities.Add(entity, entry);
                return Finish(entity, entry, kind);
            }

            // A finished entity accepts nothing: not a re-damage, not a re-paint, not
            // a re-push, and not a second remove. No sender bypasses this, host
            // included, because a broken prop has no state left to arbitrate.
            if (entry.Broken) { alreadyBroken++; return BreakableDecision.AlreadyBroken; }

            if (authenticatedSender == entry.Owner)
            {
                entry.LastSeen = now;
                return Finish(entity, entry, kind);
            }

            if (authenticatedSender != host)
            {
                // Another guest may only take an entity whose lease has gone stale.
                // A live lease is never broken into, and a stale one is claimed with
                // a new epoch so the previous owner's writes die with it.
                if (!(now - entry.LastSeen > leaseSeconds)) { ownerMismatch++; return BreakableDecision.OwnerMismatch; }
            }
            if (entry.Epoch == UInt64.MaxValue) { ownerMismatch++; return BreakableDecision.OwnerMismatch; }
            entry.Owner = authenticatedSender;
            entry.Epoch++;
            entry.LastSeen = now;
            return Finish(entity, entry, kind);
        }

        BreakableDecision Finish(uint entity, Entry entry, BreakableKind kind)
        {
            if (kind == BreakableKind.Remove) Break(entity, entry);
            accepted++;
            return BreakableDecision.Accepted;
        }

        // The entity ends here. It is recorded as a tombstone while the bounded
        // tombstone store has room; when that store is full the entity stays in the
        // live registry flagged broken, which refuses every later write just the
        // same. A tombstone is never replaced by a later claim.
        void Break(uint entity, Entry entry)
        {
            entry.Broken = true;
            if (tombstones.Count >= maxTombstones) return;
            ulong epoch = entry.Epoch;
            entities.Remove(entity);
            tombstones[entity] = epoch;
        }

        // Explicit finalisation, called by the host or by the entity's owner (the
        // caller obtained the pair from TryOwner). The expected epoch makes a
        // repeated or concurrent removal fail instead of tombstoning twice, and a
        // tombstoned id is never registered again. A full tombstone store refuses
        // the call rather than forgetting that the entity died.
        internal bool Remove(uint entity, ulong expectedEpoch)
        {
            if (entity == 0 || expectedEpoch == 0) return false;
            Entry entry;
            if (!entities.TryGetValue(entity, out entry)) return false;
            if (entry.Epoch != expectedEpoch) return false;
            if (tombstones.Count >= maxTombstones) return false;
            entities.Remove(entity);
            tombstones[entity] = expectedEpoch;
            return true;
        }

        // True for a finished entity whether it is still registered broken or has
        // already been moved to the tombstone store.
        internal bool IsBroken(uint entity)
        {
            if (entity == 0) return false;
            if (tombstones.ContainsKey(entity)) return true;
            Entry entry;
            return entities.TryGetValue(entity, out entry) && entry.Broken;
        }

        // Read-only owner lookup. A finished entity has no owner: nothing may be
        // written through the pair, so it is not reported at all.
        internal bool TryOwner(uint entity, out ulong owner, out ulong epoch)
        {
            owner = 0;
            epoch = 0;
            Entry entry;
            if (entity == 0 || !entities.TryGetValue(entity, out entry) || entry.Broken) return false;
            owner = entry.Owner;
            epoch = entry.Epoch;
            return true;
        }

        // A departed peer keeps nothing it could still write: every live entity it
        // owns moves to the host with a new epoch. Finished entities stay finished
        // and are not resurrected by the disconnect. Allocation-free: it walks the
        // registry and changes entry fields only.
        internal void ForgetPeer(ulong peer, double now)
        {
            if (peer == 0 || peer == host || entities.Count == 0) return;
            foreach (var pair in entities)
            {
                Entry entry = pair.Value;
                if (entry.Broken || entry.Owner != peer || entry.Epoch == UInt64.MaxValue) continue;
                entry.Owner = host;
                entry.Epoch++;
                entry.LastSeen = now;
            }
        }

        // Read-only classification for callers that must log or gate before they
        // claim. AllowWrite never reports UnknownEntity because an unknown id is a
        // first-writer claim; this is the only place the distinction is observable.
        // It changes no state and no counter.
        internal BreakableDecision Classify(uint entity)
        {
            if (entity == 0) return BreakableDecision.Invalid;
            if (tombstones.ContainsKey(entity)) return BreakableDecision.AlreadyBroken;
            Entry entry;
            if (!entities.TryGetValue(entity, out entry)) return BreakableDecision.UnknownEntity;
            return entry.Broken ? BreakableDecision.AlreadyBroken : BreakableDecision.Accepted;
        }

        // unknown counts writes that arrived for an id with no live registration,
        // i.e. first-writer claims; every other counter mirrors one decision.
        internal string Summary()
        {
            return "entities=" + entities.Count.ToString(CultureInfo.InvariantCulture) +
                " tombstones=" + tombstones.Count.ToString(CultureInfo.InvariantCulture) +
                " accepted=" + accepted.ToString(CultureInfo.InvariantCulture) +
                " ownerMismatch=" + ownerMismatch.ToString(CultureInfo.InvariantCulture) +
                " alreadyBroken=" + alreadyBroken.ToString(CultureInfo.InvariantCulture) +
                " unknown=" + unknown.ToString(CultureInfo.InvariantCulture) +
                " full=" + full.ToString(CultureInfo.InvariantCulture);
        }

        // Invariants only: both stores inside their caps, no id live and tombstoned
        // at once, every live entry carries an owner and a non-zero epoch.
        internal bool Verify()
        {
            if (entities.Count > maxEntities || tombstones.Count > maxTombstones) return false;
            foreach (var pair in entities)
            {
                if (pair.Key == 0 || pair.Value.Owner == 0 || pair.Value.Epoch == 0) return false;
                if (tombstones.ContainsKey(pair.Key)) return false;
            }
            foreach (var pair in tombstones)
            {
                if (pair.Key == 0 || pair.Value == 0) return false;
                if (entities.ContainsKey(pair.Key)) return false;
            }
            return true;
        }

        static bool Known(BreakableKind kind)
        {
            return kind == BreakableKind.Damage || kind == BreakableKind.Remove ||
                kind == BreakableKind.Paint || kind == BreakableKind.Push;
        }
    }

    // Native message type <-> breakable kind and the entity id the authority keys
    // on. Structural validation only: no owner, epoch or lease decision is made
    // here, and a packet this layer cannot classify is left to the native path.
    internal static class BreakablePolicy
    {
        internal static bool IsBreakableMessage(byte type)
        {
            return type == (byte)BreakableKind.Damage || type == (byte)BreakableKind.Remove ||
                type == (byte)BreakableKind.Paint || type == (byte)BreakableKind.Push;
        }

        internal static BreakableKind KindOf(byte type)
        {
            switch (type)
            {
                case (byte)BreakableKind.Damage: return BreakableKind.Damage;
                case (byte)BreakableKind.Remove: return BreakableKind.Remove;
                case (byte)BreakableKind.Paint: return BreakableKind.Paint;
                case (byte)BreakableKind.Push: return BreakableKind.Push;
                default: return BreakableKind.Unknown;
            }
        }

        // Validates the native framing first (StablePackets.Valid), then reads the
        // object id from the exact offset that type uses. Malformed bytes, an
        // unmodelled type and a zero id are all false, and the caller must leave
        // such a packet alone.
        internal static bool TryEntity(byte[] native, out uint entity)
        {
            entity = 0;
            try
            {
                if (native == null || native.Length == 0) return false;
                if (!IsBreakableMessage(native[0])) return false;
                if (!StablePackets.Valid(native)) return false;
                entity = StablePackets.Item(native);
                return entity != 0;
            }
            catch (Exception)
            {
                entity = 0;
                return false;
            }
        }
    }
}
