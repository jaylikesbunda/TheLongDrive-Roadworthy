using System;
using System.IO;
using System.Reflection;
using TldMp;

internal static class NetUpdateCostTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            string name = new AssemblyName(e.Name).Name;
            string path = name == "Assembly-CSharp" ? args[1] : Path.Combine(args[0], name + ".dll");
            return File.Exists(path) ? Assembly.Load(File.ReadAllBytes(path)) : null;
        };
        Assembly.Load(File.ReadAllBytes(args[1]));
        return Run();
    }

    static int Run()
    {
        RestoreCache();
        CoalesceKeys();
        SchedulerCoalescing();
        CarRate();
        PoseSampleKinds();
        StatsLine();
        Console.WriteLine("PASS: " + checks + " network update-cost assertions; collision restores are skipped only when exactly repeated, only full-state motion types coalesce, passenger cars stream at 20 Hz within bounds with no sweep holes, and buffer outcomes are classified.");
        return 0;
    }

    static void RestoreCache()
    {
        var c = new CollisionRestoreCache();
        int[] item = { 11, 12, 13 }, player = { 101, 102 };
        Check(!c.CanSkip(1, 7, item, 3, player, 2), "an unseen pair must restore natively");
        c.RecordRestore(1, 7, item, 3, player, 2);
        Check(c.CanSkip(1, 7, item, 3, player, 2), "an exact repeat may be skipped");
        Check(c.PairsAvoided == 6, "avoided pair calls are counted");
        Check(!c.CanSkip(1, 8, item, 3, player, 2), "another player is a different pair");
        Check(!c.CanSkip(2, 7, item, 3, player, 2), "another item is a different pair");
        Check(!c.CanSkip(1, 7, new[] { 11, 12, 13, 14 }, 4, player, 2), "a new collider in the item forces a restore");
        Check(!c.CanSkip(1, 7, new[] { 11, 99, 13 }, 3, player, 2), "a replaced collider forces a restore");
        Check(!c.CanSkip(1, 7, new[] { 11, 12 }, 2, player, 2), "a removed collider forces a restore");
        Check(!c.CanSkip(1, 7, item, 3, new[] { 101, 103 }, 2), "changed player colliders force a restore");
        Check(c.CanSkip(1, 7, new[] { 11, 12, 13, 0 }, 3, player, 2), "only the first count ids are compared");
        c.PlayerIgnored(7);
        Check(!c.CanSkip(1, 7, item, 3, player, 2), "any ignore=true for the player invalidates its pairs");
        c.RecordRestore(1, 7, item, 3, player, 2);
        Check(c.CanSkip(1, 7, item, 3, player, 2), "a fresh restore after an ignore can be repeated");
        c.RecordRestore(5, 9, item, 3, player, 2);
        c.PlayerIgnored(7);
        Check(c.CanSkip(5, 9, item, 3, player, 2), "an ignore for one player leaves other players' pairs valid");
        c.Clear();
        Check(c.Count == 0 && !c.CanSkip(5, 9, item, 3, player, 2), "clear forgets everything");
        for (int i = 0; i < CollisionRestoreCache.MaxEntries + 5; i++) c.RecordRestore(i, 1, item, 3, player, 2);
        Check(c.Count <= CollisionRestoreCache.MaxEntries, "the cache is bounded");
        Check(!c.CanSkip(1, 7, null, 0, player, 2), "missing data never skips");
    }

    static byte[] Packet(byte type, uint id, int size)
    {
        var b = new byte[size];
        b[0] = type;
        BitConverter.GetBytes(id).CopyTo(b, 1);
        return b;
    }

    static void CoalesceKeys()
    {
        foreach (byte t in new byte[] { 16, 19, 20, 39 })
        {
            ulong k = StablePackets.LatestValueKey(Packet(t, 42, 49));
            Check(k != 0, "full-state type " + t + " coalesces");
            Check(k != StablePackets.LatestValueKey(Packet(t, 43, 49)), "different objects never share a key (" + t + ")");
        }
        Check(StablePackets.LatestValueKey(Packet(39, 42, 49)) != StablePackets.LatestValueKey(Packet(20, 42, 33)), "pose and velocity of one car keep separate keys");
        foreach (byte t in new byte[] { 10, 11, 14, 17, 18, 29, 43, 46, 1, 254 })
            Check(StablePackets.LatestValueKey(Packet(t, 42, 49)) == 0, "type " + t + " is never coalesced");
        Check(StablePackets.LatestValueKey(Packet(39, 0, 49)) == 0, "object id 0 is never coalesced");
        Check(StablePackets.LatestValueKey(new byte[] { 39, 1 }) == 0, "a truncated packet is never coalesced");
        Check(StablePackets.LatestValueKey(null) == 0, "null is never coalesced");
    }

    static void SchedulerCoalescing()
    {
        var s = new NetDeliveryScheduler();
        byte[] a = Packet(39, 42, 49), b = Packet(39, 42, 49), c = Packet(39, 43, 49);
        a[10] = 1; b[10] = 2;
        ulong ka = StablePackets.LatestValueKey(a), kc = StablePackets.LatestValueKey(c);
        s.Enqueue(5, NetDeliveryLane.Motion, false, ka, a);
        s.Enqueue(5, NetDeliveryLane.Motion, false, kc, c);
        s.Enqueue(5, NetDeliveryLane.Motion, false, ka, b);
        Check(s.Coalesced == 1, "a newer pose replaces the queued older pose");
        Check(s.PendingCount(5) == 2, "one frame per object stays queued");
        var sent = new System.Collections.Generic.List<byte[]>();
        s.Drain(5, 10, 1 << 20, delegate(ulong p, NetDeliveryLane l, byte[] bytes, bool r) { sent.Add(bytes); return true; });
        Check(sent.Count == 2 && sent[0][10] == 2 && BitConverter.ToUInt32(sent[1], 1) == 43, "the newest pose leaves in the older pose's slot, other objects keep their order");
    }

    static void CarRate()
    {
        Check(CarPosePolicy.ParseRate(null) == 20 && CarPosePolicy.ParseRate("x") == 20, "default target is 20 Hz");
        Check(CarPosePolicy.ParseRate("2") == 5 && CarPosePolicy.ParseRate("99") == 60 && CarPosePolicy.ParseRate("12") == 12, "the target is clamped to 5..60");
        Check(CarPosePolicy.RateFor(0, true) == 60 && CarPosePolicy.RateFor(0, false) == 20, "the high-rate option streams passenger cars at 60 Hz, off keeps 20 Hz");
        Check(CarPosePolicy.RateFor(CarPosePolicy.ParseOverride("12"), true) == 12 && CarPosePolicy.ParseOverride("x") == 0, "TLD_MP_CAR_RATE overrides the option");
        Check(Math.Abs(PlayerRatePolicy.Interval(0.28f, true) - 1f / 30) < 1e-6, "high rate sends the local player 30 times a second");
        Check(PlayerRatePolicy.Interval(0.28f, false) == 0.28f, "off keeps the game's own player interval");
        Check(PlayerRatePolicy.Interval(0.01f, true) == 0.01f, "never slower than the game's own interval");
        Check(PlayerRatePolicy.Interval(0f, true) == 0f && float.IsNaN(PlayerRatePolicy.Interval(float.NaN, true)), "a broken native interval is left alone");
        PatchOptions.Parse(null);
        Check(PatchOptions.HighRate && PatchOptions.Format().Contains("highrate=on"), "high rate defaults on and is saved");
        PatchOptions.Parse("highrate=off");
        Check(!PatchOptions.HighRate && PatchOptions.Format().Contains("highrate=off"), "high rate can be turned off");
        PatchOptions.Parse(null);
        Check(CarPosePolicy.CountForRate(0, true, true, 1f, 20) == 19, "20 Hz over the native 1 s interval is 19 extra poses");
        Check(CarPosePolicy.CountForRate(0, true, true, 1f, 5) == 4, "5 Hz keeps the previous policy");
        Check(CarPosePolicy.CountForRate(0, true, true, 1f, 30) == 29, "30 Hz is the cap");
        Check(CarPosePolicy.CountForRate(0, true, true, 10f, 30) == CarPosePolicy.MaxBurstCount, "a long interval is capped");
        Check(CarPosePolicy.CountForRate(0, false, true, 1f, 20) == 0 && CarPosePolicy.CountForRate(0, true, false, 1f, 20) == 0, "empty or parked cars keep the native count");
        Check(CarPosePolicy.CountForRate(2, true, true, 1f, 20) == 19, "a pickup burst base is respected");
        Check(CarPosePolicy.CountForRate(40, true, true, 1f, 20) == 40, "never below the native count");
        Check(CarPosePolicy.CountForRate(0, true, true, 0f, 20) == 4 && CarPosePolicy.CountForRate(0, true, true, float.NaN, 20) == 4, "a broken interval falls back to the previous policy");
        Check(CarPosePolicy.CountForRate(0, true, true, 0.25f, 20) == 4, "a short interval never drops below the previous policy");
        Check(CarPosePolicy.ContinueStream(true, true, true, 1.5), "the newest stream keeps sending past one interval (no sweep-length hole)");
        Check(!CarPosePolicy.ContinueStream(false, true, true, 0.1), "a newer stream for the same car ends this one (no doubled rate)");
        Check(!CarPosePolicy.ContinueStream(true, false, true, 0.1), "losing authority ends the stream");
        Check(!CarPosePolicy.ContinueStream(true, true, false, 0.1), "a parked or empty car falls back to the native burst");
        Check(!CarPosePolicy.ContinueStream(true, true, true, CarPosePolicy.StreamMaxSeconds), "a stream whose replacement never comes ends at the cap");
        Check(!CarPosePolicy.ContinueStream(true, true, true, -1), "a backwards clock ends the stream");
        Check(Math.Abs(CarPosePolicy.StreamSpacing(20) - 0.05f) < 1e-6, "20 Hz spaces poses 50 ms apart");
        Check(Math.Abs(CarPosePolicy.StreamSpacing(1) - 1f / CarPosePolicy.MinTargetHz) < 1e-6 && Math.Abs(CarPosePolicy.StreamSpacing(500) - 1f / CarPosePolicy.MaxTargetHz) < 1e-6, "stream spacing respects the rate bounds");
    }

    static RemotePose At(double x)
    {
        var p = new RemotePose();
        p.X = x; p.Qw = 1;
        return p;
    }

    static void PoseSampleKinds()
    {
        var buffer = new RemotePoseBuffer();
        buffer.Add(1.0, At(0));
        buffer.Add(1.2, At(2));
        RemotePose pose; int kind;
        Check(buffer.Sample(1.1, out pose, out kind) && kind == RemotePoseBuffer.SampleInterpolated && Math.Abs(pose.X - 1) < 1e-9, "inside the samples is interpolated");
        Check(buffer.Sample(1.25, out pose, out kind) && kind == RemotePoseBuffer.SampleExtrapolated, "just past the newest is extrapolated");
        Check(buffer.Sample(1.5, out pose, out kind) && kind == RemotePoseBuffer.SampleHeld, "far past the newest is held");
        Check(buffer.Sample(0.5, out pose, out kind) && kind == RemotePoseBuffer.SampleOldest, "before the oldest returns the oldest");
        RemotePose same;
        Check(buffer.Sample(1.25, out same) && Math.Abs(same.X - 2.5) < 1e-9, "the original signature returns the same pose");
    }

    static void StatsLine()
    {
        NetStats.Offered(Packet(39, 1, 49));
        NetStats.Offered(Packet(20, 1, 33));
        NetStats.Received(Packet(39, 1, 49));
        NetStats.Sent(Packet(39, 1, 49));
        NetStats.PoseGap(0.05);
        NetStats.PoseGap(0.3);
        NetStats.Pending(3, 120);
        var cache = new CollisionRestoreCache();
        string line = NetStats.TakeLine(10, 5, 4, cache, 7, 2, 1);
        Check(line.StartsWith("[TLD MP net] t=10.000 window=5.0s"), "line header");
        Check(line.Contains("top=39:1/49 20:1/33"), "top types are ordered by bytes");
        Check(line.Contains("gapsOver100ms=1 gapsOver250ms=1") && line.Contains("maxGapMs=300.0"), "pose gaps are reported");
        Check(line.Contains("maxPending=3") && line.Contains("coalesced=4") && line.Contains("interpolated=7 extrapolated=2 held=1"), "queue and buffer counters are reported");
        string next = NetStats.TakeLine(15, 5, 4, cache, 0, 0, 0);
        Check(next.Contains("coalesced=0") && next.Contains("samples=0") && next.Contains("maxPending=0"), "each window starts from zero");
    }
}
