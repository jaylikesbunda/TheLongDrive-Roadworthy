using System;
using TldMp.Experimental;

internal static class ExperimentalHandoffTests
{
    static void Check(bool ok) { if (!ok) throw new Exception("Handoff invariant failed"); }
    public static int Main()
    {
        var graph = new EntityGraph(); var authority = new AuthorityRegistry();
        Check(graph.Add(1) && graph.Add(2) && graph.Attach(2, 1));
        Check(authority.Add(1, 10) && authority.Add(2, 10));
        ulong[] group; Check(graph.TryGroup(1, 2, out group));
        var hand = new Handoff(42, 1, graph.Revision, group, new ulong[] { 1, 1 }, 10, 20, 2);
        Check(!hand.ReadyAt(20, 42) && !hand.PreparedBy(20, 42, graph, authority) && !hand.PreparedBy(10, 41, graph, authority) && !hand.Commit(graph, authority));
        Check(hand.PreparedBy(10, 42, graph, authority) && !authority.Accepts(1, 10, 1));
        Check(!hand.ReadyAt(30, 42) && !hand.ReadyAt(20, 41) && hand.ReadyAt(20, 42));
        Check(hand.Commit(graph, authority) && !hand.Commit(graph, authority));
        Check(authority.Accepts(1, 20, 2) && authority.Accepts(2, 20, 2));
        var cancelled = new Handoff(43, 1, graph.Revision, group, new ulong[] { 2, 2 }, 20, 10, 2);
        Check(cancelled.PreparedBy(20, 43, graph, authority) && cancelled.ReadyAt(10, 43));
        cancelled.Abort(); Check(!cancelled.Commit(graph, authority));
        Check(authority.Accepts(1, 20, 2) && authority.Accepts(2, 20, 2));
        var changed = new Handoff(44, 1, graph.Revision, group, new ulong[] { 2, 2 }, 20, 10, 2);
        Check(changed.PreparedBy(20, 44, graph, authority) && changed.ReadyAt(10, 44));
        Check(graph.Detach(2));
        Check(!changed.Commit(graph, authority));
        Check(authority.Accepts(1, 20, 2) && authority.Accepts(2, 20, 2));
        Check(graph.Attach(2, 1));
        var stale = new Handoff(45, 1, graph.Revision, group, new ulong[] { 2, 2 }, 20, 10, 2);
        Check(graph.Detach(2));
        Check(!stale.PreparedBy(20, 45, graph, authority) && authority.Accepts(1, 20, 2));
        Console.WriteLine("PASS: handoff requires source and target readiness and supports abort");
        return 0;
    }
}
