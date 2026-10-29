using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using TldMp;
using UnityEngine;

internal static class TerrainRoadSelectionTests
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

    static roadGenScript.roadClass MakeRoad(double ox, double oz, int elements, int bones, float minDist, float maxDist)
    {
        var road = (roadGenScript.roadClass)FormatterServices.GetUninitializedObject(typeof(roadGenScript.roadClass));
        road.roadElements = new List<roadGenScript.roadElementClass>();
        road.terrainAlignMinDist = minDist;
        road.terrainAlignMaxDist = maxDist;
        for (int e = 0; e < elements; e++)
        {
            var element = (roadGenScript.roadElementClass)FormatterServices.GetUninitializedObject(typeof(roadGenScript.roadElementClass));
            element.bonePos = new List<Vector3d>();
            for (int b = 0; b < bones; b++)
                element.bonePos.Add(new Vector3d(ox + e * 4.0 + b * 0.25, 0, oz + Math.Sin(e * 0.7 + b * 0.11) * 3.0));
            road.roadElements.Add(element);
        }
        return road;
    }

    static bool SameBits(Vector3d a, Vector3d b)
    {
        return BitConverter.DoubleToInt64Bits(a.x) == BitConverter.DoubleToInt64Bits(b.x) &&
               BitConverter.DoubleToInt64Bits(a.y) == BitConverter.DoubleToInt64Bits(b.y) &&
               BitConverter.DoubleToInt64Bits(a.z) == BitConverter.DoubleToInt64Bits(b.z);
    }

    static void Compare(List<roadGenScript.roadClass> list, List<Vector3d> probes, string label)
    {
        var deduped = new List<roadGenScript.roadClass>(list);
        TerrainRoadSelection.Dedupe(deduped);
        Check(deduped.Count <= list.Count, label + " dedupe grew the list");
        for (int i = 0; i < probes.Count; i++)
        {
            Vector3d a, b;
            var ra = roadGenScript.NearestPointOnRoadBone(probes[i], list, out a);
            var rb = roadGenScript.NearestPointOnRoadBone(probes[i], deduped, out b);
            Check(ReferenceEquals(ra, rb), label + " selected road changed at " + i);
            Check(SameBits(a, b), label + " selected point changed at " + i);
        }
    }

    static int Run()
    {
        var empty = new List<roadGenScript.roadClass>();
        Check(TerrainRoadSelection.Dedupe(null) == null, "null list changed");
        Check(ReferenceEquals(TerrainRoadSelection.Dedupe(empty), empty) && empty.Count == 0, "empty list changed");

        var r1 = MakeRoad(0, 0, 12, 6, 3f, 30f);
        var r2 = MakeRoad(50, 20, 9, 5, 4f, 25f);
        var nullRoad = (roadGenScript.roadClass)null;

        var ordered = new List<roadGenScript.roadClass> { r1, r2, r1, nullRoad, r2, nullRoad, r1 };
        var result = TerrainRoadSelection.Dedupe(ordered);
        Check(ReferenceEquals(result, ordered), "dedupe replaced the list instead of compacting it");
        Check(ordered.Count == 3, "dedupe did not collapse to first occurrences: " + ordered.Count);
        Check(ReferenceEquals(ordered[0], r1) && ReferenceEquals(ordered[1], r2) && ordered[2] == null, "dedupe order changed");

        var probes = new List<Vector3d>();
        var random = new System.Random(4421);
        for (int i = 0; i < 400; i++)
            probes.Add(new Vector3d(random.Next(-2000, 2000) / 10.0, random.Next(-5, 6), random.Next(-2000, 2000) / 10.0));
        probes.Add(new Vector3d(0, 0, 0));
        probes.Add(new Vector3d(50, 0, 20));
        probes.Add(new Vector3d(double.NaN, 0, 0));
        probes.Add(new Vector3d(double.PositiveInfinity, 0, 0));

        Compare(ordered, probes, "mixed");
        Compare(new List<roadGenScript.roadClass> { r1, r1, r1, r1, r1 }, probes, "single repeated");
        Compare(new List<roadGenScript.roadClass> { r1, r2 }, probes, "distinct");
        Compare(new List<roadGenScript.roadClass> { nullRoad, nullRoad, r1 }, probes, "null first");
        Compare(new List<roadGenScript.roadClass> { r1, nullRoad, nullRoad }, probes, "null later");

        // Bounded fallback: above the guard the native list is returned unchanged.
        var huge = new List<roadGenScript.roadClass>(1100);
        for (int i = 0; i < 1100; i++) huge.Add(i % 2 == 0 ? r1 : r2);
        var hugeResult = TerrainRoadSelection.Dedupe(huge);
        Check(ReferenceEquals(hugeResult, huge) && huge.Count == 1100, "oversized list was not left untouched");

        // Isolated benchmark: one physical road registered once per inner chunk.
        // 9 copies is the observed 3x3 inner-cell ring; 3 copies is a conservative
        // case. Probes cover a 100 m tile plus the surrounding border.
        var big = MakeRoad(0, 0, 90, 8, 3f, 60f);
        var duplicated = new List<roadGenScript.roadClass>();
        for (int i = 0; i < 9; i++) duplicated.Add(big);
        var unique = new List<roadGenScript.roadClass> { big };
        var tile = new List<Vector3d>(2048);
        for (int x = -1; x <= 40; x++)
            for (int z = -1; z <= 40; z++)
                tile.Add(new Vector3d(x * 2.5, 0, z * 2.5));
        const int Repetitions = 20;

        Vector3d ignored;
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var watch = Stopwatch.StartNew();
        for (int r = 0; r < Repetitions; r++)
            for (int i = 0; i < tile.Count; i++)
                roadGenScript.NearestPointOnRoadBone(tile[i], duplicated, out ignored);
        watch.Stop();
        long duplicatedTicks = watch.ElapsedTicks;

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        watch.Restart();
        for (int r = 0; r < Repetitions; r++)
            for (int i = 0; i < tile.Count; i++)
                roadGenScript.NearestPointOnRoadBone(tile[i], unique, out ignored);
        watch.Stop();
        long uniqueTicks = watch.ElapsedTicks;

        Console.WriteLine("PASS: " + checks + " terrain-road dedupe assertions; exact native selection/point parity over " +
            probes.Count + " probes; synthetic 1764-vertex tile x" + Repetitions +
            " duplicated9=" + (duplicatedTicks * 1000.0 / Stopwatch.Frequency).ToString("F2") + "ms" +
            " unique=" + (uniqueTicks * 1000.0 / Stopwatch.Frequency).ToString("F2") + "ms" +
            " (isolated .NET Framework, not a game FPS claim)");
        return 0;
    }
}
