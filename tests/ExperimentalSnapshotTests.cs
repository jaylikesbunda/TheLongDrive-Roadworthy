using System;
using System.Security.Cryptography;
using TldMp.Experimental;

internal static class ExperimentalSnapshotTests
{
    static void Check(bool ok) { if (!ok) throw new Exception("Snapshot assembly invariant failed"); }
    public static int Main()
    {
        byte[] source = { 1, 2, 3, 4 }; byte[] hash;
        using (var sha = SHA256.Create()) hash = sha.ComputeHash(source);
        bool rejected = false;
        try { new SnapshotAssembly(SnapshotAssembly.MaxBytes + 1, hash); } catch (ArgumentException) { rejected = true; }
        Check(rejected);
        var snapshot = new SnapshotAssembly(4, hash); byte[] result;
        Check(!snapshot.Append(1, new byte[] { 2 }) && snapshot.Append(0, new byte[] { 1, 2 }));
        Check(snapshot.Append(0, new byte[] { 1, 2 }) && !snapshot.Append(0, new byte[] { 1, 9 }));
        Check(!snapshot.TryFinish(out result) && snapshot.Append(2, new byte[] { 3, 4 }));
        Check(snapshot.TryFinish(out result) && result.Length == 4 && result[3] == 4);
        Check(!snapshot.TryFinish(out result) && !snapshot.Append(0, new byte[] { 1 }));
        var bad = new SnapshotAssembly(4, new byte[32]);
        Check(bad.Append(0, source) && !bad.TryFinish(out result) && !bad.Append(0, source));
        var cancelled = new SnapshotAssembly(4, hash); cancelled.Cancel();
        Check(!cancelled.Append(0, source));
        Console.WriteLine("PASS: bounded snapshot assembly and integrity");
        return 0;
    }
}
