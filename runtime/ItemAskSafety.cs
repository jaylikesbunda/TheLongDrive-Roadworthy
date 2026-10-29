namespace TldMp
{
    // A host only removes an item from the asker when the item is genuinely
    // absent. An item that exists but is held, stored, worn or equipped by
    // another player must be preserved on the asking peer.
    internal static class ItemAskSafety
    {
        internal static bool MayRemove(bool itemExists)
        {
            return !itemExists;
        }
    }
}
