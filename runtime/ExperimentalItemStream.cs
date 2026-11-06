using System;
using System.Collections.Generic;
using System.Globalization;

namespace TldMp.Experimental
{
    // Phase 4 item/cargo event-stream pipeline (docs/MULTIPLAYER_ITEM_AUTHORITY_INVENTORY.md).
    // Pure bookkeeping on top of ItemEvent/ItemAuthority: no Unity types, no native
    // calls, no transport, no delivery. It decides only what order accepted events may
    // be applied in, which ones must wait for a parent entity, what to do when the wait
    // is hopeless, and what a joining peer may replay. Every array it returns is the
    // caller's responsibility to deliver exactly once.

    // Result of one Offer/Drain. Applied is the only result that fills `ready`.
    internal enum StreamResult : byte
    {
        Applied = 0,        // accepted and delivered now
        Buffered = 1,       // accepted, waiting for its parent entity
        Duplicate = 2,      // eventId already accepted for this entity
        Stale = 3,          // eventId older than the accepted watermark
        OwnerMismatch = 4,  // owner/epoch/sender is not the registered owner
        Removed = 5,        // entity or parent is tombstoned
        Invalid = 6,        // malformed envelope or entity the authority has no record of
        Overflow = 7,       // a bound was hit; the buffered events for that entity are dropped
        Expired = 8         // the dependency's bounded repair budget is spent; refuse to buffer
    }

    // Event-stream state for one receive-side session. One ItemAuthority owns identity,
    // owner, epoch, duplicate and stale decisions; this class owns ordering and
    // dependencies, so it is the only place that may hold an accepted event back.
    //
    // Assumption: entities are registered in the authority before their events are
    // offered (the authority never auto-registers, and this stream must not make an
    // authority decision on a peer's behalf). An event for an entity with no record is
    // reported as Invalid and never buffered. A dependency can therefore wait only on a
    // *registered child with an unregistered parent*, or on a parent whose first event
    // arrives in a later batch.
    internal sealed class ItemEventStream
    {
        internal const int DefaultMaxTotalEvents = 4096;
        internal const int DefaultMaxEventsPerEntity = 64;
        internal const int DefaultMaxBytes = 2 * 1024 * 1024;
        internal const double DefaultExpirySeconds = 60.0;
        // Bounded work per call: one Offer never returns more than this many events,
        // never ages more than MaxExpiryParents pending lists, and never scans more
        // repair records than it has.
        internal const int MaxReadyPerCall = 32;
        internal const int MaxExpiryParents = 8;
        internal const int MaxRepairAttempts = 6;
        internal const int MaxRepairDelaySeconds = 16;
        internal const int JournalCapacity = 256;
        internal const int JournalBytes = 256 * 1024;

        sealed class Pending
        {
            internal ItemEvent Event;
            internal double At;
        }

        sealed class Repair
        {
            internal int Attempts;
            internal double Next;
        }

        static readonly ItemEvent[] Empty = new ItemEvent[0];

        readonly ItemAuthority authority;
        readonly int maxTotalEvents, maxEventsPerEntity, maxBytes;
        readonly double expirySeconds;

        // Children waiting for a parent, keyed by the parent entity id, in arrival order.
        // `order` keeps the parent keys in insertion order so ageing is deterministic.
        readonly Dictionary<uint, List<Pending>> pending = new Dictionary<uint, List<Pending>>();
        readonly List<uint> order = new List<uint>();
        int sweepAt;
        int totalEvents, totalBytes;

        // Bounded, deduplicated repair requests: one record per missing parent.
        readonly Dictionary<uint, Repair> repairs = new Dictionary<uint, Repair>();
        readonly List<uint> repairOrder = new List<uint>();

        // Join journal: the accepted/ordered events a joining peer may replay.
        readonly List<ItemEvent> journal = new List<ItemEvent>();
        int journalBytes;
        bool journalStarted, journalValid;
        ulong barrierRevision;

        int applied, buffered, duplicate, stale, ownerMismatch, removed, invalid, overflow, expired, canceled;
        int repairsNoted, repairSends, journalCount, journalInvalid;

        internal ItemEventStream(ItemAuthority authority)
            : this(authority, DefaultMaxTotalEvents, DefaultMaxEventsPerEntity, DefaultMaxBytes, DefaultExpirySeconds) { }

