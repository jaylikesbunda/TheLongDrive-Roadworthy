namespace TldMp.Experimental
{
    // Must run on the same session thread as every graph/authority mutation.
    internal static class GraphTransfer
    {
        internal static bool Matches(EntityGraph graph, ulong root, ulong graphRevision, ulong[] ids, int maxEntities)
        {
            if (graph == null || ids == null || graph.Revision != graphRevision) return false;
            ulong[] current;
            if (!graph.TryGroup(root, maxEntities, out current) || current.Length != ids.Length) return false;
            for (int i = 0; i < ids.Length; i++) if (current[i] != ids[i]) return false;
            return true;
        }
        internal static bool Commit(EntityGraph graph, AuthorityRegistry authority, ulong root,
            ulong graphRevision, ulong[] ids, ulong[] epochs, ulong oldOwner, ulong newOwner, int maxEntities)
        {
            if (authority == null || !Matches(graph, root, graphRevision, ids, maxEntities)) return false;
            return authority.TransferGroup(ids, epochs, oldOwner, newOwner);
        }
    }
}
