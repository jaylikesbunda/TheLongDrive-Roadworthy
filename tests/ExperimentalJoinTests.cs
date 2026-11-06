using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using TldMp;
using TldMp.Experimental;

// Experimental world-join transfer (TLD_MP_EXPERIMENTAL_JOIN=1), exercised without
// Unity or Steam. The production StableNode and ExperimentalJoin/ExperimentalJoinReceiver
// code runs here; only the transport, the world capture/apply and the bridge's ~30-line
// adaptation are stand-ins (the bridge itself cannot compile without the game).
//
// Covered: the Exp Begin/Part/End frame layout inside the StableMessage envelope,
// bounded reassembly, hash mismatch, out-of-order/duplicate/oversized/zero-length
// frames, the retry with a new transfer id, native journal ordering across the
// barrier (events produced after BeginExternalSync are replayed after the guest's
// Ack), and the gate that keeps the untouched native Begin/Part/End path.
public static class ExperimentalJoinTests
{
    static int checks;
    static void Check(bool ok, string what) { checks++; if (!ok) throw new Exception("FAIL: " + what); }

    sealed class Packet { public ulong From, To; public byte[] Bytes; }

    // Minimal two-node loopback with a fake send sink. Mirrors StableGameBridge:
    // host SnapshotRequested -> experimental or native transfer, and the guest consumes
    // Exp frames before node.Receive exactly as ReadLoopback does.
    sealed class Lab
    {
        public StableNode Host, Client;
        public double Now;
        public bool Experimental = true, UseNative;
        public int CorruptPart = -1;
        public bool OversizePart, BadBegin, SpoofOrigin;
        public byte[] World;
        public int Applies, Serves;
        public byte[] AppliedBytes;
        public readonly List<Packet> Queue = new List<Packet>();
        public readonly List<byte[]> Native = new List<byte[]>();
        public readonly List<StableOp> Delivered = new List<StableOp>();
        public readonly List<string> Events = new List<string>();
        public readonly ExperimentalJoinReceiver Receiver = new ExperimentalJoinReceiver();

        public Lab(byte[] world)
        {
            World = world;
            Host = new StableNode(10, 10, 999, 101, "join", 0);
            Host.Send = delegate(ulong to, byte[] b, bool r) { Queue.Add(new Packet { From = 10, To = to, Bytes = b }); };
            Host.SnapshotRequested = delegate(ulong peer) { Serves++; SendWorld(peer); };
            Client = new StableNode(20, 10, 999, 202, "join", 0);
            Client.Send = delegate(ulong to, byte[] b, bool r) { Queue.Add(new Packet { From = 20, To = to, Bytes = b }); };
            Client.ApplySnapshot = delegate(byte[] b) { Applies++; AppliedBytes = b; Events.Add("apply"); };
            Client.Tick(0); Pump();
            Check(Client.Compatible, "client handshake completed before the transfer");
        }

        // Host side of StableGameBridge.ExperimentalSendWorld / native SendWorld.
        void SendWorld(ulong peer)
        {
            if (UseNative) { Host.BeginSnapshot(peer, World); return; }
            ulong transfer = Host.BeginExternalSync(peer);
            if (transfer == 0) return;
            byte[] hash; using (var sha = SHA256.Create()) hash = sha.ComputeHash(World);
            int part = 0;
            ExperimentalJoin.EmitSnapshot(World, hash, delegate(StableOp op, byte[] data)
            {
                if (op == StableOp.ExpSnapshotPart)
                {
                    if (part == CorruptPart) data[4] ^= 0xFF;
                    if (OversizePart) { var bigger = new byte[data.Length + 1]; Buffer.BlockCopy(data, 0, bigger, 0, data.Length); data = bigger; }
                    part++;
                }
                if (op == StableOp.ExpSnapshotBegin && BadBegin) data = new byte[ExperimentalJoin.BeginDataBytes];
                var m = new StableMessage { Op = op, Lobby = Host.Lobby, Session = Host.Session, Origin = SpoofOrigin ? 21UL : Host.Own, Epoch = transfer, Data = data };
                Queue.Add(new Packet { From = 10, To = peer, Bytes = m.Encode() });
            });
        }

        public void Pump() { PumpWhere(delegate(Packet p) { return true; }); }
        public void PumpHost() { PumpWhere(delegate(Packet p) { return p.To == 10; }); }

