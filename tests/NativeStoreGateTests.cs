using System;
using TldMp;

internal static class NativeStoreGateTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    public static int Main(string[] args)
    {
        // Single player and the host keep the stock scan.
        Check(NativeStoreGate.AllowLocalAutoStore(false, false, false, false), "single-player scan must store");
        Check(NativeStoreGate.AllowLocalAutoStore(true, true, false, false), "host scan must store a loose item");
        Check(NativeStoreGate.AllowLocalAutoStore(true, true, true, false), "host scan must store an item it controls");
        // A guest must not silently store a host-owned loose item (box gibs).
        Check(!NativeStoreGate.AllowLocalAutoStore(true, false, false, false), "guest scan of a host-owned loose item must be skipped");
        // A guest may still store what it controls, and the host's decision
        // arriving over the network is never gated.
        Check(NativeStoreGate.AllowLocalAutoStore(true, false, true, false), "guest scan of an item it controls must store");
        Check(NativeStoreGate.AllowLocalAutoStore(true, false, false, true), "network store from the host must apply on a guest");
        Check(NativeStoreGate.AllowLocalAutoStore(true, true, false, true), "network/load store must apply on the host");

        // Exhaustive: the only denied combination is a connected guest scan of
        // an item it does not control.
        int denied = 0;
        for (int mask = 0; mask < 16; mask++)
        {
            bool connected = (mask & 1) != 0, host = (mask & 2) != 0, inCharge = (mask & 4) != 0, net = (mask & 8) != 0;
            bool allowed = NativeStoreGate.AllowLocalAutoStore(connected, host, inCharge, net);
            bool expected = !(connected && !host && !inCharge && !net);
            Check(allowed == expected, "unexpected gate result for mask " + mask);
            if (!allowed) denied++;
        }
        Check(denied == 1, "exactly one input combination may be denied; found " + denied);

        // Diagnostic source classification.
        Check(NativeStoreGate.Classify(true, false) == NativeStoreSource.Scan, "an in-scan store is a scan");
        Check(NativeStoreGate.Classify(true, true) == NativeStoreSource.Scan, "the scan flag wins over a same-frame sync");
        Check(NativeStoreGate.Classify(false, true) == NativeStoreSource.Network, "a same-frame local sync is a network store");
        Check(NativeStoreGate.Classify(false, false) == NativeStoreSource.Other, "load/spawn stores are other");
        Check(NativeStoreGate.Name(NativeStoreSource.Scan) == "scan" && NativeStoreGate.Name(NativeStoreSource.Network) == "net" &&
            NativeStoreGate.Name(NativeStoreSource.Other) == "other", "source names changed");

        Console.WriteLine("PASS: " + checks + " auto-store gate assertions; only a guest scan of an item it does not control is skipped.");
        return 0;
    }
}
