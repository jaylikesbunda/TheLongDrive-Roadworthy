using System;
using System.Collections.Generic;
using System.Globalization;

namespace TldMp.Experimental
{
    // Phase 5 player/seat authority core
    // (docs/MULTIPLAYER_PLAYER_AUTHORITY_INVENTORY.md, P2/P4/P7).
    // Pure bookkeeping: no Unity types, no game calls, no transport, no delivery and no
    // live wiring. Two independent pieces:
    //   PlayerIdentityPolicy: is the player identity carried inside a native payload the
    //                         peer that actually sent the message (inventory §1.3, §3.3)?
    //   SeatLeaseTable:       host-side seat ownership with per-seat epochs (inventory §2).
    // Both are decision helpers only. Neither one mutates a payload, a seat, a body or
    // an item; callers keep ownership of delivery.

    // Result of PlayerIdentityPolicy.Check. Only RejectIdentity drops a message;
    // Allow and Unsupported are both pass-throughs.
    internal enum PlayerIdentityResult
    {
        // The type is not gated here (native structural validation remains the gate) or
        // the payload identity is present and equals the authenticated sender.
        Allow = 0,
        // The payload names a different player, or player 0, than the authenticated
        // sender. Delivering it would let one peer write another peer's body/pose/item
        // (inventory §1.3, §3.3, P2, P7).
        RejectIdentity = 1,
        // A player-related message this policy deliberately does not gate: type 13
        // askPlayerOutfit (payload is empty, `spec=""` in StablePackets.Valid) and type
        // 34 dmgPlayer (`ttfv`: a *source* id and a *victim* id, and the native receiver
        // only applies it when the victim is itself, DC:4697-4704). Callers must treat
        // Unsupported exactly like Allow.
        Unsupported = 2
    }

    // Verified native payload identities. Every offset below is the byte position of a
    // little-endian u64 inside the message as it appears on the wire (byte 0 is the
    // message type; StablePackets.Cursor starts reading at index 1), so
    //   offset = 1 + (offset of the field inside the StablePackets layout string).
    //
    // 10 playerPos      StablePackets.cs:68 "bbtgvf" -> bool sitting, bool sitAtPoi,
    //                   u64 steamID, Vector3d pos, Vector3 rot, float duck, plus
    //                   "igi" (poiGenID,poiID,seatID) when sitAtPoi or "ii"
    //                   (itemID,seatID) when sitting. Identity = sender's own id:
    //                   syncScript.SendPlayerPos emits OwnID() in all three shapes
    //                   (decompiled DC:3944 poi seat, DC:3948 saved-item seat,
    //                   DC:3953 free), and RecPlayerPos keys `players[...]` on that
    //                   payload id (DC:3983-3987). Sender-gated.
    // 11 playerAnim     StablePackets.cs:69 "tvvvvvvvvvvvvbfy" -> u64 steamID first.
    //                   SendPlayerAnim emits OwnID() (DC:4028) and RecPlayerAnim
    //                   resolves `players[_obj].mpscript` from that payload id
    //                   (DC:4033-4039). Sender-gated.
    // 12 playerOutfit   StablePackets.cs:70 "ti[b][c]" -> u64 steamID first.
    //                   SendPlayerOutfit emits OwnID() (DC:4075) and RecPlayerOutfit
    //                   resolves PlayerOutfit(_obj) from that payload id (DC:4080-4087).
    //                   Sender-gated.
    // 18 itemPosPlayer  StablePackets.cs:75 "btivvi" (+"vv" when hasVelocity) ->
    //                   bool hasVelocity, u64 steamID, uint itemID at byte 10 (see
    //                   StablePackets.Item, StablePackets.cs:128), Vector3 localPos,
    //                   Vector3 localRot, int holdType. SendItemPosPlayer emits OwnID()
    //                   (DC:4183/4187) and RecItemPosPlayer parents the item onto the
    //                   *payload* player's proxy and applies its hold type (DC:4212-4232),
    //                   i.e. the payload id is the holder naming itself. Sender-gated.
    //
    // No player message in the assembly carries a third party's id as its subject: 10,
    // 11, 12 and 18 all describe the sender's own body, skeleton, outfit or hand, which
    // is why the same rule applies to all four.
    //
    // RELAY CAVEAT (read before wiring this anywhere). The native transport is a star:
    // a client only ever sends to the lobby owner (DC:91885-91888), the host fans a
    // forwarded message out byte-for-byte to every other member (DC:92019-92029) and a
    // client only accepts packets whose P2P remote is the lobby owner (DC:91958). So on
    // the host `authenticatedSender` is the origin peer and this rule is exact, and in a
    // two-player session the client sees sender == origin as well. On a *client* in a
    // session with three or more players every relayed 10/11/12/18 arrives with
    // sender == host and payload == origin peer, and this signature cannot tell that
    // apart from a spoofed frame. A relay-aware caller must therefore supply the host
    // identity and treat `sender == host` as "relayed, origin unknown" (inventory P2:
    // `sender == payloadIdentity || (sender == host && host is relaying)`), which needs
    // one more argument than this deliberately narrow entry point has.
    internal static class PlayerIdentityPolicy
    {
        // Native msgType values (decompiled enum order; StablePackets.Valid keys on them).
        internal const byte PlayerPos = 10;
        internal const byte PlayerAnim = 11;
        internal const byte PlayerOutfit = 12;
        internal const byte AskPlayerOutfit = 13;
        internal const byte ItemPosPlayer = 18;
        internal const byte DmgPlayer = 34;

