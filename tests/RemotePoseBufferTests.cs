using System;
using TldMp;

internal static class RemotePoseBufferTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception("FAIL: " + message); }
    static bool Near(double a, double b, double tolerance) { return Math.Abs(a - b) <= tolerance; }

    static RemotePose At(double x, double y, double z)
    {
        var p = new RemotePose();
        p.X = x; p.Y = y; p.Z = z;
        p.Qw = 1.0;
        return p;
    }

    static void DelayFollowsIntervalAndClamps()
    {
        var steady = new RemotePoseBuffer();
        for (int i = 0; i < 20; i++) steady.Add(i * 0.1, At(0, 0, 0));
        Check(Near(steady.Delay, 0.15, 1e-9), "a steady 10 Hz stream gives 1.5 intervals of delay");

        var jittery = new RemotePoseBuffer();
        double t = 0.0;
        for (int i = 0; i < 40; i++) { t += (i % 2 == 0) ? 0.05 : 0.15; jittery.Add(t, At(0, 0, 0)); }
        Check(jittery.Delay > steady.Delay && jittery.Delay <= RemotePoseBuffer.MaxDelay, "jitter raises the delay within the cap");

        var slow = new RemotePoseBuffer();
        slow.Add(0.0, At(0, 0, 0));
        slow.Add(0.5, At(0, 0, 0));
        Check(slow.Delay == RemotePoseBuffer.MaxDelay, "very slow updates clamp the delay at 0.25 s");

        var fast = new RemotePoseBuffer();
        fast.Add(0.0, At(0, 0, 0));
        fast.Add(0.01, At(0, 0, 0));
        Check(fast.Delay == RemotePoseBuffer.MinDelay, "very fast updates clamp the delay at 0.06 s");
        Check(RemotePoseBuffer.DelayFor(0.0, 0.0) == RemotePoseBuffer.MinDelay, "an empty buffer uses the minimum delay");
    }

    static void InterpolatesBetweenBracketingSamples()
    {
        var buffer = new RemotePoseBuffer();
        buffer.Add(0.0, At(0, 0, 0));
        buffer.Add(1.0, At(10, 4, -2));
        RemotePose pose;
        Check(buffer.Sample(0.25, out pose), "a bracketed sample is available");
        Check(Near(pose.X, 2.5, 1e-9) && Near(pose.Y, 1.0, 1e-9) && Near(pose.Z, -0.5, 1e-9), "position is linear between brackets");
        Check(buffer.Sample(0.0, out pose) && Near(pose.X, 0.0, 1e-9), "render time at the oldest sample returns it");
        Check(buffer.Sample(1.0, out pose) && Near(pose.X, 10.0, 1e-9), "render time at the newest sample returns it");
    }

    static void RotationTakesTheShortestPath()
    {
        var a = new RemotePose();
        a.Qw = 1.0;
        // 90 degrees about Y, stored with the opposite sign: the same orientation.
        var b = new RemotePose();
        double s = Math.Sqrt(0.5);
        b.Qy = -s; b.Qw = -s;
        RemotePose mid = RemotePoseBuffer.Blend(a, b, 0.5);
        Check(Near(mid.Qy, Math.Sin(Math.PI / 8.0), 1e-6) && Near(mid.Qw, Math.Cos(Math.PI / 8.0), 1e-6),
            "halfway between identity and 90 degrees is 45 degrees about Y, not the long way round");
        double norm = Math.Sqrt(mid.Qx * mid.Qx + mid.Qy * mid.Qy + mid.Qz * mid.Qz + mid.Qw * mid.Qw);
        Check(Near(norm, 1.0, 1e-9), "blended rotation is normalized");
    }

    static void ExtrapolationIsCappedThenHeld()
    {
        var buffer = new RemotePoseBuffer();
        buffer.Add(0.0, At(0, 0, 0));
        buffer.Add(0.1, At(1, 0, 0));
        RemotePose pose;
        Check(buffer.Sample(0.2, out pose) && Near(pose.X, 2.0, 1e-9), "0.1 s past the newest sample extrapolates along its velocity");
        Check(buffer.Sample(5.0, out pose) && Near(pose.X, 2.0, 1e-9), "extrapolation stops at 0.1 s and the pose is held");
        Check(buffer.Sample(0.15, out pose) && Near(pose.X, 1.5, 1e-9), "a render time just past the newest sample extrapolates half the step");
    }

    static void TeleportAndLongGapResetAndSnap()
    {
        var buffer = new RemotePoseBuffer();
        buffer.Add(0.0, At(0, 0, 0));
        Check(buffer.Add(0.1, At(100, 0, 0)) == RemoteAddResult.Reset, "a position jump over 50 m resets the buffer");
        Check(buffer.Count == 1 && buffer.Resets == 1, "the reset keeps only the teleported sample");
        RemotePose snap;
        Check(buffer.TakeSnap(out snap) && Near(snap.X, 100.0, 1e-9), "the first take after a reset snaps to the new sample");
        Check(!buffer.TakeSnap(out snap), "the snap is taken once");

        var gap = new RemotePoseBuffer();
        gap.Add(0.0, At(0, 0, 0));
        Check(gap.Add(2.0, At(1, 0, 0)) == RemoteAddResult.Reset, "a gap longer than 1 s resets the buffer");
        Check(gap.Count == 1 && gap.SnapPending, "a long gap leaves one sample and a pending snap");
    }

    static void OutOfOrderSamplesAreDropped()
    {
        var buffer = new RemotePoseBuffer();
        buffer.Add(1.0, At(1, 0, 0));
        Check(buffer.Add(0.5, At(9, 0, 0)) == RemoteAddResult.OutOfOrder, "a sample older than the newest is refused");
        Check(buffer.Count == 1 && buffer.Dropped == 1, "a refused sample is counted and not stored");
        RemotePose pose;
        Check(buffer.Sample(1.0, out pose) && Near(pose.X, 1.0, 1e-9), "the buffer still holds the newest valid pose");
    }

    static void RingIsBounded()
    {
        var buffer = new RemotePoseBuffer();
        for (int i = 0; i < 80; i++) buffer.Add(i * 0.05, At(i * 0.01, 0, 0));
        Check(buffer.Count == RemotePoseBuffer.Capacity, "the ring keeps at most Capacity samples");
        Check(Near(buffer.NewestTime, 79 * 0.05, 1e-9), "the ring keeps the newest samples");
        RemotePose oldest;
        buffer.Sample(0.0, out oldest);
        Check(Near(oldest.X, (80 - RemotePoseBuffer.Capacity) * 0.01, 1e-9), "the oldest retained sample is the one Capacity steps back");
    }

    static void PolicyKeepsStockBehaviourByDefault()
    {
        Check(RemoteSmoothPolicy.KeepRemoteRole(1.5, 2), "a remote driver keeps the car through a 1.5 s hitch");
        Check(!RemoteSmoothPolicy.KeepRemoteRole(RemoteSmoothPolicy.CarSilenceSeconds + 0.1, 2), "after the silence window the game's own handover applies");
        Check(!RemoteSmoothPolicy.KeepRemoteRole(0.1, 1) && !RemoteSmoothPolicy.KeepRemoteRole(0.1, 0), "no remote role (nothing/notCar) is never kept");
        Check(!RemoteSmoothPolicy.KeepRemoteRole(-1, 2), "a backwards clock keeps nothing");
        Check(RemoteSmoothPolicy.PlayerEnabled(null) && RemoteSmoothPolicy.PlayerEnabled("1"), "player smoothing is on by default");
        Check(!RemoteSmoothPolicy.PlayerEnabled("0"), "TLD_MP_SMOOTH_PLAYERS=0 disables player smoothing");
        Check(!RemoteSmoothPolicy.CarEnabled(null,false) && !RemoteSmoothPolicy.CarEnabled("0",true), "car smoothing honors the option and the explicit off switch");
        Check(RemoteSmoothPolicy.CarEnabled(null,true) && RemoteSmoothPolicy.CarEnabled("1",false), "the option or the explicit on switch enables car smoothing");
    }

    static void CarGotSyncIsNativeWhenDisabledOrNotCarOrInCharge()
    {
        Check(RemoteSmoothPolicy.CarGotSyncIsNative(false, true, false), "disabled switch always runs the native teleport");
        Check(RemoteSmoothPolicy.CarGotSyncIsNative(true, false, false), "a non-car item always runs the native teleport");
        Check(RemoteSmoothPolicy.CarGotSyncIsNative(true, true, true), "a car this machine is in charge of runs the native teleport");
        Check(!RemoteSmoothPolicy.CarGotSyncIsNative(true, true, false), "an enabled, remote car is withheld from the teleport");
    }

    static void CarReleaseRules()
    {
        Check(RemoteSmoothPolicy.CarShouldRelease(true, false, 10.0, 10.0), "a destroyed car is released");
        Check(RemoteSmoothPolicy.CarShouldRelease(false, true, 10.0, 10.0), "a car this machine takes charge of is released");
        Check(RemoteSmoothPolicy.CarShouldRelease(false, false, 12.5, 10.0), "2.5 s without samples releases the car");
        Check(!RemoteSmoothPolicy.CarShouldRelease(false, false, 11.0, 10.0), "a car with recent samples stays tracked");
    }

    public static int Main(string[] args)
    {
        DelayFollowsIntervalAndClamps();
        InterpolatesBetweenBracketingSamples();
        RotationTakesTheShortestPath();
        ExtrapolationIsCappedThenHeld();
        TeleportAndLongGapResetAndSnap();
        OutOfOrderSamplesAreDropped();
        RingIsBounded();
        PolicyKeepsStockBehaviourByDefault();
        CarGotSyncIsNativeWhenDisabledOrNotCarOrInCharge();
        CarReleaseRules();
        Console.WriteLine("PASS: " + checks + " remote pose assertions; players interpolate with an adaptive delay, rotations take the shortest path, extrapolation is capped, teleports snap, out-of-order samples are dropped, and car smoothing stays native unless enabled for a remote car.");
        return 0;
    }
}
