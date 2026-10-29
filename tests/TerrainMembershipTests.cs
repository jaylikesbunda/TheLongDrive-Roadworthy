using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using UnityEngine;
using TldMp;

public static class TerrainMembershipTests
{
    static int checks;
    static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception(message); }

    static terrainGenScript Terrain()
    { return (terrainGenScript)FormatterServices.GetUninitializedObject(typeof(terrainGenScript)); }

    static List<Vector3d> NativeRemoval(Dictionary<Vector3d, newTerrainTileScript> terrains, List<Vector3d> needed)
    {
        var result = new List<Vector3d>();
        foreach (KeyValuePair<Vector3d, newTerrainTileScript> entry in terrains)
            if (!needed.Contains(entry.Key)) result.Add(entry.Key);
        return result;
    }

    static void NativeCheckRemove(Dictionary<Vector3d, newTerrainTileScript> terrains, List<Vector3d> needed, List<Vector3d> output)
    {
        output.Clear();
        foreach (KeyValuePair<Vector3d, newTerrainTileScript> entry in terrains)
            if (!needed.Contains(entry.Key)) output.Add(entry.Key);
    }

    static void Compare(string label, List<Vector3d> needed, Dictionary<Vector3d, newTerrainTileScript> terrains)
    {
        var expected = NativeRemoval(terrains, needed);
        var terrain = Terrain();
        terrain.coordsNeeded = needed;
        terrain.terrains = terrains;
        terrain.coordsToRemove = new List<Vector3d> { new Vector3d(987654, 5, 6) };
        bool native = TerrainMembership.CheckRemove(terrain);
        Check(!native, label + ": optimized sync path should skip native body (native=" + native +
            ", terrainDict=" + (terrain.terrains != null) + ", needed=" + (terrain.coordsNeeded != null) +
            ", output=" + (terrain.coordsToRemove != null) + ", async=" + (terrain.mapSetting != null && terrain.mapSetting.useAsync2) + ")");
        Check(expected.Count == terrain.coordsToRemove.Count, label + ": count differs");
        for (int i = 0; i < expected.Count; i++)
            Check(expected[i].Equals(terrain.coordsToRemove[i]), label + ": dictionary removal order/value differs at " + i);
    }

    public static int Main(string[] args)
    {
        try
        {
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
            {
                string name = new AssemblyName(e.Name).Name;
                string path = name == "Assembly-CSharp" ? args[1] : System.IO.Path.Combine(args[0], name + ".dll");
                return System.IO.File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            Assembly.LoadFrom(System.IO.Path.GetFullPath(args[1]));
            return Run();
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run()
    {
        try
        {
            var edgeNeeded = new List<Vector3d> {
                new Vector3d(-0.0, 2, 3), new Vector3d(Double.NaN, 4, 5),
                new Vector3d(1e300, -1e300, 9), new Vector3d(-8, -9, -10),
                new Vector3d(1, 2, 3), new Vector3d(1, 2, 3)
            };
            var edgeTerrain = new Dictionary<Vector3d, newTerrainTileScript>();
            edgeTerrain.Add(new Vector3d(+0.0, 2, 3), null);
            edgeTerrain.Add(new Vector3d(Double.NaN, 4, 5), null);
            edgeTerrain.Add(new Vector3d(1e300, -1e300, 9), null);
            edgeTerrain.Add(new Vector3d(20, 21, 22), null);
            Compare("signed zero, NaN, huge/negative values, duplicates", edgeNeeded, edgeTerrain);
            Check(!new Vector3d(1, 2, 3).Equals(new Vector3d(1, 2, 4)), "Vector3d.Equals fixture unexpectedly equates distinct values");

            Compare("empty", new List<Vector3d>(), new Dictionary<Vector3d, newTerrainTileScript>());
            Compare("all needed", new List<Vector3d> { new Vector3d(7, 0, -7) },
                new Dictionary<Vector3d, newTerrainTileScript> { { new Vector3d(7, 0, -7), null } });

            var asyncTerrain = Terrain();
            asyncTerrain.coordsNeeded = new List<Vector3d>();
            asyncTerrain.terrains = new Dictionary<Vector3d, newTerrainTileScript>();
            asyncTerrain.coordsToRemove = new List<Vector3d> { new Vector3d(44, 5, 6) };
            asyncTerrain.mapSetting = (mapSettingScript)FormatterServices.GetUninitializedObject(typeof(mapSettingScript));
            asyncTerrain.mapSetting.useAsync2 = true;
            Check(TerrainMembership.CheckRemove(asyncTerrain), "useAsync2 must retain native shared-collection path");
            Check(asyncTerrain.coordsToRemove.Count == 1 && asyncTerrain.coordsToRemove[0].Equals(new Vector3d(44, 5, 6)),
                "async fallback must leave the output list for the native method");

            var aliased = Terrain();
            var shared = new List<Vector3d> { new Vector3d(1, 0, 0), new Vector3d(2, 0, 0) };
            aliased.coordsNeeded = shared;
            aliased.coordsToRemove = shared;
            aliased.terrains = new Dictionary<Vector3d, newTerrainTileScript> {
                { new Vector3d(10, 0, 0), null }, { new Vector3d(20, 0, 0), null }
            };
            Check(TerrainMembership.CheckRemove(aliased), "aliased input/output lists must use native behavior");
            var aliasExpected = new List<Vector3d>(shared);
            NativeCheckRemove(aliased.terrains, aliasExpected, aliasExpected);
            NativeCheckRemove(aliased.terrains, shared, shared);
            Check(aliasExpected.Count == shared.Count, "alias fallback must retain native in-place membership semantics");
            for (int i = 0; i < shared.Count; i++)
                Check(aliasExpected[i].Equals(shared[i]), "alias fallback order differs from native at " + i);

            var needed = new List<Vector3d>();
            var terrains = new Dictionary<Vector3d, newTerrainTileScript>();
            for (int i = 0; i < 5000; i++)
            {
                var p = new Vector3d(i * 256.0 - 640000, (i % 5) - 2, -i * 128.0);
                terrains.Add(p, null);
                if ((i & 1) == 0) needed.Add(p);
            }
            // Warm up both paths and compare exact output. Timings are indicative only.
            Compare("5000 terrain keys", needed, terrains);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 3; i++) NativeRemoval(terrains, needed);
            long listTicks = sw.ElapsedTicks;
            var measured = Terrain(); measured.coordsNeeded = needed; measured.terrains = terrains;
            measured.coordsToRemove = new List<Vector3d>();
            sw.Restart();
            for (int i = 0; i < 3; i++) TerrainMembership.CheckRemove(measured);
            long setTicks = sw.ElapsedTicks;
            Console.WriteLine("PASS: " + checks + " exact terrain removal parity checks; synthetic 5k-key/2.5k-needed, 3 passes ticks native-list=" + listTicks + " indexed=" + setTicks + " (indicative, not game FPS evidence).");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
