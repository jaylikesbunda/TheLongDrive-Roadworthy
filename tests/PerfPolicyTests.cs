using System;
using System.IO;
using System.Reflection;
using TldMp;

internal static class PerfPolicyTests
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
        Grass();
        Roads();
        Lenses();
        Shadows();
        Console.WriteLine("PASS: " + checks + " performance policy assertions; grass markers stay on wherever they can reach the grass camera's square, the road pass is skipped only when nothing it reads has changed, binocular lenses render whenever a screen camera can see them, and cobweb shadows stay on near the player.");
        return 0;
    }

    static void Grass()
    {
        const float half = 75f, margin = GrassMarkerPolicy.Margin;
        Check(GrassMarkerPolicy.Needed(0, 0, 0.5f, 0.5f, 0, 0, half, half, margin), "a marker under the camera is needed");
        Check(GrassMarkerPolicy.Needed(74, -74, 0.5f, 0.5f, 0, 0, half, half, margin), "a marker inside the square is needed");
        Check(GrassMarkerPolicy.Needed(half + margin, 0, 0, 0, 0, 0, half, half, margin), "the margin edge counts as needed");
        Check(!GrassMarkerPolicy.Needed(half + margin + 1, 0, 0, 0, 0, 0, half, half, margin), "a marker past the margin is not needed");
        Check(!GrassMarkerPolicy.Needed(0, 500, 1, 1, 0, 0, half, half, margin), "a far marker is not needed");
        Check(GrassMarkerPolicy.Needed(half + margin + 3, 0, 4, 4, 0, 0, half, half, margin), "a large marker whose edge reaches the margin is needed");
        Check(GrassMarkerPolicy.Needed(1000, 1000, 0, 0, 1000 - half, 1000, half, half, margin), "the square follows the camera");
        Check(GrassMarkerPolicy.Needed(float.NaN, 0, 0, 0, 0, 0, half, half, margin) && GrassMarkerPolicy.Needed(0, 0, 0, 0, float.NaN, 0, half, half, margin), "unknown positions keep the marker on");
        Check(GrassMarkerPolicy.Margin >= GrassMarkerPolicy.JumpDistance, "a full re-check happens before a marker can outrun the margin");
    }

    static void Roads()
    {
        Check(RoadIdlePolicy.PassNeeded(false, false, 0f, false, false, false, false), "the first pass always runs");
        Check(!RoadIdlePolicy.PassNeeded(true, false, 0.1f, false, false, false, false), "an idle pass with nothing changed is skipped");
        Check(RoadIdlePolicy.PassNeeded(true, true, 0.1f, false, false, false, false), "a pass after one that placed or removed pieces runs");
        Check(RoadIdlePolicy.PassNeeded(true, false, RoadIdlePolicy.MaxIdleSeconds, false, false, false, false), "a full pass runs at least once a second");
        Check(RoadIdlePolicy.PassNeeded(true, false, 0.1f, true, false, false, false), "a draw distance change runs a pass");
        Check(RoadIdlePolicy.PassNeeded(true, false, 0.1f, false, true, false, false), "a road data change runs a pass");
        Check(RoadIdlePolicy.PassNeeded(true, false, 0.1f, false, false, true, false), "player movement runs a pass");
        Check(RoadIdlePolicy.PassNeeded(true, false, 0.1f, false, false, false, true), "a road piece appearing or disappearing runs a pass");
        Check(RoadIdlePolicy.MaxIdleSeconds <= 1f && RoadIdlePolicy.MoveTolerance <= 0.25, "idle limits stay small");
    }

    static void Shadows()
    {
        float on = SmallShadowPolicy.OnDistance, off = SmallShadowPolicy.OffDistance;
        Check(SmallShadowPolicy.Casts(true, 0f) && SmallShadowPolicy.Casts(false, 1f), "a near cobweb casts");
        Check(SmallShadowPolicy.Casts(true, off * off) && !SmallShadowPolicy.Casts(true, (off + 0.1f) * (off + 0.1f)), "a casting cobweb stops just past the off distance");
        Check(!SmallShadowPolicy.Casts(false, (on + 1f) * (on + 1f)) && SmallShadowPolicy.Casts(false, on * on), "a switched-off cobweb casts again by the on distance");
        Check(SmallShadowPolicy.Casts(false, float.NaN), "an unknown distance casts");
        Check(on < off && on >= 15f, "cobweb shadows stay on near the player with a gap against flicker");
        Check(SmallShadowPolicy.IsTarget("cobwebmaterial") && !SmallShadowPolicy.IsTarget("BigRock05") && !SmallShadowPolicy.IsTarget(null), "only cobwebs are targeted");
    }

    static void Lenses()
    {
        Check(LensCameraPolicy.Render(true, 0f), "a lens a screen camera can see renders every frame");
        Check(!LensCameraPolicy.Render(false, 0.01f), "a hidden lens just refreshed is held");
        Check(LensCameraPolicy.Render(false, LensCameraPolicy.HiddenRefreshSeconds), "a hidden lens still refreshes on time");
        Check(LensCameraPolicy.Render(false, float.NaN), "an unknown refresh time renders");
        Check(LensCameraPolicy.HiddenRefreshSeconds <= .25f, "hidden lenses refresh at least four times a second");
    }
}
