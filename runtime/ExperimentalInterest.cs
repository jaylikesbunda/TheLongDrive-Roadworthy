using System;
using System.Collections.Generic;
using System.Globalization;

namespace TldMp.Experimental
{
    // Phase 5 interest + far-store core.
    //
    // Inputs: docs/MULTIPLAYER_WORLD_AUTHORITY_INVENTORY.md section 7 ("Interest
    // management: what a peer sees and what the host keeps"), and
    // docs/EXPERIMENTAL_MULTIPLAYER_PLAN.md ("Separate interest from simulation",
    // "a player can receive a display copy without owning physics, while the host
    // retains persistent state when nothing is loaded", and the Phase 5 mandate
    // "design per-region interest, initial state on entering interest, durable
    // far-state storage and deterministic duplicate reconciliation").
    //
    // Pure bookkeeping. No Unity type, no game call, no transport, no live wiring.
    // Two independent pieces:
    //
    //   InterestSet - which stream cells a peer is interested in right now, with
    //                 explicit enter/exit hysteresis and hard bounds.
    //   FarStore    - the durable record the host keeps for an entity that is
    //                 currently unloaded, plus the stream-in reconciliation that
    //                 decides what a returning local object may do with it.
    //
    // Neither type decides gameplay authority. Interest says who may *receive* a
    // display copy; it never says who simulates (inventory section 6.5). The far
    // store says what the host still remembers and whether a returning object may
    // adopt, rebind or be refused.
    //
    // Deliberate non-goals: no wire format, no per-cell payload, no priority
    // ordering of cells, no time-based expiry. The inventory records that today's
    // streaming hysteresis is implicit (ring change, 0.6 s contact staleness,
    // 3 ms/2 s coordinator aging); this type makes the two distances explicit and
    // keeps everything else out.

    // The 3D stream grid.
    //
    // Cell size is 100 m. That is not the game's own chunk size (the inventory
    // records a 128 m minimum logical stream cell in runtime/StableMap.cs); it is
    // chosen here as a round, documented constant so the enter/exit distances in
    // this core are unambiguous. A caller that must line up with StableMap's
    // 128...512 m logical cell derives its own mapping and passes cell indices.
    //
    // The index range is bounded to +-1048576 cells (+-104857.6 km at 100 m), i.e.
    // wider than any game world, so the key space is finite: no position, however
    // large or hostile, can create a new unbounded key. Out-of-range positions and
    // indices are clamped to the edge cell instead of throwing, because a peer
    // that reports a non-finite or absurd position must not be able to allocate.
    internal static class StreamCell
    {
        internal const double Size = 100.0;
        internal const int MinIndex = -(1 << 20);
        internal const int MaxIndex = (1 << 20) - 1;
        const int Bias = 1 << 20;                  // turns [-1048576, 1048575] into [0, 2097151]
        const long AxisMask = (1L << 21) - 1L;     // 21 bits per axis, 63 bits total

        internal static int Clamp(int cell)
        {
            if (cell < MinIndex) return MinIndex;
            if (cell > MaxIndex) return MaxIndex;
            return cell;
        }

        // metres -> cell index. Floor, so a position belongs to the cell whose box
        // contains it; a position exactly on a wall belongs to both boxes (see
        // SquaredDistance), which is why an update can legitimately add two
        // neighbouring cells for one axis.
        internal static int Index(double meters)
        {
            double scaled = Math.Floor(meters / Size);
            if (Double.IsNaN(scaled)) return 0;
            if (scaled < MinIndex) return MinIndex;
            if (scaled > MaxIndex) return MaxIndex;
            return (int)scaled;
        }

        // Positions are clamped onto the grid before any distance is computed, so an
        // absurd coordinate behaves like the grid edge and still holds the edge cell,
        // instead of falling outside every cell and silently streaming nothing.
        internal static double ClampPosition(double meters)
        {
            double low = MinIndex * Size;
            double high = (MaxIndex + 1) * Size;
            if (meters < low) return low;
            if (meters > high) return high;
            return meters;
        }

        // Dense, total, collision-free key for one cell. Always non-negative, so a
        // signed right shift is never needed to decode it.
        internal static long Key(int x, int y, int z)
        {
            long bx = Clamp(x) + Bias;
            long by = Clamp(y) + Bias;
            long bz = Clamp(z) + Bias;
            return (bx << 42) | (by << 21) | bz;
        }

        internal static void Decode(long key, out int x, out int y, out int z)
        {
            z = (int)(key & AxisMask) - Bias;
            y = (int)((key >> 21) & AxisMask) - Bias;
            x = (int)((key >> 42) & AxisMask) - Bias;
        }

