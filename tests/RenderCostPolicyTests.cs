using System;
using System.IO;
using System.Reflection;
using TldMp;
using UnityEngine;

internal static class RenderCostPolicyTests
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
        CameraNesting();
        Mirrors();
        Roads();
        Console.WriteLine("PASS: " + checks + " render-cost assertions; nested camera time is split exactly, the mirror gate never adds a render, road bakes only provably identical roads.");
        return 0;
    }

    static void CameraNesting()
    {
        var core = new CameraCostCore();
        // main 0..100 with two mirrors inside (10..30, 40..45), far 100..160.
        core.Begin(1, "main", 0);
        core.Begin(2, "mirror", 10); core.End(2, 30);
        core.Begin(3, "mirror", 40); core.End(3, 45);
        core.End(1, 100);
        core.Begin(4, "far", 100); core.End(4, 160);
        core.FrameBoundary();
        var main = core.Get("main"); var mirror = core.Get("mirror"); var far = core.Get("far");
        Check(main.Inclusive == 100 && main.Exclusive == 75, "main exclusive time must exclude nested mirrors");
        Check(mirror.Renders == 2 && mirror.Exclusive == 25 && mirror.MaxExclusive == 20, "mirror renders aggregate per label");
        Check(far.Exclusive == 60 && far.Inclusive == 60, "sibling cameras are independent");
        Check(core.Frames == 1 && core.Dropped == 0, "a clean frame drops nothing");

        core.ResetWindow();
        core.Begin(1, "main", 0);
        core.Begin(2, "mirror", 5);
        core.End(1, 50); // mirror never ended: abandoned, main still closes
        Check(core.Dropped == 1 && core.Get("main").Exclusive == 50 && core.Get("mirror") == null, "an unmatched nested render is dropped, not charged");
        core.End(9, 60);
        Check(core.Get("main").Renders == 1, "an end without a begin is ignored");
        core.Begin(5, "far", 70);
        core.FrameBoundary();
        Check(core.Dropped == 2 && core.Frames == 1, "an unfinished render is dropped at the frame boundary");

        core.ResetWindow();
        for (int i = 0; i < CameraCostCore.MaxDepth + 3; i++) core.Begin(i, "x", i);
        Check(core.Overflow == 3, "depth overflow is counted, never thrown");
        core.FrameBoundary();
        core.End(1, 5);
        Check(core.Get("x") == null, "after a boundary nothing stale can close");

        core.ResetWindow();
        core.Begin(1, "main", 100); core.End(1, 90);
        Check(core.Get("main").Inclusive == 0, "a backwards clock is clamped to zero");
    }

    static void Mirrors()
    {
        // Exhaustive: the gate may only turn a native render into a skip.
        int combos = 0;
        for (int bits = 0; bits < 32; bits++)
        {
            bool native = (bits & 1) != 0, feature = (bits & 2) != 0, vr = (bits & 4) != 0, known = (bits & 8) != 0, inside = (bits & 16) != 0;
            bool result = MirrorOnScreen.Decide(native, feature, vr, known, inside);
            if (!native) Check(!result, "a mirror native skips must stay skipped");
            if (native && (!feature || vr || !known)) Check(result, "uncertain or disabled cases keep the native render");
            if (native && feature && !vr && known) Check(result == inside, "only an off-screen mirror is skipped");
            combos++;
        }
        Check(combos == 32, "every mirror input combination is covered");
    }

    static void Roads()
    {
        Vector3 one = Vector3.one;
        Check(RoadBake.Eligibility(true, true, true, true, 1, 1, one, false, false) == null, "a plain placed road is baked");
        Check(RoadBake.Eligibility(false, true, true, true, 1, 1, one, false, false) == "disabled", "the feature switch disables baking");
        Check(RoadBake.Eligibility(true, false, true, true, 1, 1, one, false, false) == "no-renderer", "no skinned renderer stays native");
        Check(RoadBake.Eligibility(true, true, true, false, 1, 1, one, false, false) == "no-mesh", "no baked mesh stays native");
        Check(RoadBake.Eligibility(true, true, true, true, 1, 1, one, false, true) == "already-baked", "a road is never baked twice");
        Check(RoadBake.Eligibility(true, true, false, true, 1, 1, one, false, false) == "renderer-disabled", "a hidden road stays as native");
        Check(RoadBake.Eligibility(true, true, true, true, 1, 1, one, true, false) == "lod-group", "LOD-managed roads stay native");
        Check(RoadBake.Eligibility(true, true, true, true, 2, 1, one, false, false) == "material-count", "submesh/material mismatch stays native");
        Check(RoadBake.Eligibility(true, true, true, true, 0, 0, one, false, false) == "material-count", "an empty mesh stays native");
        Check(RoadBake.Eligibility(true, true, true, true, 1, 1, new Vector3(1f, 2f, 1f), false, false) == null, "a scaled road is baked with its own scaled mesh");
        Check(RoadBake.Eligibility(true, true, true, true, 1, 1, Vector3.zero, false, false) == "scale", "a zero scale stays native");
        Check(RoadBake.Eligibility(true, true, true, true, 1, 1, new Vector3(-1f, 1f, 1f), false, false) == "scale", "a mirrored scale stays native");
        Check(RoadBake.Eligibility(true, true, true, true, 1, 1, new Vector3(float.NaN, 1f, 1f), false, false) == "scale", "a NaN scale stays native");
        Check(RoadBake.UnitScale(new Vector3(1.00005f, 0.99995f, 1f)), "float noise around unit scale is accepted");
        Check(!RoadBake.UnitScale(new Vector3(1.001f, 1f, 1f)), "a real scale difference is rejected");
        Check(!RoadBake.UnitScale(new Vector3(float.NaN, 1f, 1f)), "a NaN scale is rejected");
        Check(!RoadBake.UnitScale(new Vector3(-1f, 1f, 1f)), "a mirrored scale is rejected");
        Check(RoadBake.PositiveScale(new Vector3(1f, 2f, 0.5f)), "a positive non-unit scale is accepted");
        Check(!RoadBake.PositiveScale(new Vector3(float.PositiveInfinity, 1f, 1f)), "an infinite scale is rejected");
        Vector3 inverse = RoadBake.InverseScale(new Vector3(2f, 4f, 0.5f));
        Check(Math.Abs(inverse.x - 0.5f) < 1e-6f && Math.Abs(inverse.y - 0.25f) < 1e-6f && Math.Abs(inverse.z - 2f) < 1e-6f, "the child scale cancels the baked scale");
        Check(Math.Abs(RoadBake.Tolerance(10f) - 0.02f) < 1e-6f, "tolerance is relative to the bounds diagonal");
        Check(Math.Abs(RoadBake.Tolerance(1000f) - 0.05f) < 1e-6f, "tolerance never exceeds five centimetres");
        Check(Math.Abs(RoadBake.Tolerance(0.1f) - 0.002f) < 1e-6f, "small meshes keep the relative tolerance");
        Vector3[] truth = { new Vector3(10f, 1f, 20f), new Vector3(11f, 2f, 21f) };
        Vector3[] shifted = { new Vector3(10.2f, 1f, 20f), new Vector3(11.2f, 2f, 21f) };
        Vector3[] wrong = { new Vector3(13f, 1f, 20f), new Vector3(14f, 2f, 21f) };
        float identityError, inverseError;
        Check(RoadBake.ChooseScale(truth, wrong, truth, 10f, false, out identityError, out inverseError) == 1, "an exact identity bake wins at a non-unit scale");
        Check(RoadBake.ChooseScale(wrong, truth, truth, 10f, false, out identityError, out inverseError) == 2, "an exact inverse bake wins at a non-unit scale");
        Check(RoadBake.ChooseScale(shifted, truth, truth, 10f, false, out identityError, out inverseError) == 2, "a clearly better candidate wins");
        Check(RoadBake.ChooseScale(shifted, shifted, truth, 10f, false, out identityError, out inverseError) == 0, "two equally wrong candidates are rejected");
        Check(RoadBake.ChooseScale(truth, wrong, truth, 10f, true, out identityError, out inverseError) == 1, "a unit-scale road verifies the identity bake");
        Check(RoadBake.ChooseScale(shifted, shifted, truth, 10f, true, out identityError, out inverseError) == 0, "a unit-scale road outside tolerance is rejected");
        Vector3 far = new Vector3(10.04f, 1f, 20f);
        Vector3[] near = { far, new Vector3(11.04f, 2f, 21f) };
        Check(RoadBake.ChooseScale(near, wrong, truth, 1000f, true, out identityError, out inverseError) == 1, "large bounds allow up to five centimetres");
        Vector3[] nan = { new Vector3(float.NaN, 1f, 20f), new Vector3(11f, 2f, 21f) };
        Check(RoadBake.ChooseScale(nan, truth, truth, 10f, false, out identityError, out inverseError) == 0, "a NaN candidate is rejected");
    }
}
