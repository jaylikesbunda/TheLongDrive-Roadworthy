using System;
using System.Collections.Generic;
using System.Globalization;

namespace TldMp.Experimental
{
    // Phase 5 player replication core (docs/MULTIPLAYER_PLAYER_AUTHORITY_INVENTORY.md,
    // P1/P2/P3/P4/P5/P6/P7).
    //
    // Pure bookkeeping: no Unity types, no game calls, no transport, no delivery and no
    // live wiring. It decides *who may write what* and *which peer owns which seat*, and
    // it remembers the per-peer body/held/worn state that the native messages describe.
    // It never mutates a native object: callers keep ownership of delivery and of every
    // transform they touch.
    //
    // Two existing pieces are reused rather than reimplemented:
    //   SeatLeaseTable      (ExperimentalPlayers.cs) host-side seat leases with epochs.
    //   PlayerIdentityPolicy(ExperimentalPlayers.cs) payload identity vs transport sender.
    //
    // What this file adds on top of them:
    //   PlayerRecord     the per-peer state a player message may legitimately change.
    //   PlayerReplication the joint ledger: join/leave, seats, held/worn items, body and
    //                    item write authority, seat-local offsets, drops, summary, verify.
    //
    // Invariants (all re-checked by Verify):
    //   * one record per peer, at most MaxPlayers, every record joined exactly once;
    //   * a peer is seated on at most one seat, and a seat has at most one seated peer,
    //     with the record's epoch equal to the lease table's epoch for that seat;
    //   * an item is held or worn by at most one peer, and never both at once;
    //   * worn items are bounded (16), seat offsets are bounded (256), the drop queue is
    //     bounded (64);
    //   * nothing in here authorises one peer to write another peer's body or item. A car
    //     owner is not a passenger owner (P5): seat ownership, car ownership and body/item
    //     write authority are three independent facts.

    // One peer's replicated player state (inventory §1.2, §2, §3.3). Mutable by design:
    // PlayerReplication is the only writer, and only after a rule has passed.
    internal sealed class PlayerRecord
    {
        // Worn items are bounded because the native body has a handful of wear slots and
        // an unbounded array from the wire would be an allocation/DoS vector.
        internal const int MaxWorn = 16;

        // Never mutated in place: SetWorn installs a fresh copy, so a caller that keeps a
        // reference can never edit a record behind the rules' back.
        internal static readonly ulong[] EmptyWorn = new ulong[0];

        internal ulong Peer;
        // The item this peer holds in hand (msgType 18 equiped/pickedup). 0 = nothing.
        internal ulong HeldItem;
        // Worn items (msgType 18 weared). Never null, length 0..MaxWorn, no zero entries,
        // no duplicates, never the peer's own HeldItem.
        internal ulong[] WornItems = EmptyWorn;
        // Seat: Car is the owning saved item id (tosaveitemscript.idInSave), Seat is
        // seatscript.seatID, SeatEpoch is the lease epoch under which it was granted.
        internal ulong Car;
        internal int Seat = -1;
        internal ulong SeatEpoch;
        internal bool Seated;
        // Caller-supplied monotonic time of the last accepted state change for this peer.
        internal double LastSeen;

        internal int WornCount { get { return WornItems.Length; } }

        internal bool Wears(ulong item)
        {
            if (item == 0) return false;
            for (int i = 0; i < WornItems.Length; i++) if (WornItems[i] == item) return true;
            return false;
        }
    }

    // A seat-local pose: the offset and rotation of a passenger relative to the seat owner
    // (inventory §2.3: a remote occupant is parented to seat.parentObj and positioned with a
    // seat-local offset, DC:3612/3649). Deliberately not a Unity type: the bridge converts.
    internal struct SeatPose
    {
        internal double LocalX, LocalY, LocalZ;
        internal double LocalQx, LocalQy, LocalQz, LocalQw;

        // Translation with an identity rotation, which is what most seats want.
        internal static SeatPose At(double x, double y, double z)
        {
            SeatPose pose = new SeatPose();
            pose.LocalX = x; pose.LocalY = y; pose.LocalZ = z;
            pose.LocalQw = 1.0;
            return pose;
        }
    }

