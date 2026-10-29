using System;
using System.IO;
using System.Reflection;
using TldMp;

internal static class CarOwnershipTests
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
        // Roles: notCar 0, nothing 1, justHost 2, pickedup 3, shotgun 4, pushgripping 5, driving 6.
        Check(CarOwnershipPolicy.Shotgun == (int)tosaveitemscript.carWhatDoing.shotgun, "the shotgun role matches the game enum");
        Check(CarOwnershipPolicy.VetoLocalCharge(true, false, 4, 5), "a guest passenger beside the host never takes the car");
        Check(CarOwnershipPolicy.VetoLocalCharge(true, false, 4, CarOwnershipPolicy.HostRangeMetres), "the host range is inclusive");
        Check(!CarOwnershipPolicy.VetoLocalCharge(true, false, 4, CarOwnershipPolicy.HostRangeMetres + 1), "with the host out of physics range the vanilla handover applies");
        Check(!CarOwnershipPolicy.VetoLocalCharge(true, false, 4, -1), "an unknown host position keeps vanilla rules");
        Check(!CarOwnershipPolicy.VetoLocalCharge(true, true, 4, 5), "the host is never vetoed");
        Check(!CarOwnershipPolicy.VetoLocalCharge(true, false, 6, 5), "a guest driving keeps the car");
        Check(!CarOwnershipPolicy.VetoLocalCharge(true, false, 5, 5), "a guest pushing keeps the car");
        Check(!CarOwnershipPolicy.VetoLocalCharge(true, false, 3, 5), "a guest lifting keeps the car");
        Check(!CarOwnershipPolicy.VetoLocalCharge(false, false, 4, 5), "single-player is untouched");

        Check(PaintPolicy.IsUnsetPaint(0, 0, 0, 0), "the default colour is unset paint");
        Check(!PaintPolicy.IsUnsetPaint(0, 0, 0, 1), "opaque black paint is applied");
        Check(!PaintPolicy.IsUnsetPaint(0.8f, 0.1f, 0.1f, 1), "real paint is applied");
        Check(!PaintPolicy.IsUnsetPaint(0, 0, 0, 0.5f), "any alpha counts as paint");

        Console.WriteLine("PASS: " + checks + " car ownership/paint assertions; whoever drives owns the car, otherwise the host within physics range; push, lift and far-host handovers stay vanilla; unset paint never blackens a part.");
        return 0;
    }
}
