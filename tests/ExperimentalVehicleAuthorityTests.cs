using System;
using TldMp.Experimental;

internal static class ExperimentalVehicleAuthorityTests
{
    static int checks;
    static void Check(bool ok) { checks++; if (!ok) throw new Exception("Vehicle authority invariant failed at assertion " + checks); }

    static bool Throws(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return true; }
        return false;
    }

    public static int Main()
    {
        uint[] ids; ulong[] owners; ulong[] epochs;
        ulong owner, epoch;

        // The guest grant wire format is unchanged.
        uint carId; ulong grantOwner, grantEpoch;
        byte[] grant = ExperimentalBackend.EncodeGrant(7, 10, 1);
        Check(grant.Length == ExperimentalBackend.GrantBytes);
        Check(ExperimentalBackend.TryDecodeGrant(grant, out carId, out grantOwner, out grantEpoch) && carId == 7 && grantOwner == 10 && grantEpoch == 1);
        Check(!ExperimentalBackend.TryDecodeGrant(null, out carId, out grantOwner, out grantEpoch));
        Check(ExperimentalBackend.SignatureTag.Length > 0);

        // A lease needs a real peer and a positive duration.
        Check(Throws(delegate { new VehicleAuthority(0, 8, 8, 1.5); }));
        Check(Throws(delegate { new VehicleAuthority(1, 8, 8, 0); }));
        Check(Throws(delegate { new VehicleAuthority(1, 0, 8, 1.5); }));

        // Spawn, same-owner role decrease, and no re-grant for a role change.
        var host = new VehicleAuthority(1, 16, 16, 1.5);
        Check(host.AllowPeer(10, 7, 5, 0));
        Check(host.TakeChanges(out ids, out owners, out epochs) && ids.Length == 1 && ids[0] == 7 && owners[0] == 10 && epochs[0] == 1);
        Check(host.AllowPeer(10, 7, 2, 0.1));
        Check(!host.TakeChanges(out ids, out owners, out epochs));
        Check(host.TryOwner(7, out owner, out epoch) && owner == 10 && epoch == 1);

        // Equal role from another peer never takes the car over.
        Check(!host.AllowPeer(20, 7, 2, 0.2));
        Check(host.Rejected == 1);

        // A strictly higher role does, with a new epoch.
        Check(host.AllowPeer(20, 7, 3, 0.3));
        Check(host.TakeChanges(out ids, out owners, out epochs) && owners[0] == 20 && epochs[0] == 2);
        Check(host.Verify());

        // A fresh lease refuses a lower role...
        Check(!host.AllowPeer(10, 7, 2, 0.4));
        // ...but a stale lease is claimable by anyone: the recorded role is the
        // current role, not a high-water mark, so it can never lock a car out.
        Check(host.AllowPeer(10, 7, 2, 1.9));
        Check(host.TakeChanges(out ids, out owners, out epochs) && owners[0] == 10 && epochs[0] == 3);
        Check(host.Verify());
        Check(host.Summary().IndexOf("cars=", StringComparison.Ordinal) == 0);
        Check(host.Summary().IndexOf("grants=", StringComparison.Ordinal) > 0);
        Check(host.Summary().IndexOf("rejected=", StringComparison.Ordinal) > 0);
        Check(host.Summary().IndexOf("applied=", StringComparison.Ordinal) > 0);
        Check(host.Summary().IndexOf("suppressed=", StringComparison.Ordinal) > 0);
        Check(host.Describe(7).IndexOf("car=7", StringComparison.Ordinal) == 0);
        Check(host.Describe(99).IndexOf("owner=0", StringComparison.Ordinal) > 0);

        // The host reclaims its own car from a guest immediately.
        var local = new VehicleAuthority(1, 16, 16, 1.5);
        Check(local.AllowPeer(10, 7, 4, 0));
        local.TouchLocal(7, 1, 0.1);
        Check(local.TakeChanges(out ids, out owners, out epochs) && ids[0] == 7 && owners[0] == 1 && epochs[0] == 2);
        Check(local.TryOwner(7, out owner, out epoch) && owner == 1 && epoch == 2);
        Check(local.Verify());
        // An unknown host car is spawned owned by the host and granted once.
        local.TouchLocal(8, 0, 0.2);
        Check(local.TryOwner(8, out owner, out epoch) && owner == 1 && epoch == 1);
        Check(local.TakeChanges(out ids, out owners, out epochs) && ids.Length == 1 && ids[0] == 8 && owners[0] == 1);
        Check(!local.TakeChanges(out ids, out owners, out epochs));
        local.TouchLocal(8, 2, 0.3);
        Check(!local.TakeChanges(out ids, out owners, out epochs));
        Check(local.Verify());

        // TakeAll reports every live car and leaves the change list intact.
        var all = new VehicleAuthority(1, 8, 8, 1.5);
        Check(all.AllowPeer(10, 1, 1, 0) && all.AllowPeer(10, 2, 1, 0));
        Check(all.TakeAll(out ids, out owners, out epochs) && ids.Length == 2);
        Check(all.TakeChanges(out ids, out owners, out epochs) && ids.Length == 2);
        Check(!all.TakeChanges(out ids, out owners, out epochs));

        // A departing peer keeps nothing: every car and its group moves back to
        // the host with a new epoch and stays live.
        var departing = new VehicleAuthority(1, 32, 32, 1.5);
        Check(departing.AllowPeer(10, 7, 2, 0));
        Check(departing.AllowPeer(10, 8, 2, 0));
        Check(departing.RebuildGroup(8, new uint[] { 70, 71 }));
        Check(departing.GroupSize(8) == 2);
        Check(departing.AllowPeer(11, 9, 2, 0));
        Check(departing.TakeChanges(out ids, out owners, out epochs) && ids.Length == 3);
        departing.ForgetPeer(10, 0.5);
        Check(departing.TakeChanges(out ids, out owners, out epochs) && ids.Length == 2);
        Check(departing.TryOwner(7, out owner, out epoch) && owner == 1 && epoch == 2);
        Check(departing.TryOwner(8, out owner, out epoch) && owner == 1 && epoch == 2);
        Check(departing.TryOwner(70, out owner, out epoch) && owner == 1 && epoch == 2);
        Check(departing.TryOwner(71, out owner, out epoch) && owner == 1 && epoch == 2);
        Check(departing.TryOwner(9, out owner, out epoch) && owner == 11 && epoch == 1);
        Check(departing.Verify());

        // A split group cannot move atomically, so the departure falls back to
        // the car alone and the contents it can still prove belong to the peer.
        var split = new VehicleAuthority(1, 32, 32, 1.5);
        Check(split.AllowPeer(10, 7, 2, 0));
        Check(split.RebuildGroup(7, new uint[] { 20, 21 }));
        Check(split.GroupSize(7) == 2);
        Check(split.AllowPeer(30, 20, 6, 0.1));
        split.ForgetPeer(10, 0.2);
        Check(split.TryOwner(7, out owner, out epoch) && owner == 1 && epoch == 2);
        Check(split.TryOwner(21, out owner, out epoch) && owner == 1 && epoch == 2);
        Check(split.TryOwner(20, out owner, out epoch) && owner == 30);
        Check(split.GroupSize(7) == 0);
        Check(split.Verify());

        // Caps and duplicate spawns are refused without half-applying anything.
        var capped = new VehicleAuthority(1, 2, 2, 1.5);
        Check(capped.AllowPeer(10, 1, 1, 0));
        Check(capped.AllowPeer(10, 2, 1, 0));
        Check(!capped.AllowPeer(10, 3, 1, 0));
        Check(capped.Rejected == 1);
        Check(capped.Count == 2 && capped.Verify());
        Check(capped.AllowPeer(10, 1, 2, 0));
        // Self, duplicate, zero and over-cap members are excluded, not guessed.
        Check(capped.RebuildGroup(1, new uint[] { 1, 2, 2, 0, 3 }));
        Check(capped.GroupSize(1) == 1);
        Check(capped.Excluded >= 4);
        Check(capped.Verify());

        // A group transfer is all-or-nothing. One member owned by another peer
        // refuses the whole takeover, so the car is never granted alone.
        var group = new VehicleAuthority(1, 32, 32, 1.5);
        int warnings = 0;
        group.Warning = delegate(string text) { warnings++; };
        Check(group.AllowPeer(10, 7, 2, 0));
        Check(group.RebuildGroup(7, new uint[] { 20, 21 }));
        Check(group.GroupSize(7) == 2);
        Check(group.TakeChanges(out ids, out owners, out epochs) && ids.Length == 1 && ids[0] == 7);
        // Peer 30 wins member 20 outright, so the car's group is now split.
        Check(group.AllowPeer(30, 20, 6, 0.1));
        Check(group.TakeChanges(out ids, out owners, out epochs) && ids.Length == 1 && ids[0] == 20 && owners[0] == 30);
        Check(!group.AllowPeer(30, 7, 3, 0.2));
        Check(group.Rejected == 1);
        Check(group.Refused == 1);
        Check(warnings == 1);
        Check(!group.TakeChanges(out ids, out owners, out epochs));
        Check(group.TryOwner(7, out owner, out epoch) && owner == 10 && epoch == 1);
        Check(group.TryOwner(21, out owner, out epoch) && owner == 10 && epoch == 1);
        Check(group.Verify());
        // Dropping the split member restores the atomic move.
        Check(group.RebuildGroup(7, new uint[] { 21 }));
        Check(group.GroupSize(7) == 1);
        Check(group.AllowPeer(30, 7, 3, 0.3));
        Check(group.TakeChanges(out ids, out owners, out epochs) && ids.Length == 1 && ids[0] == 7 && owners[0] == 30);
        Check(group.TryOwner(21, out owner, out epoch) && owner == 30 && epoch == 2);
        Check(group.Verify());

        Console.WriteLine("PASS: vehicle authority leases, takeovers, groups and expiry");
        return 0;
    }
}