    internal sealed class PlayerReplication
    {
        internal const int DefaultMaxPlayers = 64;
        internal const int MaxPlayersLimit = 1024;
        internal const int MaxWorn = PlayerRecord.MaxWorn;
        // Seat-local offsets are bounded per seat; a car cannot have more seats than this
        // and a caller cannot grow the table without bound.
        internal const int MaxSeatOffsets = 256;
        // The drop queue is bounded: on overflow the OLDEST drop is evicted (a drop the
        // caller never collected is already stale) and counted.
        internal const int MaxDropped = 64;
        // Sanity bound on a seat-local offset in metres. A passenger is inside the car.
        internal const double MaxSeatOffsetMetres = 16.0;

        readonly ulong host;
        readonly int maxPlayers;
        readonly SeatLeaseTable seats;
        readonly Dictionary<ulong, PlayerRecord> players = new Dictionary<ulong, PlayerRecord>();
        readonly Dictionary<ulong, SeatPose> offsets = new Dictionary<ulong, SeatPose>();
        readonly List<ulong> dropped = new List<ulong>();

        long joins, leaves, forgets, joinRejections, capacityMisses;
        long seatGrants, seatRefreshes, seatRejections, preemptions;
        long itemSets, itemClears, wornSets, itemRejections;
        long offsetSets, offsetRejections, poseMisses, bodyAllows, bodyRejects;
        long droppedOverflow;

        // seatLeaseSeconds is the freshness window handed to the SeatLeaseTable; the table
        // clamps an invalid value to its own default, so this constructor never throws.
        internal PlayerReplication(ulong host, int maxPlayers, double seatLeaseSeconds)
        {
            this.host = host;
            this.maxPlayers = maxPlayers <= 0 ? DefaultMaxPlayers
                : (maxPlayers > MaxPlayersLimit ? MaxPlayersLimit : maxPlayers);
            seats = new SeatLeaseTable(this.maxPlayers * 8 > SeatLeaseTable.MaxSeatsLimit
                ? SeatLeaseTable.MaxSeatsLimit : this.maxPlayers * 8, seatLeaseSeconds);
        }

        // ---- inspection ----------------------------------------------------------------

        internal ulong Host { get { return host; } }
        internal int MaxPlayers { get { return maxPlayers; } }
        internal int Count { get { return players.Count; } }
        internal int DroppedCount { get { return dropped.Count; } }
        internal int SeatOffsetCount { get { return offsets.Count; } }
        // Exposed for diagnostics and for callers that need the raw lease window. Mutating
        // it directly would desynchronise the records; use the methods on this class.
        internal SeatLeaseTable Leases { get { return seats; } }

        internal bool IsJoined(ulong peer)
        {
            return peer != 0 && players.ContainsKey(peer);
        }

        internal bool TryGet(ulong peer, out PlayerRecord record)
        {
            if (peer == 0) { record = null; return false; }
            return players.TryGetValue(peer, out record);
        }

        // ---- membership ----------------------------------------------------------------

        // Join is refused for peer 0, a non-finite time, a peer that is already joined (a
        // duplicate join is a protocol error, not a refresh) and when the table is full.
        internal bool TryJoin(ulong peer, double now)
        {
            if (peer == 0 || !Numbers.Finite(now)) { joinRejections++; return false; }
            if (players.ContainsKey(peer)) { joinRejections++; return false; }
            if (players.Count >= maxPlayers) { capacityMisses++; return false; }
            PlayerRecord record = new PlayerRecord();
            record.Peer = peer;
            record.Seat = -1;
            record.LastSeen = now;
            players.Add(peer, record);
            joins++;
            return true;
        }

        // Leave: releases every seat the peer holds, clears held/worn, removes the record and
        // reports the items the caller must drop or hand back (P6: the drop has to run before
        // a proxy is destroyed, otherwise the item dies with it).
        //
        // This overload returns the items directly; it does NOT feed the TakeDropped queue.
        // A caller that uses these out values must not also drain the queue for the same
        // peer. Returns false for an unknown peer (nothing is changed).
        internal bool TryLeave(ulong peer, double now, out ulong heldItem, out ulong[] wornItems)
        {
            heldItem = 0;
            wornItems = PlayerRecord.EmptyWorn;
            if (peer == 0 || !Numbers.Finite(now)) { joinRejections++; return false; }
            PlayerRecord record;
            if (!players.TryGetValue(peer, out record)) { joinRejections++; return false; }
            Detach(record, out heldItem, out wornItems);
            players.Remove(peer);
            seats.ForgetPeer(peer, now);
            leaves++;
            return true;
        }

