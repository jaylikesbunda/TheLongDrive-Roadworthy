using System;
using System.IO;
using System.Reflection;
using TldMp;

internal static class WildlifeWeatherTests
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
        // Rabbits: single-player only, once a world with a player and its spawner is running.
        Check(WildlifeWeatherPolicy.RabbitsShouldRun(true, false, true, true, true), "rabbits run in a started single-player world");
        Check(!WildlifeWeatherPolicy.RabbitsShouldRun(true, true, true, true, true), "rabbits never run in multiplayer (ragdoll sync flings guest cars)");
        Check(!WildlifeWeatherPolicy.RabbitsShouldRun(false, false, true, true, true), "the option turns rabbits off");
        Check(!WildlifeWeatherPolicy.RabbitsShouldRun(true, false, false, true, true), "not before the world has started");
        Check(!WildlifeWeatherPolicy.RabbitsShouldRun(true, false, true, false, true), "not without a player");
        Check(!WildlifeWeatherPolicy.RabbitsShouldRun(true, false, true, true, false), "not without the native spawner");
        Check(WildlifeWeatherPolicy.RemoveRabbits(true, true, true), "a connected host removes existing rabbits");
        Check(!WildlifeWeatherPolicy.RemoveRabbits(true, false, true), "a guest never destroys items itself");
        Check(!WildlifeWeatherPolicy.RemoveRabbits(false, false, true), "single-player keeps its rabbits");
        Check(!WildlifeWeatherPolicy.RemoveRabbits(true, true, false), "nothing is removed without the rabbit prefab");

        // Sandstorms: single-player only, and only if a storm prefab exists.
        Check(WildlifeWeatherPolicy.SandstormsShouldRun(true, false, true, true, 1), "sandstorms run in single-player");
        Check(!WildlifeWeatherPolicy.SandstormsShouldRun(true, true, true, true, 1), "sandstorms never run in multiplayer (not synchronised)");
        Check(!WildlifeWeatherPolicy.SandstormsShouldRun(false, false, true, true, 1), "the option turns sandstorms off");
        Check(!WildlifeWeatherPolicy.SandstormsShouldRun(true, false, false, true, 1), "not before the world has started");
        Check(!WildlifeWeatherPolicy.SandstormsShouldRun(true, false, true, false, 1), "not without a player");
        Check(!WildlifeWeatherPolicy.SandstormsShouldRun(true, false, true, true, 0), "not without a storm prefab");
        Check(WildlifeWeatherPolicy.SandstormMinSeconds > 0 && WildlifeWeatherPolicy.SandstormMaxSeconds >= WildlifeWeatherPolicy.SandstormMinSeconds, "storm timing is a valid range");
        Check(WildlifeWeatherPolicy.SandstormRadius > 0 && WildlifeWeatherPolicy.SandstormRadius < 25000f, "storms spawn inside the prefab's 25 km lifetime distance");

        // Creature guard: only kinematic bodies skip the (rejected) velocity write.
        Check(WildlifeWeatherPolicy.SkipCreatureVelocity(true, true), "a frozen creature skips the velocity write");
        Check(!WildlifeWeatherPolicy.SkipCreatureVelocity(true, false), "a dynamic creature moves natively");
        Check(!WildlifeWeatherPolicy.SkipCreatureVelocity(false, false), "a missing body keeps native behaviour");

        // Options file: both default on, round-trip, and accept on/off spellings.
        PatchOptions.Parse(null);
        Check(PatchOptions.Rabbits && PatchOptions.Sandstorms, "both default on");
        PatchOptions.Parse("rabbits=off\nsandstorms=0\n");
        Check(!PatchOptions.Rabbits && !PatchOptions.Sandstorms, "both can be turned off");
        string text = PatchOptions.Format();
        Check(text.Contains("rabbits=off") && text.Contains("sandstorms=off"), "the file records both");
        PatchOptions.Parse(text);
        Check(!PatchOptions.Rabbits && !PatchOptions.Sandstorms, "round-trip keeps them off");
        PatchOptions.Parse("rabbits=TRUE\nsandstorms=nonsense\n");
        Check(PatchOptions.Rabbits && PatchOptions.Sandstorms, "unknown values keep the default");
        Check(PatchOptions.Format().Contains("smoothcars=on"), "existing keys are still written");

        Console.WriteLine("PASS: " + checks + " wildlife/weather assertions; rabbits and sandstorms are single-player only, a host clears leftover rabbits, frozen creatures skip rejected velocity writes, and both options round-trip.");
        return 0;
    }
}
