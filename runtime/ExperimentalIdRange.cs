using System;
using System.Collections.Generic;

namespace TldMp.Experimental
{
    // Preflight only; it never mutates native save records or reserves IDs.
    // The caller must reserve the entire returned range under the allocator's
    // lock before invoking the game's in-place RearrangeID on detached data.
    internal static class IdRangePreflight
    {
        internal static bool TryMap(uint[] source, uint first, uint lastAllowed, Func<uint, bool> occupied,
            out Dictionary<uint, uint> map)
        {
            map = null;
            if (source == null || source.Length == 0 || first == 0 || lastAllowed < first || occupied == null ||
                (ulong)first + (ulong)source.Length - 1UL > lastAllowed) return false;
            var candidate = new Dictionary<uint, uint>();
            for (int i = 0; i < source.Length; i++)
            {
                uint target = first + (uint)i;
                if (source[i] == 0 || candidate.ContainsKey(source[i]) || occupied(target)) return false;
                candidate.Add(source[i], target);
            }
            map = candidate;
            return true;
        }
    }

    // One contiguous uint range reserved atomically through caller-supplied
    // allocator callbacks. The lease owns a unique token, so Release can only
    // ever free its own reservation, and it keeps no static allocator state.
    // The allocator callbacks are the caller's: isOccupied reports conflicts,
    // tryReserve must reserve the whole block under the allocator's lock (or
    // fail without reserving anything) and tryRelease must free the block only
    // when the token still matches the stored reservation.
    internal sealed class IdRangeLease : IDisposable
    {
        // Sanity bound for one detached group; not a game or session limit.
        internal const int MaxCount = 1 << 16;
        readonly uint first;
        readonly int count;
        readonly Guid token;
        readonly Func<uint, bool> isOccupied;
        readonly Func<uint, uint, Guid, bool> tryRelease;
        bool held;

        internal uint First { get { return first; } }
        internal uint Last { get { return first + (uint)(count - 1); } }
        internal int Count { get { return count; } }
        internal Guid Token { get { return token; } }
        internal bool IsHeld { get { return held; } }

        IdRangeLease(uint first, int count, Guid token, Func<uint, bool> isOccupied,
            Func<uint, uint, Guid, bool> tryRelease)
        {
            this.first = first;
            this.count = count;
            this.token = token;
            this.isOccupied = isOccupied;
            this.tryRelease = tryRelease;
            held = true;
        }

        // Rejects zero, negative, oversized, overflowing and occupied ranges and
        // never leaves a partial reservation behind.
        internal static bool TryAcquire(uint first, int count, Func<uint, bool> isOccupied,
            Func<uint, uint, Guid, bool> tryReserve, Func<uint, uint, Guid, bool> tryRelease,
            out IdRangeLease lease)
        {
            lease = null;
            if (first == 0 || count <= 0 || count > MaxCount || isOccupied == null || tryReserve == null) return false;
            ulong last = (ulong)first + (ulong)count - 1UL;
            if (last > uint.MaxValue) return false;
            for (uint id = first; id <= (uint)last; id++)
            {
                if (isOccupied(id)) return false;
                if (id == uint.MaxValue) break;
            }
            var reserved = Guid.NewGuid();
            bool acquired;
            try { acquired = tryReserve(first, (uint)count, reserved); }
            catch (Exception) { return false; }
            if (!acquired) return false;
            lease = new IdRangeLease(first, count, reserved, isOccupied, tryRelease);
            return true;
        }

        internal bool Owns(uint id)
        {
            return held && id >= first && (ulong)id <= (ulong)first + (ulong)count - 1UL;
        }

        // A target inside the lease is owned by this caller, so a conflict can
        // only come from outside the reserved block.
        internal bool OccupiedByOther(uint id)
        {
            return held && !Owns(id) && isOccupied != null && isOccupied(id);
        }

        // Same rules as IdRangePreflight: no zero/duplicate source IDs and the
        // whole source must fit inside this lease.
        internal bool TryMap(uint[] source, out Dictionary<uint, uint> map)
        {
            map = null;
            if (!held) return false;
            return IdRangePreflight.TryMap(source, first, Last, delegate(uint id) { return OccupiedByOther(id); }, out map);
        }

        // Idempotent: a second Release/Dispose is a no-op and cannot free any
        // other lease's reservation. Returns false only when the allocator
        // refuses the token (its reservation is already gone or not ours).
        internal bool Release()
        {
            if (!held) return true;
            held = false;
            if (tryRelease == null) return true;
            try { return tryRelease(first, (uint)count, token); }
            catch (Exception) { return false; }
        }

        public void Dispose() { Release(); }
    }
}