        // Byte offsets of the u64 player identity inside the native message.
        internal const int PlayerPosIdentityOffset = 3;    // 10: "bbtgvf"
        internal const int PlayerAnimIdentityOffset = 1;   // 11: "t..."
        internal const int PlayerOutfitIdentityOffset = 1; // 12: "t..."
        internal const int ItemPosPlayerIdentityOffset = 2;// 18: "bt..."

        // True for message types whose payload names a player or acts on a player body:
        // the four gated types plus the two pass-throughs above. 47 stepSound is not a
        // player message: it carries no identity at all (inventory §8.1).
        internal static bool IsPlayerMessage(byte type)
        {
            switch (type)
            {
                case PlayerPos:
                case PlayerAnim:
                case PlayerOutfit:
                case AskPlayerOutfit:
                case ItemPosPlayer:
                case DmgPlayer: return true;
                default: return false;
            }
        }

        // Structural gate only: never throws, never mutates the payload, and answers
        // "may this authenticated sender be the player named inside this message?".
        // Malformed or truncated payloads and every unknown type return Allow, because
        // StablePackets.Valid (native validation) is the gate for those and this policy
        // must not become a second, disagreeing framing checker.
        internal static PlayerIdentityResult Check(ulong authenticatedSender, byte[] native)
        {
            try
            {
                // A type-13 payload is a single byte (its StablePackets layout is empty), so
                // only a null or empty message is outside this policy's scope.
                if (native == null || native.Length == 0) return PlayerIdentityResult.Allow;
                int offset;
                switch (native[0])
                {
                    case ItemPosPlayer: offset = ItemPosPlayerIdentityOffset; break;
                    case PlayerPos: offset = PlayerPosIdentityOffset; break;
                    case PlayerAnim:
                    case PlayerOutfit: offset = PlayerAnimIdentityOffset; break;
                    case AskPlayerOutfit:
                    case DmgPlayer: return PlayerIdentityResult.Unsupported;
                    default: return PlayerIdentityResult.Allow;
                }
                // Not enough bytes to hold the identity field: the message is malformed
                // and cannot be applied natively, so there is nothing to gate.
                if (native.Length < offset + 8) return PlayerIdentityResult.Allow;
                ulong identity = BitConverter.ToUInt64(native, offset);
                if (identity == 0 || identity != authenticatedSender) return PlayerIdentityResult.RejectIdentity;
                return PlayerIdentityResult.Allow;
            }
            catch (Exception) { return PlayerIdentityResult.Allow; }
        }
    }

    // Host-side seat lease table (inventory §2, P4). One seat is at most one holder per
    // (carId, seatIndex); every grant takes a per-seat epoch that never regresses while
    // the table lives, so a receiver can reject a grant it has already superseded.
    //
    // Time is a caller-supplied monotonic timeline (seconds). A record is stale when
    // `now - LastSeen > LeaseSeconds`; a non-finite time is refused rather than
    // interpreted. A clock that goes backwards cannot refresh or steal a lease, and it
    // never moves LastSeen backwards either.
    internal sealed class SeatLeaseTable
    {
        internal const int DefaultMaxSeats = 4096;
        internal const int MaxSeatsLimit = 65536;
        internal const double DefaultLeaseSeconds = 2.0;

        // One seat record. While it is held it is the only writer of that seat's owner.
        internal sealed class Lease
        {
            internal readonly ulong Seat;
            internal readonly uint CarId;
            internal readonly int SeatIndex;
            internal ulong Owner;
            internal ulong Epoch;
            internal double Granted;
            internal double LastSeen;
            internal Lease(ulong seat, uint carId, int seatIndex)
            {
                Seat = seat; CarId = carId; SeatIndex = seatIndex;
            }
        }

        // Live leases only (held seats).
        readonly Dictionary<ulong, Lease> held = new Dictionary<ulong, Lease>();
        // Per-seat epoch high-water mark. It outlives the lease, so a released seat can
        // never hand out an epoch a peer may still have cached.
        readonly Dictionary<ulong, ulong> epochs = new Dictionary<ulong, ulong>();
        // Scratch list for sweeps; the table is single-threaded by design.
        readonly List<ulong> sweep = new List<ulong>();
        readonly int maxSeats;
        readonly double leaseSeconds;

