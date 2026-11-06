using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace TldMp.Experimental
{
    // Development-only wire contract. This is not connected to game transport.
    internal static class HelloCodec
    {
        const uint Magic = 0x31585052; // RPX1, little endian
        internal const ushort Protocol = 1;
        internal const int FingerprintBytes = 32;
        internal const int Length = 4 + 2 + 8 + FingerprintBytes;

        internal static byte[] Write(ulong nonce, byte[] fingerprint)
        {
            if (nonce == 0 || fingerprint == null || fingerprint.Length != FingerprintBytes)
                throw new ArgumentException("A session nonce and 32-byte fingerprint are required.");
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(Magic); writer.Write(Protocol); writer.Write(nonce); writer.Write(fingerprint);
                return stream.ToArray();
            }
        }

        internal static bool Read(byte[] data, byte[] expectedFingerprint, out ulong nonce)
        {
            nonce = 0;
            if (data == null || data.Length != Length || expectedFingerprint == null || expectedFingerprint.Length != FingerprintBytes) return false;
            using (var reader = new BinaryReader(new MemoryStream(data), Encoding.UTF8))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != Protocol) return false;
                ulong value = reader.ReadUInt64();
                byte[] actual = reader.ReadBytes(FingerprintBytes);
                int difference = 0;
                for (int i = 0; i < FingerprintBytes; i++) difference |= actual[i] ^ expectedFingerprint[i];
                if (value == 0 || difference != 0) return false;
                nonce = value;
                return true;
            }
        }
    }

    internal enum SessionPhase { Disconnected, Handshake, Ready, Closed }

    // Retain issued challenges until the host session ends. Bound memory and
    // reject further joins on exhaustion rather than reusing a challenge.
    internal sealed class ChallengeIssuer : IDisposable
    {
        readonly RandomNumberGenerator rng = RandomNumberGenerator.Create();
        readonly HashSet<ulong> issued = new HashSet<ulong>();
        readonly int limit;
        internal ChallengeIssuer(int maxIssued) { if (maxIssued < 1) throw new ArgumentOutOfRangeException("maxIssued"); limit = maxIssued; }
        internal bool TryIssue(out ulong challenge)
        {
            challenge = 0;
            if (issued.Count >= limit) return false;
            byte[] bytes = new byte[8];
            for (int i = 0; i < 32; i++)
            {
                rng.GetBytes(bytes);
                ulong value = BitConverter.ToUInt64(bytes, 0);
                if (value != 0 && issued.Add(value)) { challenge = value; return true; }
            }
            return false;
        }
        public void Dispose() { rng.Dispose(); }
    }

    // The transport supplies authenticated identities; claimed IDs in a packet
    // are never used as authentication. A connection may be admitted only once.
    internal sealed class PeerSession
    {
        internal SessionPhase Phase { get; private set; }
        internal ulong Peer { get; private set; }
        internal ulong Nonce { get; private set; }
        ulong expectedNonce;

        // The caller supplies a fresh, nonzero host-issued challenge; this class
        // does not generate it. Never reuse a challenge for another connection.
        internal bool Begin(ulong authenticatedPeer, ulong challenge)
        {
            if (Phase != SessionPhase.Disconnected || authenticatedPeer == 0 || challenge == 0) return false;
            Peer = authenticatedPeer;
            expectedNonce = challenge;
            Phase = SessionPhase.Handshake;
            return true;
        }
        internal bool Admit(ulong authenticatedPeer, byte[] hello, byte[] expectedFingerprint)
        {
            ulong nonce;
            if (Phase != SessionPhase.Handshake || authenticatedPeer != Peer ||
                !HelloCodec.Read(hello, expectedFingerprint, out nonce) || nonce != expectedNonce) return false;
            Nonce = nonce;
            Phase = SessionPhase.Ready;
            return true;
        }
        internal bool Accepts(ulong authenticatedPeer, ulong nonce)
        {
            return Phase == SessionPhase.Ready && Peer == authenticatedPeer && Nonce == nonce;
        }
        internal void Close()
        {
            Peer = 0; Nonce = 0; expectedNonce = 0; Phase = SessionPhase.Closed;
        }
    }
}
