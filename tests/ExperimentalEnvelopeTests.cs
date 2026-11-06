using System;
using TldMp.Experimental;

internal static class ExperimentalEnvelopeTests
{
    static void Check(bool ok) { if (!ok) throw new Exception("Envelope framing invariant failed"); }
    public static int Main()
    {
        byte[] frame = EnvelopeCodec.Write(11, 5, 2, new byte[] { 1, 2, 3 });
        ulong nonce, seq; byte kind; byte[] body;
        Check(EnvelopeCodec.Read(frame, out nonce, out seq, out kind, out body) && nonce == 11 && seq == 5 && kind == 2 && body.Length == 3);
        for (int length = 0; length < frame.Length; length++)
        {
            var truncated = new byte[length]; Array.Copy(frame, truncated, length);
            Check(!EnvelopeCodec.Read(truncated, out nonce, out seq, out kind, out body) && body == null);
        }
        var extra = new byte[frame.Length + 1]; Array.Copy(frame, extra, frame.Length);
        Check(!EnvelopeCodec.Read(extra, out nonce, out seq, out kind, out body));
        var corrupt = (byte[])frame.Clone(); corrupt[21] = 255; corrupt[22] = 255; corrupt[23] = 255; corrupt[24] = 127;
        Check(!EnvelopeCodec.Read(corrupt, out nonce, out seq, out kind, out body));
        Check(EnvelopeCodec.Read(EnvelopeCodec.Write(1, 1, 1, new byte[EnvelopeCodec.MaxPayload]), out nonce, out seq, out kind, out body));
        Console.WriteLine("PASS: experimental envelope bounds and framing");
        return 0;
    }
}
