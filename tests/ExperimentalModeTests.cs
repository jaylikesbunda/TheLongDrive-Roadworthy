using System;
using TldMp.Experimental;

internal static class ExperimentalModeTests
{
    static void Check(bool ok) { if (!ok) throw new Exception("Mode isolation invariant failed"); }
    public static int Main()
    {
        var mode = new ModeLatch();
        Check(mode.Selected == MultiplayerMode.Native && !mode.InSession);
        Check(!mode.Select((MultiplayerMode)99));
        Check(mode.Begin(false) && mode.Active == MultiplayerMode.Native);
        Check(!mode.Select(MultiplayerMode.Experimental) && !mode.Begin(true));
        ulong first = mode.Attempt;
        Check(!mode.End(0) && mode.End(first));
        Check(mode.Select(MultiplayerMode.Experimental) && !mode.Begin(false) && !mode.InSession);
        Check(mode.Begin(true) && mode.Active == MultiplayerMode.Experimental);
        Check(!mode.End(first) && mode.InSession);
        Check(!mode.Select(MultiplayerMode.Native));
        Check(mode.End(mode.Attempt));
        Check(mode.Active == MultiplayerMode.Native && mode.Select(MultiplayerMode.Native));
        Console.WriteLine("PASS: experimental mode selection and session latch");
        return 0;
    }
}
