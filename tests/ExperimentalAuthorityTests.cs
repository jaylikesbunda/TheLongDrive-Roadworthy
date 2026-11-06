using System;
using TldMp.Experimental;

internal static class ExperimentalAuthorityTests
{
    static void Check(bool ok) { if (!ok) throw new Exception("Experimental authority invariant failed"); }
    public static int Main()
    {
        var registry = new AuthorityRegistry();
        Check(!registry.Add(0, 10) && !registry.Add(1, 0));
        Check(registry.Add(1, 10) && !registry.Add(1, 20));
        Check(registry.Accepts(1, 10, 1) && !registry.Accepts(1, 20, 1));
        ulong epoch;
        Check(!registry.Transfer(1, 0, 20, out epoch));
        Check(registry.Transfer(1, 1, 20, out epoch) && epoch == 2);
        Check(!registry.Transfer(1, 1, 30, out epoch));
        Check(!registry.Accepts(1, 10, 1) && registry.Accepts(1, 20, 2));
        Check(!registry.Remove(1, 1) && registry.Remove(1, 2));
        Check(!registry.Accepts(1, 20, 2) && !registry.Add(1, 30));
        Check(registry.Add(2, 10) && registry.Add(3, 10));
        Check(!registry.TransferGroup(new ulong[] { 2, 3 }, new ulong[] { 1, 2 }, 10, 20));
        Check(registry.Accepts(2, 10, 1) && registry.Accepts(3, 10, 1));
        Check(!registry.TransferGroup(new ulong[] { 2, 2 }, new ulong[] { 1, 1 }, 10, 20));
        Check(registry.TransferGroup(new ulong[] { 2, 3 }, new ulong[] { 1, 1 }, 10, 20));
        Check(registry.Accepts(2, 20, 2) && registry.Accepts(3, 20, 2));
        Console.WriteLine("PASS: experimental authority epochs and tombstones");
        return 0;
    }
}
