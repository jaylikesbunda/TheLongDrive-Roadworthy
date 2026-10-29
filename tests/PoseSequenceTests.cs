using System;
using System.IO;
using System.Reflection;
using TldMp;

internal static class PoseSequenceTests
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

    static int Run()
    {
        Trailer();
        Counter();
        Filter();
        StaleCarScenario();
        ReliableAlwaysApplies();
        BurstKeepsSendSpacing();
        Console.WriteLine("PASS: " + checks + " pose sequence assertions; position/velocity messages round-trip byte-exact, other messages are untouched, and an older pose can never be applied after a newer one.");
        return 0;
    }

    static byte[] CarPose(uint id, byte marker)
    {
        var b = new byte[49];
        b[0] = 39;
        BitConverter.GetBytes(id).CopyTo(b, 1);
        for (int i = 5; i < b.Length; i++) b[i] = (byte)(marker + i);
        return b;
    }

    static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static void Trailer()
    {
        byte[] pose = CarPose(77, 3);
        byte[] stamped = PoseSequence.Stamp(pose, 0xA1B2C3D4u);
        Check(stamped.Length == pose.Length + PoseSequence.TrailerBytes, "a stamped pose carries a 4-byte trailer");
        byte[] payload; uint seq; bool isStamped;
        Check(PoseSequence.TryUnstamp(stamped, out payload, out seq, out isStamped) && isStamped, "a stamped pose unstamps");
        Check(seq == 0xA1B2C3D4u && Same(payload, pose), "the game receives the exact original bytes and the sequence survives");

        byte[] chat = { 3, 1, 2, 3 };
        Check(ReferenceEquals(PoseSequence.Stamp(chat, 5), chat), "other message types are never touched");
        Check(PoseSequence.TryUnstamp(chat, out payload, out seq, out isStamped) && !isStamped && ReferenceEquals(payload, chat), "other message types pass through unchanged");
        Check(!PoseSequence.TryUnstamp(new byte[] { 39, 1, 2 }, out payload, out seq, out isStamped), "a stamped type too short for its trailer is rejected");
        foreach (byte t in new byte[] { 10, 16, 17, 19, 20, 39 }) Check(PoseSequence.Stamped(t), "type " + t + " is ordered");
        foreach (byte t in new byte[] { 1, 8, 11, 36, 37, 43, 46, 254 }) Check(!PoseSequence.Stamped(t), "type " + t + " is not ordered");

        Check(PoseSequence.Key(CarPose(77, 0)) != PoseSequence.Key(CarPose(78, 0)), "each car is ordered separately");
        var vel = CarPose(77, 0); vel[0] = 20;
        Check(PoseSequence.Key(vel) != PoseSequence.Key(CarPose(77, 0)), "pose and velocity of one car are ordered separately");
        var p1 = new byte[] { 10, 1, 0, 9, 9, 9, 9 }; var p2 = new byte[] { 10, 0, 0, 8, 8, 8, 8 };
        Check(PoseSequence.Key(p1) == PoseSequence.Key(p2), "a sender's player positions share one order");
    }

    static void Counter()
    {
        var c = new PoseSequenceCounter();
        uint a = c.Next(1000), b = c.Next(1000), d = c.Next(999), e = c.Next(5000);
        Check(a == 1000 && b == 1001 && d == 1002 && e == 5000, "the counter is strictly increasing and follows the clock");
        var restarted = new PoseSequenceCounter();
        Check(PoseSequence.Newer(restarted.Next(6000), e), "a restarted game starts above its previous session");
        Check(PoseSequence.Newer(5u, uint.MaxValue - 5), "ordering survives the 32-bit wrap");
        Check(!PoseSequence.Newer(10, 10) && !PoseSequence.Newer(9, 10), "equal or older is not newer");
    }

    static void Filter()
    {
        var f = new PoseSequenceFilter();
        Check(f.Accept(1, 5, 100) && f.Accept(1, 5, 101), "in-order poses apply");
        Check(!f.Accept(1, 5, 100) && !f.Accept(1, 5, 101), "older or repeated poses are dropped");
        Check(f.Accept(1, 6, 50), "another object has its own order");
        Check(f.Accept(2, 5, 1), "another sender has its own order");
        Check(f.Stale == 2 && f.Passed == 4, "drops and passes are counted");
        f.Forget(1);
        Check(f.Accept(1, 5, 7), "a sender that left and rejoined starts fresh");
    }

    // A part's reliable attach (sent after an item-data request) can arrive after
    // newer unreliable motion for the same part; it must still be applied.
    static void ReliableAlwaysApplies()
    {
        var f = new PoseSequenceFilter();
        Check(f.Accept(1, 17, 500) && f.Accept(1, 17, 501), "unreliable motion applies in order");
        Check(f.Accept(1, 17, PoseSequence.Unordered), "a late reliable message is always applied");
        Check(f.Accept(1, 17, 502) && !f.Accept(1, 17, 499), "a reliable message does not disturb the unreliable order");
        var c = new PoseSequenceCounter();
        Check(c.Next(0) != PoseSequence.Unordered && c.Next(uint.MaxValue) != PoseSequence.Unordered, "the counter never issues the reliable marker");
    }

    // A car sends a pose every 16 ms. The passenger hitches for 0.5 s and then
    // receives 31 queued poses in one frame. Timed by send clock, rendering a
    // quarter second back never lands on a pose seconds old, and render time
    // never runs backwards.
    static void BurstKeepsSendSpacing()
    {
        var clock = new SenderClock();
        var buffer = new RemotePoseBuffer();
        double late;
        double t0 = clock.Map(7, 100000, 50.0, out late);
        Check(t0 == 50.0 && late == 0, "the first pose anchors the sender clock");
        for (int i = 1; i <= 60; i++)
        {
            double t = clock.Map(7, (uint)(100000 + i * 16), 50.0 + i * 0.016, out late);
            buffer.Add(t, At(i * 0.4), late);
        }
        double arrival = 50.0 + 91 * 0.016 + 0.5;
        double first = 0, last = 0;
        for (int i = 61; i <= 91; i++)
        {
            double t = clock.Map(7, (uint)(100000 + i * 16), arrival, out late);
            if (i == 61) first = t;
            last = t;
            buffer.Add(t, At(i * 0.4), late);
        }
        Check(Math.Abs((last - first) - 30 * 0.016) < 1e-6, "a burst keeps the real send spacing");
        Check(late > 0.4, "the burst is measured as late");
        RemotePose p;
        double render = arrival - buffer.Delay;
        Check(buffer.Sample(render, out p), "the buffer samples after the burst");
        double newest = 91 * 0.4;
        Check(newest - p.X < 25 * (buffer.Delay + 0.6), "the rendered pose is never seconds behind the newest (no jump back)");
        RemotePose before;
        buffer.Sample(render - 0.016, out before);
        Check(p.X >= before.X, "render time moving forward never moves the car backwards");

        var other = new SenderClock();
        other.Map(1, uint.MaxValue - 10, 10.0, out late);
        double wrapped = other.Map(1, 5, 10.016, out late);
        Check(Math.Abs(wrapped - 10.016) < 1e-6, "the send clock survives the 32-bit wrap");
    }

    static RemotePose At(double x)
    {
        var p = new RemotePose();
        p.X = x; p.Qw = 1;
        return p;
    }

    // The passenger case from the logs: fresh poses at 60 Hz, then a pose from
    // seconds earlier arrives late. It must never be applied.
    static void StaleCarScenario()
    {
        var sender = new PoseSequenceCounter();
        var receiver = new PoseSequenceFilter();
        byte[] stale = null;
        int applied = 0;
        for (int i = 0; i < 400; i++)
        {
            byte[] wire = PoseSequence.Stamp(CarPose(9, (byte)i), sender.Next((uint)(1000 + i * 16)));
            if (i == 100) { stale = wire; continue; }
            byte[] payload; uint seq; bool stamped;
            PoseSequence.TryUnstamp(wire, out payload, out seq, out stamped);
            if (receiver.Accept(42, PoseSequence.Key(payload), seq)) applied++;
            if (i == 300)
            {
                PoseSequence.TryUnstamp(stale, out payload, out seq, out stamped);
                Check(!receiver.Accept(42, PoseSequence.Key(payload), seq), "a pose from 3 s earlier arriving late is dropped");
            }
        }
        Check(applied == 399, "every fresh pose applied");
    }
}