        public void PumpWhere(Func<Packet, bool> filter)
        {
            int guard = 0;
            for (int i = 0; i < Queue.Count;)
            {
                if (++guard > 20000) throw new Exception("reply loop");
                var p = Queue[i];
                if (!filter(p)) { i++; continue; }
                Queue.RemoveAt(i);
                if (p.Bytes == null || p.Bytes.Length == 0) continue;
                if (p.Bytes[0] != 254) { Native.Add(p.Bytes); Events.Add("native:" + BitConverter.ToInt32(p.Bytes, 1)); continue; }
                var m = StableMessage.Decode(p.Bytes);
                if (m != null) Delivered.Add(m.Op);
                if (p.To == 20 && m != null && ExperimentalJoin.IsSnapshotOp(m.Op))
                {
                    if (Experimental && Receive(m)) continue;
                    Client.Receive(p.From, p.Bytes, Now); continue;
                }
                if (p.To == 10) Host.Receive(p.From, p.Bytes, Now); else Client.Receive(p.From, p.Bytes, Now);
            }
        }

        // Mirror of StableGameBridge.ReceiveExperimentalJoin (guest only).
        bool Receive(StableMessage m)
        {
            if (!Experimental) return false;
            if (!Client.Compatible || Client.HostSession == 0 || m.Origin != Client.Host || m.Session != Client.HostSession || m.Epoch == 0) return true;
            ulong transfer = m.Epoch;
            if (m.Op == StableOp.ExpSnapshotBegin)
            {
                if (!Receiver.Begin(transfer, m.Data)) { Client.AbandonExternalReceive(); return true; }
                Client.BeginExternalReceive(transfer); return true;
            }
            if (m.Op == StableOp.ExpSnapshotPart)
            {
                if (!Receiver.Part(transfer, m.Data)) { Client.AbandonExternalReceive(); return true; }
                Client.NoteExternalProgress(); return true;
            }
            if (m.Data.Length != 0) { Receiver.Cancel(); Client.AbandonExternalReceive(); return true; }
            byte[] snapshot;
            if (!Receiver.End(transfer, out snapshot) || snapshot == null) { Client.AbandonExternalReceive(); return true; }
            Client.ApplySnapshot(snapshot);
            Client.AwaitExternalLive(transfer);
            Client.SendExternalAck(transfer);
            return true;
        }
    }

    static byte[] World(int size, int seed) { var w = new byte[size]; new Random(seed).NextBytes(w); return w; }
    static byte[] Hash(byte[] bytes) { using (var sha = SHA256.Create()) return sha.ComputeHash(bytes); }
    static byte[] Control(int value) { var b = new byte[5]; b[0] = 37; Buffer.BlockCopy(BitConverter.GetBytes(value), 0, b, 1, 4); return b; }

