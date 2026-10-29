using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mono.Cecil;

// Usage: CheckRuntimeCallbacks <Tld.Multiplayer.dll> <packaging\singleplayer directory>
// v4 is built after the gate (staging/installer) and calls back into the runtime.
// A missing callback makes Mono reject the whole patched method at run time, so
// every TldMp.StableGameBridge::Name/N named in the v4 sources must exist as a
// public static method with N parameters.
static class CheckRuntimeCallbacks
{
    static int Main(string[] args)
    {
        if (args.Length != 2) { Console.Error.WriteLine("Usage: CheckRuntimeCallbacks <runtime.dll> <v4 source directory>"); return 2; }
        var pattern = new Regex(@"TldMp\.StableGameBridge::(\w+)/(\d+)");
        var required = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.GetFiles(args[1], "*.cs"))
            foreach (Match m in pattern.Matches(File.ReadAllText(file))) required.Add(m.Groups[1].Value + "/" + m.Groups[2].Value);
        if (required.Count == 0) { Console.Error.WriteLine("No v4 runtime callbacks found in " + args[1] + "; the check would prove nothing."); return 1; }
        using (var module = ModuleDefinition.ReadModule(args[0]))
        {
            var bridge = module.Types.SingleOrDefault(t => t.FullName == "TldMp.StableGameBridge");
            if (bridge == null) { Console.Error.WriteLine("Runtime has no TldMp.StableGameBridge."); return 1; }
            foreach (string callback in required)
            {
                string[] parts = callback.Split('/');
                int count = Int32.Parse(parts[1]);
                if (!bridge.Methods.Any(m => m.Name == parts[0] && m.IsStatic && m.IsPublic && m.Parameters.Count == count))
                { Console.Error.WriteLine("Runtime is missing v4 callback TldMp.StableGameBridge::" + callback + "."); return 1; }
            }
        }
        Console.WriteLine("PASS: runtime provides every v4 callback (" + String.Join(", ", required.ToArray()) + ").");
        return 0;
    }
}
