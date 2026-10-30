namespace TldMp
{
    // A host only removes an item from the asker when the item is genuinely
    // absent. An item that exists but is held, stored, worn or equipped by
    // another player must be preserved on the asking peer.
    internal static class ItemAskSafety
    {
        internal static bool MayRemove(bool itemExists)
        {
            return MayRemove(itemExists,false,false);
        }

        // Absence from the host's live table is not a deletion tombstone. A
        // saved but streamed-out item, or a newly allocated client item whose
        // creation has not arrived, must not be erased by an ask-data reply.
        internal static bool MayRemove(bool itemExists,bool savedRecordExists,bool clientReservedID)
        {
            return !itemExists && !savedRecordExists && !clientReservedID;
        }
    }
}