    static void WireFormat()
    {
        Check((byte)StableOp.ExpSnapshotBegin == 26 && (byte)StableOp.ExpSnapshotPart == 27 && (byte)StableOp.ExpSnapshotEnd == 28, "opcodes are 26/27/28");
        var hash = Hash(new byte[] { 1, 2, 3 });
        var begin = ExperimentalJoin.BeginData(3, hash);
        Check(begin.Length == ExperimentalJoin.BeginDataBytes && BitConverter.ToInt32(begin, 0) == 3, "Begin payload is int32 total + 32-byte hash");
        int total; byte[] read;
        Check(ExperimentalJoin.ReadBegin(begin, out total, out read) && total == 3 && read.Length == 32 && read[0] == hash[0] && read[31] == hash[31], "Begin roundtrip");
        Check(!ExperimentalJoin.ReadBegin(new byte[35], out total, out read), "short Begin payload rejected");
        Check(!ExperimentalJoin.ReadBegin(new byte[37], out total, out read), "oversized Begin payload rejected");
        Check(!ExperimentalJoin.ReadBegin(ExperimentalJoin.BeginData(0, hash), out total, out read), "zero total rejected before allocation");
        Check(!ExperimentalJoin.ReadBegin(ExperimentalJoin.BeginData(-1, hash), out total, out read), "negative total rejected before allocation");
        Check(!ExperimentalJoin.ReadBegin(ExperimentalJoin.BeginData(ExperimentalJoin.MaxSnapshotBytes + 1, hash), out total, out read), "total over the 32 MB bound rejected before allocation");
        Check(ExperimentalJoin.MaxSnapshotBytes == 32 * 1024 * 1024, "the experimental transfer bound is 32 MB");
        Check(ExperimentalJoin.PartCount(1) == 1 && ExperimentalJoin.PartCount(48000) == 1 && ExperimentalJoin.PartCount(48001) == 2, "part count boundary");
        Check(ExperimentalJoin.PartCount(ExperimentalJoin.MaxSnapshotBytes) == 700 && ExperimentalJoin.PartCount(0) == 0 && ExperimentalJoin.PartCount(-1) == 0, "part count is bounded and finite");

        var source = new byte[3];
        var part = ExperimentalJoin.PartData(source, 0);
        int offset, count;
        Check(part.Length == 7 && BitConverter.ToInt32(part, 0) == 0, "Part payload is int32 offset + chunk");
        Check(ExperimentalJoin.TryReadPart(part, 3, out offset, out count) && offset == 0 && count == 3, "Part roundtrip");
        Check(!ExperimentalJoin.TryReadPart(part, 4, out offset, out count), "Part length must match the declared total");
        Check(!ExperimentalJoin.TryReadPart(new byte[] { 0, 0, 0, 0 }, 3, out offset, out count), "Part without a chunk is rejected");
        var gap = new byte[4];
        Buffer.BlockCopy(BitConverter.GetBytes(48000), 0, gap, 0, 4);
        Check(!ExperimentalJoin.TryReadPart(gap, 100000, out offset, out count), "Part offset must be inside the total");
        var misaligned = new byte[4 + 48000];
        Buffer.BlockCopy(BitConverter.GetBytes(48001), 0, misaligned, 0, 4);
        Check(!ExperimentalJoin.TryReadPart(misaligned, 100000, out offset, out count), "Part offset must be chunk aligned");
        var huge = new byte[4 + 48000];
        Buffer.BlockCopy(BitConverter.GetBytes(0), 0, huge, 0, 4);
        Check(!ExperimentalJoin.TryReadPart(huge, 40000, out offset, out count), "Part longer than the remaining total is rejected");
        Check(!ExperimentalJoin.TryReadPart(ExperimentalJoin.PartData(new byte[100000], 48000), 50000, out offset, out count), "Part past the declared total is rejected");

        var frame = new StableMessage { Op = StableOp.ExpSnapshotPart, Lobby = 999, Session = 101, Origin = 10, Epoch = 7, Sequence = 0, Data = part };
        var encoded = frame.Encode();
        var decoded = StableMessage.Decode(encoded);
        Check(decoded != null && decoded.Op == StableOp.ExpSnapshotPart && decoded.Epoch == 7 && decoded.Origin == 10 && decoded.Data.Length == part.Length, "Exp frame roundtrips through the StableMessage envelope");
        var extra = new byte[encoded.Length + 1]; Buffer.BlockCopy(encoded, 0, extra, 0, encoded.Length);
        Check(StableMessage.Decode(extra) == null, "a trailing byte is still rejected");
        var stale = new StableMessage { Op = (StableOp)29, Lobby = 999, Session = 101, Origin = 10 };
        Check(StableMessage.Decode(stale.Encode()) == null, "an opcode above the experimental range is rejected");
        Check(ExperimentalJoin.IsSnapshotOp(StableOp.ExpSnapshotBegin) && ExperimentalJoin.IsSnapshotOp(StableOp.ExpSnapshotPart) && ExperimentalJoin.IsSnapshotOp(StableOp.ExpSnapshotEnd), "the three ops are the experimental snapshot ops");
        Check(!ExperimentalJoin.IsSnapshotOp(StableOp.Begin) && !ExperimentalJoin.IsSnapshotOp(StableOp.Part) && !ExperimentalJoin.IsSnapshotOp(StableOp.End), "native Begin/Part/End are never experimental frames");
        Check(!ExperimentalJoin.EmitSnapshot(null, hash, delegate(StableOp op, byte[] data) { }) && !ExperimentalJoin.EmitSnapshot(new byte[0], hash, delegate(StableOp op, byte[] data) { }), "an empty snapshot emits nothing");
        Check(!ExperimentalJoin.EmitSnapshot(new byte[4], null, delegate(StableOp op, byte[] data) { }), "a missing hash emits nothing");
        Console.WriteLine("PASS: Exp Begin/Part/End framing, exact-length validation and the 32 MB bound.");
    }

