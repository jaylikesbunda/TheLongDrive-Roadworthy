using System;
using TldMp;
class TestBusArticulationPolicy
{
    static void Check(bool condition) { if (!condition) throw new Exception("Bus policy assertion failed"); }
    static void Main()
    {
        Check(!BusArticulationPolicy.ShouldResize(2f, 2f));
        Check(!BusArticulationPolicy.ShouldResize(2f, 2.000005f));
        Check(BusArticulationPolicy.ShouldResize(2f, 2.001f));
        Check(!BusArticulationPolicy.ShouldResize(2f, float.NaN));
        Check(!BusArticulationPolicy.ShouldResize(2f, float.PositiveInfinity));
        Console.WriteLine("PASS bus articulation policy");
    }
}
