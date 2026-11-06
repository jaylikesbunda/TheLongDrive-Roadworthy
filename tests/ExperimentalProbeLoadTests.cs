using System;
using TldMp.Experimental;

internal static class ExperimentalProbeLoadTests
{
    public static int Main()
    {
        Environment.SetEnvironmentVariable("TLD_MP_VEHICLE_SHADOW_PROBE", null);
        var method = typeof(VehicleShadowProbe).GetMethod("Observe");
        if (method == null || method.GetParameters().Length != 1 || method.GetParameters()[0].ParameterType.FullName != "tosaveitemscript")
            throw new Exception("Probe entry point has the wrong game type.");
        VehicleShadowProbe.Observe(null);
        Console.WriteLine("PASS: staged observer entry point loads and accepts null in a managed-only fixture");
        return 0;
    }
}
