using System;
using System.IO;
using System.Reflection;
using TldMp;

internal static class PlayerLeavePolicyTests
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
        Check(!PlayerLeaveSafety.ShouldPrune(true, true), "a live proxy with a script must be kept");
        Check(PlayerLeaveSafety.ShouldPrune(false, true), "a destroyed proxy must be pruned");
        Check(PlayerLeaveSafety.ShouldPrune(true, false), "a destroyed script must be pruned");
        Check(PlayerLeaveSafety.ShouldPrune(false, false), "a fully destroyed entry must be pruned");
        PlayerLeaveSafety.IgnoreCollision(null, null, false);
        Check(true, "a null remote script must be a safe no-op");
        Console.WriteLine("PASS: " + checks + " leave-safety assertions; only destroyed entries are pruned and a null remote script is ignored.");
        return 0;
    }
}