    static void ExperimentalJoinCompletes()
    {
        var world = World(100000, 3);
        var lab = new Lab(world);
        lab.Client.RequestSnapshot();
        lab.Pump();
        Check(lab.Applies == 1, "the guest applied the experimental snapshot exactly once");
        Check(lab.AppliedBytes != null && lab.AppliedBytes.Length == world.Length && lab.AppliedBytes[0] == world[0] && lab.AppliedBytes[world.Length - 1] == world[world.Length - 1], "the applied bytes are the captured world frame");
        Check(lab.Client.Ready && lab.Client.Error == null, "guest reaches Ready through the experimental transfer: " + (lab.Client.Error ?? lab.Client.JoinStatus));
        Check(lab.Host.Peers[20].Ready && !lab.Host.Peers[20].Syncing, "the host peer is ready and its journal barrier is closed");
        Check(lab.Host.Peers[20].Journal.Count == 0 && lab.Host.Peers[20].JournalBytes == 0, "the Ack drained the journal");
        Check(lab.Delivered.Contains(StableOp.ExpSnapshotBegin) && lab.Delivered.Contains(StableOp.ExpSnapshotPart) && lab.Delivered.Contains(StableOp.ExpSnapshotEnd), "the guest received the three experimental frames");
        Check(!lab.Delivered.Contains(StableOp.Begin) && !lab.Delivered.Contains(StableOp.Part) && !lab.Delivered.Contains(StableOp.End), "no native Begin/Part/End frame was sent");
        Check(lab.Receiver.Watermark == lab.Host.Peers[20].Transfer, "the receiver tracks the host transfer id");
        Console.WriteLine("PASS: end-to-end experimental join applies the world once and reaches Ready.");
    }

    static void JournalOrderingAcrossTheBarrier()
    {
        var lab = new Lab(World(60000, 7));
        lab.Client.RequestSnapshot();
        lab.PumpHost();
        Check(lab.Host.Peers[20].Syncing && !lab.Host.Peers[20].Ready, "the host opened the journal barrier before the transfer");
        for (int i = 1; i <= 25; i++) Check(lab.Host.PublishNative(Control(i)), "host publishes reliable native state during the transfer");
        Check(lab.Native.Count == 0, "nothing reached the guest while the barrier was up");
        Check(lab.Host.Peers[20].Journal.Count == 25, "every native message produced after the barrier is journaled: " + lab.Host.Peers[20].Journal.Count);
        lab.Pump();
        Check(lab.Client.Ready, "guest reaches Ready after the Ack/Live handshake");
        Check(lab.Events.Count == 26 && lab.Events[0] == "apply", "the snapshot is applied before any journaled native bytes: " + string.Join(",", lab.Events.ToArray()));
        for (int i = 0; i < 25; i++)
            Check(lab.Events[i + 1] == "native:" + (i + 1), "journaled native bytes replay in production order at " + i + ": " + lab.Events[i + 1]);
        Console.WriteLine("PASS: native events produced after the barrier replay in order after the guest's Ack.");
    }

    static void HashMismatchRetriesWithANewTransferId()
    {
        var lab = new Lab(World(50000, 4));
        lab.CorruptPart = 0;
        lab.Client.RequestSnapshot();
        lab.Pump();
        Check(lab.Applies == 0, "a corrupted snapshot is never applied");
        Check(!lab.Receiver.Active, "the failed assembly was cancelled");
        Check(lab.Client.Error == null, "a corrupted snapshot is consumed, not a fatal join error");
        Check(lab.Client.JoinStatus == "Requested" && !lab.Client.Joining, "the guest drops back to the request stage so it retries: " + lab.Client.JoinStatus);
        ulong first = lab.Host.Peers[20].Transfer;
        lab.CorruptPart = -1;
        lab.Now = 3; lab.Client.Tick(lab.Now);
        lab.Client.RequestSnapshot();
        lab.Pump();
        Check(lab.Applies == 1 && lab.Client.Ready, "the retry with a new transfer id completes: " + (lab.Client.Error ?? lab.Client.JoinStatus));
        Check(lab.Host.Peers[20].Transfer > first, "the retry uses a strictly newer transfer id (" + first + " -> " + lab.Host.Peers[20].Transfer + ")");
        Check(lab.Receiver.Watermark == lab.Host.Peers[20].Transfer, "the receiver watermark follows the newest served transfer");
        Console.WriteLine("PASS: hash mismatch is consumed, the guest retries and completes on a new transfer id.");
    }

