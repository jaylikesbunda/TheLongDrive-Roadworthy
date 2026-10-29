using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using TldMp;

internal static class TerrainWorkDiagnosticsTests
{
    static int assertions;

    static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }

    static void Main(string[] args)
    {
        if (args == null || args.Length < 2)
            throw new ArgumentException("Expected managed directory and Assembly-CSharp path.");
        string managedDirectory = args[0];
        string gameAssembly = args[1];
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            string simpleName = new AssemblyName(e.Name).Name;
            if (simpleName == "Assembly-CSharp") return Assembly.LoadFrom(gameAssembly);
            if (simpleName.StartsWith("UnityEngine", StringComparison.Ordinal))
            {
                string candidate = Path.Combine(managedDirectory, simpleName + ".dll");
                if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);
            }
            return null;
        };
        RunTests();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void RunTests()
    {
        var deadlines = new Dictionary<int, float>();
        Check(TerrainWorkDiagnostics.ShouldReport(true, 10, 0f, 30f, deadlines),
            "first instance snapshot reports immediately");
        Check(!TerrainWorkDiagnostics.ShouldReport(true, 10, 29.99f, 30f, deadlines),
            "snapshot is throttled before its deadline");
        Check(TerrainWorkDiagnostics.ShouldReport(true, 10, 30f, 30f, deadlines),
            "snapshot reports at its deadline");
        Check(TerrainWorkDiagnostics.ShouldReport(true, 11, 30f, 30f, deadlines),
            "throttle is independent per terrain instance");
        Check(!TerrainWorkDiagnostics.ShouldReport(false, 12, 30f, 30f, deadlines),
            "diagnostics disabled means no report");
        Check(!TerrainWorkDiagnostics.ShouldReport(true, 12, 30f, 30f, null),
            "missing state fails closed");

        var snapshot = new TerrainWorkDiagnostics.Snapshot
        {
            Name = "DistantTerrain", Map = "MapSettings", Active = true, Enabled = true,
            Distant = true, Collider = false, UseAsync = false, UseAsync2 = true,
            Running = true, Control = 8, Control2 = false, Tiles = 9, Needed = 25,
            Removing = 2, Queued = 3, Resolution = 100, TileSize = 200f,
            DrawDistance = 1490f, LastDrawDistance = 1490f
        };
        string line = snapshot.Format();
        Check(line.Contains("name=DistantTerrain") && line.Contains("map=MapSettings"),
            "formatted snapshot identifies component and map");
        Check(line.Contains("async=False/True") && line.Contains("running=True"),
            "formatted snapshot identifies generation mode and worker state");
        Check(line.Contains("tiles=9 needed=25 removing=2 queued=3 resolution=100"),
            "formatted snapshot includes work backlog and resolution");
        Check(line.Contains("tileSize=200 draw=1490 lastDraw=1490"),
            "formatted snapshot includes distance policy");

        Console.WriteLine("PASS: " + assertions + " terrain workload snapshot and throttle assertions");
    }
}
