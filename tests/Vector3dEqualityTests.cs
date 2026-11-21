using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Vector3d = UnityEngine.Vector3d;
using TldMp;

internal static class Vector3dEqualityTests
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
        var comparer = new Vector3dEquality();
        Check(Vector3dEquality.SelfTest(comparer) == null, "comparer matches the game's Vector3d.Equals and GetHashCode");
        var nan = new Vector3d(double.NaN, 0, 0);
        Check(comparer.Equals(nan, new Vector3d(double.NaN, 0, 0)) == nan.Equals((object)new Vector3d(double.NaN, 0, 0)), "NaN keys behave like the game's Equals");
        Check(comparer.Equals(new Vector3d(0.0, 0, 0), new Vector3d(-0.0, 0, 0)) == new Vector3d(0.0, 0, 0).Equals((object)new Vector3d(-0.0, 0, 0)), "signed zero behaves like the game's Equals");
        // A dictionary built on the comparer finds the same keys as one on the default.
        var plain = new Dictionary<Vector3d, int>();
        var fast = new Dictionary<Vector3d, int>(comparer);
        var random = new System.Random(7);
        var keys = new List<Vector3d>();
        for (int i = 0; i < 2000; i++)
        {
            var k = new Vector3d(Math.Round(random.NextDouble() * 40 - 20) * 100.0, 0, Math.Round(random.NextDouble() * 40 - 20) * 100.0);
            keys.Add(k); plain[k] = i; fast[k] = i;
        }
        Check(plain.Count == fast.Count, "same number of distinct keys");
        foreach (var k in keys) { int a, b; Check(plain.TryGetValue(k, out a) && fast.TryGetValue(k, out b) && a == b, "same lookup result"); }
        Console.WriteLine("PASS: " + checks + " Vector3d comparer assertions; equality and hash match the game's own methods, including NaN and signed zero, and dictionaries behave the same.");
        return 0;
    }
}
