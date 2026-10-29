using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using TldMp;

internal static class TerrainHoleAdjacencyTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    static void Compare(int[] triangles, int vertex, string label)
    {
        TerrainHoleAdjacency.Begin();
        List<int> actual = TerrainHoleAdjacency.Get(triangles, vertex);
        List<int> expected = mainscript.GetConnectedVertices(triangles, vertex);
        Check(actual.Count == expected.Count, label + " count");
        for (int i = 0; i < actual.Count; i++) Check(actual[i] == expected[i], label + " order/value");
    }

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
        Compare(new int[0], 0, "empty");
        Compare(new int[] { 0, 1, 2 }, 0, "single v0");
        Compare(new int[] { 0, 1, 2 }, 1, "single v1");
        Compare(new int[] { 0, 1, 2 }, 2, "single v2");
        Compare(new int[] { 0, 0, 1 }, 0, "degenerate v0");
        Compare(new int[] { 0, 1, 2 }, 99, "out of range");
        Compare(new int[] { -1, 2, 3 }, -1, "negative vertex");
        Compare(new int[] { -1, 2, 3 }, 2, "negative triangle value");

        var random = new Random(71337);
        for (int round = 0; round < 300; round++)
        {
            int triangles = random.Next(0, 120);
            int vertices = Math.Max(1, random.Next(1, 80));
            var data = new int[triangles * 3];
            for (int i = 0; i < data.Length; i++) data[i] = random.Next(vertices);
            int max = 0;
            for (int i = 0; i < data.Length; i++) if (data[i] > max) max = data[i];
            TerrainHoleAdjacency.Begin();
            for (int v = -1; v <= max + 1; v++) Compare(data, v, "random " + round + "/" + v);
            // Reuse the same call and array: the cache must serve identical results.
            int probe = max / 2;
            Compare(data, probe, "cached " + round);
        }

        // A 40x40 grid template (the collider terrain resolution in the observed log).
        const int resolution = 40;
        var grid = new List<int>();
        for (int i = -1; i < resolution + 1; i++)
            for (int j = -1; j < resolution + 1; j++)
            {
                if (j >= resolution || i >= resolution) continue;
                int i2 = j + 1 + (i + 1) * (resolution + 2);
                grid.Add(i2); grid.Add(i2 + (resolution + 2)); grid.Add(i2 + (resolution + 2) + 1);
                grid.Add(i2 + (resolution + 2) + 1); grid.Add(i2 + 1); grid.Add(i2);
            }
        var gridData = grid.ToArray();
        TerrainHoleAdjacency.Begin();
        var watch = Stopwatch.StartNew();
        for (int repeat = 0; repeat < 200; repeat++)
            for (int v = 0; v < 20; v++) TerrainHoleAdjacency.Get(gridData, v);
        watch.Stop();
        long indexedTicks = watch.ElapsedTicks;
        watch.Restart();
        for (int repeat = 0; repeat < 200; repeat++)
            for (int v = 0; v < 20; v++) mainscript.GetConnectedVertices(gridData, v);
        watch.Stop();
        long nativeTicks = watch.ElapsedTicks;
        Console.WriteLine("PASS: " + checks + " terrain-hole adjacency assertions; grid vertices=" + resolution * resolution + " triangles=" + (gridData.Length / 3) + " indexedTicks=" + indexedTicks + " nativeTicks=" + nativeTicks + " (synthetic, not a game FPS claim)");
        return 0;
    }
}