        // Squared distance from a point to the cell's axis-aligned box: zero inside
        // the box (including its walls). Squared, so the per-update scan never pays
        // for a square root; callers compare against squared thresholds.
        internal static double SquaredDistance(int x, int y, int z, double px, double py, double pz)
        {
            double gx = Gap(x, px);
            double gy = Gap(y, py);
            double gz = Gap(z, pz);
            return (gx * gx) + (gy * gy) + (gz * gz);
        }

        static double Gap(int cell, double value)
        {
            double lower = cell * Size;
            if (value < lower) return lower - value;
            double upper = lower + Size;
            if (value > upper) return value - upper;
            return 0.0;
        }
    }

    // Per-peer stream-cell interest with enter/exit hysteresis.
    //
    // Rules, all documented here because none of them exists in the current
    // runtime (inventory section 7.2):
    //
    //   * a cell is added when the peer is within enterDistance of the cell's box,
    //     and removed only once the peer is beyond exitDistance of it. A cell that
    //     is not currently interested is *never* added from the exit band, which is
    //     the whole point of the hysteresis: a peer that sits near a boundary does
    //     not thrash.
    //   * the enter boundary is inclusive (distance exactly enterDistance adds) and
    //     the exit boundary is inclusive too (distance exactly exitDistance keeps).
    //     So the state is "entered" and "still entered" at both edges.
    //   * entering and forgetting are explicit (Enter/ForgetPeer), so peer memory is
    //     bounded by maxPeers instead of by however many peer ids a wire can carry.
    //   * per peer, at most maxCells cells; across all peers, at most
    //     maxPeers * maxCells (peer, cell) pairs. A full peer refuses further cells
    //     deterministically (scan order +x, +y, +z) and frees them as the peer
    //     leaves; nothing is evicted to make room.
    //   * no allocation on the update path. The set owns one long[] scratch buffer
    //     of maxCells entries and reuses it; the only per-cell allocations happen
    //     when a peer is first added or a cell gains its first interested peer, and
    //     every one of those is bounded by the caps above.
    //   * Update(now) refuses a clock that moves backwards for that peer, so a
    //     reordered frame cannot regress interest. The core does no time-based
    //     expiry: only distance removes a cell.
    //
    // Cost of one Update: the peer's own set (<= maxCells) plus the candidate box
    // spanned by enterDistance, i.e. at most (2*ceil(enter/100)+1)^3 cells. With the
    // documented 2048 m ceiling on exitDistance and a realistic 128...512 m enter
    // radius that is at most 43^3 cells, and normally 5^3...11^3.
    internal sealed class InterestSet
    {
        internal const int HardMaxCells = 4096;
        internal const int HardMaxPeers = 64;

        // A stream radius beyond 2 km is refused rather than silently scanned: the
        // candidate box grows cubically with the radius, and the inventory's own
        // ceiling is a 512 m logical cell. Raising this constant is a deliberate
        // cost decision, not a configuration knob.
        internal const double MaxStreamDistance = 2048.0;

        sealed class PeerState
        {
            internal readonly HashSet<long> Cells = new HashSet<long>();
            internal double LastUpdate;
        }

        sealed class CellPeers
        {
            internal ulong[] Peers;
            internal int Count;

            internal CellPeers(int capacity)
            {
                Peers = new ulong[capacity < 2 ? 2 : capacity];
            }
        }

        readonly Dictionary<ulong, PeerState> peers = new Dictionary<ulong, PeerState>();
        readonly Dictionary<long, CellPeers> byCell = new Dictionary<long, CellPeers>();
        readonly long[] scratch;
        readonly int maxCells;
        readonly int maxPeers;
        readonly double enterDistance;
        readonly double exitDistance;
        readonly double enterSquared;
        readonly double exitSquared;
        int totalCells;
        int added;
        int removed;

        internal InterestSet(int maxCells, double enterDistance, double exitDistance, int maxPeers)
        {
            if (maxCells <= 0 || maxCells > HardMaxCells)
                throw new ArgumentOutOfRangeException("maxCells", "A cell cap must be between 1 and " + HardMaxCells.ToString(CultureInfo.InvariantCulture) + ".");
            if (maxPeers <= 0 || maxPeers > HardMaxPeers)
                throw new ArgumentOutOfRangeException("maxPeers", "A peer cap must be between 1 and " + HardMaxPeers.ToString(CultureInfo.InvariantCulture) + ".");
            if (!Finite(enterDistance) || enterDistance <= 0 || enterDistance > MaxStreamDistance)
                throw new ArgumentOutOfRangeException("enterDistance", "An enter distance must be positive and bounded.");
            if (!Finite(exitDistance) || exitDistance <= enterDistance || exitDistance > MaxStreamDistance)
                throw new ArgumentOutOfRangeException("exitDistance", "An exit distance must exceed the enter distance and be bounded.");
            this.maxCells = maxCells;
            this.maxPeers = maxPeers;
            this.enterDistance = enterDistance;
            this.exitDistance = exitDistance;
            this.enterSquared = enterDistance * enterDistance;
            this.exitSquared = exitDistance * exitDistance;
            this.scratch = new long[maxCells];
        }

