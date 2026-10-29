using System;
using System.Collections.Generic;

namespace TldMp
{
    // Call-local replacement for the repeated whole-triangle scan in
    // mainscript.GetConnectedVertices. Begin() is called at the start of each
    // terrainGenScript.GenerateTileMesh; the cache is never retained across calls.
    // The ordered neighbor list is built exactly like the native method:
    // triangle index ascending, slot 0..2, per-vertex first-occurrence dedupe.
    internal static class TerrainHoleAdjacency
    {
        [ThreadStatic] static int[] cachedTriangles;
        [ThreadStatic] static int[] offsets;
        [ThreadStatic] static int[] adjacency;
        [ThreadStatic] static int[] lengths;
        [ThreadStatic] static bool cachedValid;

        internal static void Begin()
        {
            cachedTriangles = null; offsets = null; adjacency = null; lengths = null; cachedValid = false;
        }

        internal static List<int> Get(int[] triangles, int vertex)
        {
            if (triangles == null || triangles.Length % 3 != 0 || vertex < 0)
                return mainscript.GetConnectedVertices(triangles, vertex);
            if (!ReferenceEquals(triangles, cachedTriangles)) Build(triangles);
            if (!cachedValid) return mainscript.GetConnectedVertices(triangles, vertex);
            var result = new List<int>();
            if (vertex < 0 || vertex >= lengths.Length) return result;
            int start = offsets[vertex], count = lengths[vertex];
            for (int i = 0; i < count; i++) result.Add(adjacency[start + i]);
            return result;
        }

        static void Build(int[] triangles)
        {
            int vertices = 0;
            for (int i = 0; i < triangles.Length; i++)
            {
                if (triangles[i] < 0 || triangles[i] > (1 << 20))
                {
                    cachedTriangles = triangles; cachedValid = false;
                    offsets = null; adjacency = null; lengths = null;
                    return;
                }
                if (triangles[i] >= vertices) vertices = triangles[i] + 1;
            }
            var occurrences = new int[vertices];
            for (int i = 0; i < triangles.Length; i += 3)
                for (int q = 0; q < 3; q++)
                {
                    int v = triangles[i + q];
                    if (v >= 0 && v < vertices) occurrences[v]++;
                }
            var newOffsets = new int[vertices + 1];
            for (int i = 0; i < vertices; i++) newOffsets[i + 1] = newOffsets[i] + 2 * occurrences[i];
            var newAdjacency = new int[newOffsets[vertices]];
            var newLengths = new int[vertices];
            for (int i = 0; i < triangles.Length; i += 3)
            {
                for (int selected = 0; selected < 3; selected++)
                {
                    int vi = triangles[i + selected];
                    if (vi < 0 || vi >= vertices) continue;
                    int start = newOffsets[vi], length = newLengths[vi];
                    for (int slot = 0; slot < 3; slot++)
                    {
                        int neighbour = triangles[i + slot];
                        if (neighbour == vi) continue;
                        bool present = false;
                        for (int p = 0; p < length; p++)
                            if (newAdjacency[start + p] == neighbour) { present = true; break; }
                        if (present) continue;
                        newAdjacency[start + length] = neighbour;
                        newLengths[vi] = length + 1;
                        length++;
                    }
                }
            }
            cachedTriangles = triangles; offsets = newOffsets; adjacency = newAdjacency; lengths = newLengths; cachedValid = true;
        }
    }
}
