using System;
using TldMp.Experimental;

internal static class ExperimentalVehicleFlowTests
{
    static void Check(bool ok) { if (!ok) throw new Exception("Vehicle flow invariant failed"); }
    public static int Main()
    {
        var authority = new AuthorityRegistry(); Check(authority.Add(1, 100));
        var fingerprint = new byte[32];
        var peer = new PeerSession(); Check(peer.Begin(100, 77));
        Check(peer.Admit(100, HelloCodec.Write(77, fingerprint), fingerprint));
        var guard = new ConnectionGuard(peer, delegate(ulong sender, byte messageKind, byte[] data) {
            return messageKind == ConnectionGuard.Motion && VehicleAdmission.Accept(authority, sender, data);
        });
        var pose = new VehiclePose { Id = 1, Epoch = 1, Qw = 1, X = 10000 };
        byte[] frame = EnvelopeCodec.Write(77, 1, ConnectionGuard.Motion, VehiclePoseCodec.Write(pose));
        byte kind; byte[] payload;
        Check(guard.Receive(100, frame, false, out kind, out payload) == SequenceResult.Accepted);
        Check(guard.Receive(100, frame, false, out kind, out payload) == SequenceResult.DuplicateOrOld);
        ulong epoch; Check(authority.Transfer(1, 1, 101, out epoch));
        Check(guard.Receive(100, EnvelopeCodec.Write(77, 2, ConnectionGuard.Motion, VehiclePoseCodec.Write(pose)), false, out kind, out payload) == SequenceResult.Invalid);
        Console.WriteLine("PASS: admitted vehicle motion rejected after authority transfer");
        return 0;
    }
}