        internal int MaxCells { get { return maxCells; } }
        internal int MaxPeers { get { return maxPeers; } }
        internal double EnterDistance { get { return enterDistance; } }
        internal double ExitDistance { get { return exitDistance; } }
        internal int PeerCount { get { return peers.Count; } }
        internal int TotalCells { get { return totalCells; } }

        // Explicitly start tracking a peer. Idempotent. A full peer table throws
        // instead of evicting or silently ignoring: an untracked peer would
        // otherwise look like a peer with no interest, which is indistinguishable
        // from a correct stream-out.
        internal void Enter(ulong peer)
        {
            if (peer == 0) throw new ArgumentException("A peer id must be non-zero.", "peer");
            if (peers.ContainsKey(peer)) return;
            if (peers.Count >= maxPeers)
                throw new InvalidOperationException("The interest set is full: " + maxPeers.ToString(CultureInfo.InvariantCulture) + " peers are already tracked.");
            peers.Add(peer, new PeerState());
        }

        // Drop a peer and every cell it was interested in. Idempotent, and a no-op
        // for an unknown peer or peer 0. Cells are removed from the reverse index
        // as well, so PeersFor cannot report a forgotten peer.
        internal void ForgetPeer(ulong peer)
        {
            if (peer == 0) return;
            PeerState state;
            if (!peers.TryGetValue(peer, out state)) return;
            if (state.Cells.Count > 0)
            {
                state.Cells.CopyTo(scratch, 0);
                int count = state.Cells.Count;
                for (int i = 0; i < count; i++) RemoveFromIndex(scratch[i], peer);
                totalCells -= count;
                removed += count;
                state.Cells.Clear();
            }
            peers.Remove(peer);
        }

        // Apply one position sample. Returns true when this peer's interest set
        // changed (a cell was added or removed), false when it was accepted with no
        // change. An unknown peer, a zero peer, a non-finite position or a clock
        // that moved backwards changes nothing and returns false.
        //
        // The cell the peer stands in always qualifies (box distance 0), and a
        // position exactly on a 100 m wall qualifies for both neighbouring boxes on
        // that axis; both are intentional, so a peer standing on a seam does not
        // lose the cell it is half inside.
        internal bool Update(ulong peer, double x, double y, double z, double now)
        {
            if (peer == 0) return false;
            if (!Finite(x) || !Finite(y) || !Finite(z) || !Finite(now)) return false;
            PeerState state;
            if (!peers.TryGetValue(peer, out state)) return false;
            if (now < state.LastUpdate) return false;
            state.LastUpdate = now;

            // Clamped once, then used for every distance below: the grid edge is the
            // only place an out-of-range sample can live.
            double px = StreamCell.ClampPosition(x);
            double py = StreamCell.ClampPosition(y);
            double pz = StreamCell.ClampPosition(z);

            bool changed = false;

            // Retention pass: keep every cell still inside the exit band, drop the
            // rest. CopyTo into the set-owned scratch buffer allocates nothing.
            int held = state.Cells.Count;
            if (held > 0)
            {
                state.Cells.CopyTo(scratch, 0);
                state.Cells.Clear();
                for (int i = 0; i < held; i++)
                {
                    int cx, cy, cz;
                    StreamCell.Decode(scratch[i], out cx, out cy, out cz);
                    if (StreamCell.SquaredDistance(cx, cy, cz, px, py, pz) <= exitSquared)
                    {
                        state.Cells.Add(scratch[i]);
                        continue;
                    }
                    RemoveFromIndex(scratch[i], peer);
                    totalCells--;
                    removed++;
                    changed = true;
                }
            }

            // Acquisition pass: only the enter band adds cells, and only while the
            // peer is under its cap. The cell the peer actually stands in is taken
            // first, so even a cap that is far too small for the configured radius
            // still covers the peer itself instead of a far corner in scan order.
            int homeX = StreamCell.Index(px), homeY = StreamCell.Index(py), homeZ = StreamCell.Index(pz);
            changed |= Acquire(state, peer, px, py, pz, homeX, homeX, homeY, homeY, homeZ, homeZ);
            changed |= Acquire(state, peer, px, py, pz,
                StreamCell.Index(px - enterDistance), StreamCell.Index(px + enterDistance),
                StreamCell.Index(py - enterDistance), StreamCell.Index(py + enterDistance),
                StreamCell.Index(pz - enterDistance), StreamCell.Index(pz + enterDistance));
            return changed;
        }

