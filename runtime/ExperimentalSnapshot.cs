using System;
using System.Security.Cryptography;

namespace TldMp.Experimental
{
    // Reassembles a single bounded snapshot, without applying it to game state.
    // Hashes detect corruption, not malicious senders. Integration must validate
    // the authenticated host/session before allocating, enforce aggregate budgets
    // and timeouts, and cancel on disconnect on the owning session thread.
    // A duplicate already-accepted chunk must match byte for byte.
    internal sealed class SnapshotAssembly
    {
        // Same bound as the native world transfer (StableNode.MaxSnapshotBytes); the
        // experimental join carries the same encoded world frame, so the lower 16 MB
        // cap would have made large worlds untransferable.
        internal const int MaxBytes = 32 * 1024 * 1024;
        readonly byte[] expectedHash;
        byte[] data;
        int received;
        internal bool IsClosed { get { return data == null; } }
        internal SnapshotAssembly(int total, byte[] sha256)
        {
            if (total <= 0 || total > MaxBytes || sha256 == null || sha256.Length != 32)
                throw new ArgumentException("Invalid snapshot metadata.");
            expectedHash = (byte[])sha256.Clone(); data = new byte[total];
        }
        internal bool Append(int offset, byte[] chunk)
        {
            if (data == null || chunk == null || chunk.Length == 0 || offset < 0 || offset > received ||
                (long)offset + chunk.Length > data.Length) return false;
            if (offset < received)
            {
                if ((long)offset + chunk.Length > received) return false;
                for (int i = 0; i < chunk.Length; i++) if (data[offset + i] != chunk[i]) return false;
                return true;
            }
            Buffer.BlockCopy(chunk, 0, data, offset, chunk.Length); received += chunk.Length;
            return true;
        }
        internal bool TryFinish(out byte[] snapshot)
        {
            snapshot = null;
            if (data == null || received != data.Length) return false;
            byte[] actual;
            using (var sha = SHA256.Create()) actual = sha.ComputeHash(data);
            int difference = 0;
            for (int i = 0; i < expectedHash.Length; i++) difference |= expectedHash[i] ^ actual[i];
            if (difference != 0) { data = null; return false; }
            snapshot = data; data = null; return true;
        }
        internal void Cancel() { data = null; }
    }
}