        internal ItemEventStream(ItemAuthority authority, int maxTotalEvents, int maxEventsPerEntity, int maxBytes, double expirySeconds)
        {
            if (authority == null) throw new ArgumentException("An item event stream needs an item authority.");
            if (maxTotalEvents <= 0 || maxEventsPerEntity <= 0 || maxBytes <= 0)
                throw new ArgumentException("Invalid item event stream limits.");
            if (maxEventsPerEntity > maxTotalEvents)
                throw new ArgumentException("The per-entity item event limit cannot exceed the total limit.");
            if (expirySeconds <= 0 || Double.IsNaN(expirySeconds) || Double.IsInfinity(expirySeconds))
                throw new ArgumentException("Invalid item event stream expiry.");
            this.authority = authority;
            this.maxTotalEvents = maxTotalEvents;
            this.maxEventsPerEntity = maxEventsPerEntity;
            this.maxBytes = maxBytes;
            this.expirySeconds = expirySeconds;
        }

        // Receive-side pipeline step. The authority decides identity first (owner, epoch,
        // duplicate, stale, removed); only an accepted event can enter the stream. An
        // accepted event that needs a parent the authority has no record of is buffered
        // per parent in arrival order. An accepted event whose own entity just became
        // deliverable drains that entity's children, so `ready` is ordered parent first,
        // then children in arrival order.
        internal StreamResult Offer(ItemEvent e, ulong authenticatedSender, double now, out ItemEvent[] ready)
        {
            ready = Empty;
            if (e == null) { invalid++; return StreamResult.Invalid; }
            ItemDecision decision = authority.Accept(e, authenticatedSender, now);
            // Ageing runs on every offer, accepted or not, so a rejected stream still
            // releases expired dependencies instead of holding them forever.
            Sweep(now);
            if (decision != ItemDecision.Accepted) return Rejected(decision);

            uint parent = ParentOf(e);
            if (parent != 0 && (ulong)parent != e.Entity && !authority.Contains(parent))
            {
                if (authority.IsRemoved(parent)) { removed++; return StreamResult.Removed; }
                Repair repair;
                if (repairs.TryGetValue(parent, out repair) && repair.Attempts >= MaxRepairAttempts)
                {
                    // The dependency already burned its bounded repair budget and never
                    // resolved: refuse to buffer again instead of churning buffer space.
                    expired++;
                    return StreamResult.Expired;
                }
                return Hold(parent, e, now);
            }

            // The entity of an accepted event is registered, so it is a known parent and
            // its own children may be flushed now.
            ClearRepair((uint)e.Entity);
            var delivered = new List<ItemEvent>(4);
            delivered.Add(e);
            applied++;
            Drain((uint)e.Entity, delivered);
            ready = delivered.ToArray();
            return StreamResult.Applied;
        }

        // Flush the children buffered for a dependency the caller has just created
        // (a spawn, a registration, a completed repair). Freshly delivered children are
        // dropped if their entity was tombstoned, transferred, or re-epochd meanwhile.
        internal bool TryDrain(uint entity, out ItemEvent[] ready)
        {
            ready = Empty;
            if (entity == 0 || !authority.Contains(entity)) return false;
            List<Pending> list;
            if (!pending.TryGetValue(entity, out list) || list.Count == 0) return false;
            var delivered = new List<ItemEvent>(MaxReadyPerCall);
            Drain(entity, delivered);
            if (delivered.Count == 0) return false;
            ready = delivered.ToArray();
            return true;
        }

