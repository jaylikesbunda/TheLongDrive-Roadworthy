using System;
using TldMp.Experimental;

internal static class ExperimentalSessionTests
{
    static void Check(bool ok) { if (!ok) throw new Exception("Experimental session invariant failed"); }
    public static int Main()
    {
        var fingerprint = new byte[32]; fingerprint[0] = 13;
        byte[] hello = HelloCodec.Write(42, fingerprint);
        ulong nonce;
        Check(HelloCodec.Read(hello, fingerprint, out nonce) && nonce == 42);
        var mismatch = new byte[32];
        Check(!HelloCodec.Read(hello, mismatch, out nonce));
        Check(!HelloCodec.Read(new byte[1], fingerprint, out nonce));
        var extended = new byte[hello.Length + 1]; Array.Copy(hello, extended, hello.Length);
        Check(!HelloCodec.Read(extended, fingerprint, out nonce));
        var corrupt = (byte[])hello.Clone(); corrupt[0] ^= 1;
        Check(!HelloCodec.Read(corrupt, fingerprint, out nonce));
        corrupt = (byte[])hello.Clone(); corrupt[4] ^= 1;
        Check(!HelloCodec.Read(corrupt, fingerprint, out nonce));
        corrupt = (byte[])hello.Clone();
        for (int i = 6; i < 14; i++) corrupt[i] = 0;
        Check(!HelloCodec.Read(corrupt, fingerprint, out nonce) && nonce == 0);
        for (int size = 0; size < hello.Length; size++)
        {
            var truncated = new byte[size]; Array.Copy(hello, truncated, size);
            Check(!HelloCodec.Read(truncated, fingerprint, out nonce) && nonce == 0);
        }
        for (int i = 14; i < hello.Length; i++)
        {
            corrupt = (byte[])hello.Clone(); corrupt[i] ^= 1;
            Check(!HelloCodec.Read(corrupt, fingerprint, out nonce) && nonce == 0);
        }
        var session = new PeerSession();
        using (var issuer = new ChallengeIssuer(2))
        {
            ulong first, second, extra;
            Check(issuer.TryIssue(out first) && first != 0);
            Check(issuer.TryIssue(out second) && second != 0 && first != second);
            Check(!issuer.TryIssue(out extra) && extra == 0);
            var old = new PeerSession();
            Check(old.Begin(55, second) && !old.Admit(55, HelloCodec.Write(first, fingerprint), fingerprint));
        }
        Check(!session.Begin(0, 42) && !session.Begin(55, 0) && session.Begin(55, 42) && !session.Begin(55, 42));
        Check(!session.Admit(56, hello, fingerprint));
        Check(!session.Admit(55, HelloCodec.Write(41, fingerprint), fingerprint));
        Check(!session.Accepts(55, 42));
        Check(session.Admit(55, hello, fingerprint));
        Check(session.Accepts(55, 42) && !session.Accepts(55, 43) && !session.Accepts(56, 42));
        Check(!session.Admit(55, hello, fingerprint));
        session.Close();
        Check(!session.Accepts(55, 42) && !session.Begin(55, 42));
        Console.WriteLine("PASS: experimental handshake and session admission");
        return 0;
    }
}