        // Convenience overload with the shape the task names. The drop payload is not
        // returned, so it is pushed onto the bounded TakeDropped queue instead of being lost.
        internal bool TryLeave(ulong peer, double now)
        {
            ulong heldItem;
            ulong[] wornItems;
            if (!TryLeave(peer, now, out heldItem, out wornItems)) return false;
            QueueDrop(heldItem, wornItems);
            return true;
        }

        // Teardown path for a peer that may already be gone (native timeout, RemovePlayer):
        // idempotent with respect to the record, but it always clears leftover seat leases.
        // Returns true when it actually removed a record or released a seat; false for
        // peer 0, a non-finite time, or a peer this table never knew. Like the out-overload
        // of TryLeave, this overload does not feed the TakeDropped queue.
        internal bool ForgetPeer(ulong peer, double now, out ulong[] droppedItems)
        {
            droppedItems = PlayerRecord.EmptyWorn;
            if (peer == 0 || !Numbers.Finite(now)) { joinRejections++; return false; }
            PlayerRecord record;
            bool hadRecord = players.TryGetValue(peer, out record);
            ulong heldItem = 0;
            ulong[] wornItems = PlayerRecord.EmptyWorn;
            if (hadRecord)
            {
                Detach(record, out heldItem, out wornItems);
                players.Remove(peer);
            }
            int heldSeats = seats.Held;
            seats.ForgetPeer(peer, now);
            bool releasedSeats = seats.Held != heldSeats;
            if (!hadRecord && !releasedSeats) { joinRejections++; return false; }
            int count = (heldItem == 0 ? 0 : 1) + wornItems.Length;
            if (count == 0) { droppedItems = PlayerRecord.EmptyWorn; }
            else
            {
                droppedItems = new ulong[count];
                int at = 0;
                if (heldItem != 0) droppedItems[at++] = heldItem;
                for (int i = 0; i < wornItems.Length; i++) droppedItems[at++] = wornItems[i];
            }
            forgets++;
            return true;
        }

        // Forget without collecting the payload: the items go to the TakeDropped queue.
        internal bool ForgetPeer(ulong peer, double now)
        {
            ulong[] droppedItems;
            if (!ForgetPeer(peer, now, out droppedItems)) return false;
            QueueDrop(0, droppedItems);
            return true;
        }

        // Drain the bounded drop queue. Returns an empty array (never null) when empty.
        internal ulong[] TakeDropped()
        {
            if (dropped.Count == 0) return PlayerRecord.EmptyWorn;
            ulong[] result = dropped.ToArray();
            dropped.Clear();
            return result;
        }

        internal bool TakeDropped(out ulong[] items)
        {
            items = TakeDropped();
            return items.Length != 0;
        }

        // ---- seats (P4) ----------------------------------------------------------------

        // Ask the lease table for a seat. Delegation only: the table owns the epoch, the
        // freshness window and the takeover rules. On success the record is updated, a
        // previous seat of the same peer is released (one seat per peer), and a preempted
        // peer's seat fields are cleared so no record claims a lease it does not hold.
        // hostAuthority is the host's own arbitration decision, never a peer's claim.
        internal bool TrySeat(ulong peer, uint carId, int seatIndex, double now, bool hostAuthority, out ulong seatEpoch)
        {
            seatEpoch = 0;
            PlayerRecord record;
            if (peer == 0 || !Numbers.Finite(now) || !players.TryGetValue(peer, out record))
            { seatRejections++; return false; }
            ulong seat = SeatLeaseTable.SeatKey(carId, seatIndex);
            if (!SeatLeaseTable.ValidSeat(seat)) { seatRejections++; return false; }
            ulong previous, previousEpoch;
            bool hadOwner = seats.TryOwner(seat, out previous, out previousEpoch);
            if (!seats.TryClaim(seat, peer, now, hostAuthority)) { seatRejections++; return false; }
            if (hadOwner && previous != peer) ClearSeatFields(previous, true);
            ulong owner, epoch;
            if (!seats.TryOwner(seat, out owner, out epoch) || owner != peer)
            { seatRejections++; return false; }
            if (record.Seated && (record.Car != carId || record.Seat != seatIndex))
                seats.Release(SeatOf(record), peer);
            record.Car = carId;
            record.Seat = seatIndex;
            record.SeatEpoch = epoch;
            record.Seated = true;
            Touch(record, now);
            if (hadOwner && previous == peer) seatRefreshes++; else seatGrants++;
            seatEpoch = epoch;
            return true;
        }

