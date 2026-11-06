using System;
using System.IO;
using System.Text;

namespace TldMp.Experimental
{
    // Framing only: the dispatcher still validates message type, sender role,
    // sequence ordering and the PeerSession before applying any gameplay data.
    internal static class EnvelopeCodec
    {
        const uint Magic = 0x31455052; // RPE1
        internal const int MaxPayload = 60 * 1024;
        const int Header = 4 + 8 + 8 + 1 + 4;
        internal static byte[] Write(ulong nonce, ulong sequence, byte kind, byte[] payload)
        {
            if (nonce == 0 || sequence == 0 || kind == 0 || payload == null || payload.Length > MaxPayload)
                throw new ArgumentException("Invalid experimental envelope.");
            using (var stream = new MemoryStream(Header + payload.Length))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(Magic); writer.Write(nonce); writer.Write(sequence); writer.Write(kind);
                writer.Write(payload.Length); writer.Write(payload);
                return stream.ToArray();
            }
        }
        internal static bool Read(byte[] frame, out ulong nonce, out ulong sequence, out byte kind, out byte[] payload)
        {
            nonce = sequence = 0; kind = 0; payload = null;
            if (frame == null || frame.Length < Header || frame.Length > Header + MaxPayload) return false;
            using (var reader = new BinaryReader(new MemoryStream(frame), Encoding.UTF8))
            {
                if (reader.ReadUInt32() != Magic) return false;
                ulong n = reader.ReadUInt64(), s = reader.ReadUInt64(); byte k = reader.ReadByte(); int len = reader.ReadInt32();
                if (n == 0 || s == 0 || k == 0 || len < 0 || len > MaxPayload || len != frame.Length - Header) return false;
                byte[] body = reader.ReadBytes(len);
                nonce = n; sequence = s; kind = k; payload = body;
                return true;
            }
        }
    }
}