        // Add every cell of the inclusive box that is within the enter band and not
        // already held. Stops as soon as the peer is at its cap, so the bound is
        // never exceeded and the result is deterministic (scan order +x, +y, +z).
        bool Acquire(PeerState state, ulong peer, double x, double y, double z,
            int x0, int x1, int y0, int y1, int z0, int z1)
        {
            bool changed = false;
            for (int cx = x0; cx <= x1; cx++)
            {
                if (state.Cells.Count >= maxCells) break;
                for (int cy = y0; cy <= y1; cy++)
                {
                    if (state.Cells.Count >= maxCells) break;
                    for (int cz = z0; cz <= z1; cz++)
                    {
                        if (state.Cells.Count >= maxCells) break;
                        long key = StreamCell.Key(cx, cy, cz);
                        if (state.Cells.Contains(key)) continue;
                        if (StreamCell.SquaredDistance(cx, cy, cz, x, y, z) > enterSquared) continue;
                        state.Cells.Add(key);
                        AddToIndex(key, peer);
                        totalCells++;
                        added++;
                        changed = true;
                    }
                }
            }
            return changed;
        }

        internal bool IsInterested(ulong peer, int cellX, int cellY, int cellZ)
        {
            if (peer == 0) return false;
            PeerState state;
            if (!peers.TryGetValue(peer, out state)) return false;
            return state.Cells.Contains(StreamCell.Key(cellX, cellY, cellZ));
        }

        // Peers currently interested in a cell, as a fresh array. Caller-driven, not
        // per-frame; order is unspecified (it is insertion order of the reverse
        // index, which changes as peers leave and return).
        internal ulong[] PeersFor(int cellX, int cellY, int cellZ)
        {
            CellPeers entry;
            if (!byCell.TryGetValue(StreamCell.Key(cellX, cellY, cellZ), out entry) || entry.Count == 0)
                return new ulong[0];
            ulong[] copy = new ulong[entry.Count];
            Array.Copy(entry.Peers, copy, entry.Count);
            return copy;
        }

        // Allocation-free alternative to PeersFor for a caller that already owns a
        // buffer of at least maxPeers entries. Returns the number written, or 0 when
        // the buffer is too small (nothing is written).
        internal int CopyPeersFor(int cellX, int cellY, int cellZ, ulong[] buffer)
        {
            if (buffer == null || buffer.Length < maxPeers) return 0;
            CellPeers entry;
            if (!byCell.TryGetValue(StreamCell.Key(cellX, cellY, cellZ), out entry) || entry.Count == 0) return 0;
            Array.Copy(entry.Peers, buffer, entry.Count);
            return entry.Count;
        }

        internal int CellCount(ulong peer)
        {
            PeerState state;
            return peers.TryGetValue(peer, out state) ? state.Cells.Count : 0;
        }

        // Drop one cell from one peer regardless of distance (a caller that finished
        // or abandoned that cell). The next Update re-adds it if the peer is still
        // inside the enter band, which is intentional: this is a release, not a ban.
        internal void RemoveCell(ulong peer, int cellX, int cellY, int cellZ)
        {
            if (peer == 0) return;
            PeerState state;
            if (!peers.TryGetValue(peer, out state)) return;
            long key = StreamCell.Key(cellX, cellY, cellZ);
            if (!state.Cells.Remove(key)) return;
            RemoveFromIndex(key, peer);
            totalCells--;
            removed++;
        }

        // Drop one cell from every peer (a cell that was evicted or unloaded for the
        // whole session). Bounded by maxPeers.
        internal void RemoveCell(int cellX, int cellY, int cellZ)
        {
            long key = StreamCell.Key(cellX, cellY, cellZ);
            CellPeers entry;
            if (!byCell.TryGetValue(key, out entry) || entry.Count == 0) return;
            ulong[] victims = new ulong[entry.Count];
            Array.Copy(entry.Peers, victims, entry.Count);
            byCell.Remove(key);
            for (int i = 0; i < victims.Length; i++)
            {
                PeerState state;
                if (!peers.TryGetValue(victims[i], out state)) continue;
                if (!state.Cells.Remove(key)) continue;
                totalCells--;
                removed++;
            }
            // The reverse entry was dropped in full above, so the per-peer removals
            // deliberately do not touch the index again.
        }

        internal string Summary()
        {
            return "peers=" + peers.Count.ToString(CultureInfo.InvariantCulture) +
                " cells=" + totalCells.ToString(CultureInfo.InvariantCulture) +
                " added=" + added.ToString(CultureInfo.InvariantCulture) +
                " removed=" + removed.ToString(CultureInfo.InvariantCulture) +
                " maxCells=" + maxCells.ToString(CultureInfo.InvariantCulture) +
                " maxPeers=" + maxPeers.ToString(CultureInfo.InvariantCulture) +
                " enter=" + enterDistance.ToString(CultureInfo.InvariantCulture) +
                " exit=" + exitDistance.ToString(CultureInfo.InvariantCulture);
        }

