using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TldMp;

internal static class LiveUsableTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            string name = new AssemblyName(e.Name).Name;
            string path = name == "Assembly-CSharp" ? args[1] : Path.Combine(args[0], name + ".dll");
            return File.Exists(path) ? Assembly.Load(File.ReadAllBytes(path)) : null;
        };
        Assembly.Load(File.ReadAllBytes(args[1]));
        return Run();
    }

    // Native item usable: type 40, bool false, uint id, int usable, 3 floats, int turn, float.
    static byte[] ItemUsable(uint id, int usable, float rot, int turn)
    {
        var b = new byte[30];
        b[0] = 40; b[1] = 0;
        BitConverter.GetBytes(id).CopyTo(b, 2);
        BitConverter.GetBytes(usable).CopyTo(b, 6);
        BitConverter.GetBytes(rot).CopyTo(b, 10);
        BitConverter.GetBytes(turn).CopyTo(b, 22);
        return b;
    }

    static byte[] PoiUsable(int gen, double x, int usable, float rot, int turn)
    {
        var b = new byte[54];
        b[0] = 40; b[1] = 1;
        BitConverter.GetBytes(gen).CopyTo(b, 2);
        BitConverter.GetBytes(x).CopyTo(b, 6);
        BitConverter.GetBytes(usable).CopyTo(b, 30);
        BitConverter.GetBytes(rot).CopyTo(b, 34);
        BitConverter.GetBytes(turn).CopyTo(b, 46);
        return b;
    }

    static int Run()
    {
        ulong k1, k2, k3, kp; int turn;
        Check(LiveUsablePolicy.TryRead(ItemUsable(5, 0, 10f, 0), out k1, out turn) && turn == 0, "an item usable is read");
        LiveUsablePolicy.TryRead(ItemUsable(5, 0, 80f, 0), out k2, out turn);
        LiveUsablePolicy.TryRead(ItemUsable(5, 1, 10f, 0), out k3, out turn);
        Check(k1 == k2, "one usable keeps one key whatever its angle");
        Check(k1 != k3, "each usable of an item has its own key");
        Check(LiveUsablePolicy.TryRead(PoiUsable(2, 100.5, 3, 5f, 1), out kp, out turn) && turn == 1 && kp != k1, "a building usable is read with its own key");
        Check((k1 & 0x8000000000000000UL) != 0 && (k1 >> 32) != 40UL, "usable keys never collide with item stream keys");
        Check(!LiveUsablePolicy.TryRead(new byte[] { 40, 0, 1, 2 }, out k1, out turn), "a malformed usable is left to the game's reliable send");
        Check(!LiveUsablePolicy.TryRead(new byte[30], out k1, out turn), "other message types are not usables");

        var s = new LiveUsableSender();
        ulong key;
        Check(!s.Offer(1, ItemUsable(5, 0, 0f, 0), 0.00, out key), "the first state of a usable goes reliably");
        Check(s.Offer(1, ItemUsable(5, 0, 10f, 0), 0.02, out key), "dragging a door streams live");
        Check(s.Offer(1, ItemUsable(5, 0, 20f, 0), 0.04, out key), "every drag frame streams live");
        Check(!s.Offer(1, ItemUsable(5, 0, 20f, 1), 0.06, out key), "a turn-state change (switch, key) keeps the reliable send");
        Check(s.Offer(1, ItemUsable(5, 0, 30f, 1), 0.08, out key), "movement after the switch streams live again");
        var due = new List<KeyValuePair<ulong, byte[]>>();
        s.TakeSettles(0.2, due);
        Check(due.Count == 0, "no settle while the door is still moving");
        s.TakeSettles(0.08 + LiveUsablePolicy.QuietSeconds, due);
        Check(due.Count == 1 && due[0].Key == 1 && BitConverter.ToSingle(due[0].Value, 10) == 30f, "the resting state is sent once, reliably, after the quiet time");
        due.Clear();
        s.TakeSettles(5, due);
        Check(due.Count == 0, "the resting state is sent only once");
        Check(!s.Offer(2, ItemUsable(5, 0, 30f, 1), 0.1, out key), "each peer starts with a reliable state");
        s.Drop(1);
        Check(!s.Offer(1, ItemUsable(5, 0, 40f, 1), 0.2, out key), "a peer that left starts fresh");

        // Ordering: usables carry the sequence trailer like poses.
        byte[] stamped = PoseSequence.Stamp(ItemUsable(5, 0, 1f, 0), 1234);
        byte[] payload; uint seq; bool isStamped;
        Check(PoseSequence.TryUnstamp(stamped, out payload, out seq, out isStamped) && isStamped && seq == 1234 && payload.Length == 30, "usables round-trip the sequence trailer");
        ulong live; LiveUsablePolicy.TryRead(payload, out live, out turn);
        Check(PoseSequence.Key(payload) == live, "usable ordering is per usable");

        Console.WriteLine("PASS: " + checks + " live usable assertions; dragged doors and windows stream latest-value, switch changes stay reliable, and the resting state is sent reliably once.");
        return 0;
    }
}
