using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using TldMp;
using UnityEngine;

// Parity gate for the distant terrain worker. The stock GenerateTileMesh runs on
// the real Assembly-CSharp; TerrainTileMesh.Generate (the worker body) must give
// bit-identical v, t, u, n and c arrays. Args: managed dir, Assembly-CSharp path,
// and "patched" for the shipped build. Concurrent jobs need the patched build,
// because the stock Noises.Noise accumulators are shared until LocalizeNoise.
public static class TerrainWorkerParityTests
{
    const int Resolution = 33;
    const float TileSize = 50f;
    const int Concurrent = 8;
    static int sequentialChecks, concurrentChecks, mismatches;
    static MethodInfo vanillaGenerate;

    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            string name = new AssemblyName(e.Name).Name;
            string p = name == "Assembly-CSharp" ? args[1] : Path.Combine(args[0], name + ".dll");
            return File.Exists(p) ? Assembly.LoadFrom(p) : null;
        };
        try
        {
            Assembly.LoadFrom(Path.GetFullPath(args[1]));
            string mode = args.Length > 2 ? args[2] : "baseline";
            return Run(mode == "patched", args[1]);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int Run(bool patched, string selected)
    {
        if (!String.Equals(Path.GetFullPath(typeof(terrainGenSettings).Assembly.Location), Path.GetFullPath(selected), StringComparison.OrdinalIgnoreCase))
            throw new Exception("Wrong terrain assembly loaded.");
        // The game sets this on map load (mapSettingScript). Without it the shared
        // permutation table has no random source and noise cannot be evaluated here.
        NoiseS3D.seed = 1234;
        var setting = MakeSettings();
        var gen = MakeGenerator(setting);
        var snapshot = TerrainWorker.Capture(gen);
        var coords = MakeCoords();

        // Vanilla output is copied at once: the stock method reuses its member arrays.
        var expected = new terrainGenScript.smesh[coords.Count];
        for (int i = 0; i < coords.Count; i++) expected[i] = Vanilla(gen, coords[i]);

        for (int i = 0; i < coords.Count; i++)
        {
            var got = TerrainTileMesh.Generate(snapshot, coords[i], null, null);
            mismatches += Diff(expected[i], got, "sequential " + i);
            sequentialChecks++;
        }

        if (patched)
        {
            for (int i = 0; i < coords.Count; i++)
            {
                var results = new terrainGenScript.smesh[Concurrent];
                int index = i;
                Parallel.For(0, Concurrent, new ParallelOptions { MaxDegreeOfParallelism = Concurrent }, delegate(int k)
                {
                    results[k] = TerrainTileMesh.Generate(snapshot, coords[index], null, null);
                });
                for (int k = 0; k < Concurrent; k++)
                {
                    mismatches += Diff(expected[i], results[k], "concurrent " + i + "/" + k);
                    concurrentChecks++;
                }
            }
        }

        if (mismatches != 0) throw new Exception("FAIL: terrain worker mismatched " + mismatches + " elements.");
        Console.WriteLine("PASS: terrain worker parity (" + (patched ? "patched" : "baseline") + "): " +
            coords.Count + " tile coords, " + sequentialChecks + " sequential worker-vs-vanilla comparisons, " +
            (patched ? concurrentChecks + " concurrent worker jobs (" + Concurrent + " per coord)" : "concurrent jobs skipped (needs the patched build)") +
            "; 0 mismatched v/t/u/n/c elements. Roads and POIs are not covered: no offline road or POI fixture.");
        return 0;
    }

    static terrainGenScript.smesh Vanilla(terrainGenScript gen, Vector3d coord)
    {
        if (vanillaGenerate == null)
            vanillaGenerate = typeof(terrainGenScript).GetMethod("GenerateTileMesh", BindingFlags.Instance | BindingFlags.NonPublic);
        if (vanillaGenerate == null) throw new MissingMethodException("terrainGenScript.GenerateTileMesh");
        var mesh = (terrainGenScript.smesh)vanillaGenerate.Invoke(gen, new object[] { coord });
        return new terrainGenScript.smesh
        {
            v = (Vector3[])mesh.v.Clone(),
            t = (int[])mesh.t.Clone(),
            u = (Vector2[])mesh.u.Clone(),
            n = (Vector3[])mesh.n.Clone(),
            c = (Color[])mesh.c.Clone()
        };
    }

    static List<Vector3d> MakeCoords()
    {
        var coords = new List<Vector3d>();
        for (int k = 0; k < 40; k++)
        {
            // Negative and positive grid coordinates near the origin.
            double x = ((k * 7919 + 17) % 2001 - 1000) * (double)TileSize + (k % 3) * 13.37;
            double z = ((k * 104729 + 5) % 2001 - 1000) * (double)TileSize - (k % 5) * 7.25;
            coords.Add(new Vector3d(x, 0.0, z));
        }
        for (int k = 0; k < 20; k++)
        {
            // Far coordinates, roughly 20 km out in both directions.
            coords.Add(new Vector3d(20000.0 + k * 97.3, 0.0, -20000.0 - k * 131.9));
        }
        return coords;
    }

    static terrainGenScript MakeGenerator(terrainGenSettings setting)
    {
        var gen = (terrainGenScript)FormatterServices.GetUninitializedObject(typeof(terrainGenScript));
        gen.isDistantTerrain = true;
        gen.hasCollider = false;
        gen.roadAffectsThis = false;
        gen.poiAffectsThis = false;
        gen.mapSetting = (mapSettingScript)FormatterServices.GetUninitializedObject(typeof(mapSettingScript));
        gen.setting = setting;
        gen.resolution = Resolution;
        gen.tileSize = TileSize;
        PrepareLikeFStart(gen);
        return gen;
    }

    // The same buffer construction as terrainGenScript.FStart, on the same fields.
    static void PrepareLikeFStart(terrainGenScript gen)
    {
        int resolution = Resolution;
        float tileSize = TileSize;
        float vertexDist = tileSize / (float)(resolution - 1);
        Vector3 offset = new Vector3(-1f, 0f, -1f) * tileSize * 0.5f;
        int vertexNum = resolution * resolution;
        int triangleNum = (resolution - 1) * (resolution - 1) * 2 * 3;
        int vertexNum2 = (resolution + 2) * (resolution + 2);
        int triangleNum2 = (resolution + 1) * (resolution + 1) * 2 * 3;
        var u = new Vector2[vertexNum];
        var t = new int[triangleNum];
        var t2 = new int[triangleNum2];
        int d = 0, d2 = 0;
        for (int i = -1; i < resolution + 1; i++)
        {
            for (int j = -1; j < resolution + 1; j++)
            {
                int i2 = j + 1 + (i + 1) * (resolution + 2);
                if (j < resolution && i < resolution)
                {
                    t2[d2] = i2;
                    t2[d2 + 1] = i2 + (resolution + 2);
                    t2[d2 + 2] = i2 + (resolution + 2) + 1;
                    t2[d2 + 3] = i2 + (resolution + 2) + 1;
                    t2[d2 + 4] = i2 + 1;
                    t2[d2 + 5] = i2;
                    d2 += 6;
                }
                if (j >= 0 && j < resolution && i >= 0 && i < resolution)
                {
                    int idx = j + i * resolution;
                    u[idx] = new Vector2((float)j / (float)(resolution - 1), (float)i / (float)(resolution - 1));
                    if (j < resolution - 1 && i < resolution - 1)
                    {
                        t[d] = idx;
                        t[d + 1] = idx + resolution;
                        t[d + 2] = idx + resolution + 1;
                        t[d + 3] = idx + resolution + 1;
                        t[d + 4] = idx + 1;
                        t[d + 5] = idx;
                        d += 6;
                    }
                }
            }
        }
        Set(gen, "vertexDist", vertexDist);
        Set(gen, "offset", offset);
        Set(gen, "vertexNum", vertexNum);
        Set(gen, "triangleNum", triangleNum);
        Set(gen, "vertexNum2", vertexNum2);
        Set(gen, "triangleNum2", triangleNum2);
        Set(gen, "v", new Vector3[vertexNum]);
        Set(gen, "u", u);
        Set(gen, "n", new Vector3[vertexNum]);
        Set(gen, "c", new Color[vertexNum]);
        Set(gen, "t", t);
        Set(gen, "v2", new Vector3[vertexNum2]);
        Set(gen, "n2", new Vector3[vertexNum2]);
        Set(gen, "t2", t2);
    }

    static void Set(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field == null) throw new MissingFieldException(name);
        field.SetValue(target, value);
    }

    static terrainGenSettings MakeSettings()
    {
        var s = (terrainGenSettings)FormatterServices.GetUninitializedObject(typeof(terrainGenSettings));
        // Height and Biome read mapSetting.imageMapLoad; null selects the procedural path.
        s.mapSetting = (mapSettingScript)FormatterServices.GetUninitializedObject(typeof(mapSettingScript));
        s.snowMinHeight = 2.0;
        s.snowMaxHeight = 40.0;
        s.snowHeightNoise = MakeNoise(101, 0.02f, 3f, 3, 2.0f, 0.5f, 0.2f, 0.3f, terrainGenSettings.NoiseFunction.def, false);
        s.terrainNoises = new terrainGenSettings.Noises[2];
        s.terrainNoises[0] = MakeNoises(11, 4);
        s.terrainNoises[1] = MakeNoises(23, 3);
        s.biomeNoises = new terrainGenSettings.Noises[4];
        for (int k = 0; k < s.biomeNoises.Length; k++) s.biomeNoises[k] = MakeNoises(40 + k * 7, 2);
        return s;
    }

    static terrainGenSettings.Noises MakeNoises(int seed, int count)
    {
        var n = new terrainGenSettings.Noises();
        // A nonzero deform frequency makes Noises.Noise write its r/d/s accumulators.
        n.maskNoise = MakeNoise(seed + 1, 0.004f, 1f, 3, 2.0f, 0.5f, 0.3f, 0.2f, terrainGenSettings.NoiseFunction.def, false);
        n.deformNoiseDir = MakeNoise(seed + 2, 0.01f, 1f, 2, 2.0f, 0.5f, 0.5f, 0.2f, terrainGenSettings.NoiseFunction.sin, false);
        n.deformNoiseStr = MakeNoise(seed + 3, 0.02f, 4f, 2, 2.0f, 0.5f, 0.5f, 0.2f, terrainGenSettings.NoiseFunction.def, true);
        n.noises = new terrainGenSettings.noiseSetting[count];
        for (int i = 0; i < count; i++)
            n.noises[i] = MakeNoise(seed + 10 + i, 0.006f * (i + 1), 25f / (i + 1), 4, 2.0f, 0.5f, 0.6f, 0.25f,
                i == 0 ? terrainGenSettings.NoiseFunction.invSqr : terrainGenSettings.NoiseFunction.def, i == 1);
        return n;
    }

    static terrainGenSettings.noiseSetting MakeNoise(int seed, float freq, float ampl, int octaves, float lacunarity,
        float persistence, float coverage, float transition, terrainGenSettings.NoiseFunction nf, bool inverse)
    {
        object boxed = new terrainGenSettings.noiseSetting();
        Set(boxed, "freq", freq);
        Set(boxed, "ampl", ampl);
        Set(boxed, "octaves", octaves);
        Set(boxed, "lacunarity", lacunarity);
        Set(boxed, "persistence", persistence);
        Set(boxed, "coverage", coverage);
        Set(boxed, "transition", transition);
        Set(boxed, "nf", nf);
        Set(boxed, "inverse", inverse);
        typeof(terrainGenSettings.noiseSetting).GetMethod("FStart").Invoke(boxed, new object[] { seed });
        return (terrainGenSettings.noiseSetting)boxed;
    }

    static int Diff(terrainGenScript.smesh a, terrainGenScript.smesh b, string label)
    {
        int bad = 0;
        if (a.v.Length != b.v.Length || a.n.Length != b.n.Length || a.c.Length != b.c.Length ||
            a.t.Length != b.t.Length || a.u.Length != b.u.Length)
            return 1;
        for (int i = 0; i < a.v.Length; i++) if (!SameVector(a.v[i], b.v[i])) bad++;
        for (int i = 0; i < a.n.Length; i++) if (!SameVector(a.n[i], b.n[i])) bad++;
        for (int i = 0; i < a.c.Length; i++) if (!SameColor(a.c[i], b.c[i])) bad++;
        for (int i = 0; i < a.u.Length; i++) if (!(a.u[i].x == b.u[i].x && a.u[i].y == b.u[i].y)) bad++;
        for (int i = 0; i < a.t.Length; i++) if (a.t[i] != b.t[i]) bad++;
        if (bad != 0 && bad < 5) Console.WriteLine("mismatch at " + label + ": " + bad + " elements");
        return bad;
    }

    static bool SameVector(Vector3 a, Vector3 b)
    {
        return Same(a.x, b.x) && Same(a.y, b.y) && Same(a.z, b.z);
    }

    static bool SameColor(Color a, Color b)
    {
        return Same(a.r, b.r) && Same(a.g, b.g) && Same(a.b, b.b) && Same(a.a, b.a);
    }

    // Exact float equality, and the same bit pattern (so -0 and +0 differ).
    static bool Same(float a, float b)
    {
        return a == b && BitConverter.ToInt32(BitConverter.GetBytes(a), 0) == BitConverter.ToInt32(BitConverter.GetBytes(b), 0);
    }
}