        // Read-only: is this peer the current, unexpired holder? Never refreshes.
        internal bool AllowSeat(ulong peer, uint carId, int seatIndex, double now)
        {
            return seats.Allow(SeatLeaseTable.SeatKey(carId, seatIndex), peer, now);
        }

        // Explicit exit. Only the holder can release; a car owner or another passenger is a
        // no-op, so nobody can free a seat they do not sit in.
        internal bool ReleaseSeat(ulong peer, uint carId, int seatIndex)
        {
            ulong seat = SeatLeaseTable.SeatKey(carId, seatIndex);
            if (peer == 0 || !SeatLeaseTable.ValidSeat(seat)) { seatRejections++; return false; }
            ulong owner, epoch;
            if (!seats.TryOwner(seat, out owner, out epoch) || owner != peer) { seatRejections++; return false; }
            seats.Release(seat, peer);
            PlayerRecord record;
            if (players.TryGetValue(peer, out record) && record.Seated &&
                record.Car == carId && record.Seat == seatIndex) ClearSeatFields(peer, false);
            return true;
        }

        // ---- held / worn items (P3, P7) ------------------------------------------------

        // Set the held item. Refused for an unknown peer, item 0, a non-finite time, an item
        // this peer wears, and — the important rule — an item any other peer already holds or
        // wears. A player may only take its own items; a replacement is explicit (calling
        // this again with a different item replaces HeldItem and the caller drops the old
        // one).
        internal bool SetHeldItem(ulong peer, ulong item, double now)
        {
            PlayerRecord record;
            if (peer == 0 || item == 0 || !Numbers.Finite(now) || !players.TryGetValue(peer, out record))
            { itemRejections++; return false; }
            if (record.Wears(item) || ClaimedByOther(peer, item)) { itemRejections++; return false; }
            record.HeldItem = item;
            Touch(record, now);
            itemSets++;
            return true;
        }

        // Clear the held item. Idempotent: succeeds (and touches LastSeen) even when nothing
        // is held. Refused only for an unknown peer or a non-finite time.
        internal bool ClearHeldItem(ulong peer, double now)
        {
            PlayerRecord record;
            if (peer == 0 || !Numbers.Finite(now) || !players.TryGetValue(peer, out record))
            { itemRejections++; return false; }
            Touch(record, now);
            if (record.HeldItem == 0) return true;
            record.HeldItem = 0;
            itemClears++;
            return true;
        }