        // Structural invariants only. Walks both directions of the (peer, cell)
        // relation, so a leaked or duplicated index entry is visible: that is the
        // only failure mode this type can have that the public API would hide.
        internal bool Verify()
        {
            if (peers.Count > maxPeers) return false;
            if (totalCells < 0 || totalCells > maxPeers * maxCells) return false;
            int counted = 0;
            foreach (var pair in peers)
            {
                if (pair.Key == 0) return false;
                PeerState state = pair.Value;
                if (state.Cells.Count > maxCells) return false;
                if (!Finite(state.LastUpdate)) return false;
                counted += state.Cells.Count;
                foreach (long key in state.Cells)
                {
                    CellPeers entry;
                    if (!byCell.TryGetValue(key, out entry)) return false;
                    if (!Contains(entry, pair.Key)) return false;
                }
            }
            if (counted != totalCells) return false;
            int indexed = 0;
            foreach (var pair in byCell)
            {
                CellPeers entry = pair.Value;
                if (entry.Count < 0 || entry.Count > maxPeers) return false;
                if (entry.Count == 0) return false;
                indexed += entry.Count;
                for (int i = 0; i < entry.Count; i++)
                {
                    PeerState state;
                    if (!peers.TryGetValue(entry.Peers[i], out state)) return false;
                    if (!state.Cells.Contains(pair.Key)) return false;
                    for (int j = 0; j < i; j++)
                        if (entry.Peers[j] == entry.Peers[i]) return false;
                }
            }
            return indexed == totalCells;
        }

        void AddToIndex(long key, ulong peer)
        {
            CellPeers entry;
            if (!byCell.TryGetValue(key, out entry))
            {
                entry = new CellPeers(maxPeers < 2 ? maxPeers : 2);
                byCell.Add(key, entry);
            }
            if (entry.Count >= maxPeers) return;
            if (entry.Count == entry.Peers.Length)
            {
                int next = entry.Peers.Length * 2;
                if (next > maxPeers) next = maxPeers;
                ulong[] grown = new ulong[next];
                Array.Copy(entry.Peers, grown, entry.Count);
                entry.Peers = grown;
            }
            entry.Peers[entry.Count] = peer;
            entry.Count++;
        }

        void RemoveFromIndex(long key, ulong peer)
        {
            CellPeers entry;
            if (!byCell.TryGetValue(key, out entry)) return;
            for (int i = 0; i < entry.Count; i++)
            {
                if (entry.Peers[i] != peer) continue;
                entry.Count--;
                entry.Peers[i] = entry.Peers[entry.Count];
                entry.Peers[entry.Count] = 0;
                break;
            }
            if (entry.Count == 0) byCell.Remove(key);
        }

        static bool Contains(CellPeers entry, ulong peer)
        {
            for (int i = 0; i < entry.Count; i++)
                if (entry.Peers[i] == peer) return true;
            return false;
        }

