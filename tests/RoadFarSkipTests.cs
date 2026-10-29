using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TldMp;
using UnityEngine;

internal static class RoadFarSkipTests
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

    static Vector3d V(double x, double z) { return new Vector3d(x, 0.0, z); }

    // The exact native per-cell test from roadGenScript.UpdPlaceRemove.
    static bool NativeNear(Vector3d key, IList<Vector3d> centers, float roadPlaceDist)
    {
        for (int i = 0; i < centers.Count; i++)
        {
            Vector3d d = key - centers[i];
            d.y = 0.0;
            if (d.magnitude < (double)roadPlaceDist) return true;
        }
        return false;
    }

    static int Run()
    {
        Dedupe();
        BoundIsConservative();
        Decisions();
        RoadFarSkip.Clear();
        Console.WriteLine("PASS: " + checks + " road far-skip assertions; duplicates removed in order, far proofs never contradict the native per-cell test, live or near chunks run native.");
        return 0;
    }

    static void Dedupe()
    {
        var a = V(0, 0); var b = V(1000, 0); var c = V(0, -1000);
        var list = new List<Vector3d> { a, b, a, c, b, a };
        RoadFarSkip.Dedupe(list);
        Check(list.Count == 3, "duplicates must be removed");
        Check(list[0].x == 0 && list[0].z == 0 && list[1].x == 1000 && list[2].z == -1000, "first occurrences must keep their order");

        var unique = new List<Vector3d> { c, b, a };
        RoadFarSkip.Dedupe(unique);
        Check(unique.Count == 3 && unique[0].z == -1000 && unique[2].x == 0, "a unique list must be unchanged");

        var nan = new List<Vector3d> { V(double.NaN, 0), V(double.NaN, 0) };
        RoadFarSkip.Dedupe(nan);
        Check(nan.Count == 2, "non-identical (NaN) entries must never be merged");

        var empty = new List<Vector3d>();
        RoadFarSkip.Dedupe(empty);
        RoadFarSkip.Dedupe(null);
        Check(empty.Count == 0, "empty and null lists are accepted");
    }

    static void BoundIsConservative()
    {
        var random = new System.Random(1234);
        var centers = new List<Vector3d>();
        int proven = 0;
        for (int round = 0; round < 40000; round++)
        {
            double scale = round % 3 == 0 ? 50 : (round % 3 == 1 ? 5000 : 2e6);
            double ox = (random.NextDouble() - .5) * scale * 4, oz = (random.NextDouble() - .5) * scale * 4;
            int count = 1 + random.Next(12);
            var keys = new List<Vector3d>();
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                var k = V(ox + (random.NextDouble() - .5) * scale, oz + (random.NextDouble() - .5) * scale);
                keys.Add(k);
                minX = Math.Min(minX, k.x); maxX = Math.Max(maxX, k.x); minZ = Math.Min(minZ, k.z); maxZ = Math.Max(maxZ, k.z);
            }
            centers.Clear();
            int players = 1 + random.Next(3);
            for (int i = 0; i < players; i++)
                centers.Add(new Vector3d((random.NextDouble() - .5) * scale * 4, random.NextDouble() * 300, (random.NextDouble() - .5) * scale * 4));
            float dist = (float)(random.NextDouble() * scale);
            // Boundary cases: place the radius at the exact nearest-cell distance.
            if (round % 5 == 0)
            {
                double best = double.PositiveInfinity;
                foreach (var k in keys) foreach (var c in centers) { var d = k - c; d.y = 0; best = Math.Min(best, d.magnitude); }
                dist = (float)(best - RoadFarSkip.Margin * (random.NextDouble() * 2));
            }
            if (!RoadFarSkip.AllFar(minX, maxX, minZ, maxZ, centers, dist)) continue;
            proven++;
            foreach (var k in keys)
                if (NativeNear(k, centers, dist))
                    throw new Exception("far proof contradicts native near test: key=" + k.x + "," + k.z + " dist=" + dist);
        }
        Check(proven > 1000, "the bound must prove a meaningful share of far chunks (proven=" + proven + ")");
        Check(!RoadFarSkip.AllFar(0, 0, 0, 0, new[] { V(double.NaN, 0) }, 100f), "a NaN centre must never prove far");
        Check(!RoadFarSkip.AllFar(0, 0, 0, 0, new[] { V(1e9, 0) }, float.NaN), "a NaN radius must never prove far");
        Check(RoadFarSkip.AllFar(0, 0, 0, 0, new Vector3d[0], 100f), "no genArounds means every cell is far, as natively");
    }

    static roadGenScript.dotChunkClass Chunk(params Vector3d[] cells)
    {
        var owner = new roadGenScript.roadClass(default(Vector3d), 0, default(Vector3d), 0,
            default(Vector3d), default(Vector3d), 0f, 0f, default(Vector3d), 0,
            roadGenScript.roadTypeEmu.mainRoad);
        owner.roadElements.Add(new roadGenScript.roadElementClass());
        owner.roadElements.Add(new roadGenScript.roadElementClass());
        var chunk = new roadGenScript.dotChunkClass();
        chunk.roads.Add(owner);
        foreach (var cell in cells) chunk.innerChunks.Add(cell, new roadGenScript.innerChunkClass());
        return chunk;
    }

    static void Decisions()
    {
        var far = new[] { V(0, 0) };
        var near = new[] { V(5000, 5000) };
        var chunk = Chunk(V(5000, 5000), V(5100, 5000), V(5000, 5100));

        Check(!RoadFarSkip.Decide(chunk, near, 500f), "a chunk with a near cell must run native");
        Check(RoadFarSkip.Decide(chunk, far, 500f), "an all-far chunk with no live element must be skipped");
        Check(RoadFarSkip.Decide(chunk, far, 500f), "the cached empty chunk must stay skippable");
        Check(!RoadFarSkip.Decide(chunk, near, 500f), "a player approaching must return the chunk to native");
        Check(RoadFarSkip.Decide(chunk, far, 500f), "after native ran near, emptiness is re-proven before skipping");

        chunk.innerChunks.Add(V(300, 0), new roadGenScript.innerChunkClass());
        Check(!RoadFarSkip.Decide(chunk, far, 500f), "a new inner cell must recompute the bounds");

        var nullElement = Chunk(V(9000, 9000));
        nullElement.roads[0].roadElements[1] = null;
        Check(!RoadFarSkip.Decide(nullElement, far, 500f), "a null element entry must fall back to native");

        var nullRoad = Chunk(V(9000, 9000));
        nullRoad.roads.Add(null);
        Check(!RoadFarSkip.Decide(nullRoad, far, 500f), "a null road must fall back to native");

        var boundary = Chunk(V(500, 0));
        Check(!RoadFarSkip.Decide(boundary, far, 500f), "a cell exactly at roadPlaceDist stays native (margin)");
        Check(!RoadFarSkip.Decide(Chunk(V(500.4, 0)), far, 500f), "a cell inside the margin stays native");
        Check(RoadFarSkip.Decide(Chunk(V(500.6, 0)), far, 500f), "a cell beyond the margin is skippable");

        var noCells = Chunk();
        Check(RoadFarSkip.Decide(noCells, near, 500f), "a chunk without inner cells has no native work when empty");

        var nonFinite = Chunk(V(double.PositiveInfinity, 0));
        Check(!RoadFarSkip.Decide(nonFinite, far, 500f), "a non-finite cell must never be proven far");

        LiveElement(far);
    }

    // A live Unity object needs the engine; emulate one by giving an
    // uninitialised GameObject a non-zero native pointer, which is what Unity's
    // null comparison inspects in the player.
    static void LiveElement(Vector3d[] far)
    {
        var field = typeof(UnityEngine.Object).GetField("m_CachedPtr", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) { Console.WriteLine("NOTE: m_CachedPtr not found; live-element emulation skipped."); return; }
        var live = (GameObject)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(GameObject));
        field.SetValue(live, new IntPtr(1));
        bool alive;
        try { alive = live != null; }
        catch (Exception e) { Console.WriteLine("NOTE: live-element emulation unavailable offline (" + e.GetType().Name + ")."); return; }
        if (!alive) { Console.WriteLine("NOTE: offline Unity treats emulated objects as destroyed; live-element emulation skipped."); return; }
        var chunk = Chunk(V(9000, 9000));
        chunk.roads[0].roadElements[0].obj = live;
        Check(!RoadFarSkip.Decide(chunk, far, 500f), "a far chunk with a live element must run native so it is destroyed");
        chunk.roads[0].roadElements[0].obj = null;
        Check(RoadFarSkip.Decide(chunk, far, 500f), "once the element is gone the chunk is skippable");
    }
}
