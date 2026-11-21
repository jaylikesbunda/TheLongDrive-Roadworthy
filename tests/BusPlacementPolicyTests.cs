using System;
using TldMp;

internal static class BusPlacementPolicyTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    public static int Main()
    {
        // The floating start-house stop: in new worlds the near terrain collider was
        // not ready within 5 s, the props deadline marked placement done and the stop
        // stayed at the prefab height. World-loading reasons now wait much longer.
        Check(!BusPlacementPolicy.ReleaseProps(5.1f, "terrain unavailable at stop"), "a stop waiting for terrain is not abandoned at 5 s");
        Check(!BusPlacementPolicy.ReleaseProps(30f, "terrain unavailable at destination"), "terrain at the roadside spot is waited for too");
        Check(BusPlacementPolicy.ReleaseProps(BusPlacementPolicy.WorldDeadline, "terrain unavailable at stop"), "props still appear eventually");
        foreach (string reason in new[] { "roads unavailable", "roads unavailable at destination", "road not generated", "road mesh unavailable", "road data unavailable", null, "" })
        {
            Check(BusPlacementPolicy.WaitsForWorld(reason), "world-loading reason: " + reason);
            Check(!BusPlacementPolicy.ReleaseProps(10f, reason), "world-loading reason waits: " + reason);
        }
        // Geometry results that cannot change are released quickly, as before.
        foreach (string reason in new[] { "road clearance too far move=81", "road clearance did not converge", "house blocks destination", "missing stop footprint", "road has no usable segment" })
        {
            Check(BusPlacementPolicy.Terminal(reason) && !BusPlacementPolicy.WaitsForWorld(reason), "terminal reason: " + reason);
            Check(!BusPlacementPolicy.ReleaseProps(0.2f, reason) && BusPlacementPolicy.ReleaseProps(0.6f, reason), "terminal reasons settle briefly: " + reason);
        }
        // Anything else keeps the short deadline.
        Check(!BusPlacementPolicy.ReleaseProps(4.9f, "invalid road mesh") && BusPlacementPolicy.ReleaseProps(5f, "invalid road mesh"), "other reasons keep the 5 s deadline");
        Check(BusPlacementPolicy.ReleaseProps(float.NaN, "terrain unavailable at stop"), "a broken clock never holds props forever");
        Check(BusPlacementPolicy.GroundOnlyLimit > BusPlacementPolicy.WorldDeadline, "height-only grounding keeps trying after the props are out");
        // Height-only grounding.
        Check(BusPlacementPolicy.GroundShift(1022.655, 1019.4) < -3.2 && BusPlacementPolicy.GroundShift(1022.655, 1019.4) > -3.3, "a floating stop drops onto the ground");
        Check(BusPlacementPolicy.GroundShift(100, 100.5) == 0.5, "a sunken stop rises onto the ground");
        Check(BusPlacementPolicy.GroundShift(100, 100.01) == 0, "a stop already on the ground is left alone");
        Check(BusPlacementPolicy.GroundShift(100, double.NaN) == 0 && BusPlacementPolicy.GroundShift(double.PositiveInfinity, 3) == 0, "unknown heights never move the stop");
        Console.WriteLine("PASS: " + checks + " bus placement policy assertions; the start-house stop waits up to " + BusPlacementPolicy.WorldDeadline + " s for terrain and roads, terminal geometry settles in " + BusPlacementPolicy.TerminalSettle + " s, and a stop released early is still put on the ground.");
        return 0;
    }
}
