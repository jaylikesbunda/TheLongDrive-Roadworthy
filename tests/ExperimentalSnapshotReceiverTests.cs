using System;
using System.Security.Cryptography;
using TldMp.Experimental;

internal static class ExperimentalSnapshotReceiverTests
{
    static void Check(bool ok) { if (!ok) throw new Exception("Snapshot receiver invariant failed"); }
    public static int Main()
    {
        byte[] fingerprint = new byte[32], data = { 1, 2, 3 }, hash;
        using (var sha = SHA256.Create()) hash = sha.ComputeHash(data);
        var host = new PeerSession(); Check(host.Begin(10, 77) && host.Admit(10, HelloCodec.Write(77, fingerprint), fingerprint));
        var budget = new SnapshotBudget(1, 3);
        var receiver = new SnapshotReceiver(host, budget);
        var other = new SnapshotReceiver(host, budget); SnapshotLease result;
        Check(!receiver.Begin(11, 77, 1, 1, 3, hash, 0, 10));
        Check(!receiver.Begin(10, 78, 1, 1, 3, hash, 0, 10));
        Check(receiver.Begin(10, 77, 1, 1, 3, hash, 0, 10));
        Check(!other.Begin(10, 77, 1, 1, 3, hash, 0, 10));
        Check(!receiver.Begin(10, 77, 2, 2, 3, hash, 0, 10));
        Check(!receiver.Append(11, 77, 1, 1, 0, data, 1) && !receiver.Append(10, 77, 2, 1, 0, data, 1));
        Check(!receiver.Append(10, 78, 1, 1, 0, data, 1));
        Check(receiver.Append(10, 77, 1, 1, 0, data, 1));
        Check(receiver.Finish(10, 77, 1, 1, 2, out result) && result.Bytes.Length == 3);
        Check(!other.Begin(10, 77, 1, 1, 3, hash, 2, 10));
        result.Dispose(); result.Dispose();
        Check(other.Begin(10, 77, 1, 1, 3, hash, 2, 10)); other.Cancel();
        Check(!receiver.Begin(10, 77, 1, 1, 3, hash, 3, 10));
        Check(!receiver.Begin(10, 77, 2, 2, 3, hash, 1, 10));
        Check(receiver.Begin(10, 77, 2, 2, 3, hash, 3, 10));
        Check(!receiver.Append(10, 77, 2, 2, 0, data, 13));
        Check(receiver.Begin(10, 77, 3, 3, 3, hash, 14, 10));
        receiver.Tick(24);
        Check(receiver.Begin(10, 77, 4, 4, 3, new byte[32], 25, 10));
        Check(receiver.Append(10, 77, 4, 4, 0, data, 26) && !receiver.Finish(10, 77, 4, 4, 27, out result));
        Check(receiver.Begin(10, 77, 5, 5, 3, hash, 28, 10));
        receiver.Tick(double.NaN);
        Check(receiver.Begin(10, 77, 6, 6, 3, hash, 29, 10));
        Check(!receiver.Append(10, 77, 6, 6, 0, data, 28));
        Check(receiver.Begin(10, 77, 7, 7, 3, hash, 30, 10));
        Check(!receiver.Append(10, 77, 7, 7, 1, data, 31));
        Check(receiver.Begin(10, 77, 8, 8, 3, hash, 32, 10));
        host.Close(); receiver.Tick(33); Check(!receiver.Append(10, 77, 8, 8, 0, data, 33));
        Console.WriteLine("PASS: authenticated, bounded, expiring snapshot reception");
        return 0;
    }
}
