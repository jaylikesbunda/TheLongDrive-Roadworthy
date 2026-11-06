namespace TldMp.Experimental
{
    internal enum PeerRole : byte { None = 0, Host = 1, Guest = 2 }

    // The transport supplies the authenticated peer identity; this table decides
    // which side may send a kind and on which channel. It is not a substitute for
    // payload validation or entity ownership checks. The channel is chosen by the
    // transport, never read from the application payload.
    internal static class MessagePolicy
    {
        internal const byte Event = 1;
        internal const byte Motion = 2;
        internal const byte AuthorityGrant = 3;
        internal const byte HandoffPrepare = 4;
        internal const byte HandoffReady = 5;
        internal const byte HandoffCommit = 6;
        internal const byte HandoffAbort = 7;
        internal const byte SnapshotBegin = 8;
        internal const byte SnapshotChunk = 9;
        internal const byte SnapshotEnd = 10;

        internal enum SenderSide { HostOnly, GuestOnly, Either }

        internal static bool Known(byte kind)
        {
            return kind >= Event && kind <= SnapshotEnd;
        }

        // Only disposable motion may ride an unreliable channel. Every control,
        // event and authority decision requires reliable delivery.
        internal static bool RequiresReliable(byte kind)
        {
            return kind != Motion;
        }

        internal static SenderSide SenderFor(byte kind)
        {
            switch (kind)
            {
                case AuthorityGrant:
                case HandoffPrepare:
                case HandoffCommit:
                case HandoffAbort:
                case SnapshotBegin:
                case SnapshotChunk:
                case SnapshotEnd:
                    return SenderSide.HostOnly;
                case HandoffReady:
                    return SenderSide.GuestOnly;
                default:
                    return SenderSide.Either;
            }
        }

        // The remote peer's role is the opposite of the local one: a host only
        // ever receives from guests and vice versa.
        internal static bool Allows(byte kind, PeerRole localRole)
        {
            if (localRole != PeerRole.Host && localRole != PeerRole.Guest || !Known(kind)) return false;
            SenderSide side = SenderFor(kind);
            if (side == SenderSide.Either) return true;
            return (localRole == PeerRole.Host) == (side == SenderSide.GuestOnly);
        }
    }
}
