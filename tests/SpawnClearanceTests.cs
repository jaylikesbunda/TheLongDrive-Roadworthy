using System;
using TldMp;

internal static class SpawnClearanceTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    public static int Main(string[] args)
    {
        Offsets();
        Rules();
        Tracker();
        Console.WriteLine("PASS: " + checks + " spawn clearance assertions; rings are nearest first, only solid non-ground non-self colliders block, the hold settles 1 s before release, searches every 0.5 s and gives up at 8 s (16 s across moves).");
        return 0;
    }

    static void Offsets()
    {
        var o = SpawnClearancePolicy.Offsets(1.5, 15);
        Check(o.Count == 8 * (1 + 2 + 3 + 4 + 5 + 6 + 7 + 8 + 9 + 10), "10 rings of 8k points");
        double last = 0;
        foreach (var p in o)
        {
            double r = Math.Sqrt(p[0] * p[0] + p[1] * p[1]);
            Check(r + 1e-9 >= last, "candidates never get closer");
            Check(r <= 15 + 1e-9, "candidates stay within the radius");
            last = r;
        }
        Check(Math.Abs(o[0][0] - 1.5) < 1e-9 && Math.Abs(o[0][1]) < 1e-9, "the first candidate is 1.5 m along +x");
        var again = SpawnClearancePolicy.Offsets(1.5, 15);
        Check(again[37][0] == o[37][0] && again[37][1] == o[37][1], "the order is deterministic");
        Check(SpawnClearancePolicy.Offsets(0, 15).Count == 0 && SpawnClearancePolicy.Offsets(1, double.NaN).Count == 0, "bad inputs give no candidates");
    }

    static void Rules()
    {
        Check(SpawnClearancePolicy.Blocks(false, false, false, true), "a solid prop, car or wall blocks");
        Check(!SpawnClearancePolicy.Blocks(true, false, false, true), "triggers never block");
        Check(!SpawnClearancePolicy.Blocks(false, true, false, true), "the joining player's own colliders never block");
        Check(!SpawnClearancePolicy.Blocks(false, false, true, true), "terrain and roads never block");
        Check(!SpawnClearancePolicy.Blocks(false, false, false, false), "disabled colliders never block");
        Check(SpawnClearancePolicy.UsableGround(1, false, 0), "flat static ground is usable");
        Check(!SpawnClearancePolicy.UsableGround(0.3, false, 0), "steep slopes and walls are not ground");
        Check(!SpawnClearancePolicy.UsableGround(1, true, 0), "a car roof or loose prop is not ground");
        Check(!SpawnClearancePolicy.UsableGround(1, false, 4.5) && !SpawnClearancePolicy.UsableGround(1, false, -4.5), "spots more than 4 m higher or lower are rejected");
        Check(SpawnClearancePolicy.UsableGround(0.45, false, 3.9), "the limits are inclusive");
    }

    static void Tracker()
    {
        var t = new SpawnClearanceTracker();
        Check(t.Evaluate(true, 10.0) == SpawnClearanceAction.Hold, "a clear spot is held while it settles");
        Check(t.Evaluate(true, 10.5) == SpawnClearanceAction.Hold, "still settling at 0.5 s");
        Check(t.Evaluate(true, 11.0) == SpawnClearanceAction.Release, "clear for 1 s releases");

        t.Reset();
        Check(t.Evaluate(false, 0) == SpawnClearanceAction.Search, "a blocked spot searches at once");
        Check(t.Evaluate(false, 0.2) == SpawnClearanceAction.Hold, "no new search before 0.5 s");
        Check(t.Evaluate(false, 0.5) == SpawnClearanceAction.Search && t.Searches == 2, "searches repeat every 0.5 s");
        Check(t.Evaluate(true, 1.0) == SpawnClearanceAction.Hold, "a spot that clears must settle");
        Check(t.Evaluate(false, 1.2) == SpawnClearanceAction.Search, "blocked again searches again");
        Check(t.Evaluate(false, 9.3) == SpawnClearanceAction.GiveUp && t.GaveUp, "8 s blocked at one spot gives up");
        Check(t.Evaluate(false, 9.4) == SpawnClearanceAction.Release, "after giving up the native release proceeds");

        t.Reset();
        Check(!t.GaveUp, "reset forgets a give-up");
        t.Evaluate(false, 0);
        t.Moved();
        Check(t.Moves == 1, "moves are counted");
        Check(t.Evaluate(true, 1) == SpawnClearanceAction.Hold, "a new spot must settle");
        Check(t.Evaluate(false, 5) == SpawnClearanceAction.Search, "the moved-to spot can block again");
        t.Moved();
        Check(t.Evaluate(false, 12) == SpawnClearanceAction.Search, "the per-spot timer restarts after a move");
        Check(t.Evaluate(false, 16) == SpawnClearanceAction.GiveUp, "16 s after the first block it gives up regardless of moves");
    }
}
