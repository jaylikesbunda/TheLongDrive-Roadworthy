using System;
using System.IO;
using System.Reflection;
using TldMp;

internal static class PlacementSkipTests
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

    static PlacementSignature Sig(int chunks, int objs, int children, int neededHash, long epoch)
    {
        var s = new PlacementSignature();
        s.Chunks = chunks; s.Objs = objs; s.Children = children; s.NeededCount = 325; s.NeededHash = neededHash; s.Epoch = epoch;
        return s;
    }

    static int Run()
    {
        var p = new PlacementSkipPolicy();
        object cat = new object();
        var steady = Sig(325, 4000, 3900, 11, 0);
        Check(!p.ShouldSkip(cat, steady), "an unseen category always runs its pass");
        p.AfterWalk(cat, steady, false);
        Check(!p.ShouldSkip(cat, steady), "a pass that hit its per-frame cap leaves work waiting, so the next pass runs");
        p.AfterWalk(cat, steady, true);
        Check(p.ShouldSkip(cat, steady), "an unchanged category after a finished pass is skipped");
        Check(!p.ShouldSkip(cat, Sig(325, 4000, 3899, 11, 0)), "a prop destroyed in play (parent lost a child) runs the pass");
        p.AfterWalk(cat, steady, true);
        Check(!p.ShouldSkip(cat, Sig(326, 4010, 3900, 11, 0)), "a newly generated chunk runs the pass");
        p.AfterWalk(cat, steady, true);
        Check(!p.ShouldSkip(cat, Sig(325, 4000, 3900, 12, 0)), "a changed needed-chunk set runs the pass");
        p.AfterWalk(cat, steady, true);
        Check(!p.ShouldSkip(cat, Sig(325, 4000, 3900, 11, 1)), "our map code changing place flags runs the pass");
        p.AfterWalk(cat, steady, true);
        int skipped = 0;
        for (int i = 0; i < PlacementSkipPolicy.MaxSkippedFrames; i++) if (p.ShouldSkip(cat, steady)) skipped++;
        Check(skipped == PlacementSkipPolicy.MaxSkippedFrames, "steady frames are skipped");
        Check(!p.ShouldSkip(cat, steady), "a full pass still runs every MaxSkippedFrames frames");
        p.AfterWalk(cat, steady, true);
        Check(p.ShouldSkip(cat, steady), "after the periodic pass skipping resumes");
        object other = new object();
        Check(!p.ShouldSkip(other, steady), "categories are tracked separately");
        p.Clear();
        Check(!p.ShouldSkip(cat, steady), "a cleared policy runs every pass again");
        Console.WriteLine("PASS: " + checks + " placement skip assertions; a place/remove pass is skipped only when nothing changed since a pass that finished, and a full pass runs every " + PlacementSkipPolicy.MaxSkippedFrames + " frames.");
        return 0;
    }
}
