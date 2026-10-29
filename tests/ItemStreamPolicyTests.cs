using System;
using TldMp;

internal static class ItemStreamPolicyTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception("FAIL: " + message); }

    // Native cleanup: distance to the nearest genAround, measured from the anchor.
    // Unparented items use their own position; parented items use their root.
    static bool RemovesItem(bool hasParent, double ownDistance, double rootDistance, double removeDistance)
    {
        double anchor = ItemStreamPolicy.UsesRootAnchor(hasParent) ? rootDistance : ownDistance;
        return ItemStreamPolicy.ShouldRemove(anchor, removeDistance);
    }

    static void ParentedFarChildNearRootIsKept()
    {
        // A stray child 900 m away under a car whose root is 40 m away must not drop the car.
        Check(!RemovesItem(true, 900, 40, 300), "parented far child with a near root must be kept");
    }

    static void ParentedFarRootIsStillRemoved()
    {
        // Root far away: the whole group is removed, as vanilla does.
        Check(RemovesItem(true, 5, 900, 300), "parented item with a far root must still be removed");
    }

    static void UnparentedUsesOwnDistance()
    {
        Check(RemovesItem(false, 900, 5, 300), "unparented far item must be removed on its own distance");
        Check(!RemovesItem(false, 40, 900, 300), "unparented near item must be kept regardless of any root value");
    }

    static void RemoveBoundaryIsStrict()
    {
        Check(!ItemStreamPolicy.ShouldRemove(300, 300), "distance equal to the remove range is kept (vanilla uses >)");
        Check(ItemStreamPolicy.ShouldRemove(300.0001, 300), "distance just beyond the remove range is removed");
    }

    public static int Main(string[] args)
    {
        ParentedFarChildNearRootIsKept();
        ParentedFarRootIsStillRemoved();
        UnparentedUsesOwnDistance();
        RemoveBoundaryIsStrict();
        Console.WriteLine("PASS: " + checks + " item-stream assertions; a parented item is measured from its root, unparented items keep their own distance.");
        return 0;
    }
}
