using System;
using System.Threading;
using TldMp.Experimental;

internal static class ExperimentalCoordinatorTests
{
    static int checks;
    static void Check(bool ok) { checks++; if (!ok) throw new Exception("Entity coordinator invariant failed at assertion " + checks); }
    public static int Main()
    {
        var host = new EntityCoordinator(true, 4, 2);
        Check(host.IsHost && host.LiveCount == 0 && host.TombstoneCount == 0 && host.Verify());
        Check(!host.Spawn(0, 10) && !host.Spawn(1, 0));
        Check(!host.Spawn(1, 0, 10) && host.LiveCount == 0);
        Check(host.Spawn(1, 10) && host.LiveCount == 1 && host.Verify());
        Check(!host.Spawn(1, 10) && !host.Spawn(1, 20) && host.LiveCount == 1);
        Check(host.Spawn(2, 1, 10) && host.LiveCount == 2 && host.Verify());
        Check(!host.Spawn(3, 99, 10) && host.LiveCount == 2 && host.Verify());
        Check(!host.Attach(1, 2) && host.Verify());
        Check(host.Spawn(3, 10) && host.Spawn(4, 3, 10) && host.LiveCount == 4 && host.Verify());
        // Live cap is enforced and rejected spawns leave no partial state.
        Check(!host.Spawn(5, 10) && host.LiveCount == 4 && host.Verify());

        ulong epoch;
        Check(host.TryEpoch(1, 10, out epoch) && epoch == 1);
        Check(!host.TryEpoch(1, 20, out epoch) && epoch == 0);
        Check(host.Accepts(1, 10, 1) && !host.Accepts(1, 20, 1) && !host.Accepts(1, 10, 2));

        ulong token, rejected;
        Check(host.BindLocal(1, 501, out token) && token != 0);
        uint local; ulong network;
        Check(host.TryLocal(1, out local) && local == 501);
        Check(host.TryNetwork(501, out network) && network == 1);
        Check(!host.BindLocal(1, 502, out rejected) && rejected == 0);
        Check(!host.BindLocal(99, 503, out rejected));
        Check(host.UnbindLocal(1, 501, token) && !host.UnbindLocal(1, 501, token));
        Check(!host.TryLocal(1, out local) && host.Verify());

        // Only leaves may be removed, and removal tombstones the ID everywhere.
        Check(!host.Remove(1, 1) && host.LiveCount == 4 && host.Verify());
        Check(!host.Remove(4, 2) && host.Verify());
        Check(host.Remove(4, 1) && host.LiveCount == 3 && host.TombstoneCount == 1 && host.Verify());
        Check(!host.Spawn(4, 10) && host.LiveCount == 3 && host.Verify());
        Check(host.Remove(3, 1) && host.LiveCount == 2 && host.TombstoneCount == 2 && host.Verify());
        Check(!host.TryEpoch(3, 10, out epoch));
        // Tombstone exhaustion ends the session; it never recycles an ID.
        Check(!host.Remove(2, 1) && host.RequiresRekey && host.Verify());
        Check(!host.Spawn(6, 10) && host.LiveCount == 2 && host.Verify());

        var guest = new EntityCoordinator(false, 2, 2);
        Check(!guest.IsHost && !guest.Spawn(1, 10) && !guest.Attach(1, 1) && !guest.Remove(1, 1) && guest.Verify());
        ulong guestToken;
        Check(!guest.BindLocal(1, 10, out guestToken));

        // The owning thread is enforced; a second thread cannot read mutable state.
        var threaded = new EntityCoordinator(true, 2, 2);
        Check(threaded.Spawn(1, 10));
        bool threw = false;
        var worker = new Thread(delegate() { try { int unused = threaded.LiveCount; } catch (InvalidOperationException) { threw = true; } });
        worker.Start(); worker.Join();
        Check(threw && threaded.Verify());

        Console.WriteLine("PASS: coordinator keeps graph, authority and identity atomic and bounded");
        return 0;
    }
}