        long grants, refreshes, staleTakeovers, hostTakeovers, denials, releases, forgotten, expiries, rejections, capacityMisses;
        double lastForgetAt;

        internal SeatLeaseTable(int maxSeats, double leaseSeconds)
        {
            this.maxSeats = maxSeats <= 0 ? DefaultMaxSeats : (maxSeats > MaxSeatsLimit ? MaxSeatsLimit : maxSeats);
            this.leaseSeconds = Numbers.Finite(leaseSeconds) && leaseSeconds > 0 ? leaseSeconds : DefaultLeaseSeconds;
        }

        // Seat key: ulong ((carId << 32) | seatIndex). carId is tosaveitemscript.idInSave
        // (the saved item that owns the seat), seatIndex is seatscript.seatID.
        internal static ulong SeatKey(uint carId, int seatIndex)
        {
            if (carId == 0 || seatIndex < 0) return 0;
            return ((ulong)carId << 32) | (uint)seatIndex;
        }
        internal static uint CarOf(ulong seat) { return (uint)(seat >> 32); }
        internal static int IndexOf(ulong seat) { return (int)(uint)seat; }
        // A well-formed key always has a non-zero car id, which makes it non-zero too.
        internal static bool ValidSeat(ulong seat) { return (seat >> 32) != 0; }

        internal int Held { get { return held.Count; } }
        internal int Capacity { get { return maxSeats; } }
        internal int EpochFloors { get { return epochs.Count; } }
        internal double LeaseSeconds { get { return leaseSeconds; } }

        // Grant, refresh or refuse. Unknown seat -> grant to peer, epoch 1. Same peer ->
        // refresh LastSeen (monotonic) and allow. Different peer -> only a stale lease or
        // explicit host authority may be taken over, and a takeover always burns a new
        // epoch. `hostAuthority` is the host's own decision (local player, or an
        // arbitration the host owns); it never applies to a peer's claim.
        internal bool TryClaim(ulong seat, ulong peer, double now, bool hostAuthority)
        {
            if (!ValidSeat(seat) || peer == 0 || !Numbers.Finite(now)) { rejections++; return false; }
            Lease lease;
            if (held.TryGetValue(seat, out lease))
            {
                if (lease.Owner == peer) { Touch(lease, now); refreshes++; return true; }
                bool stale = now - lease.LastSeen > leaseSeconds;
                if (!stale && !hostAuthority) { denials++; return false; }
                ulong takeoverEpoch;
                if (!NextEpoch(seat, out takeoverEpoch)) { capacityMisses++; return false; }
                lease.Owner = peer;
                lease.Epoch = takeoverEpoch;
                lease.Granted = now;
                lease.LastSeen = now;
                if (stale) staleTakeovers++; else hostTakeovers++;
                return true;
            }
            if (held.Count >= maxSeats) { capacityMisses++; return false; }
            ulong epoch;
            if (!NextEpoch(seat, out epoch)) { capacityMisses++; return false; }
            held.Add(seat, new Lease(seat, CarOf(seat), IndexOf(seat))
            {
                Owner = peer, Epoch = epoch, Granted = now, LastSeen = now
            });
            grants++;
            return true;
        }

        // Read-only predicate: is `peer` the current, unexpired holder? It never refreshes,
        // so a receiver cannot keep a seat alive just by asking about it.
        internal bool Allow(ulong seat, ulong peer, double now)
        {
            if (peer == 0 || !Numbers.Finite(now)) return false;
            Lease lease;
            return held.TryGetValue(seat, out lease) && lease.Owner == peer &&
                !(now - lease.LastSeen > leaseSeconds);
        }

        // Explicit exit. Only the holder can release; anyone else is a no-op, so a peer
        // cannot free a seat it does not own.
        internal void Release(ulong seat, ulong peer)
        {
            Lease lease;
            if (peer == 0 || !held.TryGetValue(seat, out lease) || lease.Owner != peer) { rejections++; return; }
            held.Remove(seat);
            releases++;
        }

        // Peer left. Release every seat it holds, so no stale holder can survive the
        // session teardown (the epoch floor is kept, so the seat still advances when it is
        // granted again). `now` is diagnostics only and never blocks the release.
        internal void ForgetPeer(ulong peer, double now)
        {
            if (peer == 0) { rejections++; return; }
            if (Numbers.Finite(now)) lastForgetAt = now;
            sweep.Clear();
            foreach (KeyValuePair<ulong, Lease> pair in held)
                if (pair.Value.Owner == peer) sweep.Add(pair.Key);
            for (int i = 0; i < sweep.Count; i++) held.Remove(sweep[i]);
            forgotten += sweep.Count;
            sweep.Clear();
        }