    static void PartAssemblyBounds()
    {
        var world = World(150000, 5);
        var hash = Hash(world);
        var receiver = new ExperimentalJoinReceiver();
        Check(receiver.Begin(7, ExperimentalJoin.BeginData(world.Length, hash)), "Begin accepted");
        Check(!receiver.Begin(7, ExperimentalJoin.BeginData(world.Length, hash)), "a duplicate Begin for the same transfer id is ignored");
        Check(receiver.Part(7, ExperimentalJoin.PartData(world, 0)), "first part accepted");
        Check(receiver.Active, "the assembly is still open after the first part");
        Check(!receiver.Part(7, ExperimentalJoin.PartData(world, 96000)), "a gap is refused and closes the assembly");
        Check(!receiver.Active, "the closed assembly accepts nothing");

        receiver = new ExperimentalJoinReceiver();
        Check(receiver.Begin(7, ExperimentalJoin.BeginData(world.Length, hash)), "another session accepts transfer id 7");
        Check(receiver.Part(7, ExperimentalJoin.PartData(world, 0)), "in-order part accepted before the restart");
        Check(receiver.Begin(12, ExperimentalJoin.BeginData(world.Length, hash)), "a newer Begin supersedes the transfer in flight");
        Check(!receiver.Part(7, ExperimentalJoin.PartData(world, 48000)), "the superseded transfer can no longer append");
        Check(receiver.Part(12, ExperimentalJoin.PartData(world, 0)), "the superseding transfer appends from the start");

        receiver = new ExperimentalJoinReceiver();
        Check(receiver.Begin(8, ExperimentalJoin.BeginData(world.Length, hash)), "second attempt Begin accepted");
        Check(receiver.Part(8, ExperimentalJoin.PartData(world, 0)), "part 0 accepted");
        Check(receiver.Part(8, ExperimentalJoin.PartData(world, 0)), "a byte-identical duplicate is accepted");
        var wrong = ExperimentalJoin.PartData(world, 0); wrong[4] ^= 0xFF;
        Check(!receiver.Part(8, wrong), "a conflicting duplicate is refused");
        Check(!receiver.Active, "a conflicting duplicate closes the assembly");

        receiver = new ExperimentalJoinReceiver();
        Check(receiver.Begin(9, ExperimentalJoin.BeginData(world.Length, hash)), "third attempt Begin accepted");
        Check(receiver.Part(9, ExperimentalJoin.PartData(world, 0)) && receiver.Part(9, ExperimentalJoin.PartData(world, 48000)), "in-order parts accepted");
        var oversized = ExperimentalJoin.PartData(world, 48000);
        var bigger = new byte[oversized.Length + 1]; Buffer.BlockCopy(oversized, 0, bigger, 0, oversized.Length);
        Check(!receiver.Part(9, bigger), "an oversized part payload is refused");
        Check(!receiver.Part(10, ExperimentalJoin.PartData(world, 0)), "a part for another transfer is refused");

        receiver = new ExperimentalJoinReceiver();
        Check(!receiver.Begin(11, new byte[ExperimentalJoin.BeginDataBytes]), "a zero-length snapshot is refused before allocation");
        var overBound = ExperimentalJoin.BeginData(ExperimentalJoin.MaxSnapshotBytes + 1, hash);
        Check(!receiver.Begin(11, overBound), "a snapshot over the 32 MB bound is refused before allocation");
        Check(receiver.Watermark == 0 && !receiver.Active, "a refused Begin leaves no state behind");
        Check(!receiver.Begin(0, ExperimentalJoin.BeginData(4, hash)), "transfer id 0 is refused");
        Console.WriteLine("PASS: out-of-order, duplicate, oversized and zero-length parts are bounded and refused.");
    }

