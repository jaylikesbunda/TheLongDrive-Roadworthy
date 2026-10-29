using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using TldMp;
using UnityEngine;

internal static class RoadChunkPolicyTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }

    static roadGenScript MakeRoad(float step, int seed, out Dictionary<Vector3d, roadGenScript.dotChunkClass> chunks, out List<Vector3d> needed)
    {
        var road = (roadGenScript)FormatterServices.GetUninitializedObject(typeof(roadGenScript));
        var map = (mapSettingScript)FormatterServices.GetUninitializedObject(typeof(mapSettingScript));
        map.seed = seed;
        road.mapSetting = map;
        road.chunkSize = step;
        chunks = new Dictionary<Vector3d, roadGenScript.dotChunkClass>();
        needed = new List<Vector3d>();
        road.chunks = chunks;
        road.chunksNeeded = needed;
        return road;
    }

    static roadGenScript.dotChunkClass Cell(roadGenScript road, Dictionary<Vector3d, roadGenScript.dotChunkClass> chunks, Vector3d position)
    {
        var cell = new roadGenScript.dotChunkClass();
        cell.seed = position.GetHashCode() + road.mapSetting.seed;
        chunks[position] = cell;
        return cell;
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
        const float Step = 100f;
        const int Seed = 12345;
        Dictionary<Vector3d, roadGenScript.dotChunkClass> chunks;
        List<Vector3d> needed;
        var road = MakeRoad(Step, Seed, out chunks, out needed);
        var center = new Vector3d(0, 0, 0);
        var border = new Vector3d(Step, 0, 0);
        var corner = new Vector3d(Step, 0, Step);
        var outside = new Vector3d(Step * 3, 0, 0);
        needed.Add(center);
        Cell(road, chunks, center);
        Cell(road, chunks, border);
        Cell(road, chunks, corner);
        Cell(road, chunks, outside);

        Check(RoadChunkPolicy.Keep(road, needed, center), "a needed cell must be kept");
        Check(RoadChunkPolicy.Keep(road, needed, border), "an empty border cell must be kept");
        Check(RoadChunkPolicy.Keep(road, needed, corner), "an empty diagonal border cell must be kept");
        Check(!RoadChunkPolicy.Keep(road, needed, outside), "a cell outside the 5x5 ring must be removed");

        var withRoad = Cell(road, chunks, new Vector3d(0, 0, Step));
        withRoad.roads.Add((roadGenScript.roadClass)FormatterServices.GetUninitializedObject(typeof(roadGenScript.roadClass)));
        Check(!RoadChunkPolicy.Keep(road, needed, new Vector3d(0, 0, Step)), "a border cell with road data must take the native path");

        var withInner = Cell(road, chunks, new Vector3d(-Step, 0, 0));
        withInner.innerChunks[new Vector3d(0, 0, 0)] = new roadGenScript.innerChunkClass();
        Check(!RoadChunkPolicy.Keep(road, needed, new Vector3d(-Step, 0, 0)), "a border cell with inner chunks must take the native path");

        var flagged = Cell(road, chunks, new Vector3d(0, 0, -Step));
        flagged.insideConnected = true;
        Check(!RoadChunkPolicy.Keep(road, needed, new Vector3d(0, 0, -Step)), "a border cell with a connection flag must take the native path");

        var stale = Cell(road, chunks, new Vector3d(-Step, 0, -Step));
        stale.seed++;
        Check(!RoadChunkPolicy.Keep(road, needed, new Vector3d(-Step, 0, -Step)), "a border cell with a stale seed must take the native path");

        Check(!RoadChunkPolicy.Keep(null, needed, center), "null road must be rejected");
        Check(!RoadChunkPolicy.Keep(road, null, center), "null needed list must be rejected");
        Dictionary<Vector3d, roadGenScript.dotChunkClass> zeroChunks;
        List<Vector3d> zeroNeeded;
        var zero = MakeRoad(0f, Seed, out zeroChunks, out zeroNeeded);
        zeroNeeded.Add(center);
        Cell(zero, zeroChunks, center);
        Check(!RoadChunkPolicy.Keep(zero, zeroNeeded, center), "zero chunk size must be rejected");

        // Two-cell distance is outside the ring even on the same axis.
        var two = new Vector3d(Step * 2, 0, 0);
        Cell(road, chunks, two);
        Check(!RoadChunkPolicy.Keep(road, needed, two), "a two-cell neighbour must be removed");

        Console.WriteLine("PASS: " + checks + " road border-chunk policy assertions; empty border cells retained, data/flag/stale-seed cells left to the native path.");
        return 0;
    }
}
