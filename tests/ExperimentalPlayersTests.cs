using System;
using TldMp;
using TldMp.Experimental;

// Console fixtures for the Phase 5 player/seat authority core. No Unity, no game.
// The native payloads below are built by hand from the StablePackets layout strings
// (runtime/StablePackets.cs:68-75) and then re-validated with StablePackets.Valid, so the
// identity offsets under test are the ones the real framing implies, not guesses.
internal static class ExperimentalPlayersTests
{
    static int checks;

    static void Check(bool ok, string name)
    {
        checks++;
        if (!ok) throw new Exception("FAILED: " + name);
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

    // 10 playerPos, free form: "bbtgvf" = 1+1+1+8+24+12+4 = 51. identity @3.
    static byte[] PlayerPosPacket(ulong steam)
    {
        var b = new byte[51];
        b[0] = 10;
        U64(b, 3, steam);
        return b;
    }

    // 10 with sitting=true, sitAtPoi=false: 51 + "ii" (itemID, seatID) = 59.
    static byte[] PlayerPosSeatedPacket(ulong steam, uint item, int seat)
    {
        var b = new byte[59];
        b[0] = 10;
        b[1] = 1;
        b[2] = 0;
        U64(b, 3, steam);
        I32(b, 51, unchecked((int)item));
        I32(b, 55, seat);
        return b;
    }

    // 10 with sitting=true, sitAtPoi=true: 51 + "igi" (int, Vector3d, int) = 83.
    static byte[] PlayerPosPoiPacket(ulong steam, int poiGen, int seat)
    {
        var b = new byte[83];
        b[0] = 10;
        b[1] = 1;
        b[2] = 1;
        U64(b, 3, steam);
        I32(b, 51, poiGen);
        // poiID: 3 doubles at 55..78, all zero (finite).
        I32(b, 79, seat);
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

    // 12 playerOutfit: "ti[b][c]" with 2 bools and 2 colors = 55. identity @1.
    static byte[] PlayerOutfitPacket(ulong steam)
    {
        var b = new byte[55];
        b[0] = 12;
        U64(b, 1, steam);
        I32(b, 9, 3);   // selected
        I32(b, 13, 2);  // BEnabled count
        // two bool bytes at 17,18 stay 0
        I32(b, 19, 2);  // Ccolor count
        // two colors (4 floats each) at 23..54 stay 0
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

    static void Identity()
    {
        const PlayerIdentityResult Allow = PlayerIdentityResult.Allow;
        const PlayerIdentityResult Reject = PlayerIdentityResult.RejectIdentity;
        const PlayerIdentityResult Unsupported = PlayerIdentityResult.Unsupported;

        // --- 18 itemPosPlayer: payload steamID is the sender (DC:4183/4187 OwnID,
        //     applied as the owning holder at DC:4212-4232).
        var p18 = ItemPosPlayerPacket(7, 0x11223344, false);
        Check(StablePackets.Valid(p18), "18 hand-built wire layout validates");
        Check(StablePackets.Item(p18) == 0x11223344, "18 item id lives at byte 10, so identity ends at byte 9");
        Check(PlayerIdentityPolicy.Check(7, p18) == Allow, "18 sender match allowed");
        Check(PlayerIdentityPolicy.Check(8, p18) == Reject, "18 foreign payload identity rejected");
        Check(PlayerIdentityPolicy.Check(0, p18) == Reject, "18 with no authenticated sender rejected");
        Check(PlayerIdentityPolicy.Check(7, ItemPosPlayerPacket(0, 0x11223344, false)) == Reject, "18 zero payload identity rejected");
        Check(PlayerIdentityPolicy.Check(0, ItemPosPlayerPacket(0, 1, false)) == Reject, "18 zero identity and zero sender rejected");
        var p18v = ItemPosPlayerPacket(7, 9, true);
        Check(StablePackets.Valid(p18v) && PlayerIdentityPolicy.Check(7, p18v) == Allow, "18 with velocity half allowed");
        Check(PlayerIdentityPolicy.Check(8, p18v) == Reject, "18 with velocity half still gated");
        Check(PlayerIdentityPolicy.Check(7, new byte[] { 18 }) == Allow, "18 truncated to one byte passes to native validation");
        Check(PlayerIdentityPolicy.Check(7, new byte[] { 18, 0, 1, 2, 3, 4, 5, 6, 7 }) == Allow, "18 truncated before identity passes to native validation");
        var p18short = new byte[10];
        p18short[0] = 18;
        U64(p18short, 2, 8);
        Check(PlayerIdentityPolicy.Check(7, p18short) == Reject, "18 at exactly the identity width is gated");
        Check(PlayerIdentityPolicy.Check(8, p18short) == Allow, "18 at exactly the identity width allows its own id");

        // --- 10 playerPos: payload steamID is the sender (DC:3944/3948/3953 OwnID).
        var p10 = PlayerPosPacket(11);
        Check(StablePackets.Valid(p10), "10 free-form wire layout validates");
        Check(PlayerIdentityPolicy.Check(11, p10) == Allow, "10 sender match allowed");
        Check(PlayerIdentityPolicy.Check(12, p10) == Reject, "10 foreign payload identity rejected");
        Check(PlayerIdentityPolicy.Check(11, PlayerPosPacket(0)) == Reject, "10 zero payload identity rejected");
        var p10s = PlayerPosSeatedPacket(11, 5, 2);
        Check(StablePackets.Valid(p10s), "10 seated wire layout validates");
        Check(PlayerIdentityPolicy.Check(11, p10s) == Allow, "10 seated sender match allowed");
        Check(PlayerIdentityPolicy.Check(12, p10s) == Reject, "10 seated foreign identity rejected");
        var p10poi = PlayerPosPoiPacket(11, 3, 1);
        Check(StablePackets.Valid(p10poi), "10 poi-seated wire layout validates");
        Check(PlayerIdentityPolicy.Check(11, p10poi) == Allow, "10 poi-seated sender match allowed");
        Check(PlayerIdentityPolicy.Check(12, p10poi) == Reject, "10 poi-seated foreign identity rejected");
        Check(PlayerIdentityPolicy.Check(11, new byte[] { 10, 0 }) == Allow, "10 truncated passes to native validation");

        // --- 11 playerAnim: payload steamID is the sender (DC:4028 OwnID, DC:4033-4039).
        var p11 = PlayerAnimPacket(21);
        Check(StablePackets.Valid(p11), "11 wire layout validates");
        Check(PlayerIdentityPolicy.Check(21, p11) == Allow, "11 sender match allowed");
        Check(PlayerIdentityPolicy.Check(22, p11) == Reject, "11 foreign payload identity rejected");
        Check(PlayerIdentityPolicy.Check(21, PlayerAnimPacket(0)) == Reject, "11 zero payload identity rejected");
        Check(PlayerIdentityPolicy.Check(21, new byte[] { 11, 1, 2, 3 }) == Allow, "11 truncated passes to native validation");

        // --- 12 playerOutfit: payload steamID is the sender (DC:4075 OwnID, DC:4080-4087).
        var p12 = PlayerOutfitPacket(31);
        Check(StablePackets.Valid(p12), "12 wire layout validates");
        Check(PlayerIdentityPolicy.Check(31, p12) == Allow, "12 sender match allowed");
        Check(PlayerIdentityPolicy.Check(32, p12) == Reject, "12 foreign payload identity rejected");
        Check(PlayerIdentityPolicy.Check(31, PlayerOutfitPacket(0)) == Reject, "12 zero payload identity rejected");
        Check(PlayerIdentityPolicy.Check(31, new byte[] { 12, 1, 2 }) == Allow, "12 truncated passes to native validation");

        // --- Deliberately ungated player messages.
        Check(PlayerIdentityPolicy.Check(41, new byte[] { 13 }) == Unsupported, "13 askPlayerOutfit is not identity gated");
        var p34 = DmgPlayerPacket(41, 99);
        Check(StablePackets.Valid(p34), "34 wire layout validates");
        Check(PlayerIdentityPolicy.Check(41, p34) == Unsupported, "34 dmgPlayer is not identity gated");
        Check(PlayerIdentityPolicy.Check(99, p34) == Unsupported, "34 is never rejected by this policy");

        // --- Everything else is left to native validation.
        Check(PlayerIdentityPolicy.Check(41, new byte[] { 47, 0 }) == Allow, "47 stepSound passes");
        Check(PlayerIdentityPolicy.Check(41, new byte[] { 21 }) == Allow, "21 askItemData passes");
        Check(PlayerIdentityPolicy.Check(41, new byte[] { 200 }) == Allow, "unknown type passes");
        Check(PlayerIdentityPolicy.Check(41, new byte[] { 0 }) == Allow, "type 0 passes");
        Check(PlayerIdentityPolicy.Check(41, null) == Allow, "null payload passes");
        Check(PlayerIdentityPolicy.Check(0, new byte[0]) == Allow, "empty payload passes");

        // --- IsPlayerMessage agrees with the policy's own classification.
        Check(PlayerIdentityPolicy.IsPlayerMessage(10), "10 is a player message");
        Check(PlayerIdentityPolicy.IsPlayerMessage(11), "11 is a player message");
        Check(PlayerIdentityPolicy.IsPlayerMessage(12), "12 is a player message");
        Check(PlayerIdentityPolicy.IsPlayerMessage(13), "13 is a player message");
        Check(PlayerIdentityPolicy.IsPlayerMessage(18), "18 is a player message");
        Check(PlayerIdentityPolicy.IsPlayerMessage(34), "34 is a player message");
        Check(!PlayerIdentityPolicy.IsPlayerMessage(47), "47 carries no identity, so it is not a player message");
        Check(!PlayerIdentityPolicy.IsPlayerMessage(21), "21 is not a player message");
        Check(!PlayerIdentityPolicy.IsPlayerMessage(0), "0 is not a player message");
        Check(!PlayerIdentityPolicy.IsPlayerMessage(255), "255 is not a player message");

        // --- Never throws, and only the four gated types can ever be rejected.
        uint seed = 0x9E3779B9;
        for (int length = 0; length <= 80; length++)
        {
            var junk = new byte[length];
            for (int i = 0; i < length; i++)
            {
                seed = seed * 1664525u + 1013904223u;
                junk[i] = (byte)(seed >> 24);
            }
            for (int type = 0; type < 4; type++)
            {
                if (length > 0) junk[0] = (byte)(type == 0 ? 10 : type == 1 ? 11 : type == 2 ? 12 : 18);
                PlayerIdentityResult r = PlayerIdentityPolicy.Check(seed, junk);
                if (r == PlayerIdentityResult.RejectIdentity)
                    Check(junk[0] == 10 || junk[0] == 11 || junk[0] == 12 || junk[0] == 18,
                        "only gated player types can be rejected");
                Check(PlayerIdentityPolicy.IsPlayerMessage(junk.Length > 0 ? junk[0] : (byte)0) ==
                    (junk.Length > 0 && (junk[0] == 10 || junk[0] == 11 || junk[0] == 12 || junk[0] == 13
                        || junk[0] == 18 || junk[0] == 34)), "IsPlayerMessage is total");
            }
        }
    }

    static void Seats()
    {
        ulong s = SeatLeaseTable.SeatKey(9, 3);
        Check(s == (((ulong)9 << 32) | 3UL), "seat key is (carId << 32) | seatIndex");
        Check(SeatLeaseTable.SeatKey(0, 1) == 0, "car id 0 is not a seat");
        Check(SeatLeaseTable.SeatKey(1, -1) == 0, "negative seat index is not a seat");
        Check(SeatLeaseTable.CarOf(s) == 9 && SeatLeaseTable.IndexOf(s) == 3, "seat key decodes");
        Check(SeatLeaseTable.ValidSeat(s) && !SeatLeaseTable.ValidSeat(5), "seat key validity follows the car id");

        var t = new SeatLeaseTable(4, 2.0);
        Check(t.Capacity == 4 && t.LeaseSeconds == 2.0 && t.Held == 0, "explicit capacity and lease window");
        Check(!t.TryClaim(0, 10, 0, false), "zero seat key refused");
        Check(!t.TryClaim(SeatLeaseTable.SeatKey(0, 1), 10, 0, false), "car id 0 refused");
        Check(!t.TryClaim(s, 0, 0, false), "peer 0 refused");
        Check(!t.TryClaim(s, 10, double.NaN, false), "NaN time refused");
        Check(!t.TryClaim(s, 10, double.PositiveInfinity, false), "infinite time refused");
        Check(t.Held == 0 && t.Verify(), "refused claims created nothing");

        // Grant.
        Check(t.TryClaim(s, 10, 0, false), "unknown seat granted to first claimant");
        ulong peer, epoch;
        Check(t.TryOwner(s, out peer, out epoch) && peer == 10 && epoch == 1, "first grant is epoch 1");
        SeatLeaseTable.Lease lease;
        Check(t.TryGet(s, out lease) && lease.CarId == 9 && lease.SeatIndex == 3 && lease.Seat == s, "lease keeps its key parts");
        Check(t.Allow(s, 10, 0) && !t.Allow(s, 11, 0), "only the holder is allowed");
        Check(t.Verify(), "grant verifies");

        // Refresh by the holder.
        Check(t.TryClaim(s, 10, 1.5, false), "holder refresh allowed");
        Check(t.TryOwner(s, out peer, out epoch) && epoch == 1, "refresh does not burn an epoch");
        Check(!t.TryClaim(s, 11, 1.5, false), "contender refused while the lease is fresh");
        Check(!t.Allow(s, 11, 1.5) && t.Allow(s, 10, 1.5), "refusal changed nothing");
        Check(t.Allow(s, 10, 3.5), "refresh extended the lease (boundary is inclusive)");
        Check(!t.Allow(s, 10, 3.5000001), "holder expires just past the lease window");

        // Stale takeover.
        Check(!t.TryClaim(s, 11, 3.4, false), "not yet stale, so no takeover");
        Check(t.TryClaim(s, 11, 3.6, false), "stale lease can be taken over");
        Check(t.TryOwner(s, out peer, out epoch) && peer == 11 && epoch == 2, "takeover burns a new epoch");
        Check(!t.Allow(s, 10, 3.6) && t.Allow(s, 11, 3.6), "takeover moved the holder");
        Check(t.Verify(), "takeover verifies");

        // Host authority takeover.
        Check(t.TryClaim(s, 12, 3.7, true), "host authority overrides a fresh lease");
        Check(t.TryOwner(s, out peer, out epoch) && peer == 12 && epoch == 3, "host takeover burns a new epoch");
        Check(t.TryClaim(s, 12, 3.8, true), "host re-claim by the holder is a refresh");
        Check(t.TryOwner(s, out peer, out epoch) && epoch == 3, "host refresh does not burn an epoch");

        // Clock rollback cannot refresh into the past or steal a lease.
        Check(t.TryClaim(s, 12, 1.0, false), "holder claim with a rolled-back clock is accepted as a no-op refresh");
        Check(t.Allow(s, 12, 3.9), "rolled-back claim did not move LastSeen backwards");
        Check(!t.Allow(s, 12, 5.9), "stale window still measured from the last real refresh");
        Check(!t.TryClaim(s, 13, 1.0, false), "rolled-back clock cannot steal a fresh lease");

        // Release.
        t.Release(s, 99);
        Check(t.Allow(s, 12, 4.0), "release by a non-holder is a no-op");
        t.Release(s, 12);
        Check(t.Held == 0 && !t.Allow(s, 12, 4.0) && t.Verify(), "holder release frees the seat");
        Check(t.TryClaim(s, 10, 5, false), "released seat can be claimed again");
        Check(t.TryOwner(s, out peer, out epoch) && peer == 10 && epoch == 4, "epochs never regress after a release");
        Check(t.Verify(), "re-claim verifies");

        // Read-only Allow never refreshes.
        var noRefresh = new SeatLeaseTable(8, 2.0);
        ulong k = SeatLeaseTable.SeatKey(4, 0);
        Check(noRefresh.TryClaim(k, 20, 0, false), "read-only fixture grant");
        Check(noRefresh.Allow(k, 20, 2.0) && !noRefresh.Allow(k, 20, 4.0), "Allow observes but never refreshes");

        // Expiry sweep.
        Check(t.Expire(6.9) == 0, "nothing expires before the window");
        Check(t.Expire(7.1) == 1, "expiry sweep frees the stale seat");
        Check(t.Held == 0 && !t.Allow(s, 10, 7.1) && t.Verify(), "expired seat is free");

        // ForgetPeer releases every seat of one peer and nothing else.
        var f = new SeatLeaseTable(8, 2.0);
        ulong a = SeatLeaseTable.SeatKey(1, 0), b = SeatLeaseTable.SeatKey(1, 1), c = SeatLeaseTable.SeatKey(2, 0);
        Check(f.TryClaim(a, 10, 0, false) && f.TryClaim(b, 10, 0, false) && f.TryClaim(c, 20, 0, false), "forget fixture grants");
        f.ForgetPeer(10, 1);
        Check(f.Held == 1 && !f.TryGet(a, out lease) && !f.TryGet(b, out lease), "ForgetPeer released both seats of the peer");
        Check(!f.Allow(a, 10, 1) && f.Allow(c, 20, 1), "ForgetPeer left the other peer alone");
        Check(f.Verify(), "ForgetPeer verifies");
        f.ForgetPeer(0, 1);
        Check(f.Held == 1, "forget of peer 0 is a no-op");
        Check(f.TryClaim(a, 30, 1.5, false), "forgotten seat can be taken immediately");
        Check(f.TryOwner(a, out peer, out epoch) && peer == 30 && epoch == 2, "forget kept the epoch floor");

        // Bounds.
        var small = new SeatLeaseTable(2, 2.0);
        ulong s0 = SeatLeaseTable.SeatKey(5, 0), s1 = SeatLeaseTable.SeatKey(5, 1), s2 = SeatLeaseTable.SeatKey(5, 2);
        Check(small.TryClaim(s0, 1, 0, false) && small.TryClaim(s1, 1, 0, false), "two seats fit");
        Check(!small.TryClaim(s2, 1, 0, false), "capacity is enforced");
        Check(small.Held == 2 && small.EpochFloors <= small.Capacity && small.Verify(), "capacity bound verifies");
        small.Release(s0, 1);
        Check(small.TryClaim(s2, 1, 1, false), "a released slot is reusable");
        Check(small.EpochFloors <= small.Capacity && small.Verify(), "epoch floors stay bounded");
        var clamped = new SeatLeaseTable(0, 0);
        Check(clamped.Capacity == SeatLeaseTable.DefaultMaxSeats && clamped.LeaseSeconds == SeatLeaseTable.DefaultLeaseSeconds, "defaults replace invalid constructor arguments");
        Check(new SeatLeaseTable(-5, double.NaN).Capacity == SeatLeaseTable.DefaultMaxSeats, "negative capacity falls back to the default");
        Check(new SeatLeaseTable(SeatLeaseTable.MaxSeatsLimit * 4, 1).Capacity == SeatLeaseTable.MaxSeatsLimit, "capacity is capped");

        // Counters.
        var counted = new SeatLeaseTable(8, 2.0);
        ulong ck = SeatLeaseTable.SeatKey(7, 0);
        Check(counted.TryClaim(ck, 1, 0, false), "counter fixture grant");
        Check(counted.TryClaim(ck, 1, 0.1, false), "counter fixture refresh");
        Check(!counted.TryClaim(ck, 2, 0.2, false), "counter fixture denial");
        Check(counted.TryClaim(ck, 2, 3.0, false), "counter fixture stale takeover");
        Check(counted.TryClaim(ck, 3, 3.1, true), "counter fixture host takeover");
        counted.Release(ck, 4);
        counted.Release(ck, 3);
        string summary = counted.Summary();
        Check(summary.Contains("grant=1") && summary.Contains("refresh=1"), "summary counts grants and refreshes");
        Check(summary.Contains("denied=1") && summary.Contains("staleTakeover=1") && summary.Contains("hostTakeover=1"), "summary counts refusals and takeovers");
        Check(summary.Contains("release=1") && summary.Contains("reject=1"), "summary counts releases and rejections");
        Check(summary.Contains("held=0") && summary.Contains("capacity=8"), "summary reports the store");
        Check(counted.Verify(), "counted table verifies");
    }

    public static int Main()
    {
        Identity();
        Seats();
        Console.WriteLine("PASS " + checks + " experimental player/seat checks");
        return 0;
    }
}
