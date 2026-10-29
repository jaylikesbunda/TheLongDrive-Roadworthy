using System;
using TldMp;

internal static class TerrainAlignPolicyTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception("FAIL: " + message); }

    // Hierarchy depth below the prefab root: 0 = the root itself, 1 = direct child, 2+ = nested.
    static bool RootRelative(int depth)
    {
        return TerrainAlignPolicy.UsesRootRelative(depth > 0, depth == 1);
    }

    public static int Main(string[] args)
    {
        Check(!RootRelative(0), "a root-level helper keeps its stock local position");
        Check(!RootRelative(1), "a direct child of the root keeps its stock local position");
        Check(RootRelative(2), "haz02 > Buszmegallo > HelpPos is measured from the root");
        Check(RootRelative(3), "deeper nesting is measured from the root");
        Check(!TerrainAlignPolicy.UsesRootRelative(false, false), "an unparented helper keeps its stock local position");
        Check(!TerrainAlignPolicy.UsesRootRelative(false, true), "parent flag is ignored without a parent");

        // haz02's nested stop: Buszmegallo at (-48.54,-1.02,-56.77), HelpPos at (-0.04,0,0.21).
        double rootRelativeX = -48.54 + -0.04, rootRelativeZ = -56.77 + 0.21;
        double stockX = -0.04, stockZ = 0.21;
        double distance = Math.Sqrt(rootRelativeX * rootRelativeX + rootRelativeZ * rootRelativeZ);
        Check(distance > 70 && distance < 80, "nested helper is about 75 m from the root, was " + distance);
        Check(Math.Sqrt(stockX * stockX + stockZ * stockZ) < 1, "stock offset is under a metre");
        Check(TerrainAlignPolicy.LookupRange(60, 75, 0, 36) == 111, "lookup includes the distant helper and its full radius");
        Check(TerrainAlignPolicy.LookupRange(60, 0, 0, 36) == 60, "existing larger range is preserved");
        Check(TerrainAlignPolicy.LevelStartingBusMarker("haz02", "Buszmegallo"), "starting area bus marker uses house height");
        Check(!TerrainAlignPolicy.LevelStartingBusMarker("haz02", "other"), "house markers unchanged");
        Check(!TerrainAlignPolicy.LevelStartingBusMarker("other", "Buszmegallo"), "other POIs unchanged");
        Console.WriteLine("PASS: " + checks + " terrain-align assertions; nested helpers are measured from the prefab root, direct children keep the stock value.");
        return 0;
    }
}
