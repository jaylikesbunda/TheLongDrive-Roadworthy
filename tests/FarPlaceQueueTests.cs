using System;
using System.Collections.Generic;
using TldMp;

internal static class FarPlaceQueueTests
{
    static int checks;
    static double now;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception("FAIL: " + message); }
    static double Clock() { return now; }

    static void DedupesQueuedIds()
    {
        var policy = new FarPlaceQueuePolicy();
        Check(policy.Enqueue(7), "first enqueue of an id is accepted");
        Check(!policy.Enqueue(7), "a second enqueue of a queued id is refused");
        Check(policy.Count == 1 && policy.Queued == 1, "a duplicate does not grow the queue or the queued count");
    }

    static void PlacesInFifoOrder()
    {
        var policy = new FarPlaceQueuePolicy();
        policy.Enqueue(3); policy.Enqueue(1); policy.Enqueue(2);
        var seen = new List<uint>();
        int handled = policy.Drain(double.PositiveInfinity, Clock, id => { seen.Add(id); return true; });
        Check(handled == 3, "a full drain handles every queued id");
        Check(seen.Count == 3 && seen[0] == 3 && seen[1] == 1 && seen[2] == 2, "ids are placed in the order they were queued");
        Check(policy.Count == 0 && policy.Placed == 3, "a drained queue is empty and its placements are counted");
    }

    static void BudgetStopsAfterTimeButPlacesAtLeastOne()
    {
        var policy = new FarPlaceQueuePolicy();
        for (uint id = 1; id <= 3; id++) policy.Enqueue(id);
        now = 0;
        int handled = policy.Drain(3.0, Clock, id => { now += 2.0; return true; });
        Check(handled == 2, "the drain stops once elapsed time reaches the budget");
        Check(policy.Count == 1, "ids past the budget stay queued for the next frame");

        now = 0;
        var zero = new FarPlaceQueuePolicy();
        zero.Enqueue(9);
        Check(zero.Drain(0.0, Clock, id => true) == 1, "a zero budget still places one id per frame");
        Check(zero.Count == 0, "the single id placed under a zero budget leaves the queue");
    }

    static void CapRefusesAndCountsOverflow()
    {
        var policy = new FarPlaceQueuePolicy();
        for (uint id = 0; id < FarPlaceQueuePolicy.MaxQueued; id++)
            Check(policy.Enqueue(id), "ids up to the cap are accepted");
        Check(policy.Count == FarPlaceQueuePolicy.MaxQueued, "the queue holds exactly its cap");
        Check(!policy.Enqueue(FarPlaceQueuePolicy.MaxQueued), "an id beyond the cap is refused");
        Check(policy.Dropped == 1 && policy.Count == FarPlaceQueuePolicy.MaxQueued, "a refused id is counted as dropped and the queue does not grow");
        Check(!policy.Enqueue(0) && policy.Dropped == 1, "a duplicate at the cap is refused as a duplicate, not counted as overflow");
        Check(policy.MaxQueue == FarPlaceQueuePolicy.MaxQueued, "the peak queue length is recorded");
    }

    static void FlushDrainsEverything()
    {
        var policy = new FarPlaceQueuePolicy();
        for (uint id = 20; id < 25; id++) policy.Enqueue(id);
        now = 0;
        int handled = policy.Drain(double.PositiveInfinity, () => { now += 100.0; return now; }, id => true);
        Check(handled == 5 && policy.Count == 0, "an unbounded flush places every queued id regardless of elapsed time");
    }

    static void DroppedIdsAreCounted()
    {
        var policy = new FarPlaceQueuePolicy();
        for (uint id = 10; id < 13; id++) policy.Enqueue(id);
        policy.Drain(double.PositiveInfinity, Clock, id => id == 11);
        Check(policy.Placed == 1 && policy.Dropped == 2, "ids that fail revalidation are dropped and counted");
        policy.Enqueue(40); policy.Enqueue(41);
        policy.Clear();
        Check(policy.Count == 0 && policy.Dropped == 4, "clearing a world drops the queued ids and counts them");
    }

    static void SummaryReportsAndResetsWindow()
    {
        var policy = new FarPlaceQueuePolicy();
        policy.Enqueue(1); policy.Enqueue(2);
        policy.Drain(double.PositiveInfinity, Clock, id => true);
        Check(policy.TakeSummary() == "farPlace{queued=2 placed=2 dropped=0 maxQueue=2}", "summary reports the window counts and peak queue");
        Check(policy.TakeSummary() == "farPlace{queued=0 placed=0 dropped=0 maxQueue=0}", "summary resets the window");
    }

    static void OnlyThePeriodicPassDefers()
    {
        // featureOff, worldStarted, loading, dontAskOnSpawn, hasPlayer
        Check(!FarPlaceTiming.MustPlaceNow(false, true, false, false, true), "steady-state periodic pass defers");
        Check(FarPlaceTiming.MustPlaceNow(true, true, false, false, true), "the switch forces synchronous placement");
        Check(FarPlaceTiming.MustPlaceNow(false, false, false, false, true), "world load (UpdInstant) places synchronously before LoadPlayerStuff");
        Check(FarPlaceTiming.MustPlaceNow(false, true, true, false, true), "a save load places synchronously");
        Check(FarPlaceTiming.MustPlaceNow(false, true, false, true, true), "the construction spawner places synchronously");
        Check(FarPlaceTiming.MustPlaceNow(false, true, false, false, false), "no player places synchronously");
    }

    public static int Main(string[] args)
    {
        DedupesQueuedIds();
        PlacesInFifoOrder();
        BudgetStopsAfterTimeButPlacesAtLeastOne();
        CapRefusesAndCountsOverflow();
        FlushDrainsEverything();
        DroppedIdsAreCounted();
        SummaryReportsAndResetsWindow();
        OnlyThePeriodicPassDefers();
        Console.WriteLine("PASS: " + checks + " far-place queue assertions; ids are deduped, placed FIFO within a 3 ms budget (at least one per frame), capped at 4096, flushed in full, and dropped ids are counted.");
        return 0;
    }
}