        // Replace the worn set atomically. null or an empty array clears it. All-or-nothing:
        // a rejected set leaves the previous set untouched. Refused for more than MaxWorn
        // entries, a zero entry, a duplicate entry, an item the peer itself holds, and an
        // item any other peer holds or wears.
        internal bool SetWorn(ulong peer, ulong[] items)
        {
            PlayerRecord record;
            if (peer == 0 || !players.TryGetValue(peer, out record)) { itemRejections++; return false; }
            if (items == null || items.Length == 0)
            {
                record.WornItems = PlayerRecord.EmptyWorn;
                wornSets++;
                return true;
            }
            if (items.Length > MaxWorn) { itemRejections++; return false; }
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == 0 || items[i] == record.HeldItem || ClaimedByOther(peer, items[i]))
                { itemRejections++; return false; }
                for (int j = 0; j < i; j++) if (items[j] == items[i]) { itemRejections++; return false; }
            }
            ulong[] copy = new ulong[items.Length];
            Array.Copy(items, copy, items.Length);
            record.WornItems = copy;
            wornSets++;
            return true;
        }

        // ---- write authority (P1, P2, P5, P7) ------------------------------------------

        // May this authenticated sender write the body/pose/outfit this native payload names?
        // True only for PlayerIdentityResult.Allow: a foreign or zero payload identity is
        // RejectIdentity, and 13/34 are Unsupported (their payloads are not the sender's own
        // body, so this helper must not answer "yes" for them). Join state is deliberately
        // not consulted — the gate has to answer during the join window too.
        internal bool CanWriteBody(ulong authenticatedSender, byte[] native)
        {
            bool allow = PlayerIdentityPolicy.Check(authenticatedSender, native) == PlayerIdentityResult.Allow;
            if (allow) bodyAllows++; else bodyRejects++;
            return allow;
        }

        // May this sender write the held/worn item of holderPeer? Only the holder itself,
        // and only for a real peer id. A car owner, a seat holder or the host is not the
        // holder: seat ownership grants no item write (P5).
        internal bool CanWriteHeldItem(ulong sender, ulong holderPeer)
        {
            return sender != 0 && sender == holderPeer;
        }

        // ---- seat-local pose -----------------------------------------------------------

        // Store the local offset/rotation of a seat. Bounded by MaxSeatOffsets seats,
        // MaxSeatOffsetMetres per offset, and a finite, normalisable rotation (a degenerate
        // zero quaternion is refused rather than silently becoming identity).
        internal bool TrySetSeatOffset(uint carId, int seatIndex, SeatPose local)
        {
            ulong seat = SeatLeaseTable.SeatKey(carId, seatIndex);
            if (!SeatLeaseTable.ValidSeat(seat) || !ValidSeatPose(local)) { offsetRejections++; return false; }
            bool known = offsets.ContainsKey(seat);
            if (!known && offsets.Count >= MaxSeatOffsets) { offsetRejections++; return false; }
            offsets[seat] = Normalised(local);
            offsetSets++;
            return true;
        }

        internal bool TrySeatOffset(uint carId, int seatIndex, out SeatPose local)
        {
            return offsets.TryGetValue(SeatLeaseTable.SeatKey(carId, seatIndex), out local);
        }

        // Seat-local -> world: world = carPosition + rotate(carRotation, localOffset).
        // Pure double math, normalised internally, so a remote passenger can be placed without
        // Unity types and without touching a transform. Returns false (and zeroes the outs)
        // when the seat has no stored offset or the car pose is not finite.
        internal bool TryWorldSeatPose(uint carId, int seatIndex,
            double carX, double carY, double carZ,
            double qx, double qy, double qz, double qw,
            out double x, out double y, out double z)
        {
            x = 0; y = 0; z = 0;
            SeatPose local;
            if (!offsets.TryGetValue(SeatLeaseTable.SeatKey(carId, seatIndex), out local)) { poseMisses++; return false; }
            if (!Numbers.Finite(carX) || !Numbers.Finite(carY) || !Numbers.Finite(carZ) ||
                !Numbers.Finite(qx) || !Numbers.Finite(qy) || !Numbers.Finite(qz) || !Numbers.Finite(qw))
            { poseMisses++; return false; }
            double norm = qx * qx + qy * qy + qz * qz + qw * qw;
            if (norm < 1e-6) { poseMisses++; return false; }
            double scale = 1.0 / Math.Sqrt(norm);
            double ux = qx * scale, uy = qy * scale, uz = qz * scale, uw = qw * scale;
            double vx = local.LocalX, vy = local.LocalY, vz = local.LocalZ;
            // t = 2 (u x v); v' = v + w t + u x t  (rotation of v by the unit quaternion u).
            double tx = 2.0 * (uy * vz - uz * vy);
            double ty = 2.0 * (uz * vx - ux * vz);
            double tz = 2.0 * (ux * vy - uy * vx);
            x = carX + vx + uw * tx + (uy * tz - uz * ty);
            y = carY + vy + uw * ty + (uz * tx - ux * tz);
            z = carZ + vz + uw * tz + (ux * ty - uy * tx);
            if (!Numbers.Finite(x) || !Numbers.Finite(y) || !Numbers.Finite(z))
            { x = 0; y = 0; z = 0; poseMisses++; return false; }
            return true;
        }

        // ---- diagnostics ---------------------------------------------------------------

        internal string Summary()
        {
            int seated = 0, worn = 0, holding = 0;
            foreach (KeyValuePair<ulong, PlayerRecord> pair in players)
            {
                PlayerRecord record = pair.Value;
                if (record.Seated) seated++;
                worn += record.WornItems.Length;
                if (record.HeldItem != 0) holding++;
            }
            return "players{joined=" + players.Count.ToString(CultureInfo.InvariantCulture) +
                " capacity=" + maxPlayers.ToString(CultureInfo.InvariantCulture) +
                " host=" + host.ToString(CultureInfo.InvariantCulture) +
                " seated=" + seated.ToString(CultureInfo.InvariantCulture) +
                " holding=" + holding.ToString(CultureInfo.InvariantCulture) +
                " worn=" + worn.ToString(CultureInfo.InvariantCulture) +
                " dropped=" + dropped.Count.ToString(CultureInfo.InvariantCulture) +
                " droppedOverflow=" + droppedOverflow.ToString(CultureInfo.InvariantCulture) +
                " offsets=" + offsets.Count.ToString(CultureInfo.InvariantCulture) +
                " join=" + joins.ToString(CultureInfo.InvariantCulture) +
                " leave=" + leaves.ToString(CultureInfo.InvariantCulture) +
                " forget=" + forgets.ToString(CultureInfo.InvariantCulture) +
                " joinReject=" + joinRejections.ToString(CultureInfo.InvariantCulture) +
                " full=" + capacityMisses.ToString(CultureInfo.InvariantCulture) +
                " seatGrant=" + seatGrants.ToString(CultureInfo.InvariantCulture) +
                " seatRefresh=" + seatRefreshes.ToString(CultureInfo.InvariantCulture) +
                " preempted=" + preemptions.ToString(CultureInfo.InvariantCulture) +
                " seatReject=" + seatRejections.ToString(CultureInfo.InvariantCulture) +
                " itemSet=" + itemSets.ToString(CultureInfo.InvariantCulture) +
                " itemClear=" + itemClears.ToString(CultureInfo.InvariantCulture) +
                " wornSet=" + wornSets.ToString(CultureInfo.InvariantCulture) +
                " itemReject=" + itemRejections.ToString(CultureInfo.InvariantCulture) +
                " offsetSet=" + offsetSets.ToString(CultureInfo.InvariantCulture) +
                " offsetReject=" + offsetRejections.ToString(CultureInfo.InvariantCulture) +
                " poseMiss=" + poseMisses.ToString(CultureInfo.InvariantCulture) +
                " bodyAllow=" + bodyAllows.ToString(CultureInfo.InvariantCulture) +
                " bodyReject=" + bodyRejects.ToString(CultureInfo.InvariantCulture) +
                " " + seats.Summary() + "}";
        }

        // Invariants only. Every record is internally consistent, the seat ledger and the
        // records agree in both directions (count and epoch), every item is owned once, and
        // every store is inside its bound.
        internal bool Verify()
        {
            if (seats == null || !seats.Verify()) return false;
            if (players.Count > maxPlayers) return false;
            if (offsets.Count > MaxSeatOffsets || dropped.Count > MaxDropped) return false;
            foreach (KeyValuePair<ulong, SeatPose> pair in offsets)
                if (!SeatLeaseTable.ValidSeat(pair.Key) || !ValidSeatPose(pair.Value)) return false;
            for (int i = 0; i < dropped.Count; i++) if (dropped[i] == 0) return false;
            int seated = 0;
            foreach (KeyValuePair<ulong, PlayerRecord> pair in players)
            {
                PlayerRecord record = pair.Value;
                if (record == null || record.Peer != pair.Key || record.Peer == 0) return false;
                if (!Numbers.Finite(record.LastSeen)) return false;
                ulong[] wornItems = record.WornItems;
                if (wornItems == null || wornItems.Length > MaxWorn) return false;
                if (record.Seated)
                {
                    if (record.Car == 0 || record.Seat < 0 || record.SeatEpoch == 0) return false;
                    ulong seat = SeatOf(record);
                    if (!SeatLeaseTable.ValidSeat(seat)) return false;
                    ulong owner, epoch;
                    if (!seats.TryOwner(seat, out owner, out epoch)) return false;
                    if (owner != record.Peer || epoch != record.SeatEpoch) return false;
                    seated++;
                }
                else if (record.Car != 0 || record.Seat != -1 || record.SeatEpoch != 0) return false;
                if (record.HeldItem != 0 && record.Wears(record.HeldItem)) return false;
                for (int i = 0; i < wornItems.Length; i++)
                {
                    if (wornItems[i] == 0 || wornItems[i] == record.HeldItem) return false;
                    for (int j = 0; j < i; j++) if (wornItems[j] == wornItems[i]) return false;
                }
                foreach (KeyValuePair<ulong, PlayerRecord> other in players)
                {
                    if (ReferenceEquals(other.Value, record)) continue;
                    if (record.HeldItem != 0 && (other.Value.HeldItem == record.HeldItem || other.Value.Wears(record.HeldItem))) return false;
                    for (int i = 0; i < wornItems.Length; i++)
                        if (other.Value.HeldItem == wornItems[i] || other.Value.Wears(wornItems[i])) return false;
                }
            }
            // Every live lease has exactly one seated record (the forward direction is the
            // epoch check above; this is the reverse).
            if (seated != seats.Held) return false;
            return true;
        }

        // ---- internals -----------------------------------------------------------------

        // A record's seat key. Car is stored as ulong for the wire-facing record shape but
        // only ever comes from a uint (tosaveitemscript.idInSave), so the cast is exact.
        static ulong SeatOf(PlayerRecord record)
        {
            if (record == null || record.Car == 0 || record.Car > UInt32.MaxValue || record.Seat < 0) return 0;
            return SeatLeaseTable.SeatKey((uint)record.Car, record.Seat);
        }

        // Clear the peer's state and hand back what it was carrying.
        static void Detach(PlayerRecord record, out ulong heldItem, out ulong[] wornItems)
        {
            heldItem = record.HeldItem;
            wornItems = PlayerRecord.EmptyWorn;
            if (record.WornItems.Length != 0)
            {
                wornItems = new ulong[record.WornItems.Length];
                Array.Copy(record.WornItems, wornItems, wornItems.Length);
            }
            record.HeldItem = 0;
            record.WornItems = PlayerRecord.EmptyWorn;
            record.Car = 0;
            record.Seat = -1;
            record.SeatEpoch = 0;
            record.Seated = false;
        }

        void QueueDrop(ulong heldItem, ulong[] wornItems)
        {
            if (heldItem == 0 && (wornItems == null || wornItems.Length == 0)) return;
            AddDrop(heldItem);
            if (wornItems != null) for (int i = 0; i < wornItems.Length; i++) AddDrop(wornItems[i]);
        }

        void AddDrop(ulong item)
        {
            if (item == 0) return;
            while (dropped.Count >= MaxDropped)
            {
                dropped.RemoveAt(0);
                droppedOverflow++;
            }
            dropped.Add(item);
        }

        // True when any other record claims the item, as held or worn (P7: one item, one
        // owner; the holder's own record is not a conflict).
        bool ClaimedByOther(ulong peer, ulong item)
        {
            if (item == 0) return true;
            foreach (KeyValuePair<ulong, PlayerRecord> pair in players)
            {
                if (pair.Key == peer) continue;
                if (pair.Value.HeldItem == item || pair.Value.Wears(item)) return true;
            }
            return false;
        }

        void ClearSeatFields(ulong peer, bool preempted)
        {
            PlayerRecord record;
            if (players.TryGetValue(peer, out record))
            {
                record.Car = 0;
                record.Seat = -1;
                record.SeatEpoch = 0;
                record.Seated = false;
            }
            if (preempted) preemptions++;
        }

        // LastSeen only moves forward, so a rolled-back clock cannot shorten or extend a
        // peer's liveness window.
        static void Touch(PlayerRecord record, double now)
        {
            if (!(now < record.LastSeen)) record.LastSeen = now;
        }

        static bool ValidSeatPose(SeatPose pose)
        {
            if (!Numbers.Finite(pose.LocalX) || !Numbers.Finite(pose.LocalY) || !Numbers.Finite(pose.LocalZ)) return false;
            if (!Numbers.Finite(pose.LocalQx) || !Numbers.Finite(pose.LocalQy) ||
                !Numbers.Finite(pose.LocalQz) || !Numbers.Finite(pose.LocalQw)) return false;
            double q = pose.LocalQx * pose.LocalQx + pose.LocalQy * pose.LocalQy +
                pose.LocalQz * pose.LocalQz + pose.LocalQw * pose.LocalQw;
            if (q < 1e-6) return false;
            double distance = pose.LocalX * pose.LocalX + pose.LocalY * pose.LocalY + pose.LocalZ * pose.LocalZ;
            return distance <= MaxSeatOffsetMetres * MaxSeatOffsetMetres;
        }

        // Store a unit-length rotation so the world pose math never has to trust the caller.
        static SeatPose Normalised(SeatPose pose)
        {
            double q = pose.LocalQx * pose.LocalQx + pose.LocalQy * pose.LocalQy +
                pose.LocalQz * pose.LocalQz + pose.LocalQw * pose.LocalQw;
            double scale = 1.0 / Math.Sqrt(q);
            if (Math.Abs(scale - 1.0) < 1e-12) return pose;
            SeatPose copy = pose;
            copy.LocalQx = pose.LocalQx * scale;
            copy.LocalQy = pose.LocalQy * scale;
            copy.LocalQz = pose.LocalQz * scale;
            copy.LocalQw = pose.LocalQw * scale;
            return copy;
        }
    }
}
