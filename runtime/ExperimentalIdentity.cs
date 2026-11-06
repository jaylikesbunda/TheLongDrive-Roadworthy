using System;
using System.Collections.Generic;

namespace TldMp.Experimental
{
    // Session-scoped bijection to locally loaded game IDs. Stream-out removes
    // only the local binding; network identity remains reserved until deletion.
    // Far-store semantics: unbind keeps the entity, a rebind always takes a new
    // generation, and a removed network ID is tombstoned forever, so it can
    // never be rebound or reused. Accounting is bounded only to make a leak
    // visible to callers; the bound is not a game or session limit.
    internal sealed class IdentityMap
    {
        internal const int DefaultMaxTracked = 1 << 16;
        readonly int maxTracked;
        readonly HashSet<ulong> known = new HashSet<ulong>();
        readonly HashSet<ulong> removed = new HashSet<ulong>();
        sealed class Binding { internal uint Local; internal ulong Generation; }
        readonly Dictionary<ulong, Binding> localByNetwork = new Dictionary<ulong, Binding>();
        readonly Dictionary<uint, ulong> networkByLocal = new Dictionary<uint, ulong>();
        ulong generation;

        internal IdentityMap() : this(DefaultMaxTracked) { }
        internal IdentityMap(int maxTracked)
        {
            if (maxTracked < 1) throw new ArgumentOutOfRangeException("maxTracked");
            this.maxTracked = maxTracked;
        }

        internal int Count { get { return known.Count + removed.Count; } }
        internal int EntityCount { get { return known.Count; } }
        internal int TombstoneCount { get { return removed.Count; } }
        internal int BoundCount { get { return localByNetwork.Count; } }
        internal int Capacity { get { return maxTracked; } }
        internal int Remaining { get { return maxTracked - Count; } }
        internal bool IsFull { get { return Count >= maxTracked; } }

        internal bool Knows(ulong networkId) { return known.Contains(networkId); }
        internal bool IsRemoved(ulong networkId) { return removed.Contains(networkId); }

        internal bool Register(ulong networkId)
        {
            return networkId != 0 && !IsFull && !removed.Contains(networkId) && known.Add(networkId);
        }

        internal bool Bind(ulong networkId, uint localId, out ulong token)
        {
            token = 0;
            if (localId == 0 || removed.Contains(networkId) || !known.Contains(networkId)) return false;
            return TryAttach(networkId, localId, out token);
        }

        // Far-store reuse: a network entity that streamed out keeps its identity
        // but has no local binding, so activation attaches a new local ID and a
        // fresh generation. Removed or currently bound entities are refused.
        internal bool TryReuse(ulong networkId, uint localId, out ulong token)
        {
            token = 0;
            if (localId == 0 || removed.Contains(networkId) || !known.Contains(networkId)) return false;
            if (localByNetwork.ContainsKey(networkId)) return false;
            return TryAttach(networkId, localId, out token);
        }

        bool TryAttach(ulong networkId, uint localId, out ulong token)
        {
            token = 0;
            Binding binding; ulong existingNetwork;
            bool hasNetwork = localByNetwork.TryGetValue(networkId, out binding);
            bool hasLocal = networkByLocal.TryGetValue(localId, out existingNetwork);
            if (hasNetwork || hasLocal)
            {
                if (!hasNetwork || !hasLocal || binding.Local != localId || existingNetwork != networkId) return false;
                token = binding.Generation;
                return true;
            }
            if (generation == ulong.MaxValue) return false;
            token = ++generation;
            localByNetwork.Add(networkId, new Binding { Local = localId, Generation = token });
            networkByLocal.Add(localId, networkId);
            return true;
        }

        // Drops only the local binding; the network identity and its tombstone
        // state are untouched, so the entity can be rebound later.
        internal bool Unbind(ulong networkId, uint expectedLocal, ulong expectedGeneration)
        {
            Binding binding;
            if (!localByNetwork.TryGetValue(networkId, out binding) || binding.Local != expectedLocal || binding.Generation != expectedGeneration) return false;
            localByNetwork.Remove(networkId); networkByLocal.Remove(binding.Local);
            return true;
        }

        // Stale-generation guard for callers still holding an old local binding.
        internal bool IsCurrent(ulong networkId, uint localId, ulong expectedGeneration)
        {
            Binding binding;
            return localByNetwork.TryGetValue(networkId, out binding) && binding.Local == localId &&
                binding.Generation == expectedGeneration;
        }

        internal bool TryGeneration(ulong networkId, out ulong token)
        {
            Binding binding;
            if (localByNetwork.TryGetValue(networkId, out binding)) { token = binding.Generation; return true; }
            token = 0;
            return false;
        }

        internal bool Remove(ulong networkId)
        {
            if (!known.Remove(networkId)) return false;
            Binding binding;
            if (localByNetwork.TryGetValue(networkId, out binding))
            {
                localByNetwork.Remove(networkId); networkByLocal.Remove(binding.Local);
            }
            removed.Add(networkId);
            return true;
        }

        internal bool TryLocal(ulong networkId, out uint localId) { Binding binding; if (localByNetwork.TryGetValue(networkId, out binding)) { localId = binding.Local; return true; } localId = 0; return false; }
        internal bool TryNetwork(uint localId, out ulong networkId) { return networkByLocal.TryGetValue(localId, out networkId); }
    }
}
