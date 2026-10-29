using System;
using System.IO;
using System.Reflection;
using TldMp;

internal static class CarPosePolicyTests
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
        Check(CarPosePolicy.AdjustCount(null, 3) == 3, "null item must keep the native count");
        Check(CarPosePolicy.Adjust(0, false, true) == 0, "an empty car must not raise a zero native count");
        Check(CarPosePolicy.Adjust(0, true, true) == 4, "base zero must raise to the minimum when occupied and moving");
        Check(CarPosePolicy.Adjust(2, true, true) == 4, "base two must raise to four");
        Check(CarPosePolicy.Adjust(5, true, true) == 7, "base five must add two");
        Check(CarPosePolicy.Adjust(6, true, true) == 8, "base six must cap at eight");
        Check(CarPosePolicy.Adjust(7, true, true) == 8, "base seven must cap at eight");
        Check(CarPosePolicy.Adjust(8, true, true) == 8, "base eight must stay eight");
        Check(CarPosePolicy.Adjust(9, true, true) == 9, "a higher native count must not be reduced");
        Check(CarPosePolicy.Adjust(int.MaxValue, true, true) == int.MaxValue, "int max must not overflow");
        Check(CarPosePolicy.Adjust(4, false, true) == 4, "empty car must keep the native count");
        Check(CarPosePolicy.Adjust(4, true, false) == 4, "parked car must keep the native count");
        Check(CarPosePolicy.Adjust(4, false, false) == 4, "empty parked car must keep the native count");
        // A car driven alone is streamed at the full rate whenever another player
        // is connected (it used to stay at about one pose per second).
        Check(CarPosePolicy.WantsBurst(true, true, 1), "moving local car with an observer not streamed");
        Check(CarPosePolicy.WantsBurst(true, true, 3), "moving local car with observers not streamed");
        Check(!CarPosePolicy.WantsBurst(true, true, 0), "streamed with nobody to see it");
        Check(!CarPosePolicy.WantsBurst(true, false, 1), "parked car streamed");
        Check(!CarPosePolicy.WantsBurst(false, true, 1), "streamed a car another machine controls");
        Check(CarPosePolicy.WantsBurst(true, false, true, 1), "a pushed car streams even at low speed");
        Check(!CarPosePolicy.WantsBurst(false, false, true, 1), "a pushed car owned elsewhere is not streamed");
        Check(!CarPosePolicy.WantsBurst(true, false, true, 0), "a pushed car without observers is not streamed");
        Console.WriteLine("PASS: " + checks + " car pose policy assertions; extra count for moving locally controlled cars while another player is connected, capped, native count otherwise.");
        return 0;
    }
}
