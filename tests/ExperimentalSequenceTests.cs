using System;
using TldMp.Experimental;

internal static class ExperimentalSequenceTests
{
    static void Check(bool ok) { if (!ok) throw new Exception("Sequence invariant failed"); }
    public static int Main()
    {
        var reliable = new SequenceGate(true);
        Check(reliable.Accept(0) == SequenceResult.Invalid);
        Check(reliable.Accept(2) == SequenceResult.Gap);
        Check(reliable.Accept(1) == SequenceResult.Accepted);
        Check(reliable.Accept(1) == SequenceResult.DuplicateOrOld);
        Check(reliable.Accept(3) == SequenceResult.Gap);
        Check(reliable.Accept(2) == SequenceResult.Accepted && reliable.Accept(3) == SequenceResult.Accepted);
        var motion = new SequenceGate(false);
        Check(motion.Accept(ulong.MaxValue) == SequenceResult.Invalid);
        Check(motion.Accept(20) == SequenceResult.Accepted && motion.Accept(19) == SequenceResult.DuplicateOrOld);
        Check(motion.Accept(22) == SequenceResult.Accepted);
        Console.WriteLine("PASS: experimental reliable gaps and motion sequencing");
        return 0;
    }
}
