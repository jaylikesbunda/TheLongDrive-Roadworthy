using System;
using TldMp.Experimental;

internal static class ExperimentalGraphAuthorityTests
{
    static void Check(bool ok) { if (!ok) throw new Exception("Graph ownership invariant failed"); }
    public static int Main()
    {
        var graph = new EntityGraph();
        var authority = new AuthorityRegistry();
        for (ulong id = 1; id <= 3; id++) { Check(graph.Add(id)); Check(authority.Add(id, 10)); }
        Check(graph.Attach(2, 1) && graph.Attach(3, 2));
        ulong[] ids;
        Check(graph.TryGroup(1, 3, out ids));
        ulong revision = graph.Revision;
        var epochs = new ulong[ids.Length];
        for (int i = 0; i < ids.Length; i++) Check(authority.TryEpoch(ids[i], 10, out epochs[i]));
        ulong next;
        Check(authority.Transfer(3, 1, 30, out next)); // competing change after snapshot
        Check(!GraphTransfer.Commit(graph, authority, 1, revision, ids, epochs, 10, 20, 3));
        Check(authority.Accepts(1, 10, 1) && authority.Accepts(2, 10, 1));
        Check(authority.Accepts(3, 30, 2));
        Check(graph.Detach(3));
        Check(!GraphTransfer.Commit(graph, authority, 1, revision, ids, epochs, 10, 20, 3));
        Check(graph.TryGroup(1, 3, out ids));
        Check(GraphTransfer.Commit(graph, authority, 1, graph.Revision, ids, new ulong[] { 1, 1 }, 10, 20, 3));
        Check(authority.Accepts(1, 20, 2) && authority.Accepts(2, 20, 2));
        Console.WriteLine("PASS: changed graph member rejects whole ownership transfer");
        return 0;
    }
}
