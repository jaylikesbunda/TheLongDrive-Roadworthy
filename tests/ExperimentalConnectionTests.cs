using System;
using TldMp.Experimental;

internal static class ExperimentalConnectionTests
{
    static void Check(bool ok) { if (!ok) throw new Exception("Connection admission invariant failed"); }
    public static int Main()
    {
        var fingerprint = new byte[32];
        var session = new PeerSession();
        Check(session.Begin(123, 42));
        int validations = 0;
        var guard = new ConnectionGuard(session, delegate(ulong peer, byte type, byte[] bytes) { validations++; return peer == 123 && (type == 2 || bytes.Length == 1); });
        byte kind; byte[] body;
        byte[] first = EnvelopeCodec.Write(42, 1, 1, new byte[] { 7 });
        Check(guard.Receive(123, first, true, out kind, out body) == SequenceResult.Invalid);
        Check(session.Admit(123, HelloCodec.Write(42, fingerprint), fingerprint));
        var failing = new ConnectionGuard(session, delegate(ulong who, byte type, byte[] bytes) { throw new InvalidOperationException(); });
        Check(failing.Receive(123, first, true, out kind, out body) == SequenceResult.Invalid);
        Check(guard.Receive(124, first, true, out kind, out body) == SequenceResult.Invalid);
        Check(guard.Receive(123, EnvelopeCodec.Write(41, 1, 1, new byte[0]), true, out kind, out body) == SequenceResult.Invalid);
        Check(guard.Receive(123, EnvelopeCodec.Write(42, 1, 99, new byte[0]), true, out kind, out body) == SequenceResult.Invalid);
        Check(guard.Receive(123, EnvelopeCodec.Write(42, 1, 2, new byte[0]), true, out kind, out body) == SequenceResult.Invalid);
        Check(guard.Receive(123, first, false, out kind, out body) == SequenceResult.Invalid);
        Check(guard.Receive(123, EnvelopeCodec.Write(42, 1, 1, new byte[0]), true, out kind, out body) == SequenceResult.Invalid);
        Check(guard.Receive(123, first, true, out kind, out body) == SequenceResult.Accepted && kind == 1 && body[0] == 7);
        int beforeDuplicate = validations;
        Check(guard.Receive(123, first, true, out kind, out body) == SequenceResult.DuplicateOrOld && body == null);
        Check(validations == beforeDuplicate);
        Check(guard.Receive(123, EnvelopeCodec.Write(42, 3, 1, new byte[] { 7 }), true, out kind, out body) == SequenceResult.Gap);
        Check(guard.Receive(123, EnvelopeCodec.Write(42, 8, 2, new byte[0]), false, out kind, out body) == SequenceResult.Accepted);
        Check(guard.Receive(123, EnvelopeCodec.Write(42, 7, 2, new byte[0]), false, out kind, out body) == SequenceResult.DuplicateOrOld);
        session.Close();
        Check(guard.Receive(123, first, true, out kind, out body) == SequenceResult.Invalid);
        Console.WriteLine("PASS: experimental connection admission and ordering");
        return 0;
    }
}
