using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TldMp;
using UnityEngine;

internal static class RoadLiveCacheTests
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
        Check(RoadLiveCache.NeedsRecount(false, 0, 0), "an unknown chunk must be recounted");
        Check(!RoadLiveCache.NeedsRecount(true, 2, 2), "an unchanged cached chunk must be reused");
        Check(RoadLiveCache.NeedsRecount(true, 2, 3), "a changed element list must be recounted");

        var owner = new roadGenScript.roadClass(default(Vector3d), 0, default(Vector3d), 0,
            default(Vector3d), default(Vector3d), 0f, 0f, default(Vector3d), 0,
            roadGenScript.roadTypeEmu.mainRoad);
        owner.roadElements.Add(new roadGenScript.roadElementClass());
        owner.roadElements.Add(new roadGenScript.roadElementClass());
        var chunk = new roadGenScript.dotChunkClass();
        chunk.roads.Add(owner);
        var road = new roadGenScript.roadChunk();
        road.roadId = 0;
        road.roadElements.Add(0);
        road.roadElements.Add(1);

        Check(!RoadLiveCache.MayHaveLive(chunk, road), "a far chunk with only null objects must be skipped");
        Check(!RoadLiveCache.MayHaveLive(chunk, road), "the cached zero must stay zero");
        RoadLiveCache.Destroyed(road);
        Check(!RoadLiveCache.MayHaveLive(chunk, road), "destroying from a zero cache must stay zero");
        RoadLiveCache.Dirty(road);
        Check(!RoadLiveCache.MayHaveLive(chunk, road), "a dirty zero chunk must recount as zero");

        road.roadId = 9;
        Check(RoadLiveCache.MayHaveLive(chunk, road), "an out-of-range road id must fall back to native");
        road.roadId = 0;
        road.roadElements.Add(9);
        Check(RoadLiveCache.MayHaveLive(chunk, road), "an out-of-range element index must fall back to native");
        road.roadElements.RemoveAt(2);

        owner.roadElements[0] = null;
        RoadLiveCache.Dirty(road);
        Check(RoadLiveCache.MayHaveLive(chunk, road), "a null element entry must fall back to native");

        RoadLiveCache.Clear();
        Console.WriteLine("PASS: " + checks + " road live-cache assertions; only proven-empty chunks are skipped and every uncertain case falls back to native.");
        return 0;
    }
}
