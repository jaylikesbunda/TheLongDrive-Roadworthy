using System.Collections.Generic;

namespace TldMp.Experimental
{
    // A registry of logical relationships, independent of streamed Unity objects.
    // All mutations are made by the host's session thread.
    internal sealed class EntityGraph
    {
        sealed class Node
        {
            internal ulong Parent;
            internal bool Removed;
        }
        readonly Dictionary<ulong, Node> nodes = new Dictionary<ulong, Node>();
        internal ulong Revision { get; private set; }

        internal bool Add(ulong id)
        {
            if (id == 0 || nodes.ContainsKey(id) || Revision == ulong.MaxValue) return false;
            nodes.Add(id, new Node());
            Revision++;
            return true;
        }
        internal bool Attach(ulong child, ulong parent)
        {
            Node c, p;
            if (Revision == ulong.MaxValue || child == parent || !nodes.TryGetValue(child, out c) || c.Removed ||
                !nodes.TryGetValue(parent, out p) || p.Removed) return false;
            // Reject cycles before changing anything. Existing links always point
            // at live nodes, and the traversal terminates because cycles are refused.
            ulong cursor = parent;
            while (cursor != 0)
            {
                if (cursor == child) return false;
                cursor = nodes[cursor].Parent;
            }
            if (c.Parent == parent) return true;
            c.Parent = parent;
            Revision++;
            return true;
        }
        internal bool Detach(ulong child)
        {
            Node c;
            if (Revision == ulong.MaxValue || !nodes.TryGetValue(child, out c) || c.Removed || c.Parent == 0) return false;
            c.Parent = 0;
            Revision++;
            return true;
        }
        internal bool RemoveLeaf(ulong id)
        {
            Node node;
            if (Revision == ulong.MaxValue || !nodes.TryGetValue(id, out node) || node.Removed) return false;
            foreach (var pair in nodes)
                if (!pair.Value.Removed && pair.Value.Parent == id) return false;
            node.Parent = 0;
            node.Removed = true;
            Revision++;
            return true;
        }
        internal ulong ParentOf(ulong id)
        {
            Node node;
            return nodes.TryGetValue(id, out node) && !node.Removed ? node.Parent : 0;
        }
        internal int Count { get { return nodes.Count; } }
        internal bool Contains(ulong id) { Node node; return nodes.TryGetValue(id, out node) && !node.Removed; }
        internal bool IsRemoved(ulong id) { Node node; return nodes.TryGetValue(id, out node) && node.Removed; }
        internal bool HasChildren(ulong id)
        {
            foreach (var pair in nodes)
                if (!pair.Value.Removed && pair.Value.Parent == id) return true;
            return false;
        }
        internal ulong[] CopyIds()
        {
            var ids = new ulong[nodes.Count];
            nodes.Keys.CopyTo(ids, 0);
            return ids;
        }

        // Snapshot the full descendant group; fail without a partial result if
        // the caller's size limit is exceeded. The sort makes snapshots stable.
        internal bool TryGroup(ulong root, int limit, out ulong[] result)
        {
            result = null;
            Node node;
            if (limit < 1 || !nodes.TryGetValue(root, out node) || node.Removed) return false;
            var children = new Dictionary<ulong, List<ulong>>();
            foreach (var pair in nodes)
            {
                if (pair.Value.Removed || pair.Value.Parent == 0) continue;
                List<ulong> siblings;
                if (!children.TryGetValue(pair.Value.Parent, out siblings))
                {
                    siblings = new List<ulong>(); children.Add(pair.Value.Parent, siblings);
                }
                siblings.Add(pair.Key);
            }
            var group = new List<ulong>();
            group.Add(root);
            for (int index = 0; index < group.Count; index++)
            {
                List<ulong> siblings;
                if (!children.TryGetValue(group[index], out siblings)) continue;
                foreach (ulong child in siblings)
                {
                    if (group.Count >= limit) return false;
                    group.Add(child);
                }
            }
            group.Sort();
            result = group.ToArray();
            return true;
        }
    }
}