    static void RefusedFramesAreConsumed()
    {
        var lab = new Lab(World(4000, 8));
        lab.BadBegin = true;
        lab.Client.RequestSnapshot();
        lab.Pump();
        Check(lab.Applies == 0, "an invalid Begin never applies a world");
        Check(lab.Client.JoinStatus == "Requested" && lab.Client.Error == null, "an invalid Begin is consumed and the guest retries: " + lab.Client.JoinStatus);
        Check(!lab.Host.Peers[20].Ready, "the host peer stays unready");

        lab = new Lab(World(4000, 9));
        lab.OversizePart = true;
        lab.Client.RequestSnapshot();
        lab.Pump();
        Check(lab.Applies == 0 && lab.Client.JoinStatus == "Requested" && lab.Client.Error == null, "an oversized Part is consumed and the guest retries");

        lab = new Lab(World(4000, 10));
        lab.SpoofOrigin = true;
        lab.Client.RequestSnapshot();
        lab.Pump();
        Check(lab.Applies == 0 && lab.Receiver.Watermark == 0, "a frame with a foreign origin is consumed and ignored");
        Check(lab.Client.Error == null, "a foreign-origin frame is not a fatal error");

        // A frame replayed after the transfer completed cannot restart it.
        lab = new Lab(World(4000, 11));
        lab.Client.RequestSnapshot();
        var replay = new List<Packet>();
        lab.PumpWhere(delegate(Packet p) { if (p.To == 20 && p.Bytes.Length > 0 && p.Bytes[0] == 254) replay.Add(p); return true; });
        Check(lab.Client.Ready, "baseline transfer completed");
        lab.Queue.AddRange(replay);
        lab.Pump();
        Check(lab.Applies == 1 && lab.Client.Ready, "a replayed Begin/Part/End sequence does not apply twice");
        Console.WriteLine("PASS: invalid, oversized and spoofed frames are consumed without applying or wedging the join.");
    }

    static void GateOffKeepsTheNativePath()
    {
        Check(ExperimentalJoin.GateOn("1", true, true), "the gate opens only for env=1 + experimental backend + loopback");
        Check(!ExperimentalJoin.GateOn("0", true, true), "the gate is off without TLD_MP_EXPERIMENTAL_JOIN=1");
        Check(!ExperimentalJoin.GateOn(null, true, true), "the gate is off when the variable is unset");
        Check(!ExperimentalJoin.GateOn("1", false, true), "the gate needs the experimental backend");
        Check(!ExperimentalJoin.GateOn("1", true, false), "the gate keeps Steam sessions on the native transfer");

        var world = World(70000, 6);
        var lab = new Lab(world);
        lab.Experimental = false;
        lab.UseNative = true;
        lab.Client.RequestSnapshot();
        lab.Pump();
        Check(lab.Client.Ready && lab.Client.Error == null, "gate off: the native transfer still completes: " + (lab.Client.Error ?? lab.Client.JoinStatus));
        Check(lab.Delivered.Contains(StableOp.Begin) && lab.Delivered.Contains(StableOp.Part) && lab.Delivered.Contains(StableOp.End), "gate off: native Begin/Part/End frames");
        Check(!lab.Delivered.Contains(StableOp.ExpSnapshotBegin) && !lab.Delivered.Contains(StableOp.ExpSnapshotPart) && !lab.Delivered.Contains(StableOp.ExpSnapshotEnd), "gate off: no experimental frame is sent");
        Check(lab.Applies == 1 && lab.AppliedBytes.Length == world.Length, "gate off: the same native apply path is used");
        Check(!lab.Receiver.Active && lab.Receiver.Watermark == 0, "gate off: the experimental receiver is never touched");

        // The native coalescing rule is unchanged for a native transfer in flight.
        lab = new Lab(world);
        lab.Experimental = false; lab.UseNative = true;
        lab.Client.RequestSnapshot();
        lab.PumpHost();
        Check(lab.Host.Peers[20].Syncing && !lab.Host.Peers[20].ExternalSync, "native transfer in flight is not marked experimental");
        int serves = lab.Serves;
        lab.Now = 3; lab.Client.Tick(lab.Now);
        lab.Client.RequestSnapshot();
        lab.PumpHost();
        Check(lab.Serves == serves, "a duplicate request during a native transfer is still coalesced");
        lab.Pump();
        Check(lab.Client.Ready, "the coalesced native transfer still completes");
        Console.WriteLine("PASS: gate off (default) keeps the native Begin/Part/End transfer and its coalescing.");
    }

    public static int Main()
    {
        try
        {
            WireFormat();
            ExperimentalJoinCompletes();
            JournalOrderingAcrossTheBarrier();
            HashMismatchRetriesWithANewTransferId();
            PartAssemblyBounds();
            RefusedFramesAreConsumed();
            GateOffKeepsTheNativePath();
            Console.WriteLine("PASS: " + checks + " experimental-join assertions (framing, bounded reassembly, refusals, retry, journal order, gate off = native path).");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
