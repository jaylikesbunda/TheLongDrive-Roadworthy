using System;

namespace TldMp.Experimental
{
    // Shared by all receivers owned by the same session manager; single thread.
    // Reservations transfer to a completed snapshot lease until it is disposed.
    internal sealed class SnapshotBudget
    {
        readonly int maxTransfers, maxBytes;
        int transfers, bytes;
        internal SnapshotBudget(int transferLimit, int byteLimit)
        {
            if (transferLimit <= 0 || byteLimit <= 0) throw new ArgumentException("Invalid snapshot budget.");
            maxTransfers = transferLimit; maxBytes = byteLimit;
        }
        internal bool Reserve(int size)
        {
            if (size <= 0 || transfers >= maxTransfers || size > maxBytes - bytes) return false;
            transfers++; bytes += size; return true;
        }
        internal void Release(int size) { transfers--; bytes -= size; }
    }
    internal sealed class SnapshotLease : IDisposable
    {
        // The consumer must not retain Bytes after Dispose. Cancellation/teardown
        // must dispose every completed lease; the managed array cannot be revoked.
        readonly SnapshotBudget budget;
        byte[] bytes;
        internal SnapshotLease(SnapshotBudget owner, byte[] data) { budget = owner; bytes = data; }
        internal byte[] Bytes { get { return bytes; } }
        public void Dispose()
        {
            if (bytes == null) return;
            int size = bytes.Length; bytes = null; budget.Release(size);
        }
    }
    // Client-side, one host snapshot at a time, called on the session thread.
    // The caller must separately bound the number of live sessions and validate
    // message sequencing. Hashes do not authenticate the sender.
    internal sealed class SnapshotReceiver
    {
        readonly PeerSession host;
        readonly SnapshotBudget budget;
        SnapshotAssembly active;
        int reservedBytes;
        ulong revision;
        ulong transfer, lastTransfer;
        double deadline;
        double lastTime;
        bool hasTime;
        internal SnapshotReceiver(PeerSession hostSession, SnapshotBudget sharedBudget) { host = hostSession; budget = sharedBudget; }
        static bool ValidTime(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        bool Advance(double now)
        {
            if (!ValidTime(now) || (hasTime && now < lastTime)) return false;
            lastTime = now; hasTime = true; return true;
        }
        internal bool Begin(ulong sender, ulong nonce, ulong transferId, ulong snapshotRevision, int length, byte[] hash, double now, double timeout)
        {
            if (active != null || host == null || budget == null || !host.Accepts(sender, nonce) || transferId == 0 || transferId <= lastTransfer || snapshotRevision == 0 ||
                length <= 0 || length > SnapshotAssembly.MaxBytes || hash == null || hash.Length != 32 ||
                !ValidTime(now) || !ValidTime(timeout) || timeout <= 0 || !ValidTime(now + timeout) || !Advance(now)) return false;
            if (!budget.Reserve(length)) return false;
            try { active = new SnapshotAssembly(length, hash); }
            catch { budget.Release(length); throw; }
            reservedBytes = length; transfer = transferId; lastTransfer = transferId; revision = snapshotRevision; deadline = now + timeout;
            return true;
        }
        internal bool Append(ulong sender, ulong nonce, ulong transferId, ulong snapshotRevision, int offset, byte[] bytes, double now)
        {
            if (active == null) return false;
            if (host == null || host.Phase != SessionPhase.Ready) { Cancel(); return false; }
            if (!host.Accepts(sender, nonce) || transfer != transferId || revision != snapshotRevision) return false;
            if (!Advance(now)) { Cancel(); return false; }
            if (now >= deadline) { Cancel(); return false; }
            if (!active.Append(offset, bytes)) { Cancel(); return false; }
            return true;
        }
        internal bool Finish(ulong sender, ulong nonce, ulong transferId, ulong snapshotRevision, double now, out SnapshotLease lease)
        {
            lease = null;
            if (active == null) return false;
            if (host == null || host.Phase != SessionPhase.Ready) { Cancel(); return false; }
            if (!host.Accepts(sender, nonce) || transfer != transferId || revision != snapshotRevision) return false;
            if (!Advance(now)) { Cancel(); return false; }
            if (now >= deadline) { Cancel(); return false; }
            byte[] bytes;
            if (!active.TryFinish(out bytes)) { if (active.IsClosed) Cancel(); return false; }
            try { lease = new SnapshotLease(budget, bytes); }
            catch { Cancel(); throw; }
            active = null; reservedBytes = 0; transfer = 0; revision = 0; deadline = 0; return true;
        }
        internal void Tick(double now)
        {
            if (active == null) return;
            if (host == null || host.Phase != SessionPhase.Ready || !Advance(now) || now >= deadline) Cancel();
        }
        void ReleaseBudget() { if (reservedBytes != 0) { budget.Release(reservedBytes); reservedBytes = 0; } }
        internal void Cancel() { if (active != null) active.Cancel(); active = null; ReleaseBudget(); transfer = 0; revision = 0; deadline = 0; }
    }
}
