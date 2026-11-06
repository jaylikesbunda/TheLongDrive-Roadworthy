using System;
using TldMp;

namespace TldMp.Experimental
{
    // Mode-gated experimental world-join transfer. The world frame itself, its JSON
    // encoding and its apply path are the native ones (StableGameBridge.CaptureWorldBytes
    // / TimedApplyWorld); only the snapshot transfer differs: three frames
    // (ExpSnapshotBegin/Part/End) instead of the native Begin/Part/End pair, with
    // explicit offsets and a SHA-256 of the whole frame.
    //
    // The gate is deliberately narrow and off by default:
    //   TLD_MP_EXPERIMENTAL_JOIN=1 AND the experimental backend is active AND loopback.
    // Steam sessions stay on the native transfer until it is validated there.
    //
    // Frame layout (StableMessage envelope, Op = 26/27/28, Epoch = transfer id):
    //   Begin  Data = int32 total | 32-byte SHA-256 of the encoded world frame
    //   Part   Data = int32 offset | chunk (offset % ChunkBytes == 0, chunk length is
    //                exactly Min(ChunkBytes, total - offset), so every part is bounded)
    //   End    Data = empty
    // Nothing here touches Unity or the game: it is the same code the console tests
    // run, so the wire format is covered without the editor.
    internal static class ExperimentalJoin
    {
        // Same chunk payload as the native snapshot/spawn transfer, so one part stays
        // far below StableMessage's 58000-byte data cap and the transport MTU.
        internal const int ChunkBytes = 48000;
        // Bounded transfer size: the native world transfer limit. SnapshotAssembly
        // enforces the same bound before it allocates anything.
        internal const int MaxSnapshotBytes = SnapshotAssembly.MaxBytes;
        internal const int BeginDataBytes = 4 + 32;

        internal static bool GateOn(string value, bool experimentalActive, bool loopback)
        {
            return value == "1" && experimentalActive && loopback;
        }

        internal static bool IsSnapshotOp(StableOp op)
        {
            return op == StableOp.ExpSnapshotBegin || op == StableOp.ExpSnapshotPart || op == StableOp.ExpSnapshotEnd;
        }

        internal static byte[] BeginData(int total, byte[] hash)
        {
            var data = new byte[BeginDataBytes];
            Buffer.BlockCopy(BitConverter.GetBytes(total), 0, data, 0, 4);
            Buffer.BlockCopy(hash, 0, data, 4, 32);
            return data;
        }

        internal static bool ReadBegin(byte[] data, out int total, out byte[] hash)
        {
            total = 0; hash = null;
            if (data == null || data.Length != BeginDataBytes) return false;
            int size = BitConverter.ToInt32(data, 0);
            if (size <= 0 || size > MaxSnapshotBytes) return false;
            hash = new byte[32];
            Buffer.BlockCopy(data, 4, hash, 0, 32);
            total = size;
            return true;
        }

        // Number of parts a bounded snapshot of this size needs; used to bound work
        // before any frame is emitted.
        internal static int PartCount(int total)
        {
            if (total <= 0 || total > MaxSnapshotBytes) return 0;
            return (total + ChunkBytes - 1) / ChunkBytes;
        }

        internal static byte[] PartData(byte[] source, int offset)
        {
            int count = Math.Min(ChunkBytes, source.Length - offset);
            var data = new byte[4 + count];
            Buffer.BlockCopy(BitConverter.GetBytes(offset), 0, data, 0, 4);
            Buffer.BlockCopy(source, offset, data, 4, count);
            return data;
        }

        // Exact-length validation: the offset must be chunk aligned, inside the
        // declared total, and the chunk must be exactly the length that offset
        // implies. Nothing here allocates.
        internal static bool TryReadPart(byte[] data, int total, out int offset, out int count)
        {
            offset = 0; count = 0;
            if (data == null || data.Length < 5 || total <= 0 || total > MaxSnapshotBytes) return false;
            int at = BitConverter.ToInt32(data, 0);
            if (at < 0 || at >= total || at % ChunkBytes != 0) return false;
            int expected = Math.Min(ChunkBytes, total - at);
            if (data.Length != 4 + expected) return false;
            offset = at; count = expected;
            return true;
        }

        // Emits the complete frame sequence for one encoded world snapshot. Returns
        // false (and emits nothing) when the snapshot or its hash is out of bounds.
        internal static bool EmitSnapshot(byte[] bytes, byte[] hash, Action<StableOp, byte[]> emit)
        {
            if (emit == null || bytes == null || bytes.Length <= 0 || bytes.Length > MaxSnapshotBytes) return false;
            if (hash == null || hash.Length != 32) return false;
            emit(StableOp.ExpSnapshotBegin, BeginData(bytes.Length, hash));
            for (int offset = 0; offset < bytes.Length; offset += ChunkBytes)
                emit(StableOp.ExpSnapshotPart, PartData(bytes, offset));
            emit(StableOp.ExpSnapshotEnd, new byte[0]);
            return true;
        }
    }

    // Guest-side reassembly for one experimental transfer, driven from the session's
    // receive path (single thread). Metadata is validated before the single snapshot
    // array is allocated, only the active transfer can append, and the transfer id
    // watermark makes a replayed old Begin a no-op after a completed transfer.
    internal sealed class ExperimentalJoinReceiver
    {
        SnapshotAssembly assembly;
        ulong transfer, watermark;
        int total;

        internal bool Active { get { return assembly != null; } }
        internal ulong Transfer { get { return transfer; } }
        internal int Total { get { return total; } }
        // Highest transfer id this session has accepted; never exposed to the node.
        internal ulong Watermark { get { return watermark; } }

        internal void Cancel()
        {
            if (assembly != null) assembly.Cancel();
            assembly = null; transfer = 0; total = 0;
        }

        internal bool Begin(ulong transferId, byte[] data)
        {
            // A replayed or stale Begin is ignored before it can disturb the transfer
            // that is already running (the native path uses the same watermark rule).
            if (transferId == 0 || transferId <= watermark) return false;
            int size; byte[] hash;
            if (!ExperimentalJoin.ReadBegin(data, out size, out hash)) return false;
            Cancel();
            try { assembly = new SnapshotAssembly(size, hash); }
            catch (ArgumentException) { assembly = null; return false; }
            transfer = transferId; total = size; watermark = transferId;
            return true;
        }

        internal bool Part(ulong transferId, byte[] data)
        {
            if (assembly == null || transferId != transfer) return false;
            int offset, count;
            if (!ExperimentalJoin.TryReadPart(data, total, out offset, out count)) { Cancel(); return false; }
            var chunk = new byte[count];
            Buffer.BlockCopy(data, 4, chunk, 0, count);
            // A duplicate already-accepted chunk must match byte for byte; anything
            // out of order, overlapping or oversized closes the assembly.
            if (!assembly.Append(offset, chunk)) { Cancel(); return false; }
            return true;
        }

        internal bool End(ulong transferId, out byte[] snapshot)
        {
            snapshot = null;
            if (assembly == null || transferId != transfer) return false;
            byte[] bytes;
            if (!assembly.TryFinish(out bytes)) { Cancel(); return false; }
            snapshot = bytes;
            assembly = null; transfer = 0; total = 0;
            return true;
        }
    }
}
