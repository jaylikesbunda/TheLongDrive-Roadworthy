using System;
using System.Collections.Generic;

namespace TldMp
{
    // Call-local duplicate removal for the road list that mapSettingScript.RoadsNear
    // returns to terrainGenScript.GenerateTileMesh. RoadsNear appends the same
    // roadClass once per inner chunk that registers one of its elements, so one
    // physical road can appear several times in the list.
    //
    // Equivalence: roadGenScript.NearestPointOnRoadBone selects with a strict
    // '<' comparison and therefore keeps the first entry on an exact distance
    // tie. Later reference-equal entries can never replace the first one, and
    // they resolve to the same roadClass object, so removing them leaves the
    // selected road, its point, the list order of all remaining roads, and every
    // floating-point result unchanged. The list is freshly allocated inside
    // RoadsNear for this call and is not retained elsewhere, so in-place
    // compaction is safe. No cross-frame cache and no invalidation is involved.
    internal static class TerrainRoadSelection
    {
        // Guard against a pathological list: above this size the quadratic scan
        // is not worth its bounded worst case, so the native list is returned.
        const int MaxDedupeCount = 1024;

        internal static long Raw, Unique, Calls, MaxRaw;

        internal static List<roadGenScript.roadClass> Dedupe(List<roadGenScript.roadClass> roads)
        {
            if (roads == null || roads.Count < 2) return roads;
            int count = roads.Count;
            if (count > MaxDedupeCount) return roads;
            Calls++;
            Raw += count;
            if (count > MaxRaw) MaxRaw = count;
            int write = 1;
            for (int read = 1; read < count; read++)
            {
                roadGenScript.roadClass road = roads[read];
                bool duplicate = false;
                for (int previous = 0; previous < write; previous++)
                {
                    if (ReferenceEquals(roads[previous], road)) { duplicate = true; break; }
                }
                if (!duplicate) roads[write++] = road;
            }
            if (write != count) roads.RemoveRange(write, count - write);
            Unique += write;
            return roads;
        }

        internal static string ReportAndReset()
        {
            if (Calls == 0) return "terrainRoads{none}";
            string text = "terrainRoads{raw=" + Raw + ";unique=" + Unique + ";calls=" + Calls + ";maxRaw=" + MaxRaw + "}";
            Raw = Unique = Calls = MaxRaw = 0;
            return text;
        }
    }
}
