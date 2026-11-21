using System;
using System.IO;
using System.Reflection;
using TldMp;

internal static class HeadroomPolicyTests
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
        float inf = float.PositiveInfinity;
        // Radius covers the near-plane corners.
        float r = HeadroomPolicy.Radius(0.075f, 60f, 16f / 9f);
        Check(r > 0.075f * 1.5f && r < 0.16f, "a 60 degree 16:9 view at 7.5 cm near clip needs about a 12 cm sphere, got " + r);
        Check(HeadroomPolicy.Radius(0.075f, 100f, 16f / 9f) > r, "a wider view needs a bigger sphere");
        Check(HeadroomPolicy.Radius(5f, 60f, 1f) == HeadroomPolicy.MaxRadius && HeadroomPolicy.Radius(0f, 60f, 1f) == HeadroomPolicy.MaxRadius, "the sphere is capped and odd cameras get the cap");
        Check(HeadroomPolicy.Radius(0.075f, float.NaN, 1f) == HeadroomPolicy.MaxRadius, "an unknown FOV gets the cap");
        // Nothing in the way leaves the camera where the game put it.
        Check(float.IsPositiveInfinity(HeadroomPolicy.Allowed(inf, inf, 0.016f)), "with nothing in the way the camera is free");
        Check(float.IsPositiveInfinity(HeadroomPolicy.Allowed(float.NaN, float.NaN, 0.016f)), "unknown values leave the camera free");
        // Pulling in is immediate.
        Check(HeadroomPolicy.Allowed(inf, 0.1f, 0.016f) == 0.1f, "an obstacle pulls the camera in the same frame");
        Check(HeadroomPolicy.Allowed(0.3f, 0.05f, 0.016f) == 0.05f, "a nearer obstacle pulls in at once");
        // Release is eased.
        float next = HeadroomPolicy.Allowed(0.1f, inf, 0.1f);
        Check(Math.Abs(next - (0.1f + HeadroomPolicy.ReleaseSpeed * 0.1f)) < 1e-5f, "the camera eases back out once the obstacle is gone");
        Check(HeadroomPolicy.Allowed(0.1f, 0.12f, 1f) == 0.12f, "easing out never passes the obstacle");
        Check(HeadroomPolicy.Allowed(0.1f, inf, -1f) == 0.1f, "a negative frame time never pulls in");
        Check(HeadroomPolicy.ReleaseSpeed >= 0.5f && HeadroomPolicy.ReleaseSpeed <= 5f, "the release is quick but not a snap");
        Console.WriteLine("PASS: " + checks + " headroom policy assertions; the camera sphere covers the near plane, an obstacle pulls the seated camera in the same frame, and it eases back out once clear.");
        return 0;
    }
}
