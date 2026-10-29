namespace TldMp
{
    internal enum NativeStoreSource : byte
    {
        Scan = 0,
        Network = 1,
        Other = 2
    }

    // Native mountStuff.FreezeUpd scans car/storage volumes on every peer, but
    // mountStuff.Store only broadcasts the result from the host. A guest that
    // auto-stores an item it does not control diverges from the host without
    // telling it. Guests leave the decision to the host, whose store arrives
    // through the native itemPosLocal path (syncScript.ItemPosLocal), which is
    // never gated. Load, spawn-in-box and network stores do not pass through here.
    internal static class NativeStoreGate
    {
        internal static bool AllowLocalAutoStore(bool connected, bool isHost, bool thisOneInCharge, bool fromNetwork)
        {
            return !connected || isHost || thisOneInCharge || fromNetwork;
        }

        // Diagnostics only. A network store is preceded in the same frame by
        // GotSyncLocal for the same parent; anything else that is not the
        // gated scan is a load, spawn-in-box or other native caller.
        internal static NativeStoreSource Classify(bool scanning, bool syncedLocalThisFrame)
        {
            if (scanning) return NativeStoreSource.Scan;
            if (syncedLocalThisFrame) return NativeStoreSource.Network;
            return NativeStoreSource.Other;
        }

        internal static string Name(NativeStoreSource source)
        {
            switch (source)
            {
                case NativeStoreSource.Scan: return "scan";
                case NativeStoreSource.Network: return "net";
                default: return "other";
            }
        }
    }
}