        internal static bool Finite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }
    }

    // What a stream-in may do with the local object it was handed.
    //
    //   Reject   - nothing happens; the caller must not construct, adopt or
    //              overwrite anything (tombstoned entity, invalid ids, no binding
    //              room). TryReconcileOnStreamIn returns false.
    //   Adopt    - the caller's local object becomes the representation of that
    //              network entity (or already was: the call is idempotent).
    //   Rebind   - the network entity was bound to a different local object; the
    //              caller's object now represents it and the previous local id is
    //              reported so the caller can retire that object.
    //   Conflict - two different network entities claim the same local object, or
    //              the local object belongs elsewhere. Nothing is changed.
    internal enum ReconcileAction : byte
    {
        Reject = 0,
        Adopt = 1,
        Rebind = 2,
        Conflict = 3
    }

    // The durable host record for one entity, live or unloaded.
    //
    // The record is the only place the host keeps what an entity *is* while nothing
    // has it loaded (plan Phase 5, inventory section 7.3: today only decorations and
    // POIs have such a record, and rabbits/items fall back to the save definition).
    // State is a caller-owned opaque byte blob; the store bounds its length and
    // copies it on write, and readers must treat the returned array as read-only.
    internal sealed class FarRecord
    {
        internal uint Entity;
        internal ushort Kind;
        internal ulong Owner;
        internal ulong Epoch;
        internal ulong Revision;
        internal byte[] State;
        internal bool Loaded;
        internal double LastSeen;
    }

    // Bounded far-state store with revision/epoch ordering and stream-in
    // reconciliation.
    //
    // Ordering rule for Upsert (the only rule that decides which write lands):
    //
    //   * epoch lower than the stored epoch            -> refused (stale writer).
    //   * epoch equal and revision not greater         -> refused (older or duplicate
    //                                                     revision).
    //   * epoch greater and revision lower             -> refused (a new epoch may not
    //                                                     roll the record back).
    //   * otherwise                                    -> accepted.
    //
    // So a newer revision wins inside one epoch, and an ownership transfer (a higher
    // epoch) is accepted while keeping the revision, but a fresh owner cannot
    // present stale content. Owner and epoch changes are counted and stored, which
    // is what a caller needs to log a transfer it did not initiate.
    //
    // Tombstones are terminal: Remove needs the current epoch, moves the id to the
    // bounded tombstone store, and every later Upsert or stream-in for that id is
    // refused. A full tombstone store refuses the removal rather than forgetting
    // that the entity died, so "no resurrection" never depends on capacity.
    //
    // ForgetPeer hands every live record owned by a departed peer to the host by
    // clearing the owner (0 = host-reserved) and bumping the epoch, so the departed
    // peer's old (owner, epoch) pair can never write again. Tombstones are untouched:
    // a disconnect resurrects nothing.
    internal sealed class FarStore
    {
        internal const int HardMaxEntities = 1 << 16;
        internal const int HardMaxTombstones = 1 << 16;
        internal const int HardMaxStateBytes = 1 << 20;

        static readonly byte[] Empty = new byte[0];

        readonly Dictionary<uint, FarRecord> live = new Dictionary<uint, FarRecord>();
        readonly Dictionary<uint, ulong> tombstones = new Dictionary<uint, ulong>();
        readonly Dictionary<uint, uint> localByNetwork = new Dictionary<uint, uint>();
        readonly Dictionary<uint, uint> networkByLocal = new Dictionary<uint, uint>();
        readonly int maxEntities;
        readonly int maxTombstones;
        readonly int maxStateBytes;
        int accepted;
        int removed;
        int refusedRevision;
        int refusedEpoch;
        int refusedState;
        int refusedFull;
        int ownerChanges;
        int epochChanges;
        int transferred;
        int adopted;
        int rebound;
        int conflicts;
        int rejected;

        internal FarStore(int maxEntities, int maxTombstones, int maxStateBytes)
        {
            if (maxEntities <= 0 || maxEntities > HardMaxEntities)
                throw new ArgumentOutOfRangeException("maxEntities", "An entity cap must be between 1 and " + HardMaxEntities.ToString(CultureInfo.InvariantCulture) + ".");
            if (maxTombstones <= 0 || maxTombstones > HardMaxTombstones)
                throw new ArgumentOutOfRangeException("maxTombstones", "A tombstone cap must be between 1 and " + HardMaxTombstones.ToString(CultureInfo.InvariantCulture) + ".");
            if (maxStateBytes <= 0 || maxStateBytes > HardMaxStateBytes)
                throw new ArgumentOutOfRangeException("maxStateBytes", "A state cap must be between 1 and " + HardMaxStateBytes.ToString(CultureInfo.InvariantCulture) + ".");
            this.maxEntities = maxEntities;
            this.maxTombstones = maxTombstones;
            this.maxStateBytes = maxStateBytes;
        }

        internal int MaxEntities { get { return maxEntities; } }
        internal int MaxTombstones { get { return maxTombstones; } }
        internal int MaxStateBytes { get { return maxStateBytes; } }
        internal int Count { get { return live.Count; } }
        internal int TombstoneCount { get { return tombstones.Count; } }
        internal int BindingCount { get { return localByNetwork.Count; } }

        // Write one host record. False means the record was not stored and nothing
        // changed: invalid input, state over the byte cap, tombstoned id, stale
        // epoch/revision, or a full entity table.
        internal bool Upsert(uint entity, ushort kind, ulong owner, ulong epoch, ulong revision, byte[] state, bool loaded, double now)
        {
            if (entity == 0 || kind == 0 || owner == 0 || epoch == 0 || revision == 0) return false;
            if (!InterestSet.Finite(now)) return false;
            int length = state == null ? 0 : state.Length;
            if (length > maxStateBytes) { refusedState++; return false; }
            if (tombstones.ContainsKey(entity)) { rejected++; return false; }

            FarRecord record;
            if (live.TryGetValue(entity, out record))
            {
                if (epoch > record.Epoch)
                {
                    if (revision < record.Revision) { refusedRevision++; return false; }
                }
                else if (epoch == record.Epoch)
                {
                    if (revision <= record.Revision) { refusedRevision++; return false; }
                }
                else
                {
                    refusedEpoch++;
                    return false;
                }
                if (record.Owner != owner) ownerChanges++;
                if (record.Epoch != epoch) epochChanges++;
                record.Kind = kind;
                record.Owner = owner;
                record.Epoch = epoch;
                record.Revision = revision;
                record.State = Copy(state, length);
                record.Loaded = loaded;
                record.LastSeen = now;
                accepted++;
                return true;
            }

            if (live.Count >= maxEntities) { refusedFull++; return false; }
            live.Add(entity, new FarRecord
            {
                Entity = entity,
                Kind = kind,
                Owner = owner,
                Epoch = epoch,
                Revision = revision,
                State = Copy(state, length),
                Loaded = loaded,
                LastSeen = now
            });
            accepted++;
            return true;
        }

        // Finalise an entity: it must be live and the epoch must be the current one.
        // The id becomes a tombstone and its local binding is released. A full
        // tombstone store refuses the call (the record stays live) so that the store
        // never loses the fact that the entity died.
        internal bool Remove(uint entity, ulong expectedEpoch)
        {
            if (entity == 0 || expectedEpoch == 0) return false;
            FarRecord record;
            if (!live.TryGetValue(entity, out record)) return false;
            if (record.Epoch != expectedEpoch) { refusedEpoch++; return false; }
            if (tombstones.Count >= maxTombstones) { refusedFull++; return false; }
            live.Remove(entity);
            tombstones.Add(entity, expectedEpoch);
            Unbind(entity);
            removed++;
            return true;
        }

        internal bool TryGet(uint entity, out FarRecord record)
        {
            record = null;
            if (entity == 0) return false;
            return live.TryGetValue(entity, out record);
        }

        internal bool MarkLoaded(uint entity, bool loaded)
        {
            FarRecord record;
            if (entity == 0 || !live.TryGetValue(entity, out record)) return false;
            record.Loaded = loaded;
            return true;
        }

        internal bool IsTombstoned(uint entity)
        {
            return entity != 0 && tombstones.ContainsKey(entity);
        }

        internal bool TryLocal(uint networkId, out uint localId)
        {
            localId = 0;
            if (networkId == 0) return false;
            return localByNetwork.TryGetValue(networkId, out localId);
        }

        internal bool TryNetwork(uint localId, out uint networkId)
        {
            networkId = 0;
            if (localId == 0) return false;
            return networkByLocal.TryGetValue(localId, out networkId);
        }

        // Stream-in reconciliation. Returns true for Adopt and Rebind (the caller may
        // proceed), false for Reject and Conflict (the caller must not construct or
        // overwrite anything).
        //
        // Decision order, and the reason for each edge:
        //   1. zero ids                -> Reject (nothing to bind).
        //   2. tombstoned network id   -> Reject (no resurrection, inventory 7.5).
        //   3. local id bound to a different network id -> Conflict. Two entities
        //      cannot share one local object; the caller must resolve before binding.
        //   4. network id bound to a different local id -> Rebind. The identity is
        //      durable, the local object is not: the object that streams in takes the
        //      identity over and the displaced local id is reported. This is the
        //      normal stream-out/stream-in path, not an error.
        //   5. otherwise               -> Adopt. Also returned when the exact binding
        //      already exists, so a repeated stream-in is idempotent.
        // A binding table at its cap refuses a *new* binding with Reject; a rebind
        // reuses an existing slot and is always allowed.
        internal bool TryReconcileOnStreamIn(uint networkId, uint localId, out ReconcileAction action)
        {
            uint previousLocalId;
            return TryReconcileOnStreamIn(networkId, localId, out action, out previousLocalId);
        }

        internal bool TryReconcileOnStreamIn(uint networkId, uint localId, out ReconcileAction action, out uint previousLocalId)
        {
            previousLocalId = 0;
            if (networkId == 0 || localId == 0) { action = ReconcileAction.Reject; rejected++; return false; }
            if (tombstones.ContainsKey(networkId)) { action = ReconcileAction.Reject; rejected++; return false; }

            uint boundLocal;
            bool hasNetwork = localByNetwork.TryGetValue(networkId, out boundLocal);
            uint boundNetwork;
            bool hasLocal = networkByLocal.TryGetValue(localId, out boundNetwork);

            if (hasLocal && boundNetwork != networkId)
            {
                action = ReconcileAction.Conflict;
                conflicts++;
                return false;
            }
            // The exact binding already exists: idempotent adopt, and checked here so
            // the rebind path below can never run against a self-binding.
            if (hasLocal && hasNetwork && boundLocal == localId)
            {
                action = ReconcileAction.Adopt;
                adopted++;
                return true;
            }
            if (hasNetwork)
            {
                previousLocalId = boundLocal;
                localByNetwork[networkId] = localId;
                networkByLocal.Remove(boundLocal);
                networkByLocal[localId] = networkId;
                action = ReconcileAction.Rebind;
                rebound++;
                return true;
            }
            if (localByNetwork.Count >= maxEntities)
            {
                action = ReconcileAction.Reject;
                rejected++;
                return false;
            }
            localByNetwork.Add(networkId, localId);
            networkByLocal.Add(localId, networkId);
            action = ReconcileAction.Adopt;
            adopted++;
            return true;
        }

        // A departed peer keeps nothing it could still write: every live record it
        // owns becomes host-reserved (owner 0) with a new epoch. Records owned by
        // others and every tombstone are untouched. Idempotent for an unknown peer
        // and a no-op for peer 0.
        internal void ForgetPeer(ulong peer)
        {
            if (peer == 0 || live.Count == 0) return;
            foreach (var pair in live)
            {
                FarRecord record = pair.Value;
                if (record.Owner != peer) continue;
                record.Owner = 0;
                if (record.Epoch != UInt64.MaxValue) record.Epoch++;
                ownerChanges++;
                transferred++;
            }
        }

        internal string Summary()
        {
            return "entities=" + live.Count.ToString(CultureInfo.InvariantCulture) +
                " tombstones=" + tombstones.Count.ToString(CultureInfo.InvariantCulture) +
                " bindings=" + localByNetwork.Count.ToString(CultureInfo.InvariantCulture) +
                " accepted=" + accepted.ToString(CultureInfo.InvariantCulture) +
                " removed=" + removed.ToString(CultureInfo.InvariantCulture) +
                " refusedRevision=" + refusedRevision.ToString(CultureInfo.InvariantCulture) +
                " refusedEpoch=" + refusedEpoch.ToString(CultureInfo.InvariantCulture) +
                " refusedState=" + refusedState.ToString(CultureInfo.InvariantCulture) +
                " refusedFull=" + refusedFull.ToString(CultureInfo.InvariantCulture) +
                " ownerChanges=" + ownerChanges.ToString(CultureInfo.InvariantCulture) +
                " epochChanges=" + epochChanges.ToString(CultureInfo.InvariantCulture) +
                " transferred=" + transferred.ToString(CultureInfo.InvariantCulture) +
                " adopted=" + adopted.ToString(CultureInfo.InvariantCulture) +
                " rebound=" + rebound.ToString(CultureInfo.InvariantCulture) +
                " conflicts=" + conflicts.ToString(CultureInfo.InvariantCulture) +
                " rejected=" + rejected.ToString(CultureInfo.InvariantCulture);
        }

        // Caps, record shape, tombstone exclusivity and binding bijection. A stored
        // record whose state exceeds the cap, a live and tombstoned id, or a
        // one-directional binding is a real defect; nothing else here can go wrong
        // without the public API having hidden it.
        internal bool Verify()
        {
            if (live.Count > maxEntities) return false;
            if (tombstones.Count > maxTombstones) return false;
            if (localByNetwork.Count > maxEntities) return false;
            if (localByNetwork.Count != networkByLocal.Count) return false;
            foreach (var pair in live)
            {
                FarRecord record = pair.Value;
                if (pair.Key == 0 || record.Entity != pair.Key) return false;
                if (record.Kind == 0 || record.Epoch == 0 || record.Revision == 0) return false;
                if (record.State == null || record.State.Length > maxStateBytes) return false;
                if (!InterestSet.Finite(record.LastSeen)) return false;
                if (tombstones.ContainsKey(pair.Key)) return false;
            }
            foreach (var pair in tombstones)
            {
                if (pair.Key == 0 || pair.Value == 0) return false;
                if (live.ContainsKey(pair.Key)) return false;
                if (localByNetwork.ContainsKey(pair.Key)) return false;
            }
            foreach (var pair in localByNetwork)
            {
                if (pair.Key == 0 || pair.Value == 0) return false;
                if (tombstones.ContainsKey(pair.Key)) return false;
                uint back;
                if (!networkByLocal.TryGetValue(pair.Value, out back) || back != pair.Key) return false;
            }
            foreach (var pair in networkByLocal)
            {
                if (pair.Key == 0 || pair.Value == 0) return false;
                uint forward;
                if (!localByNetwork.TryGetValue(pair.Value, out forward) || forward != pair.Key) return false;
            }
            return true;
        }

        void Unbind(uint networkId)
        {
            uint localId;
            if (!localByNetwork.TryGetValue(networkId, out localId)) return;
            localByNetwork.Remove(networkId);
            networkByLocal.Remove(localId);
        }

        static byte[] Copy(byte[] state, int length)
        {
            if (length == 0) return Empty;
            byte[] copy = new byte[length];
            Array.Copy(state, copy, length);
            return copy;
        }
    }
}
