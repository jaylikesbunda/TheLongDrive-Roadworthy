using System;
using TldMp.Experimental;

// Interest/far-store core tests (Phase 5). Console Check pattern: the first failed
// invariant throws, so a non-zero exit means a real regression and Main returns 0
// only when every check passed. No Unity, no game assembly, no transport: the core
// is compiled with NetCore.cs only.
internal static class ExperimentalInterestTests
{
    static int checks;
    static void Check(bool ok) { checks++; if (!ok) throw new Exception("Interest invariant failed at check " + checks); }
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message + " (check " + checks + ")"); }

    const ulong A = 10, B = 20, C = 30, Host = 1;

    // ---- helpers -----------------------------------------------------------

    static ulong[] Sorted(ulong[] values)
    {
        ulong[] copy = new ulong[values.Length];
        Array.Copy(values, copy, values.Length);
        Array.Sort(copy);
        return copy;
    }

    static bool SamePeers(ulong[] actual, params ulong[] expected)
    {
        ulong[] sorted = Sorted(actual);
        if (sorted.Length != expected.Length) return false;
        for (int i = 0; i < sorted.Length; i++) if (sorted[i] != expected[i]) return false;
        return true;
    }

    static bool InterestRefused(int cells, double enter, double exit, int peers)
    {
        try { new InterestSet(cells, enter, exit, peers); return false; }
        catch (ArgumentException) { return true; }
    }

    static bool StoreRefused(int entities, int tombstones, int maxStateBytes)
    {
        try { new FarStore(entities, tombstones, maxStateBytes); return false; }
        catch (ArgumentException) { return true; }
    }

    static bool EnterRefused(InterestSet set, ulong peer)
    {
        try { set.Enter(peer); return false; }
        catch (InvalidOperationException) { return true; }
    }

    static bool ZeroPeerRefused(InterestSet set)
    {
        try { set.Enter(0); return false; }
        catch (ArgumentException) { return true; }
    }

    // ---- interest set ------------------------------------------------------

    static void InterestConstruction()
    {
        Check(InterestRefused(0, 5.0, 15.0, 4), "a zero cell cap is refused");
        Check(InterestRefused(-1, 5.0, 15.0, 4), "a negative cell cap is refused");
        Check(InterestRefused(InterestSet.HardMaxCells + 1, 5.0, 15.0, 4), "an unbounded cell cap is refused");
        Check(InterestRefused(4, 5.0, 15.0, 0), "a zero peer cap is refused");
        Check(InterestRefused(4, 5.0, 15.0, InterestSet.HardMaxPeers + 1), "an unbounded peer cap is refused");

        Check(InterestRefused(4, 0.0, 15.0, 4), "a zero enter distance is refused");
        Check(InterestRefused(4, -1.0, 15.0, 4), "a negative enter distance is refused");
        Check(InterestRefused(4, 20.0, 15.0, 4), "an enter distance beyond the exit distance is refused");
        Check(InterestRefused(4, 15.0, 15.0, 4), "equal enter and exit distances are refused");
        Check(InterestRefused(4, Double.NaN, 15.0, 4), "a NaN enter distance is refused");
        Check(InterestRefused(4, Double.PositiveInfinity, 15.0, 4), "an infinite enter distance is refused");
        Check(InterestRefused(4, 5.0, 0.0, 4), "a zero exit distance is refused");
        Check(InterestRefused(4, 5.0, -1.0, 4), "a negative exit distance is refused");
        Check(InterestRefused(4, 5.0, Double.NaN, 4), "a NaN exit distance is refused");
        Check(InterestRefused(4, 5.0, Double.PositiveInfinity, 4), "an infinite exit distance is refused");
        Check(InterestRefused(4, 5.0, InterestSet.MaxStreamDistance + 1.0, 4), "an exit distance beyond the stream ceiling is refused");
        Check(!InterestRefused(4, 5.0, InterestSet.MaxStreamDistance, 4), "the stream ceiling itself is allowed");

        var set = new InterestSet(6, 5.0, 15.0, 3);
        Check(set.MaxCells == 6 && set.MaxPeers == 3, "the caps are retained");
        Check(set.EnterDistance == 5.0 && set.ExitDistance == 15.0, "the distances are retained");
        Check(set.PeerCount == 0 && set.TotalCells == 0, "a new set is empty");
        Check(set.Verify(), "an empty set verifies");
        Check(set.Summary() == "peers=0 cells=0 added=0 removed=0 maxCells=6 maxPeers=3 enter=5 exit=15",
            "the empty summary has the documented shape: " + set.Summary());
    }

    static void CellGrid()
    {
        // 100 m cells, documented in the source, are the unit of every distance here.
        Check(StreamCell.Size == 100.0, "the stream cell is 100 m");

        Check(StreamCell.Index(0.0) == 0, "the origin is in cell 0");
        Check(StreamCell.Index(10.0) == 0, "10 m is in cell 0");
        Check(StreamCell.Index(99.9) == 0, "99.9 m is in cell 0");
        Check(StreamCell.Index(100.0) == 1, "100 m is the next cell");
        Check(StreamCell.Index(-0.5) == -1, "a negative position floors to the previous cell");
        Check(StreamCell.Index(-100.0) == -1, "-100 m is cell -1");
        Check(StreamCell.Index(-100.5) == -2, "-100.5 m is cell -2");

        // Bounded: an absurd or non-finite position clamps instead of allocating a key.
        Check(StreamCell.Index(1e12) == StreamCell.MaxIndex, "a huge position clamps to the maximum cell");
        Check(StreamCell.Index(-1e12) == StreamCell.MinIndex, "a hugely negative position clamps to the minimum cell");
        Check(StreamCell.Index(Double.PositiveInfinity) == StreamCell.MaxIndex, "positive infinity clamps to the maximum cell");
        Check(StreamCell.Index(Double.NegativeInfinity) == StreamCell.MinIndex, "negative infinity clamps to the minimum cell");
        Check(StreamCell.Index(Double.NaN) == 0, "a NaN position maps to cell 0");

        Check(StreamCell.Clamp(Int32.MaxValue) == StreamCell.MaxIndex, "an overflowing index clamps");
        Check(StreamCell.Clamp(Int32.MinValue) == StreamCell.MinIndex, "an underflowing index clamps");

        // Positions clamp to the grid edge, so an absurd sample still lands in the
        // edge cell instead of nowhere.
        Check(StreamCell.ClampPosition(42.0) == 42.0, "an in-range position is unchanged");
        Check(StreamCell.ClampPosition(1e12) == (StreamCell.MaxIndex + 1) * StreamCell.Size, "a huge position clamps to the grid edge");
        Check(StreamCell.ClampPosition(-1e12) == StreamCell.MinIndex * StreamCell.Size, "a hugely negative position clamps to the grid edge");
        Check(StreamCell.Index(StreamCell.ClampPosition(1e12)) == StreamCell.MaxIndex, "the clamped position is inside the edge cell");
        Check(StreamCell.Index(StreamCell.ClampPosition(-1e12)) == StreamCell.MinIndex, "the negative clamped position is inside the edge cell");

        // Dense, non-negative, collision-free keys.
        Check(StreamCell.Key(0, 0, 0) > 0, "the origin key is positive");
        Check(StreamCell.Key(StreamCell.MinIndex, StreamCell.MinIndex, StreamCell.MinIndex) >= 0, "the lowest key is non-negative");
        Check(StreamCell.Key(StreamCell.MinIndex, StreamCell.MinIndex, StreamCell.MinIndex) != StreamCell.Key(StreamCell.MinIndex, StreamCell.MinIndex, 0), "the lowest key is not confused with a neighbour");
        Check(StreamCell.Key(StreamCell.MaxIndex, StreamCell.MaxIndex, StreamCell.MaxIndex) > 0, "the highest key fits a positive long");
        Check(StreamCell.Key(1, 2, 3) != StreamCell.Key(3, 2, 1), "keys are axis ordered");
        Check(StreamCell.Key(1, 2, 3) != StreamCell.Key(2, 3, 1), "keys are not a sum");
        Check(StreamCell.Key(1, 0, 0) != StreamCell.Key(0, 1, 0), "keys separate the axes");
        Check(StreamCell.Key(Int32.MaxValue, 0, 0) == StreamCell.Key(StreamCell.MaxIndex, 0, 0), "an overflowing index keys like the clamped one");

        int x, y, z;
        StreamCell.Decode(StreamCell.Key(1, 2, 3), out x, out y, out z);
        Check(x == 1 && y == 2 && z == 3, "a key round-trips");
        StreamCell.Decode(StreamCell.Key(StreamCell.MinIndex, StreamCell.MaxIndex, 0), out x, out y, out z);
        Check(x == StreamCell.MinIndex && y == StreamCell.MaxIndex && z == 0, "the extreme key round-trips");
        StreamCell.Decode(StreamCell.Key(-1, -2, -3), out x, out y, out z);
        Check(x == -1 && y == -2 && z == -3, "negative cells round-trip");

        // Box distance: zero inside and on the wall, the axis gap outside.
        Check(StreamCell.SquaredDistance(0, 0, 0, 10.0, 20.0, 30.0) == 0.0, "a point inside a cell has zero distance");
        Check(StreamCell.SquaredDistance(0, 0, 0, 100.0, 50.0, 50.0) == 0.0, "a point on a wall has zero distance");
        Check(StreamCell.SquaredDistance(1, 0, 0, 95.0, 20.0, 30.0) == 25.0, "a 5 m gap squares to 25");
        Check(StreamCell.SquaredDistance(1, 0, 0, 85.0, 20.0, 30.0) == 225.0, "a 15 m gap squares to 225");
        Check(StreamCell.SquaredDistance(1, 1, 0, 95.0, 95.0, 20.0) == 50.0, "two 5 m gaps square to 50");
    }

    // enter=5, exit=15 against 100 m cells: the enter boundary is inclusive, the
    // exit boundary is inclusive, and the exit band never adds a new cell.
    static void Hysteresis()
    {
        var set = new InterestSet(16, 5.0, 15.0, 4);
        set.Enter(A);

        Check(set.Update(A, 10.0, 20.0, 30.0, 0.0), "the first update acquires the containing cell");
        Check(set.CellCount(A) == 1 && set.TotalCells == 1, "one cell is held");
        Check(set.IsInterested(A, 0, 0, 0), "the containing cell is interested");
        Check(!set.IsInterested(A, 1, 0, 0) && !set.IsInterested(A, -1, 0, 0), "the neighbours are outside the enter band");
        Check(SamePeers(set.PeersFor(0, 0, 0), A), "the reverse index lists the peer");
        Check(set.Verify(), "the first update verifies");

        // Standing still is not a change: no thrash, no re-report.
        Check(!set.Update(A, 10.0, 20.0, 30.0, 1.0), "an unchanged position changes nothing");
        Check(set.CellCount(A) == 1, "an unchanged position keeps one cell");

        // A clock that moves backwards is refused instead of regressing interest.
        Check(!set.Update(A, 8000.0, 8000.0, 8000.0, 0.5), "a backwards clock is refused");
        Check(set.CellCount(A) == 1 && set.IsInterested(A, 0, 0, 0), "a refused update changes nothing");

        // Exactly 5 m from cell (1,0,0): added, because the enter edge is inclusive.
        Check(set.Update(A, 95.0, 20.0, 30.0, 2.0), "the enter boundary adds a cell");
        Check(set.CellCount(A) == 2 && set.IsInterested(A, 1, 0, 0), "the second cell is interested");
        Check(SamePeers(set.PeersFor(1, 0, 0), A), "the second cell is indexed");

        // Exactly 15 m from cell (1,0,0): retained, because the exit edge is inclusive.
        Check(!set.Update(A, 85.0, 20.0, 30.0, 3.0), "the exit boundary retains a cell");
        Check(set.CellCount(A) == 2 && set.IsInterested(A, 1, 0, 0), "the cell survives at the exit edge");

        // 15.1 m: removed.
        Check(set.Update(A, 84.9, 20.0, 30.0, 4.0), "beyond the exit boundary a cell is released");
        Check(set.CellCount(A) == 1 && !set.IsInterested(A, 1, 0, 0), "the released cell is gone");
        Check(set.PeersFor(1, 0, 0).Length == 0, "the released cell left the reverse index");

        // 10 m from cell (1,0,0): inside the exit band but outside the enter band,
        // so it must NOT come back. This is the hysteresis rule.
        Check(!set.Update(A, 90.0, 20.0, 30.0, 5.0), "the exit band does not re-acquire a cell");
        Check(set.CellCount(A) == 1 && !set.IsInterested(A, 1, 0, 0), "the exit band left the cell released");

        // Back to 5 m: acquired again, at the enter edge.
        Check(set.Update(A, 95.0, 20.0, 30.0, 6.0), "re-entering the enter band re-acquires the cell");
        Check(set.CellCount(A) == 2 && set.IsInterested(A, 1, 0, 0), "the cell is interested again");

        // Leaving far behind releases everything and acquires the new home cell only.
        Check(set.Update(A, 5013.0, 5007.0, 5009.0, 7.0), "a jump changes interest");
        Check(set.CellCount(A) == 1 && set.IsInterested(A, 50, 50, 50), "the new home cell is the only interest");
        Check(!set.IsInterested(A, 0, 0, 0) && !set.IsInterested(A, 1, 0, 0), "the old cells are gone");
        Check(set.TotalCells == 1 && set.Verify(), "the set stays consistent");

        // Non-finite positions are refused, not clamped into interest.
        Check(!set.Update(A, Double.NaN, 0.0, 0.0, 8.0), "a NaN position is refused");
        Check(!set.Update(A, 0.0, Double.PositiveInfinity, 0.0, 8.0), "an infinite position is refused");
        Check(!set.Update(A, 0.0, 0.0, 0.0, Double.NaN), "an infinite clock is refused");
        Check(set.CellCount(A) == 1, "refused positions change nothing");

        // An unentered peer is not tracked at all.
        Check(!set.Update(B, 10.0, 20.0, 30.0, 0.0), "an unentered peer is refused");
        Check(set.CellCount(B) == 0 && set.PeerCount == 1, "an unentered peer allocates nothing");
        Check(!set.Update(0, 10.0, 20.0, 30.0, 0.0), "peer 0 is refused");
    }

    static void Separation()
    {
        var set = new InterestSet(16, 5.0, 15.0, 4);
        set.Enter(A);
        set.Enter(B);

        Check(set.Update(A, 10.0, 20.0, 30.0, 0.0), "peer A acquires its home cell");
        Check(set.Update(B, 2007.0, 2013.0, 2011.0, 0.0), "peer B acquires its home cell");
        Check(SamePeers(set.PeersFor(0, 0, 0), A), "A alone is interested near the origin");
        Check(SamePeers(set.PeersFor(20, 20, 20), B), "B alone is interested two kilometres away");
        Check(set.PeersFor(5, 5, 5).Length == 0, "an unvisited cell has no peers");
        Check(set.TotalCells == 2 && set.Verify(), "two separated peers hold two cells");

        // Reunion: B walks into A's cell.
        Check(set.Update(B, 15.0, 25.0, 35.0, 1.0), "peer B moves into A's cell");
        Check(SamePeers(set.PeersFor(0, 0, 0), A, B), "both peers are interested in the shared cell");
        Check(set.PeersFor(20, 20, 20).Length == 0, "B's old cell is released");
        Check(set.CellCount(B) == 1 && set.TotalCells == 2, "the shared cell is stored once per peer");

        // Separation again, in the other direction.
        Check(set.Update(A, 5013.0, 5007.0, 5009.0, 2.0), "peer A moves far away");
        Check(SamePeers(set.PeersFor(0, 0, 0), B), "B keeps the shared cell");
        Check(SamePeers(set.PeersFor(50, 50, 50), A), "A holds its new cell alone");
        Check(set.TotalCells == 2, "separation still holds two cells");

        // Returning restores the shared interest.
        Check(set.Update(A, 10.0, 20.0, 30.0, 3.0), "peer A returns");
        Check(SamePeers(set.PeersFor(0, 0, 0), A, B), "the reunion is symmetric");
        Check(set.PeersFor(50, 50, 50).Length == 0, "A's previous cell is released");
        Check(set.PeerCount == 2 && set.Verify(), "the reunion verifies");

        // CopyPeersFor is the allocation-free alternative and refuses a short buffer.
        ulong[] buffer = new ulong[4];
        Check(set.CopyPeersFor(0, 0, 0, buffer) == 2, "the buffer path reports both peers");
        ulong[] shortBuffer = new ulong[1];
        Check(set.CopyPeersFor(0, 0, 0, shortBuffer) == 0, "a short buffer is refused");
        Check(set.CopyPeersFor(9, 9, 9, buffer) == 0, "a cell with no peers reports zero");
    }

    static void Caps()
    {
        // A cap far smaller than the radius: the containing cell wins, the bound
        // still holds, and the set stays consistent.
        var tight = new InterestSet(2, 300.0, 400.0, 4);
        tight.Enter(A);
        Check(tight.Update(A, 10.0, 20.0, 30.0, 0.0), "the tight cap acquires cells");
        Check(tight.CellCount(A) == 2, "the cell cap is respected");
        Check(tight.IsInterested(A, 0, 0, 0), "the containing cell is acquired first");
        Check(tight.TotalCells == 2 && tight.Verify(), "a capped set verifies");
        Check(!tight.Update(A, 10.0, 20.0, 30.0, 1.0), "a full set adds nothing at the same position");
        Check(tight.CellCount(A) == 2, "a full set stays at its cap");

        // One cell per peer, two peers: the total bound is peers * cells.
        var shared = new InterestSet(1, 300.0, 400.0, 2);
        shared.Enter(A);
        shared.Enter(B);
        shared.Update(A, 500.0, 600.0, 700.0, 0.0);
        Check(shared.CellCount(A) == 1 && shared.IsInterested(A, 5, 6, 7), "one cell per peer");
        Check(shared.Update(B, 500.0, 600.0, 700.0, 0.0), "the second peer acquires the same cell");
        Check(shared.TotalCells == 2, "the total bound counts pairs, not distinct cells");
        Check(SamePeers(shared.PeersFor(5, 6, 7), A, B), "both peers are indexed for the cell");
        Check(shared.Verify(), "the shared cell verifies");

        // Explicit release, then re-acquisition on the next update.
        shared.RemoveCell(A, 5, 6, 7);
        Check(shared.CellCount(A) == 0 && !shared.IsInterested(A, 5, 6, 7), "RemoveCell releases one peer");
        Check(SamePeers(shared.PeersFor(5, 6, 7), B), "the other peer keeps the cell");
        Check(shared.TotalCells == 1 && shared.Verify(), "the release verifies");
        Check(shared.Update(A, 500.0, 600.0, 700.0, 1.0), "the next update re-acquires it");
        Check(shared.CellCount(A) == 1, "the re-acquisition took one cell");

        // The whole-cell variant releases every peer.
        shared.RemoveCell(5, 6, 7);
        Check(shared.CellCount(A) == 0 && shared.CellCount(B) == 0, "the cell variant releases every peer");
        Check(shared.PeersFor(5, 6, 7).Length == 0, "the reverse index is empty");
        Check(shared.TotalCells == 0 && shared.Verify(), "the whole-cell release verifies");
        shared.RemoveCell(5, 6, 7);
        shared.RemoveCell(A, 1, 1, 1);
        shared.RemoveCell(0, 1, 1, 1);
        Check(shared.Verify(), "repeated releases are harmless");
    }

    static void PeerLifecycle()
    {
        var set = new InterestSet(16, 5.0, 15.0, 2);
        set.Enter(A);
        set.Enter(A);
        Check(set.PeerCount == 1, "Enter is idempotent");
        Check(ZeroPeerRefused(set), "peer 0 cannot be entered");
        set.Enter(B);
        Check(EnterRefused(set, C), "a full peer table refuses a new peer");

        set.Update(A, 10.0, 20.0, 30.0, 0.0);
        set.Update(B, 15.0, 25.0, 35.0, 0.0);
        Check(SamePeers(set.PeersFor(0, 0, 0), A, B), "both peers are tracked");

        // Forget releases everything the peer held and frees its table slot.
        set.ForgetPeer(A);
        Check(set.PeerCount == 1 && set.CellCount(A) == 0, "ForgetPeer drops the peer");
        Check(!set.IsInterested(A, 0, 0, 0), "a forgotten peer has no interest");
        Check(SamePeers(set.PeersFor(0, 0, 0), B), "the forgotten peer left the reverse index");
        Check(!set.Update(A, 10.0, 20.0, 30.0, 1.0), "a forgotten peer cannot update");
        Check(set.TotalCells == 1 && set.Verify(), "the forget verifies");

        set.ForgetPeer(A);
        set.ForgetPeer(0);
        set.ForgetPeer(C);
        Check(set.PeerCount == 1 && set.Verify(), "repeated and unknown forgets are harmless");

        // The slot is reusable, and the peer comes back empty.
        set.Enter(C);
        Check(set.PeerCount == 2, "a freed slot can be reused");
        Check(set.Update(C, 10.0, 20.0, 30.0, 2.0), "the new peer acquires its cell");
        Check(SamePeers(set.PeersFor(0, 0, 0), B, C), "the new peer is indexed alongside the old one");

        // A peer at an absurd coordinate is clamped into the grid, not into a new key.
        Check(set.Update(C, 1e12, 1e12, 1e12, 3.0), "an absurd position is still accepted");
        Check(set.CellCount(C) == 1, "clamping still holds one cell");
        Check(set.IsInterested(C, StreamCell.MaxIndex, StreamCell.MaxIndex, StreamCell.MaxIndex), "the clamped cell is interested");
        Check(set.Verify(), "clamped interest verifies");

        set.ForgetPeer(B);
        set.ForgetPeer(C);
        Check(set.PeerCount == 0 && set.TotalCells == 0, "forgetting every peer empties the set");
        Check(set.PeersFor(0, 0, 0).Length == 0 && set.Verify(), "the empty set verifies");
    }

    // ---- far store ---------------------------------------------------------

    static void StoreConstruction()
    {
        Check(StoreRefused(0, 4, 8), "a zero entity cap is refused");
        Check(StoreRefused(-1, 4, 8), "a negative entity cap is refused");
        Check(StoreRefused(FarStore.HardMaxEntities + 1, 4, 8), "an unbounded entity cap is refused");
        Check(StoreRefused(4, 0, 8), "a zero tombstone cap is refused");
        Check(StoreRefused(4, FarStore.HardMaxTombstones + 1, 8), "an unbounded tombstone cap is refused");
        Check(StoreRefused(4, 4, 0), "a zero state cap is refused");
        Check(StoreRefused(4, 4, FarStore.HardMaxStateBytes + 1), "an unbounded state cap is refused");

        var store = new FarStore(4, 2, 8);
        Check(store.MaxEntities == 4 && store.MaxTombstones == 2 && store.MaxStateBytes == 8, "the caps are retained");
        Check(store.Count == 0 && store.TombstoneCount == 0 && store.BindingCount == 0, "a new store is empty");
        Check(store.Verify(), "an empty store verifies");
        Check(store.Summary() == "entities=0 tombstones=0 bindings=0 accepted=0 removed=0 refusedRevision=0 refusedEpoch=0 refusedState=0 refusedFull=0 ownerChanges=0 epochChanges=0 transferred=0 adopted=0 rebound=0 conflicts=0 rejected=0",
            "the empty summary has the documented shape: " + store.Summary());
    }

    static void UpsertBasics()
    {
        var store = new FarStore(8, 4, 4);
        byte[] state = new byte[] { 1, 2, 3 };
        Check(store.Upsert(7, 2, A, 1, 1, state, false, 5.0), "the first record is stored");

        FarRecord record;
        Check(store.TryGet(7, out record), "the record is readable");
        Check(record.Entity == 7, "the entity id is stored");
        Check(record.Kind == 2, "the kind is stored");
        Check(record.Owner == A, "the owner is stored");
        Check(record.Epoch == 1 && record.Revision == 1, "the first epoch and revision are stored");
        Check(!record.Loaded, "the loaded flag is stored");
        Check(record.LastSeen == 5.0, "the last-seen clock is stored");
        Check(record.State.Length == 3 && record.State[1] == 2, "the state bytes are stored");
        Check(store.Count == 1 && store.Verify(), "one record verifies");

        // The store copies state, so a caller cannot rewrite it afterwards.
        state[0] = 99;
        Check(store.TryGet(7, out record) && record.State[0] == 1, "the stored state is a defensive copy");

        // Invalid input never stores anything.
        int before = store.Count;
        Check(!store.Upsert(0, 2, A, 1, 1, null, false, 0.0), "a zero entity is refused");
        Check(!store.Upsert(8, 0, A, 1, 1, null, false, 0.0), "a zero kind is refused");
        Check(!store.Upsert(8, 2, 0, 1, 1, null, false, 0.0), "a zero owner is refused");
        Check(!store.Upsert(8, 2, A, 0, 1, null, false, 0.0), "a zero epoch is refused");
        Check(!store.Upsert(8, 2, A, 1, 0, null, false, 0.0), "a zero revision is refused");
        Check(!store.Upsert(8, 2, A, 1, 1, null, false, Double.NaN), "a NaN clock is refused");
        Check(!store.Upsert(8, 2, A, 1, 1, null, false, Double.PositiveInfinity), "an infinite clock is refused");
        Check(store.Count == before, "invalid input stores nothing");

        // A null state is an empty state, not a failure.
        Check(store.Upsert(8, 2, A, 1, 1, null, true, 0.0), "a record without state is stored");
        Check(store.TryGet(8, out record) && record.State != null && record.State.Length == 0, "the empty state is normalised");
        Check(store.Count == 2 && store.Verify(), "two records verify");
        Check(!store.TryGet(0, out record) && record == null, "entity 0 is never readable");
        Check(!store.TryGet(999, out record) && record == null, "an unknown entity is not readable");
    }

    static void Revisions()
    {
        var store = new FarStore(8, 4, 8);
        Check(store.Upsert(7, 2, A, 1, 1, null, false, 0.0), "revision 1 is stored");
        Check(!store.Upsert(7, 2, A, 1, 1, null, true, 1.0), "the same revision is refused");
        FarRecord record;
        Check(store.TryGet(7, out record) && !record.Loaded, "a refused duplicate changes nothing");
        Check(store.Upsert(7, 3, A, 1, 2, null, true, 2.0), "a newer revision is stored");
        Check(store.TryGet(7, out record) && record.Revision == 2 && record.Kind == 3 && record.Loaded, "the newer revision lands");
        Check(!store.Upsert(7, 2, A, 1, 1, null, false, 3.0), "an older revision is refused");
        Check(store.TryGet(7, out record) && record.Revision == 2, "an older revision cannot roll back");

        // An owner change inside one epoch is a normal newer revision.
        Check(store.Upsert(7, 2, B, 1, 3, null, true, 4.0), "a new owner may write a newer revision");
        Check(store.TryGet(7, out record) && record.Owner == B && record.Epoch == 1, "the owner change is stored");
        Check(store.Summary().Contains("ownerChanges=1 "), "the owner change is counted: " + store.Summary());
        Check(store.Summary().Contains("refusedRevision=2 "), "the two refused revisions are counted");
        Check(store.Summary().Contains("epochChanges=0 "), "no epoch changed yet");
        Check(store.Verify(), "revision ordering verifies");
    }

    static void Epochs()
    {
        var store = new FarStore(8, 4, 8);
        Check(store.Upsert(7, 2, A, 1, 5, null, true, 0.0), "epoch 1 revision 5 is stored");

        // A transfer keeps the revision and raises the epoch.
        Check(store.Upsert(7, 2, B, 2, 5, null, true, 1.0), "a higher epoch at the same revision is stored");
        FarRecord record;
        Check(store.TryGet(7, out record) && record.Owner == B && record.Epoch == 2, "the transfer is stored");
        Check(!store.Upsert(7, 2, A, 1, 6, null, true, 2.0), "a stale epoch is refused even with a newer revision");
        Check(store.TryGet(7, out record) && record.Epoch == 2 && record.Revision == 5, "a stale epoch cannot write");
        Check(!store.Upsert(7, 2, B, 2, 5, null, true, 3.0), "the transfer revision is not repeatable");
        Check(!store.Upsert(7, 2, B, 3, 4, null, true, 4.0), "a new epoch may not roll the revision back");
        Check(store.Upsert(7, 2, Host, 3, 5, null, true, 5.0), "a new epoch at the same revision is stored");
        Check(store.Upsert(7, 2, Host, 3, 6, null, true, 6.0), "the standard path resumes");
        Check(store.TryGet(7, out record) && record.Owner == Host && record.Epoch == 3 && record.Revision == 6, "the final owner and revision are stored");
        Check(store.Summary().Contains("epochChanges=2 "), "two epoch changes are counted: " + store.Summary());
        Check(store.Summary().Contains("refusedEpoch=1 "), "the stale epoch is counted");
        Check(store.Verify(), "epoch ordering verifies");
    }

    static void StateBounds()
    {
        var store = new FarStore(8, 4, 4);
        Check(store.Upsert(1, 1, A, 1, 1, new byte[4], false, 0.0), "a state at the cap is stored");
        Check(!store.Upsert(2, 1, A, 1, 1, new byte[5], false, 0.0), "a state over the cap is refused");
        Check(store.Count == 1, "an oversized state stores nothing");
        Check(store.Summary().Contains("refusedState=1 "), "the oversized state is counted");
        Check(store.Upsert(1, 1, A, 1, 2, new byte[1], false, 1.0), "a smaller state can replace it");
        FarRecord record;
        Check(store.TryGet(1, out record) && record.State.Length == 1, "the smaller state is stored");
        Check(store.Verify(), "state bounds verify");
    }

    static void Tombstones()
    {
        var store = new FarStore(8, 4, 8);
        Check(store.Upsert(9, 1, A, 1, 1, null, true, 0.0), "the record is stored");
        ReconcileAction action;
        Check(store.TryReconcileOnStreamIn(9, 900, out action) && action == ReconcileAction.Adopt, "the record binds a local id");

        Check(!store.Remove(0, 1), "a zero entity cannot be removed");
        Check(!store.Remove(9, 0), "a zero epoch cannot remove");
        Check(!store.Remove(9, 2), "a wrong epoch cannot remove");
        Check(!store.Remove(8, 1), "an unknown entity cannot be removed");
        Check(store.Count == 1 && store.Verify(), "a refused removal changes nothing");

        Check(store.Remove(9, 1), "the current epoch removes the record");
        Check(store.IsTombstoned(9), "the id is tombstoned");
        Check(store.Count == 0 && store.TombstoneCount == 1, "the record became a tombstone");
        FarRecord record;
        Check(!store.TryGet(9, out record), "a tombstoned record is not readable");
        Check(!store.MarkLoaded(9, true), "a tombstoned record cannot be loaded");
        Check(!store.Upsert(9, 1, A, 5, 9, null, true, 1.0), "a tombstoned id cannot be written again");
        Check(!store.Remove(9, 1), "a tombstone cannot be removed twice");
        uint local;
        Check(!store.TryLocal(9, out local), "the removal released the binding");
        Check(store.BindingCount == 0 && store.Verify(), "the tombstone left no binding");

        // No resurrection through the stream-in path either.
        Check(!store.TryReconcileOnStreamIn(9, 901, out action) && action == ReconcileAction.Reject, "a tombstoned id cannot stream in");
        Check(store.BindingCount == 0, "a refused stream-in binds nothing");
        Check(store.Count == 0 && store.IsTombstoned(9), "the tombstone is terminal");
        Check(store.Summary().Contains("removed=1 "), "the removal is counted");
    }

    static void LoadedFlag()
    {
        var store = new FarStore(8, 4, 8);
        Check(store.Upsert(3, 1, A, 1, 1, null, false, 0.0), "the record starts unloaded");
        Check(store.MarkLoaded(3, true), "the record can be marked loaded");
        FarRecord record;
        Check(store.TryGet(3, out record) && record.Loaded, "the loaded flag is set");
        Check(store.MarkLoaded(3, true), "marking it loaded again is still accepted");
        Check(store.MarkLoaded(3, false), "the record can be marked unloaded");
        Check(store.TryGet(3, out record) && !record.Loaded, "the loaded flag is cleared");
        Check(!store.MarkLoaded(0, true), "entity 0 has no loaded flag");
        Check(!store.MarkLoaded(999, true), "an unknown entity has no loaded flag");

        // A remote record carries its own loaded flag and replaces the local one.
        Check(store.Upsert(3, 1, A, 1, 2, null, true, 1.0), "a newer record is stored");
        Check(store.TryGet(3, out record) && record.Loaded, "the record's loaded flag wins");
        Check(store.Verify(), "the loaded flag verifies");
    }

    static void StoreCaps()
    {
        var store = new FarStore(2, 4, 8);
        Check(store.Upsert(1, 1, A, 1, 1, null, false, 0.0), "the first record fits");
        Check(store.Upsert(2, 1, A, 1, 1, null, false, 0.0), "the second record fits");
        Check(!store.Upsert(3, 1, A, 1, 1, null, false, 0.0), "a full store refuses a new entity");
        Check(store.Count == 2, "a refused entity registers nothing");
        Check(store.Summary().Contains("refusedFull=1 "), "the refusal is counted");
        Check(store.Upsert(1, 1, A, 1, 2, null, false, 1.0), "an existing entity can still be updated when full");
        Check(store.Remove(1, 1), "a removal frees a slot");
        Check(store.Upsert(3, 1, A, 1, 1, null, false, 2.0), "the freed slot is usable");
        Check(store.Count == 2 && store.TombstoneCount == 1 && store.Verify(), "the capped store verifies");

        // A full tombstone store refuses the removal so the death is never forgotten.
        var tight = new FarStore(4, 1, 8);
        Check(tight.Upsert(1, 1, A, 1, 1, null, false, 0.0), "tight: entity 1 is stored");
        Check(tight.Upsert(2, 1, A, 1, 1, null, false, 0.0), "tight: entity 2 is stored");
        Check(tight.Remove(1, 1), "tight: the only tombstone slot is used");
        Check(!tight.Remove(2, 1), "tight: a full tombstone store refuses the removal");
        Check(!tight.IsTombstoned(2), "tight: the refused removal tombstoned nothing");
        FarRecord record;
        Check(tight.TryGet(2, out record), "tight: the record stays live");
        Check(tight.TombstoneCount == 1 && tight.Count == 1, "tight: the store stays consistent");
        Check(tight.Summary().Contains("refusedFull=1 "), "tight: the refusal is counted");
        Check(tight.Verify(), "tight caps verify");
    }

    static void StreamIn()
    {
        var store = new FarStore(4, 4, 8);
        ReconcileAction action;
        uint previous, local, network;

        Check(store.TryReconcileOnStreamIn(100, 1000, out action) && action == ReconcileAction.Adopt, "a fresh pair is adopted");
        Check(store.BindingCount == 1, "the adoption binds one pair");
        Check(store.TryLocal(100, out local) && local == 1000, "the network id maps to the local id");
        Check(store.TryNetwork(1000, out network) && network == 100, "the local id maps back");

        // A repeated stream-in of the same pair is idempotent.
        Check(store.TryReconcileOnStreamIn(100, 1000, out action) && action == ReconcileAction.Adopt, "the same pair is adopted again");
        Check(store.BindingCount == 1, "the repeated adoption binds nothing new");

        // A different local id for the same network id rebinds and reports the old one.
        Check(store.TryReconcileOnStreamIn(100, 1001, out action, out previous) && action == ReconcileAction.Rebind, "a new local object rebinds");
        Check(previous == 1000, "the displaced local id is reported");
        Check(store.TryLocal(100, out local) && local == 1001, "the network id now maps to the new local id");
        Check(!store.TryNetwork(1000, out network), "the displaced local id is released");
        Check(store.TryNetwork(1001, out network) && network == 100, "the new local id maps back");
        Check(store.BindingCount == 1 && store.Verify(), "the rebind verifies");

        // Two network ids may never share one local object.
        Check(store.TryReconcileOnStreamIn(200, 2000, out action) && action == ReconcileAction.Adopt, "a second pair is adopted");
        Check(!store.TryReconcileOnStreamIn(300, 2000, out action) && action == ReconcileAction.Conflict, "a second entity conflicts on a bound local id");
        Check(!store.TryLocal(300, out local), "a conflict binds nothing");
        Check(!store.TryReconcileOnStreamIn(100, 2000, out action) && action == ReconcileAction.Conflict, "a bound network id conflicts with a foreign local id");
        Check(store.TryLocal(100, out local) && local == 1001, "a conflict changes no binding");
        Check(store.BindingCount == 2, "conflicts bind nothing");

        // Invalid ids are rejected, not bound.
        Check(!store.TryReconcileOnStreamIn(0, 1000, out action) && action == ReconcileAction.Reject, "a zero network id is rejected");
        Check(!store.TryReconcileOnStreamIn(100, 0, out action) && action == ReconcileAction.Reject, "a zero local id is rejected");
        Check(store.BindingCount == 2 && store.Verify(), "rejections bind nothing");

        // A tombstoned id can never be adopted.
        Check(store.Upsert(500, 1, A, 1, 1, null, true, 0.0), "a record to tombstone is stored");
        Check(store.Remove(500, 1), "the record is tombstoned");
        Check(!store.TryReconcileOnStreamIn(500, 5000, out action) && action == ReconcileAction.Reject, "a tombstoned id is rejected on stream-in");
        Check(!store.TryLocal(500, out local), "a rejected stream-in binds nothing");

        // The binding table is bounded by the entity cap; a rebind reuses a slot.
        var full = new FarStore(1, 2, 4);
        Check(full.TryReconcileOnStreamIn(1, 11, out action) && action == ReconcileAction.Adopt, "the first binding fits");
        Check(!full.TryReconcileOnStreamIn(2, 22, out action) && action == ReconcileAction.Reject, "a full binding table refuses a new binding");
        Check(full.BindingCount == 1, "the refused binding registered nothing");
        Check(full.TryReconcileOnStreamIn(1, 12, out action, out previous) && action == ReconcileAction.Rebind, "a rebind still fits a full table");
        Check(previous == 11, "the rebind reports the displaced id");
        Check(full.Verify(), "the full binding table verifies");

        Check(store.Summary().Contains("adopted=3 "), "the adoptions are counted: " + store.Summary());
        Check(store.Summary().Contains("rebound=1 "), "the rebind is counted");
        Check(store.Summary().Contains("conflicts=2 "), "the conflicts are counted");
        Check(store.Summary().Contains("rejected=3"), "the rejections are counted");
    }

    static void ForgetPeer()
    {
        var store = new FarStore(8, 4, 8);
        Check(store.Upsert(1, 2, A, 1, 1, new byte[] { 7 }, true, 0.0), "peer A owns entity 1");
        Check(store.Upsert(2, 2, A, 1, 1, null, false, 0.0), "peer A owns entity 2");
        Check(store.Upsert(3, 2, B, 1, 1, null, true, 0.0), "peer B owns entity 3");
        Check(store.Upsert(4, 2, A, 1, 1, null, true, 0.0), "peer A owns entity 4");
        Check(store.Remove(4, 1), "entity 4 is tombstoned");

        store.ForgetPeer(A);

        FarRecord record;
        Check(store.TryGet(1, out record), "the orphaned record is still live");
        Check(record.Owner == 0, "an orphaned record becomes host-reserved");
        Check(record.Epoch == 2, "the transfer bumps the epoch");
        Check(record.Revision == 1, "the transfer keeps the revision");
        Check(record.Loaded && record.State.Length == 1, "the transfer keeps the loaded flag and the state");
        Check(store.TryGet(2, out record) && record.Owner == 0 && record.Epoch == 2, "the second orphan is transferred");
        Check(store.TryGet(3, out record) && record.Owner == B && record.Epoch == 1, "another peer's record is untouched");
        Check(store.IsTombstoned(4), "a disconnect resurrects nothing");
        Check(store.Summary().Contains("transferred=2 "), "the transfers are counted: " + store.Summary());
        Check(store.Verify(), "the transfer verifies");

        // The departed peer's old epoch cannot write; the current epoch can.
        Check(!store.Upsert(1, 2, A, 1, 3, null, true, 1.0), "the departed owner's old epoch is refused");
        Check(store.Upsert(1, 2, Host, 2, 2, null, true, 2.0), "the host takes the transferred record");
        Check(!store.Upsert(2, 2, A, 1, 2, null, false, 3.0), "a stale epoch cannot reclaim the second record");
        Check(store.Upsert(2, 2, A, 2, 2, null, false, 4.0), "the current epoch can be reclaimed");

        // A departure applies to any owner id, including the host's.
        store.ForgetPeer(Host);
        Check(store.TryGet(1, out record) && record.Owner == 0 && record.Epoch == 3, "the host's records are transferred too");
        Check(!store.Upsert(1, 2, Host, 2, 3, null, true, 5.0), "the host's old epoch is refused");
        Check(store.Upsert(1, 2, Host, 3, 3, null, true, 6.0), "the current epoch can be reclaimed");

        // Unknown and zero peers are no-ops.
        string before = store.Summary();
        store.ForgetPeer(0);
        store.ForgetPeer(999);
        Check(store.Summary() == before, "unknown forgets change nothing");
        Check(store.TryGet(3, out record) && record.Owner == B && record.Epoch == 1, "an unrelated record is untouched");
        Check(store.Verify(), "the end state verifies");
    }

    static void StoreDecisions()
    {
        // One store exercising every refusal path at once, so the counters can be
        // compared against the calls that produced them.
        var store = new FarStore(2, 2, 4);
        Check(store.Upsert(1, 1, A, 1, 1, null, false, 0.0), "entity 1 is stored");
        Check(store.Upsert(2, 1, A, 1, 1, null, false, 0.0), "entity 2 is stored");
        Check(!store.Upsert(3, 1, A, 1, 1, null, false, 0.0), "a third entity is refused when full");
        Check(!store.Upsert(1, 1, A, 1, 1, null, false, 1.0), "a duplicate revision is refused");
        Check(!store.Upsert(1, 1, A, 0, 2, null, false, 1.0), "a zero epoch is invalid");
        Check(!store.Upsert(1, 1, A, 1, 2, new byte[5], false, 1.0), "an oversized state is refused");
        Check(store.Upsert(1, 1, A, 1, 2, new byte[4], false, 1.0), "a valid update is accepted");
        Check(store.Remove(1, 1), "entity 1 is removed at its old epoch");
        Check(!store.Remove(2, 5), "entity 2 cannot be removed at a foreign epoch");
        Check(store.Verify(), "the mixed store verifies");
        Check(store.Summary() == "entities=1 tombstones=1 bindings=0 accepted=3 removed=1 refusedRevision=1 refusedEpoch=1 refusedState=1 refusedFull=1 ownerChanges=0 epochChanges=0 transferred=0 adopted=0 rebound=0 conflicts=0 rejected=0",
            "the counters mirror the calls: " + store.Summary());
    }

    public static int Main()
    {
        InterestConstruction();
        CellGrid();
        Hysteresis();
        Separation();
        Caps();
        PeerLifecycle();

        StoreConstruction();
        UpsertBasics();
        Revisions();
        Epochs();
        StateBounds();
        Tombstones();
        LoadedFlag();
        StoreCaps();
        StreamIn();
        ForgetPeer();
        StoreDecisions();

        Console.WriteLine("PASS: interest/far-store core (" + checks + " checks): 100 m cell grid and clamping, enter/exit hysteresis with inclusive edges, exit band does not acquire, unchanged-update suppression, separation/reunion, peer and cell caps, per-peer and per-cell release, Enter/ForgetPeer lifecycle, far record upsert with revision and epoch ordering, state bounds, tombstones without resurrection, loaded flag, caps, stream-in adopt/rebind/conflict/reject, ForgetPeer transfer to host-reserved, Verify and Summary");
        return 0;
    }
}
