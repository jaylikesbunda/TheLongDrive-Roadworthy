using System;
using TldMp;

internal static class ItemSettlePolicyTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception("FAIL: " + message); }

    static void HoldsWithoutGroundThenReleasesWhenGroundAppears()
    {
        var policy = new ItemSettlePolicy();
        Check(policy.ShouldHold(1, false, false, 100.0), "kinematic item with no ground beneath it must stay frozen");
        Check(policy.Held == 1 && policy.Tracked == 1, "first hold is counted once");
        Check(policy.ShouldHold(1, false, false, 101.0), "second pass without ground still holds");
        Check(policy.Held == 1, "a continuing hold is not counted again");
        Check(!policy.ShouldHold(1, false, true, 102.0), "ground arriving releases the item to the native unfreeze");
        Check(policy.Released == 1 && policy.Tracked == 0, "a released hold is counted and forgotten");
    }

    static void GroundFromTheStartNeverHolds()
    {
        var policy = new ItemSettlePolicy();
        Check(!policy.ShouldHold(2, false, true, 0.0), "an item with ground under it unfreezes at once");
        Check(policy.Held == 0 && policy.Released == 0 && policy.Tracked == 0, "an item that never held is not counted");
    }

    static void NativeOwnedItemsAreNeverHeld()
    {
        var policy = new ItemSettlePolicy();
        Check(policy.ShouldHold(3, false, false, 0.0), "setup: the item starts held");
        Check(!policy.ShouldHold(3, true, false, 1.0), "a held, attached, stored or buried item is left to the native game");
        Check(policy.Tracked == 0, "a native-owned item is forgotten");
        Check(policy.Held == 1 && policy.Released == 0 && policy.Timeouts == 0, "native ownership does not count as a release or timeout");
        Check(!policy.ShouldHold(4, true, false, 0.0), "a native-owned item with no ground is not held");
    }

    static void HoldIsCappedAtTwentySeconds()
    {
        var policy = new ItemSettlePolicy();
        Check(policy.ShouldHold(5, false, false, 0.0), "setup: hold starts at t=0");
        Check(policy.ShouldHold(5, false, false, 19.9), "still held just before the 20 s cap");
        Check(!policy.ShouldHold(5, false, false, 20.0), "the 20 s cap releases the item even without ground");
        Check(policy.Timeouts == 1 && policy.Tracked == 0, "the timeout is counted and forgotten");
    }

    static void ForgetClearsTheTimer()
    {
        var policy = new ItemSettlePolicy();
        policy.ShouldHold(6, false, false, 0.0);
        policy.Forget(6);
        Check(policy.Tracked == 0, "forget removes a destroyed or unfrozen item");
        Check(policy.ShouldHold(6, false, false, 50.0) && policy.Held == 2, "a new hold after forget starts a fresh 20 s window");
    }

    static void TableIsBounded()
    {
        var policy = new ItemSettlePolicy();
        for (int i = 0; i < ItemSettlePolicy.MaxTracked; i++)
            Check(policy.ShouldHold(1000 + i, false, false, 0.0), "fresh held items fill the table");
        Check(policy.Tracked == ItemSettlePolicy.MaxTracked, "table holds exactly its bound");
        Check(!policy.ShouldHold(9999, false, false, 1.0), "a full table refuses to track another hold");
        Check(policy.Tracked == ItemSettlePolicy.MaxTracked, "refused holds do not grow the table");
        Check(!policy.ShouldHold(1000, false, false, 25.0), "stale entries past the cap still time out when queried");
    }

    static void PruneFreesStaleEntriesWhenFull()
    {
        var policy = new ItemSettlePolicy();
        for (int i = 0; i < ItemSettlePolicy.MaxTracked; i++)
            policy.ShouldHold(2000 + i, false, false, 0.0);
        Check(policy.ShouldHold(3000, false, false, 25.0), "a full table of stale entries is pruned so a new item can hold");
        Check(policy.Tracked == 1, "pruning drops only entries past the cap");
    }

    static void SummaryReportsAndResetsWindow()
    {
        var policy = new ItemSettlePolicy();
        policy.ShouldHold(7, false, false, 0.0);
        policy.ShouldHold(7, false, true, 1.0);
        Check(policy.TakeSummary() == "itemSettle{held=1 released=1 timeouts=0}", "summary reports the window counts");
        Check(policy.TakeSummary() == "itemSettle{held=0 released=0 timeouts=0}", "summary resets the window");
    }

    public static int Main(string[] args)
    {
        HoldsWithoutGroundThenReleasesWhenGroundAppears();
        GroundFromTheStartNeverHolds();
        NativeOwnedItemsAreNeverHeld();
        HoldIsCappedAtTwentySeconds();
        ForgetClearsTheTimer();
        TableIsBounded();
        PruneFreesStaleEntriesWhenFull();
        SummaryReportsAndResetsWindow();
        Console.WriteLine("PASS: " + checks + " item-settle assertions; unsupported items hold at most 20 s, native-owned items are never held, and the table is bounded.");
        return 0;
    }
}
