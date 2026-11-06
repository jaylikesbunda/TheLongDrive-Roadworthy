using System;
using TldMp;

namespace TldMp.Experimental
{
    // Development loopback adapter. Owns no Unity objects or save state.
    // Pump on the session thread; a transport-authenticated Steam identity is
    // NOT provided by loopback's synthetic IDs, so never use this for Steam.
    internal sealed class LoopbackPeer : IDisposable
    {
        readonly LoopbackTransport transport;
        readonly bool isHost;
        readonly ulong expectedPeer;
        readonly byte[] fingerprint;
        readonly Func<ulong, byte, byte[], bool> validate;
        readonly ChallengeIssuer issuer = new ChallengeIssuer(1024);
        PeerSession session;
        ConnectionGuard guard;
        ulong reliableSequence, motionSequence;
        bool connected, disposed;
        internal string Error { get; private set; }
        internal bool Ready { get { return session != null && session.Phase == SessionPhase.Ready && Error == null; } }
        internal Action<byte, byte[]> Received;

        internal LoopbackPeer(LoopbackTransport socket, bool host, byte[] contentFingerprint,
            Func<ulong, byte, byte[], bool> validator)
        {
            if (socket == null || contentFingerprint == null || contentFingerprint.Length != 32 || validator == null)
                throw new ArgumentException("Invalid loopback peer configuration.");
            transport = socket; isHost = host; expectedPeer = host ? LoopbackConfig.ClientID : LoopbackConfig.HostID;
            fingerprint = (byte[])contentFingerprint.Clone(); validate = validator;
        }
        void Fail(string reason)
        {
            Error = reason; if (session != null) session.Close(); connected = false;
        }
        void Connected(ulong peer)
        {
            if (peer != expectedPeer || connected || Error != null) { Fail("Unexpected loopback connection."); return; }
            connected = true; reliableSequence = motionSequence = 0; session = null; guard = null;
            if (!isHost) return;
            ulong nonce;
            if (!issuer.TryIssue(out nonce)) { Fail("Challenge budget exhausted."); return; }
            session = new PeerSession();
            if (!session.Begin(peer, nonce) || !transport.Send(peer, HelloCodec.Write(nonce, fingerprint), true))
                Fail("Could not start loopback handshake.");
        }
        void OnBytes(LoopbackEvent item)
        {
            if (!connected || item.From != expectedPeer || item.Bytes == null) { Fail("Unexpected loopback data."); return; }
            if (!Ready)
            {
                if (!item.Reliable) { Fail("Unreliable handshake."); return; }
                ulong nonce;
                if (!HelloCodec.Read(item.Bytes, fingerprint, out nonce)) { Fail("Experimental handshake mismatch."); return; }
                if (!isHost)
                {
                    if (session != null) { Fail("Duplicate handshake."); return; }
                    session = new PeerSession();
                    if (!session.Begin(item.From, nonce) || !session.Admit(item.From, item.Bytes, fingerprint) ||
                        !transport.Send(item.From, HelloCodec.Write(nonce, fingerprint), true))
                    { Fail("Could not acknowledge handshake."); return; }
                }
                else if (session == null || !session.Admit(item.From, item.Bytes, fingerprint))
                { Fail("Experimental handshake rejected."); return; }
                guard = new ConnectionGuard(session, validate, isHost ? PeerRole.Host : PeerRole.Guest);
                return;
            }
            byte kind; byte[] payload;
            var result = guard.Receive(item.From, item.Bytes, item.Reliable, out kind, out payload);
            if (result == SequenceResult.DuplicateOrOld) return;
            if (result != SequenceResult.Accepted) { Fail("Experimental packet rejected: " + result); return; }
            if (Received != null) Received(kind, payload);
        }
        internal void Pump()
        {
            if (disposed || Error != null) return;
            LoopbackEvent item;
            while (Error == null && transport.TryDequeue(out item))
            {
                if (item.Connected) Connected(item.From);
                else if (item.Disconnected) { if (session != null) session.Close(); session = null; guard = null; connected = false; }
                else OnBytes(item);
            }
        }
        internal bool Send(byte kind, byte[] payload)
        {
            if (!Ready || !MessagePolicy.Allows(kind, isHost ? PeerRole.Guest : PeerRole.Host)) return false;
            bool reliable = MessagePolicy.RequiresReliable(kind);
            ulong previous = reliable ? reliableSequence : motionSequence;
            if (previous >= ulong.MaxValue - 1) { Fail("Sequence exhausted; reconnect required."); return false; }
            byte[] frame;
            try { frame = EnvelopeCodec.Write(session.Nonce, previous + 1, kind, payload); }
            catch (ArgumentException) { return false; }
            if (!transport.Send(expectedPeer, frame, reliable)) return false;
            if (reliable) reliableSequence++; else motionSequence++;
            return true;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if (session != null) session.Close(); issuer.Dispose();
        }
    }
}
