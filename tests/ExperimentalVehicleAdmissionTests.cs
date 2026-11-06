using System;
using TldMp.Experimental;

internal static class ExperimentalVehicleAdmissionTests
{
    static void Check(bool ok) { if (!ok) throw new Exception("Vehicle admission invariant failed"); }
    public static int Main()
    {
        var registry = new AuthorityRegistry(); Check(registry.Add(1, 100));
        var pose = new VehiclePose { Id = 1, Epoch = 1, Qw = 1 };
        byte[] data = VehiclePoseCodec.Write(pose);
        Check(VehicleAdmission.Accept(registry, 100, data));
        Check(!VehicleAdmission.Accept(registry, 101, data) && !VehicleAdmission.Accept(registry, 100, new byte[0]));
        ulong epoch; Check(registry.Transfer(1, 1, 101, out epoch));
        Check(!VehicleAdmission.Accept(registry, 100, data) && !VehicleAdmission.Accept(registry, 101, data));
        pose.Epoch = epoch;
        Check(VehicleAdmission.Accept(registry, 101, VehiclePoseCodec.Write(pose)));
        Console.WriteLine("PASS: vehicle pose owner and epoch admission");
        return 0;
    }
}
