using System;

namespace TldMp.Experimental
{
    // Pure admission and ordering boundary. The transport supplies the sender
    // identity and reliable/motion channel; no gameplay handler is called here.
    // Validator must be side-effect-free; the caller applies only an accepted
    // payload. All checks and sequence updates run on one session thread.
    internal sealed class ConnectionGuard
    {
        // Provisional development kinds; add a role and payload validator before
        // admitting any gameplay message. The channel must come from transport,
        // never from an untrusted application-supplied field.
        internal const byte Event = 1;
        internal const byte Motion = 2;
        readonly PeerSession session;
        readonly Func<ulong, byte, byte[], bool> validatePayload;
        readonly PeerRole localRole;
        readonly SequenceGate reliable = new SequenceGate(true);
        readonly SequenceGate motion = new SequenceGate(false);
        // Legacy two-argument form: only the development Event/Motion kinds are
        // admitted and the caller's channel bit is cross-checked against them.
        internal ConnectionGuard(PeerSession admittedSession, Func<ulong, byte, byte[], bool> validator)
            : this(admittedSession, validator, PeerRole.None) { }
        // Role-aware form: kinds are gated by MessagePolicy so host-only decisions
        // and guest acknowledgements cannot be sent by the wrong side.
        internal ConnectionGuard(PeerSession admittedSession, Func<ulong, byte, byte[], bool> validator, PeerRole role)
        {
            session = admittedSession;
            validatePayload = validator;
            localRole = role;
        }

        internal SequenceResult Receive(ulong authenticatedPeer, byte[] frame, bool isReliable,
            out byte kind, out byte[] payload)
        {
            kind = 0; payload = null;
            ulong nonce, sequence; byte parsedKind; byte[] parsedPayload;
            if (session == null || !EnvelopeCodec.Read(frame, out nonce, out sequence, out parsedKind, out parsedPayload) ||
                !session.Accepts(authenticatedPeer, nonce)) return SequenceResult.Invalid;
            bool channelAllowed = localRole == PeerRole.None
                ? (parsedKind == Event || parsedKind == Motion) && (parsedKind == Event) == isReliable
                : MessagePolicy.Allows(parsedKind, localRole) && MessagePolicy.RequiresReliable(parsedKind) == isReliable;
            if (!channelAllowed || validatePayload == null) return SequenceResult.Invalid;
            SequenceGate gate = isReliable ? reliable : motion;
            SequenceResult preview = gate.Preview(sequence);
            if (preview != SequenceResult.Accepted) return preview;
            try { if (!validatePayload(authenticatedPeer, parsedKind, parsedPayload)) return SequenceResult.Invalid; }
            catch (Exception) { return SequenceResult.Invalid; }
            SequenceResult result = gate.Accept(sequence);
            if (result == SequenceResult.Accepted) { kind = parsedKind; payload = parsedPayload; }
            return result;
        }
    }
}