        // Sweep expired leases. Returns how many seats were freed.
        internal int Expire(double now)
        {
            if (!Numbers.Finite(now)) { rejections++; return 0; }
            sweep.Clear();
            foreach (KeyValuePair<ulong, Lease> pair in held)
                if (now - pair.Value.LastSeen > leaseSeconds) sweep.Add(pair.Key);
            for (int i = 0; i < sweep.Count; i++) held.Remove(sweep[i]);
            expiries += sweep.Count;
            int count = sweep.Count;
            sweep.Clear();
            return count;
        }

        internal bool TryGet(ulong seat, out Lease lease) { return held.TryGetValue(seat, out lease); }

        internal bool TryOwner(ulong seat, out ulong peer, out ulong epoch)
        {
            peer = 0; epoch = 0;
            Lease lease;
            if (!held.TryGetValue(seat, out lease)) return false;
            peer = lease.Owner; epoch = lease.Epoch;
            return true;
        }

        // LastSeen only ever moves forward: a rolled-back clock cannot shorten or lengthen
        // an existing lease.
        static void Touch(Lease lease, double now)
        {
            if (!(now < lease.LastSeen)) lease.LastSeen = now;
        }

        // Issue the next epoch for a seat. Every issued epoch is > any epoch issued before
        // for the same seat. When the floor table is full, a floor whose seat is no longer
        // held is evicted; when every floor belongs to a live lease the claim fails.
        bool NextEpoch(ulong seat, out ulong epoch)
        {
            epoch = 0;
            ulong floor;
            if (epochs.TryGetValue(seat, out floor))
            {
                if (floor == UInt64.MaxValue) return false;
                epoch = floor + 1;
                epochs[seat] = epoch;
                return true;
            }
            if (epochs.Count >= maxSeats)
            {
                ulong evict = 0;
                bool found = false;
                foreach (KeyValuePair<ulong, ulong> pair in epochs)
                    if (!held.ContainsKey(pair.Key)) { evict = pair.Key; found = true; break; }
                if (!found) return false;
                epochs.Remove(evict);
            }
            epoch = 1;
            epochs.Add(seat, epoch);
            return true;
        }

        internal string Summary()
        {
            return "seats{held=" + held.Count.ToString(CultureInfo.InvariantCulture) +
                " capacity=" + maxSeats.ToString(CultureInfo.InvariantCulture) +
                " floors=" + epochs.Count.ToString(CultureInfo.InvariantCulture) +
                " grant=" + grants.ToString(CultureInfo.InvariantCulture) +
                " refresh=" + refreshes.ToString(CultureInfo.InvariantCulture) +
                " staleTakeover=" + staleTakeovers.ToString(CultureInfo.InvariantCulture) +
                " hostTakeover=" + hostTakeovers.ToString(CultureInfo.InvariantCulture) +
                " denied=" + denials.ToString(CultureInfo.InvariantCulture) +
                " release=" + releases.ToString(CultureInfo.InvariantCulture) +
                " forgotten=" + forgotten.ToString(CultureInfo.InvariantCulture) +
                " expired=" + expiries.ToString(CultureInfo.InvariantCulture) +
                " reject=" + rejections.ToString(CultureInfo.InvariantCulture) +
                " full=" + capacityMisses.ToString(CultureInfo.InvariantCulture) +
                "}";
        }

        // Invariants only: both stores are bounded, every held seat has a consistent key,
        // owner, epoch and timeline, and every live lease agrees with its epoch floor.
        internal bool Verify()
        {
            if (held.Count > maxSeats || epochs.Count > maxSeats || !Numbers.Finite(lastForgetAt)) return false;
            foreach (KeyValuePair<ulong, Lease> pair in held)
            {
                Lease lease = pair.Value;
                if (!ValidSeat(lease.Seat) || lease.Seat != pair.Key) return false;
                if (lease.CarId != CarOf(lease.Seat) || lease.SeatIndex != IndexOf(lease.Seat)) return false;
                if (lease.Owner == 0 || lease.Epoch == 0) return false;
                if (!Numbers.Finite(lease.LastSeen) || !Numbers.Finite(lease.Granted)) return false;
                if (lease.LastSeen < lease.Granted) return false;
                ulong floor;
                if (!epochs.TryGetValue(pair.Key, out floor) || floor != lease.Epoch) return false;
            }
            foreach (KeyValuePair<ulong, ulong> pair in epochs)
            {
                if (!ValidSeat(pair.Key) || pair.Value == 0) return false;
                Lease lease;
                if (held.TryGetValue(pair.Key, out lease) && lease.Epoch != pair.Value) return false;
            }
            return true;
        }
    }
}
