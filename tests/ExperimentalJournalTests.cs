using System;
using TldMp.Experimental;

internal static class ExperimentalJournalTests
{
    static void Check(bool ok) { if (!ok) throw new Exception("Journal invariant failed"); }
    public static int Main()
    {
        var journal = new SnapshotJournal(10, 2, 4); byte[][] events; ulong revision;
        var gap = new SnapshotJournal(10, 2, 4);
        Check(!gap.Append(12, new byte[] { 1 }) && !gap.TryCopy(out revision, out events));
        byte[] first = { 1, 2 }; Check(journal.Append(11, first)); first[0] = 9;
        Check(journal.Append(12, new byte[] { 3, 4 }));
        Check(journal.TryCopy(out revision, out events) && revision == 12 && events[0][0] == 1);
        events[0][0] = 8; Check(journal.TryCopy(out revision, out events) && events[0][0] == 1);
        Check(!journal.Append(13, new byte[] { 5 }) && !journal.TryCopy(out revision, out events));
        Console.WriteLine("PASS: bounded ordered join journal fails closed");
        return 0;
    }
}