        // Cancels this stream's work for one entity: its buffered children, its buffered
        // events as a child of another parent, and its repair record. The authority owns
        // the tombstone (pair this with ItemAuthority.Remove); while the tombstone is in
        // place no dependency on the entity can be buffered again, so it cannot resurrect.
        internal void Remove(uint entity)
        {
            if (entity == 0) return;
            canceled += Drop(entity, false, 0.0);
            ClearRepair(entity);
            for (int at = order.Count - 1; at >= 0; at--)
            {
                uint parent = order[at];
                List<Pending> list = pending[parent];
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i].Event.Entity != (ulong)entity) continue;
                    DropAt(list, i);
                    canceled++;
                }
                if (list.Count == 0) { pending.Remove(parent); RemoveOrder(parent); }
            }
        }

        // Claim at most one due repair request, with exponential backoff and a hard
        // attempt cap (1, 2, 4, 8, 16 s, then 16 s). `due` is the next time this entity
        // may be asked again; the caller sends the native ask (21/46) itself and calls
        // ClearRepair once the dependency resolves. A record whose entity is now known
        // or tombstoned is dropped without spending an attempt.
        internal bool TryNextRepair(out uint entity, out double due, double now)
        {
            entity = 0;
            due = 0;
            for (int i = 0; i < repairOrder.Count; )
            {
                uint id = repairOrder[i];
                Repair repair = repairs[id];
                if (authority.Contains(id) || authority.IsRemoved(id)) { ClearRepair(id); continue; }
                if (repair.Attempts >= MaxRepairAttempts || now < repair.Next) { i++; continue; }
                repair.Attempts++;
                repair.Next = now + Delay(repair.Attempts);
                repairSends++;
                entity = id;
                due = repair.Next;
                return true;
            }
            return false;
        }

        internal void ClearRepair(uint entity)
        {
            if (entity == 0) return;
            if (!repairs.Remove(entity)) return;
            repairOrder.Remove(entity);
        }

        // Join barrier: the events recorded after this point may be replayed to a peer
        // that joined at `revision`. Starting a barrier discards the previous journal.
        internal void BeginBarrier(ulong revision)
        {
            if (revision == 0) throw new ArgumentException("An item event barrier revision must not be zero.");
            journal.Clear();
            journalBytes = 0;
            journalStarted = true;
            journalValid = true;
            barrierRevision = revision;
        }

        // Record one event the caller accepted and applied, in order. An invalid event or
        // any bound overflow invalidates the whole journal (fail closed), so a peer is
        // never handed a partial join state.
        internal bool Record(ItemEvent e)
        {
            if (!journalStarted || !journalValid) return false;
            if (e == null || !e.Valid()) { Invalidate(); return false; }
            int size = e.Payload.Length + ItemEvent.HeaderBytes;
            if (journal.Count >= JournalCapacity || size > JournalBytes - journalBytes) { Invalidate(); return false; }
            journal.Add(e);
            journalBytes += size;
            journalCount++;
            return true;
        }

        internal bool TryReplay(out ItemEvent[] events, out ulong finalRevision)
        {
            events = Empty;
            finalRevision = 0;
            if (!journalStarted || !journalValid) return false;
            finalRevision = barrierRevision;
            events = journal.ToArray();
            return true;
        }

        // The dependency a native payload names, or 0 when the family needs no parent.
        // itemPosLocal (17): id @1, parent @5, physlock index @9, lpos @13, lrot @25 - the
        // parent is required in every index branch, so both pose families wait for it.
        // attachable (42): id @1, attachType @5, parentid @13, only for attachType 1 (item).
        // A payload too short or of another type is not guessed at: no parent, no wait.
        internal static uint ParentOf(ItemEvent e)
        {
            if (e == null) return 0;
            byte[] payload = e.Payload;
            if (payload == null || payload.Length < 9) return 0;
            switch (e.Kind)
            {
                case ItemEventKind.PoseLocalStorage:
                case ItemEventKind.PoseLocalMount:
                    return payload[0] == 17 ? BitConverter.ToUInt32(payload, 5) : 0;
                case ItemEventKind.Attach:
                    if (payload[0] != 42 || payload.Length < 17) return 0;
                    return BitConverter.ToInt32(payload, 5) == 1 ? BitConverter.ToUInt32(payload, 13) : 0;
                default:
                    return 0;
            }
        }

        static double Delay(int attempt)
        {
            int seconds = 1 << (attempt - 1);
            return seconds > MaxRepairDelaySeconds ? MaxRepairDelaySeconds : seconds;
        }

        StreamResult Rejected(ItemDecision decision)
        {
            switch (decision)
            {
                case ItemDecision.Duplicate: duplicate++; return StreamResult.Duplicate;
                case ItemDecision.Stale: stale++; return StreamResult.Stale;
                case ItemDecision.OwnerMismatch: ownerMismatch++; return StreamResult.OwnerMismatch;
                case ItemDecision.Removed: removed++; return StreamResult.Removed;
                // The authority's bounded dedupe window can no longer classify this id:
                // fail closed and report a bound, never a guess.
                case ItemDecision.Full: overflow++; return StreamResult.Overflow;
                // Invalid, and Unknown (no entity record at all): not deliverable.
                default: invalid++; return StreamResult.Invalid;
            }
        }

        StreamResult Hold(uint parent, ItemEvent e, double now)
        {
            List<Pending> list;
            bool known = pending.TryGetValue(parent, out list);
            if (totalEvents >= maxTotalEvents || e.Payload.Length > maxBytes - totalBytes ||
                (known && list.Count >= maxEventsPerEntity))
            {
                // Overflow fails closed for this dependency only: its buffered children
                // are dropped and a repair is noted, while the authority's accepted state
                // (watermark, owner, epoch) is untouched.
                Drop(parent, true, now);
                overflow++;
                return StreamResult.Overflow;
            }
            if (!known)
            {
                list = new List<Pending>(2);
                pending.Add(parent, list);
                order.Add(parent);
            }
            list.Add(new Pending { Event = e, At = now });
            totalEvents++;
            totalBytes += e.Payload.Length;
            buffered++;
            return StreamResult.Buffered;
        }

        void Drain(uint entity, List<ItemEvent> into)
        {
            if (entity == 0) return;
            List<Pending> list;
            if (!pending.TryGetValue(entity, out list)) return;
            int i = 0;
            while (i < list.Count && into.Count < MaxReadyPerCall)
            {
                Pending p = list[i];
                // A removal or a group transfer between buffering and delivery makes the
                // buffered event obsolete; the new owner resends its own state.
                if (authority.IsRemoved(p.Event.Entity)) { DropAt(list, i); removed++; continue; }
                ulong owner, epoch;
                if (authority.TryOwner(p.Event.Entity, out owner, out epoch) &&
                    (owner != p.Event.Owner || epoch != p.Event.Epoch))
                { DropAt(list, i); ownerMismatch++; continue; }
                into.Add(p.Event);
                applied++;
                DropAt(list, i);
            }
            // Anything left over is flushed by a later Offer or TryDrain.
            if (list.Count == 0)
            {
                pending.Remove(entity);
                RemoveOrder(entity);
                ClearRepair(entity);
            }
        }

        // Bounded ageing: at most MaxExpiryParents pending lists per call, rotating so a
        // long-lived missing dependency cannot starve the others. Dropped events are
        // counted and a deduplicated repair is noted for the missing parent.
        void Sweep(double now)
        {
            if (order.Count == 0) { sweepAt = 0; return; }
            if (sweepAt >= order.Count) sweepAt = 0;
            for (int visited = 0; visited < MaxExpiryParents && order.Count > 0; visited++)
            {
                if (sweepAt >= order.Count) sweepAt = 0;
                uint parent = order[sweepAt];
                List<Pending> list = pending[parent];
                int kept = 0, dropped = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    Pending p = list[i];
                    if (now - p.At >= expirySeconds)
                    {
                        totalBytes -= p.Event.Payload.Length;
                        totalEvents--;
                        dropped++;
                        continue;
                    }
                    list[kept++] = p;
                }
                if (kept < list.Count) list.RemoveRange(kept, list.Count - kept);
                if (dropped > 0) { expired += dropped; Note(parent, now); }
                if (list.Count == 0) { pending.Remove(parent); order.RemoveAt(sweepAt); }
                else sweepAt++;
            }
            if (sweepAt >= order.Count) sweepAt = 0;
        }

        // Drops every child waiting on one parent and returns how many were released.
        int Drop(uint parent, bool note, double now)
        {
            List<Pending> list;
            if (!pending.TryGetValue(parent, out list)) return 0;
            totalEvents -= list.Count;
            for (int i = 0; i < list.Count; i++) totalBytes -= list[i].Event.Payload.Length;
            pending.Remove(parent);
            RemoveOrder(parent);
            if (note && list.Count > 0) Note(parent, now);
            return list.Count;
        }

        void DropAt(List<Pending> list, int index)
        {
            totalBytes -= list[index].Event.Payload.Length;
            totalEvents--;
            list.RemoveAt(index);
        }

        void RemoveOrder(uint parent)
        {
            int at = order.IndexOf(parent);
            if (at < 0) return;
            order.RemoveAt(at);
            if (sweepAt > at) sweepAt--;
        }

        void Note(uint entity, double now)
        {
            if (entity == 0 || repairs.ContainsKey(entity)) return;
            // Bounded: the repair store cannot outgrow the buffer bound it serves.
            if (repairs.Count >= maxTotalEvents) return;
            repairs.Add(entity, new Repair { Next = now });
            repairOrder.Add(entity);
            repairsNoted++;
        }

        void Invalidate()
        {
            journal.Clear();
            journalBytes = 0;
            journalValid = false;
            journalInvalid++;
        }

        internal string Summary()
        {
            return "stream{applied=" + applied.ToString(CultureInfo.InvariantCulture) +
                " buffered=" + buffered.ToString(CultureInfo.InvariantCulture) +
                " duplicate=" + duplicate.ToString(CultureInfo.InvariantCulture) +
                " stale=" + stale.ToString(CultureInfo.InvariantCulture) +
                " owner=" + ownerMismatch.ToString(CultureInfo.InvariantCulture) +
                " removed=" + removed.ToString(CultureInfo.InvariantCulture) +
                " invalid=" + invalid.ToString(CultureInfo.InvariantCulture) +
                " overflow=" + overflow.ToString(CultureInfo.InvariantCulture) +
                " expired=" + expired.ToString(CultureInfo.InvariantCulture) +
                " canceled=" + canceled.ToString(CultureInfo.InvariantCulture) +
                " repairs=" + repairsNoted.ToString(CultureInfo.InvariantCulture) +
                " repairSends=" + repairSends.ToString(CultureInfo.InvariantCulture) +
                " repairPending=" + repairs.Count.ToString(CultureInfo.InvariantCulture) +
                " journal=" + journalCount.ToString(CultureInfo.InvariantCulture) +
                " journalInvalid=" + journalInvalid.ToString(CultureInfo.InvariantCulture) +
                " pending=" + totalEvents.ToString(CultureInfo.InvariantCulture) +
                " pendingEntities=" + pending.Count.ToString(CultureInfo.InvariantCulture) +
                " bytes=" + totalBytes.ToString(CultureInfo.InvariantCulture) + "}";
        }

        // Invariants only: every bound holds, the accounting matches the stores, key
        // lists agree with their dictionaries, repair budgets stay in range, and the
        // journal is either empty or replayable. A buffer still waiting on a tombstoned
        // parent is a contract violation: Remove must be called on the stream.
        internal bool Verify()
        {
            if (totalEvents < 0 || totalEvents > maxTotalEvents) return false;
            if (totalBytes < 0 || totalBytes > maxBytes) return false;
            if (order.Count != pending.Count) return false;
            int events = 0, bytes = 0;
            foreach (var pair in pending)
            {
                if (pair.Key == 0 || pair.Value == null || pair.Value.Count == 0) return false;
                if (pair.Value.Count > maxEventsPerEntity) return false;
                if (authority.IsRemoved(pair.Key)) return false;
                if (order.IndexOf(pair.Key) < 0) return false;
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    ItemEvent e = pair.Value[i].Event;
                    if (e == null || e.Payload == null || e.Payload.Length == 0) return false;
                    if (e.Entity == 0 || e.EventId == 0) return false;
                    if (e.Payload.Length > ItemEvent.MaxPayload) return false;
                    events++;
                    bytes += e.Payload.Length;
                }
            }
            if (events != totalEvents || bytes != totalBytes) return false;
            if (repairOrder.Count != repairs.Count) return false;
            foreach (var pair in repairs)
            {
                if (pair.Key == 0 || pair.Value == null) return false;
                if (pair.Value.Attempts < 0 || pair.Value.Attempts > MaxRepairAttempts) return false;
                if (repairOrder.IndexOf(pair.Key) < 0) return false;
            }
            if (journal.Count > JournalCapacity || journalBytes < 0 || journalBytes > JournalBytes) return false;
            if (journal.Count > 0 && (!journalStarted || !journalValid)) return false;
            if (applied < 0 || buffered < 0 || duplicate < 0 || stale < 0 || ownerMismatch < 0 ||
                removed < 0 || invalid < 0 || overflow < 0 || expired < 0 || canceled < 0 ||
                repairsNoted < 0 || repairSends < 0 || journalCount < 0 || journalInvalid < 0) return false;
            return authority.Verify();
        }
    }
}
