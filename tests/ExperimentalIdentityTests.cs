using System;
using TldMp.Experimental;

internal static class ExperimentalIdentityTests
{
    static void Check(bool ok) { if (!ok) throw new Exception("Identity invariant failed"); }
    public static int Main()
    {
        var ids = new IdentityMap(); uint local; ulong network, token, duplicate, next, generation;
        Check(!ids.Register(0) && ids.Register(1) && ids.Register(2) && !ids.Register(1));
        Check(!ids.Bind(3, 7, out token) && !ids.Bind(1, 0, out token));
        Check(ids.Bind(1, 7, out token) && ids.Bind(1, 7, out duplicate) && duplicate == token);
        Check(!ids.Bind(1, 8, out duplicate) && !ids.Bind(2, 7, out duplicate));
        Check(ids.TryLocal(1, out local) && local == 7 && ids.TryNetwork(7, out network) && network == 1);
        Check(!ids.Unbind(1, 8, token) && ids.Unbind(1, 7, token));
        Check(ids.Bind(1, 7, out next) && next != token && !ids.Unbind(1, 7, token));
        Check(ids.Unbind(1, 7, next));
        Check(!ids.TryNetwork(7, out network) && ids.Bind(1, 8, out next) && ids.Bind(2, 7, out duplicate));
        Check(ids.Remove(1) && !ids.Register(1) && !ids.TryNetwork(8, out network));
        Console.WriteLine("PASS: experimental streaming identity bijection and tombstones");

        // Far store: stream-out drops only the local binding.
        var far = new IdentityMap();
        Check(far.Register(10) && far.EntityCount == 1 && far.BoundCount == 0 && far.TombstoneCount == 0 && far.Count == 1);
        Check(far.Bind(10, 100, out token) && far.BoundCount == 1);
        Check(far.IsCurrent(10, 100, token) && far.TryGeneration(10, out generation) && generation == token);
        Check(far.Unbind(10, 100, token) && far.Knows(10) && !far.IsRemoved(10));
        Check(far.BoundCount == 0 && far.EntityCount == 1 && far.Count == 1);
        Check(!far.IsCurrent(10, 100, token) && !far.TryGeneration(10, out generation));
        Check(!far.TryLocal(10, out local) && !far.TryNetwork(100, out network));

        // Rebinding takes a new generation; the stale generation is rejected.
        Check(far.Bind(10, 100, out next) && next != token && far.IsCurrent(10, 100, next));
        Check(!far.IsCurrent(10, 100, token) && !far.Unbind(10, 100, token) && far.Unbind(10, 100, next));

        // A new local ID after stream-out keeps the same network identity.
        Check(far.TryReuse(10, 200, out next) && next != token);
        Check(far.TryLocal(10, out local) && local == 200 && far.TryNetwork(200, out network) && network == 10);
        Check(!far.TryReuse(10, 201, out duplicate) && far.TryLocal(10, out local) && local == 200);
        ulong reused = next;
        Check(far.TryGeneration(10, out generation) && generation == reused && far.Unbind(10, 200, reused));
        Check(far.TryReuse(10, 200, out next) && next != reused);
        Check(far.TryGeneration(10, out generation) && generation == next && far.IsCurrent(10, 200, next));

        // Duplicate local IDs stay rejected in both directions.
        Check(far.Register(11) && !far.TryReuse(11, 200, out duplicate) && !far.Bind(11, 200, out duplicate));
        Check(far.Bind(11, 201, out token) && far.BoundCount == 2);
        Check(far.TryNetwork(201, out network) && network == 11);

        // Remove tombstones the identity: no rebind, no reuse, no local mapping.
        Check(far.Remove(10) && far.IsRemoved(10) && !far.Knows(10) && far.TombstoneCount == 1);
        Check(!far.Register(10) && !far.Bind(10, 202, out duplicate) && !far.TryReuse(10, 202, out duplicate));
        Check(!far.IsCurrent(10, 200, next) && !far.TryLocal(10, out local) && !far.TryNetwork(200, out network));
        Check(far.EntityCount == 1 && far.TombstoneCount == 1 && far.BoundCount == 1 && far.Count == 2);

        // Bijection: every bound local maps back to its own network identity.
        var bij = new IdentityMap(16);
        ulong boundToken;
        for (uint i = 1; i <= 8; i++)
            Check(bij.Register(1000UL + i) && bij.Bind(1000UL + i, i, out boundToken));
        Check(bij.BoundCount == 8 && bij.EntityCount == 8 && bij.Remaining == 8);
        for (uint i = 1; i <= 8; i++)
        {
            Check(bij.TryLocal(1000UL + i, out local) && local == i);
            Check(bij.TryNetwork(i, out network) && network == 1000UL + i);
            Check(bij.TryGeneration(1000UL + i, out generation) && bij.IsCurrent(1000UL + i, i, generation));
        }

        // Bounded entity/tombstone accounting.
        var bounded = new IdentityMap(2);
        Check(bounded.Capacity == 2 && bounded.Remaining == 2 && !bounded.IsFull);
        Check(bounded.Register(1) && bounded.Register(2) && bounded.IsFull && bounded.Remaining == 0);
        Check(!bounded.Register(3) && bounded.EntityCount == 2 && bounded.Count == 2);
        Check(bounded.Remove(1) && bounded.IsFull && !bounded.Register(3));
        Check(bounded.EntityCount == 1 && bounded.TombstoneCount == 1 && bounded.Count == 2);
        Check(!bounded.Register(0));
        Console.WriteLine("PASS: experimental identity far-store semantics, generations and bounded accounting");
        return 0;
    }
}
