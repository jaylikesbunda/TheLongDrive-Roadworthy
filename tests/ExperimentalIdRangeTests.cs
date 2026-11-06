using System;
using System.Collections.Generic;
using TldMp.Experimental;

internal static class ExperimentalIdRangeTests
{
    static void Check(bool ok) { if (!ok) throw new Exception("ID range invariant failed"); }

    // Minimal allocator that keeps a reservation token per ID, like a real
    // session allocator would under its own lock.
    sealed class Allocator
    {
        internal readonly HashSet<uint> Used = new HashSet<uint>();
        internal readonly Dictionary<uint, Guid> Owner = new Dictionary<uint, Guid>();
        internal int Reserves, Releases;
        internal bool Occupied(uint id) { return Used.Contains(id); }
        internal bool Reserve(uint first, uint count, Guid token)
        {
            for (uint id = first; id < first + count; id++) if (Used.Contains(id)) return false;
            for (uint id = first; id < first + count; id++) { Used.Add(id); Owner[id] = token; }
            Reserves++;
            return true;
        }
        internal bool Release(uint first, uint count, Guid token)
        {
            Guid held;
            if (count == 0 || !Owner.TryGetValue(first, out held) || held != token) return false;
            for (uint id = first; id < first + count; id++) { Used.Remove(id); Owner.Remove(id); }
            Releases++;
            return true;
        }
    }

    public static int Main()
    {
        Dictionary<uint, uint> map;
        Func<uint, bool> clear = delegate(uint id) { return false; };
        Check(IdRangePreflight.TryMap(new uint[] { 8, 3 }, 20, 21, clear, out map) && map[8] == 20 && map[3] == 21);
        Check(!IdRangePreflight.TryMap(new uint[] { 8, 8 }, 20, 21, clear, out map) && map == null);
        Check(!IdRangePreflight.TryMap(new uint[] { 8, 0 }, 20, 21, clear, out map));
        Check(!IdRangePreflight.TryMap(new uint[] { 8, 3 }, uint.MaxValue, uint.MaxValue, clear, out map));
        Check(!IdRangePreflight.TryMap(new uint[] { 8, 3 }, 20, 21, delegate(uint id) { return id == 21; }, out map));
        Console.WriteLine("PASS: experimental whole-group ID range preflight");

        var allocator = new Allocator();
        IdRangeLease lease, second;
        // Zero, empty, negative, oversized and overflowing ranges are rejected
        // before the allocator is touched.
        Check(!IdRangeLease.TryAcquire(0, 2, allocator.Occupied, allocator.Reserve, allocator.Release, out lease) && lease == null);
        Check(!IdRangeLease.TryAcquire(20, 0, allocator.Occupied, allocator.Reserve, allocator.Release, out lease));
        Check(!IdRangeLease.TryAcquire(20, -1, allocator.Occupied, allocator.Reserve, allocator.Release, out lease));
        Check(!IdRangeLease.TryAcquire(20, IdRangeLease.MaxCount + 1, allocator.Occupied, allocator.Reserve, allocator.Release, out lease));
        Check(!IdRangeLease.TryAcquire(uint.MaxValue, 2, allocator.Occupied, allocator.Reserve, allocator.Release, out lease));
        Check(!IdRangeLease.TryAcquire(20, 2, null, allocator.Reserve, allocator.Release, out lease));
        Check(!IdRangeLease.TryAcquire(20, 2, allocator.Occupied, null, allocator.Release, out lease));
        Check(allocator.Reserves == 0 && allocator.Used.Count == 0 && lease == null);

        Check(IdRangeLease.TryAcquire(20, 2, allocator.Occupied, allocator.Reserve, allocator.Release, out lease));
        Check(lease.IsHeld && lease.First == 20 && lease.Last == 21 && lease.Count == 2 && lease.Token != Guid.Empty);
        Check(allocator.Reserves == 1 && allocator.Used.Contains(20) && allocator.Used.Contains(21));
        Check(lease.Owns(20) && lease.Owns(21) && !lease.Owns(19) && !lease.Owns(22) && !lease.OccupiedByOther(20));
        // The allocator refuses an overlapping range while the lease is held.
        Check(!IdRangeLease.TryAcquire(21, 2, allocator.Occupied, allocator.Reserve, allocator.Release, out second) &&
            second == null && allocator.Reserves == 1 && allocator.Used.Count == 2);

        // Mapping inside the lease uses the existing preflight rules.
        Check(lease.TryMap(new uint[] { 8, 3 }, out map) && map[8] == 20 && map[3] == 21);
        Check(!lease.TryMap(new uint[] { 8, 8 }, out map) && map == null);
        Check(!lease.TryMap(new uint[] { 8, 0 }, out map) && map == null);
        Check(!lease.TryMap(new uint[] { 8, 3, 5 }, out map) && map == null);
        Check(!lease.TryMap(null, out map) && map == null);
        Check(!lease.TryMap(new uint[0], out map) && map == null);

        // Release is idempotent and frees exactly its own reservation.
        Check(lease.Release() && !lease.IsHeld && allocator.Releases == 1 && allocator.Used.Count == 0);
        Check(lease.Release() && allocator.Releases == 1 && allocator.Used.Count == 0);
        lease.Dispose();
        Check(allocator.Releases == 1);
        Check(!lease.TryMap(new uint[] { 8 }, out map) && !lease.Owns(20));

        // A second lease over the same allocator cannot free the first one.
        Check(IdRangeLease.TryAcquire(40, 2, allocator.Occupied, allocator.Reserve, allocator.Release, out second));
        Check(IdRangeLease.TryAcquire(20, 2, allocator.Occupied, allocator.Reserve, allocator.Release, out lease));
        Check(second.Token != lease.Token);
        Check(lease.Release() && allocator.Used.Contains(40) && allocator.Used.Contains(41) && allocator.Releases == 2);
        Check(second.Release() && allocator.Used.Count == 0 && allocator.Releases == 3);
        Check(second.Release() && allocator.Releases == 3);

        // A lease whose allocator no longer holds the token reports the loss once.
        var foreign = new Allocator();
        IdRangeLease lost;
        Check(IdRangeLease.TryAcquire(60, 2, foreign.Occupied, foreign.Reserve, foreign.Release, out lost));
        foreign.Used.Clear();
        foreign.Owner.Clear();
        Check(!lost.Release() && !lost.IsHeld && lost.Release());

        // Group-sized leases map the largest supported detached group (4096 items).
        var wide = new Allocator();
        IdRangeLease whole;
        Check(IdRangeLease.TryAcquire(1000, 4096, wide.Occupied, wide.Reserve, wide.Release, out whole));
        Check(whole.Last == 1000 + 4096 - 1);
        whole.Dispose();
        Check(wide.Used.Count == 0);
        Console.WriteLine("PASS: experimental ID range lease reserves, maps and releases atomically");
        return 0;
    }
}
