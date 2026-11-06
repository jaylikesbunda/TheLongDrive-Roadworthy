using System;
using TldMp.Experimental;

internal static class ExperimentalGraphTests
{
    static void Check(bool ok) { if (!ok) throw new Exception("Experimental graph invariant failed"); }
    public static int Main()
    {
        var graph = new EntityGraph();
        Check(!graph.Add(0) && graph.Add(1) && graph.Add(2) && graph.Add(3));
        Check(!graph.Add(1) && !graph.Attach(1, 99) && !graph.Attach(1, 1));
        Check(graph.Attach(2, 1) && graph.Attach(3, 2));
        ulong revision = graph.Revision;
        Check(graph.Attach(3, 2) && graph.Revision == revision);
        ulong[] group;
        Check(!graph.TryGroup(1, 2, out group) && group == null);
        Check(graph.TryGroup(1, 3, out group) && group.Length == 3 && group[0] == 1 && group[2] == 3);
        Check(!graph.Attach(1, 3) && graph.ParentOf(2) == 1 && graph.ParentOf(3) == 2);
        Check(!graph.RemoveLeaf(1) && !graph.RemoveLeaf(2));
        Check(graph.Detach(3) && graph.ParentOf(3) == 0 && graph.RemoveLeaf(2));
        Check(graph.Revision > revision);
        Check(!graph.Add(2) && !graph.Attach(3, 2) && graph.RemoveLeaf(1));
        Console.WriteLine("PASS: experimental graph lifecycle and cycle prevention");
        return 0;
    }
}
