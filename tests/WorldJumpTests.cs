using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using TldMp;

internal static class WorldJumpTests
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
        JumpPolicy();
        RecentreFrame();
        HandlerTiming();
        Console.WriteLine("PASS: " + checks + " world-jump assertions; car samples survive a world recentre, view jumps are told apart from driving, and slow handlers are reported.");
        return 0;
    }

    static void JumpPolicy()
    {
        Check(WorldJumpPolicy.Distance(0, 0, 0, 3, 4, 0) == 5, "distance is euclidean");
        Check(!WorldJumpPolicy.IsJump(1.5, 1.0 / 60), "a driving frame is not a jump");
        Check(!WorldJumpPolicy.IsJump(30, 0.9), "a 900 ms stall at highway speed is not a jump");
        Check(WorldJumpPolicy.IsJump(4500, 0.02), "a kilometre-scale move in one frame is a jump");
        Check(WorldJumpPolicy.IsJump(40, 0.02), "40 m in one frame is a jump");
        Check(WorldJumpPolicy.IsJump(double.NaN, 0.02), "a NaN position is reported");
        Check(!WorldJumpPolicy.IsJump(20, -1), "a backwards clock does not invent a jump");
        Check(WorldJumpPolicy.OriginChanged(0, 0, 0, -4500, 0, 0), "a recentre is seen");
        Check(!WorldJumpPolicy.OriginChanged(1, 2, 3, 1, 2, 3), "an unchanged origin is not a recentre");
    }

    // The game recentres by moving every object by dir and adding dir to the
    // origin. Unity = origin + global. A car moving steadily in global space must
    // keep one continuous buffer across the recentre.
    static void RecentreFrame()
    {
        double originBefore = 0, dir = -4500;
        var global = new RemotePoseBuffer();
        var unity = new RemotePoseBuffer();
        for (int i = 0; i < 10; i++)
        {
            double t = i * 0.05, g = 2490 + i * 1.5;
            double origin = i < 5 ? originBefore : originBefore + dir;
            global.Add(t, Pose(g));
            unity.Add(t, Pose(origin + g));
        }
        Check(global.Resets == 0, "global-space samples do not reset at a recentre");
        Check(unity.Resets == 1, "unity-space samples would jump 4.5 km at the recentre (the old bug)");
        RemotePose pose;
        Check(global.Sample(0.225, out pose) && Math.Abs(pose.X - (2490 + 4.5 * 1.5)) < 1e-9, "a render time across the recentre interpolates in global space");
        Check(Math.Abs((originBefore + dir + pose.X) - (dir + 2490 + 4.5 * 1.5)) < 1e-9, "converted back with the new origin it lands beside the car");
    }

    static RemotePose Pose(double x)
    {
        var p = new RemotePose();
        p.X = x; p.Qw = 1;
        return p;
    }

    static void HandlerTiming()
    {
        long msTicks = Stopwatch.Frequency / 1000;
        Check(!NetStats.Handled(new byte[] { 16, 0 }, msTicks), "a 1 ms handler is not logged");
        Check(NetStats.Handled(new byte[] { 43, 0, 0 }, msTicks * 30), "a 30 ms handler is logged");
        Check(!NetStats.Handled(null, msTicks * 100), "an empty message is ignored");
        Check(!NetStats.Handled(new byte[] { 16 }, -5), "a negative duration is ignored");
        string top = NetStats.TopHandlers(6);
        Check(top.StartsWith("43:30.00/30.00"), "the slowest type leads the list: " + top);
        Check(top.Contains("16:1.00/1.00"), "every handled type is listed: " + top);
        Check(NetStats.IsSlowUpdate(NetStats.SlowUpdateMs + 1) && !NetStats.IsSlowUpdate(NetStats.SlowUpdateMs), "slow-update threshold is exclusive");
        string line = NetStats.TakeLine(1, 5, 0, null, 0, 0, 0);
        Check(line.Contains("handlerMs{top=43:30.00/30.00 16:1.00/1.00}"), "the net line reports handler cost: " + line);
        line = NetStats.TakeLine(2, 5, 0, null, 0, 0, 0);
        Check(line.Contains("handlerMs{top=}"), "the window resets handler cost");
    }
}
