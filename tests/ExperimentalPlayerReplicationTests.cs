using System;
using TldMp;
using TldMp.Experimental;

// Console fixtures for the Phase 5 player replication core. No Unity, no game.
// The native player payloads are built by hand from the StablePackets layout strings
// (runtime/StablePackets.cs:68-75) and re-validated with StablePackets.Valid, so the
// identity offsets exercised here are the ones the real framing implies.
internal static class ExperimentalPlayerReplicationTests
{
    static int checks;

    static void Check(bool ok, string name)
    {
        checks++;
        if (!ok) throw new Exception("FAILED: " + name);
    }

    static void Near(double actual, double expected, string name)
    {
        checks++;
        if (Math.Abs(actual - expected) > 1e-9)
            throw new Exception("FAILED: " + name + " (expected " + expected + ", got " + actual + ")");
    }

    static void U64(byte[] b, int at, ulong v) { Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, at, 8); }
    static void I32(byte[] b, int at, int v) { Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, at, 4); }

    // 18 itemPosPlayer: "btivvi" (42) + "vv" (24) when hasVelocity. identity @2, item @10.
    static byte[] ItemPosPlayerPacket(ulong steam, uint item, bool hasVelocity)
    {
        var b = new byte[hasVelocity ? 66 : 42];
        b[0] = 18;
        b[1] = (byte)(hasVelocity ? 1 : 0);
        U64(b, 2, steam);
        I32(b, 10, unchecked((int)item));
        return b;
    }

    // 10 playerPos, free form: "bbtgvf" = 51. identity @3.
    static byte[] PlayerPosPacket(ulong steam)
    {
        var b = new byte[51];
        b[0] = 10;
        U64(b, 3, steam);
        return b;
    }

    // 11 playerAnim: "t" + 12*"v" + "b" + "f" + "y" = 159. identity @1.
    static byte[] PlayerAnimPacket(ulong steam)
    {
        var b = new byte[159];
        b[0] = 11;
        U64(b, 1, steam);
        return b;
    }

    // 12 playerOutfit: "ti[b][c]" = 55. identity @1.
    static byte[] PlayerOutfitPacket(ulong steam)
    {
        var b = new byte[55];
        b[0] = 12;
        U64(b, 1, steam);
        I32(b, 9, 3);
        I32(b, 13, 2);
        I32(b, 19, 2);
        return b;
    }

    // 34 dmgPlayer: "ttfv" = 33 (source id, victim id, dmg, dir).
    static byte[] DmgPlayerPacket(ulong from, ulong victim)
    {
        var b = new byte[33];
        b[0] = 34;
        U64(b, 1, from);
        U64(b, 9, victim);
        return b;
    }

    static readonly double Half = Math.Sqrt(0.5);

    // ---- join / leave ----------------------------------------------------------------

    static void Membership()
    {
        var r = new PlayerReplication(1, 4, 2.0);
        Check(r.Host == 1 && r.MaxPlayers == 4 && r.Count == 0, "explicit host and capacity");
        Check(r.Leases.LeaseSeconds == 2.0 && r.Leases.Held == 0, "seat lease window is forwarded to the seat table");
        Check(r.SeatOffsetCount == 0 && r.DroppedCount == 0 && r.Verify(), "empty table verifies");
        Check(!r.IsJoined(5), "unknown peer is not joined");

        Check(!r.TryJoin(0, 0), "peer 0 cannot join");
        Check(!r.TryJoin(5, double.NaN), "NaN join time refused");
        Check(!r.TryJoin(5, double.PositiveInfinity), "infinite join time refused");
        Check(r.Count == 0 && r.Verify(), "refused joins created nothing");

        Check(r.TryJoin(5, 0), "first join");
        Check(r.IsJoined(5) && r.Count == 1, "join is recorded");
        PlayerRecord record;
        Check(r.TryGet(5, out record), "record is readable");
        Check(record.Peer == 5 && record.HeldItem == 0, "fresh record holds nothing");
        Check(record.WornItems != null && record.WornItems.Length == 0 && record.WornCount == 0, "fresh record wears nothing");
        Check(!record.Seated && record.Car == 0 && record.Seat == -1 && record.SeatEpoch == 0, "fresh record is not seated");
        Check(record.LastSeen == 0, "join stamps LastSeen");
        Check(!r.TryJoin(5, 1), "duplicate join refused");
        Check(record.LastSeen == 0, "duplicate join did not refresh LastSeen");
        Check(!r.TryGet(0, out record), "peer 0 has no record");
        Check(r.Verify(), "one record verifies");

        Check(!r.TryLeave(6, 2.0), "leaving without joining is refused");
        Check(!r.TryLeave(0, 2.0), "peer 0 cannot leave");
        Check(!r.TryLeave(5, double.NaN), "NaN leave time refused");
        Check(r.IsJoined(5) && r.Verify(), "refused leaves changed nothing");

        ulong held;
        ulong[] worn;
        Check(r.TryLeave(5, 2.0, out held, out worn), "leave succeeds");
        Check(held == 0 && worn != null && worn.Length == 0, "leave of an empty peer reports no drop");
        Check(!r.IsJoined(5) && r.Count == 0 && r.Verify(), "leave removed the record");
        Check(!r.TryLeave(5, 2.0, out held, out worn), "second leave is refused");

        // Leaving releases the seat and lets the next peer take it immediately.
        Check(r.TryJoin(5, 3.0) && r.TryJoin(7, 3.0), "rejoin and second peer");
        ulong epoch;
        Check(r.TrySeat(5, 9, 0, 3.0, false, out epoch), "peer 5 takes seat (9,0)");
        Check(r.TryLeave(5, 3.5), "peer 5 leaves while seated");
        Check(!r.AllowSeat(5, 9, 0, 3.5), "leave released the seat");
        Check(r.TrySeat(7, 9, 0, 3.6, false, out epoch), "seat is free for the next peer");
        Check(epoch == 2, "the epoch never regresses across a leave");
        Check(r.Verify(), "seated handover verifies");

        // Capacity.
        var full = new PlayerReplication(1, 2, 2.0);
        Check(full.TryJoin(10, 0) && full.TryJoin(11, 0), "two peers fit");
        Check(!full.TryJoin(12, 0), "capacity is enforced");
        Check(full.Count == 2 && full.Verify(), "full table verifies");
        Check(full.MaxPlayers == 2, "explicit capacity is kept");
        Check(new PlayerReplication(1, 0, 2.0).MaxPlayers == PlayerReplication.DefaultMaxPlayers, "invalid capacity falls back to the default");
        Check(new PlayerReplication(1, PlayerReplication.MaxPlayersLimit * 4, 2.0).MaxPlayers == PlayerReplication.MaxPlayersLimit, "capacity is capped");

        string summary = r.Summary();
        Check(summary.Contains("host=1") && summary.Contains("capacity=4"), "summary reports the table");
        Check(summary.Contains("join=3") && summary.Contains("leave=2"), "summary counts joins and leaves after rejoins");
        Check(summary.Contains("seatGrant=2") && summary.Contains("forget="), "summary counts seat grants");
        Check(r.Verify(), "membership fixture verifies");
    }

    // ---- seats ------------------------------------------------------------------------

    static void Seats()
    {
        var r = new PlayerReplication(1, 8, 2.0);
        Check(r.TryJoin(10, 0) && r.TryJoin(11, 0) && r.TryJoin(12, 0), "seat fixture joins");
        PlayerRecord a, b;
        r.TryGet(10, out a);
        r.TryGet(11, out b);

        ulong epoch;
        Check(!r.TrySeat(99, 9, 3, 0, false, out epoch), "unknown peer cannot claim a seat");
        Check(!r.TrySeat(10, 0, 3, 0, false, out epoch), "car id 0 is not a seat");
        Check(!r.TrySeat(10, 9, -1, 0, false, out epoch), "negative seat index is not a seat");
        Check(!r.TrySeat(10, 9, 3, double.NaN, false, out epoch), "NaN claim time refused");
        Check(!a.Seated && r.Verify(), "refused claims seated nobody");

        Check(r.TrySeat(10, 9, 3, 0, false, out epoch), "peer 10 claims (9,3)");
        Check(epoch == 1, "first grant is epoch 1");
        Check(a.Seated && a.Car == 9 && a.Seat == 3 && a.SeatEpoch == 1, "the record mirrors the grant");
        Check(r.AllowSeat(10, 9, 3, 0), "the holder is allowed");
        Check(!r.AllowSeat(11, 9, 3, 0), "a non-holder is not allowed");
        Check(r.Verify(), "grant verifies");

        // Refresh by the holder: no new epoch, extended window.
        Check(r.TrySeat(10, 9, 3, 1.5, false, out epoch), "holder re-claim is a refresh");
        Check(epoch == 1 && a.SeatEpoch == 1, "refresh keeps the epoch");
        Check(r.AllowSeat(10, 9, 3, 3.5), "refresh extended the lease window");
        Check(!r.AllowSeat(10, 9, 3, 3.5000001), "the holder expires just past the window");

        // Fresh contention: refused, and nothing changed.
        Check(!r.TrySeat(11, 9, 3, 1.5, false, out epoch), "a contender is refused while the lease is fresh");
        Check(epoch == 0 && !b.Seated, "a refused claim reports no epoch and seats nobody");
        Check(a.Seated && r.Verify(), "a refused claim left the holder alone");

        // Stale takeover: new epoch, previous record preempted.
        Check(!r.TrySeat(11, 9, 3, 3.4, false, out epoch), "not yet stale, so no takeover");
        Check(r.TrySeat(11, 9, 3, 3.6, false, out epoch), "a stale lease can be taken over");
        Check(epoch == 2 && b.Seated && b.Car == 9 && b.Seat == 3 && b.SeatEpoch == 2, "takeover burns a new epoch");
        Check(!a.Seated && a.Car == 0 && a.Seat == -1 && a.SeatEpoch == 0, "the preempted record was cleared");
        Check(!r.AllowSeat(10, 9, 3, 3.6) && r.AllowSeat(11, 9, 3, 3.6), "takeover moved the holder");
        Check(r.Verify(), "stale takeover verifies");

        // Host authority: takes a fresh lease, still burns an epoch.
        Check(r.TrySeat(12, 9, 3, 3.7, true, out epoch), "host authority overrides a fresh lease");
        Check(epoch == 3 && !b.Seated, "host takeover burns a new epoch and clears the loser");
        Check(r.TrySeat(12, 9, 3, 3.8, true, out epoch) && epoch == 3, "host re-claim by the holder is a refresh");
        PlayerRecord c;
        r.TryGet(12, out c);
        Check(c.Seated && c.SeatEpoch == 3, "host holder record is consistent");
        Check(r.Verify(), "host takeover verifies");

        // One seat per peer: claiming elsewhere releases the previous seat.
        Check(r.TrySeat(12, 4, 0, 4.0, false, out epoch), "peer 12 moves to (4,0)");
        Check(epoch == 1 && c.Car == 4 && c.Seat == 0, "the moved peer holds the new seat");
        Check(!r.AllowSeat(12, 9, 3, 4.0), "the previous seat was released on the move");
        Check(r.Verify(), "seat move verifies");

        // Release: only the holder may.
        Check(!r.ReleaseSeat(10, 4, 0), "a non-holder cannot release the seat");
        Check(c.Seated, "a refused release kept the seat");
        Check(r.ReleaseSeat(12, 4, 0), "the holder releases the seat");
        Check(!c.Seated && c.Car == 0 && c.Seat == -1 && c.SeatEpoch == 0, "release cleared the record");
        Check(!r.AllowSeat(12, 4, 0, 4.1), "released seat is free");
        Check(!r.ReleaseSeat(12, 4, 0), "a second release is refused");
        Check(r.Verify(), "release verifies");

        // Forget: releases every seat of one peer only, keeps the epoch floor.
        Check(r.TrySeat(10, 1, 0, 5.0, false, out epoch) && r.TrySeat(11, 1, 1, 5.0, false, out epoch), "two peers on one car");
        ulong[] droppedItems;
        Check(r.ForgetPeer(10, 5.5, out droppedItems), "forget a seated peer");
        Check(!r.AllowSeat(10, 1, 0, 5.5) && r.AllowSeat(11, 1, 1, 5.5), "forget released only that peer's seat");
        Check(!r.IsJoined(10) && r.IsJoined(11), "forget removed only that peer");
        Check(droppedItems != null && droppedItems.Length == 0, "forget of an empty peer reports no drop");
        Check(r.Verify(), "forget verifies");
        Check(!r.ForgetPeer(99, 6.0, out droppedItems), "forget of an unknown peer is refused");
        Check(!r.ForgetPeer(0, 6.0, out droppedItems), "forget of peer 0 is refused");
        Check(r.TryJoin(12, 6.0) || r.IsJoined(12), "peer 12 is available for the re-claim");
        Check(r.TrySeat(12, 1, 0, 6.0, false, out epoch), "forgotten seat can be taken immediately");
        Check(epoch == 2, "forget kept the epoch floor");
        Check(r.Verify(), "post-forget re-claim verifies");

        Check(r.Summary().Contains("seatGrant=") && r.Summary().Contains("preempted=2"), "summary counts grants and preemptions");
    }

    // ---- held / worn items ------------------------------------------------------------

    static void Items()
    {
        var r = new PlayerReplication(1, 8, 2.0);
        Check(r.TryJoin(10, 0) && r.TryJoin(20, 0), "item fixture joins");
        PlayerRecord a, b;
        r.TryGet(10, out a);
        r.TryGet(20, out b);

        // Held item.
        Check(!r.SetHeldItem(99, 0x11, 0), "unknown peer cannot hold an item");
        Check(!r.SetHeldItem(10, 0, 0), "item 0 is not a held item");
        Check(!r.SetHeldItem(10, 0x11, double.NaN), "NaN item time refused");
        Check(a.HeldItem == 0 && r.Verify(), "refused item sets changed nothing");

        Check(r.SetHeldItem(10, 0x11, 1.0), "peer 10 holds 0x11");
        Check(a.HeldItem == 0x11 && a.LastSeen == 1.0, "the record exposes the held item and LastSeen");
        Check(r.SetHeldItem(10, 0x11, 1.5), "holding the same item again is idempotent");
        Check(!r.SetHeldItem(20, 0x11, 1.5), "another peer cannot take an item that is already held");
        Check(b.HeldItem == 0, "the refused takeover changed nothing");
        Check(!r.SetHeldItem(20, 0x11, 1.5) && r.Verify(), "cross-player item hold refused and verified");

        // Write authority for the held item.
        Check(r.CanWriteHeldItem(10, 10), "the holder may write its own item");
        Check(!r.CanWriteHeldItem(20, 10), "another sender may not write the holder's item");
        Check(!r.CanWriteHeldItem(10, 20), "the holder may not write another peer's item");
        Check(!r.CanWriteHeldItem(10, 0), "holder 0 is never writable");
        Check(!r.CanWriteHeldItem(0, 0), "peer 0 writes nothing");

        // Clear.
        Check(!r.ClearHeldItem(99, 2.0), "unknown peer cannot clear");
        Check(!r.ClearHeldItem(10, double.NaN), "NaN clear time refused");
        Check(r.ClearHeldItem(20, 2.0) && b.HeldItem == 0, "a clear on an empty peer is a no-op");
        Check(a.HeldItem == 0x11, "another peer's clear did not touch the holder");
        Check(r.ClearHeldItem(10, 2.0) && a.HeldItem == 0 && a.LastSeen == 2.0, "the holder clears its item");

        // Replacement is explicit.
        Check(r.SetHeldItem(10, 0x11, 3.0) && r.SetHeldItem(10, 0x22, 3.1), "a held item can be replaced");
        Check(a.HeldItem == 0x22, "replacement wins");
        Check(r.SetHeldItem(20, 0x11, 3.2), "the freed item can be held by another peer");
        Check(r.Verify(), "two distinct held items verify");
        Check(r.ClearHeldItem(10, 3.3) && r.ClearHeldItem(20, 3.3), "clear both");

        // Worn items.
        Check(!r.SetWorn(99, new ulong[] { 0x31 }), "unknown peer cannot wear");
        Check(r.SetWorn(10, new ulong[] { 0x31, 0x32 }), "peer 10 wears two items");
        Check(a.WornItems.Length == 2 && a.Wears(0x31) && a.Wears(0x32) && !a.Wears(0x33), "the record exposes the worn set");
        Check(!r.SetWorn(20, new ulong[] { 0x31 }), "another peer cannot wear an item already worn");
        Check(b.WornItems.Length == 0, "a refused worn set applies nothing");
        Check(!r.SetWorn(20, new ulong[] { 0x41, 0x31 }), "a partially conflicting worn set is refused");
        Check(b.WornItems.Length == 0 && r.Verify(), "worn sets are all-or-nothing");
        Check(!r.SetWorn(10, new ulong[] { 0x31, 0x31 }), "duplicate worn entries are refused");
        Check(!r.SetWorn(10, new ulong[] { 0x31, 0 }), "a zero worn entry is refused");
        Check(a.WornItems.Length == 2, "a refused worn set kept the previous set");
        Check(r.SetWorn(10, new ulong[] { 0x31, 0x32, 0x33 }), "worn set can grow");
        Check(a.WornItems.Length == 3, "worn set grew");
        Check(r.SetWorn(10, new ulong[] { 0x31 }), "worn set can shrink");
        Check(a.WornItems.Length == 1 && a.Wears(0x31), "worn set shrank");

        // Held and worn are mutually exclusive, for the same peer and across peers.
        Check(r.SetHeldItem(10, 0x51, 4.0), "peer 10 holds an item while wearing another");
        Check(!r.SetHeldItem(10, 0x31, 4.1), "a worn item cannot also be held by its wearer");
        Check(!r.SetWorn(10, new ulong[] { 0x51 }), "a held item cannot also be worn by its holder");
        Check(!r.SetHeldItem(20, 0x31, 4.2), "another peer cannot take a worn item");
        Check(r.Verify(), "held/worn exclusivity verifies");

        // Bounds.
        var sixteen = new ulong[PlayerReplication.MaxWorn];
        for (int i = 0; i < sixteen.Length; i++) sixteen[i] = (ulong)(0x100 + i);
        Check(PlayerReplication.MaxWorn == 16, "worn bound is 16");
        Check(r.SetWorn(20, sixteen), "16 worn items fit");
        var seventeen = new ulong[17];
        for (int i = 0; i < seventeen.Length; i++) seventeen[i] = (ulong)(0x200 + i);
        Check(!r.SetWorn(20, seventeen), "17 worn items are refused");
        Check(b.WornItems.Length == 16, "the refused oversized set kept the previous one");
        Check(r.SetWorn(20, null) && b.WornItems.Length == 0, "null clears the worn set");
        Check(r.SetWorn(20, new ulong[0]) && b.WornItems.Length == 0, "an empty array clears the worn set");
        Check(r.Verify(), "bounds verifies");

        // Leave / forget return the items the caller must drop.
        Check(r.SetWorn(10, new ulong[] { 0x31, 0x32 }), "re-wear for the leave path");
        ulong held;
        ulong[] worn;
        Check(r.TryLeave(10, 5.0, out held, out worn), "leave a carrying peer");
        Check(held == 0x51 && worn.Length == 2 && worn[0] == 0x31 && worn[1] == 0x32, "leave reports the held and worn items");
        Check(r.DroppedCount == 0, "the out-overload does not queue a duplicate drop");
        Check(!r.IsJoined(10) && r.Verify(), "leave cleared the peer's items");

        Check(r.TryJoin(10, 6.0) && r.SetHeldItem(10, 0x61, 6.0) && r.SetWorn(10, new ulong[] { 0x62 }), "rejoin while carrying");
        ulong[] droppedItems;
        Check(r.ForgetPeer(10, 6.5, out droppedItems), "forget a carrying peer");
        Check(droppedItems.Length == 2 && droppedItems[0] == 0x61 && droppedItems[1] == 0x62, "forget reports the held and worn items");
        Check(r.DroppedCount == 0 && r.Verify(), "the out-overload of ForgetPeer does not queue either");

        // The queue path (no out parameters) is the one a teardown hook uses.
        Check(r.TryJoin(10, 7.0) && r.SetHeldItem(10, 0x71, 7.0), "rejoin for the queue path");
        Check(r.ForgetPeer(10, 7.5), "forget without collecting");
        Check(r.DroppedCount == 1 && r.TakeDropped()[0] == 0x71 && r.DroppedCount == 0, "forget queues the drop for TakeDropped");
        ulong[] drained;
        Check(!r.TakeDropped(out drained) && drained.Length == 0, "an empty drain reports nothing");
        Check(r.Verify(), "item fixture verifies");
    }

    // ---- body and item identity (P1, P2, P7) ------------------------------------------

    static void Identity()
    {
        var r = new PlayerReplication(1, 8, 2.0);
        Check(r.TryJoin(10, 0) && r.TryJoin(20, 0), "identity fixture joins");

        var p10 = PlayerPosPacket(10);
        var p11 = PlayerAnimPacket(10);
        var p12 = PlayerOutfitPacket(10);
        var p18 = ItemPosPlayerPacket(10, 0x11223344, false);
        Check(StablePackets.Valid(p10), "10 wire layout validates");
        Check(StablePackets.Valid(p11), "11 wire layout validates");
        Check(StablePackets.Valid(p12), "12 wire layout validates");
        Check(StablePackets.Valid(p18) && StablePackets.Item(p18) == 0x11223344, "18 wire layout validates");

        Check(r.CanWriteBody(10, p10), "10 from the named sender is allowed");
        Check(r.CanWriteBody(10, p11), "11 from the named sender is allowed");
        Check(r.CanWriteBody(10, p12), "12 from the named sender is allowed");
        Check(r.CanWriteBody(10, p18), "18 from the named sender is allowed");

        Check(!r.CanWriteBody(20, p10), "10 naming another player's body is refused");
        Check(!r.CanWriteBody(20, p11), "11 naming another player is refused");
        Check(!r.CanWriteBody(20, p12), "12 naming another player is refused");
        Check(!r.CanWriteBody(20, p18), "18 naming another holder is refused");
        Check(!r.CanWriteBody(0, p10), "a message with no authenticated sender is refused");
        Check(!r.CanWriteBody(10, PlayerPosPacket(0)), "a zero payload identity is refused");
        Check(!r.CanWriteBody(10, PlayerPosPacket(20)), "a spoofed payload identity is refused");

        // Only Allow authorises: 13 and 34 are Unsupported by the policy and must be false.
        Check(!r.CanWriteBody(10, new byte[] { 13 }), "13 askPlayerOutfit never authorises a body write here");
        Check(!r.CanWriteBody(10, DmgPlayerPacket(10, 20)), "34 dmgPlayer never authorises a body write here");
        // Everything else is left to native validation and passes.
        Check(r.CanWriteBody(10, new byte[] { 47, 0 }), "47 stepSound passes");
        Check(r.CanWriteBody(10, null), "null payload passes");
        Check(r.CanWriteBody(10, new byte[] { 10 }), "a truncated player message passes to native validation");

        string summary = r.Summary();
        Check(summary.Contains("bodyAllow=") && summary.Contains("bodyReject="), "summary counts body decisions");
        Check(r.Verify(), "identity fixture verifies");
    }

    // ---- seat pose math ---------------------------------------------------------------

    static void Pose()
    {
        var r = new PlayerReplication(1, 8, 2.0);
        double x, y, z;

        Check(!r.TryWorldSeatPose(9, 0, 0, 0, 0, 0, 0, 0, 1, out x, out y, out z), "a seat with no stored offset has no world pose");
        Check(x == 0 && y == 0 && z == 0, "a missing offset reports zeroed outs");

        Check(!r.TrySetSeatOffset(0, 0, SeatPose.At(1, 0, 0)), "car id 0 is not a seat");
        Check(!r.TrySetSeatOffset(9, -1, SeatPose.At(1, 0, 0)), "negative seat index is not a seat");
        var nan = SeatPose.At(1, 0, 0);
        nan.LocalY = double.NaN;
        Check(!r.TrySetSeatOffset(9, 0, nan), "a non-finite offset is refused");
        var degenerate = new SeatPose();
        degenerate.LocalX = 1;
        Check(!r.TrySetSeatOffset(9, 0, degenerate), "a zero quaternion offset is refused");
        Check(!r.TrySetSeatOffset(9, 0, SeatPose.At(100, 0, 0)), "an offset beyond the metre bound is refused");
        Check(r.SeatOffsetCount == 0 && r.Verify(), "refused offsets stored nothing");

        // Translation only.
        Check(r.TrySetSeatOffset(9, 0, SeatPose.At(1, 0, 0)), "store a translated seat offset");
        SeatPose stored;
        Check(r.TrySeatOffset(9, 0, out stored) && stored.LocalX == 1 && stored.LocalQw == 1, "the stored offset round-trips");
        Check(!r.TrySeatOffset(9, 1, out stored), "an unknown seat has no offset");
        Check(r.TryWorldSeatPose(9, 0, 10, 2, -3, 0, 0, 0, 1, out x, out y, out z), "identity car rotation");
        Near(x, 11, "identity rotation x");
        Near(y, 2, "identity rotation y");
        Near(z, -3, "identity rotation z");

        // 90 degrees about +Y maps +X to -Z (Unity's Quaternion.Euler(0,90,0)).
        Check(r.TrySetSeatOffset(9, 1, SeatPose.At(1, 0, 0)), "store a seat along +X");
        Check(r.TryWorldSeatPose(9, 1, 0, 0, 0, 0, Half, 0, Half, out x, out y, out z), "90 degree Y rotation");
        Near(x, 0, "90Y x");
        Near(y, 0, "90Y y");
        Near(z, -1, "90Y z (right becomes backwards)");

        // 90 degrees about +Y applied to a +Z offset.
        Check(r.TrySetSeatOffset(9, 2, SeatPose.At(0, 0, 2)), "store a seat along +Z");
        Check(r.TryWorldSeatPose(9, 2, 10, 0, 0, 0, Half, 0, Half, out x, out y, out z), "90 degree Y rotation of +Z");
        Near(x, 12, "90Y +Z x");
        Near(y, 0, "90Y +Z y");
        Near(z, 0, "90Y +Z z");

        // 180 degrees about +Y.
        Check(r.TrySetSeatOffset(9, 3, SeatPose.At(1, 2, 3)), "store a three component offset");
        Check(r.TryWorldSeatPose(9, 3, 5, 5, 5, 0, 1, 0, 0, out x, out y, out z), "180 degree Y rotation");
        Near(x, 4, "180Y x");
        Near(y, 7, "180Y y");
        Near(z, 2, "180Y z");

        // A non-normalised car rotation is normalised, not misapplied.
        Check(r.TryWorldSeatPose(9, 1, 0, 0, 0, 0, 2, 0, 2, out x, out y, out z), "a non-unit car rotation is normalised");
        Near(x, 0, "normalised 90Y x");
        Near(z, -1, "normalised 90Y z");
        Check(r.TryWorldSeatPose(9, 0, 0, 0, 0, 0, 0, 0, 2, out x, out y, out z), "a non-unit identity rotation is normalised");
        Near(x, 1, "normalised identity x");

        // Stored rotations are normalised too.
        var rotated = new SeatPose();
        rotated.LocalQy = 3 * Half;
        rotated.LocalQw = 3 * Half;
        Check(r.TrySetSeatOffset(9, 4, rotated), "store a non-unit local rotation");
        Check(r.TrySeatOffset(9, 4, out stored), "read the normalised local rotation");
        Near(stored.LocalQy * stored.LocalQy + stored.LocalQw * stored.LocalQw, 1.0, "stored rotation is unit length");
        Check(r.TryWorldSeatPose(9, 4, 0, 0, 0, 0, 0, 0, 1, out x, out y, out z), "a seat with no local translation sits at the car origin");
        Near(x, 0, "zero offset x");
        Near(y, 0, "zero offset y");
        Near(z, 0, "zero offset z");

        // Invalid car poses.
        Check(!r.TryWorldSeatPose(9, 0, double.NaN, 0, 0, 0, 0, 0, 1, out x, out y, out z), "a non-finite car position is refused");
        Check(!r.TryWorldSeatPose(9, 0, 0, 0, 0, 0, 0, 0, 0, out x, out y, out z), "a degenerate car rotation is refused");
        Check(!r.TryWorldSeatPose(9, 0, 0, 0, 0, 0, 0, 0, double.PositiveInfinity, out x, out y, out z), "an infinite car rotation is refused");
        Check(x == 0 && y == 0 && z == 0, "a refused car pose zeroes the outs");

        // Overwrite keeps one entry per seat.
        int before = r.SeatOffsetCount;
        Check(r.TrySetSeatOffset(9, 0, SeatPose.At(2, 0, 0)), "an existing seat offset can be replaced");
        Check(r.SeatOffsetCount == before, "replacing an offset does not grow the table");
        Check(r.TryWorldSeatPose(9, 0, 0, 0, 0, 0, 0, 0, 1, out x, out y, out z), "world pose after replacement");
        Near(x, 2, "replaced offset is used");

        // Bound: 256 seats.
        var bounded = new PlayerReplication(1, 4, 2.0);
        Check(PlayerReplication.MaxSeatOffsets == 256, "seat offset bound is 256");
        for (int i = 0; i < PlayerReplication.MaxSeatOffsets; i++)
            Check(bounded.TrySetSeatOffset(7, i, SeatPose.At(0, 1, 0)), "seat " + i + " fits");
        Check(bounded.SeatOffsetCount == PlayerReplication.MaxSeatOffsets, "256 offsets stored");
        Check(!bounded.TrySetSeatOffset(7, PlayerReplication.MaxSeatOffsets, SeatPose.At(0, 1, 0)), "the 257th offset is refused");
        Check(bounded.Verify(), "bounded offsets verify");

        Check(r.Summary().Contains("offsetSet=") && r.Summary().Contains("poseMiss="), "summary counts pose work");
        Check(r.Verify(), "pose fixture verifies");
    }

    // ---- a car owner is not a passenger owner (P5) -------------------------------------

    static void CrossPlayer()
    {
        var r = new PlayerReplication(1, 8, 2.0);
        const ulong Owner = 40, Passenger = 41;
        Check(r.TryJoin(Owner, 0) && r.TryJoin(Passenger, 0), "car fixture joins");
        PlayerRecord owner, passenger;
        r.TryGet(Owner, out owner);
        r.TryGet(Passenger, out passenger);

        ulong epoch;
        Check(r.TrySeat(Owner, 7, 0, 0, false, out epoch) && epoch == 1, "the car owner takes the driver seat");
        Check(r.TrySeat(Passenger, 7, 1, 0, false, out epoch) && epoch == 1, "the passenger takes another seat on the same car");
        Check(owner.Seated && passenger.Seated && owner.Seat == 0 && passenger.Seat == 1, "two seats on one car are independent");
        Check(r.Verify(), "two seats verify");

        // The passenger's body and items belong to the passenger.
        Check(r.SetHeldItem(Passenger, 0xB1, 1.0) && r.SetWorn(Passenger, new ulong[] { 0xB2 }), "the passenger carries its own items");
        var passengerBody = ItemPosPlayerPacket(Passenger, 0xB1, false);
        Check(r.CanWriteBody(Passenger, passengerBody), "the passenger writes its own item pose");
        Check(!r.CanWriteBody(Owner, passengerBody), "the car owner cannot write the passenger's item pose");
        Check(!r.CanWriteBody(Owner, PlayerPosPacket(Passenger)), "the car owner cannot write the passenger's body");
        Check(r.CanWriteBody(Owner, PlayerPosPacket(Owner)), "the car owner still writes its own body");

        Check(!r.CanWriteHeldItem(Owner, Passenger), "the car owner cannot write the passenger's held item");
        Check(r.CanWriteHeldItem(Passenger, Passenger), "the passenger writes its own held item");
        Check(!r.SetHeldItem(Owner, 0xB1, 1.5), "the car owner cannot take the passenger's held item");
        Check(!r.SetWorn(Owner, new ulong[] { 0xB2 }), "the car owner cannot take the passenger's worn item");
        Check(!r.SetWorn(Owner, new ulong[] { 0xB3, 0xB2 }), "a partially conflicting set is refused too");
        Check(owner.HeldItem == 0 && owner.WornItems.Length == 0, "the car owner gained nothing");
        Check(passenger.HeldItem == 0xB1 && passenger.Wears(0xB2), "the passenger kept both items");
        Check(r.Verify(), "no cross-player item write verifies");

        // Seat ownership of one seat does not reach another seat on the same car.
        Check(!r.ReleaseSeat(Owner, 7, 1), "the car owner cannot release the passenger's seat");
        Check(passenger.Seated, "a refused release kept the passenger seated");

        // The car owner leaving does not evict the passenger.
        ulong[] droppedItems;
        Check(r.ForgetPeer(Owner, 2.0, out droppedItems), "the car owner disconnects");
        Check(!r.AllowSeat(Owner, 7, 0, 2.0), "the owner's seat was released");
        Check(r.AllowSeat(Passenger, 7, 1, 2.0) && passenger.Seated, "the passenger kept its seat and lease");
        Check(passenger.HeldItem == 0xB1, "the passenger kept its held item across the owner's leave");
        Check(r.Verify(), "owner leave verifies");

        // Host arbitration may move the passenger's seat, and it clears the loser's record
        // without touching the loser's items.
        Check(r.TryJoin(Owner, 3.0) && r.TrySeat(Owner, 7, 1, 3.0, true, out epoch), "host arbitration hands the seat to the owner");
        Check(epoch == 2 && !passenger.Seated && passenger.Seat == -1, "the preempted passenger record was cleared");
        Check(passenger.HeldItem == 0xB1 && passenger.Wears(0xB2), "seat arbitration left the passenger's items alone");
        Check(!r.SetHeldItem(Owner, 0xB1, 3.1), "the arbitrated seat still grants no item write");
        Check(r.Verify(), "arbitration verifies");

        // The car itself is never a player identity: a car's own id is not a peer.
        Check(!r.IsJoined(7) && !r.TryGet(7, out owner), "a car id is not a player record");
        Check(r.Summary().Contains("seated=1"), "summary reports the arbitrated seating");
        Check(r.Verify(), "cross-player fixture verifies");
    }

    // ---- bounds on the drop queue ------------------------------------------------------

    static void Drops()
    {
        var r = new PlayerReplication(1, 8, 2.0);
        Check(PlayerReplication.MaxDropped == 64, "drop queue bound is 64");
        for (ulong peer = 100; peer < 104; peer++)
        {
            Check(r.TryJoin(peer, 0), "drop fixture join " + peer);
            Check(r.SetHeldItem(peer, peer * 1000 + 1, 0), "drop fixture held item");
            var worn = new ulong[16];
            for (int i = 0; i < worn.Length; i++) worn[i] = peer * 1000 + 2 + (ulong)i;
            Check(r.SetWorn(peer, worn), "drop fixture worn set");
        }
        Check(r.Verify(), "a fully loaded fixture verifies");
        for (ulong peer = 100; peer < 104; peer++) Check(r.TryLeave(peer, 1.0), "drop fixture leave " + peer);
        Check(r.DroppedCount == PlayerReplication.MaxDropped, "the drop queue is bounded at 64");
        Check(r.Verify(), "the bounded drop queue verifies");
        ulong[] drained = r.TakeDropped();
        Check(drained.Length == 64 && r.DroppedCount == 0, "the bounded queue drains completely");
        for (int i = 0; i < drained.Length; i++) Check(drained[i] != 0, "no zero drop entries");
        Check(r.Summary().Contains("droppedOverflow=4") && r.Summary().Contains("dropped=0"), "summary counts evicted drops");
        Check(r.Verify(), "drained table verifies");
    }

    public static int Main()
    {
        Membership();
        Seats();
        Items();
        Identity();
        Pose();
        CrossPlayer();
        Drops();
        Console.WriteLine("PASS " + checks + " experimental player replication checks");
        return 0;
    }
}
