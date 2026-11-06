using System;
using TldMp.Experimental;

internal static class ExperimentalVehiclePoseTests
{
    static void Check(bool ok) { if (!ok) throw new Exception("Vehicle pose invariant failed"); }
    public static int Main()
    {
        var pose = new VehiclePose { Id = 2, Epoch = 3, X = 300000.25, Y = 5, Z = -10, Qw = 1, Vx = 20 };
        byte[] bytes = VehiclePoseCodec.Write(pose); VehiclePose decoded = null;
        Check(bytes.Length == VehiclePoseCodec.Length && VehiclePoseCodec.Read(bytes, out decoded));
        Check(decoded.Id == 2 && decoded.Epoch == 3 && decoded.X == 300000.25 && decoded.Vx == 20);
        Check(!VehiclePoseCodec.Read(new byte[bytes.Length - 1], out decoded));
        pose.X = double.NaN; Check(!VehiclePoseCodec.Valid(pose)); pose.X = 1;
        pose.Vx = float.PositiveInfinity; Check(!VehiclePoseCodec.Valid(pose)); pose.Vx = 0;
        pose.Qw = 0; Check(!VehiclePoseCodec.Valid(pose));
        Console.WriteLine("PASS: experimental global vehicle pose validation");
        return 0;
    }
}
