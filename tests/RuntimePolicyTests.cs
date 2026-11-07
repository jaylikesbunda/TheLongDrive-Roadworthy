using System;
using System.IO;
using System.Reflection;
using TldMp;
using UnityEngine;

internal static class RuntimePolicyTests
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
        MirrorSwitchesAndReflections();
        SmallFixes();
        Console.WriteLine("PASS: " + checks + " runtime-policy assertions; mirror switches default off, the reflection hold only fires for the preset, the four small fixes change only their guarded cases, radiators borrow the metal repair table only with the option on, and the trailer grip stands in only for a wired trailer.");
        return 0;
    }

    static void MirrorSwitchesAndReflections()
    {
        Check(RenderSwitches.IsOff("0") && !RenderSwitches.IsOff(null) && !RenderSwitches.IsOff("1") && !RenderSwitches.IsOff(""), "only the value 0 switches a mirror option off");
        Check(RenderSwitches.PresetMatches(2, 4, 2, 80f), "the exact preset values match");
        Check(!RenderSwitches.PresetMatches(0, 4, 2, 80f) && !RenderSwitches.PresetMatches(2, 2, 2, 80f) && !RenderSwitches.PresetMatches(2, 4, 0, 80f) && !RenderSwitches.PresetMatches(2, 4, 2, 120f), "any changed preset value means the preset is not in effect");
        Check(RenderSwitches.ShouldHoldReflections(true, true, true), "probes on in the world with the preset are held off");
        Check(!RenderSwitches.ShouldHoldReflections(false, true, true), "no hold before the world is active");
        Check(!RenderSwitches.ShouldHoldReflections(true, false, true), "no write when probes are already off");
        Check(!RenderSwitches.ShouldHoldReflections(true, true, false), "no hold when the preset is not in effect");
    }

    static void SmallFixes()
    {
        Check(!ParkedCarSleepPolicy.ShouldWriteVelocity(true, 0f, 0f, 0f, 1f, 0f, 0f), "a sleeping parked car skips the velocity write");
        Check(!ParkedCarSleepPolicy.ShouldWriteVelocity(false, 1f, 2f, 3f, 1f, 2f, 3f), "an unchanged velocity is not written again");
        Check(ParkedCarSleepPolicy.ShouldWriteVelocity(false, 1f, 2f, 3f, 1f, 2f, 3.0001f), "a changed velocity is written");
        Check(ParkedCarSleepPolicy.ShouldWriteVelocity(false, 0f, 0f, 0f, 0f, 0f, 0f) == false, "an awake body with the same zero velocity is not written");

        Check(IdlePourPausePolicy.Decide(false, false, 0) == IdlePourPausePolicy.Pause, "an idle pour with no particles pauses");
        Check(IdlePourPausePolicy.Decide(false, false, 3) == IdlePourPausePolicy.None, "a pour with particles still alive keeps running");
        Check(IdlePourPausePolicy.Decide(true, true, 0) == IdlePourPausePolicy.Resume, "requested emission resumes a paused pour");
        Check(IdlePourPausePolicy.Decide(true, false, 0) == IdlePourPausePolicy.None, "a running pour with emission is left alone");
        Check(IdlePourPausePolicy.Decide(false, true, 0) == IdlePourPausePolicy.None, "an already paused pour is not paused twice");

        Check(WornItemGuard.ShouldDrop(true, false), "a null renderer is dropped from the hide list");
        Check(WornItemGuard.ShouldDrop(false, true), "a renderer of the destroyed worn item is dropped");
        Check(!WornItemGuard.ShouldDrop(false, false), "a live renderer of another item stays hidden");

        Check(SliceGuard.FirstFailure(ref sliceLogged), "the first slicer failure is reported");
        Check(!SliceGuard.FirstFailure(ref sliceLogged), "later slicer failures are not reported again");

        Check(RadiatorRepairPolicy.Metal == (int)itemdatabase.repairProperty.metal, "the borrowed table is the native metal entry");
        Check(RadiatorRepairPolicy.UseMetal(true, true, false), "a radiator borrows the metal table while the option is on");
        Check(!RadiatorRepairPolicy.UseMetal(false, true, false), "turning the option off restores the native table");
        Check(!RadiatorRepairPolicy.UseMetal(true, false, false), "other parts keep their own table");
        Check(!RadiatorRepairPolicy.UseMetal(true, true, true), "a radiator that already accepts the brush is left alone");
        var none = new bool[][] { new bool[7], new bool[7] };
        var brush = new bool[][] { new bool[7], new bool[] { false, true, false, false, false, false, false } };
        Check(!RadiatorRepairPolicy.AcceptsTool(none, RadiatorRepairPolicy.WireBrush) && RadiatorRepairPolicy.AcceptsTool(brush, RadiatorRepairPolicy.WireBrush), "brush acceptance reads any state row");
        Check(!RadiatorRepairPolicy.AcceptsTool(null, 1) && !RadiatorRepairPolicy.AcceptsTool(brush, 9), "missing tables and tools are refused");
        PatchOptions.Parse(null);
        Check(!PatchOptions.RadiatorRepair, "radiator repair defaults off");
        PatchOptions.Parse("radiatorrepair=on\n");
        Check(PatchOptions.RadiatorRepair && PatchOptions.Format().Contains("radiatorrepair=on\n"), "radiator repair can be turned on and is saved");
        PatchOptions.Parse(null);

        Check(TrailerHitchPolicy.ShouldWire(true, true), "a trailer grip is linked to its trailer save");
        Check(!TrailerHitchPolicy.ShouldWire(false, true) && !TrailerHitchPolicy.ShouldWire(true, false), "a missing grip or trailer save is skipped");
        Check(TrailerHitchPolicy.UseGrip(false, true), "a wired trailer without its own pickupable reads the grip");
        Check(!TrailerHitchPolicy.UseGrip(true, true), "a native pickupable always wins");
        Check(!TrailerHitchPolicy.UseGrip(false, false), "unwired items keep the native null");
        PatchOptions.Parse("trailerhitch=off\nradiatorrepair=on\n");
        Check(PatchOptions.RadiatorRepair && !PatchOptions.Format().Contains("trailerhitch"), "a retired trailerhitch line is ignored and never saved");
        PatchOptions.Parse(null);
    }

    static bool sliceLogged;
}
