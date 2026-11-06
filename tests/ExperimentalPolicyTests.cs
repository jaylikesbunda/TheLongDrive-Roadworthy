using System;
using TldMp.Experimental;

internal static class ExperimentalPolicyTests
{
    static int checks;
    static void Check(bool ok) { checks++; if (!ok) throw new Exception("Message policy invariant failed at assertion " + checks); }
    public static int Main()
    {
        for (byte kind = 1; kind <= 10; kind++) Check(MessagePolicy.Known(kind));
        Check(!MessagePolicy.Known(0) && !MessagePolicy.Known(11) && !MessagePolicy.Known(99));
        Check(MessagePolicy.RequiresReliable(MessagePolicy.Event));
        Check(!MessagePolicy.RequiresReliable(MessagePolicy.Motion));
        for (byte kind = 3; kind <= 10; kind++) Check(MessagePolicy.RequiresReliable(kind));

        Check(MessagePolicy.Allows(MessagePolicy.Event, PeerRole.Host) && MessagePolicy.Allows(MessagePolicy.Event, PeerRole.Guest));
        Check(MessagePolicy.Allows(MessagePolicy.Motion, PeerRole.Host) && MessagePolicy.Allows(MessagePolicy.Motion, PeerRole.Guest));
        Check(MessagePolicy.Allows(MessagePolicy.AuthorityGrant, PeerRole.Guest));
        Check(!MessagePolicy.Allows(MessagePolicy.AuthorityGrant, PeerRole.Host));
        Check(MessagePolicy.Allows(MessagePolicy.HandoffReady, PeerRole.Host));
        Check(!MessagePolicy.Allows(MessagePolicy.HandoffReady, PeerRole.Guest));
        Check(!MessagePolicy.Allows(MessagePolicy.Event, PeerRole.None));
        Check(!MessagePolicy.Allows(99, PeerRole.Guest));

        var fingerprint = new byte[32];
        var peer = new PeerSession(); Check(peer.Begin(100, 77));
        Check(peer.Admit(100, HelloCodec.Write(77, fingerprint), fingerprint));
        int validated = 0;
        var guard = new ConnectionGuard(peer, delegate(ulong sender, byte kind, byte[] data) { validated++; return true; }, PeerRole.Guest);
        byte received; byte[] payload;

        Check(guard.Receive(100, EnvelopeCodec.Write(77, 1, MessagePolicy.AuthorityGrant, new byte[0]), true, out received, out payload) == SequenceResult.Accepted && received == MessagePolicy.AuthorityGrant);
        Check(validated == 1);
        // A guest may not send a host-only decision; the reliable sequence is untouched.
        Check(guard.Receive(100, EnvelopeCodec.Write(77, 2, MessagePolicy.HandoffReady, new byte[0]), true, out received, out payload) == SequenceResult.Invalid);
        Check(validated == 1);
        Check(guard.Receive(100, EnvelopeCodec.Write(77, 2, MessagePolicy.Event, new byte[0]), true, out received, out payload) == SequenceResult.Accepted && received == MessagePolicy.Event);
        // The channel must match the kind in both directions.
        Check(guard.Receive(100, EnvelopeCodec.Write(77, 3, MessagePolicy.Motion, new byte[0]), true, out received, out payload) == SequenceResult.Invalid);
        Check(guard.Receive(100, EnvelopeCodec.Write(77, 3, MessagePolicy.Event, new byte[0]), false, out received, out payload) == SequenceResult.Invalid);
        Check(guard.Receive(100, EnvelopeCodec.Write(77, 3, MessagePolicy.SnapshotBegin, new byte[0]), false, out received, out payload) == SequenceResult.Invalid);
        // Unknown kinds never reach the payload validator.
        Check(guard.Receive(100, EnvelopeCodec.Write(77, 3, 99, new byte[0]), true, out received, out payload) == SequenceResult.Invalid);
        Check(validated == 2);
        Check(guard.Receive(100, EnvelopeCodec.Write(77, 3, MessagePolicy.SnapshotBegin, new byte[0]), true, out received, out payload) == SequenceResult.Accepted && received == MessagePolicy.SnapshotBegin);

        // A host receives guest-originated kinds only.
        var hostPeer = new PeerSession(); Check(hostPeer.Begin(200, 88));
        Check(hostPeer.Admit(200, HelloCodec.Write(88, fingerprint), fingerprint));
        var hostGuard = new ConnectionGuard(hostPeer, delegate(ulong sender, byte kind, byte[] data) { return true; }, PeerRole.Host);
        Check(hostGuard.Receive(200, EnvelopeCodec.Write(88, 1, MessagePolicy.HandoffReady, new byte[0]), true, out received, out payload) == SequenceResult.Accepted);
        Check(hostGuard.Receive(200, EnvelopeCodec.Write(88, 2, MessagePolicy.AuthorityGrant, new byte[0]), true, out received, out payload) == SequenceResult.Invalid);
        Check(hostGuard.Receive(200, EnvelopeCodec.Write(88, 2, MessagePolicy.Event, new byte[0]), true, out received, out payload) == SequenceResult.Accepted);

        Console.WriteLine("PASS: message kinds, channels and sender roles are enforced");
        return 0;
    }
}
