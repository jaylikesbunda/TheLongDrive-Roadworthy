namespace TldMp
{
    // Item streaming distance. The vanilla item cleanup measures each item from
    // its own transform. A parented item (a car part, a stored item) is measured
    // from its transform root instead, so one child with a stray position cannot
    // drop or freeze every item under the same root. Unparented items keep their
    // own position, which is also their root.
    internal static class ItemStreamPolicy
    {
        internal static bool UsesRootAnchor(bool hasParent)
        {
            return hasParent;
        }

        internal static bool ShouldRemove(double anchorDistance, double removeDistance)
        {
            return anchorDistance > removeDistance;
        }
    }
}
