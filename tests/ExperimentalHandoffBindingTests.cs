using System;
using TldMp.Experimental;

internal static class ExperimentalHandoffBindingTests
{
    static int checks;
    static void Check(bool ok) { checks++; if (!ok) throw new Exception("Bound handoff invariant failed at assertion " + checks); }

    static PeerSession Ready(ulong peer, ulong nonce, byte[] fingerprint)
    {
        var session = new PeerSession();
        if (!session.Begin(peer, nonce) || !session.Admit(peer, HelloCodec.Write(nonce, fingerprint), fingerprint))
            throw new Exception("Could not admit test peer.");
        return session;
    }

    public static int Main()
    {
        var host = new EntityCoordinator(true, 8, 8);
        Check(host.Spawn(1, 10) && host.Spawn(2, 1, 10) && host.Verify());

        var fingerprint = new byte[32];
        var source = Ready(10, 5, fingerprint);
        var target = Ready(20, 6, fingerprint);
        var stranger = Ready(30, 7, fingerprint);
        var pending = new PeerSession(); Check(pending.Begin(40, 8));
        var digest = new byte[32]; digest[3] = 9;
        Handoff hand;

        // Both peers must be authenticated and bound to the transfer endpoints.
        Check(!host.TryBeginHandoff(1, 1, 10, 20, source, target, null, 100, out hand));
        Check(!host.TryBeginHandoff(1, 1, 10, 20, source, target, new byte[31], 100, out hand));
        Check(!host.TryBeginHandoff(1, 1, 10, 20, source, stranger, digest, 100, out hand));
        Check(!host.TryBeginHandoff(1, 1, 10, 20, pending, target, digest, 100, out hand));
        Check(!host.TryBeginHandoff(0, 1, 10, 20, source, target, digest, 100, out hand));
        Check(!host.TryBeginHandoff(1, 0, 10, 20, source, target, digest, 100, out hand));
        Check(!host.TryBeginHandoff(1, 1, 10, 10, source, target, digest, 100, out hand));
        Check(!host.HasActiveHandoff);

        Check(host.TryBeginHandoff(7, 1, 10, 20, source, target, digest, 100, out hand));
        Check(host.HasActiveHandoff);
        // Only one transfer may be in flight per coordinator.
        Handoff extra;
        Check(!host.TryBeginHandoff(8, 1, 10, 20, source, target, digest, 100, out extra));

        // Source authentication and the graph snapshot must both match.
        Check(!host.PrepareHandoff(hand, 20, 5));
        Check(!host.PrepareHandoff(hand, 10, 6));
        Check(!host.PrepareHandoff(extra, 10, 5));
        Check(host.PrepareHandoff(hand, 10, 5));
        Check(!host.Accepts(1, 10, 1) && !host.Accepts(2, 10, 1));
        Check(host.PrepareHandoff(hand, 10, 5));

        // The target must echo the digest of the state it actually applied.
        var wrong = new byte[32]; wrong[0] = 1;
        Check(!host.ReadyHandoff(hand, 20, 6, wrong, 1));
        Check(!host.ReadyHandoff(hand, 20, 6, null, 1));
        Check(!host.ReadyHandoff(hand, 10, 5, digest, 1));
        Check(!host.CommitHandoff(hand, 1));
        Check(host.ReadyHandoff(hand, 20, 6, digest, 1));
        Check(host.CommitHandoff(hand, 2));
        Check(!host.HasActiveHandoff && host.Verify());
        Check(host.Accepts(1, 20, 2) && host.Accepts(2, 20, 2));
        // Late source traffic and repeated commits are rejected.
        Check(!host.Accepts(1, 10, 1));
        Check(!host.CommitHandoff(hand, 3));

        // A reverse transfer needs the endpoints and sessions reversed.
        Handoff reverse;
        Check(!host.TryBeginHandoff(9, 1, 10, 20, source, target, digest, 100, out reverse));
        Check(host.TryBeginHandoff(9, 1, 20, 10, target, source, digest, 100, out reverse));
        Check(host.PrepareHandoff(reverse, 20, 6));
        // Expiry aborts, unfreezes the source and clears the in-flight transfer.
        Check(!host.ReadyHandoff(reverse, 10, 5, digest, 100));
        Check(!host.HasActiveHandoff && host.Accepts(1, 20, 2) && host.Verify());

        Handoff timed;
        Check(host.TryBeginHandoff(10, 1, 20, 10, target, source, digest, 5, out timed));
        Check(host.PrepareHandoff(timed, 20, 6));
        Check(!host.TickHandoff(4) && !host.Accepts(1, 20, 2));
        Check(host.TickHandoff(5) && !host.HasActiveHandoff && host.Accepts(1, 20, 2));
        Check(!timed.Tick(5) && host.Verify());

        Console.WriteLine("PASS: handoff binds authenticated peers, applied digest and deadline");
        return 0;
    }
}
